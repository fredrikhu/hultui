using System.Text;
using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

public class ApiMapTests
{
    private static HttpResponseData Response(int status, string body = "{}") => new(
        status, "OK", [("Content-Type", "application/json")], "application/json",
        new ResponseBody(BodyKind.Text, body, [], body.Length, false, null),
        new PhaseTimings(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
            TimeSpan.Zero, false, "HTTP/1.1", null),
        "https://x");

    // ── Recording traffic ────────────────────────────────────────────────

    [Fact]
    public void AnUnknownSendBecomesAnObservedEndpoint()
    {
        var map = ApiMap.Empty("https://api.x").Record("GET", "https://api.x/v1/balance", 200, """{"amount":10}""");

        var endpoint = map.Endpoints.Single();
        Assert.Equal(Confidence.Observed, endpoint.Confidence);
        Assert.Equal(("GET", "/v1/balance"), (endpoint.Method, endpoint.Path));
        Assert.Equal(1, endpoint.SeenCount);
        Assert.Equal(1, endpoint.Codes[200]);
        Assert.Contains("amount: number", endpoint.InferredShape);
    }

    [Fact]
    public void TwoSendsToTheSameResourceAreOneTemplatedEndpoint()
    {
        // The whole point of templating: ch_1 and ch_2 are one endpoint.
        var map = ApiMap.Empty("https://api.x")
            .Record("GET", "https://api.x/v1/charges/ch_9f2Kp", 200, null)
            .Record("GET", "https://api.x/v1/charges/ch_L8xKz", 404, null);

        var endpoint = map.Endpoints.Single();
        Assert.Equal("/v1/charges/{id}", endpoint.Path);
        Assert.Equal(2, endpoint.SeenCount);
        Assert.Equal([200, 404], endpoint.Codes.Keys.Order());
    }

    [Fact]
    public void ACollectionPathIsNotMistakenForAnIdentifier()
    {
        var map = ApiMap.Empty("https://api.x")
            .Record("GET", "https://api.x/v1/charges", 200, null)
            .Record("POST", "https://api.x/v1/charges", 201, null);

        Assert.Equal(["/v1/charges", "/v1/charges"], map.Endpoints.Select(e => e.Path));
        Assert.Equal(2, map.Endpoints.Count);   // different methods, same path
    }

    [Fact]
    public void TrafficAgainstADeclaredEndpointKeepsItDeclared()
    {
        var declared = new ApiMap("https://api.x", [
            new Endpoint("GET", "/v1/charges/{id}", Confidence.Declared, "charges")]);

        var map = declared.Record("GET", "https://api.x/v1/charges/ch_1", 200, null);

        var endpoint = map.Endpoints.Single();
        Assert.Equal(Confidence.Declared, endpoint.Confidence);
        Assert.Equal(1, endpoint.SeenCount);
    }

    [Fact]
    public void ADeclaredTemplateMatchesEvenWhenTheValueLooksNothingLikeAnId()
    {
        // Templating alone can't do this: "simple-name" is not identifier-shaped,
        // so only matching against the declared template folds it in. Without
        // that path the map grows a duplicate observed endpoint per slug.
        var declared = new ApiMap("https://api.x", [
            new Endpoint("GET", "/v1/products/{slug}", Confidence.Declared, "products")]);

        var map = declared.Record("GET", "https://api.x/v1/products/simple-name", 200, null);

        var endpoint = Assert.Single(map.Endpoints);
        Assert.Equal(Confidence.Declared, endpoint.Confidence);
        Assert.Equal(1, endpoint.SeenCount);
    }

    [Fact]
    public void ALiteralSegmentThatLooksLikeAnIdIsLeftAlone()
    {
        // "1099" is all digits, but another endpoint uses it literally — a tax
        // form, not a row id. Templating it would merge unrelated endpoints.
        var known = new[] { "/v1/forms/1099" };

        Assert.Equal("/v1/forms/1099", ApiMap.Templatize("/v1/forms/1099", known));
        // …while a genuine id in the same position still templatizes.
        Assert.Equal("/v1/forms/{id}", ApiMap.Templatize("/v1/forms/8821", known));
    }

    [Theory]
    [InlineData("/v1/charges/ch_9f2", "/v1/charges/{id}")]
    [InlineData("/v1/charges/42", "/v1/charges/{id}")]
    [InlineData("/users/8f3d1a2b-0000-4c1e-9a2b-1234567890ab", "/users/{id}")]
    [InlineData("/v1/charges", "/v1/charges")]
    [InlineData("/health", "/health")]
    public void IdentifierSegmentsTemplatize(string concrete, string expected)
    {
        Assert.Equal(expected, ApiMap.Templatize(concrete, []));
    }

    [Theory]
    [InlineData("/v1/charges/{id}", "charges")]
    [InlineData("/v2/refunds", "refunds")]
    [InlineData("/internal/health", "internal")]
    public void GroupsComeFromTheFirstMeaningfulSegment(string path, string group)
    {
        Assert.Equal(group, ApiMap.GroupOf(path));
    }

    [Fact]
    public void ANonObjectBodyInfersNoShape()
    {
        Assert.Null(ApiMap.InferShape("[1,2,3]"));
        Assert.Null(ApiMap.InferShape("not json"));
        Assert.Null(ApiMap.InferShape(null));
    }

    // ── Spec parsing ─────────────────────────────────────────────────────

    private const string Spec = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Payments", "version": "2.4.0" },
          "paths": {
            "/v1/charges": {
              "get": { "tags": ["charges"], "summary": "List charges" },
              "post": { "tags": ["charges"], "summary": "Create a charge",
                "responses": { "201": { "content": { "application/json": { "example": { "id": "ch_1" } } } } } }
            },
            "/v1/charges/{id}": {
              "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } } ],
              "get": { "tags": ["charges"], "summary": "Retrieve a charge",
                "parameters": [ { "name": "expand", "in": "query", "schema": { "type": "array" } } ] },
              "delete": { "tags": ["charges"], "deprecated": true }
            }
          }
        }
        """;

    [Fact]
    public void ASpecBecomesDeclaredEndpoints()
    {
        var map = OpenApi.Parse(Spec, "https://api.x", "openapi.json", out var failure);

        Assert.Null(failure);
        Assert.Equal("2.4.0", map!.SpecVersion);
        Assert.Equal(4, map.DeclaredCount);
        Assert.Equal(0, map.ObservedCount);
        Assert.All(map.Endpoints, e => Assert.Equal("charges", e.Group));
    }

    [Fact]
    public void PathLevelParametersApplyToEachMethod()
    {
        var map = OpenApi.Parse(Spec, "https://api.x", "spec", out _)!;
        var get = map.Endpoints.Single(e => e.Method == "GET" && e.Path == "/v1/charges/{id}");

        Assert.Equal(["id", "expand"], get.Params.Select(p => p.Name));
        Assert.True(get.Params[0].Required);
        Assert.Equal("path", get.Params[0].In);
    }

    [Fact]
    public void DeprecationAndExamplesAreRead()
    {
        var map = OpenApi.Parse(Spec, "https://api.x", "spec", out _)!;

        Assert.True(map.Endpoints.Single(e => e.Method == "DELETE").Deprecated);
        Assert.Contains("\"id\"", map.Endpoints.Single(e => e.Method == "POST").ExampleResponse);
    }

    [Fact]
    public void AParseFailureNamesTheLineAndColumn()
    {
        var map = OpenApi.Parse("{\n  \"paths\": {\n    oops\n  }\n}", "https://api.x", "openapi.json", out var failure);

        Assert.Null(map);
        Assert.Equal(3, failure!.Line);
        Assert.True(failure.Column > 0);
    }

    [Fact]
    public void ADocumentWithoutPathsIsNotASpec()
    {
        Assert.Null(OpenApi.Parse("""{"hello":"world"}""", "https://api.x", "x.json", out var failure));
        Assert.Contains("no \"paths\"", failure!.Message);
    }

    // ── Probing ──────────────────────────────────────────────────────────

    [Theory]
    // A Swagger UI page: the base is before /swagger/.
    [InlineData("https://h/api/swagger/index.html", "https://h/api")]
    // A spec at a well-known path: the base is before it.
    [InlineData("https://h/api/swagger/v1/swagger.json", "https://h/api")]
    [InlineData("https://h/api/openapi.json", "https://h/api")]
    // Anything else is the base itself.
    [InlineData("https://h/api/", "https://h/api")]
    public void APastedUrlIsTriedFirstAndMapsItsBase(string pasted, string expectedBase)
    {
        var (baseUrl, urls) = OpenApi.ProbeCandidates(pasted);

        Assert.Equal(expectedBase, baseUrl);
        Assert.Equal(pasted.TrimEnd('/'), urls[0]);
        Assert.Contains("https://h/api/swagger/v1/swagger.json", urls);
        Assert.Equal(urls.Count, urls.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ASpecPastTheViewerCapIsReadInFull()
    {
        var spill = Path.GetTempFileName();
        try
        {
            File.WriteAllText(spill, """{"paths":{"/a":{"get":{}}}}""");
            var body = new ResponseBody(BodyKind.Text, """{"paths":{"/a""", [], 27, Truncated: true, spill);

            Assert.NotNull(OpenApi.Parse(OpenApi.FullText(body), "https://h", "x.json", out _));
        }
        finally { File.Delete(spill); }
    }

    // ── Parsed from bytes ────────────────────────────────────────────────
    //
    // A spec file, and a body spilled past the viewer cap, are parsed from their UTF-8 bytes: read
    // as text they cost a string and a rented UTF-8 copy that stayed in the shared pool (8 MB after
    // a spec of 2,000 operations). The oracle is the text path they replaced.

    private static readonly Dictionary<string, Func<string>> Specs = new()
    {
        ["the spec above"] = () => Spec,
        ["60 endpoints, as the walks' spec60.json"] = () => DiscoveryViewTests.WalkSpec(30),
        ["600 endpoints, as the walks' spec600.json"] = () => DiscoveryViewTests.WalkSpec(300),
        ["500 operations with examples and parameters"] = () => BigSpec(100),
        ["text past ASCII"] = () => """{"info":{"version":"1.0 ü"},"paths":{"/café/{id}":{"get":{"tags":["Café ☕"],"summary":"Hämta 東 😀"}}}}""",
        ["a broken spec"] = () => "{\n  \"paths\": {\n    \"/a\": { \"get\": { \"summary\": \"x\" } },\n    oops\n  }\n}",
        ["not a spec"] = () => """{"hello":"world"}""",
        // The bytes are read a path item at a time: what a document of the whole text decides.
        ["broken inside a path item"] = () => "{\"paths\":{\"/a\":{\"get\":{}},\n \"/b\":{\"get\":{\"summary\":\"x\",}}}}",
        ["cut short"] = () => """{"paths":{"/a":{"get":{"summary":"x"}}""",
        ["text after the root"] = () => """{"paths":{"/a":{"get":{}}}} x""",
        ["broken in info"] = () => """{"info":{"version":1.2.3},"paths":{"/a":{"get":{}}}}""",
        ["broken in a value nobody reads"] = () => """{"servers":[{"url":}],"paths":{"/a":{"get":{}}}}""",
        ["paths and info twice"] = () => """{"info":{"version":"1"},"paths":{"/a":{"get":{}}},"info":{"version":"2"},"paths":{"/b":{"post":{}},"/b":{"put":{}}}}""",
        ["escaped names"] = () => """{"paths":{"/a/{id}":{"get":{"summary":"sé","tags":["T"]}}},"info":{"version":"1"}}""",
        ["path items that are not objects"] = () => """{"paths":{"/a":[1,{"get":{}}],"/b":"x","/c":null,"/d":{"get":{}}}}""",
        ["a root that is an array"] = () => """[{"paths":{"/a":{"get":{}}}}]""",
        ["nothing at all"] = () => "  \n ",
        ["nested too deep"] = () => "{\"x\":" + new string('[', 70) + new string(']', 70) + ",\"paths\":{}}",
    };

    public static TheoryData<string> SpecFiles()
    {
        var data = new TheoryData<string>();
        foreach (var what in Specs.Keys) data.Add(what);
        return data;
    }

    [Theory]
    [MemberData(nameof(SpecFiles))]
    public void ASpecFileParsedFromBytes_IsTheMapItsTextGives(string what)
    {
        string spec = Specs[what]();
        var path = Path.GetTempFileName();
        try
        {
            // As saved: plain UTF-8, with a byte order mark, as UTF-16; and with a byte that is not UTF-8.
            foreach (var (form, bytes) in new (string, byte[])[]
            {
                ("UTF-8", new UTF8Encoding(false).GetBytes(spec)),
                ("UTF-8 with a BOM", new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(spec)).ToArray()),
                ("UTF-16 with a BOM", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(spec)).ToArray()),
                ("Latin-1 in a string", Encoding.Latin1.GetBytes(spec.Replace("\"paths\"", "\"x\":\"résumé\",\"paths\"", StringComparison.Ordinal))),
            })
            {
                File.WriteAllBytes(path, bytes);
                var text = OpenApi.Parse(File.ReadAllText(path), "https://api.x", "openapi.json", out var textFailure);
                var utf8 = OpenApi.Parse(OpenApi.ReadUtf8(path), "https://api.x", "openapi.json", out var utf8Failure);
                Assert.True(Dump(text) == Dump(utf8), $"{what}, {form}: the map from bytes differs:\n{Dump(text)}\n---\n{Dump(utf8)}");
                Assert.Equal(textFailure, utf8Failure);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(SpecFiles))]
    public void AResponseBodyParsedFromBytes_IsTheMapItsTextGives(string what)
    {
        string spec = Specs[what]();
        var spill = Path.GetTempFileName();
        try
        {
            File.WriteAllText(spill, spec);
            foreach (var body in new[]
            {
                new ResponseBody(BodyKind.Text, spec, [], spec.Length, Truncated: false, null),
                new ResponseBody(BodyKind.Text, spec[..Math.Min(spec.Length, 12)], [], spec.Length, Truncated: true, spill),
            })
            {
                var text = OpenApi.Parse(OpenApi.FullText(body), "https://api.x", "spec", out var textFailure);
                var utf8 = OpenApi.Parse(OpenApi.FullUtf8(body), "https://api.x", "spec", out var utf8Failure);
                Assert.True(Dump(text) == Dump(utf8), $"{what}, truncated {body.Truncated}: the map from bytes differs");
                Assert.Equal(textFailure, utf8Failure);
            }
        }
        finally { File.Delete(spill); }
    }

    /// <summary>Everything a parsed map holds, in order.</summary>
    private static string Dump(ApiMap? map)
    {
        if (map is null) return "<none>";
        var sb = new StringBuilder($"{map.BaseUrl}|{map.SpecSource}|{map.SpecVersion}|{map.DeclaredCount}|{map.ObservedCount}\n");
        foreach (var e in map.Endpoints)
        {
            sb.Append($"{e.Method} {e.Path} {e.Confidence} [{e.Group}] [{e.Summary}] {e.Deprecated} {e.SeenCount} [{e.InferredShape}]");
            foreach (var p in e.Params) sb.Append($" ({p.Name},{p.In},{p.Required},{p.Type})");
            sb.Append('\n').Append(e.ExampleResponse).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Expert 4's synthetic spec (plan 17 round 9): five operations a resource, each with a
    /// 300-character description, query parameters, an item id and a 200 example; tags res0..res39.</summary>
    internal static string BigSpec(int resources)
    {
        var sb = new StringBuilder("{\"openapi\":\"3.0.1\",\"info\":{\"title\":\"Big\",\"version\":\"2.4.0\"},\"paths\":{");
        const string example = "{\"id\":\"it_123\",\"name\":\"An item\",\"tags\":[\"a\",\"b\"],\"owner\":{\"id\":\"u_1\"},\"amount\":1200}";
        const string idParam = ",{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\"}}";
        for (int i = 0; i < resources; i++)
        {
            string tag = $"res{i % 40}";
            string op(string summary, string extra) =>
                $"{{\"tags\":[\"{tag}\"],\"summary\":\"{summary}\",\"description\":\"{new string('d', 300)}\",\"parameters\":[{{\"name\":\"limit\",\"in\":\"query\",\"schema\":{{\"type\":\"integer\"}}}}{extra}],\"responses\":{{\"200\":{{\"description\":\"ok\",\"content\":{{\"application/json\":{{\"example\":{example}}}}}}},\"404\":{{\"description\":\"not found\"}}}}}}";
            if (i > 0) sb.Append(',');
            sb.Append($"\"/v1/res{i}/items\":{{\"get\":{op("List items " + i, "")},\"post\":{op("Create an item " + i, "")}}},");
            sb.Append($"\"/v1/res{i}/items/{{id}}\":{{\"get\":{op("Get an item " + i, idParam)},\"put\":{op("Replace an item " + i, idParam)},\"delete\":{op("Delete an item " + i, idParam)}}}");
        }
        return sb.Append("}}").ToString();
    }

    // ── Promotion ────────────────────────────────────────────────────────

    [Fact]
    public void APromotedRequestIsEnvironmentDriven()
    {
        var endpoint = new Endpoint("GET", "/v1/charges/{id}", Confidence.Declared, "charges",
            Summary: "Retrieve a charge",
            Parameters: [new EndpointParam("X-Key", "header", true, "string")]);

        var text = OpenApi.ToRequestFile(endpoint);

        Assert.Contains("# Retrieve a charge", text);
        Assert.Contains("GET {{base_url}}/v1/charges/{{id}}", text);
        Assert.Contains("name Get charge", text);
        Assert.Contains("header X-Key: {{X_Key}}", text);
        Assert.Contains("assert status equals 200", text);
        // And it parses as a real request file.
        var doc = ReqDocument.Parse(text);
        Assert.Empty(doc.Errors);
        Assert.Equal("GET", doc.Method);
    }

    [Fact]
    public void APromotedObservedEndpointSaysItIsInferred()
    {
        var endpoint = new Endpoint("POST", "/v1/transfers", Confidence.Observed, "transfers", SeenCount: 3);

        var text = OpenApi.ToRequestFile(endpoint);

        Assert.Contains("# observed in traffic", text);
        Assert.Contains("seen 3×", text);
        Assert.Contains("body json", text);   // POST gets a body block
    }

    [Theory]
    [InlineData("GET", "/v1/charges/{id}", "Get charge")]
    [InlineData("GET", "/v1/charges", "Get charges")]
    [InlineData("POST", "/v1/charges", "Create charges")]
    [InlineData("DELETE", "/v1/refunds/{id}", "Delete refund")]
    public void NamesReadLikeSomeoneTypedThem(string method, string path, string expected)
    {
        Assert.Equal(expected, OpenApi.NameOf(new Endpoint(method, path, Confidence.Declared, "g")));
    }
}

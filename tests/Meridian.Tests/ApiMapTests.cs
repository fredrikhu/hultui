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

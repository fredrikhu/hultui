using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

public class DirectivesTests
{
    private static HttpResponseData Response(int status = 200, string body = """{"status":"succeeded","id":"ch_1"}""",
        int totalMs = 96) => new(
        status, "OK",
        [("Content-Type", "application/json; charset=utf-8"), ("X-Request-Id", "rid-42")],
        "application/json",
        new ResponseBody(BodyKind.Text, body, [], body.Length, false, null),
        new PhaseTimings(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
            TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(46),
            TimeSpan.FromMilliseconds(totalMs), false, "HTTP/1.1", null),
        "https://x/y");

    // ── Assertions ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("status equals 200", true, "200")]
    [InlineData("status equals 201", false, "200")]
    [InlineData("time under 500 ms", true, "96")]
    [InlineData("time under 50", false, "96")]
    [InlineData("header content-type contains application/json", true, "application/json; charset=utf-8")]
    [InlineData("header x-missing contains x", false, "header absent")]
    [InlineData("body $.status equals \"succeeded\"", true, "succeeded")]
    [InlineData("body $.status equals succeeded", true, "succeeded")]     // quotes optional
    [InlineData("body $.status equals failed", false, "succeeded")]
    [InlineData("body $.nope equals x", false, "no match")]
    public void AssertionsEvaluate(string args, bool passed, string actual)
    {
        var spec = Directives.ParseAssert(args, out var error);
        Assert.True(spec != null, error);
        var outcome = Directives.Evaluate(spec!, Response());
        Assert.Equal(passed, outcome.Passed);
        Assert.Equal(actual, outcome.Actual);
    }

    [Theory]
    [InlineData("status", "assert needs")]
    [InlineData("status wibbles 200", "unknown operator")]
    [InlineData("smell equals bad", "unknown source")]
    [InlineData("body notapath equals x", "$")]
    [InlineData("header contains x", "assert header needs")]   // missing name
    public void BadAssertLinesSayWhy(string args, string fragment)
    {
        Assert.Null(Directives.ParseAssert(args, out var error));
        Assert.Contains(fragment, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatRoundTripsTheSpec()
    {
        var spec = Directives.ParseAssert("body $.status equals succeeded", out _)!;
        Assert.Equal("body $.status equals succeeded", Directives.FormatAssert(spec));
    }

    // ── Extractions ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("body $.id -> charge_id", "ch_1")]
    [InlineData("header x-request-id -> rid", "rid-42")]
    public void ExtractionsSelect(string args, string expected)
    {
        var spec = Directives.ParseExtract(args, out var error);
        Assert.True(spec != null, error);
        Assert.Equal(expected, Directives.EvaluateExtract(spec!, Response()));
    }

    [Fact]
    public void AnExtractionThatSelectsNothingIsNull()
    {
        var spec = Directives.ParseExtract("body $.absent -> x", out _)!;
        Assert.Null(Directives.EvaluateExtract(spec, Response()));
    }

    [Theory]
    [InlineData("body $.id charge_id", "->")]                  // no arrow
    [InlineData("body $.id -> $uuid", "built-ins")]            // $-name reserved
    [InlineData("cookie sid -> x", "body $.path | header")]    // unknown source
    public void BadExtractLinesSayWhy(string args, string fragment)
    {
        Assert.Null(Directives.ParseExtract(args, out var error));
        Assert.Contains(fragment, error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Chain ────────────────────────────────────────────────────────────

    private static Chain.Node Node(string path, string req)
        => Chain.Analyze(path, ReqDocument.Parse(req));

    [Fact]
    public void AnalyzeFindsUsesAndProduces()
    {
        var node = Node("login.req",
            "POST {{base_url}}/login\nheader X-K: {{api_key}}\nbody\n{{$uuid}} {{base_url}}\nend\nextract body $.token -> token\n");

        Assert.Equal(["base_url", "api_key"], node.Uses);   // $uuid excluded, base_url deduped
        Assert.Equal(["token"], node.Produces);
    }

    [Fact]
    public void RunOrderPutsDependenciesFirst()
    {
        var login = Node("login.req", "POST https://x/login\nextract body $.token -> token\n");
        var create = Node("create.req", "POST https://x/c\nheader A: {{token}}\nextract body $.id -> charge_id\n");
        var get = Node("get.req", "GET https://x/c/{{charge_id}}\nheader A: {{token}}\n");

        var (order, error) = Chain.RunOrderFor("get.req", [get, create, login]);

        Assert.Null(error);
        Assert.Equal(["login.req", "create.req", "get.req"], order);
    }

    [Fact]
    public void AVariableNobodyProducesIsNotADependency()
    {
        var lone = Node("lone.req", "GET {{base_url}}/x\n");
        var (order, error) = Chain.RunOrderFor("lone.req", [lone]);

        Assert.Null(error);
        Assert.Equal(["lone.req"], order);
    }

    [Fact]
    public void ACycleIsAnErrorNamingAMember()
    {
        var a = Node("a.req", "GET https://x/{{from_b}}\nextract body $.x -> from_a\n");
        var b = Node("b.req", "GET https://x/{{from_a}}\nextract body $.x -> from_b\n");

        var (_, error) = Chain.RunOrderFor("a.req", [a, b]);

        Assert.NotNull(error);
        Assert.Contains("cycle", error);
    }

    [Fact]
    public void ConsumersAreFound()
    {
        var login = Node("login.req", "POST https://x\nextract body $.t -> token\n");
        var get = Node("get.req", "GET https://x\nheader A: {{token}}\n");

        Assert.Equal(["get.req"], Chain.ConsumersOf("token", [login, get]));
    }
}

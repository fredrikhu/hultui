using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

public class VariableTests
{
    // ── Tokenizer ────────────────────────────────────────────────────────

    [Fact]
    public void LiteralsAndVariablesSplit()
    {
        var segs = VariableResolver.Tokenize("{{base_url}}/v1/charges/{{charge_id}}?x=1");

        Assert.Equal(
            [("base_url", true), ("/v1/charges/", false), ("charge_id", true), ("?x=1", false)],
            segs.Select(s => (s.Text, s.IsVariable)).ToArray());
    }

    [Theory]
    [InlineData("no variables at all")]
    [InlineData("unclosed {{brace")]
    [InlineData("empty {{}} braces")]
    [InlineData("spaced {{ two words }} name")]
    [InlineData("nested {{a{{b}}")]                 // the inner {{b}} is real; outer {{a is literal
    public void MalformedBracesAreLiteralNotErrors(string template)
    {
        var segs = VariableResolver.Tokenize(template);
        // Whatever the split, reassembling literals + {{var}} reproduces the input.
        var rebuilt = string.Concat(segs.Select(s => s.IsVariable ? "{{" + s.Text + "}}" : s.Text));
        Assert.Equal(template, rebuilt);
    }

    // ── Precedence: env wins over extracted; $ is builtins-only ──────────

    [Fact]
    public void TheEnvironmentBeatsAnExtractedValue()
    {
        var r = new VariableResolver(
            environment: new Dictionary<string, string> { ["token"] = "from-env" },
            extracted: new Dictionary<string, string> { ["token"] = "from-run" });

        Assert.Equal("from-env", r.Resolve("{{token}}").Text);
    }

    [Fact]
    public void ExtractedFillsWhatTheEnvironmentDoesNotDefine()
    {
        var r = new VariableResolver(
            environment: new Dictionary<string, string> { ["base_url"] = "https://x" },
            extracted: new Dictionary<string, string> { ["charge_id"] = "ch_1" });

        var res = r.Resolve("{{base_url}}/v1/charges/{{charge_id}}");
        Assert.Equal("https://x/v1/charges/ch_1", res.Text);
        Assert.True(res.FullyResolved);
    }

    [Fact]
    public void DollarNamesResolveOnlyThroughBuiltins()
    {
        var r = new VariableResolver(
            environment: new Dictionary<string, string> { ["$uuid"] = "spoofed" },
            builtins: name => name == "$uuid" ? "u-1" : null);

        Assert.Equal("u-1", r.Resolve("{{$uuid}}").Text);
    }

    [Fact]
    public void PlainNamesNeverReachBuiltins()
    {
        var r = new VariableResolver(builtins: _ => "should-not-appear");
        var res = r.Resolve("{{plain}}");
        Assert.Equal("{{plain}}", res.Text);
        Assert.Equal(["plain"], res.Unresolved);
    }

    // ── Unresolved handling ──────────────────────────────────────────────

    [Fact]
    public void UnresolvedVariablesStayVisibleAndAreReported()
    {
        var r = new VariableResolver(environment: new Dictionary<string, string> { ["a"] = "1" });
        var res = r.Resolve("{{a}}-{{missing}}-{{also_missing}}");

        Assert.Equal("1-{{missing}}-{{also_missing}}", res.Text);
        Assert.Equal(["missing", "also_missing"], res.Unresolved);
        Assert.False(res.FullyResolved);
    }

    [Fact]
    public void TheShippedBuiltinsProduceAUuidAndATimestamp()
    {
        Assert.True(Guid.TryParse(VariableResolver.DefaultBuiltins("$uuid"), out _));
        Assert.True(long.TryParse(VariableResolver.DefaultBuiltins("$timestamp"), out _));
        Assert.Null(VariableResolver.DefaultBuiltins("$nope"));
    }
}

using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

public class ReqDocumentTests
{
    private const string Full = """
        # Get one charge by id.
        GET {{base_url}}/v1/charges/{{charge_id}}
        name Get charge

        header Authorization: Bearer {{token}}
        header Accept: application/json

        body json
        {
          "expand": ["refunds"]
        }
        end

        auth bearer {{token}}
        assert status equals 200
        assert body $.status equals "succeeded"
        extract body $.id -> charge_id
        """;

    [Fact]
    public void EveryConstructParses()
    {
        var doc = ReqDocument.Parse(Full);

        Assert.Empty(doc.Errors);
        Assert.Equal("GET", doc.Method);
        Assert.Equal("{{base_url}}/v1/charges/{{charge_id}}", doc.Url);
        Assert.Equal("Get charge", doc.Name);
        Assert.Equal(2, doc.Headers.Count);
        Assert.Equal(("Authorization", "Bearer {{token}}"), (doc.Headers[0].Name, doc.Headers[0].Value));
        Assert.Equal("json", doc.Body!.Type);
        Assert.Equal("{\n  \"expand\": [\"refunds\"]\n}", doc.Body.Content.ReplaceLineEndings("\n"));
        Assert.Equal("bearer {{token}}", doc.Auth!.Args);
        Assert.Equal(2, doc.Asserts.Count);
        Assert.Equal("body $.id -> charge_id", doc.Extracts.Single().Args);
    }

    // ── Round-trip: the format's core contract ───────────────────────────

    [Theory]
    [InlineData("GET https://x/y\n")]
    [InlineData("GET https://x/y")]                          // no trailing newline
    [InlineData("GET https://x/y\r\nheader A: b\r\n")]       // CRLF
    [InlineData("# only a comment\n\n\n")]
    [InlineData("")]
    [InlineData("garbage line\nGET https://x\nmore ??? garbage\n")]   // errors preserved too
    public void SerializeReproducesTheInputExactly(string text)
    {
        Assert.Equal(text, ReqDocument.Parse(text).Serialize());
    }

    [Fact]
    public void MixedLineEndingsSurviveByteForByte()
    {
        const string text = "GET https://x\r\nheader A: b\nheader C: d\r\nbody\r\nraw \r\n mixed\nend\n";
        Assert.Equal(text, ReqDocument.Parse(text).Serialize());
    }

    [Fact]
    public void TheFullExampleRoundTrips()
    {
        Assert.Equal(Full, ReqDocument.Parse(Full).Serialize());
    }

    // ── Body fences ──────────────────────────────────────────────────────

    [Fact]
    public void ACustomMarkerLetsTheBodyContainAnEndLine()
    {
        var doc = ReqDocument.Parse("POST https://x\nbody text until DONE-7f3a\nfirst\nend\nlast\nDONE-7f3a\n");

        Assert.Empty(doc.Errors);
        Assert.Equal("first\nend\nlast", doc.Body!.Content);
        Assert.Equal("DONE-7f3a", doc.Body.Marker);
    }

    [Fact]
    public void AnEmptyBodyIsEmptyNotANewline()
    {
        var doc = ReqDocument.Parse("POST https://x\nbody json\nend\n");
        Assert.Equal("", doc.Body!.Content);
    }

    [Fact]
    public void AnUnclosedBodyIsAnErrorAtTheFenceLine()
    {
        var doc = ReqDocument.Parse("POST https://x\nbody json\n{ never closed\n");

        var error = Assert.Single(doc.Errors);
        Assert.Equal(2, error.Line);
        Assert.Contains("never closed", error.Message);
        // And still byte-exact.
        Assert.Equal("POST https://x\nbody json\n{ never closed\n", ReqDocument.Parse("POST https://x\nbody json\n{ never closed\n").Serialize());
    }

    // ── Errors: line-numbered, never thrown ──────────────────────────────

    [Fact]
    public void AHeaderWithoutAColonIsALineNumberedError()
    {
        var doc = ReqDocument.Parse("GET https://x\nheader broken\n");

        var error = Assert.Single(doc.Errors);
        Assert.Equal(2, error.Line);
        Assert.Contains("Name: value", error.Message);
    }

    [Fact]
    public void AnUnknownDirectiveNamesItself()
    {
        var doc = ReqDocument.Parse("GET https://x\nfrobnicate hard\n");

        var error = Assert.Single(doc.Errors);
        Assert.Contains("frobnicate", error.Message);
        Assert.Equal(2, error.Line);
    }

    [Fact]
    public void ASecondRequestLineIsAnErrorAndTheFirstWins()
    {
        var doc = ReqDocument.Parse("GET https://one\nPOST https://two\n");

        Assert.Equal("GET", doc.Method);
        Assert.Equal("https://one", doc.Url);
        Assert.Equal(2, Assert.Single(doc.Errors).Line);
    }

    [Fact]
    public void AMethodWithoutAUrlIsAnError()
    {
        var doc = ReqDocument.Parse("GET\n");
        Assert.Contains("URL", Assert.Single(doc.Errors).Message);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("auth")]
    [InlineData("assert")]
    [InlineData("extract")]
    public void ABareDirectiveIsAnError(string directive)
    {
        var doc = ReqDocument.Parse($"GET https://x\n{directive}\n");
        Assert.Equal(2, Assert.Single(doc.Errors).Line);
    }

    // ── _folder.req shape ────────────────────────────────────────────────

    [Fact]
    public void AFolderFileHasNoRequestLineAndThatIsFine()
    {
        var doc = ReqDocument.Parse("header Authorization: Bearer {{token}}\nauth oauth2 client_credentials\n");

        Assert.Empty(doc.Errors);
        Assert.Null(doc.Method);
        Assert.Single(doc.Headers);
        Assert.NotNull(doc.Auth);
    }

    [Fact]
    public void CustomMethodsParse()
    {
        // Any all-caps token is a method — PROPFIND, PURGE, a future verb.
        var doc = ReqDocument.Parse("PROPFIND https://dav.example/cal\n");
        Assert.Empty(doc.Errors);
        Assert.Equal("PROPFIND", doc.Method);
    }
}

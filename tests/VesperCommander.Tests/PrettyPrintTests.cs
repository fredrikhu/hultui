using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The formatter behind the viewer's `f`. What matters is that a minified document comes
/// out one thing per line, that nothing in it is lost or mangled on the way, and that a
/// document that is not JSON or XML is refused with a line number rather than half-done.
/// </summary>
public class PrettyPrintTests
{
    private static string[] Format(PrettyKind kind, string text, out string? error) =>
        PrettyPrint.Format(kind, text.Split('\n'), out error) ?? [];

    // ── JSON ─────────────────────────────────────────────────────────────
    [Fact]
    public void MinifiedJsonComesOutIndented()
    {
        var lines = Format(PrettyKind.Json, """{"a":1,"b":[true,null],"c":{"d":"e"}}""", out var err);

        Assert.Null(err);
        Assert.Equal(
        [
            "{",
            "  \"a\": 1,",
            "  \"b\": [",
            "    true,",
            "    null",
            "  ],",
            "  \"c\": {",
            "    \"d\": \"e\"",
            "  }",
            "}",
        ], lines);
    }

    /// <summary>The default JSON encoder writes every non-ASCII character as \uXXXX, which
    /// would turn a Swedish settings file into escape codes. Text stays text.</summary>
    [Fact]
    public void NonAsciiTextIsKeptAsText()
    {
        var lines = Format(PrettyKind.Json, """{"stad":"Göteborg","tecken":"ä→ö"}""", out _);
        Assert.Contains("  \"stad\": \"Göteborg\",", lines);
        Assert.Contains("  \"tecken\": \"ä→ö\"", lines);
    }

    [Fact]
    public void TrailingCommasAndCommentsAreAccepted()
    {
        var lines = Format(PrettyKind.Json, "{\n  // a comment\n  \"a\": 1,\n}", out var err);
        Assert.Null(err);
        Assert.Equal(["{", "  \"a\": 1", "}"], lines);
    }

    [Fact]
    public void ALeadingByteOrderMarkIsNotAnError()
    {
        var lines = Format(PrettyKind.Json, new string((char)0xFEFF, 1) + "{\"a\":1}", out var err);
        Assert.Null(err);
        Assert.Equal(["{", "  \"a\": 1", "}"], lines);
    }

    [Fact]
    public void InvalidJsonIsRefusedWithTheLine()
    {
        var lines = Format(PrettyKind.Json, "{\n\"a\": 1,\n\"b\": }\n}", out var err);
        Assert.Empty(lines);
        Assert.StartsWith("Not valid JSON", err);
        Assert.Contains("line 3", err);
    }

    [Fact]
    public void LongNumbersAndStringsSurviveTheRoundTrip()
    {
        var lines = Format(PrettyKind.Json, """{"n":12345678901234567890,"f":1.0e-10,"s":"a\"b\\c\n"}""", out var err);
        Assert.Null(err);
        Assert.Contains("  \"n\": 12345678901234567890,", lines);
        Assert.Contains("  \"f\": 1.0e-10,", lines);
        Assert.Contains("  \"s\": \"a\\\"b\\\\c\\n\"", lines);
    }

    // ── XML ──────────────────────────────────────────────────────────────
    [Fact]
    public void MinifiedXmlComesOutIndentedAndKeepsItsDeclaration()
    {
        var lines = Format(PrettyKind.Xml,
            """<?xml version="1.0" encoding="utf-8"?><root a="1"><item>x</item><empty/><!-- c --></root>""", out var err);

        Assert.Null(err);
        Assert.Equal(
        [
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            "<root a=\"1\">",
            "  <item>x</item>",
            "  <empty />",
            "  <!-- c -->",
            "</root>",
        ], lines);
    }

    /// <summary>A file that had no declaration doesn't grow one — it would be the one line
    /// in the view that was not in the file.</summary>
    [Fact]
    public void XmlWithoutADeclarationDoesNotGetOne()
    {
        var lines = Format(PrettyKind.Xml, "<a><b/></a>", out _);
        Assert.Equal(["<a>", "  <b />", "</a>"], lines);
    }

    [Fact]
    public void ADoctypeDoesNotStopTheParse()
    {
        var lines = Format(PrettyKind.Xml,
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://example.invalid/x.dtd\"><plist><dict/></plist>",
            out var err);
        Assert.Null(err);
        Assert.Contains("  <dict />", lines);
    }

    [Fact]
    public void InvalidXmlIsRefusedWithTheLine()
    {
        var lines = Format(PrettyKind.Xml, "<a>\n<b>\n</a>", out var err);
        Assert.Empty(lines);
        Assert.StartsWith("Not valid XML", err);
        Assert.Contains("line 3", err);
    }

    [Fact]
    public void NonAsciiXmlTextIsKeptAsText()
    {
        var lines = Format(PrettyKind.Xml, "<stad namn=\"Göteborg\">Malmö</stad>", out _);
        Assert.Equal(["<stad namn=\"Göteborg\">Malmö</stad>"], lines);
    }

    /// <summary>A text node is written byte for byte by the writer, so a doc comment's
    /// summary kept the file's twelve-space indentation and put its closing tag wherever the
    /// trailing whitespace landed. The text is re-set one level under its element, inline
    /// elements stay in the flow, and the closing tag gets a line at the element's depth.</summary>
    [Fact]
    public void MultiLineTextIsReindentedUnderItsElement()
    {
        var lines = Format(PrettyKind.Xml, string.Join('\n',
        [
            "<doc>",
            "    <members>",
            "        <member name=\"T:X\">",
            "            <summary>",
            "            Platform-neutral router for accessibility actions.",
            "            Each action is queued when <see cref=\"T:HultUI.Window\" /> drains it at the start",
            "            of the next frame.",
            "            </summary>",
            "            <remarks>One line.</remarks>",
            "        </member>",
            "    </members>",
            "</doc>",
        ]), out var err);

        Assert.Null(err);
        Assert.Equal(
        [
            "<doc>",
            "  <members>",
            "    <member name=\"T:X\">",
            "      <summary>",
            "        Platform-neutral router for accessibility actions.",
            "        Each action is queued when <see cref=\"T:HultUI.Window\" /> drains it at the start",
            "        of the next frame.",
            "      </summary>",
            "      <remarks>One line.</remarks>",
            "    </member>",
            "  </members>",
            "</doc>",
        ], lines);
    }

    [Fact]
    public void ABlankLineInTextStaysBlankAndAFollowingElementGetsItsOwnLine()
    {
        var lines = Format(PrettyKind.Xml, "<r>\n    first\n\n    second\n    <para>p</para>\n</r>", out _);
        Assert.Equal(["<r>", "  first", "", "  second", "  <para>p</para>", "</r>"], lines);
    }

    [Fact]
    public void PreservedWhitespaceIsLeftAlone()
    {
        var lines = Format(PrettyKind.Xml, "<r><pre xml:space=\"preserve\">\n   keep\n  this</pre></r>", out _);
        Assert.Equal(["<r>", "  <pre xml:space=\"preserve\">", "   keep", "  this</pre>", "</r>"], lines);
    }

    /// <summary>The writer's own declaration is generated from its settings — utf-8 whatever
    /// the file said, and added to a file that declared no encoding. The file's is written
    /// back as it was.</summary>
    [Theory]
    [InlineData("<?xml version=\"1.0\"?>")]
    [InlineData("<?xml version=\"1.0\" encoding=\"iso-8859-1\"?>")]
    [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>")]
    public void TheDeclarationIsWrittenBackAsItWas(string declaration)
    {
        var lines = Format(PrettyKind.Xml, declaration + "<a><b/></a>", out var err);
        Assert.Null(err);
        Assert.Equal([declaration, "<a>", "  <b />", "</a>"], lines);
    }

    // ── Detection ────────────────────────────────────────────────────────
    [Theory]
    [InlineData("settings.json", "{}", PrettyKind.Json)]
    [InlineData("app.csproj", "<Project/>", PrettyKind.Xml)]
    [InlineData("icon.svg", "<svg/>", PrettyKind.Xml)]
    [InlineData("response.txt", "  {\"a\":1}", PrettyKind.Json)]
    [InlineData("response.txt", "[1,2]", PrettyKind.Json)]
    [InlineData("dump", "<x/>", PrettyKind.Xml)]
    [InlineData("notes.txt", "hello", PrettyKind.None)]
    [InlineData("Program.cs", "[assembly: X]", PrettyKind.None)]   // a code file is not sniffed
    [InlineData("page.html", "<!doctype html>", PrettyKind.None)]
    public void TheFooterOffersWhatTheFileLooksLike(string name, string text, PrettyKind expected)
    {
        Assert.Equal(expected, PrettyPrint.Detect(name, text.Split('\n')));
    }

    [Fact]
    public void SniffingSkipsBlankLeadingLines()
    {
        Assert.Equal(PrettyKind.Json, PrettyPrint.Sniff(["", "   ", "\t{"]));
    }
}

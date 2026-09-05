using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// `f` in the Shift+F3 viewer: the file re-indented, and back. Driven through the real key
/// on the real view, so what is asserted is what the rows show — and that the two things
/// the key must not disturb, find and the file as stored, are still themselves afterwards.
/// </summary>
public class ViewerPrettyTests
{
    private static void WaitPretty(ViewerTests.Fixture t, bool pretty) =>
        t.Session.PumpUntil(() => t.Field<bool>("_viewerPretty") == pretty
                               || t.Field<string?>("_viewerPrettyError") != null,
            TimeSpan.FromSeconds(20));

    [Fact]
    public void FReIndentsAMinifiedJsonFileAndFAgainRestoresIt()
    {
        using var t = new ViewerTests.Fixture();
        t.Write("min.json", """{"a":1,"b":[2,3]}""");
        t.Open();
        t.ViewFile("min.json");
        Assert.Single(t.Lines);

        t.Press(KeyCode.F);
        WaitPretty(t, true);
        t.Session.PumpFrame();

        Assert.Null(t.Field<string?>("_viewerPrettyError"));
        Assert.Equal(["{", "  \"a\": 1,", "  \"b\": [", "    2,", "    3", "  ]", "}"], t.Lines);
        // The rows show the new text, not the old one patched in place.
        Assert.Equal("{", t.Label(0).Content);
        Assert.Equal("  \"a\": 1,", t.Label(1).Content);

        t.Press(KeyCode.F);
        WaitPretty(t, false);
        t.Session.PumpFrame();
        Assert.Equal(["""{"a":1,"b":[2,3]}"""], t.Lines);
        Assert.Equal("""{"a":1,"b":[2,3]}""", t.Label(0).Content);
    }

    [Fact]
    public void FReIndentsXmlByExtension()
    {
        using var t = new ViewerTests.Fixture();
        t.Write("cfg.xml", "<a><b>x</b></a>");
        t.Open();
        t.ViewFile("cfg.xml");
        Assert.Equal(PrettyKind.Xml, t.Field<PrettyKind>("_viewerPrettyKind"));

        t.Press(KeyCode.F);
        WaitPretty(t, true);

        Assert.Equal(["<a>", "  <b>x</b>", "</a>"], t.Lines);
    }

    /// <summary>The extension says nothing, so the footer offers nothing — but the key still
    /// tries, from the first character, and a JSON body saved as .txt formats.</summary>
    [Fact]
    public void FTriesAPlainTextFileByItsFirstCharacter()
    {
        using var t = new ViewerTests.Fixture();
        t.Write("body.txt", "[1,2]");
        t.Open();
        t.ViewFile("body.txt");

        t.Press(KeyCode.F);
        WaitPretty(t, true);

        Assert.Equal(["[", "  1,", "  2", "]"], t.Lines);
    }

    [Fact]
    public void AFileThatIsNotJsonSaysSoAndStaysAsItWas()
    {
        using var t = new ViewerTests.Fixture();
        t.Write("broken.json", "{\"a\": }");
        t.Open();
        t.ViewFile("broken.json");

        t.Press(KeyCode.F);
        WaitPretty(t, true);   // returns on the error

        Assert.False(t.Field<bool>("_viewerPretty"));
        Assert.StartsWith("Not valid JSON", t.Field<string?>("_viewerPrettyError"));
        Assert.Equal(["{\"a\": }"], t.Lines);
    }

    [Fact]
    public void ProseIsRefusedWithoutAParse()
    {
        using var t = new ViewerTests.Fixture();
        t.Write("notes.txt", "hello there");
        t.Open();
        t.ViewFile("notes.txt");

        t.Press(KeyCode.F);
        t.Session.PumpFrame();

        Assert.Equal("Not JSON or XML", t.Field<string?>("_viewerPrettyError"));
        Assert.Equal(["hello there"], t.Lines);
    }

    /// <summary>Find searches what is on screen: after a format, a hit is on the formatted
    /// line, and Ctrl+F is still find — bare f must not have taken it.</summary>
    [Fact]
    public void FindSearchesTheFormattedTextAndCtrlFIsStillFind()
    {
        using var t = new ViewerTests.Fixture();
        t.Write("min.json", """{"alpha":1,"beta":2}""");
        t.Open();
        t.ViewFile("min.json");
        t.Press(KeyCode.F);
        WaitPretty(t, true);
        t.Session.PumpFrame();

        t.Find("beta");
        Assert.True(t.FindOpen);
        Assert.Single(t.Hits);
        Assert.Equal(2, t.Hits[0].Line);   // "  \"beta\": 2" is the third line
        // Typing an f into the query is text, not a toggle.
        Assert.True(t.Field<bool>("_viewerPretty"));
    }

    /// <summary>Opening another file starts over: the second file is shown as stored.</summary>
    [Fact]
    public void TheNextFileOpensAsStored()
    {
        using var t = new ViewerTests.Fixture();
        t.Write("a.json", "{\"a\":1}");
        t.Write("b.json", "{\"b\":2}");
        t.Open();
        t.ViewFile("a.json");
        t.Press(KeyCode.F);
        WaitPretty(t, true);
        t.Press(KeyCode.Escape);

        t.ViewFile("b.json");

        Assert.False(t.Field<bool>("_viewerPretty"));
        Assert.Equal(["{\"b\":2}"], t.Lines);
    }
}

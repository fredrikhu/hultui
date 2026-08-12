using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

/// <summary>The edit API's contract: rewrite only your own line(s), keep every
/// other byte — comments, blanks, unknown lines, per-line terminators.</summary>
public class ReqDocumentEditTests
{
    [Fact]
    public void SetRequestLineTouchesOnlyItsLine()
    {
        var doc = ReqDocument.Parse("# keep me\r\nGET https://old\r\nheader A: b\r\n");
        doc.SetRequestLine("POST", "https://new");

        Assert.Equal("# keep me\r\nPOST https://new\r\nheader A: b\r\n", doc.Serialize());
        Assert.Equal("POST", doc.Method);
        Assert.True(doc.Edited);
    }

    [Fact]
    public void SetRequestLineInsertsAtTheTopWhenAbsent()
    {
        var doc = ReqDocument.Parse("header A: b\n");
        doc.SetRequestLine("GET", "https://x");

        Assert.Equal("GET https://x\nheader A: b\n", doc.Serialize());
    }

    [Fact]
    public void MarkSavedClearsEdited()
    {
        var doc = ReqDocument.Parse("GET https://x\n");
        doc.SetRequestLine("GET", "https://y");
        doc.MarkSaved();
        Assert.False(doc.Edited);
    }

    // ── Headers ──────────────────────────────────────────────────────────

    [Fact]
    public void SetHeaderRewritesInPlace()
    {
        var doc = ReqDocument.Parse("GET https://x\nheader A: 1\n# note\nheader B: 2\n");
        doc.SetHeader(1, "B", "changed");

        Assert.Equal("GET https://x\nheader A: 1\n# note\nheader B: changed\n", doc.Serialize());
    }

    [Fact]
    public void AddHeaderLandsAfterTheLastHeader()
    {
        var doc = ReqDocument.Parse("GET https://x\nheader A: 1\n\nassert status equals 200\n");
        doc.AddHeader("B", "2");

        Assert.Equal("GET https://x\nheader A: 1\nheader B: 2\n\nassert status equals 200\n", doc.Serialize());
    }

    [Fact]
    public void AddHeaderWithoutAnyLandsAfterTheRequestLine()
    {
        var doc = ReqDocument.Parse("GET https://x\n\nbody\nhi\nend\n");
        doc.AddHeader("A", "1");

        Assert.Equal("GET https://x\nheader A: 1\n\nbody\nhi\nend\n", doc.Serialize());
    }

    [Fact]
    public void RemoveHeaderRemovesOnlyItsLine()
    {
        var doc = ReqDocument.Parse("GET https://x\nheader A: 1\nheader B: 2\n");
        doc.RemoveHeader(0);

        Assert.Equal("GET https://x\nheader B: 2\n", doc.Serialize());
        Assert.Equal("B", doc.Headers.Single().Name);
    }

    [Fact]
    public void EditsFollowTheFilesOwnLineEndings()
    {
        var doc = ReqDocument.Parse("GET https://x\r\nheader A: 1\r\n");
        doc.AddHeader("B", "2");

        Assert.Equal("GET https://x\r\nheader A: 1\r\nheader B: 2\r\n", doc.Serialize());
    }

    // ── Body ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetBodyReplacesTheBlockAndKeepsEverythingElse()
    {
        var doc = ReqDocument.Parse("POST https://x\nbody json\nold\nend\nassert status equals 200\n");
        doc.SetBody("json", "{ \"new\": true }");

        Assert.Equal("POST https://x\nassert status equals 200\nbody json\n{ \"new\": true }\nend\n", doc.Serialize());
        Assert.Equal("{ \"new\": true }", doc.Body!.Content);
    }

    [Fact]
    public void SetBodyAppendsWhenAbsentEvenWithoutAFinalNewline()
    {
        var doc = ReqDocument.Parse("POST https://x");   // no trailing newline
        doc.SetBody("json", "{}");

        Assert.Equal("POST https://x\nbody json\n{}\nend\n", doc.Serialize());
    }

    [Fact]
    public void BodyContentContainingAnEndLineGetsACollisionFreeMarker()
    {
        var doc = ReqDocument.Parse("POST https://x\n");
        doc.SetBody("text", "first\nend\nlast");

        // The document must reparse to exactly this content — marker collision
        // would have truncated it at the "end" line.
        Assert.Equal("first\nend\nlast", doc.Body!.Content);
        Assert.NotEqual("end", doc.Body.Marker);
        // And a fresh parse of the serialized text agrees.
        Assert.Equal("first\nend\nlast", ReqDocument.Parse(doc.Serialize()).Body!.Content);
    }

    [Fact]
    public void ClearingABodyRemovesTheBlock()
    {
        var doc = ReqDocument.Parse("POST https://x\nbody json\nold\nend\n");
        doc.SetBody("", "");

        Assert.Equal("POST https://x\n", doc.Serialize());
        Assert.Null(doc.Body);
    }

    [Fact]
    public void EditsKeepLineNumbersTrue()
    {
        // The reparse-after-edit contract: typed views always match the text.
        var doc = ReqDocument.Parse("GET https://x\nheader A: 1\n");
        doc.AddHeader("B", "2");
        doc.SetHeader(1, "B", "3");

        Assert.Equal(3, doc.Headers[1].Line);
        Assert.Equal("3", doc.Headers[1].Value);
    }
}

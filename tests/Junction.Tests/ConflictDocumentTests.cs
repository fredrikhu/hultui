using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Conflict-marker parsing and re-rendering. This class writes to the user's working
/// tree, so the property that matters most is not "does it pick the right side" but
/// "does an untouched hunk come back byte-identical" — a partial save has to stay a
/// valid conflicted file that git and every other tool still understand.
/// </summary>
public class ConflictDocumentTests
{
    private const string TwoWay =
        "before\n" +
        "<<<<<<< HEAD\n" +
        "ours line\n" +
        "=======\n" +
        "theirs line\n" +
        ">>>>>>> feature/x\n" +
        "after\n";

    private const string Diff3 =
        "before\n" +
        "<<<<<<< HEAD\n" +
        "ours line\n" +
        "||||||| 4374b94\n" +
        "base line\n" +
        "=======\n" +
        "theirs line\n" +
        ">>>>>>> feature/x\n" +
        "after\n";

    [Fact]
    public void MarkersSplitTheFileIntoContextAndHunks()
    {
        var doc = ConflictDocument.Parse(TwoWay);

        Assert.True(doc.HasConflicts);
        var hunk = Assert.Single(doc.Hunks);
        Assert.Equal("HEAD", hunk.OursLabel);
        Assert.Equal("feature/x", hunk.TheirsLabel);
        Assert.Equal("ours line", Assert.Single(hunk.Ours));
        Assert.Equal("theirs line", Assert.Single(hunk.Theirs));
        Assert.False(hunk.HasBase);

        // Context either side survives as its own segments.
        Assert.Equal(3, doc.Segments.Count);
    }

    [Fact]
    public void Diff3StyleKeepsTheCommonAncestor()
    {
        var doc = ConflictDocument.Parse(Diff3);
        var hunk = Assert.Single(doc.Hunks);

        Assert.True(hunk.HasBase);
        Assert.Equal("base line", Assert.Single(hunk.Base));
        // git labels the ancestor with the merge base's short hash, not a fixed string.
        Assert.Equal("4374b94", hunk.BaseLabel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnresolvedHunkRendersBackByteIdentically(bool diff3)
    {
        // The save path writes every hunk, resolved or not. Anything lost here — a
        // label, the ancestor section, a line ending — corrupts a file the user has
        // not finished merging.
        var text = diff3 ? Diff3 : TwoWay;
        var doc = ConflictDocument.Parse(text);

        Assert.Equal(text, doc.Render(new Dictionary<int, ConflictChoice>()));
    }

    [Fact]
    public void CrlfFilesRenderBackWithCrlf()
    {
        var text = TwoWay.Replace("\n", "\r\n");
        var doc = ConflictDocument.Parse(text);

        Assert.Equal("\r\n", doc.NewLine);
        Assert.Equal(text, doc.Render(new Dictionary<int, ConflictChoice>()));
    }

    [Fact]
    public void AFileWithNoTrailingNewlineKeepsNotHavingOne()
    {
        var text = TwoWay.TrimEnd('\n');
        var doc = ConflictDocument.Parse(text);

        Assert.False(doc.TrailingNewline);
        Assert.Equal(text, doc.Render(new Dictionary<int, ConflictChoice>()));
    }

    [Theory]
    [InlineData(ConflictChoice.Ours, "before\nours line\nafter\n")]
    [InlineData(ConflictChoice.Theirs, "before\ntheirs line\nafter\n")]
    [InlineData(ConflictChoice.Both, "before\nours line\ntheirs line\nafter\n")]
    [InlineData(ConflictChoice.BothReversed, "before\ntheirs line\nours line\nafter\n")]
    public void EachChoiceReplacesTheWholeMarkedRegion(ConflictChoice choice, string expected)
    {
        var doc = ConflictDocument.Parse(TwoWay);
        Assert.Equal(expected, doc.Render(new Dictionary<int, ConflictChoice> { [0] = choice }));
    }

    [Fact]
    public void ManualTextIsSplitAndNormalised()
    {
        var doc = ConflictDocument.Parse(TwoWay);

        // Whatever the editor hands back — CRLF, a trailing newline — becomes plain
        // lines, and the document's own line ending is reapplied on render.
        var rendered = doc.Render(
            new Dictionary<int, ConflictChoice> { [0] = ConflictChoice.Manual },
            new Dictionary<int, string> { [0] = "merged a\r\nmerged b\r\n" });

        Assert.Equal("before\nmerged a\nmerged b\nafter\n", rendered);
    }

    [Fact]
    public void OneResolvedHunkDoesNotDisturbTheOther()
    {
        var text =
            "<<<<<<< HEAD\nA1\n=======\nB1\n>>>>>>> t\n" +
            "middle\n" +
            "<<<<<<< HEAD\nA2\n=======\nB2\n>>>>>>> t\n";
        var doc = ConflictDocument.Parse(text);
        Assert.Equal(2, doc.Hunks.Count);

        var rendered = doc.Render(new Dictionary<int, ConflictChoice> { [1] = ConflictChoice.Theirs });

        Assert.Equal(
            "<<<<<<< HEAD\nA1\n=======\nB1\n>>>>>>> t\n" +
            "middle\n" +
            "B2\n",
            rendered);
    }

    [Fact]
    public void ContentThatMerelyLooksLikeAMarkerIsContext()
    {
        // Markers are exactly seven identical characters followed by end-of-line or a
        // space. A line of eight, or one with the count right but text jammed against
        // it, is ordinary content — often inside a fenced code block in a README.
        var text = "<<<<<<<<\n=======x\nplain\n";
        var doc = ConflictDocument.Parse(text);

        Assert.False(doc.HasConflicts);
        Assert.Equal(text, doc.Render(new Dictionary<int, ConflictChoice>()));
    }

    [Fact]
    public void AnUnterminatedBlockDegradesToContextInsteadOfThrowing()
    {
        // A hand-mangled or truncated file. The competing bodies are kept as plain
        // context so the editor still shows them; only the opening marker line itself
        // is not re-emitted, since there is no conflict left to describe.
        var doc = ConflictDocument.Parse("<<<<<<< HEAD\nours\nstill ours\n");

        Assert.False(doc.HasConflicts);
        Assert.Equal("ours\nstill ours\n", doc.Render(new Dictionary<int, ConflictChoice>()));
    }
}

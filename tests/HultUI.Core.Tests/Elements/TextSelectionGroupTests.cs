using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// Cross-label selection held in model coordinates. The behaviour carried over from the old
/// element-anchored implementation is here (partial endpoints, full middles, direction
/// agnosticism, clearing); so are the cases that only the model form can satisfy — copying a
/// span whose lines were never realized, and a label recycled and rebuilt mid-selection.
/// </summary>
public class TextSelectionGroupTests
{
    private static TextSelectionGroup Group(params string?[] lines)
    {
        var g = new TextSelectionGroup();
        g.Bind("model", lines.Length, i => i >= 0 && i < lines.Length ? lines[i] : null);
        return g;
    }

    private static TextElement Label(TextSelectionGroup g, int index, string content)
    {
        var el = TextElement.New(content);
        el.Selectable = true;
        el.SelectionGroup = g;
        el.SelectionIndex = index;
        return el;
    }

    [Fact]
    public void EndpointsSelectPartially_AndTheMiddleFully()
    {
        var g = Group("line one", "line two", "line three");
        var a = Label(g, 0, "line one");
        var b = Label(g, 1, "line two");
        var c = Label(g, 2, "line three");

        g.Anchor(0, 5);
        g.ExtendTo(2, 4);

        Assert.Equal("one", a.SelectedText);       // from offset 5 to end
        Assert.Equal("line two", b.SelectedText);  // full middle line
        Assert.Equal("line", c.SelectedText);      // start to offset 4
    }

    [Fact]
    public void DraggingUpwardsSelectsTheSameSpanAsDraggingDown()
    {
        var g = Group("alpha", "beta", "gamma");
        var a = Label(g, 0, "alpha");
        var b = Label(g, 1, "beta");
        var c = Label(g, 2, "gamma");

        g.Anchor(0, 2); g.ExtendTo(2, 3);
        var forward = (a.SelectedText, b.SelectedText, c.SelectedText, g.SelectedText);

        g.Clear();
        g.Anchor(2, 3); g.ExtendTo(0, 2);   // dragged the other way

        Assert.Equal(forward, (a.SelectedText, b.SelectedText, c.SelectedText, g.SelectedText));
    }

    [Fact]
    public void ASelectionInsideOneLineLeavesTheOthersAlone()
    {
        var g = Group("alpha", "beta", "gamma");
        var a = Label(g, 0, "alpha");
        var b = Label(g, 1, "beta");
        var c = Label(g, 2, "gamma");

        g.Anchor(1, 1); g.ExtendTo(1, 3);

        Assert.False(a.HasSelection);
        Assert.Equal("et", b.SelectedText);
        Assert.False(c.HasSelection);
    }

    [Fact]
    public void TheCopiedTextJoinsTheSelectedLinesWithNewlines()
    {
        var g = Group("line one", "line two", "line three");
        g.Anchor(0, 5);
        g.ExtendTo(2, 4);

        Assert.Equal("one\nline two\nline", g.SelectedText);
    }

    // The whole point of the model form: the text comes from the model, so a copy covers
    // lines that have no element at all — which is every off-screen line once the view is
    // virtualized. The element-anchored version could only gather from live labels.
    [Fact]
    public void CopyingCoversLinesThatWereNeverRealized()
    {
        var lines = Enumerable.Range(0, 900).Select(i => $"line {i}").ToArray();
        var g = new TextSelectionGroup();
        g.Bind("big", lines.Length, i => lines[i]);

        // Only three labels exist — as if the viewport showed rows 0..2.
        _ = Label(g, 0, lines[0]);
        _ = Label(g, 1, lines[1]);
        _ = Label(g, 2, lines[2]);

        g.Anchor(0, 0);
        g.ExtendTo(899, lines[899].Length);

        var copied = g.SelectedText.Split('\n');
        Assert.Equal(900, copied.Length);
        Assert.Equal("line 0", copied[0]);
        Assert.Equal("line 899", copied[899]);
    }

    // A recycled row is destroyed and later rebuilt. The rebuilt label derives its highlight
    // from the group, so it comes back correct with nothing pushed into it.
    [Fact]
    public void ARebuiltLabelPicksItsHighlightBackUp()
    {
        var g = Group("first line", "second line", "third line");
        g.Anchor(0, 6);
        g.ExtendTo(2, 5);

        var rebuilt = Label(g, 1, "second line");   // created *after* the selection was made

        Assert.True(rebuilt.HasSelection);
        Assert.Equal("second line", rebuilt.SelectedText);
    }

    [Fact]
    public void ALabelOutsideTheSpanHasNoSelection()
    {
        var g = Group("a", "b", "c", "d");
        g.Anchor(1, 0);
        g.ExtendTo(2, 1);

        Assert.False(Label(g, 0, "a").HasSelection);
        Assert.True(Label(g, 1, "b").HasSelection);
        Assert.False(Label(g, 3, "d").HasSelection);
    }

    // A row the model has no line for — a collapsed run, or the blank half of a split diff
    // opposite an added line — takes no highlight and contributes nothing to the copy.
    [Fact]
    public void ARowWithNoLineIsSkippedEntirely()
    {
        var g = Group("top", null, "bottom");
        var blank = Label(g, 1, "");

        g.Anchor(0, 0);
        g.ExtendTo(2, 6);

        Assert.False(blank.HasSelection);
        Assert.Equal("top\nbottom", g.SelectedText);
    }

    // Distinct from the above: a real line that happens to be empty still copies, as the
    // blank line it is. The old implementation dropped it, because a label with no content
    // reported no selection and was skipped when gathering.
    [Fact]
    public void AnEmptyLineInTheMiddleStillCopiesAsABlankLine()
    {
        var g = Group("above", "", "below");

        g.Anchor(0, 0);
        g.ExtendTo(2, 5);

        Assert.Equal("above\n\nbelow", g.SelectedText);
    }

    [Fact]
    public void ClearingRemovesTheSelectionEverywhere()
    {
        var g = Group("a text", "b text");
        var a = Label(g, 0, "a text");
        var b = Label(g, 1, "b text");
        g.Anchor(0, 0); g.ExtendTo(1, 6);
        Assert.True(a.HasSelection && b.HasSelection);

        g.Clear();

        Assert.False(a.HasSelection);
        Assert.False(b.HasSelection);
        Assert.Equal("", g.SelectedText);
        Assert.False(g.HasSelection);
    }

    [Fact]
    public void RebindingADifferentModelDropsTheSelection()
    {
        var g = Group("one", "two");
        g.Anchor(0, 0); g.ExtendTo(1, 3);
        Assert.True(g.HasSelection);

        g.Bind("another model", 2, i => "xx");

        Assert.False(g.HasSelection);
    }

    [Fact]
    public void RebindingTheSameModelKeepsTheSelectionThroughARebuild()
    {
        var g = Group("one", "two");
        g.Anchor(0, 1); g.ExtendTo(1, 2);

        g.Bind("model", 2, i => i == 0 ? "one" : "two");   // same key: a plain rebuild

        Assert.True(g.HasSelection);
        Assert.Equal("ne\ntw", g.SelectedText);
    }

    [Fact]
    public void AnOffsetPastTheEndOfAShorterLabelIsClamped()
    {
        var g = Group("a very long line", "hi");
        var shortLabel = Label(g, 1, "hi");

        g.Anchor(0, 0);
        g.ExtendTo(1, 99);           // beyond "hi"

        Assert.Equal("hi", shortLabel.SelectedText);
    }

    [Fact]
    public void SelectAllCoversTheWholeModel()
    {
        var g = Group("one", "two", "three");

        g.SelectAll();

        Assert.Equal("one\ntwo\nthree", g.SelectedText);
    }

    [Fact]
    public void AnUngroupedLabelKeepsItsOwnSelection()
    {
        var el = TextElement.New("standalone");
        el.Selectable = true;

        Assert.False(el.HasSelection);
        Assert.Equal(-1, el.SelectionIndex);
    }
}

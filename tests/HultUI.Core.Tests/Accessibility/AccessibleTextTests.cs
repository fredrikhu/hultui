using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

/// <summary>Seam A1's model: lines, words and characters a reader moves through, the boxes it
/// reads them from, and the differ telling a bridge the text or the caret changed.</summary>
public class AccessibleTextTests
{
    /// <summary>A fixed grid: every character 10 wide, every line 20 tall, from (100, 50).</summary>
    private sealed class Grid : AccessibleTextGeometry
    {
        public override Rect CharacterBounds(AccessibleText text, int offset)
        {
            int line = text.LineAt(offset);
            int column = offset - text.LineStart(line);
            bool atEnd = offset >= text.LineEnd(line);
            return new Rect(100 + column * 10, 50 + line * 20, atEnd ? 0 : 10, 20);
        }

        public override int OffsetAt(AccessibleText text, Point point)
        {
            int line = (int)((point.Y - 50) / 20);
            if (point.Y < 50 || line >= text.LineCount) return -1;
            int column = (int)((point.X - 100) / 10);
            return Math.Clamp(text.LineStart(line) + Math.Max(0, column), text.LineStart(line), text.LineEnd(line));
        }
    }

    [Fact]
    public void Lines_AreTheNewlineSeparatedOnes()
    {
        var t = new AccessibleText("ab\ncde\n\nf");
        Assert.Equal(4, t.LineCount);
        // A line's newline is on that line.
        Assert.Equal([0, 0, 0, 1, 1, 1, 1, 2, 3, 3], Enumerable.Range(0, t.Length + 1).Select(t.LineAt));
        Assert.Equal((3, 7), t.LineRange(1)); // with its newline
        Assert.Equal(6, t.LineEnd(1));
        Assert.Equal((7, 8), t.LineRange(2));
        Assert.Equal((8, 9), t.LineRange(3));
    }

    [Fact]
    public void Words_RunToTheNextWord_BlanksAndLineBreaksIncluded()
    {
        var t = new AccessibleText("ls -la  /tmp\nok");
        // "-" and "la" are different kinds, so two words.
        Assert.Equal((0, 3), t.WordRange(0));   // "ls "
        Assert.Equal((0, 3), t.WordRange(2));   // in the blank after it
        Assert.Equal((3, 4), t.WordRange(3));   // "-"
        Assert.Equal((4, 8), t.WordRange(5));   // "la  "
        Assert.Equal((8, 9), t.WordRange(8));   // "/"
        Assert.Equal((9, 13), t.WordRange(10)); // "tmp\n"
        Assert.Equal((13, 15), t.WordRange(14)); // "ok"

        Assert.Equal(3, t.NextWordStart(0));
        Assert.Equal(13, t.NextWordStart(9));
        Assert.Equal(9, t.PreviousWordStart(13));  // from a word's start: the one before
        Assert.Equal(9, t.PreviousWordStart(11));  // from inside: its own start
        Assert.Equal(0, t.PreviousWordStart(0));
    }

    [Fact]
    public void LeadingBlanks_AreAWordOfTheirOwn()
    {
        var t = new AccessibleText("  x");
        Assert.Equal((0, 2), t.WordRange(1));
        Assert.Equal((2, 3), t.WordRange(2));
    }

    [Fact]
    public void ASurrogatePair_IsOneCharacter()
    {
        var t = new AccessibleText("a𝐀b");
        Assert.Equal(1, t.NextCharacter(0));
        Assert.Equal(3, t.NextCharacter(1));
        Assert.Equal(1, t.PreviousCharacter(3));
        Assert.Equal(4, t.NextCharacter(3));
    }

    [Fact]
    public void ARange_HasABoxPerLine_FromItsFirstToItsLastCharacter()
    {
        var t = new AccessibleText("abc\ndefg", geometry: new Grid());
        Assert.Equal([new Rect(110, 50, 20, 20)], t.RangeBounds(1, 3));
        Assert.Equal([new Rect(120, 50, 10, 20), new Rect(100, 70, 20, 20)], t.RangeBounds(2, 6));
        // A range ending where a line starts does not reach into it.
        Assert.Equal([new Rect(100, 50, 30, 20)], t.RangeBounds(0, 4));
        // An empty range is a box of no width where a caret there stands.
        Assert.Equal([new Rect(110, 70, 0, 20)], t.RangeBounds(5, 5));
        Assert.Equal(6, t.OffsetAt(new Point(125, 75)));
        Assert.Equal(-1, t.OffsetAt(new Point(125, 10)));
        Assert.Empty(new AccessibleText("abc").RangeBounds(0, 2)); // no geometry, no boxes
    }

    [Fact]
    public void TheSelection_IsOrdered_AndEmptyIsNone()
    {
        var t = new AccessibleText("hello", caret: 9, selectionStart: 4, selectionEnd: 1);
        Assert.Equal((1, 4, true), (t.SelectionStart, t.SelectionEnd, t.HasSelection));
        Assert.Equal(5, t.Caret); // clamped
        Assert.False(new AccessibleText("hello", selectionStart: 2, selectionEnd: 2).HasSelection);
    }

    // ──── The differ ────

    private sealed class Texty : BoxElement
    {
        private sealed class Source(Texty owner) : IAccessibleTextSource
        {
            public AccessibleText? AccessibleText => owner.Current;
        }

        public AccessibleText? Current { get; set; }
        internal override IAccessibleTextSource? AccessibleTextSource => new Source(this);
    }

    private static readonly BoxElement Root = BoxElement.New();

    /// <summary>The tree of <paramref name="element"/> under one root, so successive trees
    /// differ by property rather than by a replaced root.</summary>
    private static AccessibilityNode Build(Texty element)
    {
        if (element.Parent is null) Root.AddChild(element);
        return AccessibilityTreeBuilder.Build(Root);
    }

    [Fact]
    public void TheDiffer_TellsTheTextAndTheCaretApart_AndKeepsTheOldText()
    {
        var element = new Texty { AccessibleName = "t", Current = new AccessibleText("abc", caret: 3) };
        var before = Build(element);

        element.Current = new AccessibleText("abc", caret: 1);
        var caretMoved = AccessibilityTreeDiffer.Diff(before, Build(element));
        Assert.Equal([AccessibilityProperties.TextSelection], caretMoved.PropertyChanged.Select(p => p.PropertyName));

        var middle = Build(element);
        element.Current = new AccessibleText("abXc", caret: 1);
        var typed = AccessibilityTreeDiffer.Diff(middle, Build(element));
        var change = Assert.Single(typed.PropertyChanged);
        Assert.Equal(AccessibilityProperties.Text, change.PropertyName);
        Assert.Equal("abc", change.Previous!.Text!.Text);
        Assert.Equal("abXc", change.Node.Text!.Text);

        // The same snapshot again: nothing.
        var same = Build(element);
        Assert.Empty(AccessibilityTreeDiffer.Diff(same, Build(element)).PropertyChanged);
    }
}

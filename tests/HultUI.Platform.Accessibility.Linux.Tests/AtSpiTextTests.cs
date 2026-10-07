using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Linux;
using Xunit;

namespace HultUI.Platform.Accessibility.Linux.Tests;

/// <summary>Seam A1 on Linux: AT-SPI's Text answers over an element's text, and the
/// insert/delete a change is told as. No bus: the answers are plain functions.</summary>
public class AtSpiTextTests
{
    private sealed class Grid : AccessibleTextGeometry
    {
        public override Rect CharacterBounds(AccessibleText text, int offset)
        {
            int line = text.LineAt(offset);
            int column = offset - text.LineStart(line);
            return new Rect(10 + column * 8, 20 + line * 16, offset >= text.LineEnd(line) ? 0 : 8, 16);
        }

        public override int OffsetAt(AccessibleText text, Point point)
        {
            int line = (int)((point.Y - 20) / 16);
            if (point.Y < 20 || line >= text.LineCount) return -1;
            return Math.Clamp(text.LineStart(line) + (int)((point.X - 10) / 8), text.LineStart(line), text.LineEnd(line));
        }
    }

    private static readonly AccessibleText Screen = new("$ ls -la\nfile.txt\n$ ", caret: 20, geometry: new Grid());

    [Fact]
    public void Units_AreCharactersWordsAndLines()
    {
        Assert.Equal(("l", 2, 3), AtSpiText.GetStringAtOffset(Screen, 2, AtSpiConstants.GranularityChar));
        Assert.Equal(("ls ", 2, 5), AtSpiText.GetStringAtOffset(Screen, 3, AtSpiConstants.GranularityWord));
        Assert.Equal(("file.txt\n", 9, 18), AtSpiText.GetStringAtOffset(Screen, 12, AtSpiConstants.GranularityLine));
        // A paragraph is a line in text that does not wrap.
        Assert.Equal(("file.txt\n", 9, 18), AtSpiText.GetStringAtOffset(Screen, 12, AtSpiConstants.GranularityParagraph));
        // In text that does, a line is what is drawn and a paragraph what a newline ends.
        var wrapped = new AccessibleText("one two\nthree", lineStarts: [0, 4]);
        Assert.Equal(("one ", 0, 4), AtSpiText.GetStringAtOffset(wrapped, 1, AtSpiConstants.GranularityLine));
        Assert.Equal(("one two\n", 0, 8), AtSpiText.GetStringAtOffset(wrapped, 1, AtSpiConstants.GranularityParagraph));
        Assert.Equal("ls -la", AtSpiText.GetText(Screen, 2, 8));
        Assert.Equal("$ ", AtSpiText.GetText(Screen, 18, -1));
    }

    [Fact]
    public void BeforeAndAfter_AreTheNeighbouringUnits()
    {
        Assert.Equal(("$ ls -la\n", 0, 9), AtSpiText.GetTextRelative(Screen, 12, AtSpiConstants.BoundaryLineStart, -1));
        Assert.Equal(("$ ", 18, 20), AtSpiText.GetTextRelative(Screen, 12, AtSpiConstants.BoundaryLineStart, 1));
        Assert.Equal(("", 0, 0), AtSpiText.GetTextRelative(Screen, 3, AtSpiConstants.BoundaryLineStart, -1));
    }

    [Fact]
    public void Extents_AreDevicePixels_AndAPointFindsItsCharacter()
    {
        Assert.Equal((15, 30, 12, 24), AtSpiText.GetCharacterExtents(Screen, 0, 1.5f));
        // file.txt on line 1: x 10..74, y 36..52.
        Assert.Equal((10, 36, 64, 16), AtSpiText.GetRangeExtents(Screen, 9, 17, 1f));
        Assert.Equal(11, AtSpiText.GetOffsetAtPoint(Screen, 27 * 3 / 2, 40 * 3 / 2, 1.5f));
        Assert.Equal('$', AtSpiText.GetCharacterAtOffset(Screen, 0));
        Assert.Equal(0x1D400, AtSpiText.GetCharacterAtOffset(new AccessibleText("𝐀"), 0));
    }

    [Fact]
    public void AChange_IsOneDeletionAndOneInsertion_WhereTheTextsDiffer()
    {
        Assert.Equal((null, (4, "X")), AtSpiText.Difference("$ ls", "$ lsX"));
        Assert.Equal(((2, "ls"), null), AtSpiText.Difference("$ ls", "$ "));
        Assert.Equal(((2, "a"), (2, "bc")), AtSpiText.Difference("$ a!", "$ bc!"));
        Assert.Equal((null, null), AtSpiText.Difference("same", "same"));
    }

    [Fact]
    public void AnObjectWithText_OffersTheTextInterface_AndAStringValueIsNotAValueInterface()
    {
        var root = BoxElement.New();
        var input = TextInputElement.New();
        input.Text = "hello";
        root.AddChild(input);
        var tree = AccessibilityTreeBuilder.Build(root);
        var acc = AtSpiAccessible.From(tree.Children[0], isRoot: false, AtSpiConstants.RootPath, 0, 1f);
        Assert.DoesNotContain(AtSpiConstants.ValueInterface, acc.Interfaces);
    }
}

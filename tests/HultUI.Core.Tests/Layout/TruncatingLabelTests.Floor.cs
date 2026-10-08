using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Layout;

public partial class TruncatingLabelTests
{
    // ── Its automatic minimum is 0 ──
    //
    // A label that truncates has no width below which its text breaks: it shows less of it. So
    // a row that must shrink it takes it down to nothing if it has to, as CSS does an item with
    // `overflow: hidden`, rather than stopping at its widest word and drawing the rest over its
    // neighbours. A box holding it can shrink past it too.

    /// <summary>A label in a row of 100 beside a 70 px box that keeps its width.</summary>
    private static (TextElement Label, Element Rigid) Squeezed(TextElement label, float rowWidth = 100)
    {
        var rigid = Box.New().Width(Dimension.Px(70)).Height(Dimension.Px(10)).FlexShrink(0);
        var root = Flex.Column().AlignItems(AlignItems.FlexStart).Children(
            Flex.Row().Width(Dimension.Px(rowWidth)).Children(label, rigid));
        new LayoutEngine(new EllipsisMeasureContext()).PerformLayout(root, 300, 200);
        return (label, rigid);
    }

    [Fact]
    public void ATruncatingLabelShrinksPastItsWidestWord()
    {
        // "wonderfully" is 77 px; the room left is 30.
        var (label, rigid) = Squeezed(Truncating("a wonderfully long word"));

        Assert.Equal(30, label.Layout.Width);
        Assert.Equal(30, rigid.Layout.X);
    }

    [Fact]
    public void AClippedLabelShrinksPastItsWidestWord()
    {
        var (label, _) = Squeezed(Truncating("a wonderfully long word", TextOverflow.Clip));

        Assert.Equal(30, label.Layout.Width);
    }

    [Fact]
    public void ATruncatingLabelWithNoRoomLeftTakesNone()
    {
        // The neighbour starts where the row does: the label is not pushed out over it.
        var (label, rigid) = Squeezed(Truncating("a wonderfully long word"), rowWidth: 70);

        Assert.Equal(0, label.Layout.Width);
        Assert.Equal(0, rigid.Layout.X);
    }

    [Fact]
    public void ATruncatingLabelKeepsItsPadding()
    {
        // Its content goes; the padding is the box's own, as for any item shrunk to its floor.
        var (label, _) = Squeezed(Truncating("a wonderfully long word").Padding(new Thickness(0, 6)), rowWidth: 70);

        Assert.Equal(12, label.Layout.Width);
    }

    [Fact]
    public void ATruncatingLabelWithAMinWidthShrinksNoFurtherThanIt()
    {
        var (label, _) = Squeezed(Truncating("a wonderfully long word").MinWidth(Dimension.Px(50)));

        Assert.Equal(50, label.Layout.Width);
    }

    [Fact]
    public void ALabelOfTwoLinesStillStopsAtItsWidestWord()
    {
        // It wraps, so its widest word is still where it breaks.
        var label = Text.New("a wonderfully long word").MaxLines(2).TextOverflow(TextOverflow.Ellipsis);
        var (shrunk, _) = Squeezed(label);

        Assert.Equal(77, shrunk.Layout.Width);
    }

    [Fact]
    public void ABoxHoldingATruncatingLabelShrinksPastItToo()
    {
        // Meridian's URL bar: a chip and a path in a box that takes the slack, then the Send
        // button. The path is one 231 px word; held to it, the box pushed the button out of the
        // row. The box's floor is now the chip alone.
        var label = Truncating("C:/Users/fredr/source/repos/hului");
        var chip = Box.New().Width(Dimension.Px(50)).Height(Dimension.Px(10)).FlexShrink(0);
        var urlBox = Flex.Row().FlexGrow(1).Children(chip, label);
        var send = Box.New().Width(Dimension.Px(40)).Height(Dimension.Px(10)).FlexShrink(0);
        var root = Flex.Column().AlignItems(AlignItems.FlexStart).Children(
            Flex.Row().Width(Dimension.Px(200)).Children(urlBox, send));
        new LayoutEngine(new EllipsisMeasureContext()).PerformLayout(root, 300, 200);

        Assert.Equal(160, urlBox.Layout.Width);
        Assert.Equal(110, label.Layout.Width);
        Assert.Equal(160, send.Layout.X);
    }

    [Fact]
    public void ATruncatingLabelInAShrinkingColumnKeepsItsLineHeight()
    {
        // Down a column nothing changes: it truncates across, and its one line is its floor.
        var label = Truncating(Long);
        var tall = Box.New().Height(Dimension.Px(30));
        var root = Flex.Column().AlignItems(AlignItems.FlexStart).Children(
            Flex.Column().Width(Dimension.Px(100)).Height(Dimension.Px(30)).Children(label, tall));
        new LayoutEngine(new EllipsisMeasureContext()).PerformLayout(root, 300, 200);

        Assert.Equal(EllipsisMeasureContext.LineHeight, label.Layout.Height);
        Assert.Equal(30 - EllipsisMeasureContext.LineHeight, tall.Layout.Height, 3);
    }

    /// <summary>Counts the text a label draws; measures 7 px a character and the ellipsis 12.</summary>
    private sealed class DrawCounter : HultUI.Core.Tests.Elements.NullRenderContext
    {
        public int Draws;
        public override void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines) =>
            Draws++;
        public override Size MeasureText(string text, ResolvedInheritedStyle style) =>
            new(text == "…" ? 12f : text.Length * 7f, 14f);
    }

    [Theory]
    [InlineData("abcd abcd abcd", 0f, 0)]
    [InlineData("abcd abcd abcd", 5f, 0)]
    [InlineData("abcd abcd abcd", 12f, 1)]
    [InlineData("abcd abcd abcd", 40f, 1)]
    [InlineData("7", 7f, 1)]
    public void ALabelTooNarrowForItsEllipsisDrawsNothing(string text, float width, int draws)
    {
        // Drawn, the ellipsis alone would be wider than the box and lie over whatever is beside
        // it: a label with no room shows nothing. One with room for the ellipsis shows it, and
        // one whose text fits whole shows it, however narrow: "7" is narrower than an ellipsis.
        var label = Truncating(text).Width(Dimension.Px(width)).FontSize(14);
        var root = Flex.Row().Children(label);
        StyleResolver.Resolve(root);
        new LayoutEngine(new EllipsisMeasureContext()).PerformLayout(root, 300, 200);
        var ctx = new DrawCounter();

        label.Render(ctx);

        Assert.Equal(draws, ctx.Draws);
    }
}

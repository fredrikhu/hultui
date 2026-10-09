using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// A wrapping row inside an item whose width the parent decides (flex-basis 0, grow): the row's
/// height is that of the lines it breaks into at the width it is finally given, and everything
/// after it moves down by that much — never a second line drawn over the next sibling.
/// </summary>
public class WrapInGrowItemTests
{
    private readonly LayoutEngine _engine = new(new StubMeasureContext());

    /// <summary>
    /// A list card: a 3 px bar beside a text column (basis 0, grow) holding a title line and a
    /// wrapping meta row (two fixed labels, then a growing group that holds a count).
    /// </summary>
    private static (Element Card, Element Meta, Element Count) Card()
    {
        var count = Box.New().Width(70).Height(14).FlexShrink(0);
        var counts = Flex.Row(gap: 6).FlexGrow(1).FlexShrink(0).JustifyContent(JustifyContent.FlexEnd).Children(count);
        var meta = Flex.Row(gap: 6).FlexWrap(FlexWrap.Wrap).Children(
            Box.New().Width(80).Height(14).FlexShrink(0),
            Box.New().Width(90).Height(14).FlexShrink(0),
            counts);
        var text = Flex.Column(gap: 2).FlexGrow(1).FlexShrink(1).FlexBasis(0).Children(
            Box.New().Height(20),
            meta);
        var card = Flex.Row().AlignItems(AlignItems.Stretch).FlexShrink(0).Children(
            Box.New().Width(3).FlexShrink(0),
            text);
        return (card, meta, count);
    }

    [Fact]
    public void AWrappedLine_GrowsTheCard_AndPushesTheNextSiblingDown()
    {
        var (card, meta, count) = Card();
        var next = Box.New().Height(40).FlexShrink(0);
        var list = Flex.Column().Width(200).Children(card, next);
        _engine.PerformLayout(list, 800, 600);

        // 3 + 197 for the text column: 80 + 6 + 90 = 176 fits, the count (70) wraps.
        Assert.Equal(14 + 6 + 14, meta.Layout.Height, 1f);
        Assert.True(count.Layout.AbsoluteY >= meta.Layout.AbsoluteY + 14, $"count at {count.Layout.AbsoluteY}, meta at {meta.Layout.AbsoluteY}");
        Assert.True(card.Layout.Height >= 20 + 2 + 34 - 0.5f, $"card {card.Layout.Height} tall");
        Assert.True(next.Layout.AbsoluteY >= count.Layout.AbsoluteY + count.Layout.Height - 0.5f,
            $"next sibling at {next.Layout.AbsoluteY} overlaps the wrapped line ending at {count.Layout.AbsoluteY + count.Layout.Height}");
    }

    [Fact]
    public void AWrappedLine_InsideAScrollView_GrowsTheCard_AndPushesTheNextSiblingDown()
    {
        var (card, meta, count) = Card();
        meta.Margin(new Thickness(0, 0, 0, 17));
        var next = Box.New().Height(40).FlexShrink(0);
        var list = Flex.Column().Children(card, next);
        var panel = Flex.Column().Width(220).Height(400).Children(ScrollView.New().FlexGrow(1).FlexBasis(0).Children(list));
        _engine.PerformLayout(panel, 800, 600);

        Assert.True(count.Layout.AbsoluteY >= meta.Layout.AbsoluteY + 14, $"count at {count.Layout.AbsoluteY}, meta at {meta.Layout.AbsoluteY}");
        Assert.True(next.Layout.AbsoluteY >= count.Layout.AbsoluteY + count.Layout.Height - 0.5f,
            $"next sibling at {next.Layout.AbsoluteY} overlaps the wrapped line ending at {count.Layout.AbsoluteY + count.Layout.Height}");
    }

    [Fact]
    public void AWrappedLineOfText_GrowsTheCard_AndPushesTheNextSiblingDown()
    {
        var count = Text.New("1 se · 1 im").MaxLines(1).FlexShrink(0);
        var counts = Flex.Row(gap: 6).FlexGrow(1).FlexShrink(0).JustifyContent(JustifyContent.FlexEnd).Children(count);
        var meta = Flex.Row(gap: 6).FlexWrap(FlexWrap.Wrap).Margin(new Thickness(0, 0, 0, 17)).Children(
            Text.New("Older 2").MaxLines(1).FlexShrink(0),
            Text.New("1 h 49 min earlier").MaxLines(1).FlexShrink(0),
            counts);
        var text = Flex.Column(gap: 2).FlexGrow(1).FlexShrink(1).FlexBasis(0).Children(Text.New("XR CHEST AP PORTABLE").MaxLines(1), meta);
        var card = Flex.Row().AlignItems(AlignItems.Stretch).FlexShrink(0).Children(Box.New().Width(3).FlexShrink(0), text);
        var next = Box.New().Height(40).FlexShrink(0);
        var list = Flex.Column().Children(card, next);
        var panel = Flex.Column().Width(200).Height(400).Children(ScrollView.New().FlexGrow(1).FlexBasis(0).Children(list));
        _engine.PerformLayout(panel, 800, 600);

        float lineH = meta.Children[0].Layout.Height;
        bool wrapped = count.Layout.AbsoluteY >= meta.Layout.AbsoluteY + lineH - 0.5f;
        Assert.True(wrapped, $"meta {meta.Layout.Width} wide: '{meta.Children[0].Layout.Width}' + '{meta.Children[1].Layout.Width}' + count {count.Layout.Width} should wrap");
        Assert.True(next.Layout.AbsoluteY >= count.Layout.AbsoluteY + count.Layout.Height - 0.5f,
            $"next sibling at {next.Layout.AbsoluteY} overlaps the wrapped line ending at {count.Layout.AbsoluteY + count.Layout.Height}; "
            + $"meta {meta.Layout.Width}x{meta.Layout.Height} at {meta.Layout.AbsoluteY}, card {card.Layout.Width}x{card.Layout.Height}");
    }

    [Fact]
    public void AWrappingRowWithAMargin_IsMeasuredAtTheWidthItIsLaidOutAt()
    {
        // 95 + 95 fits the column's 200 but not the 180 inside the row's margin: two lines.
        var a = Box.New().Width(95).Height(10).FlexShrink(0);
        var b = Box.New().Width(95).Height(10).FlexShrink(0);
        var row = Flex.Row().FlexWrap(FlexWrap.Wrap).Margin(new Thickness(0, 0, 0, 20)).Children(a, b);
        var next = Box.New().Height(10).FlexShrink(0);
        var column = Flex.Column().Width(200).Children(row, next);
        _engine.PerformLayout(column, 800, 600);

        Assert.Equal(180, row.Layout.Width, 1f);
        Assert.Equal(20, row.Layout.Height, 1f);
        Assert.Equal(10, b.Layout.AbsoluteY, 1f);
        Assert.Equal(20, next.Layout.AbsoluteY, 1f);
    }

    [Fact]
    public void AMarginIsOutsideTheRoom_ButAPercentageStillReadsTheWholeOfIt()
    {
        var half = Box.New().Width(Dimension.Percent(50)).Height(10).Margin(new Thickness(0, 0, 0, 20));
        var column = Flex.Column().Width(200).AlignItems(AlignItems.FlexStart).Children(half);
        _engine.PerformLayout(column, 800, 600);
        Assert.Equal(100, half.Layout.Width, 1f);
    }

    [Fact]
    public void WhenEverythingFits_OneLine()
    {
        var (card, meta, count) = Card();
        var list = Flex.Column().Width(300).Children(card);
        _engine.PerformLayout(list, 800, 600);
        Assert.Equal(14, meta.Layout.Height, 1f);
        Assert.Equal(meta.Layout.AbsoluteY, count.Layout.AbsoluteY, 1f);
        // Right-aligned on the line.
        Assert.Equal(300, count.Layout.AbsoluteX + count.Layout.Width, 1f);
    }
}

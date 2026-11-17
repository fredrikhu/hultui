using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Layout;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// A row item of auto basis and width whose content spreads with the room offered (a row holding a
/// growing child) takes its max-content width as its base, as in CSS. It was measured at all the
/// room the row offered and came out that wide: a sibling beside it was pushed past the row's end,
/// or squeezed to nothing. Growing still happens where the item is given room: stretched in a
/// column, as the root, growing itself, or given a width. Text: 7 px a character, 14 px a line.
/// </summary>
public class ContentBasedRowItemTests
{
    private readonly LayoutEngine _engine = new(new StubMeasureContext());

    /// <summary>The report's tree: a row of FlexShrink(0) holding a spacer and "x", beside "y".</summary>
    private static (Element Root, Element Inner, Element Spacer, Element X, Element Y) Repro()
    {
        var spacer = Flex.Row().FlexGrow(1);
        var x = Text.New("x");
        var inner = Flex.Row().FlexShrink(0).Children(spacer, x);
        var y = Text.New("y");
        return (Flex.Row().AlignItems(AlignItems.FlexStart).Children(inner, y), inner, spacer, x, y);
    }

    [Fact]
    public void ARowItemHoldingAGrowingChild_IsAsWideAsItsContent()
    {
        var (root, inner, spacer, x, y) = Repro();

        _engine.PerformLayout(root, 400, 300);

        Assert.Equal(7, inner.Layout.Width, 0.01f);
        Assert.Equal(0, spacer.Layout.Width, 0.01f);
        Assert.Equal(7, y.Layout.AbsoluteX, 0.01f);
        Assert.Equal(14, inner.Layout.Height, 0.01f);
    }

    [Fact]
    public void ASiblingIsNoLongerSqueezedToNothing()
    {
        var inner = Flex.Row().FlexShrink(0).Children(Flex.Row().FlexGrow(1), Text.New("x"));
        var box = Box.New().Width(50).Height(10);
        var root = Flex.Row().Children(inner, box);

        _engine.PerformLayout(root, 400, 300);

        Assert.Equal(7, inner.Layout.Width, 0.01f);
        Assert.Equal(50, box.Layout.Width, 0.01f);
    }

    /// <summary>A FlexBasis(0) growing label counts at its content, not at nothing: measured at
    /// zero width it would wrap a word to a line.</summary>
    [Fact]
    public void AGrowingLabelOfBasisZero_CountsAtItsContent()
    {
        var label = Text.New("hello world").FlexGrow(1).FlexBasis(0);
        var inner = Flex.Row().Children(label, Box.New().Width(16).Height(10));
        var root = Flex.Row().AlignItems(AlignItems.FlexStart).Children(inner);

        _engine.PerformLayout(root, 400, 300);

        Assert.Equal(77 + 16, inner.Layout.Width, 0.01f);
        Assert.Equal(77, label.Layout.Width, 0.01f);
        Assert.Equal(14, label.Layout.Height, 0.01f);
    }

    /// <summary>A column or a component host between the rows passes it on.</summary>
    [Fact]
    public void ThroughAColumnOrAComponent_TheItemStillHugsItsContent()
    {
        var spreading = Flex.Row().Children(Flex.Row().FlexGrow(1), Text.New("x"));
        var column = Flex.Column().Children(spreading);
        var root = Flex.Row().Children(column, Text.New("y"));
        _engine.PerformLayout(root, 400, 300);
        Assert.Equal(7, column.Layout.Width, 0.01f);

        var host = new Wrapper().Mount();
        var root2 = Flex.Row().Children(host, Text.New("y"));
        _engine.PerformLayout(root2, 400, 300);
        Assert.Equal(7, host.Layout.Width, 0.01f);
    }

    private sealed class Wrapper : Component
    {
        public override Element Build() => Flex.Row().Children(Flex.Row().FlexGrow(1), Text.New("x"));
    }

    /// <summary>Given room, the growing child still takes it.</summary>
    [Fact]
    public void GivenRoom_TheGrowingChildStillGrows()
    {
        // Stretched across a column of set width.
        var spacer1 = Flex.Row().FlexGrow(1);
        var col = Flex.Column().Width(300).Children(Flex.Row().Children(spacer1, Text.New("x")));
        _engine.PerformLayout(col, 400, 300);
        Assert.Equal(293, spacer1.Layout.Width, 0.01f);

        // As the root.
        var spacer2 = Flex.Row().FlexGrow(1);
        _engine.PerformLayout(Flex.Row().Children(spacer2, Text.New("x")), 400, 300);
        Assert.Equal(393, spacer2.Layout.Width, 0.01f);

        // Growing itself in the row.
        var spacer3 = Flex.Row().FlexGrow(1);
        var growing = Flex.Row().FlexGrow(1).Children(spacer3, Text.New("x"));
        _engine.PerformLayout(Flex.Row().Children(growing, Text.New("y")), 400, 300);
        Assert.Equal(393, growing.Layout.Width, 0.01f);
        Assert.Equal(386, spacer3.Layout.Width, 0.01f);

        // Given a width.
        var spacer4 = Flex.Row().FlexGrow(1);
        _engine.PerformLayout(Flex.Row().Children(Flex.Row().Width(200).Children(spacer4, Text.New("x"))), 400, 300);
        Assert.Equal(193, spacer4.Layout.Width, 0.01f);
    }

    /// <summary>Two growing items whose content spreads share the free room from their content, as
    /// CSS's auto basis does: each was based at the whole row and they came out even.</summary>
    [Fact]
    public void TwoSuchGrowingItems_ShareTheRoomFromTheirContent()
    {
        var a = Flex.Row().FlexGrow(1).Children(Flex.Row().FlexGrow(1), Text.New("aaaa"));
        var b = Flex.Row().FlexGrow(1).Children(Flex.Row().FlexGrow(1), Text.New("b"));
        _engine.PerformLayout(Flex.Row().Children(a, b), 400, 300);

        float free = 400 - 28 - 7;
        Assert.Equal(28 + free / 2, a.Layout.Width, 0.01f);
        Assert.Equal(7 + free / 2, b.Layout.Width, 0.01f);
    }

    /// <summary>Content whose width is not known without measuring at a width keeps the measurement
    /// made before: a scroll view inside, so two such panes still split evenly.</summary>
    [Fact]
    public void AScrollViewInside_KeepsTheMeasurementMadeBefore()
    {
        Element Pane() => Flex.Column().FlexGrow(1).Children(
            Flex.Row().Children(Flex.Row().FlexGrow(1), Text.New("title")),
            ScrollView.New().Height(100));
        var a = Pane();
        var b = Pane();
        _engine.PerformLayout(Flex.Row().Children(a, b), 400, 300);

        Assert.Equal(200, a.Layout.Width, 0.01f);
        Assert.Equal(200, b.Layout.Width, 0.01f);
    }

    /// <summary>An item of set height whose base came from its content is laid out at that height:
    /// its height is measured once flexing gave it its width, set or not.</summary>
    [Fact]
    public void AnItemOfSetHeight_KeepsItsHeight()
    {
        var inner = Flex.Row().Height(30).FlexShrink(0).Children(Flex.Row().FlexGrow(1), Text.New("x"));
        _engine.PerformLayout(Flex.Row().Children(inner, Text.New("y")), 400, 300);

        Assert.Equal(7, inner.Layout.Width, 0.01f);
        Assert.Equal(30, inner.Layout.Height, 0.01f);
    }

    /// <summary>The content-based base is kept like the min-content width, and let go with it when
    /// the content changes.</summary>
    [Fact]
    public void AChangeOfTheContent_ChangesTheBase()
    {
        var (root, inner, _, x, _) = Repro();
        _engine.PerformLayout(root, 400, 300);
        Assert.Equal(7, inner.Layout.Width, 0.01f);

        ((TextElement)x).Content = "xxxx";
        _engine.PerformLayout(root, 400, 300);

        Assert.Equal(28, inner.Layout.Width, 0.01f);
    }

    /// <summary>The row holding such an item is measured once and kept (its column measures it): the
    /// measurement holds only at widths where the item's content fits, and below them the row is
    /// measured again — the item's text wraps there, and a row kept from a wider pass would not.</summary>
    [Fact]
    public void AKeptMeasurementOfTheRow_HoldsOnlyWhereTheItemsContentFits()
    {
        (Element Column, Element Row, Element Label) Tree()
        {
            var label = Text.New("x x x x");
            var row = Flex.Row().AlignItems(AlignItems.FlexStart).Children(
                Flex.Row().Children(Flex.Row().FlexGrow(1), label), Text.New("y"));
            return (Flex.Column().AlignItems(AlignItems.FlexStart).Children(row), row, label);
        }

        var (column, row, label) = Tree();
        foreach (float w in new[] { 400f, 30f, 400f, 20f })
        {
            _engine.PerformLayout(column, w, 300);
            var (fresh, freshRow, freshLabel) = Tree();
            new LayoutEngine(new StubMeasureContext()).PerformLayout(fresh, w, 300);
            Assert.Equal(freshRow.Layout.Width, row.Layout.Width, 0.001f);
            Assert.Equal(freshRow.Layout.Height, row.Layout.Height, 0.001f);
            Assert.Equal(freshLabel.Layout.Height, label.Layout.Height, 0.001f);
        }
    }

    /// <summary>A one-line truncating label in such an item keeps its pinned measurement beside the
    /// max-content width now kept with it (MeasureCache: the pin's bits and the new flags share a
    /// byte), through widths where it truncates and where it does not.</summary>
    [Fact]
    public void ATruncatingLabelInSuchAnItem_LaysOutAsIfFresh_ThroughARunOfWidths()
    {
        (Element Root, Element Label) Tree()
        {
            var label = Text.New("a long title that truncates").MaxLines(1);
            var inner = Flex.Row().Children(Flex.Row().FlexGrow(1), label);
            return (Flex.Row().AlignItems(AlignItems.FlexStart).Children(inner, Text.New("y")), label);
        }

        var (root, label) = Tree();
        foreach (float w in new[] { 400f, 60f, 400f, 61f, 30f, 400f })
        {
            _engine.PerformLayout(root, w, 300);
            var (fresh, freshLabel) = Tree();
            new LayoutEngine(new StubMeasureContext()).PerformLayout(fresh, w, 300);
            Assert.Equal(freshLabel.Layout.Width, label.Layout.Width, 0.001f);
            Assert.Equal(freshLabel.Layout.AbsoluteX, label.Layout.AbsoluteX, 0.001f);
        }
    }

    /// <summary>Laid out through a run of widths, the tree comes out as if laid out fresh at each:
    /// the base holds wherever it fits, and the measurement before it where it does not.</summary>
    [Fact]
    public void ThroughARunOfWidths_TheTreeLaysOutAsIfFresh()
    {
        var (root, inner, _, _, y) = Repro();
        foreach (float w in new[] { 400f, 401f, 300f, 10f, 3f, 400f })
        {
            _engine.PerformLayout(root, w, 300);
            var (fresh, freshInner, _, _, freshY) = Repro();
            new LayoutEngine(new StubMeasureContext()).PerformLayout(fresh, w, 300);
            Assert.Equal(freshInner.Layout.Width, inner.Layout.Width, 0.001f);
            Assert.Equal(freshY.Layout.AbsoluteX, y.Layout.AbsoluteX, 0.001f);
        }
    }
}

using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// Flex shrinking: what compresses, what refuses to, and where the boundary
/// between "scrolls" and "shrinks" sits.
/// </summary>
public class FlexShrinkTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private static BoxElement Box(float? height = null)
    {
        var box = BoxElement.New();
        if (height is { } h) box.Box.Height = h;
        return box;
    }

    private static ScrollViewElement Scroll(float height)
    {
        var sv = ScrollViewElement.New();
        sv.Box.Height = height;
        return sv;
    }

    [Fact]
    public void ScrollView_DirectChild_KeepsItsHeight()
    {
        var tall = Box(400);
        var scroll = Scroll(100); scroll.AddChild(tall);

        _layout.PerformLayout(scroll, 200, 200);

        Assert.Equal(400, tall.Layout.Height);
    }

    [Fact]
    public void ScrollView_NestedContainer_KeepsItsHeight()
    {
        // The indefinite main axis has to survive one more level of nesting:
        // a plain column inside the scroll view used to resolve against the
        // viewport and crush its own children to fit.
        var tall = Box(400);
        var inner = BoxElement.New(); inner.AddChild(tall);
        var scroll = Scroll(100); scroll.AddChild(inner);

        _layout.PerformLayout(scroll, 200, 200);

        Assert.Equal(400, tall.Layout.Height);
        Assert.Equal(400, inner.Layout.Height);
    }

    [Fact]
    public void ScrollView_NestedContainer_MultipleChildren_KeepTheirHeights()
    {
        var first = Box(40);
        var second = Box(400);
        var inner = BoxElement.New();
        inner.AddChild(first);
        inner.AddChild(second);
        var scroll = Scroll(100); scroll.AddChild(inner);

        _layout.PerformLayout(scroll, 200, 200);

        Assert.Equal(40, first.Layout.Height);
        Assert.Equal(400, second.Layout.Height);
    }

    [Fact]
    public void BoundedContainer_StillShrinksExplicitlySizedItems()
    {
        // CSS parity: a fixed-height item in an overflowing flex column does
        // compress — which is why flex-shrink:0 is such a common idiom. This
        // must not change.
        var tall = Box(400);
        var container = Box(100); container.AddChild(tall);

        _layout.PerformLayout(container, 200, 200);

        Assert.Equal(100, tall.Layout.Height);
    }

    [Fact(Skip = "Automatic minimum size for flex items is not implemented — needs a real min-content pass.")]
    public void BoundedContainer_DoesNotCrushContentBelowItsMinimum()
    {
        // CSS min-height:auto — an auto-sized item should not be compressed below
        // the size its own content needs; today it is crushed to the container.
        //
        // Approximating the content minimum with the hypothetical measurement
        // taken during flex Phase 1 does NOT work: for a container that
        // measurement is the full content extent rather than the min-content
        // size, so an ancestor wrapping a scroll view gets pinned to the entire
        // scrollable height — the viewport collapses, culling stops working, and
        // every row is painted. Implementing this needs a genuine recursive
        // min-content measurement (where a scroll container contributes zero).
        var text = TextElement.New("Some content");
        var card = BoxElement.New(); card.AddChild(text);
        var container = Box(10); container.AddChild(card);

        _layout.PerformLayout(container, 200, 200);

        Assert.Equal(14, card.Layout.Height);
    }

    [Fact]
    public void FlexShrinkZero_StillOptsOut()
    {
        var tall = Box(400);
        tall.Flex.FlexShrink = 0;
        var container = Box(100); container.AddChild(tall);

        _layout.PerformLayout(container, 200, 200);

        Assert.Equal(400, tall.Layout.Height);
    }

    [Fact]
    public void Row_ItemsStillCompress()
    {
        // The content-based minimum is applied on the column axis only: along a
        // row an item's true min-content width is its longest word, not its
        // measured single-line width, so clamping to the measurement would stop
        // labels compressing. Guards against that regressing.
        var wide = BoxElement.New();
        wide.Box.Width = 400;
        var container = BoxElement.New();
        container.Box.Width = 100;
        container.Flex.Direction = FlexDirection.Row;
        container.AddChild(wide);

        _layout.PerformLayout(container, 200, 200);

        Assert.Equal(100, wide.Layout.Width);
    }

    [Fact]
    public void PercentageHeight_InsideScrollView_FallsBackToContent()
    {
        // A percentage against an indefinite size is unresolvable and behaves as
        // auto (CSS). The danger this guards: resolving it against the unbounded
        // sentinel instead, which would produce an astronomically tall element.
        var child = BoxElement.New();
        child.Box.Height = Dimension.Percent(50);
        child.AddChild(Box(30));
        var scroll = Scroll(100); scroll.AddChild(child);

        _layout.PerformLayout(scroll, 200, 200);

        // Sized from content, not from the sentinel.
        Assert.Equal(30, child.Layout.Height);
        Assert.True(child.Layout.Height < 10_000,
            $"Percentage resolved against the unbounded sentinel: {child.Layout.Height}");
    }

    [Fact]
    public void PercentageHeight_InBoundedContainer_StillResolves()
    {
        // Regression guard for the change above: percentages must keep working
        // wherever the containing size is definite.
        var child = BoxElement.New();
        child.Box.Height = Dimension.Percent(50);
        var container = Box(200); container.AddChild(child);

        _layout.PerformLayout(container, 200, 200);

        Assert.Equal(100, child.Layout.Height);
    }
}

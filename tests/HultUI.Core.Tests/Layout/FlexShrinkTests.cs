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

    [Fact]
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
    public void AScrollViewInAncestors_DoesNotPinThemToItsContentHeight()
    {
        // The reverted approximation's failure shape (docs/plans/13-min-content.md):
        // the min-content floor must see a scroll container as ZERO, or an auto-sized
        // wrapper around one is pinned to the entire scrollable height — 14,640px
        // tall, viewport collapsed, culling dead. The floor exists to protect
        // content; a scroll view's content is, by design, allowed to not fit.
        // Real content, not an explicit height: 600 text rows at the stub's 14px
        // line height is an 8,400px min-content column — the floor must not see it
        // through the scroll view.
        var list = BoxElement.New();
        for (int i = 0; i < 600; i++)
            list.AddChild(TextElement.New($"row {i}"));
        var scroll = ScrollViewElement.New(); scroll.AddChild(list);
        var wrapper = BoxElement.New(); wrapper.AddChild(scroll);
        var sibling = Box(50); sibling.Flex.FlexShrink = 0;
        var window = Box(600);
        window.AddChild(wrapper);
        window.AddChild(sibling);

        _layout.PerformLayout(window, 800, 600);

        Assert.True(wrapper.Layout.Height <= 600,
            $"the scroll wrapper was pinned to {wrapper.Layout.Height}px — the floor "
            + "counted the scrollable content instead of zeroing the scroll container");
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

    // A row 100 wide holding a label that measures 100 and an unshrinkable 70px
    // sibling: 70px of overflow, all of which the label has to absorb. Where it
    // stops is the automatic minimum size, and for text that is the longest word —
    // "wonderfully", 11 chars = 77px in the stub's metrics — not the widest
    // character, which would let it collapse to 30.
    private (TextElement Label, BoxElement Row) SqueezedLabel(string text)
    {
        var label = TextElement.New(text);
        var rigid = TextElement.New("yyyyyyyyyy");
        rigid.Flex.FlexShrink = 0;

        var row = BoxElement.New();
        row.Flex.Direction = FlexDirection.Row;
        row.Box.Width = 100;
        row.AddChild(label);
        row.AddChild(rigid);
        return (label, row);
    }

    [Fact]
    public void ALabelOfASetWidthShrunkInARow_WrapsAtTheWidthItIsGiven()
    {
        // 19 characters, 133px on one line, asked to be 140 wide in a row of 100: shrunk to
        // 100, where the stub breaks it after 14 characters — two lines. Measured as if it were
        // the 140 it asked for, it would fit on one: a forced width is the unforced measurement
        // at that width only for a label sized from its content.
        var label = TextElement.New("aaaa bbbb cccc dddd");
        label.Box.Width = 140;
        var row = BoxElement.New();
        row.Flex.Direction = FlexDirection.Row;
        row.Flex.AlignItems = AlignItems.FlexStart;
        row.Box.Width = 100;
        row.AddChild(label);

        _layout.PerformLayout(row, 300, 200);

        Assert.Equal(100, label.Layout.Width);
        Assert.Equal(28, label.Layout.Height);
    }

    [Fact]
    public void AShrinkingLabel_StopsAtItsWidestWord()
    {
        var (label, row) = SqueezedLabel("a wonderfully long word");

        _layout.PerformLayout(row, 300, 200);

        Assert.Equal(77, label.Layout.Width);
    }

    [Fact]
    public void AShrinkingLabelOfShortWords_StillShrinksPastThem()
    {
        // The floor follows the content: nothing here is wider than 3 characters,
        // so this label gives way where the one above refuses to.
        var (label, row) = SqueezedLabel("a bb ccc dd e ff g hh i jj");

        _layout.PerformLayout(row, 300, 200);

        Assert.Equal(30, label.Layout.Width);
    }

    // The floor is kept between layouts (it reads no constraint), so it has to go when the
    // content does — both ways, and through a container whose floor is its child's.
    [Fact]
    public void AShrinkingLabel_FloorFollowsItsWordsWhenTheyChange()
    {
        var (label, row) = SqueezedLabel("a bb ccc dd e ff g hh i jj");
        _layout.PerformLayout(row, 300, 200);
        Assert.Equal(30, label.Layout.Width);

        label.Content = "a wonderfully long word";
        _layout.PerformLayout(row, 300, 200);
        Assert.Equal(77, label.Layout.Width);

        label.Content = "a bb ccc dd e ff g hh i jj";
        _layout.PerformLayout(row, 300, 200);
        Assert.Equal(30, label.Layout.Width);
    }

    [Fact]
    public void AShrinkingBox_FloorFollowsItsLabelWhenItChanges()
    {
        var label = TextElement.New("a bb ccc dd e ff g hh i jj");
        var inner = BoxElement.New();
        inner.AddChild(label);
        var rigid = TextElement.New("yyyyyyyyyy");
        rigid.Flex.FlexShrink = 0;
        var row = BoxElement.New();
        row.Flex.Direction = FlexDirection.Row;
        row.Box.Width = 100;
        row.AddChild(inner);
        row.AddChild(rigid);

        _layout.PerformLayout(row, 300, 200);
        Assert.Equal(30, inner.Layout.Width);

        label.Content = "a wonderfully long word";
        _layout.PerformLayout(row, 300, 200);
        Assert.Equal(77, inner.Layout.Width);
    }

    [Fact]
    public void AShrinkingLabelInsideAScrollContainer_HasNoFloorAtAll()
    {
        // A horizontally scrolling container is entitled to be narrower than its
        // content — the rule that made the automatic minimum safe to turn on.
        var label = TextElement.New("a wonderfully long word");
        var inner = BoxElement.New();
        inner.Box.OverflowX = Overflow.Scroll;
        inner.AddChild(label);
        var rigid = TextElement.New("yyyyyyyyyy");
        rigid.Flex.FlexShrink = 0;

        var row = BoxElement.New();
        row.Flex.Direction = FlexDirection.Row;
        row.Box.Width = 100;
        row.AddChild(inner);
        row.AddChild(rigid);

        _layout.PerformLayout(row, 300, 200);

        Assert.Equal(30, inner.Layout.Width);
    }
}

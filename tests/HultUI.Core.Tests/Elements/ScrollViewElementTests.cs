using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class ScrollViewElementTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    [Fact]
    public void New_SetsOverflow_HiddenX_ScrollY()
    {
        var sv = ScrollViewElement.New();
        Assert.Equal(Overflow.Hidden, sv.Box.OverflowX);
        Assert.Equal(Overflow.Scroll, sv.Box.OverflowY);
    }

    [Fact]
    public void ScrollOffset_ClampsToZeroWhenNoContent()
    {
        var sv = ScrollViewElement.New();
        sv.Box.Width = 200;
        sv.Box.Height = 200;

        _layout.PerformLayout(sv, 200, 200);
        sv.UpdateContentSize();

        sv.ScrollOffsetY = 100;
        Assert.Equal(0, sv.ScrollOffsetY);
    }

    [Fact]
    public void ScrollOffset_ClampsToMaxScroll()
    {
        var sv = ScrollViewElement.New();
        sv.Box.Width = 200;
        sv.Box.Height = 100;

        // Add a tall child
        var child = BoxElement.New();
        child.Box.Width = 200;
        child.Box.Height = 500;
        sv.AddChild(child);

        _layout.PerformLayout(sv, 200, 100);
        sv.UpdateContentSize();

        // Content is 500, viewport is 100, max scroll = 400
        sv.ScrollOffsetY = 999;
        Assert.Equal(400, sv.ScrollOffsetY);
    }

    [Fact]
    public void ScrollOffset_ClampsNegativeToZero()
    {
        var sv = ScrollViewElement.New();
        sv.Box.Width = 200;
        sv.Box.Height = 100;

        var child = BoxElement.New();
        child.Box.Width = 200;
        child.Box.Height = 500;
        sv.AddChild(child);

        _layout.PerformLayout(sv, 200, 100);
        sv.UpdateContentSize();

        sv.ScrollOffsetY = -50;
        Assert.Equal(0, sv.ScrollOffsetY);
    }

    [Fact]
    public void ContentSize_ComputedFromChildren()
    {
        var sv = ScrollViewElement.New();
        sv.Box.Width = 200;
        sv.Box.Height = 100;

        var child1 = BoxElement.New();
        child1.Box.Width = 200;
        child1.Box.Height = 300;
        sv.AddChild(child1);

        var child2 = BoxElement.New();
        child2.Box.Width = 200;
        child2.Box.Height = 200;
        sv.AddChild(child2);

        _layout.PerformLayout(sv, 200, 100);
        sv.UpdateContentSize();

        // Column layout: children stack vertically, total = 300 + 200 = 500
        Assert.Equal(500, sv.ContentHeight);
    }

    [Fact]
    public void ScrollEvent_UpdatesOffset()
    {
        var sv = ScrollViewElement.New();
        sv.Box.Width = 200;
        sv.Box.Height = 100;
        sv.ScrollSpeed = 40;

        var child = BoxElement.New();
        child.Box.Width = 200;
        child.Box.Height = 500;
        sv.AddChild(child);

        _layout.PerformLayout(sv, 200, 100);
        sv.UpdateContentSize();

        // Dispatch scroll event (deltaY=-1 means scroll down)
        var evt = new ScrollEvent
        {
            Position = new Point(100, 50),
            DeltaX = 0,
            DeltaY = -1
        };
        EventDispatcher.Dispatch(evt, sv);

        Assert.Equal(40, sv.ScrollOffsetY);
    }

    [Fact]
    public void ScrollEvent_StopsPropagation()
    {
        var sv = ScrollViewElement.New();
        sv.Box.Width = 200;
        sv.Box.Height = 100;

        var child = BoxElement.New();
        child.Box.Width = 200;
        child.Box.Height = 500;
        sv.AddChild(child);

        _layout.PerformLayout(sv, 200, 100);
        sv.UpdateContentSize();

        bool parentScrolled = false;
        var parent = BoxElement.New();
        parent.On<ScrollEvent>(_ => parentScrolled = true);
        parent.AddChild(sv);

        var evt = new ScrollEvent
        {
            Position = new Point(100, 50),
            DeltaX = 0,
            DeltaY = -1
        };
        EventDispatcher.Dispatch(evt, sv);

        Assert.False(parentScrolled);
    }

    [Fact]
    public void PatchFrom_PreservesScrollOffset()
    {
        var sv = ScrollViewElement.New();
        sv.Box.Width = 200;
        sv.Box.Height = 100;

        var child = BoxElement.New();
        child.Box.Width = 200;
        child.Box.Height = 500;
        sv.AddChild(child);

        _layout.PerformLayout(sv, 200, 100);
        sv.UpdateContentSize();
        sv.ScrollOffsetY = 100;

        var incoming = ScrollViewElement.New();
        incoming.ScrollSpeed = 80;

        sv.PatchFrom(incoming);

        // Scroll offset should be preserved
        Assert.Equal(100, sv.ScrollOffsetY);
        // But other properties should be patched
        Assert.Equal(80, sv.ScrollSpeed);
    }

    [Fact]
    public void HitTest_AccountsForScrollOffset()
    {
        var sv = ScrollViewElement.New();
        sv.Box.Width = 200;
        sv.Box.Height = 100;

        var child = BoxElement.New();
        child.Box.Width = 200;
        child.Box.Height = 500;
        sv.AddChild(child);

        _layout.PerformLayout(sv, 200, 100);
        sv.UpdateContentSize();
        sv.ScrollOffsetY = 200;

        // Hit test at point (100, 50) inside the scroll view
        // With scroll offset 200, the logical point in content is (100, 250)
        var hit = HitTester.HitTest(sv, new Point(100, 50));
        // The child occupies 0-500 in content space, so it should be hit
        Assert.NotNull(hit);
    }

    [Fact]
    public void HitTest_HitsItemsScrolledIntoView()
    {
        // Regression test: items below the initial viewport must be hittable
        // after scrolling them into view.
        var sv = ScrollViewElement.New();
        sv.Box.Width = 200;
        sv.Box.Height = 100;

        // 5 items, each 50px tall → total 250px, viewport 100px
        var items = new BoxElement[5];
        for (int i = 0; i < 5; i++)
        {
            items[i] = BoxElement.New();
            items[i].Box.Width = 200;
            items[i].Box.Height = 50;
            sv.AddChild(items[i]);
        }

        _layout.PerformLayout(sv, 200, 100);
        sv.UpdateContentSize();

        // Without scrolling, item[0] and item[1] are visible (Y 0-50 and 50-100)
        // item[3] is at Y=150-200, not visible
        var hitNoScroll = HitTester.HitTest(sv, new Point(100, 75));
        Assert.Same(items[1], hitNoScroll);

        // Scroll down by 100 → items[2] at Y=100-150 and items[3] at Y=150-200 are now visible
        // on screen at Y=0-50 and Y=50-100 respectively
        sv.ScrollOffsetY = 100;

        // Click at Y=25 (screen) → should hit items[2] (content Y=125)
        var hitScrolled = HitTester.HitTest(sv, new Point(100, 25));
        Assert.Same(items[2], hitScrolled);

        // Click at Y=75 (screen) → should hit items[3] (content Y=175)
        var hitScrolled2 = HitTester.HitTest(sv, new Point(100, 75));
        Assert.Same(items[3], hitScrolled2);
    }

    [Fact]
    public void ScrollRangeIntoViewHorizontal_RevealsOffscreenBand_Minimally()
    {
        var sv = ScrollViewElement.New();
        sv.Box.Width = 100;
        sv.Box.Height = 100;

        // A single wide child gives 500px of horizontal content in a 100px viewport.
        var child = BoxElement.New();
        child.Box.Width = 500;
        child.Box.Height = 100;
        sv.AddChild(child);

        _layout.PerformLayout(sv, 100, 100);
        sv.UpdateContentSize();

        // Band [300,340] is right of the viewport → scroll so its right edge is flush.
        sv.ScrollRangeIntoViewHorizontal(300, 40);
        Assert.Equal(240, sv.ScrollOffsetX);   // 340 - 100

        // A band already within [240,340] → no movement.
        sv.ScrollRangeIntoViewHorizontal(250, 10);
        Assert.Equal(240, sv.ScrollOffsetX);

        // Band left of the viewport → scroll left to its left edge.
        sv.ScrollRangeIntoViewHorizontal(50, 10);
        Assert.Equal(50, sv.ScrollOffsetX);
    }
}

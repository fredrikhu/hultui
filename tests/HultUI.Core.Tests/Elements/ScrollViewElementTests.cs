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
}

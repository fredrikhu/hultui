using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Core.Tests.Events;

/// <summary>
/// How a wheel event is shared out between the scrollers on its path (<see cref="ScrollEvent.Consume"/>)
/// and which of them a run of events belongs to (<see cref="WheelGesture"/>). Driven through
/// real scroll views in <c>HultUI.Headless.Tests</c>' <c>ScrollChainingTests</c>.
/// </summary>
public class ScrollEventTests
{
    [Fact]
    public void ShiftTurnsAVerticalWheelSideways()
    {
        var e = new ScrollEvent { DeltaY = -1, Modifiers = KeyModifiers.Shift };

        Assert.Equal(-1, e.ScrollDeltaX);
        Assert.Equal(0, e.ScrollDeltaY);
    }

    /// <summary>A trackpad already says which way it went; Shift held during a diagonal swipe
    /// must not throw its vertical half sideways.</summary>
    [Fact]
    public void ShiftLeavesAMotionThatIsAlreadySidewaysAlone()
    {
        var e = new ScrollEvent { DeltaX = -2, DeltaY = -1, Modifiers = KeyModifiers.Shift };

        Assert.Equal(-2, e.ScrollDeltaX);
        Assert.Equal(-1, e.ScrollDeltaY);
    }

    [Fact]
    public void AnAxisThatDidNotMoveIsLeftForTheNextScroller()
    {
        var inner = BoxElement.New();
        var e = new ScrollEvent { DeltaX = -1, DeltaY = -1 };

        e.Consume(inner, movedX: false, movedY: true);

        Assert.False(e.ConsumedX);
        Assert.Equal(-1, e.RemainingDeltaX);
        Assert.True(e.ConsumedY);
        Assert.Equal(0, e.RemainingDeltaY);
        Assert.Same(inner, e.ConsumedBy);
    }

    [Fact]
    public void TheLatchedScrollerKeepsAnAxisItCouldNotMoveOn()
    {
        var list = BoxElement.New();
        var e = new ScrollEvent { DeltaY = -1, Latched = list };

        e.Consume(list, movedX: false, movedY: false);

        Assert.True(e.ConsumedY);
    }

    [Fact]
    public void OnlyTheLatchedScrollerMayUseTheEvent()
    {
        var page = BoxElement.New();
        var list = BoxElement.New();

        Assert.True(new ScrollEvent().IsFor(list));
        Assert.False(new ScrollEvent { Latched = page }.IsFor(list));
        Assert.True(new ScrollEvent { Latched = page }.IsFor(page));
    }

    // ──── WheelGesture ────

    private static (BoxElement Page, BoxElement List, BoxElement Row) Tree()
    {
        var page = BoxElement.New();
        var list = BoxElement.New();
        var row = BoxElement.New();
        page.AddChild(list);
        list.AddChild(row);
        return (page, list, row);
    }

    private static ScrollEvent UsedBy(Element scroller, Element? latched = null)
    {
        var e = new ScrollEvent { DeltaY = -1, Latched = latched };
        e.Consume(scroller, movedX: false, movedY: true);
        return e;
    }

    [Fact]
    public void AGestureStaysWithTheScrollerThatUsedItsFirstEvent()
    {
        var (_, list, row) = Tree();
        var gesture = new WheelGesture();

        Assert.Null(gesture.LatchFor(row, 1000));
        gesture.Dispatched(UsedBy(list), 1000);

        Assert.Same(list, gesture.LatchFor(row, 1100));
    }

    [Fact]
    public void APauseEndsTheGesture()
    {
        var (_, list, row) = Tree();
        var gesture = new WheelGesture();
        gesture.Dispatched(UsedBy(list), 1000);

        Assert.Null(gesture.LatchFor(row, 1000 + (long)WheelGesture.Gap.TotalMilliseconds + 1));
    }

    [Fact]
    public void LeavingTheScrollerEndsTheGesture()
    {
        var (page, list, _) = Tree();
        var gesture = new WheelGesture();
        gesture.Dispatched(UsedBy(list), 1000);

        Assert.Null(gesture.LatchFor(page, 1010));   // the page, outside the list
    }

    [Fact]
    public void AnEventNothingUsedStartsNoGesture()
    {
        var (_, _, row) = Tree();
        var gesture = new WheelGesture();
        gesture.Dispatched(new ScrollEvent { DeltaY = -1 }, 1000);

        Assert.Null(gesture.LatchFor(row, 1010));
    }
}

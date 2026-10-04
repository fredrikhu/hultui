using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The wheel through the real input funnel: a scroll view uses the axes it can move on and
/// hands the rest to the one around it, a run of wheel events stays with the scroller it
/// started on, and a wheel over an overlay belongs to the overlay.
///
/// <para>Wheel events between pumps share one instant of virtual time, so a burst of them is
/// one gesture; <see cref="Pause"/> ends it. One notch is 40px (<c>ScrollSpeed</c>), and a
/// negative <c>DeltaY</c> scrolls down.</para>
/// </summary>
public class ScrollChainingTests
{
    private const float Notch = 40f;

    private static void Pause(HeadlessSession session) =>
        session.PumpFrame(TimeSpan.FromMilliseconds(WheelGesture.Gap.TotalMilliseconds + 100));

    private static void WheelDown(HeadlessSession session, float x, float y, int notches = 1)
    {
        for (int i = 0; i < notches; i++)
            session.Window.Wheel(x, y, 0, -1);
    }

    private static Element Spacer(float height) => Box.New().Height(height).FlexShrink(0);

    /// <summary>A 200px page holding, 50px down, a 100px list of <paramref name="innerContent"/>,
    /// then 500px more page. The list sits at y 50–150 until the page scrolls.</summary>
    private static (HeadlessSession Session, ScrollViewElement Page, ScrollViewElement List) PageWithList(float innerContent = 300)
    {
        var session = HeadlessSession.Create(400, 300);
        var list = ScrollView.New().Height(100f).FlexShrink(0).Children(Spacer(innerContent));
        var page = ScrollView.New().Width(300f).Height(200f).Children(Spacer(50), list, Spacer(500));
        session.Application.SetRoot(Box.New().Children(page));
        session.PumpFrame();
        return (session, page, list);
    }

    [Fact]
    public void AListAtItsEndHandsTheNextGestureToThePage()
    {
        var (session, page, list) = PageWithList();
        using var _ = session;

        WheelDown(session, 100, 100, notches: 6);   // the list has 200px: five notches and one over
        Assert.Equal(200, list.ScrollOffsetY);
        Assert.Equal(0, page.ScrollOffsetY);         // the extra notch was the list's gesture

        Pause(session);
        WheelDown(session, 100, 100);

        Assert.Equal(Notch, page.ScrollOffsetY);
        Assert.Equal(200, list.ScrollOffsetY);
    }

    [Fact]
    public void AListWithNothingToScrollPassesTheWheelStraightOn()
    {
        var (session, page, list) = PageWithList(innerContent: 50);
        using var _ = session;

        WheelDown(session, 100, 100);

        Assert.Equal(Notch, page.ScrollOffsetY);
        Assert.Equal(0, list.ScrollOffsetY);
    }

    /// <summary>The other half of latching: a page being scrolled keeps scrolling when a list
    /// arrives under the pointer, instead of the list taking the rest of the gesture.</summary>
    [Fact]
    public void APageBeingScrolledIsNotTakenOverByAListPassingUnderThePointer()
    {
        var (session, page, list) = PageWithList();
        using var _ = session;

        WheelDown(session, 100, 20);                 // over the page above the list
        Assert.Equal(Notch, page.ScrollOffsetY);
        // The list is now at y 10–110: the same gesture, aimed at it.
        WheelDown(session, 100, 60);

        Assert.Equal(2 * Notch, page.ScrollOffsetY);
        Assert.Equal(0, list.ScrollOffsetY);

        Pause(session);
        WheelDown(session, 100, 0);                  // list at y -30–70 now

        Assert.Equal(Notch, list.ScrollOffsetY);
        Assert.Equal(2 * Notch, page.ScrollOffsetY);
    }

    /// <summary>A horizontal strip inside a vertical page: the strip takes the sideways motion
    /// and lets the vertical wheel through to the page. It used to eat both.</summary>
    [Fact]
    public void AHorizontalStripPassesTheVerticalWheelToThePage()
    {
        using var session = HeadlessSession.Create(400, 300);
        var strip = ScrollView.Horizontal().Width(300f).Height(80f).FlexShrink(0)
            .Children(Box.New().Width(900f).Height(80f).FlexShrink(0));
        var page = ScrollView.New().Width(300f).Height(200f).Children(Spacer(50), strip, Spacer(500));
        session.Application.SetRoot(Box.New().Children(page));
        session.PumpFrame();

        WheelDown(session, 100, 90);
        Assert.Equal(Notch, page.ScrollOffsetY);
        Assert.Equal(0, strip.ScrollOffsetX);

        Pause(session);
        session.Window.Wheel(100, 50, 0, -1, KeyModifiers.Shift);   // the strip is at y 10–90
        Assert.Equal(Notch, strip.ScrollOffsetX);
        Assert.Equal(Notch, page.ScrollOffsetY);

        Pause(session);
        session.Window.Wheel(100, 50, -1, 0);                       // a trackpad's sideways swipe
        Assert.Equal(2 * Notch, strip.ScrollOffsetX);
    }

    /// <summary>The view's own handler used to stop the event, and in this framework stopping
    /// also ends the element's remaining handlers — so a handler an app put on a scroll view
    /// never ran. Vesper's brief view re-windows its columns from one.</summary>
    [Fact]
    public void AnAppsWheelHandlerOnAScrollViewRuns()
    {
        var (session, _, list) = PageWithList();
        using var _s = session;
        int seen = 0;
        list.On<ScrollEvent>(_ => seen++);

        WheelDown(session, 100, 100);

        Assert.Equal(1, seen);
        Assert.Equal(Notch, list.ScrollOffsetY);
    }

    /// <summary>A list whose sideways axis belongs to someone else — a pan under a virtualized
    /// list — gets the wheel's sideways motion delivered there, from a swipe or Shift+wheel,
    /// and a Shift+wheel no longer scrolls it vertically as well.</summary>
    [Fact]
    public void SidewaysMotionGoesToTheSidewaysScroller()
    {
        using var session = HeadlessSession.Create(400, 300);
        var pan = new RecordingPan();
        var list = ScrollView.New().Width(300f).Height(200f).Children(Spacer(800)).ScrollSidewaysWith(pan);
        session.Application.SetRoot(Box.New().Children(list));
        session.PumpFrame();

        session.Window.Wheel(100, 100, -1, 0);
        Assert.Equal(Notch, pan.Moved);

        Pause(session);
        session.Window.Wheel(100, 100, 0, -1, KeyModifiers.Shift);
        Assert.Equal(2 * Notch, pan.Moved);
        Assert.Equal(0, list.ScrollOffsetY);

        Pause(session);
        WheelDown(session, 100, 100);
        Assert.Equal(Notch, list.ScrollOffsetY);
        Assert.Equal(2 * Notch, pan.Moved);
    }

    [Fact]
    public void AWheelOverAnOverlayScrollsTheOverlayNotThePageBehindIt()
    {
        var (session, page, _) = PageWithList();
        using var _s = session;
        var popup = ScrollView.New().Width(150f).Height(100f).Children(Spacer(400));
        session.Window.Window.ShowOverlay(popup, 200, 20);
        session.PumpFrame();

        WheelDown(session, 250, 60);

        Assert.Equal(Notch, popup.ScrollOffsetY);
        Assert.Equal(0, page.ScrollOffsetY);
    }

    [Fact]
    public void ATextAreaAtItsEndHandsTheNextGestureToThePage()
    {
        using var session = HeadlessSession.Create(400, 300);
        var area = TextArea.New(string.Join('\n', Enumerable.Range(1, 30).Select(i => $"line {i}")))
            .Height(60f).FlexShrink(0);
        var page = ScrollView.New().Width(300f).Height(200f).Children(Spacer(50), area, Spacer(500));
        session.Application.SetRoot(Box.New().Children(page));
        session.PumpFrame();

        WheelDown(session, 100, 80, notches: 40);
        Assert.True(area.ScrollOffsetY > 0);
        Assert.Equal(0, page.ScrollOffsetY);

        Pause(session);
        WheelDown(session, 100, 80);

        Assert.Equal(Notch, page.ScrollOffsetY);
    }

    // ──── Fields that step on the wheel ────

    private static (HeadlessSession Session, ScrollViewElement Page, NumericUpDownElement Field) PageWithField()
    {
        var session = HeadlessSession.Create(400, 300);
        var field = NumericUpDown.New(value: 5, min: 0, max: 100, step: 1).Width(120f).FlexShrink(0);
        var page = ScrollView.New().Width(300f).Height(200f).Children(Spacer(50), field, Spacer(500));
        session.Application.SetRoot(Box.New().Children(page));
        session.PumpFrame();
        return (session, page, field);
    }

    private static Point Centre(Element e)
    {
        var box = e.ScreenBorderBox;
        return new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    [Fact]
    public void AFieldStepsOnTheWheelAndKeepsIt()
    {
        var (session, page, field) = PageWithField();
        using var _ = session;
        var at = Centre(field);

        session.Window.Wheel(at.X, at.Y, 0, 1);

        Assert.Equal(6, field.Value);
        Assert.Equal(0, page.ScrollOffsetY);
    }

    /// <summary>A sideways swipe has no DeltaY, and "DeltaY &gt; 0 ? up : down" read it as down.</summary>
    [Fact]
    public void ASidewaysSwipeDoesNotStepAField()
    {
        var (session, _, field) = PageWithField();
        using var _s = session;
        var at = Centre(field);

        session.Window.Wheel(at.X, at.Y, -1, 0);

        Assert.Equal(5, field.Value);
    }

    /// <summary>The accident latching exists to prevent: scrolling a settings page, a number
    /// field passes under the pointer and the rest of the spin changes it.</summary>
    [Fact]
    public void APageScrolledPastAFieldDoesNotChangeIt()
    {
        var (session, page, field) = PageWithField();
        using var _s = session;

        WheelDown(session, 100, 20);
        var at = Centre(field);
        session.Window.Wheel(at.X, at.Y, 0, -1);

        Assert.Equal(5, field.Value);
        Assert.Equal(2 * Notch, page.ScrollOffsetY);
    }

    private sealed class RecordingPan : ISidewaysScroller
    {
        public float Moved;
        public bool CanScrollSideways => true;
        public bool ScrollSidewaysBy(float dx) { Moved += dx; return true; }
    }
}

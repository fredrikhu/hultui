using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Middle-click autoscroll, the way Windows does it: the press anchors an origin, and the
/// pointer's distance from it is a <i>speed</i>. The view keeps moving while the button is
/// held even though no further input arrives, and it stops when the pointer comes back inside
/// the dead zone or the button is released.
///
/// <para>That "even though no further input arrives" is the whole substance of the feature and
/// the reason these tests pump frames without moving the mouse: an implementation that scrolled
/// only on pointer moves would pass a test that kept moving the pointer, and would sit still in
/// front of a real user holding it steady.</para>
///
/// <para>The other boundary is what the middle button does <i>besides</i> scrolling: nothing.
/// A click is the primary button's alone (see <c>PointerState</c>), so no middle press fires
/// an <c>OnClick</c> handler however far it travelled — which is what keeps a press over a
/// list from activating the row under it. The press itself still reaches PointerDown, which
/// is where a handler that really wants the middle button belongs.</para>
/// </summary>
public class MiddleClickAutoScrollTests
{
    private const float RowH = 40f;
    private const float ViewH = 100f;

    // Matches Window.AutoScrollDeadZone / AutoScrollGain. Duplicated rather than exposed:
    // the numbers are a feel decision, and a test that read them back could not notice one
    // being changed to something unusable.
    private const float DeadZone = 14f;
    private const float Gain = 9f;

    // 10 rows of 40 → 400 of content in a 100-tall viewport (maxScroll 300).
    private static (ScrollViewElement scroll, Element[] rows) BuildList(
        HeadlessSession session, bool autoScroll = true)
    {
        var rows = new Element[10];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = Box.New().Height(RowH).FlexShrink(0).Background(Color.Red);
        var scroll = ScrollView.New().Height(ViewH).Children(Flex.Column().Children(rows));
        scroll.MiddleClickAutoScroll = autoScroll;
        session.Window.SetRoot(scroll);
        session.PumpFrame();
        return (scroll, rows);
    }

    /// <summary>Holds the middle button with the pointer parked <paramref name="dy"/> below the
    /// anchor for <paramref name="seconds"/>, pumping frames but never moving the mouse again.</summary>
    private static void HoldBelow(HeadlessSession session, float originY, float dy,
        double seconds, bool release = true)
    {
        session.Window.MouseDown(100, originY, MouseButton.Middle);
        session.Window.MoveMouse(100, originY + dy);

        Pump(session, seconds);
        if (release) session.Window.MouseUp(100, originY + dy, MouseButton.Middle);
    }

    private static float ExpectedScroll(float dy, double seconds) =>
        (Math.Abs(dy) - DeadZone) * Gain * (float)seconds * Math.Sign(dy);

    [Fact]
    public void HoldingBelowTheAnchorKeepsScrollingDownWithNoFurtherInput()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, _) = BuildList(session);

        HoldBelow(session, originY: 50, dy: 30, seconds: 0.5);

        // 16 past the dead zone × 9 = 144/s for half a second.
        Assert.Equal(ExpectedScroll(30, 0.5), scroll.ScrollOffsetY, 0);
    }

    [Fact]
    public void HoldingAboveTheAnchorScrollsUp()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, _) = BuildList(session);
        scroll.ScrollOffsetY = 200f;

        HoldBelow(session, originY: 50, dy: -30, seconds: 0.5);

        Assert.Equal(200f + ExpectedScroll(-30, 0.5), scroll.ScrollOffsetY, 0);
    }

    /// <summary>Further out is faster — the displacement is a speed, so twice as far past the
    /// dead zone covers twice the distance in the same time.</summary>
    [Fact]
    public void FurtherFromTheAnchorScrollsFaster()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);

        var (near, _) = BuildList(session);
        HoldBelow(session, originY: 50, dy: 24, seconds: 0.4);
        float slow = near.ScrollOffsetY;

        var (far, _) = BuildList(session);
        HoldBelow(session, originY: 50, dy: 34, seconds: 0.4);
        float fast = far.ScrollOffsetY;

        Assert.True(slow > 0, "the near hold did not scroll at all");
        Assert.Equal(2f, fast / slow, 1);   // 10 past vs 20 past the dead zone
    }

    [Fact]
    public void InsideTheDeadZoneNothingMoves()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, _) = BuildList(session);

        HoldBelow(session, originY: 50, dy: DeadZone - 2f, seconds: 1.0);

        Assert.Equal(0f, scroll.ScrollOffsetY, 3);
    }

    /// <summary>Coming back into the dead zone parks the scroll without ending the gesture —
    /// moving back out starts it again, no second press needed.</summary>
    [Fact]
    public void ReturningToTheDeadZoneStopsItAndLeavingAgainResumes()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, _) = BuildList(session);

        session.Window.MouseDown(100, 50, MouseButton.Middle);
        session.Window.MoveMouse(100, 80);
        Pump(session, 0.25);
        float afterFirstRun = scroll.ScrollOffsetY;
        Assert.True(afterFirstRun > 0, "the first leg did not scroll");

        session.Window.MoveMouse(100, 52);   // back inside the dead zone
        Pump(session, 0.5);
        Assert.Equal(afterFirstRun, scroll.ScrollOffsetY, 3);

        session.Window.MoveMouse(100, 80);   // out again
        Pump(session, 0.25);
        Assert.True(scroll.ScrollOffsetY > afterFirstRun, "leaving the dead zone again did not resume");

        session.Window.MouseUp(100, 80, MouseButton.Middle);
    }

    [Fact]
    public void ReleasingTheButtonStopsIt()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, _) = BuildList(session);

        HoldBelow(session, originY: 50, dy: 30, seconds: 0.3);
        float atRelease = scroll.ScrollOffsetY;
        Assert.True(atRelease > 0, "nothing scrolled before the release");

        Pump(session, 1.0);   // frames keep coming; the scroll must not
        Assert.Equal(atRelease, scroll.ScrollOffsetY, 3);
    }

    [Fact]
    public void ItStopsAtTheEndOfTheContentRatherThanRunningPastIt()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, _) = BuildList(session);

        HoldBelow(session, originY: 50, dy: 120, seconds: 3.0);   // far more than 300 worth

        Assert.Equal(300f, scroll.ScrollOffsetY, 3);   // maxScroll, not beyond
    }

    // ── The click boundary ───────────────────────────────────────────────

    /// <summary>
    /// A middle press that never leaves the dead zone scrolls nothing and clicks nothing.
    /// <para>
    /// It used to click, and that was the bug: <c>OnClick</c> takes no button, so every
    /// handler in every app fired on a middle press too. Harmless while nobody pressed the
    /// middle button over a list — and not harmless at all once middle-drag became the way to
    /// scroll one. In Junction it staged the diff line under the pointer.
    /// </para>
    /// </summary>
    [Fact]
    public void AMiddlePressThatNeverLeavesTheDeadZoneScrollsNothingAndClicksNothing()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, rows) = BuildList(session);
        int clicks = 0, presses = 0;
        rows[0].On<ClickEvent>(_ => clicks++);
        rows[0].On<PointerDownEvent>(e => { if (e.Button == 1) presses++; });
        session.PumpFrame();

        session.Window.Click(100, 20, MouseButton.Middle);

        Assert.Equal(0, clicks);
        Assert.Equal(1, presses);   // the press still arrives, which is where middle belongs
        Assert.Equal(0f, scroll.ScrollOffsetY, 3);
    }

    /// <summary>The primary button is unaffected — this is the control case for the one
    /// above, so "no click" cannot pass by the click machinery being broken outright.</summary>
    [Fact]
    public void ALeftClickStillClicks()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (_, rows) = BuildList(session);
        int clicks = 0;
        rows[0].On<ClickEvent>(_ => clicks++);
        session.PumpFrame();

        session.Window.Click(100, 20);

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void AnAutoScrollThatRanClicksNothingEither()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, rows) = BuildList(session);
        int clicks = 0;
        foreach (var row in rows) row.On<ClickEvent>(_ => clicks++);
        session.PumpFrame();

        HoldBelow(session, originY: 50, dy: 30, seconds: 0.3);

        Assert.Equal(0, clicks);
        Assert.True(scroll.ScrollOffsetY > 0);
    }

    /// <summary>
    /// The gesture survives the view it captured being <i>replaced</i> rather than patched —
    /// a rebuild that swaps the element rather than reusing it. A replaced element is dropped
    /// from the tree without being marked detached, so it goes on looking alive: the window
    /// kept scrolling that corpse while the view on screen sat still. It re-acquires whatever
    /// scrollable now sits under the pointer and carries the offset across.
    /// </summary>
    [Fact]
    public void AReplacedViewMidGestureIsReAcquiredRatherThanScrolledAsACorpse()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (captured, _) = BuildList(session);

        session.Window.MouseDown(100, 50, MouseButton.Middle);
        session.Window.MoveMouse(100, 80);
        Pump(session, 0.25);
        Assert.True(captured.ScrollOffsetY > 0, "it never started scrolling");

        // A whole new tree in the same shape: the element the press captured is no longer
        // the one on screen, and nothing told it so.
        var (live, _) = BuildList(session);
        Assert.NotSame(captured, live);
        Pump(session, 0.25);

        Assert.True(live.ScrollOffsetY > 0, "the gesture did not find the view that replaced it");
        // The discriminating half: without re-acquisition the offset keeps climbing on the
        // element nobody can see, and the live one never leaves zero.
        Assert.True(live.ScrollOffsetY > captured.ScrollOffsetY,
            $"the scroll stayed on the replaced view (live {live.ScrollOffsetY:0}, "
            + $"captured {captured.ScrollOffsetY:0})");

        session.Window.MouseUp(100, 80, MouseButton.Middle);
    }

    // ── Opting out, and lists with nowhere to go ─────────────────────────

    [Fact]
    public void AutoScrollOffLeavesTheViewAlone()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, rows) = BuildList(session, autoScroll: false);
        int presses = 0;
        foreach (var row in rows) row.On<PointerDownEvent>(e => { if (e.Button == 1) presses++; });
        session.PumpFrame();

        HoldBelow(session, originY: 50, dy: 30, seconds: 0.5);

        Assert.Equal(0f, scroll.ScrollOffsetY, 3);
        // And the press still reaches the app, which is the whole of what opting out means.
        Assert.True(presses > 0, "opting out swallowed the press as well as the scroll");
    }

    [Fact]
    public void AListWithNothingToScrollLeavesTheMiddleButtonAlone()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var row = Box.New().Height(RowH).FlexShrink(0).Background(Color.Red);
        var scroll = ScrollView.New().Height(ViewH).Children(Flex.Column().Children(row));
        session.Window.SetRoot(scroll);
        session.PumpFrame();

        int presses = 0;
        row.On<PointerDownEvent>(e => { if (e.Button == 1) presses++; });
        session.PumpFrame();

        // Nothing to scroll, so nothing is claimed and the press reaches the app as always.
        session.Window.MouseDown(100, 20, MouseButton.Middle);
        session.Window.MoveMouse(100, 35);
        session.Window.MouseUp(100, 35, MouseButton.Middle);

        Assert.Equal(0f, scroll.ScrollOffsetY, 3);
        Assert.Equal(1, presses);
    }

    // ── The sideways axis, when something else owns it ───────────────────

    /// <summary>A stand-in for <c>HorizontalPan</c>: something that moves the view's content
    /// sideways by means the view knows nothing about, and keeps the position itself.</summary>
    private sealed class Pan(float max) : ISidewaysScroller
    {
        public float Offset;
        public bool CanScrollSideways => max > 0;
        public bool ScrollSidewaysBy(float dx)
        {
            float next = Math.Clamp(Offset + dx, 0, max);
            if (next == Offset) return false;
            Offset = next;
            return true;
        }
    }

    /// <summary>
    /// A view that has handed its sideways axis to something else sends the gesture's
    /// horizontal half there, and its own horizontal offset stays where it was.
    /// <para>This is what a virtualized list under a <c>HorizontalPan</c> looks like: its own
    /// <c>ScrollOffsetX</c> can never leave zero, so a gesture that drove it did nothing
    /// sideways while the scrollbar right under the list worked. Junction's diff found it.</para>
    /// </summary>
    [Fact]
    public void HoldingRightOfTheAnchorDrivesTheSidewaysScrollerInstead()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, _) = BuildList(session);
        var pan = new Pan(max: 1000);
        scroll.ScrollSidewaysWith(pan);

        session.Window.MouseDown(100, 50, MouseButton.Middle);
        session.Window.MoveMouse(130, 50);   // 16 past the dead zone, straight right
        Pump(session, 0.5);

        Assert.Equal(ExpectedScroll(30, 0.5), pan.Offset, 0);
        Assert.Equal(0f, scroll.ScrollOffsetX, 3);
        Assert.Equal(0f, scroll.ScrollOffsetY, 3);   // straight right is not down at all

        session.Window.MoveMouse(70, 50);    // and back the other way
        Pump(session, 0.25);
        Assert.Equal(ExpectedScroll(30, 0.5) + ExpectedScroll(-30, 0.25), pan.Offset, 0);

        session.Window.MouseUp(70, 50, MouseButton.Middle);
    }

    /// <summary>Both axes at once go to their respective owners: the view scrolls down by
    /// itself while the scroller carries the sideways part.</summary>
    [Fact]
    public void ADiagonalHoldSplitsBetweenTheViewAndTheSidewaysScroller()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, _) = BuildList(session);
        var pan = new Pan(max: 1000);
        scroll.ScrollSidewaysWith(pan);

        session.Window.MouseDown(100, 50, MouseButton.Middle);
        session.Window.MoveMouse(130, 80);
        Pump(session, 0.5);

        Assert.Equal(ExpectedScroll(30, 0.5), pan.Offset, 0);
        Assert.Equal(ExpectedScroll(30, 0.5), scroll.ScrollOffsetY, 0);

        session.Window.MouseUp(130, 80, MouseButton.Middle);
    }

    /// <summary>
    /// A view with nothing to scroll of its own still claims the press when its sideways
    /// scroller has somewhere to go — a short, wide list is exactly the case a reader reaches
    /// for the middle button to pan.
    /// </summary>
    [Fact]
    public void AShortViewStillClaimsThePressWhenItsSidewaysScrollerCanMove()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var row = Box.New().Height(RowH).FlexShrink(0).Background(Color.Red);
        var scroll = ScrollView.New().Height(ViewH).Children(Flex.Column().Children(row));
        var pan = new Pan(max: 1000);
        scroll.ScrollSidewaysWith(pan);
        session.Window.SetRoot(scroll);
        session.PumpFrame();
        Assert.True(scroll.CanAutoScroll);

        session.Window.MouseDown(100, 20, MouseButton.Middle);
        session.Window.MoveMouse(130, 20);
        Pump(session, 0.5);

        Assert.Equal(ExpectedScroll(30, 0.5), pan.Offset, 0);
        session.Window.MouseUp(130, 20, MouseButton.Middle);
    }

    /// <summary>And when the scroller has nowhere to go either, the press is just a press —
    /// the same promise a plain view with nothing to scroll already keeps.</summary>
    [Fact]
    public void AShortViewWhoseSidewaysScrollerCannotMoveLeavesTheMiddleButtonAlone()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var row = Box.New().Height(RowH).FlexShrink(0).Background(Color.Red);
        var scroll = ScrollView.New().Height(ViewH).Children(Flex.Column().Children(row));
        scroll.ScrollSidewaysWith(new Pan(max: 0));
        session.Window.SetRoot(scroll);
        session.PumpFrame();

        int presses = 0;
        row.On<PointerDownEvent>(e => { if (e.Button == 1) presses++; });
        session.PumpFrame();

        Assert.False(scroll.CanAutoScroll);
        session.Window.MouseDown(100, 20, MouseButton.Middle);
        session.Window.MoveMouse(130, 20);
        Pump(session, 0.2);
        session.Window.MouseUp(130, 20, MouseButton.Middle);

        Assert.Equal(1, presses);
    }

    /// <summary>Runs the loop for <paramref name="seconds"/> of virtual time. PumpFrame
    /// advances the clock itself, one 60 Hz frame each — advancing it again here would run
    /// the scroll at double speed while the frame count stayed the same.</summary>
    private static void Pump(HeadlessSession session, double seconds)
    {
        int frames = (int)Math.Round(seconds * 60);
        for (int i = 0; i < frames; i++) session.PumpFrame();
    }
}

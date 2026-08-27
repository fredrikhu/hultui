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
/// <para>The other two worth pinning are the boundaries: a middle press that never leaves the
/// dead zone still clicks (a tab strip closes tabs with it), and one that scrolled does not.</para>
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

    [Fact]
    public void AMiddleClickThatNeverLeavesTheDeadZoneStillClicks()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, rows) = BuildList(session);
        int clicks = 0;
        rows[0].On<ClickEvent>(e => { if (e.Button == 1) clicks++; });
        session.PumpFrame();

        session.Window.Click(100, 20, MouseButton.Middle);

        Assert.Equal(1, clicks);
        Assert.Equal(0f, scroll.ScrollOffsetY, 3);
    }

    [Fact]
    public void AnAutoScrollThatRanSwallowsItsClick()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, rows) = BuildList(session);
        int clicks = 0;
        foreach (var row in rows) row.On<ClickEvent>(e => { if (e.Button == 1) clicks++; });
        session.PumpFrame();

        HoldBelow(session, originY: 50, dy: 30, seconds: 0.3);

        Assert.Equal(0, clicks);
        Assert.True(scroll.ScrollOffsetY > 0);
    }

    // ── Opting out, and lists with nowhere to go ─────────────────────────

    [Fact]
    public void AutoScrollOffLeavesTheViewAloneAndKeepsTheClick()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, rows) = BuildList(session, autoScroll: false);
        int clicks = 0;
        foreach (var row in rows) row.On<ClickEvent>(e => { if (e.Button == 1) clicks++; });
        session.PumpFrame();

        HoldBelow(session, originY: 50, dy: 30, seconds: 0.5);

        Assert.Equal(0f, scroll.ScrollOffsetY, 3);
        session.Window.Click(100, 20, MouseButton.Middle);
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void AListWithNothingToScrollLeavesTheMiddleButtonAlone()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var row = Box.New().Height(RowH).FlexShrink(0).Background(Color.Red);
        var scroll = ScrollView.New().Height(ViewH).Children(Flex.Column().Children(row));
        session.Window.SetRoot(scroll);
        session.PumpFrame();

        int clicks = 0;
        row.On<ClickEvent>(e => { if (e.Button == 1) clicks++; });
        session.PumpFrame();

        // A press that travels inside a list with nowhere to go is not an autoscroll, so its
        // click survives — the middle button keeps meaning what the app made it mean.
        session.Window.MouseDown(100, 20, MouseButton.Middle);
        session.Window.MoveMouse(100, 35);   // still inside the same row, so a click is due
        session.Window.MouseUp(100, 35, MouseButton.Middle);

        Assert.Equal(0f, scroll.ScrollOffsetY, 3);
        Assert.Equal(1, clicks);
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

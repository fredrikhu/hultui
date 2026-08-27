using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Middle-button drag pans a scroll view like a hand tool: the content follows the pointer,
/// so dragging <i>up</i> moves the content up and the offset down the list.
///
/// <para>The two cases worth pinning are the ones that decide whether the gesture is safe to
/// have on by default: a middle press that never travels must still click (a tab strip closes
/// tabs with it), and one that does travel must not, or every pan would fire a click on
/// whatever row it started over.</para>
/// </summary>
public class MiddleDragScrollTests
{
    private const float RowH = 40f;
    private const float ViewH = 100f;

    // 10 rows of 40 → 400 of content in a 100-tall viewport (maxScroll 300).
    private static (ScrollViewElement scroll, Element[] rows) BuildList(
        HeadlessSession session, bool middleDragScroll = true)
    {
        var rows = new Element[10];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = Box.New().Height(RowH).FlexShrink(0).Background(Color.Red);
        var scroll = ScrollView.New().Height(ViewH).Children(Flex.Column().Children(rows));
        scroll.MiddleDragScroll = middleDragScroll;
        session.Window.SetRoot(scroll);
        session.PumpFrame();
        return (scroll, rows);
    }

    [Fact]
    public void MiddleDragUp_ScrollsDown()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, _) = BuildList(session);

        session.Window.Drag(new Point(100, 90), new Point(100, 30), button: MouseButton.Middle);

        // The hand carried the content 60 up, so the view is 60 further down the list.
        Assert.Equal(60f, scroll.ScrollOffsetY, 3);
    }

    [Fact]
    public void MiddleDragDown_ScrollsBackUpAndClampsAtTheTop()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, _) = BuildList(session);
        scroll.ScrollOffsetY = 100f;

        session.Window.Drag(new Point(100, 20), new Point(100, 90), button: MouseButton.Middle);

        Assert.Equal(30f, scroll.ScrollOffsetY, 3);

        // Past the top it stops rather than going negative.
        session.Window.Drag(new Point(100, 20), new Point(100, 90), button: MouseButton.Middle);
        Assert.Equal(0f, scroll.ScrollOffsetY, 3);
    }

    [Fact]
    public void MiddlePressThatDoesNotTravel_StillClicks()
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
    public void MiddleDragThatTravels_SwallowsTheClick()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, rows) = BuildList(session);
        int clicks = 0;
        foreach (var row in rows) row.On<ClickEvent>(e => { if (e.Button == 1) clicks++; });
        session.PumpFrame();

        session.Window.Drag(new Point(100, 90), new Point(100, 30), button: MouseButton.Middle);

        Assert.Equal(0, clicks);
        Assert.Equal(60f, scroll.ScrollOffsetY, 3);
    }

    [Fact]
    public void MiddleDragScrollOff_LeavesTheViewAloneAndKeepsTheClick()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, rows) = BuildList(session, middleDragScroll: false);
        int clicks = 0;
        foreach (var row in rows) row.On<ClickEvent>(e => { if (e.Button == 1) clicks++; });
        session.PumpFrame();

        session.Window.Drag(new Point(100, 90), new Point(100, 30), button: MouseButton.Middle);

        Assert.Equal(0f, scroll.ScrollOffsetY, 3);
        // Press and release land on different rows, so no click is synthesized either way —
        // what matters is that nothing panned. A press that stays put still clicks:
        session.Window.Click(100, 20, MouseButton.Middle);
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void ListWithNothingToScroll_LeavesTheMiddleButtonAlone()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var row = Box.New().Height(RowH).FlexShrink(0).Background(Color.Red);
        var scroll = ScrollView.New().Height(ViewH).Children(Flex.Column().Children(row));
        session.Window.SetRoot(scroll);
        session.PumpFrame();

        int clicks = 0;
        row.On<ClickEvent>(e => { if (e.Button == 1) clicks++; });
        session.PumpFrame();

        // A press that travels inside a list with nowhere to go is not a pan, so its click
        // survives — the middle button keeps meaning what the app made it mean.
        session.Window.MouseDown(100, 20, MouseButton.Middle);
        session.Window.MoveMouse(100, 35);
        session.Window.MouseUp(100, 35, MouseButton.Middle);

        Assert.Equal(0f, scroll.ScrollOffsetY, 3);
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void PanFollowsTheViewItStartedIn_EvenAfterThePointerLeavesIt()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, _) = BuildList(session);
        scroll.ScrollOffsetY = 150f;   // room to move both ways without clamping

        session.Window.MouseDown(100, 90, MouseButton.Middle);
        session.Window.MoveMouse(100, 40);
        session.Window.MoveMouse(100, 150);   // outside the 100-tall viewport
        session.Window.MoveMouse(100, 10);
        session.Window.MouseUp(100, 10, MouseButton.Middle);

        // Net travel is 80 up, and every leg — including the ones spent outside the
        // viewport — was applied to the view the press started in.
        Assert.Equal(230f, scroll.ScrollOffsetY, 3);
    }
}

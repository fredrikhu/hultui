using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="ScrollViewElement.UserScrolled"/>: called once each time the user's input moves the
/// view — the wheel, the scrollbar, the autoscroll, a virtual list's scroll keys — and never for a
/// scroll from code. The scrollbar and the autoscroll raise no <see cref="ScrollEvent"/>, so a view
/// that made what it shows from the offset and listened only to the wheel's event (Vesper's Brief
/// columns) showed blank columns after a track click or a drag.
/// </summary>
public class UserScrolledTests
{
    // 900 of content in a 150-tall view (750 to scroll); the bar's thumb is 25 tall, at the
    // right edge, and reachable 12 in from it.
    private static (HeadlessSession Session, ScrollViewElement Scroll, List<float> Seen) Start()
    {
        var session = HeadlessSession.Create(200, 150);
        var scroll = ScrollView.New().Height(150f).Children(Box.New().Height(900f).FlexShrink(0));
        var seen = new List<float>();
        scroll.UserScrolled = () => seen.Add(scroll.ScrollOffsetY);
        session.Application.SetRoot(scroll);
        session.PumpFrames(2);
        return (session, scroll, seen);
    }

    private const float BarX = 194f;

    [Fact]
    public void ATrackClick_TellsTheListenerOnce_AfterTheViewMoved()
    {
        var (session, scroll, seen) = Start();
        using var _ = session;

        session.Window.MouseDown(BarX, 120f);
        session.PumpFrame();
        session.Window.MouseUp(BarX, 120f);
        session.PumpFrame();

        Assert.True(scroll.ScrollOffsetY > 0, "the track click did not scroll");
        Assert.Equal([scroll.ScrollOffsetY], seen);
    }

    [Fact]
    public void AThumbDrag_TellsTheListenerOncePerMoveThatScrolls()
    {
        var (session, scroll, seen) = Start();
        using var _ = session;

        session.Window.MouseDown(BarX, 10f);   // on the thumb
        session.PumpFrame();
        var offsets = new List<float>();
        foreach (float y in new[] { 30f, 50f, 50f, 70f, 400f, 420f })   // a move that stays put; two past the end
        {
            float before = scroll.ScrollOffsetY;
            session.Window.MoveMouse(BarX, y);
            session.PumpFrame();
            if (scroll.ScrollOffsetY != before) offsets.Add(scroll.ScrollOffsetY);
        }
        session.Window.MouseUp(BarX, 420f);
        session.PumpFrame();

        Assert.Equal(4, offsets.Count);
        Assert.Equal(750f, scroll.ScrollOffsetY);
        Assert.Equal(offsets, seen);
    }

    [Fact]
    public void AScrollFromCode_TellsNothing()
    {
        var (session, scroll, seen) = Start();
        using var _ = session;

        scroll.ScrollOffsetY = 300f;
        session.PumpFrame();
        scroll.ScrollBy(0f, 100f);
        session.PumpFrame();
        scroll.ScrollRangeIntoView(0f, 20f);
        session.PumpFrame();
        scroll.ScrollIntoView(scroll.Children[0]);
        session.PumpFrame();

        Assert.Equal(0f, scroll.ScrollOffsetY);
        Assert.Empty(seen);
    }

    /// <summary>The wheel too, once per notch that moved the view; its own event goes on as
    /// before, for every notch, a notch at the top included.</summary>
    [Fact]
    public void TheWheel_TellsTheListenerWhenItMovesTheView_AndItsEventIsUnchanged()
    {
        var (session, scroll, seen) = Start();
        using var _ = session;
        int events = 0;
        scroll.On<ScrollEvent>(_ => events++);

        session.Window.Wheel(100f, 75f, 0f, -1f);   // down
        session.PumpFrame();
        session.Window.Wheel(100f, 75f, 0f, 1f);    // back up to the top
        session.PumpFrame();
        session.Window.Wheel(100f, 75f, 0f, 1f);    // at the top: nothing moves
        session.PumpFrame();

        Assert.Equal(3, events);
        Assert.Equal([40f, 0f], seen);
    }

    [Fact]
    public void TheMiddleButtonAutoscroll_TellsTheListenerEachFrameItMoves()
    {
        var (session, scroll, seen) = Start();
        using var _ = session;

        session.Window.MouseDown(100f, 50f, MouseButton.Middle);
        session.Window.MoveMouse(100f, 90f);   // 40 below the anchor: past the dead zone
        var offsets = new List<float>();
        for (int i = 0; i < 20; i++)
        {
            float before = scroll.ScrollOffsetY;
            session.PumpFrame();
            if (scroll.ScrollOffsetY != before) offsets.Add(scroll.ScrollOffsetY);
        }
        session.Window.MouseUp(100f, 90f, MouseButton.Middle);
        session.PumpFrames(3);

        Assert.True(offsets.Count >= 10, $"the autoscroll moved the view on {offsets.Count} frames");
        Assert.Equal(offsets, seen);
    }

    [Fact]
    public void ASelectionHeldPastTheEdge_TellsTheListenerAsTheViewFollows()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var label = TextElement.New(string.Join("\n", Enumerable.Range(0, 40).Select(i => $"line {i:00}")));
        label.Selectable = true;
        var scroll = ScrollView.Vertical().Width(200).Height(100).Children(label);
        var seen = new List<float>();
        scroll.UserScrolled = () => seen.Add(scroll.ScrollOffsetY);
        session.Window.SetRoot(scroll);
        session.PumpFrame();
        float top = scroll.Layout.AbsoluteContentBox.Y;

        session.Window.MouseDown(0f, top + 4f);
        session.Window.MoveMouse(150f, 150f);   // below the view, inside the window
        var offsets = new List<float>();
        for (int i = 0; i < 20; i++)
        {
            float before = scroll.ScrollOffsetY;
            session.PumpFrame();
            if (scroll.ScrollOffsetY != before) offsets.Add(scroll.ScrollOffsetY);
        }
        session.Window.MouseUp(150f, 150f);

        Assert.True(offsets.Count >= 10, $"the view followed the selection on {offsets.Count} frames");
        Assert.Equal(offsets, seen);
    }

    [Fact]
    public void AVirtualListsScrollKeys_TellTheListener_AndAKeyThatMovesNothingDoesNot()
    {
        using var session = HeadlessSession.Create(200, 150);
        var list = VirtualListElement<int>.New().ItemHeight(30).Items(Enumerable.Range(0, 50).ToArray())
            .RenderWith((i, _) => TextElement.New($"Row {i}"));
        list.KeyboardScrollEnabled = true;
        list.IsFocusable = true;
        var seen = new List<float>();
        list.UserScrolled = () => seen.Add(list.ScrollOffsetY);
        session.Window.SetRoot(list);
        session.PumpFrames(2);
        list.Focus();
        session.PumpFrame();

        session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();
        session.Window.PressKey(KeyCode.End);
        session.PumpFrame();
        session.Window.PressKey(KeyCode.End);   // already there
        session.PumpFrame();

        Assert.Equal([30f, list.ScrollOffsetY], seen);
        Assert.True(list.ScrollOffsetY > 30f, "End did not scroll");
    }

    /// <summary>Sideways motion that goes to a pan moves the app's own state, not this offset.</summary>
    [Fact]
    public void SidewaysMotionHandedToAPan_TellsNothing()
    {
        using var session = HeadlessSession.Create(200, 150);
        var pan = new CountingPan();
        var scroll = ScrollView.Horizontal().Width(200).Height(100).ScrollSidewaysWith(pan)
            .Children(Box.New().Width(900f).Height(50f).FlexShrink(0));
        int calls = 0;
        scroll.UserScrolled = () => calls++;
        session.Window.SetRoot(scroll);
        session.PumpFrames(2);

        session.Window.Wheel(100f, 50f, 1f, 0f);
        session.PumpFrame();

        Assert.Equal(1, pan.Moves);
        Assert.Equal(0f, scroll.ScrollOffsetX);
        Assert.Equal(0, calls);
    }

    /// <summary>A build that patches the view hands it the listener the build made.</summary>
    [Fact]
    public void APatch_CarriesTheNewListener()
    {
        var live = ScrollView.New();
        var incoming = ScrollView.New();
        Action listener = () => { };
        incoming.UserScrolled = listener;

        live.PatchFrom(incoming);

        Assert.Same(listener, live.UserScrolled);
    }

    private sealed class CountingPan : ISidewaysScroller
    {
        public int Moves;
        public bool CanScrollSideways => true;
        public bool ScrollSidewaysBy(float dx) { Moves++; return true; }
    }
}

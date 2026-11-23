using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;
// The builder: this assembly also sees the internal Elements.Scrollbar that paints it.
using ScrollbarBuilder = HultUI.Core.Builders.Scrollbar;

namespace HultUI.Headless.Tests;

/// <summary>
/// A scrollbar standing on its own over a range no float could hold: 50 GiB of a document drawn a
/// screenful at a time. Dragged, clicked, wheeled and keyed as ScrollView's bars are, reporting
/// user gestures through OnChanged once a frame, and read as a scroll bar with its range.
/// </summary>
public class ScrollbarElementTests
{
    private const long FiftyGiB = 50L << 30;
    private const long Viewport = 4096;

    /// <summary>A viewer's position, rebuilt from the bar's reports.</summary>
    private sealed class Owner : Component
    {
        public long Top;
        public bool Focusable;
        public int Builds;
        public readonly List<long> Changes = [];

        public override Element Build()
        {
            Builds++;
            var bar = ScrollbarBuilder.Vertical(Top, FiftyGiB, Viewport).Width(Dimension.Px(20));
            bar.IsFocusable = Focusable;
            bar.OnChanged = v => { Top = v; Changes.Add(v); MarkDirty(); };
            return Flex.Row().Height(300f).AlignItems(AlignItems.Stretch).Children(bar);
        }
    }

    /// <summary>A 20 × 300 bar, alone in its window.</summary>
    private static (HeadlessSession Session, Owner Owner, ComponentHost Host) Mount(bool focusable = false)
    {
        var session = HeadlessSession.Create(20, 300, options: new HeadlessOptions { RecordDrawCalls = true });
        var owner = new Owner { Focusable = focusable };
        var host = owner.Mount();
        session.Window.SetRoot(host);
        session.PumpFrames(2);
        return (session, owner, host);
    }

    private static ScrollbarElement Bar(ComponentHost host) => (ScrollbarElement)host.Children[0].Children[0];

    [Fact]
    public void ADragFromTheThumbToTheBottom_EndsOnMaxValue()
    {
        var (session, owner, host) = Mount();
        using var _ = session;

        session.Window.Drag(new Point(10, 10), new Point(10, 299), steps: 8);
        session.PumpFrames(2);

        Assert.Equal(FiftyGiB - Viewport, owner.Changes[^1]);
        Assert.Equal(FiftyGiB - Viewport, Bar(host).Value);
        Assert.False(Bar(host).IsDragging);
    }

    [Fact]
    public void ATrackClick_CentresTheThumbUnderThePointer_AndDoesNotDrag()
    {
        var (session, owner, host) = Mount();
        using var _ = session;

        session.Window.MouseDown(10, 150);
        Assert.False(Bar(host).IsDragging);
        session.Window.MoveMouse(10, 200);
        session.Window.MouseUp(10, 200);
        session.PumpFrames(2);

        // The 20 px thumb's middle under y = 150 on a 300 px track: (150 - 10) / 280 of the way.
        long expected = (long)Math.Round(140.0 / 280.0 * (FiftyGiB - Viewport));
        Assert.Equal([expected], owner.Changes);
        var thumb = Bars(session).Split(" | ")[1];
        Assert.Contains(",140 ", thumb);
    }

    [Fact]
    public void TheWheel_MovesThreeSmallChangesANotch_AndKeepsTheFractions()
    {
        var (session, owner, host) = Mount();
        using var _ = session;
        Bar(host).SmallChange = 1;

        session.Window.Wheel(10, 150, 0, -3);
        Assert.Equal([9L], owner.Changes);

        // A smooth wheel's sixth of a notch is half a unit: two of them make one.
        session.Window.Wheel(10, 150, 0, -1f / 6);
        Assert.Equal([9L], owner.Changes);
        session.Window.Wheel(10, 150, 0, -1f / 6);
        Assert.Equal([9L, 10L], owner.Changes);
    }

    [Fact]
    public void Focused_PageDownMovesALargeChange_AndEndGoesToMaxValue()
    {
        var (session, owner, host) = Mount(focusable: true);
        using var _ = session;
        session.Window.Window.SetFocus(Bar(host));

        session.Window.PressKey(KeyCode.PageDown);
        session.Window.PressKey(KeyCode.End);

        Assert.Equal([Viewport, FiftyGiB - Viewport], owner.Changes);
    }

    [Fact]
    public void NotFocusableByDefault_TabPassesItBy()
    {
        using var session = HeadlessSession.Create(300, 300);
        var first = Button.New("First");
        var bar = ScrollbarBuilder.Vertical(0, FiftyGiB, Viewport);
        var second = Button.New("Second");
        session.Window.SetRoot(Flex.Row().AlignItems(AlignItems.Stretch).Children(first, bar, second));
        session.PumpFrames(2);
        session.Window.Window.SetFocus(first);

        session.Window.PressKey(KeyCode.Tab);

        Assert.Same(second, session.Window.Window.FocusedElement);
    }

    [Fact]
    public void Hover_WidensTheDrawnTrackFromSixToTwelve()
    {
        var (session, _, _) = Mount();
        using var _ = session;
        Assert.Contains(" 6x300", Bars(session));

        session.Window.MoveMouse(10, 150);
        session.PumpFrames(20);                       // past the 120 ms expansion

        Assert.Contains(" 12x300", Bars(session));
    }

    [Fact]
    public void MovesDuringADrag_ApplyOnceAFrame()
    {
        var (session, owner, _) = Mount();
        using var _ = session;
        session.Window.MouseDown(10, 10);             // on the thumb: a drag, no change yet
        Assert.Empty(owner.Changes);
        int builds = owner.Builds;

        for (int i = 1; i <= 16; i++)
            session.Window.MoveMouse(10, 10 + i);     // a 1,000 Hz mouse's moves in one frame
        Assert.Empty(owner.Changes);

        session.PumpFrame();
        Assert.Single(owner.Changes);
        Assert.Equal(builds + 1, owner.Builds);
        session.Window.MouseUp(10, 26);
    }

    [Fact]
    public void ItIsFoundAsAScrollBar_WithItsRange()
    {
        var (session, _, _) = Mount();
        using var _ = session;

        var node = session.Window.FindByRole(AccessibilityRole.ScrollBar);

        Assert.NotNull(node);
        Assert.Equal(new AccessibleRange(0, 0, FiftyGiB - Viewport, 1, Viewport, false), node.Node.Range);
    }

    /// <summary>The bar's two rects in a fresh frame: track, then thumb.</summary>
    private static string Bars(HeadlessSession session)
    {
        session.Window.RenderLog.Clear();
        session.PumpFrame(session.DefaultFrameDelta, forceRender: true);
        return string.Join(" | ", session.Window.RenderLog.Named("DrawRoundedRect").Select(o => o.Args));
    }
}

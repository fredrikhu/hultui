using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Fingers as pointers (docs/design/POINTER-IDENTITY.md): the first finger down is the primary
/// pointer and goes through the mouse's paths with its ID and type stamped on every event, under
/// the touch rules (a tap within 8 DIP, a double tap within 300 ms and 32 DIP, no sticky hover, no
/// tooltip, a cancel that clicks nothing). Other fingers reach the primary pointer's capture holder
/// only. Each rule has a mouse control beside it: the mouse keeps today's rules exactly.
/// </summary>
public sealed class TouchPointerTests
{
    private sealed class Log
    {
        public readonly List<string> Events = [];
        public readonly List<PointerEvent> Pointer = [];

        public T Watch<T>(T element, string name) where T : Element
        {
            element.OnPointerDown(e => Add($"{name} down", e));
            element.OnPointerUp(e => Add($"{name} up", e));
            element.OnPointerCancel(e => Add($"{name} cancel", e));
            element.OnClick(e => Add($"{name} click {e.ClickCount}", e));
            element.OnPointerEnter(e => Add($"{name} enter", e));
            element.OnPointerLeave(e => Add($"{name} leave", e));
            return element;
        }

        private void Add(string what, PointerEvent e)
        {
            Events.Add(what);
            Pointer.Add(e);
        }

        public IEnumerable<string> Of(string name) => Events.Where(e => e.StartsWith(name + " ", StringComparison.Ordinal));
    }

    private static (HeadlessSession Session, ButtonElement A, ButtonElement B, Log Log) TwoButtons()
    {
        var log = new Log();
        var a = log.Watch(Button.New("A"), "A");
        var b = log.Watch(Button.New("B"), "B");
        var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Flex.Column().Gap(20).Children(a.Height(40), b.Height(40)));
        session.PumpFrame();
        return (session, a, b, log);
    }

    private static (float X, float Y) Center(Element e)
    {
        var box = e.Layout.AbsoluteBorderBox;
        return (box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    [Fact]
    public void ATap_Clicks_StampedAsTheFirstFinger()
    {
        var (session, a, _, log) = TwoButtons();
        using var _ = session;

        session.Window.Tap(a);

        Assert.Equal(["A enter", "A down", "A up", "A click 1", "A leave"], log.Of("A"));
        Assert.All(log.Pointer, e =>
        {
            Assert.Equal(PointerType.Touch, e.PointerType);
            Assert.Equal(2, e.PointerId);
            Assert.True(e.IsPrimary);
        });
    }

    [Fact]
    public void TheMouse_IsStampedAsTheMouse_AfterATouchToo()
    {
        var (session, a, _, log) = TwoButtons();
        using var _ = session;
        session.Window.Tap(a);
        log.Pointer.Clear();

        session.Window.Click(a);

        Assert.NotEmpty(log.Pointer);
        Assert.All(log.Pointer, e =>
        {
            Assert.Equal(PointerType.Mouse, e.PointerType);
            Assert.Equal(PointerEvent.MousePointerId, e.PointerId);
            Assert.True(e.IsPrimary);
        });
    }

    /// <summary>One leaf element, so a slide stays on the element it pressed: what decides the
    /// click is the slop alone, not a move onto a child or off one.</summary>
    private static (HeadlessSession Session, Element Pad, Log Log) OnePad()
    {
        var log = new Log();
        var pad = log.Watch(Box.New().Height(100), "pad");
        var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Flex.Column().Children(pad));
        session.PumpFrame();
        return (session, pad, log);
    }

    [Fact]
    public void AFingerThatSlidesPastTheSlop_DoesNotClick()
    {
        var (session, pad, log) = OnePad();
        using var _ = session;
        var (x, y) = Center(pad);

        session.Window.TouchDown(0, x, y);
        session.Window.TouchMove(0, x + 12, y);   // still on the pad, 12 DIP from the press
        session.Window.TouchUp(0);

        Assert.DoesNotContain("pad click 1", log.Events);
        Assert.Contains("pad up", log.Events);

        // The mouse has no slop: the same slide on the same element clicks.
        log.Events.Clear();
        session.PumpFrame(TimeSpan.FromSeconds(1));
        session.Window.MouseDown(x, y);
        session.Window.MoveMouse(x + 12, y);
        session.Window.MouseUp(x + 12, y);
        Assert.Contains("pad click 1", log.Events);
    }

    [Fact]
    public void AFingerThatWobblesInsideTheSlop_Clicks()
    {
        var (session, a, _, log) = TwoButtons();
        using var _ = session;
        var (x, y) = Center(a);

        session.Window.TouchDown(0, x, y);
        session.Window.TouchMove(0, x + 6, y + 3);
        session.Window.TouchUp(0);

        Assert.Contains("A click 1", log.Events);
    }

    [Fact]
    public void AFingerThatWobblesOffTheLabel_StillTapsTheButton_WhereTheMouseDoesNot()
    {
        var (session, a, _, log) = TwoButtons();
        using var _ = session;
        var label = a.Children.OfType<TextElement>().Single();
        var box = label.Layout.AbsoluteBorderBox;
        float x = box.X + box.Width - 1, y = box.Y + box.Height / 2;

        // Pressed on the label, lifted 6 DIP to its right, on the button's padding.
        session.Window.TouchDown(0, x, y);
        session.Window.TouchMove(0, x + 6, y);
        session.Window.TouchUp(0);
        Assert.Contains("A click 1", log.Events);

        // The mouse keeps its rule: the release has to be on the element it pressed.
        log.Events.Clear();
        session.PumpFrame(TimeSpan.FromSeconds(1));
        session.Window.MouseDown(x, y);
        session.Window.MoveMouse(x + 6, y);
        session.Window.MouseUp(x + 6, y);
        Assert.DoesNotContain("A click 1", log.Events);
    }

    [Fact]
    public void HoverEndsAtTheRelease_ForAFinger_NotForTheMouse()
    {
        var (session, a, _, _) = TwoButtons();
        using var _ = session;

        session.Window.Tap(a);
        Assert.False(a.IsHovered);
        Assert.False(a.IsPressed);

        session.Window.Click(a);
        Assert.True(a.IsHovered);   // the mouse is still over it
    }

    [Fact]
    public void TwoTapsAFingerWidthApart_AreADoubleTap_WhereTwoClicksAreNot()
    {
        var (session, a, _, log) = TwoButtons();
        using var _ = session;
        var (x, y) = Center(a);

        session.Window.Tap(x, y);
        session.PumpFrame(TimeSpan.FromMilliseconds(150));
        session.Window.Tap(x + 20, y);
        Assert.Equal("A click 2", log.Of("A").Last(e => e.Contains("click")));

        // The mouse's reach is 5 units: the same two points are two single clicks.
        session.PumpFrame(TimeSpan.FromSeconds(1));
        session.Window.Click(x, y);
        session.PumpFrame(TimeSpan.FromMilliseconds(150));
        session.Window.Click(x + 20, y);
        Assert.Equal("A click 1", log.Of("A").Last(e => e.Contains("click")));
    }

    [Fact]
    public void TapsFurtherApartInTimeThanADoubleTap_AreTwoTaps()
    {
        var (session, a, _, log) = TwoButtons();
        using var _ = session;

        session.Window.Tap(a);
        session.PumpFrame(TimeSpan.FromMilliseconds(350));   // past 300 ms, inside the mouse's 400
        session.Window.Tap(a);

        Assert.Equal(["A click 1", "A click 1"], log.Of("A").Where(e => e.Contains("click")));
    }

    [Fact]
    public void ACancel_ClicksNothing_AndEndsThePressAndTheHover()
    {
        var (session, a, _, log) = TwoButtons();
        using var _ = session;
        var (x, y) = Center(a);

        session.Window.TouchDown(0, x, y);
        Assert.True(a.IsPressed);
        session.Window.TouchCancel(0);

        Assert.Equal(["A enter", "A down", "A cancel", "A leave"], log.Of("A"));
        Assert.False(a.IsPressed);
        Assert.False(a.IsHovered);
    }

    [Fact]
    public void ACancel_GoesToThePressedElement_WhereverTheFingerIsNow()
    {
        var (session, a, b, log) = TwoButtons();
        using var _ = session;
        var (ax, ay) = Center(a);
        var (bx, by) = Center(b);

        session.Window.TouchDown(0, ax, ay);
        session.Window.TouchMove(0, bx, by);
        session.Window.TouchCancel(0);

        Assert.Contains("A cancel", log.Events);
        Assert.DoesNotContain("B cancel", log.Events);
        Assert.DoesNotContain(log.Events, e => e.Contains("click"));
    }

    [Fact]
    public void ACancel_EndsTheCapture()
    {
        var log = new Log();
        var moves = 0;
        var pad = log.Watch(Box.New().Height(100), "pad");
        pad.OnPointerDown(e => e.CapturePointer());
        pad.On<PointerMoveEvent>(_ => moves++);
        using var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Flex.Column().Children(pad, Box.New().Height(100)));
        session.PumpFrame();

        session.Window.TouchDown(0, 50, 50);
        session.Window.TouchMove(0, 50, 150);   // captured: reaches the pad from outside it
        Assert.Equal(1, moves);
        session.Window.TouchCancel(0);
        Assert.Contains("pad cancel", log.Events);
        Assert.DoesNotContain("pad up", log.Events);

        session.Window.MoveMouse(60, 160);      // the capture has ended: outside is outside
        Assert.Equal(1, moves);
    }

    [Fact]
    public void ASecondFinger_PressesNothing()
    {
        var (session, a, b, log) = TwoButtons();
        using var _ = session;
        var (ax, ay) = Center(a);
        var (bx, by) = Center(b);

        session.Window.TouchDown(0, ax, ay);
        session.Window.TouchDown(1, bx, by);
        session.Window.TouchUp(1);
        session.Window.TouchUp(0);

        Assert.Empty(log.Of("B"));
        Assert.Contains("A click 1", log.Events);
    }

    [Fact]
    public void ASecondFinger_ReachesTheCaptureHolder_AsNotPrimary()
    {
        var downs = new List<PointerDownEvent>();
        var moves = new List<PointerMoveEvent>();
        var ups = new List<PointerUpEvent>();
        var pad = Box.New().Height(100);
        pad.OnPointerDown(e => { downs.Add(e); if (e.IsPrimary) e.CapturePointer(); });
        pad.On<PointerMoveEvent>(moves.Add);
        pad.OnPointerUp(ups.Add);
        using var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Flex.Column().Children(pad, Box.New().Height(100)));
        session.PumpFrame();

        session.Window.TouchDown(0, 50, 50);
        session.Window.TouchDown(1, 200, 150);   // outside the pad: it still gets it
        session.Window.TouchMove(1, 220, 160);
        session.Window.TouchUp(1);

        Assert.Equal([true, false], downs.Select(d => d.IsPrimary));
        Assert.Equal([2, 3], downs.Select(d => d.PointerId));
        var second = Assert.Single(moves, m => !m.IsPrimary);
        Assert.Equal(3, second.PointerId);
        Assert.Equal(20, second.Delta.X);
        Assert.Equal(3, Assert.Single(ups).PointerId);
    }

    [Fact]
    public void ASecondFinger_WithNothingCaptured_GoesNowhere()
    {
        var (session, a, b, log) = TwoButtons();
        using var _ = session;
        var (bx, by) = Center(b);

        // A finger on the empty space below the buttons: nothing takes its press.
        session.Window.TouchDown(0, 200, 250);
        session.Window.TouchDown(1, bx, by);
        session.Window.TouchMove(1, bx + 5, by);
        session.Window.TouchUp(1);
        session.Window.TouchUp(0);

        Assert.Empty(log.Of("A"));
        Assert.Empty(log.Of("B"));
    }

    [Fact]
    public void PointerIds_AreNotReused_WhileAFingerIsDown()
    {
        var ids = new List<int>();
        var pad = Box.New().Height(300);
        pad.OnPointerDown(e => { ids.Add(e.PointerId); if (e.IsPrimary) e.CapturePointer(); });
        using var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(pad);
        session.PumpFrame();

        session.Window.TouchDown(0, 10, 10);   // 2
        session.Window.TouchDown(1, 20, 20);   // 3
        session.Window.TouchUp(1);
        session.Window.TouchDown(2, 30, 30);   // 4, not 3 again: finger 0 is still down
        session.Window.TouchUp(2);
        session.Window.TouchUp(0);
        session.Window.TouchDown(3, 40, 40);   // every finger lifted: 2 again
        session.Window.TouchUp(3);

        Assert.Equal([2, 3, 4, 2], ids);
    }

    [Fact]
    public void WhenTheFirstFingerLifts_NoOtherBecomesPrimary()
    {
        var (session, a, b, log) = TwoButtons();
        using var _ = session;
        var (ax, ay) = Center(a);
        var (bx, by) = Center(b);

        session.Window.TouchDown(0, ax, ay);
        session.Window.TouchDown(1, bx, by);
        session.Window.TouchUp(0);
        session.Window.TouchMove(1, bx + 2, by);
        session.Window.TouchUp(1);
        Assert.Empty(log.Of("B"));

        session.Window.Tap(b);   // every finger lifted: the next is primary again
        Assert.Contains("B click 1", log.Events);
    }

    /// <summary>A finger that rests on a tooltip's owner shows it as a long press
    /// (<see cref="TouchGestureTests"/>); one that slides over it, as a mouse would hover, does not.</summary>
    [Fact]
    public void AFingerOpensNoTooltip_WhereTheMouseDoes()
    {
        var target = Button.New("Hold").Width(300).Height(40).Tooltip("tip");
        using var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Flex.Column().Children(target));
        session.PumpFrame();
        var (x, y) = Center(target);

        // Pressed and slid past the slop (no long press then), and resting on the owner: a mouse
        // moved there while pressed opens the tooltip once it rests.
        session.Window.TouchDown(0, x, y);
        session.Window.TouchMove(0, x + 20, y);
        session.PumpFrame(TimeSpan.FromMilliseconds(400));
        session.PumpFrame(TimeSpan.FromSeconds(1));
        Assert.Empty(TooltipTexts(session));
        session.Window.TouchUp(0);

        session.Window.MoveMouse(x, y);
        session.PumpFrame(TimeSpan.FromMilliseconds(400));
        session.PumpFrame(TimeSpan.FromSeconds(1));
        Assert.Equal(["tip"], TooltipTexts(session));
    }

    [Fact]
    public void ASecondFinger_DoesNotMoveASlidersThumb()
    {
        var slider = Slider.New(value: 0, min: 0, max: 100).Width(400);
        using var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Flex.Column().Children(slider));
        session.PumpFrame();
        var box = slider.Layout.AbsoluteBorderBox;
        float y = box.Y + box.Height / 2;

        session.Window.TouchDown(0, box.X + box.Width * 0.25f, y);
        double first = slider.Value;
        session.Window.TouchDown(1, box.X + box.Width * 0.75f, y);
        session.Window.TouchMove(1, box.X + box.Width * 0.8f, y);
        session.PumpFrame();
        Assert.Equal(first, slider.Value);
        session.Window.TouchUp(1);

        session.Window.TouchMove(0, box.X + box.Width * 0.5f, y);   // the first finger still drags
        session.PumpFrame();
        Assert.True(slider.Value > first);
        session.Window.TouchUp(0);
    }

    [Fact]
    public void ACancelledSliderDrag_Stops_AndKeepsItsValue()
    {
        var slider = Slider.New(value: 0, min: 0, max: 100).Width(400);
        using var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Flex.Column().Children(slider));
        session.PumpFrame();
        var box = slider.Layout.AbsoluteBorderBox;
        float y = box.Y + box.Height / 2;

        session.Window.TouchDown(0, box.X + box.Width * 0.25f, y);
        session.Window.TouchMove(0, box.X + box.Width * 0.5f, y);
        session.PumpFrame();
        double reached = slider.Value;
        session.Window.TouchCancel(0);

        session.Window.MoveMouse(box.X + box.Width * 0.9f, y);   // a hover: no drag any more
        session.PumpFrame();
        Assert.Equal(reached, slider.Value);
    }

    private static List<string> TooltipTexts(HeadlessSession session) =>
        session.Window.OverlayContents
            .SelectMany(o => o.Children)
            .OfType<TextElement>()
            .Select(t => t.Content)
            .ToList();
}

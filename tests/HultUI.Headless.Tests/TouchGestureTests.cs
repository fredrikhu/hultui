using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Touch gestures (docs/design/GESTURES.md): a finger that moves past the slop scrolls the
/// innermost scroller that can move that way, 1:1, and cancels the press it began; a flick throws
/// the content, on virtual time; a finger held still is a long press, which opens the context
/// menu, or else the tooltip; a press that captured the pointer keeps the finger from every
/// scroller. The mouse and the wheel are unchanged: the existing suites.
/// </summary>
public sealed class TouchGestureTests
{
    private const float Row = 40;

    private static (ScrollViewElement List, List<ButtonElement> Rows, List<string> Clicks) List(int rows = 30)
    {
        var clicks = new List<string>();
        var buttons = new List<ButtonElement>();
        for (int i = 0; i < rows; i++)
        {
            string name = $"row {i}";
            buttons.Add(Button.New(name).Height(Row).FlexShrink(0).OnClick(_ => clicks.Add(name)));
        }
        var list = ScrollView.Vertical().Height(300).Children([.. buttons]);
        return (list, buttons, clicks);
    }

    private static HeadlessSession Mount(Element root)
    {
        var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(root);
        session.PumpFrame();
        return session;
    }

    /// <summary>A finger drags from <paramref name="from"/> to <paramref name="to"/> in 10 DIP steps a
    /// frame apart, rests 100 ms (so no fling), and lifts.</summary>
    private static void Drag(HeadlessSession session, Point from, Point to)
    {
        var w = session.Window;
        w.TouchDown(0, from.X, from.Y);
        float length = MathF.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
        int steps = Math.Max(1, (int)MathF.Round(length / 10));
        for (int i = 1; i <= steps; i++)
        {
            session.PumpFrame();
            float t = i / (float)steps;
            w.TouchMove(0, from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
        }
        session.PumpFrame(TimeSpan.FromMilliseconds(100));
        w.TouchUp(0);
        session.PumpFrame();
    }

    [Fact]
    public void ATap_StillClicks_InAList()
    {
        var (list, rows, clicks) = List();
        using var session = Mount(list);

        session.Window.Tap(rows[2]);

        Assert.Equal(["row 2"], clicks);
    }

    [Fact]
    public void AVerticalDrag_ScrollsTheList_FollowingTheFingerPastTheSlop_AndClicksNothing()
    {
        var (list, rows, clicks) = List();
        using var session = Mount(list);

        Drag(session, new Point(200, 250), new Point(200, 150));

        Assert.Equal(100 - PointerState.TouchSlop, list.ScrollOffsetY, 0.01f);
        Assert.Empty(clicks);
        Assert.False(rows[6].IsPressed);
    }

    [Fact]
    public void TheMouse_DraggingInAList_ScrollsNothing()
    {
        var (list, _, _) = List();
        using var session = Mount(list);

        session.Window.MouseDown(200, 250);
        for (int y = 240; y >= 150; y -= 10) session.Window.MoveMouse(200, y);
        session.Window.MouseUp(200, 150);

        Assert.Equal(0, list.ScrollOffsetY);
    }

    [Fact]
    public void APressedRowGetsPointerCancel_WhenTheListTakesTheFinger()
    {
        var (list, rows, _) = List();
        var events = new List<string>();
        rows[6].OnPointerCancel(_ => events.Add("cancel")).OnPointerUp(_ => events.Add("up"));
        using var session = Mount(list);

        Drag(session, new Point(200, 250), new Point(200, 150));

        Assert.Equal(["cancel"], events);
    }

    [Fact]
    public void AHorizontalDragOnAShelf_ScrollsTheShelf_AndAVerticalOneThePage()
    {
        var posters = Enumerable.Range(0, 12).Select(i => (Element)Box.New().Width(100).Height(80).FlexShrink(0)).ToArray();
        var shelf = ScrollView.Horizontal().Height(80).FlexShrink(0).Children(posters);
        var filler = Enumerable.Range(0, 10).Select(_ => (Element)Box.New().Height(Row).FlexShrink(0));
        var page = ScrollView.Vertical().Height(300).Children([shelf, .. filler]);
        using var session = Mount(page);

        Drag(session, new Point(300, 40), new Point(150, 45));   // mostly sideways, on the shelf
        Assert.Equal(150 - PointerState.TouchSlop, shelf.ScrollOffsetX, 0.5f);
        Assert.Equal(0, page.ScrollOffsetY);

        Drag(session, new Point(200, 60), new Point(195, 20));   // mostly up, on the same shelf
        Assert.Equal(40 - PointerState.TouchSlop, page.ScrollOffsetY, 0.5f);
        Assert.Equal(150 - PointerState.TouchSlop, shelf.ScrollOffsetX, 0.5f);
    }

    [Fact]
    public void AnInnerListAtItsTop_LetsTheOuterOneTakeAPullDown()
    {
        var (inner, _, _) = List(10);
        inner.Height(200).FlexShrink(0);
        var filler = Enumerable.Range(0, 20).Select(_ => (Element)Box.New().Height(Row).FlexShrink(0));
        var outer = ScrollView.Vertical().Height(300).Children([Box.New().Height(100).FlexShrink(0), inner, .. filler]);
        using var session = Mount(outer);
        outer.ScrollOffsetY = 80;   // the inner list now starts at y = 20
        session.PumpFrame();

        Drag(session, new Point(200, 100), new Point(200, 160));   // a pull down on the inner list

        Assert.Equal(0, inner.ScrollOffsetY);                        // at its top: cannot go further
        Assert.Equal(80 - (60 - PointerState.TouchSlop), outer.ScrollOffsetY, 0.5f);
    }

    [Fact]
    public void ASliderInAList_TakesSidewaysDrags_AndGivesVerticalOnesToTheList()
    {
        var slider = Slider.New(value: 0, min: 0, max: 100).Width(400).FlexShrink(0);
        var filler = Enumerable.Range(0, 20).Select(_ => (Element)Box.New().Height(Row).FlexShrink(0));
        var list = ScrollView.Vertical().Height(300).Children([Box.New().Height(100).FlexShrink(0), slider, .. filler]);
        using var session = Mount(list);
        float y = slider.Layout.AbsoluteBorderBox.Y + slider.Layout.AbsoluteBorderBox.Height / 2;

        Drag(session, new Point(100, y), new Point(100, y - 60));   // up, on the slider
        Assert.Equal(0, slider.Value);
        Assert.Equal(60 - PointerState.TouchSlop, list.ScrollOffsetY, 0.5f);

        y -= list.ScrollOffsetY;
        Drag(session, new Point(100, y), new Point(300, y));         // sideways, on the slider
        Assert.InRange(slider.Value, 70, 80);   // 300 of 400, less the thumb's inset
        Assert.Equal(60 - PointerState.TouchSlop, list.ScrollOffsetY, 0.5f);
    }

    [Fact]
    public void ATapOnASlider_SetsItsValue()
    {
        var slider = Slider.New(value: 0, min: 0, max: 100).Width(400);
        using var session = Mount(Flex.Column().Children(slider));
        var box = slider.Layout.AbsoluteBorderBox;

        session.Window.Tap(box.X + box.Width * 0.5f, box.Y + box.Height / 2);

        Assert.Equal(50, slider.Value, 1);
    }

    [Fact]
    public void APressThatCapturesThePointer_KeepsTheFingerFromTheList()
    {
        var (list, rows, _) = List();
        int moves = 0;
        rows[6].OnPointerDown(e => e.CapturePointer());
        rows[6].On<PointerMoveEvent>(_ => moves++);
        using var session = Mount(list);

        Drag(session, new Point(200, 250), new Point(200, 150));

        Assert.Equal(0, list.ScrollOffsetY);
        Assert.Equal(10, moves);
    }

    [Fact]
    public void AnOnPanElement_GetsStartUpdatesAndEnd_WithTheTotalSinceThePress()
    {
        var log = new List<string>();
        Point total = default, velocity = default;
        var card = Box.New().Height(100).OnPan(PanAxis.Horizontal,
            _ => log.Add("start"),
            e => { log.Add("update"); total = e.Total; },
            e => { log.Add(e.Cancelled ? "cancelled" : "end"); velocity = e.Velocity; });
        using var session = Mount(Flex.Column().Children(card));

        session.Window.Swipe(new Point(50, 50), new Point(250, 50), TimeSpan.FromMilliseconds(100));

        Assert.Equal("start", log[0]);
        Assert.Equal("end", log[^1]);
        Assert.Equal(200, total.X, 0.01f);
        Assert.Equal(0, total.Y);
        Assert.InRange(velocity.X, 1500, 2500);   // 200 DIP in 0.1 s
    }

    [Fact]
    public void AnOnPanElement_IgnoresTheOtherAxis()
    {
        int updates = 0;
        var card = Box.New().Height(200).OnPan(PanAxis.Horizontal, null, _ => updates++, null);
        using var session = Mount(Flex.Column().Children(card));

        Drag(session, new Point(100, 150), new Point(100, 50));

        Assert.Equal(0, updates);
    }

    [Fact]
    public void AFlick_ThrowsTheList_AndItSlowsToAStop()
    {
        var (list, _, clicks) = List(100);
        using var session = Mount(list);

        session.Window.Swipe(new Point(200, 250), new Point(200, 150), TimeSpan.FromMilliseconds(50));   // 2,000 DIP/s
        float atLift = list.ScrollOffsetY;
        session.PumpFrames(180);   // 3 s at 60 Hz: long past the stop

        // 2,000 DIP/s × 325 ms ≈ 650 past the lift.
        Assert.InRange(list.ScrollOffsetY - atLift, 600, 700);
        Assert.False(session.PumpFrame());   // stopped: no frames asked for
        Assert.Empty(clicks);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(120)]
    public void AFlick_EndsAtTheSameOffset_AtAnyFrameRate(int hz)
    {
        float at60 = FlingEnd(60);
        Assert.Equal(at60, FlingEnd(hz), 1f);

        static float FlingEnd(int hz)
        {
            var (list, _, _) = List(100);
            using var session = Mount(list);
            session.Window.Swipe(new Point(200, 250), new Point(200, 150), TimeSpan.FromMilliseconds(50));
            var frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / hz);
            for (int i = 0; i < hz * 4; i++) session.PumpFrame(frame);
            return list.ScrollOffsetY;
        }
    }

    [Fact]
    public void AFlick_StopsAtTheEnd_WithNoBounce()
    {
        var (list, _, _) = List(12);   // 480 high in 300: 180 to scroll
        using var session = Mount(list);

        session.Window.Swipe(new Point(200, 250), new Point(200, 150), TimeSpan.FromMilliseconds(30));
        session.PumpFrames(120);

        Assert.Equal(180, list.ScrollOffsetY, 0.01f);
        Assert.False(session.PumpFrame());
    }

    [Fact]
    public void ATouchDuringAFling_StopsIt_WithoutClicking()
    {
        var (list, rows, clicks) = List(100);
        using var session = Mount(list);
        session.Window.Swipe(new Point(200, 250), new Point(200, 150), TimeSpan.FromMilliseconds(50));
        session.PumpFrames(5);

        session.Window.Tap(200, 100);
        float stoppedAt = list.ScrollOffsetY;
        session.PumpFrames(60);

        Assert.Equal(stoppedAt, list.ScrollOffsetY);
        Assert.Empty(clicks);

        session.Window.Tap(200, 100);   // with the list at rest, a tap is a click again
        Assert.Single(clicks);
    }

    [Fact]
    public void TheWheel_StopsAFling()
    {
        var (list, _, _) = List(100);
        using var session = Mount(list);
        session.Window.Swipe(new Point(200, 250), new Point(200, 150), TimeSpan.FromMilliseconds(50));
        session.PumpFrames(5);

        session.Window.Wheel(200, 150, 0, 0);
        float stoppedAt = list.ScrollOffsetY;
        session.PumpFrames(30);

        Assert.Equal(stoppedAt, list.ScrollOffsetY);
    }

    [Fact]
    public void AFlick_AcrossARebuildThatReplacedTheList_EndsWhereItWouldHave()
    {
        float undisturbed = Run(replace: false);
        Assert.Equal(undisturbed, Run(replace: true), 1f);

        static float Run(bool replace)
        {
            var host = Flex.Column();
            host.Children(List(100).List);
            using var session = Mount(host);
            session.Window.Swipe(new Point(200, 250), new Point(200, 150), TimeSpan.FromMilliseconds(50));
            session.PumpFrames(10);
            if (replace) host.ReplaceChildren(List(100).List);
            session.PumpFrames(240);
            return ((ScrollViewElement)host.Children[0]).ScrollOffsetY;
        }
    }

    [Fact]
    public void ALongPress_OpensTheContextMenu_AndClicksNothing()
    {
        var (list, rows, clicks) = List();
        rows[2].ContextMenu([new ContextMenuItem { Label = "Delete" }]);
        using var session = Mount(list);

        session.Window.LongPress(rows[2]);
        session.PumpFrame();

        Assert.Empty(clicks);
        Assert.Contains(session.Window.OverlayContents, o => o is ContextMenuElement);
    }

    [Theory]
    [InlineData(false, -1)]   // nothing highlighted under a finger
    [InlineData(true, 0)]     // a reader's focus goes to the first item
    public void ALongPressMenu_HasItsFirstItemCurrent_OnlyWithAReaderOn(bool reader, int current)
    {
        var (list, rows, _) = List();
        rows[2].ContextMenu([new ContextMenuItem { Label = "Rename" }, new ContextMenuItem { Label = "Delete" }]);
        using var session = Mount(list);
        var was = HultUI.Core.Accessibility.AccessibilityClients.Force;
        HultUI.Core.Accessibility.AccessibilityClients.Force = reader;
        try
        {
            session.Window.LongPress(rows[2]);
            session.PumpFrame();
            var menu = Assert.Single(session.Window.OverlayContents.OfType<ContextMenuElement>());
            Assert.Equal(current, ((HultUI.Core.Accessibility.IAccessibleItemSource)menu).CurrentIndex);
        }
        finally { HultUI.Core.Accessibility.AccessibilityClients.Force = was; }
    }

    [Fact]
    public void ALongPress_WithNoMenu_ShowsTheTooltip_WhileTheFingerStaysDown()
    {
        var (list, rows, clicks) = List();
        rows[2].Tooltip("Row two");
        using var session = Mount(list);
        var box = rows[2].Layout.AbsoluteBorderBox;

        session.Window.TouchDown(0, box.X + box.Width / 2, box.Y + box.Height / 2);
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        Assert.Empty(TooltipTexts(session));
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        Assert.Equal(["Row two"], TooltipTexts(session));

        session.Window.TouchUp(0);
        session.PumpFrame();
        Assert.Empty(TooltipTexts(session));
        Assert.Empty(clicks);
    }

    [Fact]
    public void AnOnLongPressHandler_IsTheLongPressesMeaning_NotTheMenu()
    {
        var (list, rows, clicks) = List();
        var held = new List<PointerType>();
        rows[2].ContextMenu([new ContextMenuItem { Label = "Delete" }]).OnLongPress(e => held.Add(e.PointerType));
        using var session = Mount(list);

        session.Window.LongPress(rows[2]);
        session.PumpFrame();

        Assert.Equal([PointerType.Touch], held);
        Assert.DoesNotContain(session.Window.OverlayContents, o => o is ContextMenuElement);
        Assert.Empty(clicks);
    }

    [Fact]
    public void AFingerThatMovesBeforeTheLongPressTime_IsNoLongPress()
    {
        var (list, rows, _) = List();
        int held = 0;
        rows[2].OnLongPress(_ => held++);
        using var session = Mount(list);
        var box = rows[2].Layout.AbsoluteBorderBox;
        float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;

        session.Window.TouchDown(0, x, y);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        session.Window.TouchMove(0, x, y - 20);
        session.PumpFrame(TimeSpan.FromMilliseconds(600));
        session.Window.TouchUp(0);

        Assert.Equal(0, held);
    }

    private static List<string> TooltipTexts(HeadlessSession session) =>
        session.Window.OverlayContents
            .SelectMany(o => o.Children)
            .OfType<TextElement>()
            .Select(t => t.Content)
            .ToList();
}

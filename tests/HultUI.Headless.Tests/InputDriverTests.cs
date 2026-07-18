using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

// Inside the HultUI.* namespace hierarchy, bare `Text` binds to the HultUI.Text
// namespace (the text assembly), not the builder. An alias placed inside the
// namespace wins that lookup; one placed at the top of the file would not.
using Text = HultUI.Core.Builders.Text;

public class InputDriverTests
{
    [Fact]
    public void Click_FiresElementHandler()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        int clicks = 0;
        var button = Box.New().Padding(12).Children(Text.New("Click me"));
        button.OnClick(_ => clicks++);
        session.Window.SetRoot(Flex.Column().Padding(20).Children(button));
        session.PumpFrame();

        session.Window.Click(button);
        Assert.Equal(1, clicks);

        session.Window.Click(button);
        Assert.Equal(2, clicks);
    }

    [Fact]
    public void DoubleClick_WithinVirtualWindow_Fires()
    {
        using var session = HeadlessSession.Create();
        int doubleClicks = 0;
        var box = Box.New().Width(100).Height(100);
        box.On<DoubleClickEvent>(_ => doubleClicks++);
        session.Window.SetRoot(box);
        session.PumpFrame();

        session.Window.Click(box);
        session.AdvanceTime(TimeSpan.FromMilliseconds(399));
        session.Window.Click(box);
        Assert.Equal(1, doubleClicks);
    }

    [Fact]
    public void DoubleClick_OutsideVirtualWindow_DoesNotFire()
    {
        using var session = HeadlessSession.Create();
        int doubleClicks = 0;
        var box = Box.New().Width(100).Height(100);
        box.On<DoubleClickEvent>(_ => doubleClicks++);
        session.Window.SetRoot(box);
        session.PumpFrame();

        session.Window.Click(box);
        session.AdvanceTime(TimeSpan.FromMilliseconds(401));
        session.Window.Click(box);
        Assert.Equal(0, doubleClicks);
    }

    [Fact]
    public void TabTraversal_MovesFocusInTabIndexOrder()
    {
        using var session = HeadlessSession.Create();
        var first = TextInput.New("first");
        var second = TextInput.New("second");
        session.Window.SetRoot(Flex.Column(gap: 8).Padding(10).Children(first, second));
        session.PumpFrame();

        session.Window.Click(first);
        Assert.Same(first, session.Window.Window.FocusedElement);

        session.Window.PressKey(KeyCode.Tab);
        Assert.Same(second, session.Window.Window.FocusedElement);

        session.Window.PressKey(KeyCode.Tab, KeyModifiers.Shift);
        Assert.Same(first, session.Window.Window.FocusedElement);
    }

    [Fact]
    public void TypeText_InsertsIntoFocusedInput()
    {
        using var session = HeadlessSession.Create();
        var input = TextInput.New();
        session.Window.SetRoot(Flex.Column().Padding(10).Children(input));
        session.PumpFrame();

        session.Window.Click(input);
        session.Window.TypeText("hello");
        Assert.Equal("hello", input.Text);

        // The framework armed OS text input on focus — assert via the recorded host.
        Assert.True(session.Window.TextInputHost.IsTextInputActive);
    }

    [Fact]
    public void CopyPaste_RoundTripsThroughHeadlessClipboard()
    {
        using var session = HeadlessSession.Create();
        var input = TextInput.New("secret");
        session.Window.SetRoot(Flex.Column().Padding(10).Children(input));
        session.PumpFrame();

        session.Window.Click(input);
        session.Window.PressKey(KeyCode.A, KeyModifiers.Ctrl);  // select all
        session.Window.PressKey(KeyCode.C, KeyModifiers.Ctrl);  // copy
        Assert.Equal("secret", session.Window.Clipboard.Text);

        session.Window.PressKey(KeyCode.V, KeyModifiers.Ctrl);  // paste over selection
        Assert.Equal("secret", input.Text);
    }

    [Fact]
    public void Wheel_ScrollsScrollView()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var scrollView = ScrollView.New().Height(150).Children(
            Box.New().Height(1000));
        session.Window.SetRoot(scrollView);
        session.PumpFrame();

        Assert.Equal(0, scrollView.ScrollOffsetY);
        // Negative deltaY = scroll down, SDL convention.
        session.Window.Wheel(100, 100, 0, -3);
        Assert.True(scrollView.ScrollOffsetY > 0,
            $"Expected scroll offset > 0, was {scrollView.ScrollOffsetY}");
    }

    [Fact]
    public void Tooltip_AppearsOnlyAfterVirtualShowDelay()
    {
        using var session = HeadlessSession.Create();
        var box = Box.New().Width(100).Height(50).Tooltip("Helpful hint");
        session.Window.SetRoot(Flex.Column().Padding(10).Children(box));
        session.PumpFrame();

        session.Window.MoveMouse(50, 30);   // hover starts the show-delay timer
        session.PumpFrame(TimeSpan.FromMilliseconds(100));
        Assert.Empty(session.Window.OverlayContents);

        // Default Theme.Tooltip.ShowDelay is 0.5 s of virtual time.
        session.PumpFrame(TimeSpan.FromMilliseconds(450));
        Assert.NotEmpty(session.Window.OverlayContents);
    }

    [Fact]
    public void Drag_TransfersDataBetweenElements()
    {
        using var session = HeadlessSession.Create(width: 400, height: 200);
        string? dropped = null;

        var source = Box.New().Width(80).Height(80).Draggable();
        source.On<DragStartEvent>(e => e.DragData.Set("payload", "cargo"));

        var target = Box.New().Width(80).Height(80);
        target.OnDragOver(e => e.Accepted = true);
        target.OnDrop(e => dropped = e.DragData.TryGet<string>("payload", out var v) ? v : null);

        session.Window.SetRoot(Flex.Row(gap: 100).Padding(10).Children(source, target));
        session.PumpFrame();

        var from = Center(source);
        var to = Center(target);
        session.Window.Drag(from, to, steps: 8);

        Assert.Equal("cargo", dropped);
    }

    [Fact]
    public void MoveMouse_AppliesElementCursor()
    {
        using var session = HeadlessSession.Create();
        var link = Box.New().Width(100).Height(40).Cursor(CursorType.Pointer);
        session.Window.SetRoot(Flex.Column().Padding(10).Children(link));
        session.PumpFrame();

        session.Window.MoveMouse(60, 30);
        Assert.Equal(CursorType.Pointer, session.Window.LastCursor);

        session.Window.MoveMouse(5, 5);   // off the element
        Assert.Equal(CursorType.Default, session.Window.LastCursor);
    }

    [Fact]
    public void HoverEnterLeave_FireOnMoves()
    {
        using var session = HeadlessSession.Create();
        int enters = 0, leaves = 0;
        var box = Box.New().Width(100).Height(50);
        box.OnPointerEnter(_ => enters++);
        box.OnPointerLeave(_ => leaves++);
        session.Window.SetRoot(Flex.Column().Padding(10).Children(box));
        session.PumpFrame();

        session.Window.MoveMouse(50, 30);
        Assert.Equal(1, enters);
        Assert.Equal(0, leaves);

        session.Window.MoveMouse(300, 300);
        Assert.Equal(1, leaves);
    }

    private static Point Center(Element element)
    {
        var box = element.Layout.AbsoluteBorderBox;
        return new Point(box.X + box.Width / 2f, box.Y + box.Height / 2f);
    }
}

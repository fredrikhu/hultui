using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

public class NumericUpDownTests
{
    private static NumericUpDownElement Mount(HeadlessSession session, double value = 5, double min = 0, double max = 10, double step = 1)
    {
        var n = NumericUpDown.New(value, min, max, step);
        session.Window.SetRoot(Flex.Row().AlignItems(Core.Layout.AlignItems.FlexStart).Padding(new Thickness(40)).Children(n));
        session.PumpFrame();
        return n;
    }

    private static (float x, float y) UpButton(NumericUpDownElement n)
    {
        var box = n.Layout.AbsoluteBorderBox;
        return (box.Right - 8, box.Y + box.Height * 0.25f);
    }

    private static (float x, float y) DownButton(NumericUpDownElement n)
    {
        var box = n.Layout.AbsoluteBorderBox;
        return (box.Right - 8, box.Y + box.Height * 0.75f);
    }

    [Fact]
    public void Buttons_Step_AndReport()
    {
        using var session = HeadlessSession.Create();
        var n = Mount(session);
        var seen = new List<double>();
        n.OnValueChanged(seen.Add);

        var (ux, uy) = UpButton(n);
        session.Window.Click(ux, uy);
        var (dx, dy) = DownButton(n);
        session.Window.Click(dx, dy);
        session.Window.Click(dx, dy);

        Assert.Equal([6, 5, 4], seen);
        Assert.Equal(4, n.Value);
    }

    [Fact]
    public void HeldButton_RepeatsAfterTheDelay_AndStopsOnRelease()
    {
        using var session = HeadlessSession.Create();
        var n = Mount(session, value: 0, max: 100);

        var (x, y) = UpButton(n);
        session.Window.MouseDown(x, y);
        Assert.Equal(1, n.Value);                              // the press itself
        session.PumpFrame(TimeSpan.FromMilliseconds(300));     // inside the 400 ms delay
        Assert.Equal(1, n.Value);
        session.PumpFrame(TimeSpan.FromMilliseconds(400));     // 300 ms past it at 60 ms/step
        Assert.InRange(n.Value, 5, 7);

        session.Window.MouseUp(x, y);
        double after = n.Value;
        session.PumpFrame(TimeSpan.FromMilliseconds(500));
        Assert.Equal(after, n.Value);
    }

    [Fact]
    public void Keys_Step_Page_AndJumpToTheBounds()
    {
        using var session = HeadlessSession.Create();
        var n = Mount(session, value: 50, max: 100);
        session.Window.Click(n);   // focus (the text half)

        session.Window.PressKey(KeyCode.Up);
        Assert.Equal(51, n.Value);
        session.Window.PressKey(KeyCode.PageDown);
        Assert.Equal(41, n.Value);
        session.Window.PressKey(KeyCode.End);
        Assert.Equal(100, n.Value);
        session.Window.PressKey(KeyCode.Home);
        Assert.Equal(0, n.Value);
    }

    [Fact]
    public void Typing_ReplacesTheValue_OnEnter_AndEscapeReverts()
    {
        using var session = HeadlessSession.Create();
        var n = Mount(session, value: 5, max: 100);
        session.Window.Click(n);

        session.Window.TypeText("42");
        Assert.True(n.IsEditing);
        Assert.Equal("42", n.DisplayText);
        Assert.Equal(5, n.Value);                 // not yet
        session.Window.PressKey(KeyCode.Enter);
        Assert.Equal(42, n.Value);
        Assert.False(n.IsEditing);

        session.Window.TypeText("9");
        session.Window.PressKey(KeyCode.Escape);
        Assert.Equal(42, n.Value);
        Assert.Equal("42", n.DisplayText);
    }

    [Fact]
    public void Typing_OutOfRange_ClampsOnCommit()
    {
        using var session = HeadlessSession.Create();
        var n = Mount(session, value: 5, max: 10);
        session.Window.Click(n);
        session.Window.TypeText("250");
        session.Window.PressKey(KeyCode.Enter);
        Assert.Equal(10, n.Value);
    }

    [Fact]
    public void Focus_ArmsTextInput_SoTypedCharactersArriveOnARealWindow()
    {
        using var session = HeadlessSession.Create();
        var n = Mount(session);
        Assert.False(session.Window.TextInputHost.IsTextInputActive);
        session.Window.Click(n);
        Assert.True(session.Window.TextInputHost.IsTextInputActive);
    }

    [Fact]
    public void ReadsAsASpinButton()
    {
        using var session = HeadlessSession.Create();
        var n = Mount(session);
        n.AccessibleName = "Font size";
        session.PumpFrame();
        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.SpinButton, "Font size"));
    }
}

using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A control that acts on a key marks it handled, as a text field marks the keys it takes, so an
/// app's root that acts on the same key (a list's Enter, a player's Space) can leave it. Before,
/// Space on a focused button in Aperture pressed the button, then bubbled to the root bare and
/// played the card under the shelf's cursor as well (docs/FOCUS.md, "Tab reaches your handler
/// first").
/// </summary>
public class PressedKeyHandledTests
{
    private static (HeadlessSession Session, List<(KeyCode Key, bool Handled)> Root) Host(Element control)
    {
        var session = HeadlessSession.Create(200, 120);
        var seen = new List<(KeyCode, bool)>();
        var root = Box.New().Padding(20f).Children(control);
        root.On<KeyDownEvent>(e => seen.Add((e.Key, e.Handled)));
        session.Application.SetRoot(root);
        session.PumpFrames(2);
        control.Focus();
        return (session, seen);
    }

    [Theory]
    [InlineData(KeyCode.Enter)]
    [InlineData(KeyCode.Space)]
    public void AButtonPressedByAKey_MarksTheKeyHandled(KeyCode key)
    {
        int clicks = 0;
        var button = ButtonElement.New("Go");
        button.OnClick(_ => clicks++);
        var (session, root) = Host(button);
        using (session)
        {
            session.Window.PressKey(key);
            session.PumpFrame();

            Assert.Equal(1, clicks);
            Assert.Equal(new[] { (key, true) }, root);
        }
    }

    [Fact]
    public void ADisabledButton_LeavesTheKeyAlone()
    {
        var button = ButtonElement.New("Go");
        var (session, root) = Host(button);
        using (session)
        {
            button.IsEnabled = false;
            session.Window.PressKey(KeyCode.Space);
            session.PumpFrame();

            Assert.Equal(new[] { (KeyCode.Space, false) }, root);
        }
    }

    [Fact]
    public void ACheckBoxToggledBySpace_MarksItHandled_AndLeavesOtherKeys()
    {
        var box = CheckboxElement.New(false, "Subtitles");
        var (session, root) = Host(box);
        using (session)
        {
            session.Window.PressKey(KeyCode.Space);
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrame();

            Assert.True(box.IsChecked);
            Assert.Equal(new[] { (KeyCode.Space, true), (KeyCode.Down, false) }, root);
        }
    }

    [Fact]
    public void ARadioButtonChosenBySpace_MarksItHandled()
    {
        var radio = RadioButtonElement.New("quality", false, "Original");
        var (session, root) = Host(radio);
        using (session)
        {
            session.Window.PressKey(KeyCode.Space);
            session.PumpFrame();

            Assert.True(radio.IsChecked);
            Assert.Equal(new[] { (KeyCode.Space, true) }, root);
        }
    }
}

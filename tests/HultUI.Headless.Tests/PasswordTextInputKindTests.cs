using HultUI.Core.Builders;
using HultUI.Core.Platform;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// What a field tells the platform's text input when it takes the keyboard. A password field
/// asks for password input — on Wayland the input method is told so, on Windows the window
/// loses its input context — and an ordinary field asks for text again, so an input method
/// switched off for a password does not stay off for the next field.
/// </summary>
public class PasswordTextInputKindTests
{
    [Fact]
    public void FocusAsksForTheKindOfTheFieldThatTakesIt()
    {
        using var session = HeadlessSession.Create(300, 160);
        var username = TextInput.New("fredrik");
        var password = TextInput.New("hunter2").Password();
        session.Application.SetRoot(Box.New().Padding(10f).Children(username, password));
        session.PumpFrames(2);
        var host = session.Window.TextInputHost;

        password.Focus();
        session.PumpFrames(1);
        Assert.True(host.IsTextInputActive);
        Assert.Equal(TextInputKind.Password, host.Kind);

        username.Focus();
        session.PumpFrames(1);
        Assert.Equal(TextInputKind.Text, host.Kind);
    }

    /// <summary>A show-password toggle, both ways, while the field keeps the keyboard.</summary>
    [Fact]
    public void MaskingTheFocusedFieldSwitchesItsInput()
    {
        using var session = HeadlessSession.Create(300, 160);
        var field = TextInput.New("hunter2");
        session.Application.SetRoot(Box.New().Padding(10f).Children(field));
        session.PumpFrames(2);
        var host = session.Window.TextInputHost;

        field.Focus();
        session.PumpFrames(1);
        Assert.Equal(TextInputKind.Text, host.Kind);

        field.Masked = true;
        Assert.Equal(TextInputKind.Password, host.Kind);

        field.Masked = false;
        Assert.Equal(TextInputKind.Text, host.Kind);
    }
}

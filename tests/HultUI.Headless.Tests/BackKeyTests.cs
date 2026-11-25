using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="KeyCode.Back"/> (docs/design/ANDROID-PLATFORM.md §4): Android's Back, which SDL
/// sends as a key. The app's handlers first; unhandled, what Escape would do (close the topmost
/// menu, dropdown or dialog); with nothing for Escape to do, the system's own back
/// (<c>Application.SystemBack</c>, which on Android moves the app to the background).
/// </summary>
public sealed class BackKeyTests : IDisposable
{
    private readonly Action? _systemBackBefore = Application.SystemBack;
    private int _systemBacks;

    public BackKeyTests() => Application.SystemBack = () => _systemBacks++;

    public void Dispose() => Application.SystemBack = _systemBackBefore;

    [Fact]
    public void WithNothingToClose_TheSystemsBackRuns()
    {
        using var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Flex.Column().Children(Button.New("Hello")));
        session.PumpFrame();

        session.Window.PressKey(KeyCode.Back);

        Assert.Equal(1, _systemBacks);
    }

    [Fact]
    public void AnAppThatTakesBack_KeepsIt()
    {
        using var session = HeadlessSession.Create(400, 300);
        var pages = new List<string>();
        var root = Flex.Column().Children(Button.New("Hello"));
        // A page's history going back, as Aperture's does.
        root.OnKeyDown(e =>
        {
            if (e.Key != KeyCode.Back) return;
            pages.Add("back");
            e.Handled = true;
        });
        session.Window.SetRoot(root);
        session.PumpFrame();

        session.Window.PressKey(KeyCode.Back);

        Assert.Equal(["back"], pages);
        Assert.Equal(0, _systemBacks);
    }

    [Fact]
    public void AnOpenDropdown_IsClosed_AndTheAppStays()
    {
        using var session = HeadlessSession.Create(400, 300);
        var dropdown = Dropdown.New(["One", "Two", "Three"], 0);
        session.Window.SetRoot(Flex.Column().Padding(20f).Children(dropdown));
        session.PumpFrame();
        session.Window.Click(dropdown);
        session.PumpFrame();
        Assert.True(dropdown.IsOpen, "the dropdown did not open");

        session.Window.PressKey(KeyCode.Back);
        session.PumpFrame();

        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, _systemBacks);
    }

    [Fact]
    public void AnOpenContextMenu_IsClosedFirst_BeforeTheAppHearsBack()
    {
        using var session = HeadlessSession.Create(400, 300);
        int appBacks = 0;
        var box = Box.New().Width(120f).Height(30f);
        box.ContextMenu([new ContextMenuItem { Label = "Copy" }]);
        var root = Flex.Column().Padding(20f).Children(box);
        root.OnKeyDown(e => { if (e.Key == KeyCode.Back) { appBacks++; e.Handled = true; } });
        session.Window.SetRoot(root);
        session.PumpFrame();
        session.Window.Click(box, MouseButton.Right);
        session.PumpFrame();
        Assert.Single(session.Window.OverlayContents.OfType<ContextMenuElement>());

        session.Window.PressKey(KeyCode.Back);
        session.PumpFrame();

        Assert.Empty(session.Window.OverlayContents.OfType<ContextMenuElement>());
        Assert.Equal(0, appBacks);
        Assert.Equal(0, _systemBacks);
    }

    [Fact]
    public void AnOpenDialog_IsClosed_AsEscapeClosesIt()
    {
        using var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Flex.Column().Children(Button.New("Hello")));
        session.PumpFrame();
        var shown = HultUI.Core.Dialogs.Dialog.Show<bool>(session.Window.Window, c => Flex.Column().Children(
            TextInput.New("name"),
            Button.New("OK").OnClick(_ => c.Close(true))));
        for (int i = 0; i < 12; i++) session.PumpFrame();   // past the fade-in, the focus inside
        Assert.False(shown.IsCompleted);

        session.Window.PressKey(KeyCode.Back);
        session.PumpFrame();

        Assert.True(shown.IsCompleted, "Back left the dialog open");
        Assert.Equal(0, _systemBacks);
    }
}

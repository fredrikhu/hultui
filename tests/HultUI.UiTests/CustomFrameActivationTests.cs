using System.Runtime.InteropServices;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// A window must not ask to be activated while it is made, whichever frame it has. The custom
/// frame's install forces a <c>WM_NCCALCSIZE</c> with <c>SetWindowPos</c>, which without
/// <c>SWP_NOACTIVATE</c> activated the window, still hidden, inside the Application
/// constructor: an app made with <c>SDL_HINT_WINDOW_ACTIVATE_WHEN_SHOWN=0</c> (a probe, a test)
/// took the foreground all the same (plan 17, round 3, step 9b). The guard refuses every
/// activation on this thread and counts them; none may be asked for, neither while the window is
/// made nor when its first frame shows it with the hint set.
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed class CustomFrameActivationTests
{
    public static readonly TheoryData<WindowStyle> Styles = new() { WindowStyle.Borderless, WindowStyle.Default };

    [UiTheory]
    [MemberData(nameof(Styles))]
    public void AWindow_AsksToBeActivated_NeitherWhenMade_NorWhenShownWithTheHint(WindowStyle style)
    {
        using var guard = ForegroundGuard.Begin();
        int refused = ForegroundGuard.Refused;

        using var app = new Application(new SdlWindowingPlatform(), "activation", 320, 200, style);
        var window = app.PrimaryWindow;
        var hwnd = window.NativeHandle;
        Assert.False(IsWindowVisible(hwnd));
        Assert.True(ForegroundGuard.Refused == refused,
            $"{style}: {ForegroundGuard.Refused - refused} activation(s) asked for while the window was made");

        app.SetRoot(Box.New().Background(new Color(40, 80, 120)).FlexGrow(1));
        app.RunFrame(TimeSpan.FromMilliseconds(16));   // the first frame shows it
        Assert.True(IsWindowVisible(hwnd));
        Assert.True(ForegroundGuard.Refused == refused,
            $"{style}: {ForegroundGuard.Refused - refused} activation(s) asked for when the window was shown");
        window.Close();
    }

    [DllImport("user32")] private static extern bool IsWindowVisible(nint hwnd);
}

using System.Runtime.InteropServices;
using HultUI.Core.Builders;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// A WM_CLOSE to the only window of a real SDL application (what Alt+F4 and the title bar's or
/// the task bar's close send) is asked of the app (<see cref="Window.CloseRequested"/>), and a
/// cancel keeps the window and the application running. SDL sent its quit with the last window's
/// close request, in the same poll, so the application ended whatever the app answered; the
/// platform now turns that off (SDL_HINT_QUIT_ON_LAST_WINDOW_CLOSE).
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed class WindowCloseRequestUiTests
{
    public static readonly TheoryData<WindowStyle> Styles = new() { WindowStyle.Borderless, WindowStyle.Default };

    private const uint WM_CLOSE = 0x0010;

    [UiTheory]
    [MemberData(nameof(Styles))]
    public void AClosePostedToTheLastWindow_IsAsked_AndACancelKeepsTheApplicationRunning(WindowStyle style)
    {
        using var guard = ForegroundGuard.Begin();
        using var app = new Application(new SdlWindowingPlatform(), "close request", 480, 320, style);
        app.SetRoot(Box.New().FlexGrow(1).Background(new Color(40, 80, 120)));
        var window = app.PrimaryWindow;
        app.RunFrame(TimeSpan.FromMilliseconds(16));   // the first frame shows it

        bool keep = true;
        int asked = 0;
        window.CloseRequested += request => { asked++; if (keep) request.Cancel(); };

        PostMessageW(window.NativeHandle, WM_CLOSE, 0, 0);
        for (int i = 0; i < 5 && app.IsRunning; i++) app.RunFrame(TimeSpan.FromMilliseconds(16));

        Assert.Equal(1, asked);
        Assert.True(app.IsRunning, "the application quit with the request");
        Assert.True(window.IsOpen);

        // Not cancelled: the window closes, and with it the application.
        keep = false;
        PostMessageW(window.NativeHandle, WM_CLOSE, 0, 0);
        for (int i = 0; i < 5 && window.IsOpen; i++) app.RunFrame(TimeSpan.FromMilliseconds(16));

        Assert.Equal(2, asked);
        Assert.False(window.IsOpen);
        Assert.False(app.IsRunning);
    }

    [DllImport("user32")] private static extern bool PostMessageW(nint hwnd, uint msg, nuint wParam, nint lParam);
}

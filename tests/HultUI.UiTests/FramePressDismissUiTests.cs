using System.Runtime.InteropServices;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// A press on a custom-frame window's title bar reaches the frame as a non-client message, never
/// as a pointer event, and the window's light-dismiss popups stayed open over it. The custom
/// frame now reports it, and the loop closes them (Window.DismissPopups). A right press on the
/// caption is used: it opens nothing until its release, and starts no move loop.
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed class FramePressDismissUiTests
{
    private const uint WM_NCRBUTTONDOWN = 0x00A4;
    private const int HTCAPTION = 2;

    [UiFact]
    public void APressOnTheCustomTitleBar_ClosesAnOpenMenu()
    {
        using var guard = ForegroundGuard.Begin();
        using var app = new Application(new SdlWindowingPlatform(), "frame press", 480, 320, WindowStyle.Borderless);
        app.SetRoot(Box.New().FlexGrow(1).Background(new Color(40, 80, 120)).Children(
            Flex.Row().Height(40f).WindowDrag().Background(new Color(30, 30, 30))));
        var window = app.PrimaryWindow;
        app.RunFrame(TimeSpan.FromMilliseconds(16));   // the first frame shows it

        bool closed = false;
        window.OpenContextMenu([new ContextMenuItem { Label = "Copy", Action = () => { } }], new Point(100, 100),
            () => closed = true);
        app.RunFrame(TimeSpan.FromMilliseconds(16));
        Assert.False(closed);

        PostMessageW(window.NativeHandle, WM_NCRBUTTONDOWN, HTCAPTION, 0);
        for (int i = 0; i < 3 && !closed; i++) app.RunFrame(TimeSpan.FromMilliseconds(16));

        Assert.True(closed, "the menu stayed open after a press on the title bar");
    }

    [DllImport("user32")] private static extern bool PostMessageW(nint hwnd, uint msg, nuint wParam, nint lParam);
}

using System.Runtime.InteropServices;
using HultUI.Core.Builders;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// A window placed by what is seen of it (<see cref="Window.VisiblePlacement"/>) shows exactly that
/// rectangle, on both frames and at the monitor's own scale: two windows on a work area's halves
/// meet edge to edge. Placed by <see cref="Window.Placement"/> (the outer rectangle, with the
/// invisible resize borders), they left a gap of two borders between them. The visible rectangle
/// is read here as Windows gives it: the custom frame's client area, the standard frame's DWM
/// bounds.
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed unsafe class VisiblePlacementUiTests
{
    public static readonly TheoryData<WindowStyle> Styles = new() { WindowStyle.Borderless, WindowStyle.Default };

    [UiTheory]
    [MemberData(nameof(Styles))]
    public void TwoWindowsPlacedByWhatIsSeen_ShowExactlyTheHalvesOfTheWorkArea(WindowStyle style)
    {
        using var guard = ForegroundGuard.Begin();
        var area = Monitors.WorkAreas()[0];
        int x = (int)area.X, y = (int)area.Y, w = (int)area.Width / 2, h = (int)area.Height;
        using var app = new Application(new SdlWindowingPlatform(), "visible left", 640, 480, style);
        app.SetRoot(Box.New().FlexGrow(1).Background(new Color(40, 80, 120)));
        var left = app.PrimaryWindow;
        var right = app.CreateWindow("visible right", 640, 480, style);
        right.SetRoot(Box.New().FlexGrow(1).Background(new Color(120, 80, 40)));

        left.VisiblePlacement = new WindowPlacement(x, y, w, h, false);
        right.VisiblePlacement = new WindowPlacement(x + w, y, w, h, false);
        for (int i = 0; i < 3; i++) app.RunFrame(TimeSpan.FromMilliseconds(16));

        bool custom = style == WindowStyle.Borderless;
        Assert.Equal((x, y, w, h), Seen(left.NativeHandle, custom));
        Assert.Equal((x + w, y, w, h), Seen(right.NativeHandle, custom));
        Assert.Equal(new WindowPlacement(x, y, w, h, false), left.VisiblePlacement);
        Assert.Equal(new WindowPlacement(x + w, y, w, h, false), right.VisiblePlacement);
    }

    private static (int X, int Y, int W, int H) Seen(nint hwnd, bool customFrame)
    {
        if (customFrame)
        {
            GetClientRect(hwnd, out var client);
            var origin = new POINT();
            ClientToScreen(hwnd, ref origin);
            return (origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        }
        DwmGetWindowAttribute(hwnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out var frame, sizeof(RECT));
        return (frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top);
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32")] private static extern bool GetClientRect(nint hwnd, out RECT rect);
    [DllImport("user32")] private static extern bool ClientToScreen(nint hwnd, ref POINT point);
    [DllImport("dwmapi")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out RECT value, int size);
}

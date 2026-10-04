using System.Runtime.InteropServices;
using HultUI.Core.Builders;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Platform.SDL3;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.UiTests;

/// <summary>
/// Window placement on real Windows windows, both frames. What the headless window cannot show:
/// that the outer rectangle lands exactly where it was asked for (the custom frame's client
/// rectangle is a caption taller than SDL thinks), on a monitor at another scale as well as the
/// primary, and that a maximized window is maximized — cloaked — before its first frame and
/// revealed by it.
/// </summary>
[Collection("ui")]
public sealed class WindowPlacementTests(ITestOutputHelper output)
{
    public static readonly WindowStyle[] Styles = [WindowStyle.Borderless, WindowStyle.Default];

    [UiFact]
    public void APlacementOnEveryMonitor_LandsExactly()
    {
        foreach (var style in Styles)
        foreach (var area in WorkAreas())
        {
            var wanted = new WindowPlacement((int)area.X + 40, (int)area.Y + 40, 900, 640, false);
            using var app = Open(style);
            var window = app.PrimaryWindow;

            window.Placement = wanted;
            Assert.Equal(wanted, OuterRect(window));   // in place while still hidden
            FirstFrame(app);

            output.WriteLine($"{style} on {area}: dpi {GetDpiForWindow(window.NativeHandle)}");
            Assert.True(IsWindowVisible(window.NativeHandle));
            Assert.Equal(wanted, OuterRect(window));
            Assert.Equal(wanted, window.Placement);
            window.Close();
        }
    }

    [UiFact]
    public void AMaximizedPlacement_IsMaximizedBeforeTheFirstFrame_WhichRevealsIt()
    {
        foreach (var style in Styles)
        foreach (var area in WorkAreas())
        {
            var normal = new WindowPlacement((int)area.X + 60, (int)area.Y + 60, 900, 640, false);
            using var app = Open(style);
            var window = app.PrimaryWindow;
            var hwnd = window.NativeHandle;

            window.Placement = normal with { Maximized = true };
            Assert.True(IsZoomed(hwnd), $"{style}: not maximized before the first frame");
            Assert.True(Cloaked(hwnd), $"{style}: a maximized window showed before its first frame");
            float width = window.LayoutWidth;

            FirstFrame(app);
            Assert.False(Cloaked(hwnd), $"{style}: the first frame did not reveal the window");
            Assert.True(IsWindowVisible(hwnd));
            Assert.Equal(width, window.LayoutWidth);   // drawn at the size it is shown at
            Assert.Equal(normal with { Maximized = true }, window.Placement);

            window.Restore();
            app.RunFrame(TimeSpan.Zero, forceRender: true);
            Assert.Equal(normal, OuterRect(window));
            window.Close();
        }
    }

    [UiFact]
    public void TheRestoredBounds_AreWhatIsReported_MaximizedMinimizedOrFullScreen()
    {
        foreach (var style in Styles)
        {
            var area = WorkAreas()[0];
            var normal = new WindowPlacement((int)area.X + 80, (int)area.Y + 80, 800, 600, false);
            using var app = Open(style);
            var window = app.PrimaryWindow;
            window.Placement = normal;
            FirstFrame(app);

            window.Maximize();
            app.RunFrame(TimeSpan.Zero, forceRender: true);
            Assert.Equal(normal with { Maximized = true }, window.Placement);

            window.Minimize();
            app.RunFrame(TimeSpan.Zero, forceRender: true);
            Assert.Equal(normal with { Maximized = true }, window.Placement);

            window.Restore();
            window.Restore();
            app.RunFrame(TimeSpan.Zero, forceRender: true);
            Assert.Equal(normal, window.Placement);

            window.SetFullscreen(true);
            app.RunFrame(TimeSpan.Zero, forceRender: true);
            Assert.NotEqual(normal, OuterRect(window));
            Assert.Equal(normal, window.Placement);

            // Setting a placement leaves full screen for it.
            var moved = normal with { X = normal.X + 50 };
            window.Placement = moved;
            Assert.False(window.IsFullscreen);
            Assert.Equal(moved, OuterRect(window));
            window.Close();
        }
    }

    [UiFact]
    public void Raise_UnMinimizes_ButLeavesAMaximizedWindowMaximized()
    {
        // Raise restored unconditionally, and a restore is what takes a window out of
        // maximized: every file handed over from a second launch shrank the player.
        foreach (var style in Styles)
        {
            var area = WorkAreas()[0];
            var normal = new WindowPlacement((int)area.X + 80, (int)area.Y + 80, 800, 600, false);
            using var app = Open(style);
            var window = app.PrimaryWindow;
            var hwnd = window.NativeHandle;
            window.Placement = normal with { Maximized = true };
            FirstFrame(app);

            window.Raise();
            app.RunFrame(TimeSpan.Zero, forceRender: true);
            Assert.True(IsZoomed(hwnd), $"{style}: raising a maximized window un-maximized it");
            Assert.True(window.IsMaximized);

            window.Minimize();
            app.RunFrame(TimeSpan.Zero, forceRender: true);
            window.Raise();
            app.RunFrame(TimeSpan.Zero, forceRender: true);
            Assert.False(IsIconic(hwnd), $"{style}: raise left the window minimized");
            Assert.True(IsZoomed(hwnd), $"{style}: a window minimized from maximized came back restored");

            window.Restore();   // posted on the custom frame: let it land before minimizing
            app.RunFrame(TimeSpan.Zero, forceRender: true);
            window.Minimize();
            app.RunFrame(TimeSpan.Zero, forceRender: true);
            window.Raise();
            app.RunFrame(TimeSpan.Zero, forceRender: true);
            Assert.False(IsIconic(hwnd));
            Assert.Equal(normal, OuterRect(window));
            window.Close();
        }
    }

    [UiFact]
    public void BoundsOnAMonitorThatIsGone_ComeBackOnOne()
    {
        using var app = Open(WindowStyle.Borderless);
        var window = app.PrimaryWindow;

        window.Placement = new WindowPlacement(100_000, 100_000, 900, 640, false);
        var landed = OuterRect(window);

        Assert.Equal((900, 640), (landed.Width, landed.Height));
        Assert.Contains(WorkAreas(), a => landed.X >= a.X && landed.Y >= a.Y
            && landed.X + landed.Width <= a.X + a.Width && landed.Y + landed.Height <= a.Y + a.Height);
        window.Close();
    }

    private static Application Open(WindowStyle style)
    {
        var app = new Application(new SdlWindowingPlatform(), "placement", 640, 480, style);
        app.SetRoot(Box.New().Background(new Color(40, 80, 120)).FlexGrow(1));
        return app;
    }

    /// <summary>The first frame as <see cref="Application.Run"/> draws it: only if something is
    /// dirty. A forced one renders whatever happened before it, so it cannot see a frame that
    /// cleared the dirty flags without revealing the window — the bug a cloaked window had.</summary>
    private static void FirstFrame(Application app)
    {
        app.RunFrame(TimeSpan.FromMilliseconds(16));
        Assert.True(app.PrimaryWindow.IsOpen);
    }

    private static WindowPlacement OuterRect(Window window)
    {
        GetWindowRect(window.NativeHandle, out var r);
        return new WindowPlacement(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, IsZoomed(window.NativeHandle));
    }

    private static bool Cloaked(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static List<Rect> WorkAreas()
    {
        var areas = new List<Rect>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfoW(monitor, ref info))
                areas.Add(new Rect(info.rcWork.Left, info.rcWork.Top,
                    info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top));
            return true;
        }, 0);
        return areas;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint clip, nint data);

    [DllImport("user32")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);
    [DllImport("user32", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);
    [DllImport("user32")] private static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("dwmapi")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
}

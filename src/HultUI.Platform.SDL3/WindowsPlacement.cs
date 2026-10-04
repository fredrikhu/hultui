using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;

namespace HultUI.Platform.SDL3;

/// <summary>
/// Window placement on Windows, read and applied on the HWND rather than through SDL.
/// </summary>
/// <remarks>
/// <para><b>Outer rectangles, from Win32.</b> SDL works in client rectangles and turns one into
/// a window rectangle with <c>AdjustWindowRectEx</c>, which assumes the standard caption the
/// custom frame does not have, so a size set through SDL comes out one caption height taller
/// (see <see cref="SDL3Window.SetFullscreen"/>). The window rectangle has no such ambiguity, for
/// either kind of frame.</para>
/// <para><b>Screen coordinates throughout.</b> <c>GetWindowPlacement</c> gives the restored
/// rectangle of a maximized window in <i>workspace</i> coordinates, which are offset from the
/// screen's by any task bar along the top or left of the monitor. It is converted the way
/// Chromium converts it: by the work area of the monitor the window is on.</para>
/// <para><b>A window that is neither maximized nor minimized reports where it is</b>
/// (<c>GetWindowRect</c>), not its placement's normal rectangle: for a window Aero Snap
/// arranged, that one is the rectangle before the snap, and the person last saw the window
/// snapped.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static unsafe class WindowsPlacement
{
    private const int SW_SHOWMAXIMIZED = 3;
    private const uint WPF_RESTORETOMAXIMIZED = 0x0002;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const int DWMWA_CLOAK = 13;

    /// <summary>The window's placement, in screen coordinates.</summary>
    public static WindowPlacement Read(IntPtr hwnd)
    {
        bool zoomed = IsZoomed(hwnd), iconic = IsIconic(hwnd);
        if (!zoomed && !iconic && GetWindowRect(hwnd, out var current))
            return new(current.Left, current.Top, current.Right - current.Left, current.Bottom - current.Top, false);

        var wp = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
        GetWindowPlacement(hwnd, ref wp);
        var r = wp.rcNormalPosition;
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (GetMonitorInfoW(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info))
        {
            int dx = info.rcWork.Left - info.rcMonitor.Left, dy = info.rcWork.Top - info.rcMonitor.Top;
            r = new RECT { Left = r.Left + dx, Top = r.Top + dy, Right = r.Right + dx, Bottom = r.Bottom + dy };
        }
        bool maximized = zoomed || (wp.flags & WPF_RESTORETOMAXIMIZED) != 0;
        return new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, maximized);
    }

    /// <summary>The work area of every monitor, in screen coordinates.</summary>
    public static List<Rect> WorkAreas()
    {
        var areas = new List<Rect>();
        var handle = GCHandle.Alloc(areas);
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, &CollectWorkArea, GCHandle.ToIntPtr(handle));
        }
        finally { handle.Free(); }
        return areas;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CollectWorkArea(IntPtr monitor, IntPtr hdc, RECT* clip, IntPtr data)
    {
        try
        {
            var areas = (List<Rect>)GCHandle.FromIntPtr(data).Target!;
            var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            if (GetMonitorInfoW(monitor, ref info))
            {
                var w = info.rcWork;
                areas.Add(new Rect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HultUI] Listing monitors failed: {ex.Message}");
        }
        return 1;   // keep enumerating
    }

    public static bool IsMaximizedOrMinimized(IntPtr hwnd) => IsZoomed(hwnd) || IsIconic(hwnd);

    /// <summary>
    /// Moves and sizes a window that is neither maximized nor minimized to the bounds' outer
    /// rectangle, without activating it or changing its z-order.
    /// </summary>
    /// <remarks>
    /// Landing on a monitor at another scale sends <c>WM_DPICHANGED</c>, which SDL answers by
    /// keeping the client's pixel size from before the move instead of the size just asked
    /// for. The window is on the right monitor after the first call, so a second one at the
    /// same scale is exact.
    /// </remarks>
    public static void Move(IntPtr hwnd, WindowPlacement bounds)
    {
        uint dpi = GetDpiForWindow(hwnd);
        const uint flags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER;
        SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags);
        if (GetDpiForWindow(hwnd) != dpi)
            SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags);
    }

    /// <summary>
    /// Shows a hidden window maximized, but cloaked: Windows treats it as shown — it has its
    /// maximized size, so the first frame is drawn at that size — while the compositor draws
    /// none of it until <see cref="Uncloak"/>. A window cannot be maximized and stay hidden,
    /// and maximizing at the reveal instead shows the first frame, drawn at the restored size,
    /// stretched over the screen. The same technique Chromium uses for its first paint.
    /// </summary>
    /// <remarks>
    /// The first <c>ShowWindow</c> a process makes takes its command from whoever started the
    /// process (<c>STARTUPINFO.wShowWindow</c>) and ignores the one passed — normally "show
    /// normal", so the window came up cloaked at its restored size. Cloaked, the spent call
    /// shows nothing, and the second one maximizes.
    /// </remarks>
    public static void ShowMaximizedCloaked(IntPtr hwnd)
    {
        int cloak = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref cloak, sizeof(int));
        ShowWindow(hwnd, SW_SHOWMAXIMIZED);
        if (!IsZoomed(hwnd)) ShowWindow(hwnd, SW_SHOWMAXIMIZED);
    }

    public static void Uncloak(IntPtr hwnd)
    {
        int cloak = 0;
        DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref cloak, sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public uint length, flags, showCmd;
        public POINT ptMinPosition, ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32", EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        delegate* unmanaged[Stdcall]<IntPtr, IntPtr, RECT*, IntPtr, int> lpfnEnum, IntPtr dwData);

    [DllImport("dwmapi")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);
}

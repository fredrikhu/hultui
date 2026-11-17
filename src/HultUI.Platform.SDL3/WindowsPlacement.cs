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
    private const int SW_SHOWNOACTIVATE = 4;
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

    /// <summary>
    /// Every monitor, in screen coordinates (physical pixels: the process is per-monitor DPI
    /// aware), with its work area, effective DPI and primary flag — the same numbers
    /// <see cref="Read"/> and <see cref="Move"/> use.
    /// </summary>
    public static List<MonitorInfo> Monitors()
    {
        var monitors = new List<MonitorInfo>();
        var handle = GCHandle.Alloc(monitors);
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, &CollectMonitor, GCHandle.ToIntPtr(handle));
        }
        finally { handle.Free(); }
        return monitors;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CollectMonitor(IntPtr monitor, IntPtr hdc, RECT* clip, IntPtr data)
    {
        try
        {
            var monitors = (List<MonitorInfo>)GCHandle.FromIntPtr(data).Target!;
            var info = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
            if (GetMonitorInfoW(monitor, ref info))
            {
                var b = info.rcMonitor;
                var w = info.rcWork;
                string device = new string(info.szDevice).TrimEnd('\0');
                float scale = GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out _) == 0 && dpiX > 0
                    ? dpiX / 96f
                    : 1f;
                string name = device.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase)
                    ? "Display " + device[@"\\.\DISPLAY".Length..]
                    : device;
                monitors.Add(new MonitorInfo(device, name,
                    new Rect(b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top),
                    new Rect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top),
                    scale, (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HultUI] Listing monitors failed: {ex.Message}");
        }
        return 1;   // keep enumerating
    }

    private const uint MONITORINFOF_PRIMARY = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEXW
    {
        public uint cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        public fixed char szDevice[32];
    }

    [DllImport("user32", EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEXW lpmi);

    [DllImport("shcore")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    public static bool IsMaximizedOrMinimized(IntPtr hwnd) => IsZoomed(hwnd) || IsIconic(hwnd);

    // ──── By the visible rectangle (Window.VisiblePlacement) ────
    //
    // A resizable window keeps an invisible resize border outside its visible edge on the left,
    // the right and the bottom: none on top, where the caption (or the custom title bar) is the
    // edge. GetWindowRect includes it, so two windows placed on a display's halves by their
    // window rectangles left a gap of two borders between them: 16 px at 100 %, 22 at 150 %.

    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CXPADDEDBORDER = 92;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    /// <summary>The invisible resize border at <paramref name="dpi"/>, as Windows sizes a thick frame.</summary>
    public static int InvisibleBorder(uint dpi) =>
        GetSystemMetricsForDpi(SM_CXSIZEFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);

    /// <summary>The window's visible rectangle, in screen coordinates. Neither maximized nor
    /// minimized: the custom frame's client area (it draws all of it), the standard frame's DWM
    /// bounds. Otherwise its restored bounds less the borders at the window's scale.</summary>
    public static WindowPlacement ReadVisible(IntPtr hwnd, bool customFrame)
    {
        if (!IsZoomed(hwnd) && !IsIconic(hwnd))
        {
            if (customFrame && GetClientRect(hwnd, out var client))
            {
                var origin = new POINT();
                if (ClientToScreen(hwnd, ref origin))
                    return new(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top, false);
            }
            else if (!customFrame && DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, sizeof(RECT)) == 0)
                return new(frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top, false);
        }
        return Inset(Read(hwnd), hwnd);
    }

    /// <summary>Outer bounds less the borders at the window's scale.</summary>
    public static WindowPlacement Inset(WindowPlacement outer, IntPtr hwnd)
    {
        int b = InvisibleBorder(GetDpiForWindow(hwnd));
        return new(outer.X + b, outer.Y, outer.Width - 2 * b, outer.Height - b, outer.Maximized);
    }

    /// <summary>The outer rectangle whose visible part is <paramref name="visible"/>: grown by the
    /// borders at the scale of the monitor it is on, where the window lands.</summary>
    public static WindowPlacement OuterFor(WindowPlacement visible)
    {
        var rect = new RECT { Left = visible.X, Top = visible.Y, Right = visible.X + visible.Width, Bottom = visible.Y + visible.Height };
        uint dpi = GetDpiForMonitor(MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST), 0, out uint dpiX, out _) == 0 && dpiX > 0 ? dpiX : 96;
        int b = InvisibleBorder(dpi);
        return new(visible.X - b, visible.Y, visible.Width + 2 * b, visible.Height + b, visible.Maximized);
    }

    [DllImport("user32")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

    [DllImport("user32")]
    private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);

    [DllImport("dwmapi")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);

    /// <summary>Takes a maximized or minimized window back to its restored bounds before this
    /// returns, without activating it: a placement then moves it from there. The custom frame's
    /// own restore is posted, for the handlers that call it from inside a window message.</summary>
    public static void RestoreNow(IntPtr hwnd) => ShowWindow(hwnd, SW_SHOWNOACTIVATE);

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

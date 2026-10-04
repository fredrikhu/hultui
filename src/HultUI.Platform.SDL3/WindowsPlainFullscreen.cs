using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HultUI.Platform.SDL3;

/// <summary>
/// Windowed fullscreen for a window that kept its OS title bar — the counterpart to
/// <see cref="WindowsCustomFrame"/>'s version, for apps created with
/// <see cref="HultUI.Core.Styling.WindowStyle.Default"/>.
///
/// <para>The reasoning is the frame's, and the problem it avoids is the same. SDL's
/// fullscreen turns the window into a popup exactly the size of the monitor, which is the
/// shape the display driver reads as a bid for the screen itself: alt-tabbing out of one
/// blacks the display out while the compositor takes it back, and so does toggling it. This
/// instead leaves an ordinary composited window that happens to be as big as the screen,
/// which switches away like any other window. The task bar still gets out of the way — the
/// shell hides it for a foreground window covering the monitor.</para>
///
/// <para>A window with a caption cannot simply be moved over the monitor: the title bar and
/// the resize border would be drawn across the picture. So those two styles come off for the
/// duration and go back on the way out, and the saved rectangle is what restores the window —
/// <c>SetWindowPos</c> with the old styles back would otherwise size the client area against
/// a frame that is no longer there.</para>
///
/// <para>The window ends one pixel proud of the monitor on every side, for the reason the
/// frame gives: covering a monitor <i>exactly</i> is the other shape a driver may take as a
/// request for an exclusive flip.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsPlainFullscreen(IntPtr hwnd)
{
    /// <summary>One pixel past the monitor on each side. See the type remarks.</summary>
    private const int FullscreenOverscan = 1;

    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;

    private const int SW_MAXIMIZE = 3;
    private const int SW_RESTORE = 9;

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;
    private const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    private readonly IntPtr _hwnd = hwnd;
    private RECT _rectBeforeFullscreen;
    private long _styleBeforeFullscreen;
    private bool _zoomedBeforeFullscreen;

    public bool IsFullscreen { get; private set; }

    /// <summary>Where full screen brings the window back to: its placement while it is full
    /// screen, since the window's own rectangle is the monitor's then.</summary>
    public HultUI.Core.Platform.WindowPlacement PlacementBeforeFullscreen => new(
        _rectBeforeFullscreen.Left, _rectBeforeFullscreen.Top,
        _rectBeforeFullscreen.Right - _rectBeforeFullscreen.Left,
        _rectBeforeFullscreen.Bottom - _rectBeforeFullscreen.Top, _zoomedBeforeFullscreen);

    public void SetFullscreen(bool fullscreen)
    {
        if (IsFullscreen == fullscreen) return;

        if (fullscreen)
        {
            _zoomedBeforeFullscreen = IsZoomed(_hwnd);
            // A maximized window cannot simply be moved: Windows keeps the maximized state
            // and snaps the rect back. Restore first, then take the screen.
            if (_zoomedBeforeFullscreen) ShowWindow(_hwnd, SW_RESTORE);
            GetWindowRect(_hwnd, out _rectBeforeFullscreen);

            var monitor = MonitorFromWindow(_hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfoW(monitor, ref info)) return;

            _styleBeforeFullscreen = (long)GetWindowLongPtrW(_hwnd, GWL_STYLE);
            SetWindowLongPtrW(_hwnd, GWL_STYLE,
                (IntPtr)(_styleBeforeFullscreen & ~(long)(WS_CAPTION | WS_THICKFRAME)));

            IsFullscreen = true;
            var m = info.rcMonitor;
            SetWindowPos(_hwnd, IntPtr.Zero,
                m.Left - FullscreenOverscan, m.Top - FullscreenOverscan,
                m.Right - m.Left + FullscreenOverscan * 2,
                m.Bottom - m.Top + FullscreenOverscan * 2,
                SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED);
        }
        else
        {
            IsFullscreen = false;
            SetWindowLongPtrW(_hwnd, GWL_STYLE, (IntPtr)_styleBeforeFullscreen);

            var r = _rectBeforeFullscreen;
            SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED);
            // The window was maximized when full screen took over, so put that back too.
            if (_zoomedBeforeFullscreen) ShowWindow(_hwnd, SW_MAXIMIZE);
        }

        SetCompositionChrome(fullscreen);
    }

    /// <summary>
    /// The corner rounding and hairline border DWM draws around a window. Both are right for
    /// a window and wrong for a picture filling the screen — the corners show the desktop
    /// through, and the border runs a line down the edge of the image.
    /// </summary>
    private void SetCompositionChrome(bool fullscreen)
    {
        int corner = fullscreen ? DWMWCP_DONOTROUND : DWMWCP_DEFAULT;
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        uint border = fullscreen ? DWMWA_COLOR_NONE : DWMWA_COLOR_DEFAULT;
        DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(uint));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;   // the whole display
        public RECT rcWork;      // the display minus the task bar — deliberately not used
        public uint dwFlags;
    }

    [DllImport("user32", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("dwmapi")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint value, int size);
}

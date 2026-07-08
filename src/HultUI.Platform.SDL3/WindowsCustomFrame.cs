using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Styling;

namespace HultUI.Platform.SDL3;

/// <summary>
/// Gives a borderless window native Windows chrome without an OS title bar:
/// the DWM drop shadow, the Windows 11 accent border, Aero Snap, snap layouts,
/// and native edge/corner resizing — the same technique Chrome, VS Code and
/// Electron use.
///
/// It works by subclassing SDL's window procedure and:
/// <list type="bullet">
///   <item><description><c>WM_NCCALCSIZE</c> returns the whole window as client
///     area, hiding the title bar while keeping the resizable frame.</description></item>
///   <item><description><c>DwmExtendFrameIntoClientArea</c> restores the DWM
///     shadow and accent border that removing the title bar would drop.</description></item>
///   <item><description><c>WM_NCHITTEST</c> reports resize edges and the drag
///     (caption) region so Windows drives resize and move natively.</description></item>
/// </list>
/// Every other message is forwarded to SDL's original procedure unchanged.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsCustomFrame : IDisposable
{
    // ── Win32 constants ──
    private const int GWLP_WNDPROC = -4;

    private const uint WM_NCCALCSIZE = 0x0083;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_DWMCOMPOSITIONCHANGED = 0x031E;

    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private const int SM_CXFRAME = 32;
    private const int SM_CYFRAME = 33;
    private const int SM_CXPADDEDBORDER = 92;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint SWP_FRAMECHANGED = 0x0020;

    // Extending the frame by a single pixel is enough to make DWM composite the
    // window as framed again — that is what re-enables the shadow and the
    // Windows 11 accent border. Our opaque GL content draws over the strip.
    private static readonly MARGINS ShadowMargins = new() { CyTopHeight = 1 };

    private static readonly Dictionary<IntPtr, WindowsCustomFrame> _frames = new();

    private readonly SDL3Window _window;
    private readonly IntPtr _hwnd;
    private IntPtr _originalProc;
    private bool _disposed;

    private WindowsCustomFrame(SDL3Window window, IntPtr hwnd)
    {
        _window = window;
        _hwnd = hwnd;
    }

    /// <summary>
    /// Installs the custom frame on the given window. Returns null (leaving the
    /// window untouched) if the HWND is unavailable so the caller can fall back
    /// to the plain SDL borderless path.
    /// </summary>
    public static WindowsCustomFrame? Install(SDL3Window window, IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;

        var frame = new WindowsCustomFrame(window, hwnd);
        _frames[hwnd] = frame;

        delegate* unmanaged[Stdcall]<IntPtr, uint, UIntPtr, IntPtr, IntPtr> proc = &WndProc;
        frame._originalProc = SetWindowLongPtrW(hwnd, GWLP_WNDPROC, (IntPtr)proc);

        frame.ExtendFrame();

        // Force a WM_NCCALCSIZE so the title bar is dropped immediately.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED);

        return frame;
    }

    private void ExtendFrame()
    {
        var margins = ShadowMargins;
        DwmExtendFrameIntoClientArea(_hwnd, ref margins);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam)
    {
        // An exception escaping this callback tears down the runtime, so guard
        // the whole body and fall back to default processing on any trouble.
        try
        {
            if (!_frames.TryGetValue(hwnd, out var frame))
                return DefWindowProcW(hwnd, msg, wParam, lParam);

            switch (msg)
            {
                case WM_NCCALCSIZE when wParam != UIntPtr.Zero:
                    frame.HandleNcCalcSize(hwnd, wParam, lParam);
                    return IntPtr.Zero;

                case WM_NCHITTEST:
                    return frame.HandleNcHitTest(hwnd, lParam);

                case WM_DWMCOMPOSITIONCHANGED:
                    frame.ExtendFrame();
                    break;
            }

            return CallWindowProcW(frame._originalProc, hwnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HultUI] WindowsCustomFrame WndProc failed: {ex}");
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private void HandleNcCalcSize(IntPtr hwnd, UIntPtr wParam, IntPtr lParam)
    {
        // wParam TRUE: rgrc[0] is the proposed window rect; whatever we leave in
        // it becomes the client area.
        var p = (NCCALCSIZE_PARAMS*)lParam;

        // A maximized WS_THICKFRAME window is positioned slightly larger than the
        // monitor so its frame sits off-screen. Inset by the frame size so our
        // content isn't clipped off the edges of the screen.
        if (IsZoomed(hwnd))
        {
            int fx = GetSystemMetrics(SM_CXFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
            int fy = GetSystemMetrics(SM_CYFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
            p->rgrc0.Left += fx;
            p->rgrc0.Top += fy;
            p->rgrc0.Right -= fx;
            p->rgrc0.Bottom -= fy;
            return;
        }

        // Let Windows compute the standard frame, then reclaim only the title-bar
        // height. This keeps the native side and bottom resize borders — which
        // reach a few pixels *beyond* the visible content, so you can grab them
        // just outside the window like a native app — while removing the OS
        // title bar so our custom chrome runs to the top edge.
        int originalTop = p->rgrc0.Top;
        DefWindowProcW(hwnd, WM_NCCALCSIZE, wParam, lParam);
        p->rgrc0.Top = originalTop;
    }

    private int HandleNcHitTest(IntPtr hwnd, IntPtr lParam)
    {
        long packed = lParam.ToInt64();
        var pt = new POINT { X = unchecked((short)(packed & 0xFFFF)), Y = unchecked((short)((packed >> 16) & 0xFFFF)) };
        ScreenToClient(hwnd, ref pt);
        GetClientRect(hwnd, out RECT rc);

        float scale = _window.DisplayScale <= 0f ? 1f : _window.DisplayScale;
        var edges = _window.ResizableEdges;
        int border = Math.Max(1, (int)MathF.Round(_window.ResizeBorderDip * scale));

        // Resize borders only apply when the window can actually resize.
        if (!IsZoomed(hwnd))
        {
            // The left/right/bottom resize zones live entirely in the native
            // frame *outside* the client area, so content flush to the edge
            // (e.g. a scrollbar) stays fully clickable — the resize handle never
            // reaches inward over it. The top was reclaimed for the custom title
            // bar and has no frame, so it keeps a thin inward border.
            bool left = (edges & ResizeEdges.Left) != 0 && pt.X < rc.Left;
            bool right = (edges & ResizeEdges.Right) != 0 && pt.X >= rc.Right;
            bool bottom = (edges & ResizeEdges.Bottom) != 0 && pt.Y >= rc.Bottom;
            bool top = (edges & ResizeEdges.Top) != 0 && pt.Y < rc.Top + border;

            // Corners extend the grab *along* each frame edge (never deeper into
            // the client) so the diagonal target is easy to hit.
            int reach = border * 3;
            bool nearLeft = (edges & ResizeEdges.Left) != 0 && pt.X < rc.Left + reach;
            bool nearRight = (edges & ResizeEdges.Right) != 0 && pt.X >= rc.Right - reach;
            bool nearTop = (edges & ResizeEdges.Top) != 0 && pt.Y < rc.Top + reach;
            bool nearBottom = (edges & ResizeEdges.Bottom) != 0 && pt.Y >= rc.Bottom - reach;

            if ((top && nearLeft) || (left && nearTop)) return HTTOPLEFT;
            if ((top && nearRight) || (right && nearTop)) return HTTOPRIGHT;
            if ((bottom && nearLeft) || (left && nearBottom)) return HTBOTTOMLEFT;
            if ((bottom && nearRight) || (right && nearBottom)) return HTBOTTOMRIGHT;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;
        }

        // Not a resize edge: ask the app whether this is a drag region so Windows
        // can move (and snap) the window natively. Coordinates are converted from
        // physical client pixels to layout units for the element hit test.
        int dipX = (int)(pt.X / scale);
        int dipY = (int)(pt.Y / scale);
        if (_window.IsWindowDragPoint?.Invoke(dipX, dipY) == true)
            return HTCAPTION;

        return HTCLIENT;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_originalProc != IntPtr.Zero)
            SetWindowLongPtrW(_hwnd, GWLP_WNDPROC, _originalProc);
        _frames.Remove(_hwnd);
    }

    // ── Win32 interop ──

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int CxLeftWidth, CxRightWidth, CyTopHeight, CyBottomHeight; }

    // Only the first rectangle (the proposed client area) is written; the rest
    // of NCCALCSIZE_PARAMS is left in place and reached through the pointer.
    [StructLayout(LayoutKind.Sequential)]
    private struct NCCALCSIZE_PARAMS { public RECT rgrc0, rgrc1, rgrc2; public IntPtr lppos; }

    [DllImport("user32", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("dwmapi")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);
}

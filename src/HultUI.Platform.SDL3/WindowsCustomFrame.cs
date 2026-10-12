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
    private const int GWL_STYLE = -16;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_SYSMENU = 0x00080000;

    private const uint WM_SIZE = 0x0005;
    private const uint WM_NCCALCSIZE = 0x0083;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_NCMOUSEMOVE = 0x00A0;
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const uint WM_NCLBUTTONUP = 0x00A2;
    private const uint WM_NCRBUTTONUP = 0x00A5;
    private const uint WM_NCMOUSELEAVE = 0x02A2;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSCHAR = 0x0106;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const uint WM_DWMCOMPOSITIONCHANGED = 0x031E;
    private const uint WM_GETTITLEBARINFOEX = 0x033F;

    private const int VK_SPACE = 0x20;

    private const int SC_SIZE = 0xF000;
    private const int SC_MOVE = 0xF010;
    private const int SC_MINIMIZE = 0xF020;
    private const int SC_MAXIMIZE = 0xF030;
    private const int SC_CLOSE = 0xF060;
    private const int SC_RESTORE = 0xF120;

    private const uint MF_BYCOMMAND = 0x0000;
    private const uint MF_ENABLED = 0x0000;
    private const uint MF_GRAYED = 0x0001;
    private const uint TPM_RETURNCMD = 0x0100;

    private const int HTCLIENT = 1;
    private const int SW_MAXIMIZE = 3;
    private const int SW_RESTORE = 9;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    // Windows 11 composition attributes. Both are what give a window its modern look —
    // and both have to go while it is covering the screen: a rounded corner leaves the
    // desktop showing through at every corner of the picture, and the 1px border draws a
    // line down the edge of it. Unknown attributes are an error on Windows 10, which is
    // the right outcome there: that Windows neither rounds nor borders.
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const int DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);
    private const int HTCAPTION = 2;
    private const int HTMAXBUTTON = 9;
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
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint SWP_FRAMECHANGED = 0x0020;

    /// <summary>The install's frame-changed nudge: nothing moved, sized, reordered or activated.</summary>
    private const uint InstallFlags =
        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_FRAMECHANGED;

    private const uint TME_LEAVE = 0x0002;
    private const uint TME_NONCLIENT = 0x0010;

    private static readonly Dictionary<IntPtr, WindowsCustomFrame> _frames = new();

    private readonly SDL3Window _window;
    private readonly IntPtr _hwnd;
    private IntPtr _originalProc;
    private bool _disposed;
    // Height (physical px) of the DWM caption region extended into the client.
    // A single pixel is enough to re-enable the drop shadow and accent border
    // (our opaque GL content draws over the strip), but Windows 11 only offers the
    // Snap Layouts flyout for a maximize button that sits *within* the caption
    // region — so once we know the button's bounds we grow this to cover it.
    private int _captionHeightPx = 1;
    private bool _maxPressed;      // a left-press landed on the maximize button
    private bool _maxLeaveTracked; // WM_NCMOUSELEAVE is armed for the current NC hover
    private bool _wasZoomed;       // maximized state at the last WM_SIZE (see ExtendFrame)

    // ──── Windowed fullscreen ────
    private RECT _rectBeforeFullscreen;   // window rect to come back to
    private bool _zoomedBeforeFullscreen; // it was maximized when we took over

    /// <summary>
    /// How far past the monitor the window is pushed on each edge while fullscreen.
    ///
    /// <para>A window whose rect matches the monitor <i>exactly</i> is what Windows and
    /// the display driver look for before handing it the screen directly — the "exclusive
    /// flip" path. That path is why alt-tabbing out of a fullscreen OpenGL window blacks
    /// the screen out for a moment: the compositor has to take the display back. One pixel
    /// of overscan keeps the window ordinary and composited, so switching away is as
    /// instant as any other window, and the pixel that falls off each edge is a pixel of
    /// letterbox nobody can see.</para>
    /// </summary>
    private const int FullscreenOverscan = 1;

    /// <summary>True while the window is covering its monitor with no chrome.</summary>
    public bool IsFullscreen { get; private set; }

    /// <summary>Where full screen brings the window back to: its placement while it is full
    /// screen, since the window's own rectangle is the monitor's then.</summary>
    public HultUI.Core.Platform.WindowPlacement PlacementBeforeFullscreen => new(
        _rectBeforeFullscreen.Left, _rectBeforeFullscreen.Top,
        _rectBeforeFullscreen.Right - _rectBeforeFullscreen.Left,
        _rectBeforeFullscreen.Bottom - _rectBeforeFullscreen.Top, _zoomedBeforeFullscreen);

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

        // Windows 11 only offers the Snap Layouts flyout for windows that advertise
        // a maximize box, so add WS_MAXIMIZEBOX/MINIMIZEBOX back (no visual effect
        // without an OS title bar). WS_SYSMENU must go: with it, Windows assumes the
        // window has *standard* caption buttons and only honours HTMAXBUTTON inside
        // the standard caption band (SM_CYFRAME + SM_CXPADDEDBORDER + SM_CYCAPTION
        // from the top — 45px at 150% DPI), which is shorter than our title bar; a
        // hover that enters the button below that band never shows the flyout.
        // Without WS_SYSMENU there is no standard geometry to validate against and
        // HTMAXBUTTON is honoured across the full button (this is why Electron apps
        // get full-height flyouts — they drop WS_SYSMENU too). Costs the Alt+Space
        // system menu, which a custom-chrome window doesn't surface anyway.
        long style = GetWindowLongPtrW(hwnd, GWL_STYLE).ToInt64();
        SetWindowLongPtrW(hwnd, GWL_STYLE,
            (IntPtr)((style | WS_MAXIMIZEBOX | WS_MINIMIZEBOX) & ~(long)WS_SYSMENU));

        frame.ExtendFrame();

        // Force a WM_NCCALCSIZE so the title bar is dropped immediately. Without
        // SWP_NOACTIVATE this also activated the window — hidden, inside the Application
        // constructor — so an app told not to (SDL_HINT_WINDOW_ACTIVATE_WHEN_SHOWN=0: a probe,
        // a test) took the foreground all the same. Activating is for the show, which SDL does
        // as the hint says.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, InstallFlags);

        return frame;
    }

    /// <summary>
    /// Windowed fullscreen: the window is moved and sized to cover its monitor, and the
    /// frame stops reserving anything — <see cref="HandleNcCalcSize"/> hands the whole
    /// window rect over as client area, so the picture runs edge to edge.
    ///
    /// <para>Deliberately not SDL's fullscreen. That one swaps the window to a popup
    /// exactly the size of the monitor, which is the shape Windows grants the display to;
    /// this stays an ordinary composited window that happens to be as big as the screen.
    /// The task bar still gets out of the way — the shell hides it for a foreground window
    /// that covers the monitor — and alt-tab costs nothing.</para>
    /// </summary>
    public void SetFullscreen(bool fullscreen)
    {
        if (IsFullscreen == fullscreen) return;

        if (fullscreen)
        {
            _zoomedBeforeFullscreen = IsZoomed(_hwnd);
            // A maximized window cannot simply be moved: Windows keeps the maximized
            // state and snaps the rect back. Restore first, then take the screen.
            if (_zoomedBeforeFullscreen) ShowWindow(_hwnd, SW_RESTORE);
            GetWindowRect(_hwnd, out _rectBeforeFullscreen);

            var monitor = MonitorFromWindow(_hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfoW(monitor, ref info)) return;

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
            var r = _rectBeforeFullscreen;
            SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED);
            // The window was maximized when full screen took over, so put that back too.
            if (_zoomedBeforeFullscreen) ShowWindow(_hwnd, SW_MAXIMIZE);
        }

        SetCompositionChrome(fullscreen);
        ExtendFrame();
    }

    /// <summary>
    /// The corner rounding and the hairline border DWM draws around a window. Both are
    /// right for a window and wrong for a picture filling the screen — the corners show
    /// the desktop through, and the border runs a line down the edge of the image.
    /// </summary>
    private void SetCompositionChrome(bool fullscreen)
    {
        int corner = fullscreen ? DWMWCP_DONOTROUND : DWMWCP_DEFAULT;
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        int border = fullscreen ? DWMWA_COLOR_NONE : DWMWA_COLOR_DEFAULT;
        DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    private void ExtendFrame()
    {
        // The margin is measured from the window-rect top. Maximized, the rect
        // (and with it the frame's first SM_CYFRAME + SM_CXPADDEDBORDER px) sits
        // off-screen, which would leave the bottom of the maximize button outside
        // the caption region — and Windows only honours HTMAXBUTTON for the Snap
        // Layouts flyout inside that region — so grow the margin by the clipped
        // frame. Re-applied on maximize/restore transitions (see WM_SIZE).
        // Fullscreen wants no frame at all: the extension exists to give a windowed
        // custom-chrome window its shadow and a caption band for the Snap Layouts flyout,
        // and neither belongs over a picture that reaches the screen edges.
        int px = _captionHeightPx;
        if (IsFullscreen) px = 0;
        else if (IsZoomed(_hwnd))
            px += GetSystemMetrics(SM_CYFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
        var margins = new MARGINS { CyTopHeight = px };
        DwmExtendFrameIntoClientArea(_hwnd, ref margins);
    }

    /// <summary>Sets the DWM caption height (physical px) so the maximize button
    /// falls inside the caption region — required for the Snap Layouts flyout when
    /// the window isn't maximized. Visually harmless: the region is composited as
    /// frame but our opaque content draws over it. No-op if unchanged.</summary>
    public void SetCaptionHeight(int px)
    {
        px = Math.Max(1, px);
        if (px == _captionHeightPx) return;
        _captionHeightPx = px;
        ExtendFrame();
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
                    return (IntPtr)frame.HandleNcHitTest(hwnd, lParam);

                // The maximize button lives in the non-client area (we report it as
                // HTMAXBUTTON so Windows shows Snap Layouts), so its hover and click
                // arrive as NC messages. Drive the app's button highlight from them
                // and handle the click ourselves as maximize/restore.
                case WM_NCMOUSEMOVE:
                    frame.HandleNcMouseMove(hwnd, wParam);
                    break;

                case WM_NCMOUSELEAVE:
                    frame._maxLeaveTracked = false;
                    frame._window.OnMaximizeButtonHover?.Invoke(false);
                    break;

                case WM_MOUSEMOVE:
                    // The pointer is back in the client area — clear any NC hover.
                    frame._window.OnMaximizeButtonHover?.Invoke(false);
                    break;

                case WM_NCLBUTTONDOWN when (int)wParam.ToUInt64() == HTMAXBUTTON:
                    frame._maxPressed = true;
                    return IntPtr.Zero; // swallow: don't let DefWindowProc open a menu / start a move

                case WM_NCLBUTTONUP when (int)wParam.ToUInt64() == HTMAXBUTTON:
                    if (frame._maxPressed)
                    {
                        frame._maxPressed = false;
                        frame.ToggleMaximizeNative();
                    }
                    return IntPtr.Zero;

                case WM_DWMCOMPOSITIONCHANGED:
                    frame.ExtendFrame();
                    break;

                // The shell (when showing the Snap Layouts flyout) and assistive
                // tech query the caption geometry through this; without WS_SYSMENU
                // DefWindowProc reports a zeroed, invisible maximize button, so fill
                // in our custom chrome's real rects.
                case WM_GETTITLEBARINFOEX:
                {
                    IntPtr res = CallWindowProcW(frame._originalProc, hwnd, msg, wParam, lParam);
                    frame.FillTitleBarInfo(hwnd, lParam);
                    return res;
                }

                // Without WS_SYSMENU DefWindowProc no longer opens the window menu;
                // provide it ourselves the way Chromium does (Alt+Space and
                // right-click on the caption).
                case WM_SYSKEYDOWN when (int)wParam.ToUInt64() == VK_SPACE:
                    frame.ShowSystemMenu(hwnd, atCursor: false);
                    return IntPtr.Zero;

                case WM_SYSCHAR when (int)wParam.ToUInt64() == VK_SPACE:
                    return IntPtr.Zero; // handled at WM_SYSKEYDOWN; swallow the beep

                case WM_NCRBUTTONUP when (int)wParam.ToUInt64() == HTCAPTION:
                    frame.ShowSystemMenu(hwnd, atCursor: true);
                    return IntPtr.Zero;

                // The DWM caption margin depends on the maximized state (see
                // ExtendFrame); re-apply it when that state flips.
                case WM_SIZE:
                {
                    bool zoomed = IsZoomed(hwnd);
                    if (zoomed != frame._wasZoomed)
                    {
                        frame._wasZoomed = zoomed;
                        frame.ExtendFrame();
                    }
                    break;
                }
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

        // Fullscreen: every pixel of the window is ours. Leaving the proposed rect
        // untouched is what makes the client area the whole window — no frame, no
        // reclaimed caption, nothing inset from the screen edges.
        if (IsFullscreen) return;

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
        // Fullscreen has no frame to grab and no caption to drag: a resize cursor on the
        // screen edge, or a drag that half-restores the window, would both be wrong.
        if (IsFullscreen) return HTCLIENT;

        long packed = lParam.ToInt64();
        var pt = new POINT { X = unchecked((short)(packed & 0xFFFF)), Y = unchecked((short)((packed >> 16) & 0xFFFF)) };
        ScreenToClient(hwnd, ref pt);
        GetClientRect(hwnd, out RECT rc);

        float scale = _window.DisplayScale <= 0f ? 1f : _window.DisplayScale;
        var edges = _window.ResizableEdges;
        int border = Math.Max(1, (int)MathF.Round(_window.ResizeBorderDip * scale));

        int dipX = (int)(pt.X / scale);
        int dipY = (int)(pt.Y / scale);

        // The maximize button reports HTMAXBUTTON so Windows 11 shows its Snap
        // Layouts flyout when the pointer hovers it. Check it BEFORE the resize
        // edges: when not maximized the button's top overlaps the top resize
        // border, and returning HTTOP there hides the flyout (Windows probes the
        // button's top). The button is never at a resize corner, so claiming its
        // whole rect costs no real resize target. We drive the button's own
        // highlight and click from the NC messages (see WndProc). The flyout
        // works over the button's full height only because Install() strips
        // WS_SYSMENU — see the comment there.
        if (_window.IsWindowMaximizePoint?.Invoke(dipX, dipY) == true)
            return HTMAXBUTTON;

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

        // Ask the app whether this is a drag region so Windows can move (and snap)
        // the window natively.
        if (_window.IsWindowDragPoint?.Invoke(dipX, dipY) == true)
            return HTCAPTION;

        return HTCLIENT;
    }

    /// <summary>Fills the caller's TITLEBARINFOEX with our custom chrome's title
    /// bar and maximize-button rects (screen px). Index 3 of rgstate/rgrect is the
    /// maximize button; state 0 means visible and enabled. The shell reads this to
    /// position the Snap Layouts flyout.</summary>
    private void FillTitleBarInfo(IntPtr hwnd, IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return;
        var p = (byte*)lParam;
        // DWORD cbSize; RECT rcTitleBar; DWORD rgstate[6]; RECT rgrect[6]
        if (*(uint*)p < 4u + 16u + 6 * 4u + 6 * 16u) return;
        if (_window.GetWindowMaximizeButtonBounds?.Invoke() is not { } b) return;

        float scale = _window.DisplayScale <= 0f ? 1f : _window.DisplayScale;
        var origin = new POINT();
        ClientToScreen(hwnd, ref origin);
        var btn = new RECT
        {
            Left = origin.X + (int)(b.X * scale),
            Top = origin.Y + (int)(b.Y * scale),
            Right = origin.X + (int)((b.X + b.Width) * scale),
            Bottom = origin.Y + (int)((b.Y + b.Height) * scale),
        };

        var rcTitle = (RECT*)(p + 4);
        var states = (uint*)(p + 4 + sizeof(RECT));
        var rects = (RECT*)(p + 4 + sizeof(RECT) + 6 * sizeof(uint));
        GetWindowRect(hwnd, out RECT wr);
        *rcTitle = new RECT { Left = wr.Left, Top = btn.Top, Right = wr.Right, Bottom = btn.Bottom };
        states[3] = 0;
        rects[3] = btn;
    }

    /// <summary>Shows the native window (system) menu and executes the picked
    /// command. Without WS_SYSMENU DefWindowProc won't do this itself, so Alt+Space
    /// and caption right-click are wired to this — the same approach Chromium uses.
    /// GetSystemMenu still returns the default menu for WS_SYSMENU-less windows;
    /// enable/disable items to match the window state like the native menu does.</summary>
    private void ShowSystemMenu(IntPtr hwnd, bool atCursor)
    {
        // GetSystemMenu is gated on the CURRENT window style, and Install strips
        // WS_SYSMENU (required for full-height Snap Layouts on the maximize
        // button). Flip it on for the duration of the menu; the flyout only
        // consults the style while hovering the button, so the transient flip —
        // under a modal menu loop — is unobservable.
        long style = GetWindowLongPtrW(hwnd, GWL_STYLE).ToInt64();
        SetWindowLongPtrW(hwnd, GWL_STYLE, (IntPtr)(style | WS_SYSMENU));
        try
        {
            IntPtr menu = GetSystemMenu(hwnd, false);
            if (menu == IntPtr.Zero) return;
            ShowSystemMenuCore(hwnd, menu, atCursor);
        }
        finally
        {
            SetWindowLongPtrW(hwnd, GWL_STYLE, (IntPtr)(style & ~(long)WS_SYSMENU));
        }
    }

    private void ShowSystemMenuCore(IntPtr hwnd, IntPtr menu, bool atCursor)
    {

        bool zoomed = IsZoomed(_hwnd);
        EnableMenuItem(menu, SC_RESTORE, MF_BYCOMMAND | (zoomed ? MF_ENABLED : MF_GRAYED));
        EnableMenuItem(menu, SC_MOVE, MF_BYCOMMAND | (zoomed ? MF_GRAYED : MF_ENABLED));
        EnableMenuItem(menu, SC_SIZE, MF_BYCOMMAND | (zoomed ? MF_GRAYED : MF_ENABLED));
        EnableMenuItem(menu, SC_MINIMIZE, MF_BYCOMMAND | MF_ENABLED);
        EnableMenuItem(menu, SC_MAXIMIZE, MF_BYCOMMAND | (zoomed ? MF_GRAYED : MF_ENABLED));
        EnableMenuItem(menu, SC_CLOSE, MF_BYCOMMAND | MF_ENABLED);
        SetMenuDefaultItem(menu, (uint)(zoomed ? SC_RESTORE : SC_MAXIMIZE), 0);

        int x, y;
        if (atCursor && GetCursorPos(out POINT cur))
        {
            x = cur.X; y = cur.Y;
        }
        else
        {
            // Alt+Space: below the top-left corner of the visible caption,
            // like the native menu.
            GetWindowRect(hwnd, out RECT wr);
            int inset = GetSystemMetrics(SM_CXFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
            x = wr.Left + (zoomed ? inset : 0);
            y = wr.Top + (zoomed ? inset : 0) + _captionHeightPx;
        }

        // TPM_RETURNCMD runs the menu loop and hands the choice back instead of
        // sending WM_COMMAND; post it as the system command it stands for.
        uint cmd = (uint)TrackPopupMenu(menu, TPM_RETURNCMD, x, y, 0, hwnd, IntPtr.Zero);
        if (cmd != 0)
            PostMessageW(hwnd, WM_SYSCOMMAND, (UIntPtr)cmd, IntPtr.Zero);
    }

    /// <summary>Maximizes/restores the window natively via WM_SYSCOMMAND — the
    /// same path the caption double-click uses. Posted (not sent) so it runs after
    /// the message being handled returns. SDL's maximize (SDL_MaximizeWindow) only
    /// flips SDL's flag on this custom-frame window without resizing it, so it must
    /// not be used; the WM_SIZE these produce still keeps SDL's state in sync.</summary>
    public void Maximize() => PostMessageW(_hwnd, WM_SYSCOMMAND, (UIntPtr)SC_MAXIMIZE, IntPtr.Zero);

    /// <inheritdoc cref="Maximize"/>
    public void Restore() => PostMessageW(_hwnd, WM_SYSCOMMAND, (UIntPtr)SC_RESTORE, IntPtr.Zero);

    private void ToggleMaximizeNative()
    {
        if (IsZoomed(_hwnd)) Restore();
        else Maximize();
    }

    private void HandleNcMouseMove(IntPtr hwnd, UIntPtr wParam)
    {
        bool onMax = (int)wParam.ToUInt64() == HTMAXBUTTON;
        _window.OnMaximizeButtonHover?.Invoke(onMax);

        // Arm a WM_NCMOUSELEAVE so the highlight clears when the pointer leaves the
        // button straight off the window (a move into the client clears via WM_MOUSEMOVE).
        if (onMax && !_maxLeaveTracked)
        {
            var tme = new TRACKMOUSEEVENT
            {
                cbSize = (uint)sizeof(TRACKMOUSEEVENT),
                dwFlags = TME_LEAVE | TME_NONCLIENT,
                hwndTrack = hwnd,
                dwHoverTime = 0,
            };
            if (TrackMouseEvent(ref tme))
                _maxLeaveTracked = true;
        }
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
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;   // the whole display
        public RECT rcWork;      // the display minus the task bar — deliberately not used
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int CxLeftWidth, CxRightWidth, CyTopHeight, CyBottomHeight; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT { public uint cbSize; public uint dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }

    // Only the first rectangle (the proposed client area) is written; the rest
    // of NCCALCSIZE_PARAMS is left in place and reached through the pointer.
    [StructLayout(LayoutKind.Sequential)]
    private struct NCCALCSIZE_PARAMS { public RECT rgrc0, rgrc1, rgrc2; public IntPtr lppos; }

    [DllImport("user32", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32")]
    private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    [DllImport("user32", EntryPoint = "PostMessageW")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32")]
    private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);
    [DllImport("user32", EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32")]
    private static extern bool EnableMenuItem(IntPtr hMenu, int uIDEnableItem, uint uEnable);

    [DllImport("user32")]
    private static extern bool SetMenuDefaultItem(IntPtr hMenu, uint uItem, uint fByPos);

    [DllImport("user32")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("dwmapi")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);
}

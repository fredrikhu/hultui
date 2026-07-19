using System.Runtime.InteropServices;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using SDL;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3;

public sealed unsafe class SDL3Window : IPlatformWindow, IWindowChrome
{
    private static bool _sdlInitialized;
    private static readonly Dictionary<SDL_WindowID, SDL3Window> _registry = new();

    private SDL_Window* _window;
    private SDL_GLContextState* _glContext;
    private SkiaGLRenderSurface? _surface;
    private bool _disposed;
    private readonly Dictionary<CursorType, nint> _cursorCache = [];
    private CursorType _currentCursor = CursorType.Default;
    private GCHandle _gcHandle;
    private bool _textInputActive;
    private SDL3Clipboard? _clipboard;
    private SDL3TextInputHost? _textInputHost;

    public bool ResizePending { get; set; }
    public long LastResizeRenderTick { get; set; }

    /// <summary>
    /// Invoked from the event watcher during a Win32 modal resize loop,
    /// when <see cref="SDL_PollEvent"/> is blocked and the main loop cannot
    /// drive frames itself. Implementers are expected to throttle.
    /// </summary>
    public Action? LiveResizeFrame { get; set; }

    public SDL_WindowID WindowId { get; private set; }

    /// <summary>Framebuffer width in physical pixels.</summary>
    public int PixelWidth { get; private set; }

    /// <summary>Framebuffer height in physical pixels.</summary>
    public int PixelHeight { get; private set; }

    /// <summary>
    /// OS-recommended content scale (1.0, 1.5, 2.0…). Drives how large the UI
    /// appears and how many physical pixels each density-independent unit maps
    /// to. This — not pixel density — is what makes the UI track the monitor's
    /// scaling setting; on Windows pixel density stays 1.0 at every scale.
    /// </summary>
    public float DisplayScale { get; private set; } = 1f;

    /// <summary>
    /// Physical pixels per SDL window coordinate (pixel_size / window_size).
    /// Only used to translate incoming SDL input coordinates into layout units.
    /// On Windows this is 1.0; on macOS Retina it is the backing-store scale.
    /// </summary>
    public float PixelDensity { get; private set; } = 1f;

    /// <summary>Layout viewport width in density-independent units (<see cref="PixelWidth"/> / <see cref="DisplayScale"/>).</summary>
    public float LayoutWidth => PixelWidth / DisplayScale;

    /// <summary>Layout viewport height in density-independent units (<see cref="PixelHeight"/> / <see cref="DisplayScale"/>).</summary>
    public float LayoutHeight => PixelHeight / DisplayScale;

    /// <summary>
    /// Factor converting SDL window-coordinate input into layout units
    /// (<see cref="PixelDensity"/> / <see cref="DisplayScale"/>). SDL delivers
    /// pointer positions in window coordinates; layout, hit-testing and events
    /// all run in layout units.
    /// </summary>
    public float InputScale => PixelDensity / DisplayScale;

    public IRenderSurface Surface => _surface ?? throw new InvalidOperationException("Window not initialized");
    public bool IsOpen { get; private set; }

    public EventTranslator EventTranslator { get; } = new();

    /// <summary>
    /// Layout-unit input events, forwarded from <see cref="EventTranslator"/>.
    /// The framework Window subscribes here rather than to the translator so
    /// the input path is identical across platform backends.
    /// </summary>
    public event Action<InputEvent>? InputReceived;

    public WindowStyle Style { get; }

    public IClipboard Clipboard => _clipboard ??= new SDL3Clipboard(this);

    public ITextInputHost TextInputHost => _textInputHost ??= new SDL3TextInputHost(this);

    /// <summary>
    /// Delegate called by the SDL hit test callback. Receives logical (x, y) and
    /// returns an SDL_HitTestResult value as int.
    /// Set by Window.cs for borderless windows (non-Windows path).
    /// </summary>
    public Func<int, int, int>? HitTestHandler { get; set; }

    private WindowsCustomFrame? _customFrame;

    /// <summary>
    /// True when this window uses the native Windows custom frame (borderless on
    /// Windows) rather than SDL's borderless + hit-test path. In that mode Windows
    /// drives resize/move through <see cref="IsWindowDragPoint"/> and
    /// <see cref="ResizableEdges"/> instead of <see cref="HitTestHandler"/>.
    /// </summary>
    public bool UsesWindowsNativeFrame => _customFrame is not null;

    /// <summary>Which edges the native frame allows resizing from. Set by Window.cs.</summary>
    public ResizeEdges ResizableEdges { get; set; } = ResizeEdges.All;

    /// <summary>Resize-border grab thickness in density-independent units. Set by Window.cs.</summary>
    public int ResizeBorderDip { get; set; } = 8;

    /// <summary>
    /// Returns true when the given layout-unit point is a window drag region
    /// (and not over an interactive element). Used by the native frame's
    /// <c>WM_NCHITTEST</c> to report the caption area. Set by Window.cs.
    /// </summary>
    public Func<int, int, bool>? IsWindowDragPoint { get; set; }

    /// <summary>
    /// Returns true when the given layout-unit point is over the window's
    /// maximize/restore button. Lets the native frame report <c>HTMAXBUTTON</c>
    /// so Windows 11 shows its Snap Layouts flyout on hover. Set by Window.cs.
    /// </summary>
    public Func<int, int, bool>? IsWindowMaximizePoint { get; set; }

    /// <summary>Returns the maximize/restore button's bounds in layout units
    /// (client-relative), or null when there is none. The Windows custom frame
    /// reports it via WM_GETTITLEBARINFOEX so the shell can anchor the Snap
    /// Layouts flyout to the button. Set by Window.cs.</summary>
    public Func<Rect?>? GetWindowMaximizeButtonBounds { get; set; }

    /// <summary>Called (true) when the pointer enters the maximize button in the
    /// non-client area and (false) when it leaves, so the app can drive the
    /// button's hover highlight itself (Windows won't). Set by Window.cs.</summary>
    public Action<bool>? OnMaximizeButtonHover { get; set; }

    /// <summary>Extends the Windows custom frame's DWM caption region to
    /// <paramref name="px"/> physical pixels so the maximize button sits inside it
    /// (needed for the Snap Layouts flyout when not maximized). No-op off Windows
    /// or when unchanged. Called by Window.cs once the button's layout is known.</summary>
    public void SetCaptionHeightPx(int px)
    {
        // The redundant IsWindows() check (implied by _customFrame being non-null)
        // satisfies the platform-compatibility analyzer, as at the other custom
        // frame call sites.
        if (OperatingSystem.IsWindows() && _customFrame is { } frame)
            frame.SetCaptionHeight(px);
    }

    public SDL3Window(string title, int width, int height, WindowStyle style = WindowStyle.Default)
    {
        Style = style;
        EventTranslator.OnInputEvent += evt => InputReceived?.Invoke(evt);
        EnsureSDLInitialized();

        // Set OpenGL attributes for SkiaSharp compatibility
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, 3);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, 3);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL_GLProfile.SDL_GL_CONTEXT_PROFILE_CORE);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_DOUBLEBUFFER, 1);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_STENCIL_SIZE, 8);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_RED_SIZE, 8);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_GREEN_SIZE, 8);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_BLUE_SIZE, 8);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_ALPHA_SIZE, 8);

        // Create the window hidden. Its GL back buffer is uninitialized until we
        // render and swap the first frame; if the window were visible before then
        // the OS would composite that garbage (a black/white flash), made worse on
        // Windows by the custom frame's SWP_FRAMECHANGED repaint below. The app
        // loop calls Show() right after the first frame is presented instead.
        var flags = SDL_WindowFlags.SDL_WINDOW_OPENGL
            | SDL_WindowFlags.SDL_WINDOW_RESIZABLE
            | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY
            | SDL_WindowFlags.SDL_WINDOW_HIDDEN;

        // On Windows a borderless window keeps its native frame and hides the
        // title bar via a WndProc subclass (see WindowsCustomFrame) so it still
        // gets the DWM shadow, accent border and Aero Snap. Everywhere else the
        // SDL borderless flag + hit-test path is used.
        bool useWindowsNativeFrame = style == WindowStyle.Borderless && OperatingSystem.IsWindows();
        if (style == WindowStyle.Borderless && !useWindowsNativeFrame)
            flags |= SDL_WindowFlags.SDL_WINDOW_BORDERLESS;

        var titleBytes = MarshalUtf8(title);
        fixed (byte* titlePtr = titleBytes)
        {
            _window = SDL_CreateWindow(titlePtr, width, height, flags);
        }

        if (_window == null)
            throw new InvalidOperationException($"SDL_CreateWindow failed: {GetError()}");

        _glContext = SDL_GL_CreateContext(_window);
        if (_glContext == null)
            throw new InvalidOperationException($"SDL_GL_CreateContext failed: {GetError()}");

        SDL_GL_MakeCurrent(_window, _glContext);
        // Disable driver-level vsync — on Wayland the compositor handles
        // vsync, and swap interval 1 can block during interactive resize.
        SDL_GL_SetSwapInterval(0);

        // Create SkiaSharp surface with SDL3 GL proc resolver
        _surface = new SkiaGLRenderSurface(GetGLProcAddress);

        // Text input is enabled on focus (see StartTextInput) — leaving it
        // always on would make the Win32 IMM/TSF consume dead keys and
        // IME-toggle hotkeys even over non-text UI.

        // Register in the static window registry
        WindowId = SDL_GetWindowID(_window);
        _registry[WindowId] = this;

        // Register event watcher for live resize rendering.
        // SDL_AddEventWatch fires even during the modal resize loop
        // where SDL_PollEvent is blocked.
        _gcHandle = GCHandle.Alloc(this);
        SDL_AddEventWatch(&EventWatchCallback, GCHandle.ToIntPtr(_gcHandle));

        // For SDL-borderless windows, register the hit test callback so SDL
        // knows which regions are draggable (title bar) and resizable (edges).
        // The Windows native frame drives this through WM_NCHITTEST instead.
        if (style == WindowStyle.Borderless && !useWindowsNativeFrame)
            SDL_SetWindowHitTest(_window, &HitTestNativeCallback, GCHandle.ToIntPtr(_gcHandle));

        // The redundant IsWindows() check (implied by useWindowsNativeFrame)
        // satisfies the platform-compatibility analyzer at the call site.
        if (useWindowsNativeFrame && OperatingSystem.IsWindows())
            _customFrame = WindowsCustomFrame.Install(this, GetWin32Hwnd());

        // Give the platform backend a chance to settle the real pixel size
        // before the first frame on high-DPI displays.
        SDL_SyncWindow(_window);
        UpdateSize();

        // The requested size is in density-independent units, but SDL created
        // the window in window coordinates (which equal pixels on Windows). On
        // a scaled display the two differ, so resize once to give the app the
        // content area it asked for: e.g. an 800-DIP window becomes 1200 px at
        // 150%. InputScale ≈ 1 (100% displays, macOS Retina) is a no-op.
        if (Math.Abs(InputScale - 1f) > 0.001f)
        {
            SDL_SetWindowSize(_window,
                (int)MathF.Round(width / InputScale),
                (int)MathF.Round(height / InputScale));
            SDL_SyncWindow(_window);
            UpdateSize();
        }

        IsOpen = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static SDLBool EventWatchCallback(IntPtr userdata, SDL_Event* evt)
    {
        // Exceptions escaping an [UnmanagedCallersOnly] method tear down the
        // runtime with no managed stack trace — the process just dies. Guard
        // the whole body so a stale GCHandle or a throwing live-resize frame
        // is logged and swallowed instead of silently killing the app.
        try
        {
            if (userdata == IntPtr.Zero)
                return true;
            var handle = GCHandle.FromIntPtr(userdata);
            if (!handle.IsAllocated || handle.Target is not SDL3Window window)
                return true;

            switch (evt->type)
            {
                case (uint)SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
                case (uint)SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
                case (uint)SDL_EventType.SDL_EVENT_WINDOW_DISPLAY_SCALE_CHANGED:
                case (uint)SDL_EventType.SDL_EVENT_WINDOW_DISPLAY_CHANGED:
                case (uint)SDL_EventType.SDL_EVENT_WINDOW_SHOWN:
                case (uint)SDL_EventType.SDL_EVENT_WINDOW_EXPOSED:
                    // Only handle events for this specific window
                    if (!evt->window.windowID.Equals(window.WindowId))
                        break;

                    window.UpdateSize();
                    window.ResizePending = true;

                    // Win32 enters a modal resize loop (WM_ENTERSIZEMOVE) that
                    // blocks SDL_PollEvent until the mouse is released. The
                    // event watcher is the only code path that runs during
                    // that pump, so drive a live frame from here on Windows.
                    if (OperatingSystem.IsWindows())
                        window.LiveResizeFrame?.Invoke();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HultUI] EventWatchCallback failed: {ex}");
        }

        return true;
    }

    /// <summary>
    /// Polls all SDL events and routes them to the correct window.
    /// Input events are dispatched via each window's EventTranslator.
    /// Returns false if a quit event was received.
    /// </summary>
    public static bool PollAllEvents(Action<SDL3Window>? onWindowClose = null)
    {
        bool quit = false;
        SDL_Event evt;
        while (SDL_PollEvent(&evt))
        {
            switch (evt.type)
            {
                case (uint)SDL_EventType.SDL_EVENT_QUIT:
                    quit = true;
                    break;

                case (uint)SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                    if (_registry.TryGetValue(evt.window.windowID, out var closeWin))
                        onWindowClose?.Invoke(closeWin);
                    break;

                case (uint)SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
                case (uint)SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
                case (uint)SDL_EventType.SDL_EVENT_WINDOW_DISPLAY_SCALE_CHANGED:
                case (uint)SDL_EventType.SDL_EVENT_WINDOW_DISPLAY_CHANGED:
                case (uint)SDL_EventType.SDL_EVENT_WINDOW_SHOWN:
                case (uint)SDL_EventType.SDL_EVENT_WINDOW_EXPOSED:
                    // After startup, monitor moves, or the modal resize loop,
                    // render the final size only if the watcher observed
                    // an unhandled size/scale change. This preserves resize
                    // throttling for events already handled by live resize.
                    if (_registry.TryGetValue(evt.window.windowID, out var resizeWin) && resizeWin.ResizePending)
                    {
                        resizeWin.UpdateSize();
                        resizeWin.ResizePending = true;
                    }
                    break;

                default:
                    var windowId = ExtractWindowId(&evt);
                    if (_registry.TryGetValue(windowId, out var inputWin))
                        inputWin.EventTranslator.ProcessEvent(&evt);
                    break;
            }
        }
        return !quit;
    }

    public void MakeCurrent()
    {
        SDL_GL_MakeCurrent(_window, _glContext);
    }

    public void Close()
    {
        IsOpen = false;
    }

    // ──── Window management (for custom chrome) ────

    public void Minimize() => SDL_MinimizeWindow(_window);

    // With the Windows custom frame installed, SDL_MaximizeWindow/SDL_RestoreWindow
    // only flip SDL's maximized flag without resizing the window, so route through
    // the frame's native WM_SYSCOMMAND path instead.
    public void Maximize()
    {
        if (OperatingSystem.IsWindows() && _customFrame is { } frame) frame.Maximize();
        else SDL_MaximizeWindow(_window);
    }

    public void Restore()
    {
        if (OperatingSystem.IsWindows() && _customFrame is { } frame) frame.Restore();
        else SDL_RestoreWindow(_window);
    }

    public bool IsMaximized =>
        ((uint)SDL_GetWindowFlags(_window) & (uint)SDL_WindowFlags.SDL_WINDOW_MAXIMIZED) != 0;

    /// <summary>
    /// Constrains how small the user can resize the window, in density-independent
    /// (layout) units. SDL takes the limit in window coordinates, so convert via
    /// <see cref="InputScale"/> (layout = windowCoord · InputScale) exactly as the
    /// DPI-preserving resize above does. Pass 0 for a dimension to leave it
    /// unconstrained.
    /// </summary>
    public void SetMinimumSize(int layoutWidth, int layoutHeight)
    {
        if (_window == null) return;
        float scale = InputScale;
        if (scale <= 0f) scale = 1f;
        SDL_SetWindowMinimumSize(_window,
            layoutWidth <= 0 ? 0 : (int)MathF.Round(layoutWidth / scale),
            layoutHeight <= 0 ? 0 : (int)MathF.Round(layoutHeight / scale));
    }

    /// <summary>
    /// Sets the window/taskbar icon from decoded image pixels. SDL copies the
    /// surface, so the pixel buffer only needs to live for the call. On Windows
    /// the exe's embedded .ico usually already covers this; on Linux this is the
    /// only way the window manager gets an icon.
    /// </summary>
    public void SetIcon(int width, int height, ReadOnlySpan<byte> rgbaPixels)
    {
        if (_window == null || width <= 0 || height <= 0 || rgbaPixels.Length < width * height * 4)
            return;
        // The binding lacks the byte-order RGBA32 alias, so pick the packed
        // format that reads R,G,B,A in memory for this endianness.
        var format = BitConverter.IsLittleEndian
            ? SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888
            : SDL_PixelFormat.SDL_PIXELFORMAT_RGBA8888;
        fixed (byte* pixels = rgbaPixels)
        {
            var surface = SDL_CreateSurfaceFrom(width, height,
                format, (IntPtr)pixels, width * 4);
            if (surface == null) return;
            SDL_SetWindowIcon(_window, surface);
            SDL_DestroySurface(surface);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static SDL_HitTestResult HitTestNativeCallback(SDL_Window* window, SDL_Point* area, IntPtr data)
    {
        // Exceptions out of an [UnmanagedCallersOnly] method tear down the
        // runtime, so guard against a stale/freed GCHandle — SDL can fire
        // this during window destruction after Dispose freed it.
        if (area == null || data == IntPtr.Zero)
            return SDL_HitTestResult.SDL_HITTEST_NORMAL;
        var handle = GCHandle.FromIntPtr(data);
        if (!handle.IsAllocated || handle.Target is not SDL3Window self)
            return SDL_HitTestResult.SDL_HITTEST_NORMAL;
        if (self.HitTestHandler is { } handler)
        {
            // area is in SDL window coordinates; the handler works in layout units.
            float s = self.InputScale;
            return (SDL_HitTestResult)handler((int)(area->x * s), (int)(area->y * s));
        }
        return SDL_HitTestResult.SDL_HITTEST_NORMAL;
    }

    public void SwapBuffers()
    {
        SDL_GL_SwapWindow(_window);
    }

    /// <summary>
    /// Reveals the window (created hidden). Call once, right after the first
    /// frame has been rendered and swapped, so the window's first visible
    /// composite shows real content instead of the uninitialized GL surface.
    /// No-op on subsequent calls.
    /// </summary>
    public void EnsureShown()
    {
        if (_shown || _window == null) return;
        _shown = true;
        SDL_ShowWindow(_window);
    }
    private bool _shown;

    public void SetCursor(CursorType cursor)
    {
        if (cursor == _currentCursor) return;
        _currentCursor = cursor;

        if (!_cursorCache.TryGetValue(cursor, out var sdlCursor))
        {
            sdlCursor = (nint)SDL_CreateSystemCursor(MapCursorType(cursor));
            _cursorCache[cursor] = sdlCursor;
        }

        SDL_SetCursor((SDL_Cursor*)sdlCursor);
    }

    public string? GetClipboardText()
    {
        return SDL_GetClipboardText();
    }

    public void SetClipboardText(string text)
    {
        var bytes = MarshalUtf8(text);
        fixed (byte* ptr = bytes)
        {
            SDL_SetClipboardText(ptr);
        }
    }

    public void StartTextInput()
    {
        if (_textInputActive) return;
        _textInputActive = true;
        SDL_StartTextInput(_window);
    }

    public void StopTextInput()
    {
        if (!_textInputActive) return;
        _textInputActive = false;
        SDL_StopTextInput(_window);
    }

    public void SetTextInputArea(Rect caretRect, int cursorOffset)
    {
        // caretRect is in layout units; SDL wants window coordinates.
        float toWindow = 1f / InputScale;
        var rect = new SDL_Rect
        {
            x = (int)(caretRect.X * toWindow),
            y = (int)(caretRect.Y * toWindow),
            w = Math.Max(1, (int)(caretRect.Width * toWindow)),
            h = Math.Max(1, (int)(caretRect.Height * toWindow))
        };
        SDL_SetTextInputArea(_window, &rect, cursorOffset);
    }

    public void ClearComposition() => SDL_ClearComposition(_window);

    private static SDL_SystemCursor MapCursorType(CursorType cursor) => cursor switch
    {
        CursorType.Pointer => SDL_SystemCursor.SDL_SYSTEM_CURSOR_POINTER,
        CursorType.Text => SDL_SystemCursor.SDL_SYSTEM_CURSOR_TEXT,
        CursorType.Crosshair => SDL_SystemCursor.SDL_SYSTEM_CURSOR_CROSSHAIR,
        CursorType.Move => SDL_SystemCursor.SDL_SYSTEM_CURSOR_MOVE,
        CursorType.ResizeNS => SDL_SystemCursor.SDL_SYSTEM_CURSOR_NS_RESIZE,
        CursorType.ResizeEW => SDL_SystemCursor.SDL_SYSTEM_CURSOR_EW_RESIZE,
        CursorType.ResizeNESW => SDL_SystemCursor.SDL_SYSTEM_CURSOR_NESW_RESIZE,
        CursorType.ResizeNWSE => SDL_SystemCursor.SDL_SYSTEM_CURSOR_NWSE_RESIZE,
        CursorType.NotAllowed => SDL_SystemCursor.SDL_SYSTEM_CURSOR_NOT_ALLOWED,
        CursorType.Wait => SDL_SystemCursor.SDL_SYSTEM_CURSOR_WAIT,
        CursorType.None => SDL_SystemCursor.SDL_SYSTEM_CURSOR_DEFAULT,
        CursorType.ColResize => SDL_SystemCursor.SDL_SYSTEM_CURSOR_EW_RESIZE,
        CursorType.RowResize => SDL_SystemCursor.SDL_SYSTEM_CURSOR_NS_RESIZE,
        _ => SDL_SystemCursor.SDL_SYSTEM_CURSOR_DEFAULT
    };

    /// <summary>
    /// Updates cached size fields from SDL. Returns true if the size actually changed.
    /// </summary>
    internal bool UpdateSize()
    {
        int pw, ph;
        SDL_GetWindowSizeInPixels(_window, &pw, &ph);

        // Pixel density (pixel_size / window_size) only tells us how many
        // physical pixels back a window coordinate — on Windows it is 1.0 at
        // every scale. DisplayScale carries the OS DPI preference (1.5 at
        // 150%, etc.); laying out in DIP units and scaling the canvas by it is
        // what makes the UI grow with the monitor's scaling setting instead of
        // rendering 1:1 in raw pixels.
        float density = SDL_GetWindowPixelDensity(_window);
        float displayScale = SDL_GetWindowDisplayScale(_window);
        if (density <= 0f) density = 1f;
        if (displayScale <= 0f) displayScale = 1f;

        if (pw == PixelWidth && ph == PixelHeight
            && density == PixelDensity && displayScale == DisplayScale)
            return false;

        // The window moved to a monitor with a different scaling setting, but
        // its physical pixel size stayed the same — so the DIP viewport (and
        // thus the amount of visible content) would jump. Resize to keep the
        // DIP size constant, the standard DPI-change behavior. We only correct
        // this when the pixel size was left untouched; if the platform already
        // resized the window for the new scale, the DIP size is preserved and
        // there's nothing to do. Skipped while maximized (the OS owns the size)
        // and on the first sync (PixelWidth == 0, handled by the constructor).
        bool scaleOnlyChange = pw == PixelWidth && ph == PixelHeight
            && displayScale != DisplayScale && PixelWidth > 0 && !IsMaximized;
        float prevLayoutW = LayoutWidth;
        float prevLayoutH = LayoutHeight;

        PixelWidth = pw;
        PixelHeight = ph;
        PixelDensity = density;
        DisplayScale = displayScale;
        EventTranslator.InputScale = InputScale;

        if (scaleOnlyChange)
        {
            // Resize to preserve the DIP viewport. SDL_SetWindowSize takes
            // window coordinates (pixels / density); the resulting resize event
            // re-enters with the new pixel size (scale now unchanged, so no
            // further resize). Re-query so the surface below matches this pass.
            SDL_SetWindowSize(_window,
                (int)MathF.Round(prevLayoutW / InputScale),
                (int)MathF.Round(prevLayoutH / InputScale));
            SDL_GetWindowSizeInPixels(_window, &pw, &ph);
            PixelWidth = pw;
            PixelHeight = ph;
        }

        _surface?.Resize(pw, ph, displayScale);
        return true;
    }

    private static SDL_WindowID ExtractWindowId(SDL_Event* evt)
    {
        return evt->type switch
        {
            (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN or
            (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP => evt->button.windowID,
            (uint)SDL_EventType.SDL_EVENT_MOUSE_MOTION => evt->motion.windowID,
            (uint)SDL_EventType.SDL_EVENT_MOUSE_WHEEL => evt->wheel.windowID,
            (uint)SDL_EventType.SDL_EVENT_KEY_DOWN or
            (uint)SDL_EventType.SDL_EVENT_KEY_UP => evt->key.windowID,
            (uint)SDL_EventType.SDL_EVENT_TEXT_INPUT => evt->text.windowID,
            (uint)SDL_EventType.SDL_EVENT_TEXT_EDITING => evt->edit.windowID,
            (uint)SDL_EventType.SDL_EVENT_DROP_BEGIN or
            (uint)SDL_EventType.SDL_EVENT_DROP_FILE or
            (uint)SDL_EventType.SDL_EVENT_DROP_TEXT or
            (uint)SDL_EventType.SDL_EVENT_DROP_COMPLETE or
            (uint)SDL_EventType.SDL_EVENT_DROP_POSITION => evt->drop.windowID,
            _ => default
        };
    }

    private static void EnsureSDLInitialized()
    {
        if (_sdlInitialized) return;
        _sdlInitialized = true;

        // Tell SDL the app renders the IME composition (preedit) itself. Without
        // this, SDL leaves composition to the OS's native overlay, which never
        // appears because we draw our own text via Skia — so the candidate
        // window shows but the inline preedit does not. With it, SDL delivers
        // the composition as SDL_EVENT_TEXT_EDITING events for inline drawing,
        // while the native candidate list stays (we don't claim "candidates").
        // Must be set before text input starts.
        SDL_SetHint(SDL_HINT_IME_IMPLEMENTED_UI, "composition");

        // Deliver the click that focuses a background window to the app, instead of
        // eating it. SDL's default swallows the activating click, so a click into an
        // inactive window only raises it and the control under the cursor never fires
        // — the user has to click a second time. With click-through the same click
        // both focuses the window and hits the control, as users expect.
        SDL_SetHint(SDL_HINT_MOUSE_FOCUS_CLICKTHROUGH, "1");

        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
            throw new InvalidOperationException($"SDL_Init failed: {GetError()}");
    }

    public static void ShutdownSDL()
    {
        if (!_sdlInitialized) return;
        _sdlInitialized = false;
        SDL_Quit();
    }

    private static IntPtr GetGLProcAddress(string name)
    {
        var nameBytes = MarshalUtf8(name);
        fixed (byte* namePtr = nameBytes)
        {
            return SDL_GL_GetProcAddress(namePtr);
        }
    }

    private static string GetError()
    {
        return SDL_GetError() ?? "Unknown error";
    }

    /// <summary>
    /// Returns the native Win32 <c>HWND</c> for this window on Windows, or
    /// <see cref="IntPtr.Zero"/> on other platforms / if the window was
    /// destroyed. Used by the UIA accessibility bridge.
    /// </summary>
    public IntPtr GetWin32Hwnd()
    {
        if (_window == null) return IntPtr.Zero;
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;

        var props = SDL_GetWindowProperties(_window);
        fixed (byte* name = SDL_PROP_WINDOW_WIN32_HWND_POINTER)
            return SDL_GetPointerProperty(props, name, IntPtr.Zero);
    }

    private static byte[] MarshalUtf8(string text)
    {
        var bytes = new byte[System.Text.Encoding.UTF8.GetByteCount(text) + 1];
        System.Text.Encoding.UTF8.GetBytes(text, bytes);
        bytes[^1] = 0; // null terminator
        return bytes;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registry.Remove(WindowId);

        // Restore SDL's window procedure before the window is destroyed.
        if (OperatingSystem.IsWindows())
            _customFrame?.Dispose();
        _customFrame = null;

        SDL_RemoveEventWatch(&EventWatchCallback, GCHandle.ToIntPtr(_gcHandle));

        // Clear the hit test before freeing the handle — SDL_DestroyWindow
        // can fire the callback during teardown, and a freed GCHandle would
        // crash the runtime via the [UnmanagedCallersOnly] callback.
        if (_window != null && Style == WindowStyle.Borderless)
            SDL_SetWindowHitTest(_window, null, IntPtr.Zero);

        if (_gcHandle.IsAllocated)
            _gcHandle.Free();

        _surface?.Dispose();
        _surface = null;

        foreach (var cursor in _cursorCache.Values)
            SDL_DestroyCursor((SDL_Cursor*)cursor);
        _cursorCache.Clear();

        if (_glContext != null)
        {
            SDL_GL_DestroyContext(_glContext);
            _glContext = null;
        }

        if (_window != null)
        {
            SDL_DestroyWindow(_window);
            _window = null;
        }
    }
}

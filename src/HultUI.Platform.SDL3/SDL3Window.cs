using System.Runtime.InteropServices;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using SDL;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3;

public sealed unsafe class SDL3Window : IDisposable
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
    internal bool ResizePending;
    internal long LastResizeRenderTick;

    /// <summary>
    /// Invoked from the event watcher during a Win32 modal resize loop,
    /// when <see cref="SDL_PollEvent"/> is blocked and the main loop cannot
    /// drive frames itself. Implementers are expected to throttle.
    /// </summary>
    internal Action? LiveResizeFrame { get; set; }

    public SDL_WindowID WindowId { get; private set; }
    public int PixelWidth { get; private set; }
    public int PixelHeight { get; private set; }
    public int LogicalWidth { get; private set; }
    public int LogicalHeight { get; private set; }
    public float ScaleFactor { get; private set; } = 1f;

    public IRenderSurface Surface => _surface ?? throw new InvalidOperationException("Window not initialized");
    public bool IsOpen { get; private set; }

    public EventTranslator EventTranslator { get; } = new();

    public WindowStyle Style { get; }

    /// <summary>
    /// Delegate called by the SDL hit test callback. Receives logical (x, y) and
    /// returns an SDL_HitTestResult value as int.
    /// Set by Window.cs for borderless windows.
    /// </summary>
    public Func<int, int, int>? HitTestHandler { get; set; }

    public SDL3Window(string title, int width, int height, WindowStyle style = WindowStyle.Default)
    {
        Style = style;
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

        var flags = SDL_WindowFlags.SDL_WINDOW_OPENGL
            | SDL_WindowFlags.SDL_WINDOW_RESIZABLE
            | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY;

        if (style == WindowStyle.Borderless)
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

        // Enable text input events (SDL3 requires explicit activation)
        SDL_StartTextInput(_window);

        // Register in the static window registry
        WindowId = SDL_GetWindowID(_window);
        _registry[WindowId] = this;

        // Register event watcher for live resize rendering.
        // SDL_AddEventWatch fires even during the modal resize loop
        // where SDL_PollEvent is blocked.
        _gcHandle = GCHandle.Alloc(this);
        SDL_AddEventWatch(&EventWatchCallback, GCHandle.ToIntPtr(_gcHandle));

        // For borderless windows, register the hit test callback so SDL knows
        // which regions are draggable (title bar) and resizable (edges).
        if (style == WindowStyle.Borderless)
            SDL_SetWindowHitTest(_window, &HitTestNativeCallback, GCHandle.ToIntPtr(_gcHandle));

        UpdateSize();
        IsOpen = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static SDLBool EventWatchCallback(IntPtr userdata, SDL_Event* evt)
    {
        var window = (SDL3Window)GCHandle.FromIntPtr(userdata).Target!;

        switch (evt->type)
        {
            case (uint)SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
            case (uint)SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
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
                case (uint)SDL_EventType.SDL_EVENT_WINDOW_EXPOSED:
                    // After the modal resize loop ends, ensure we render
                    // the final size (the watcher may have throttled it).
                    if (_registry.TryGetValue(evt.window.windowID, out var resizeWin) && resizeWin.ResizePending)
                        resizeWin.ResizePending = true;
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
    public void Maximize() => SDL_MaximizeWindow(_window);
    public void Restore() => SDL_RestoreWindow(_window);

    public bool IsMaximized =>
        ((uint)SDL_GetWindowFlags(_window) & (uint)SDL_WindowFlags.SDL_WINDOW_MAXIMIZED) != 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static SDL_HitTestResult HitTestNativeCallback(SDL_Window* window, SDL_Point* area, IntPtr data)
    {
        var self = (SDL3Window)GCHandle.FromIntPtr(data).Target!;
        if (self.HitTestHandler is { } handler)
            return (SDL_HitTestResult)handler(area->x, area->y);
        return SDL_HitTestResult.SDL_HITTEST_NORMAL;
    }

    public void SwapBuffers()
    {
        SDL_GL_SwapWindow(_window);
    }

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
        int pw, ph, lw, lh;
        SDL_GetWindowSizeInPixels(_window, &pw, &ph);
        SDL_GetWindowSize(_window, &lw, &lh);
        float scale = SDL_GetWindowDisplayScale(_window);

        if (pw == PixelWidth && ph == PixelHeight && lw == LogicalWidth && lh == LogicalHeight
            && scale == ScaleFactor)
            return false;

        PixelWidth = pw;
        PixelHeight = ph;
        LogicalWidth = lw;
        LogicalHeight = lh;
        ScaleFactor = scale;

        _surface?.Resize(pw, ph, ScaleFactor);
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

        SDL_RemoveEventWatch(&EventWatchCallback, GCHandle.ToIntPtr(_gcHandle));
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

using HultUI.Core.Platform;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using SDL;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3;

/// <summary>
/// The real-display windowing platform: SDL3 windows with GL-backed Skia
/// surfaces, driven by SDL's event queue.
/// </summary>
public sealed unsafe class SdlWindowingPlatform : IWindowingPlatform
{
    // The event type Wake pushes. SDL hands types out from a counter it never
    // rewinds, so this is registered once per process and shared by every
    // instance (one test session after another would otherwise leak a type each).
    private static uint _wakeEventType;

    // 1 while a wake event is queued that PumpEvents has not yet drained. A chatty
    // producer — a file-watcher storm, progress reported at 10 kHz — then costs one
    // atomic exchange per call instead of one queued event each: SDL's queue is
    // finite, and every push also runs the event watchers on the pushing thread.
    private int _wakePending;

    // The text services every window draws and measures with: fonts resolved once, caches
    // shared, and nothing of them left behind when a window closes (a window that built its
    // own leaked its HarfBuzz fonts). The preloaded set when HULTUI_GL_PRELOAD made one.
    // Shared on one thread only: an application's windows all run on its UI thread.
    private SkiaTextServices? _text;

    public SdlWindowingPlatform() : this(RendererKind.Default)
    {
    }

    /// <summary>
    /// Creates the platform, asking for a graphics API.
    ///
    /// <para>The request is recorded now rather than passed to each window, because it is
    /// process-wide: the GL driver preload, the shared text pipeline and the video interop's
    /// hidden GL context all assume one backend. <c>HULTUI_RENDERER</c> still overrides it in
    /// both directions — it is how the design docs' commands select a renderer, and how a
    /// machine where Vulkan misbehaves is got working again without a rebuild.</para>
    /// </summary>
    public SdlWindowingPlatform(RendererKind renderer) : this(renderer, displayColorEvents: false)
    {
    }

    /// <summary>
    /// Creates the platform, asking for a graphics API and for whether Vulkan windows follow
    /// display-colour changes by event.
    /// </summary>
    /// <param name="renderer">See <see cref="SdlWindowingPlatform(RendererKind)"/>.</param>
    /// <param name="displayColorEvents">On Windows 11 22H2 and later, subscribe each Vulkan
    /// window to WinRT's <c>AdvancedColorInfoChanged</c>, so that switching HDR or dragging the
    /// SDR brightness slider reaches it immediately rather than on the next 100 ms poll. For
    /// apps that show HDR content; <c>HULTUI_DISPLAY_INFORMATION</c> overrides it either
    /// way.</param>
    public SdlWindowingPlatform(RendererKind renderer, bool displayColorEvents)
    {
        SDL3Window.RequestRenderer(renderer);
        SDL3Window.RequestDisplayColorEvents(displayColorEvents);

        // No SDL_Init needed: registering a type only claims a number.
        if (_wakeEventType == 0)
            _wakeEventType = SDL_RegisterEvents(1);
    }

    public TimeProvider TimeProvider => TimeProvider.System;

    public IPlatformWindow CreateWindow(string title, int width, int height, WindowStyle style)
        => new SDL3Window(title, width, height, style,
            _text ??= SDL3Window.TakePreloadedText() ?? new SkiaTextServices());

    public bool PumpEvents(Action<IPlatformWindow>? onWindowClose)
    {
        // Cleared before polling, not after: a Wake that lands while the queue is
        // being drained must push a fresh event, or its work would sit unnoticed
        // until the next idle wait timed out. One that lands between the clear and
        // the poll pushes an event the poll consumes at once, which is harmless —
        // the queued work is drained later in the same frame regardless.
        Interlocked.Exchange(ref _wakePending, 0);
        return SDL3Window.PollAllEvents(onWindowClose is null ? null : w => onWindowClose(w));
    }

    /// <summary>
    /// Idle-frame wait. A null event peeks without popping: whatever arrives stays
    /// queued for the next <see cref="PumpEvents"/>, the one place events are routed.
    /// </summary>
    /// <remarks>
    /// This used to be a hand-written <c>MsgWaitForMultipleObjectsEx</c> on Windows
    /// (and a plain sleep elsewhere) because a sleeping thread does not process Win32
    /// <em>sent</em> messages, which stalled every UI Automation query for the whole
    /// interval. SDL's own wait is the same Win32 call with the same
    /// <c>QS_ALLINPUT</c> mask followed by a pump, so that behaviour is kept — and,
    /// unlike either of the old paths, it is interrupted on every platform by
    /// <see cref="Wake"/> from another thread. The real-window suite in
    /// HultUI.UiTests is the proof for both halves.
    /// </remarks>
    public void WaitForEvents(int timeoutMs)
    {
        SDL_WaitEventTimeout(null, timeoutMs);
    }

    /// <summary>
    /// Pushes a wake event so a <see cref="WaitForEvents"/> in progress returns at
    /// once. Thread-safe; coalesced while one is already queued. The event carries no
    /// window id, so the pump's routing ignores it.
    /// </summary>
    public void Wake()
    {
        if (Interlocked.Exchange(ref _wakePending, 1) == 1)
            return;

        SDL_Event evt = default;
        evt.type = _wakeEventType;
        // Fails (returns false) only before SDL's event subsystem exists, in which
        // case nothing can be waiting either; PumpEvents clears the flag next frame.
        SDL_PushEvent(&evt);
    }

    public void Dispose()
    {
        _text?.Dispose();
        _text = null;
        SDL3Window.ShutdownSDL();
    }
}

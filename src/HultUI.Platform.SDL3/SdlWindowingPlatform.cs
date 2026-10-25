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

    // 1 from a Wake that pushed an event until the next PumpEvents has polled: until then
    // the loop is sure to come round before it waits for good, so another Wake need not
    // push. A chatty producer — a file-watcher storm, progress reported at 10 kHz — then
    // costs one atomic exchange per call instead of one queued event each: SDL's queue is
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

    /// <remarks>
    /// <para><b>The wake flag is cleared after the poll, not before.</b> Cleared before, a
    /// <see cref="Wake"/> landing between the clear and the poll set it again and pushed an
    /// event the poll then took: the flag said a wake event was queued with none queued, so
    /// every later <see cref="Wake"/> returned without pushing one until the next pump. That
    /// cost at most the 16 ms the idle wait used to last; since the idle loop waits with no
    /// timeout when it owns no deadline, the post waited for the next input. The window is
    /// wide: SDL's first poll pumps the OS (window messages, the event watchers that draw a live
    /// resize, callbacks queued for the main thread) before it takes the queue.</para>
    ///
    /// <para>Cleared after, no wake is lost wherever it lands. Before the clear, its work was
    /// put where the loop looks before it next waits (a post is queued before its wake), and
    /// the loop drains and checks that after this returns. After the clear, it pushes an event
    /// the next wait finds: SDL's wait only peeks. An event pushed during the poll that the
    /// poll left queued (SDL takes, in one poll, only what was queued when it pumped) ends the
    /// next wait at once: a spare pass, not a lost wake. So the work a wake announces must be
    /// read after <see cref="PumpEvents"/> returns, as everything the loop drains is. And
    /// cleared in a <c>finally</c>: a throwing handler must not leave the flag set.</para>
    /// </remarks>
    public bool PumpEvents(Action<IPlatformWindow>? onWindowClose)
    {
        try
        {
            // Passed straight through: an Action<IPlatformWindow> is an Action<SDL3Window>
            // (Action is contravariant), so no adapter is needed. A lambda wrapping it here was
            // a new closure and delegate on every pass of the loop, idle ones included: 88 B
            // (72 B on NativeAOT), 5.7 KB/s in an idle window and 3–4 MB/s while animating.
            return SDL3Window.PollAllEvents(onWindowClose);
        }
        finally
        {
            // A full fence: the loop's look at its queue after this cannot move before it.
            Interlocked.Exchange(ref _wakePending, 0);
        }
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
    ///
    /// <para><b>A millisecond more on Windows.</b> SDL starts its clock, pumps, and hands
    /// <c>MsgWaitForMultipleObjects</c> what is left of the timeout in whole milliseconds,
    /// rounded down — so a wait of <i>n</i> ms parked for <i>n</i> − 1, and a wait of 1 ms
    /// returned at once. <c>Application.Run</c> waits out the last of a display
    /// interval with exactly such a wait while a window is dirty, so it spun instead: on a
    /// 200 Hz display ~45,000 loop passes a second for 200 frames, each pass pumping,
    /// draining posts and ticking animations. One more millisecond in, the microseconds
    /// SDL spends before it waits come off that instead, and Windows gets the <i>n</i> asked
    /// for. Only Windows: SDL's X11 and Wayland waits take the remainder in nanoseconds
    /// (or round it up), and Cocoa's in seconds, so there it already waits what it is
    /// asked to.</para>
    /// </remarks>
    public void WaitForEvents(int timeoutMs)
    {
        if (OperatingSystem.IsWindows() && timeoutMs is > 0 and < int.MaxValue)
            timeoutMs++;
        SDL_WaitEventTimeout(null, timeoutMs);
    }

    /// <summary>
    /// Pushes a wake event so a <see cref="WaitForEvents"/> in progress returns at
    /// once. Thread-safe; coalesced until the next <see cref="PumpEvents"/> has polled.
    /// The event carries no window id, so the pump's routing ignores it.
    /// </summary>
    public void Wake()
    {
        if (Interlocked.Exchange(ref _wakePending, 1) == 1)
            return;

        SDL_Event evt = default;
        evt.type = _wakeEventType;
        // Fails (returns false) only before SDL's event subsystem exists, in which
        // case nothing can be waiting either; PumpEvents clears the flag next pass.
        SDL_PushEvent(&evt);
    }

    public void Dispose()
    {
        _text?.Dispose();
        _text = null;
        SDL3Window.ShutdownSDL();
    }
}

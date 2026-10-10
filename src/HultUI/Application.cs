using System.Collections.Concurrent;
using System.Diagnostics;
using HultUI.Core.Animation;
using HultUI.Core.Components;
using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Platform;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Core.Threading;
using HultUI.DevTools;
using HultUI.Platform.SDL3;

namespace HultUI;

/// <summary>How often the frame loop may render; see <see cref="Application.FramePacing"/>.</summary>
public enum FramePacing
{
    /// <summary>At most once per display interval. Events are still handled as they arrive.</summary>
    Display,
    /// <summary>Whenever anything is dirty, as fast as the GPU returns. For measuring.</summary>
    Unlimited,
}

public sealed class Application : IDisposable
{
    private static Application? _current;

    private readonly IWindowingPlatform _platform;
    private readonly List<Window> _windows = [];
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
    private Theme _theme = Theme.Light;
    private bool _disposed;
    private bool _quitRequested;
    private bool _firstFrameTraced;
    private Action<IPlatformWindow>? _onWindowClose;

    /// <summary>The windowing platform this application runs on.</summary>
    internal IWindowingPlatform Platform => _platform;

    /// <summary>
    /// The clock this application runs on: the system clock behind a real window, and the
    /// manually advanced one under a headless session, where it moves only when a frame is
    /// pumped.
    ///
    /// <para>Anything whose behavior depends on elapsed time should read it here rather than
    /// from <see cref="DateTime"/> or <c>Environment.TickCount64</c>. A wall clock makes such
    /// code untestable in the specific way that hurts: it passes alone and fails in a full run,
    /// because the only thing that changed was how busy the machine was. The pointer's
    /// double-click window has always been measured this way (see <c>PointerState</c>); this
    /// makes the same clock available to applications.</para>
    /// </summary>
    public TimeProvider Time => _platform.TimeProvider;

    /// <summary>
    /// The most recently created Application instance. Used by hot reload to find the running app.
    /// </summary>
    internal static Application? Current => _current;

    /// <summary>
    /// The first window created by this application.
    /// </summary>
    public Window PrimaryWindow => _windows.Count > 0
        ? _windows[0]
        : throw new InvalidOperationException("No windows created.");

    /// <summary>All currently tracked windows (including closed ones not yet disposed).</summary>
    public IReadOnlyList<Window> Windows => _windows;

    /// <summary>
    /// Schedules an action to run on the main (UI) thread during the next
    /// frame, and wakes the loop if it is parked between frames so that frame
    /// starts now. Use this to safely update component state from background
    /// threads (e.g. filesystem watchers, async completions).
    /// </summary>
    public static void InvokeOnMainThread(Action action)
    {
        var app = _current;
        if (app is null) return;
        app._mainThreadQueue.Enqueue(action);
        app.WakeIfParked();
    }

    /// <summary>
    /// Unparks the frame loop after queued work, unless this is the UI thread — which
    /// cannot be parked while it runs this, and whose wake would only queue a spurious
    /// event that makes the next idle wait return at once.
    /// </summary>
    private void WakeIfParked()
    {
        if (Environment.CurrentManagedThreadId == UIThread.BoundThreadId) return;
        _platform.Wake();
    }

    public Theme Theme
    {
        get => _theme;
        set
        {
            _theme = value ?? throw new ArgumentNullException(nameof(value));
            foreach (var w in _windows)
                w.MarkStyleDirty();
        }
    }

    // Convenience accessors that delegate to PrimaryWindow
    public PerformanceCounters Performance => PrimaryWindow.Performance;
    public DevToolsOverlay DevTools => PrimaryWindow.DevTools;
    public IClipboard Clipboard => PrimaryWindow.Clipboard;
    public IFileDialogs FileDialogs => PrimaryWindow.FileDialogs;

    // ──── DevTools inspector ────

    private InspectorHost? _inspectorHost;

    /// <summary>The inspector host, or null until the panel is opened for the first time.</summary>
    internal InspectorHost? InspectorHost => _inspectorHost;

    /// <summary>
    /// Whether the framework's built-in developer chords are live: <b>Ctrl+Shift+I</b>
    /// (element inspector), <b>F12</b> (performance HUD), and <b>F11</b> (trace
    /// recording). Set false in apps that bind those chords themselves, or in shipped
    /// builds where a developer surface has no business appearing.
    ///
    /// Turning it off suppresses only the chords. <see cref="OpenInspector"/>,
    /// <see cref="DevTools"/>, and the <c>HULTUI_TRACE</c> environment variable all
    /// still work, so an app can offer any of it on its own terms.
    /// </summary>
    public bool DevToolsHotkeysEnabled { get; set; } = true;

    /// <summary>True while the inspector panel is open.</summary>
    public bool IsInspectorOpen => _inspectorHost?.IsOpen ?? false;

    /// <summary>
    /// Opens the DevTools inspector against <paramref name="target"/> (default: the
    /// primary window), raising it if it is already open. With
    /// <paramref name="startPicking"/> the picker is armed immediately, so the next
    /// click in the inspected window selects an element instead of operating it.
    /// </summary>
    public void OpenInspector(Window? target = null, bool startPicking = false)
    {
        _inspectorHost ??= new InspectorHost(this);
        _inspectorHost.Open(target ?? PrimaryWindow, startPicking);
    }

    /// <summary>Closes the inspector panel. No-op when it isn't open.</summary>
    public void CloseInspector() => _inspectorHost?.Close();

    internal void ToggleInspector(Window target, bool startPicking)
    {
        _inspectorHost ??= new InspectorHost(this);
        _inspectorHost.Toggle(target, startPicking);
    }

    /// <summary>
    /// Creates the inspector's window. Flagged as DevTools so it is excluded from
    /// inspection and from the run loop's "is anything still open" test.
    /// </summary>
    internal Window CreateDevToolsWindow(string title, int width, int height)
    {
        var window = CreateWindow(title, width, height);
        window.IsDevToolsWindow = true;
        return window;
    }

    /// <summary>
    /// Starts the graphics driver loading on a worker thread, so the work an app does
    /// before its first window — settings, project discovery, theme, the first element
    /// tree — overlaps with it instead of following it. Call it as the first statement
    /// in <c>Main</c>, on the main thread; the <see cref="Application"/> constructor
    /// joins the load before it creates the window.
    ///
    /// <para>Opt-in via <c>HULTUI_GL_PRELOAD=1</c> and a no-op otherwise, because
    /// loading the driver off the main thread is outside SDL's documented threading
    /// contract (see <c>SDL3Window.BeginPreload</c>). Set <c>HULTUI_STARTUP=1</c> to
    /// print the phase timings it is meant to move.</para>
    /// </summary>
    public static void BeginGraphicsPreload() => BeginGraphicsPreload(RendererKind.Default);

    /// <summary>
    /// <see cref="BeginGraphicsPreload()"/> for an app that will ask for a renderer in its
    /// <see cref="Application"/> constructor, told here too because the preload is decided
    /// now, before that constructor runs.
    ///
    /// <para>A preload warms the <b>OpenGL</b> driver, which a Vulkan window never loads. Told
    /// nothing, this cannot know the app is about to ask for Vulkan: Reel did exactly that, and
    /// loaded the GL driver on every launch only for the Vulkan window to wait on it.</para>
    /// </summary>
    /// <param name="renderer">The same renderer the app passes to its constructor.</param>
    public static void BeginGraphicsPreload(RendererKind renderer)
    {
        StartupTrace.Mark("main entry");
        if (renderer != RendererKind.Default) SDL3Window.RequestRenderer(renderer);
        SDL3Window.BeginPreload();
    }

    /// <summary>
    /// Creates an application with a primary window on the default (SDL3)
    /// windowing platform.
    /// </summary>
    /// <param name="renderer">
    /// Which graphics API to draw through. <see cref="RendererKind.Vulkan"/> is what an app
    /// wanting HDR output asks for — it is the only renderer that can present it — and costs
    /// more at start-up and through a resize, both measured in <c>docs/design/HDR.md</c>.
    /// <c>HULTUI_RENDERER</c> overrides this in both directions.
    /// </param>
    /// <param name="displayColorEvents">
    /// On Windows 11 22H2 and later, have Vulkan windows follow display-colour changes — HDR
    /// switched on or off, the SDR brightness slider dragged — the moment they happen, through
    /// WinRT's <c>DisplayInformation</c>, instead of on a 100 ms poll that visibly trails a
    /// dragged slider. Off by default: it is only worth having for HDR content, and it hooks
    /// the window's message loop. <c>HULTUI_DISPLAY_INFORMATION</c> overrides this either way.
    /// </param>
    public Application(string title = "HultUI Application", int width = 800, int height = 600,
        WindowStyle style = WindowStyle.Default, RendererKind renderer = RendererKind.Default,
        bool displayColorEvents = false)
        : this(new SdlWindowingPlatform(renderer, displayColorEvents), title, width, height, style)
    {
    }

    /// <summary>
    /// Creates an application with a primary window on the given windowing
    /// platform. Used by HultUI.Headless to run apps without a display; apps
    /// normally use the default constructor.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Another application is already running in this process. See the remarks.
    /// </exception>
    /// <remarks>
    /// One application per process is not a limitation to work around, it is what the
    /// framework is: <see cref="Current"/>, <see cref="InvokeOnMainThread"/> and the
    /// UI-thread binding are all static, and a second instance would take them over
    /// while the first kept running. Several windows — and several hosted controls —
    /// share one application, which is also what makes <c>InvokeOnMainThread</c> from a
    /// background worker land correctly whichever window's tree it updates.
    ///
    /// <para>This used to be a silent rebind, whose symptom was
    /// <c>InvokeOnMainThread</c> quietly posting to a queue nothing drained. Disposing
    /// an application releases the claim, so creating one, disposing it, and creating
    /// another is fine.</para>
    /// </remarks>
    public Application(IWindowingPlatform platform, string title = "HultUI Application",
        int width = 800, int height = 600, WindowStyle style = WindowStyle.Default)
    {
        if (_current is not null)
            throw new InvalidOperationException(
                "A HultUI Application is already running in this process. Application.Current, " +
                "InvokeOnMainThread and the UI-thread binding are static, so a second one would " +
                "take them from the first. Use the existing application's CreateWindow for another " +
                "window (or, when hosting, HultUIApplicationHost, which owns the one application " +
                "every HultUIControl shares), and dispose the current application before creating " +
                "a replacement.");

        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _current = this;
        // Claim this thread before any window (and therefore any element) exists, so
        // the tree can never be built on one thread and driven from another.
        UIThread.Bind();

        try
        {
            CreateWindow(title, width, height, style);
        }
        catch
        {
            // The claim is only released by Dispose, and there is nothing to dispose:
            // this instance never escaped the constructor. Without this, one failed
            // window creation — no GL context, no display — would leave the process
            // unable to create any application at all, and the second attempt would
            // report the first one as "already running".
            _current = null;
            UIThread.Unbind();
            throw;
        }

        StartupTrace.Mark("application ready");
    }

    /// <summary>
    /// Sets the root element of the primary window.
    /// </summary>
    public void SetRoot(Element root) => PrimaryWindow.SetRoot(root);

    /// <summary>
    /// Creates a new window managed by this application. Shares the application's theme.
    /// </summary>
    public Window CreateWindow(string title, int width = 800, int height = 600,
        WindowStyle style = WindowStyle.Default)
    {
        var platformWindow = _platform.CreateWindow(title, width, height, style);
        var window = new Window(this, platformWindow);
        platformWindow.LiveResizeFrame = () => RenderLiveResizeFrame(window);
        _windows.Add(window);
        return window;
    }

    /// <summary>
    /// Draws one frame from inside the platform's modal resize loop.
    ///
    /// <para><b>Deliberately not throttled.</b> This is the only code that runs while Win32 owns
    /// the thread in <c>WM_ENTERSIZEMOVE</c>, so a frame skipped here is not deferred to the next
    /// pass the way <see cref="ProcessPendingResizes"/>'s throttle defers one — there is no next
    /// pass until the drag pauses. The window has already been resized by the OS, so what the
    /// compositor shows meanwhile is the previous frame scaled to the new size: on Vulkan that
    /// is a visible stretch-then-snap, measured at 402 device pixels for a bar drawn 300 wide.
    /// It was mistaken for a present-mode problem because MAILBOX makes it much rarer than
    /// IMMEDIATE, but both stretch and neither does once this draws every time.</para>
    ///
    /// <para>It costs about 36% more resize frames while a drag is in progress, which is the
    /// right trade: those frames are the ones the user is looking at.</para>
    ///
    /// <para>A resize, when the platform flagged one (a new size, or an area Windows asked to
    /// have painted): laid out again, Resized raised, drawn. Anything else — the second event
    /// of a step, SDL's live-resize timer every ~10 ms while an edge is held or the window is
    /// moved — draws only a window with something new to show: one that is dirty, or one whose
    /// posted work is waiting, which the held loop cannot run. That is how a video keeps
    /// playing while its window is moved: its next frame is posted, and the draw takes it from
    /// the decoder. Each of those events was a whole resize frame before, three to a step of a
    /// drag at one size, and one every ~10 ms of the same picture while the mouse held still.</para>
    /// </summary>
    private void RenderLiveResizeFrame(Window window)
    {
        var platform = window.PlatformWindow;
        if (platform.ResizePending)
        {
            long now = Environment.TickCount64;
            platform.MakeCurrent();
            window.OnWindowResized();
            window.RenderFrame();
            platform.LastResizeRenderTick = now;
            platform.ResizePending = false;
        }
        else if (window.NeedsRender || !_mainThreadQueue.IsEmpty)
        {
            platform.MakeCurrent();
            window.RenderFrame();
        }
    }

    /// <summary>
    /// Whether the application still has work to do: no quit has been requested and
    /// at least one of its own windows is open. <see cref="Run"/> loops on this, and
    /// a host driving <see cref="RunFrame"/> from its own message loop uses it to
    /// decide when to stop.
    /// </summary>
    /// <remarks>
    /// The DevTools window is excluded deliberately: once the app's own windows are
    /// gone there is nothing left to inspect, and an inspector left open would
    /// otherwise keep a closed application running.
    /// </remarks>
    public bool IsRunning => !_quitRequested && _windows.Any(w => w.IsOpen && !w.IsDevToolsWindow);

    /// <summary>
    /// Runs one frame: pumps native events, applies pending resizes and activations,
    /// drains work posted from background threads (and <c>await</c> continuations),
    /// ticks animations, and renders every window that needs a frame. Returns true
    /// when any window rendered.
    ///
    /// <para>This is the whole of <see cref="Run"/>'s loop body, exposed for hosts
    /// that own their own message loop — a WinForms or WPF application embedding
    /// HultUI, a game loop, a test harness. Such a host calls this once per tick and
    /// watches <see cref="IsRunning"/> to know when to stop; it must <em>not</em>
    /// call <see cref="Run"/>, which would block on a loop of its own.</para>
    ///
    /// <para>Nothing here waits: an idle frame returns immediately having rendered
    /// nothing, and parking the thread until the next event is the caller's policy.
    /// <see cref="Run"/> parks for ~16 ms; a host with its own timer or idle handler
    /// does whatever suits it.</para>
    ///
    /// <para>Dirty tracking is respected, so an idle UI costs a walk and no drawing.
    /// Pass <paramref name="forceRender"/> to render regardless — a host that must
    /// repaint on demand (a <c>WM_PAINT</c> after occlusion, where the back buffer's
    /// contents are undefined) needs it, and so does golden-image capture.</para>
    ///
    /// <para>Every call steps animations by <paramref name="delta"/>, and a tween that writes
    /// a signal a <c>Build</c> reads, or calls <c>MarkDirty</c>, rebuilds on every step. Call
    /// it once per frame the host may draw, as <see cref="Run"/> steps them once per frame it
    /// may draw — not once per message.</para>
    /// </summary>
    /// <param name="delta">Time since the previous call, for animation.</param>
    /// <param name="forceRender">Render every open window even when nothing is dirty.</param>
    public bool RunFrame(TimeSpan delta, bool forceRender = false) => RunFrameCore(delta, forceRender, allowRender: true);

    /// <param name="allowRender">False lets the frame pump and drain but not render,
    /// which is how <see cref="Run"/> holds a dirty window back until the display's next
    /// interval; the dirt stays, so the next allowed frame draws it.</param>
    /// <param name="tick">Whether to step animations, by <paramref name="delta"/>.</param>
    private bool RunFrameCore(TimeSpan delta, bool forceRender, bool allowRender, bool tick = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Poll and route all native events. A platform asking to quit ends the frame
        // here, before anything touches a tree the platform may already have torn down.
        // The callback is created once: a method group passed as an argument is a new
        // delegate on every call, 64 bytes on every pass of the loop, idle ones included.
        if (!_platform.PumpEvents(onWindowClose: _onWindowClose ??= OnWindowCloseRequested))
        {
            _quitRequested = true;
            return false;
        }

        ProcessPendingResizes();
        ProcessPendingActivations();

        // Drain actions posted from background threads
        DrainMainThreadQueue();

        // Tick animations (marks elements dirty as needed). Run asks for it once per frame
        // it may draw, not once per pass (see RunUntil).
        if (tick) AnimationManager.Tick((float)delta.TotalSeconds);

        // Run any inspector refresh its rate limit deferred, so the panel still
        // catches up after the inspected window goes idle mid-interval.
        _inspectorHost?.Tick();

        bool anyRendered = allowRender && RenderDirtyWindows(forceRender);

        // Clean up closed windows
        CleanupClosedWindows();

        return anyRendered;
    }

    /// <summary>
    /// Runs the application event loop until all windows are closed. A host that owns
    /// its own message loop drives <see cref="RunFrame"/> instead.
    /// </summary>
    public void Run() => RunUntil(null);

    /// <summary>
    /// <see cref="Run"/>'s loop, which also ends when <paramref name="done"/> returns true; it
    /// is asked once per pass, before the pass. HultUI.Headless runs the real loop this way on
    /// its virtual clock, to see what only the loop does: how often it passes, ticks and
    /// renders.
    /// </summary>
    /// <remarks>
    /// The loop reads time from the platform's <see cref="IWindowingPlatform.TimeProvider"/>:
    /// the system clock behind a real window (whose timestamps are the
    /// <see cref="Stopwatch"/>'s), the virtual one under a headless session, where a wait is
    /// virtual time moving on.
    /// </remarks>
    internal void RunUntil(Func<bool>? done)
    {
        if (_windows.Count == 0)
            throw new InvalidOperationException("No windows created.");

        InstallSynchronizationContext();

        var clock = _platform.TimeProvider;
        long frequency = clock.TimestampFrequency;
        long slack = frequency / 1000;
        long lastTick = clock.GetTimestamp();
        // Clock ticks; the earliest the next frame may render. A second ago: the first frame
        // may render at once, and starts the schedule (a virtual clock starts near 0, where a
        // deadline of 0 put the first frame on the schedule and let the next pass draw again).
        long nextRenderAt = lastTick - frequency;
        // The earliest animations may step again after a step that drew nothing.
        long nextTickAt = nextRenderAt;
        // Whether the last step was drawn: a tween is moving on screen, and its next step
        // is due with the next frame.
        bool stepDrawn = false;

        while (IsRunning && done?.Invoke() != true)
        {
            long now = clock.GetTimestamp();

            // Rendering is paced to the display. The swap interval is 0 (a vsync'd swap
            // blocks the modal resize loop), so nothing else would stop a 300 ms fade
            // from rendering two thousand frames, or a 1000 Hz mouse from making every
            // report a frame. Events are pumped and handled every iteration regardless;
            // only the render waits for its deadline, with a millisecond of slack so the
            // wait below never has to be shorter than one. Unpaced (FramePacing.Unlimited,
            // or the DevTools continuous-redraw mode) renders whenever anything is dirty.
            bool unpaced = FramePacing == FramePacing.Unlimited || AnyWindowContinuousRedraw();
            bool due = unpaced || now >= nextRenderAt - slack;

            // Animations step once per frame that may be drawn, by all the time since their
            // last step: what a browser's requestAnimationFrame does. They stepped on every
            // pass, and every event that ends a wait early is a pass (a 1,000 Hz mouse, a
            // worker's post, a playing video's frames), so a tween writing a signal a Build
            // reads rebuilt that component on each: 15 times a frame with an event every
            // millisecond, 2.2 in Reel while its bar faded. A step that draws nothing (a pure
            // delay, a minimized window, which is not drawn) puts the next one an interval on,
            // so events arriving in between do not step them either.
            bool tick = due && (unpaced || now >= nextTickAt - slack);
            var elapsed = TimeSpan.Zero;
            if (tick)
            {
                elapsed = clock.GetElapsedTime(lastTick, now);
                lastTick = now;
            }

            if (RunFrameCore(elapsed, forceRender: false, allowRender: due, tick))
            {
                if (tick) stepDrawn = true;
                // The next deadline follows the schedule, not this render: a frame that
                // took its slack and drew a millisecond early shifts the phase, not the
                // rate (anchored to now it read 136 fps on a 120 Hz display). A frame
                // more than an interval late restarts the schedule instead of catching up.
                // An unpaced frame keeps no schedule: advancing it per frame at thousands
                // of frames a second once pushed the deadline tens of seconds out, and the
                // window stayed frozen after continuous redraw was switched off.
                long interval = (long)(frequency / DisplayHz());
                nextRenderAt = unpaced ? now
                    : now > nextRenderAt + interval ? now + interval
                    : nextRenderAt + interval;
                continue;
            }

            if (tick)
            {
                stepDrawn = false;
                if (AnimationManager.HasActiveAnimations)
                    nextTickAt = now + (long)(frequency / DisplayHz());
            }

            // Dirty, but too soon: wait out what is left of the interval. An event ends the
            // wait early and is handled on the next iteration; the render still waits its
            // turn. A tween being drawn waits the same way, though nothing is dirty yet: it
            // steps (and dirties) when its frame is due, and the idle wait below would hold
            // it to ~60 frames a second on a faster display.
            if (AnyWindowNeedsRender() || (stepDrawn && AnimationManager.HasActiveAnimations))
            {
                long remainingMs = (nextRenderAt - clock.GetTimestamp()) * 1000 / frequency;
                _platform.WaitForEvents((int)Math.Max(1, remainingMs));
            }
            else
            {
                // Nothing to draw. An animation that changed nothing visible (a pure
                // delay, e.g. the tooltip show timer) only needs a step an interval, so
                // wait instead of busy-spinning until it fires; a post or an event wakes
                // this.
                _platform.WaitForEvents(16);
            }
        }
    }

    /// <summary>The refresh rate of the fastest display any open window is on, in Hz;
    /// 60 when no platform window knows its rate.</summary>
    private float DisplayHz()
    {
        float hz = 0f;
        foreach (var w in _windows)
            if (w.IsOpen) hz = Math.Max(hz, w.PlatformWindow.RefreshRate);
        return hz > 0f ? hz : 60f;
    }

    /// <summary>Whether a window is waiting for its next frame. A minimized one is not: it is
    /// drawn after its restore, whose event wakes the loop (see RenderDirtyWindows).</summary>
    private bool AnyWindowNeedsRender()
    {
        foreach (var w in _windows)
            if (w.IsOpen && w.NeedsRender && !w.PlatformWindow.IsMinimized) return true;
        return false;
    }

    private bool AnyWindowContinuousRedraw()
    {
        foreach (var w in _windows)
            if (w.IsOpen && w.ContinuousRedraw) return true;
        return false;
    }

    /// <summary>
    /// How often <see cref="Run"/> may render: once per display interval (the default,
    /// see <see cref="Window.RefreshRate"/>), or whenever anything is dirty. This is the
    /// loop's own pacing, not the driver's vsync: the swap interval stays 0 either way,
    /// because a vsync'd swap blocks the modal resize loop. Unlimited is for measuring,
    /// which is also what the DevTools continuous-redraw mode does on its own.
    /// </summary>
    public FramePacing FramePacing { get; set; } = FramePacing.Display;

    /// <summary>
    /// Binds async/await continuations on this thread to the UI thread.
    /// Any `await` reached from UI-thread code resumes via _mainThreadQueue
    /// on the next frame, so user code no longer needs InvokeOnMainThread
    /// after awaits. Library I/O can opt out with ConfigureAwait(false).
    /// Returns the previously installed context so callers that don't own the
    /// thread for good (the headless pump) can restore it on teardown.
    /// </summary>
    internal SynchronizationContext? InstallSynchronizationContext()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new UIThreadSynchronizationContext(_mainThreadQueue, WakeIfParked));
        return previous;
    }

    /// <summary>Relayouts windows whose platform reported a resize, honoring the throttle.</summary>
    private void ProcessPendingResizes()
    {
        foreach (var window in _windows)
        {
            if (window.PlatformWindow.ResizePending)
            {
                long now = Environment.TickCount64;
                // Only relayout when the resize throttle interval has passed.
                if (now - window.PlatformWindow.LastResizeRenderTick >= window.ResizeThrottleMs)
                {
                    window.OnWindowResized();
                    window.PlatformWindow.LastResizeRenderTick = now;
                    window.PlatformWindow.ResizePending = false;
                }
                window.Root?.MarkPaintDirty();
            }
        }
    }

    /// <summary>Raises <see cref="Window.Activated"/> for windows the platform reported as
    /// newly foregrounded. Runs before the main-thread queue drains, so a handler that posts
    /// work gets it picked up in the same frame rather than the next one.</summary>
    private void ProcessPendingActivations()
    {
        foreach (var window in _windows)
        {
            if (!window.PlatformWindow.ActivatedPending) continue;
            window.PlatformWindow.ActivatedPending = false;
            if (window.IsOpen) window.RaiseActivated();
        }
    }

    /// <summary>Runs actions posted from background threads and await continuations.</summary>
    private void DrainMainThreadQueue()
    {
        while (_mainThreadQueue.TryDequeue(out var action))
            action();
    }

    /// <summary>
    /// Renders every open window that needs a frame (or all open windows when
    /// <paramref name="force"/> is set). Returns true when any frame was rendered.
    /// </summary>
    internal bool RenderDirtyWindows(bool force)
    {
        bool anyRendered = false;
        foreach (var window in _windows)
        {
            if (!window.IsOpen || (!force && !window.NeedsRender)) continue;
            // A minimized window's frame stops before drawing (see Window.RenderFrame). It is
            // neither timed nor counted as rendered, so a dirty one leaves Run waiting as if
            // idle instead of pacing frames nobody would see: on Vulkan they could not be drawn
            // at all, and the loop retried one every display interval until the restore.
            if (!force && window.PlatformWindow.IsMinimized)
            {
                window.RenderFrame();
                continue;
            }
            window.PlatformWindow.MakeCurrent();
            window.Performance.DisplayHz = window.PlatformWindow.RefreshRate;
            window.Performance.Unpaced = FramePacing == FramePacing.Unlimited || window.ContinuousRedraw;
            window.Performance.BeginFrame();
            window.RenderFrame(evenIfMinimized: force);
            window.Performance.EndFrame();
            // The window is created hidden; reveal it only now that its first
            // frame has been presented, so the OS never composites the blank
            // GL surface. No-op after the first frame.
            window.PlatformWindow.EnsureShown();
            if (StartupTrace.Enabled && !_firstFrameTraced)
            {
                _firstFrameTraced = true;
                StartupTrace.Mark("first frame shown");
            }
            anyRendered = true;
        }
        return anyRendered;
    }

    private void OnWindowCloseRequested(IPlatformWindow platformWindow)
    {
        var window = _windows.FirstOrDefault(w => w.PlatformWindow == platformWindow);
        window?.NotifyClosed();
    }

    private void CleanupClosedWindows()
    {
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            if (!_windows[i].IsOpen)
            {
                _windows[i].Dispose();
                _windows.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Forces every component host across all windows to rebuild, then
    /// marks all windows for re-render. Use after changing global state
    /// (e.g. a theme palette swap) that components read but don't subscribe
    /// to reactively. Hot reload also uses this when code changes apply.
    /// </summary>
    public void RebuildAll()
    {
        foreach (var window in _windows)
        {
            if (window.Root is not null)
                ForceRebuildTree(window.Root);
            window.MarkStyleDirty();
        }
    }


    private static void ForceRebuildTree(Element element)
    {
        if (element is ComponentHost host)
            host.ForceRebuild();

        foreach (var child in element.Children)
            ForceRebuildTree(child);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_current == this)
        {
            _current = null;
            // Return the guard to its inert state. A test host that runs the next
            // session on a different pool thread would otherwise inherit this
            // binding and fail on a thread that is legitimately its UI thread.
            UIThread.Unbind();
        }

        foreach (var window in _windows)
            window.Dispose();
        _windows.Clear();

        _platform.Dispose();
    }
}

internal sealed class UIThreadSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<Action> _queue;
    private readonly Action _wake;

    public UIThreadSynchronizationContext(ConcurrentQueue<Action> queue, Action wake)
    {
        _queue = queue;
        _wake = wake;
    }

    // A continuation whose antecedent completed on another thread (file I/O, a
    // Task.Run) posts from that thread; without the wake it would sit in the queue
    // for the rest of the idle wait.
    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue(() => d(state));
        _wake();
    }

    public override void Send(SendOrPostCallback d, object? state)
        => throw new NotSupportedException(
            "Synchronous Send across threads is not supported; use Post or Application.InvokeOnMainThread.");
}

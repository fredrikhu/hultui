using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using HultUI.Core.Animation;
using HultUI.Core.Styling;

namespace HultUI.Headless;

/// <summary>
/// A complete HultUI application running with no display: raster rendering,
/// a manually pumped frame loop, and virtual time. The entry point for
/// headless testing:
/// <code>
/// using var session = HeadlessSession.Create();
/// session.Window.SetRoot(BuildUi());
/// session.PumpFrame();
/// </code>
/// Sessions may run side by side — two desks of one system in one test — each its
/// own application with its own main-thread queue, synchronization context, tweens
/// and clock. Process-wide state (Application.Current, the animation list, the
/// thread's SynchronizationContext) follows the session a test drives: see
/// <see cref="MakeCurrent"/>. Drive them from one thread at a time, and keep tests
/// that use sessions from running in parallel.
/// </summary>
public sealed partial class HeadlessSession : IDisposable
{
    private readonly HeadlessTimeProvider _time;
    private readonly HeadlessWindowingPlatform _platform;
    private readonly SynchronizationContext? _previousSyncContext;
    // Concurrent because producers run off the test thread, so anything scheduled while
    // they run arrives from several threads at once.
    private readonly ConcurrentQueue<Func<Action?>> _deferredWork = new();
    private bool _disposed;

    /// <summary>The display-free platform this session runs on (tests of the loop's waits).</summary>
    internal HeadlessWindowingPlatform Platform => _platform;

    /// <summary>The application under test. All normal app APIs (theme, InvokeOnMainThread, multi-window) work.</summary>
    public Application Application { get; }

    /// <summary>The primary window's headless driver.</summary>
    public HeadlessWindow Window { get; }

    /// <summary>The session's virtual clock. Advanced by <see cref="PumpFrame()"/> and <see cref="AdvanceTime"/>.</summary>
    public HeadlessTimeProvider Time => _time;

    /// <summary>
    /// Virtual time each parameterless <see cref="PumpFrame()"/> advances by.
    /// Defaults to one 60 Hz frame, at tick precision (TimeSpan.FromSeconds
    /// would round to whole milliseconds).
    /// </summary>
    public TimeSpan DefaultFrameDelta { get; set; } = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private HeadlessSession(HeadlessTimeProvider time, HeadlessWindowingPlatform platform,
        Application application)
    {
        _time = time;
        _platform = platform;
        Application = application;
        var previous = application.InstallSynchronizationContext();
        // A session already alive on this thread: what its context replaced is what to restore.
        _previousSyncContext = previous is UIThreadSynchronizationContext other ? other.Replaced : previous;

        // AsyncLoader's thread-pool hop becomes a queue this session drains at the top
        // of each PumpFrame. A load requested during frame N's build runs its producer
        // at the start of frame N+1 and its completion is applied by that same frame's
        // main-thread drain — the value arrives exactly one frame later, every run,
        // instead of whenever the pool got to it.
        // The queued load is work the loop's next pass must see (RunLoop drains it there), as a
        // real producer's post is: it wakes the loop.
        application.AsyncScheduler = work =>
        {
            _deferredWork.Enqueue(work);
            platform.Wake();
        };
        AsyncWork.SetScheduler(application.AsyncScheduler);

        Window = Wrap(application.PrimaryWindow);
    }

    /// <summary>Creates a session with a primary window of the given layout-unit size.</summary>
    public static HeadlessSession Create(int width = 800, int height = 600, float scale = 1f,
        string title = "Headless", WindowStyle style = WindowStyle.Default,
        HeadlessOptions? options = null)
    {
        var time = new HeadlessTimeProvider();
        var platform = new HeadlessWindowingPlatform(time)
        {
            NextWindowScale = scale,
            Options = options,
        };
        var application = new Application(platform, title, width, height, style);
        return new HeadlessSession(time, platform, application);
    }

    /// <summary>Creates an additional window in this session.</summary>
    public HeadlessWindow CreateWindow(string title = "", int width = 800, int height = 600,
        float scale = 1f, WindowStyle style = WindowStyle.Default)
    {
        ThrowIfDisposed();
        Application.Enter();
        _platform.NextWindowScale = scale;
        return Wrap(Application.CreateWindow(title, width, height, style));
    }

    /// <summary>
    /// Wraps a window this session did not create — one the application opened
    /// itself (a secondary window, the DevTools inspector) — in a driver, so tests
    /// can inject input into it and query its content like any other.
    /// </summary>
    public HeadlessWindow Attach(Window window)
    {
        ThrowIfDisposed();
        if (window.PlatformWindow is not HeadlessPlatformWindow)
            throw new ArgumentException("The window does not belong to this headless session.", nameof(window));
        return Wrap(window);
    }

    /// <summary>The simulated monitors (<see cref="HeadlessOptions.Monitors"/>); empty when the
    /// session has no desktop.</summary>
    public IReadOnlyList<Core.Platform.MonitorInfo> Monitors => _platform.Monitors;

    /// <summary>
    /// Replaces the simulated monitors — plugs one in, unplugs one, rotates or re-sizes one.
    /// Windows move as an OS moves them (off a monitor that went, onto the nearest remaining
    /// one), and <c>Application.MonitorsChanged</c> is raised during the next
    /// <see cref="PumpFrame()"/>, from the event pump, as a real display change is. An empty
    /// list leaves the windows where they are and takes the desktop away.
    /// </summary>
    public void SetMonitors(IReadOnlyList<Core.Platform.MonitorInfo> monitors)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(monitors);
        _platform.SetMonitors(monitors);
    }

    /// <summary>
    /// Makes this session's application the one the code on this thread runs for, as its pumps,
    /// input and <see cref="HeadlessWindow.SetRoot"/> already do: <c>Application.InvokeOnMainThread</c>
    /// (and the work it starts, which carries it along), <c>await</c> continuations, tweens and
    /// <c>AsyncLoader</c> loads go to this session. Call it before a test runs app code for one of
    /// several sessions directly (a store's method, a component's constructor). A no-op while it is
    /// the only session.
    /// </summary>
    public void Enter()
    {
        ThrowIfDisposed();
        Application.Enter();
    }

    private HeadlessWindow Wrap(Window window)
        => new(this, window, (HeadlessPlatformWindow)window.PlatformWindow);

    /// <summary>
    /// Pumps one frame at <see cref="DefaultFrameDelta"/>. Returns true when
    /// any window rendered.
    /// </summary>
    public bool PumpFrame() => PumpFrame(DefaultFrameDelta);

    /// <summary>
    /// Pumps one frame: advances virtual time by <paramref name="delta"/>,
    /// drains main-thread work (InvokeOnMainThread, await continuations),
    /// ticks animations, and renders each open window that needs a frame.
    /// It runs <see cref="Application.RunFrame"/> itself rather than a copy of
    /// it, so a test frame and a real one cannot drift apart.
    /// Dirty-tracking is respected: an idle UI renders nothing, so a missing
    /// dirty-flag bug fails a test here just as it would skip a repaint on
    /// screen. Pass <paramref name="forceRender"/> to render regardless.
    /// Returns true when any window rendered.
    /// </summary>
    public bool PumpFrame(TimeSpan delta, bool forceRender = false)
    {
        ThrowIfDisposed();
        Application.Enter();
        _time.Advance(delta);
        // Before the frame, so a producer's completion is applied by the same frame's
        // main-thread drain inside RunFrame — the one-frame-later contract below.
        DrainDeferredWork();
        return Application.RunFrame(delta, forceRender);
    }

    /// <summary>
    /// Runs the application's own frame loop — <see cref="Application.Run"/>'s, not a copy —
    /// on the virtual clock until <paramref name="until"/> returns true, or until no window is
    /// open. Where the real loop parks the thread, between frames and while it waits out the
    /// display's interval, virtual time moves on instead, so a second of the loop takes
    /// milliseconds and does the same on every machine. Windows render at the display's rate,
    /// 60 Hz here.
    ///
    /// <para>Use it for what <see cref="PumpFrame()"/> cannot show, because a pump is one pass
    /// that always renders: how often the loop passes, ticks animations and renders while
    /// events arrive between frames.</para>
    /// </summary>
    /// <param name="until">Asked once per pass of the loop, before the pass.</param>
    /// <param name="eventEvery">Ends every wait after this much virtual time, as arriving
    /// events do: a 1,000 Hz mouse, a worker posting to the UI thread, a video's frames. Null:
    /// nothing arrives, and every wait runs to its deadline; an idle loop that owns none parks
    /// for good, as it does on a display, so its wait runs past <paramref name="limit"/>, the
    /// condition is asked once more, and the run fails if it is still unmet.</param>
    /// <param name="onEvent">Runs at each of those events, on this thread, inside the wait —
    /// where a real platform would take the event in. Post from it with
    /// <see cref="Application.InvokeOnMainThread"/>, as a worker does, and the next pass
    /// drains it.</param>
    /// <param name="limit">Virtual time after which the run fails with
    /// <see cref="TimeoutException"/>; default 60 seconds.</param>
    public void RunLoop(Func<bool> until, TimeSpan? eventEvery = null, Action? onEvent = null,
        TimeSpan? limit = null)
        => RunLoop(until, eventEvery, onEvent, limit, eventsEndWaits: true);

    /// <summary><see cref="RunLoop(Func{bool}, TimeSpan?, Action?, TimeSpan?)"/>, where with
    /// <paramref name="eventsEndWaits"/> false the events run inside the loop's waits without
    /// ending them, as a message SDL makes no event of does (a WM_GETOBJECT, a DispatcherQueue
    /// item): only a wake one makes, or the wait's own deadline, ends it.</summary>
    internal void RunLoop(Func<bool> until, TimeSpan? eventEvery, Action? onEvent, TimeSpan? limit, bool eventsEndWaits)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(until);
        Application.Enter();
        long deadline = _time.GetTimestamp() + (limit ?? TimeSpan.FromSeconds(60)).Ticks;
        bool timedOut = false;
        _platform.SetEventSource(eventEvery, onEvent, eventsEndWaits);
        // An idle loop with no deadline parks for good on a real display; here its wait runs past
        // this, where nothing more can happen.
        _platform.Horizon = deadline;
        try
        {
            Application.RunUntil(() =>
            {
                if (until()) return true;
                if (_time.GetTimestamp() > deadline) return timedOut = true;
                // Where PumpFrame runs them: AsyncLoader's producers stay one frame behind.
                DrainDeferredWork();
                return false;
            });
        }
        finally
        {
            _platform.SetEventSource(null, null);
            _platform.Horizon = null;
        }
        if (timedOut)
            throw new TimeoutException(
                $"RunLoop's condition was not met within {(limit ?? TimeSpan.FromSeconds(60)).TotalSeconds:0.#} s of virtual time.");
    }

    /// <summary>
    /// Runs the AsyncLoader producers queued since the last frame, then queues their
    /// results for this frame's main-thread drain. Only what was pending when the drain
    /// began: work scheduled by a running item waits for the next frame, preserving the
    /// one-frame-later contract.
    ///
    /// <para>The producers run on the thread pool, all at once, and the drain waits for
    /// every one of them. That is how a running app treats them, and it matters for
    /// speed: a producer is typically a subprocess or I/O wait, and an app that asks for
    /// five views' data at start-up used to wait for five in a row here. Their results
    /// are applied in the order the loads were requested, whichever finished first, so
    /// the outcome does not depend on machine load.</para>
    /// </summary>
    private void DrainDeferredWork()
    {
        int pending = _deferredWork.Count;
        if (pending == 0) return;

        var running = new Task<Action?>[pending];
        for (int i = 0; i < pending; i++)
        {
            _deferredWork.TryDequeue(out var work);
            running[i] = Task.Run(work!);
        }
        try { Task.WaitAll(running); }
        catch (AggregateException) { /* rethrown below, once the others have landed */ }

        foreach (var task in running)
            if (task.IsCompletedSuccessfully && task.Result is { } apply)
                Application.Post(apply);
        foreach (var task in running)
            if (task.IsFaulted)
                ExceptionDispatchInfo.Capture(task.Exception!.InnerException!).Throw();
    }

    /// <summary>Pumps <paramref name="count"/> frames at <see cref="DefaultFrameDelta"/>.</summary>
    public void PumpFrames(int count)
    {
        for (int i = 0; i < count; i++)
            PumpFrame();
    }

    /// <summary>Advances virtual time without pumping a frame.</summary>
    public void AdvanceTime(TimeSpan delta)
    {
        ThrowIfDisposed();
        _time.Advance(delta);
    }

    /// <summary>
    /// Pumps frames until <paramref name="condition"/> is true. UI time is
    /// virtual (one <see cref="DefaultFrameDelta"/> per pump), but the timeout
    /// is real time and the loop yields briefly between pumps — this is the
    /// one place determinism is deliberately relaxed, so background work the
    /// session does not control (raw Task.Run, real I/O) can complete and post
    /// back to the pump. AsyncLoader no longer needs the relaxation: its
    /// producers run deterministically at the top of each frame, so a load is
    /// applied a fixed number of pumps after the Build that requested it.
    /// Throws <see cref="TimeoutException"/> after <paramref name="timeout"/>
    /// (default 5 seconds).
    /// </summary>
    public void PumpUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        ThrowIfDisposed();
        var limit = timeout ?? TimeSpan.FromSeconds(5);
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (condition())
                return;
            if (stopwatch.Elapsed > limit)
                throw new TimeoutException(
                    $"PumpUntil condition not met within {limit.TotalSeconds:0.#}s of real time.");
            PumpFrame();
            Thread.Sleep(1);
        }
    }

    /// <summary>
    /// Pumps frames until <paramref name="task"/> completes, then propagates
    /// its exception if it faulted. See <see cref="PumpUntil"/> for timing.
    /// </summary>
    public void PumpUntilCompleted(Task task, TimeSpan? timeout = null)
    {
        PumpUntil(() => task.IsCompleted, timeout);
        task.GetAwaiter().GetResult();
    }

    /// <summary>Renders all open windows now, without advancing time. Used by pixel capture.</summary>
    internal void RenderNow()
    {
        Application.Enter();
        Application.RenderDirtyWindows(force: true);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Order matters: disposing the application closes windows (which may
        // cancel tooltip tweens), then the animation reset drops whatever test
        // code left running, so nothing ticks into the next session. Disposing
        // hands the process to a session still alive on this thread, if any
        // (Application.Leave), so the reset is of this session's tweens only.
        Application.Dispose();
        AnimationManager.Reset(Application.Animations);

        // AsyncLoader has its thread pool back (or the other session's queue) from
        // the application's disposal; drop producers no frame will ever run — a real
        // app started after this session must not enqueue into a queue nothing drains.
        _deferredWork.Clear();

        // Give the thread back its previous context (under xunit, the
        // AsyncTestSyncContext) — without this, later async tests deadlock
        // posting continuations to a queue nothing drains anymore. Only a dead
        // application's context: another session's live one is in use, and a
        // session disposed from another thread leaves this thread's alone.
        if (SynchronizationContext.Current is UIThreadSynchronizationContext { Owner.IsDisposed: true })
            SynchronizationContext.SetSynchronizationContext(_previousSyncContext);
    }
}

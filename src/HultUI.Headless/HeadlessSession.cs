using System.Diagnostics;
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
/// Sessions own process-global state (Application.Current, the animation
/// list, the thread's SynchronizationContext); create and dispose them
/// sequentially — one session at a time, tests not parallelized.
/// </summary>
public sealed class HeadlessSession : IDisposable
{
    private readonly HeadlessTimeProvider _time;
    private readonly HeadlessWindowingPlatform _platform;
    private readonly SynchronizationContext? _previousSyncContext;
    private readonly Queue<Action> _deferredWork = new();
    private bool _disposed;

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
        _previousSyncContext = application.InstallSynchronizationContext();

        // AsyncLoader's thread-pool hop becomes a queue this session drains at the top
        // of each PumpFrame. A load requested during frame N's build runs its producer
        // at the start of frame N+1 and its completion is applied by that same frame's
        // main-thread drain — the value arrives exactly one frame later, every run,
        // instead of whenever the pool got to it.
        AsyncWork.SetScheduler(_deferredWork.Enqueue);

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
        _time.Advance(delta);
        // Before the frame, so a producer's completion is applied by the same frame's
        // main-thread drain inside RunFrame — the one-frame-later contract below.
        DrainDeferredWork();
        return Application.RunFrame(delta, forceRender);
    }

    /// <summary>
    /// Runs the AsyncLoader producers queued since the last frame. Only what was
    /// pending when the drain began: work scheduled by a running item waits for the
    /// next frame, preserving the one-frame-later contract.
    /// </summary>
    private void DrainDeferredWork()
    {
        int pending = _deferredWork.Count;
        while (pending-- > 0)
            _deferredWork.Dequeue()();
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
    internal void RenderNow() => Application.RenderDirtyWindows(force: true);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Order matters: disposing the application closes windows (which may
        // cancel tooltip tweens), then the animation reset drops whatever test
        // code left running, so nothing ticks into the next session.
        Application.Dispose();
        AnimationManager.Reset();

        // Give AsyncLoader back its thread pool, and drop producers no frame will
        // ever run — a real app started after this session must not enqueue into a
        // queue nothing drains.
        AsyncWork.SetScheduler(null);
        _deferredWork.Clear();

        // Give the thread back its previous context (under xunit, the
        // AsyncTestSyncContext) — without this, later async tests deadlock
        // posting continuations to a queue nothing drains anymore.
        SynchronizationContext.SetSynchronizationContext(_previousSyncContext);
    }
}

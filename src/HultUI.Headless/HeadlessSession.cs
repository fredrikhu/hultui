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
        Window = Wrap(application.PrimaryWindow);
    }

    /// <summary>Creates a session with a primary window of the given layout-unit size.</summary>
    public static HeadlessSession Create(int width = 800, int height = 600, float scale = 1f,
        string title = "Headless", WindowStyle style = WindowStyle.Default)
    {
        var time = new HeadlessTimeProvider();
        var platform = new HeadlessWindowingPlatform(time) { NextWindowScale = scale };
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
    /// ticks animations, and renders each open window that needs a frame —
    /// the same per-iteration sequence as <see cref="Application.Run"/>.
    /// Dirty-tracking is respected: an idle UI renders nothing, so a missing
    /// dirty-flag bug fails a test here just as it would skip a repaint on
    /// screen. Pass <paramref name="forceRender"/> to render regardless.
    /// Returns true when any window rendered.
    /// </summary>
    public bool PumpFrame(TimeSpan delta, bool forceRender = false)
    {
        ThrowIfDisposed();
        _time.Advance(delta);
        Application.ProcessPendingResizes();
        Application.DrainMainThreadQueue();
        AnimationManager.Tick((float)delta.TotalSeconds);
        bool rendered = Application.RenderDirtyWindows(forceRender);
        Application.CleanupClosedWindows();
        return rendered;
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
    /// one place determinism is deliberately relaxed, so background work
    /// (Task.Run, AsyncLoader) can complete and post back to the pump.
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

        // Give the thread back its previous context (under xunit, the
        // AsyncTestSyncContext) — without this, later async tests deadlock
        // posting continuations to a queue nothing drains anymore.
        SynchronizationContext.SetSynchronizationContext(_previousSyncContext);
    }
}

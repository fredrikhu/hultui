using System.Collections.Concurrent;
using System.Diagnostics;
using HultUI.Core.Animation;
using HultUI.Core.Components;
using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Platform;
using HultUI.Core.Styling;
using HultUI.Platform.SDL3;

namespace HultUI;

public sealed class Application : IDisposable
{
    private static Application? _current;

    private readonly IWindowingPlatform _platform;
    private readonly List<Window> _windows = [];
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
    private Theme _theme = Theme.Light;
    private bool _disposed;
    private bool _quitRequested;

    /// <summary>The windowing platform this application runs on.</summary>
    internal IWindowingPlatform Platform => _platform;

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
    /// frame. Use this to safely update component state from background
    /// threads (e.g. filesystem watchers, async completions).
    /// </summary>
    public static void InvokeOnMainThread(Action action)
    {
        _current?._mainThreadQueue.Enqueue(action);
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

    /// <summary>
    /// Creates an application with a primary window on the default (SDL3)
    /// windowing platform.
    /// </summary>
    public Application(string title = "HultUI Application", int width = 800, int height = 600,
        WindowStyle style = WindowStyle.Default)
        : this(new SdlWindowingPlatform(), title, width, height, style)
    {
    }

    /// <summary>
    /// Creates an application with a primary window on the given windowing
    /// platform. Used by HultUI.Headless to run apps without a display; apps
    /// normally use the default constructor.
    /// </summary>
    public Application(IWindowingPlatform platform, string title = "HultUI Application",
        int width = 800, int height = 600, WindowStyle style = WindowStyle.Default)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _current = this;
        CreateWindow(title, width, height, style);
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

    private static void RenderLiveResizeFrame(Window window)
    {
        var platform = window.PlatformWindow;
        long now = Environment.TickCount64;
        if (now - platform.LastResizeRenderTick < window.ResizeThrottleMs)
            return;

        platform.MakeCurrent();
        window.OnWindowResized();
        window.RenderFrame();
        platform.LastResizeRenderTick = now;
        platform.ResizePending = false;
    }

    /// <summary>
    /// Runs the application event loop until all windows are closed.
    /// </summary>
    public void Run()
    {
        if (_windows.Count == 0)
            throw new InvalidOperationException("No windows created.");

        InstallSynchronizationContext();

        var frameStopwatch = Stopwatch.StartNew();

        while (!_quitRequested && _windows.Any(w => w.IsOpen))
        {
            // Delta time
            float deltaSeconds = (float)frameStopwatch.Elapsed.TotalSeconds;
            frameStopwatch.Restart();

            // Poll and route all native events
            bool alive = _platform.PumpEvents(onWindowClose: w => OnWindowCloseRequested(w));

            if (!alive)
            {
                _quitRequested = true;
                break;
            }

            ProcessPendingResizes();

            // Drain actions posted from background threads
            DrainMainThreadQueue();

            // Tick animations (marks elements dirty as needed)
            AnimationManager.Tick(deltaSeconds);

            // Render any dirty windows
            bool anyRendered = RenderDirtyWindows(force: false);

            // Animations that changed something visible marked it dirty in Tick
            // above and rendered this iteration (anyRendered). One that didn't
            // (a pure delay, e.g. the tooltip show timer) only needs ~60Hz ticks,
            // so wait instead of busy-spinning until it fires.
            if (!anyRendered)
                _platform.WaitForEvents(16);

            // Clean up closed windows
            CleanupClosedWindows();
        }
    }

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
            new UIThreadSynchronizationContext(_mainThreadQueue));
        return previous;
    }

    /// <summary>Relayouts windows whose platform reported a resize, honoring the throttle.</summary>
    internal void ProcessPendingResizes()
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

    /// <summary>Runs actions posted from background threads and await continuations.</summary>
    internal void DrainMainThreadQueue()
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
            window.PlatformWindow.MakeCurrent();
            window.Performance.BeginFrame();
            window.RenderFrame();
            window.Performance.EndFrame();
            // The window is created hidden; reveal it only now that its first
            // frame has been presented, so the OS never composites the blank
            // GL surface. No-op after the first frame.
            window.PlatformWindow.EnsureShown();
            anyRendered = true;
        }
        return anyRendered;
    }

    private void OnWindowCloseRequested(IPlatformWindow platformWindow)
    {
        var window = _windows.FirstOrDefault(w => w.PlatformWindow == platformWindow);
        window?.NotifyClosed();
    }

    internal void CleanupClosedWindows()
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
            _current = null;

        foreach (var window in _windows)
            window.Dispose();
        _windows.Clear();

        _platform.Dispose();
    }
}

internal sealed class UIThreadSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<Action> _queue;

    public UIThreadSynchronizationContext(ConcurrentQueue<Action> queue)
    {
        _queue = queue;
    }

    public override void Post(SendOrPostCallback d, object? state)
        => _queue.Enqueue(() => d(state));

    public override void Send(SendOrPostCallback d, object? state)
        => throw new NotSupportedException(
            "Synchronous Send across threads is not supported; use Post or Application.InvokeOnMainThread.");
}

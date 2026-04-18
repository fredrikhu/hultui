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

    private readonly List<Window> _windows = [];
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
    private Theme _theme = Theme.Light;
    private bool _disposed;
    private bool _quitRequested;

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
    /// Creates an application with a primary window.
    /// </summary>
    public Application(string title = "HultUI Application", int width = 800, int height = 600,
        WindowStyle style = WindowStyle.Default)
    {
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
        var sdlWindow = new SDL3Window(title, width, height, style);
        var window = new Window(this, sdlWindow);
        _windows.Add(window);
        return window;
    }

    /// <summary>
    /// Runs the application event loop until all windows are closed.
    /// </summary>
    public void Run()
    {
        if (_windows.Count == 0)
            throw new InvalidOperationException("No windows created.");

        // Bind async/await continuations on this thread to the UI thread.
        // Any `await` reached from UI-thread code resumes via _mainThreadQueue
        // on the next frame, so user code no longer needs InvokeOnMainThread
        // after awaits. Library I/O can opt out with ConfigureAwait(false).
        SynchronizationContext.SetSynchronizationContext(
            new UIThreadSynchronizationContext(_mainThreadQueue));

        var frameStopwatch = Stopwatch.StartNew();

        while (!_quitRequested && _windows.Any(w => w.IsOpen))
        {
            // Delta time
            float deltaSeconds = (float)frameStopwatch.Elapsed.TotalSeconds;
            frameStopwatch.Restart();

            // Poll and route all SDL events
            bool alive = SDL3Window.PollAllEvents(onWindowClose: w => OnWindowCloseRequested(w));

            if (!alive)
            {
                _quitRequested = true;
                break;
            }
            
            foreach (var window in _windows)
            {
                if (window.PlatformWindow.ResizePending)
                {
                    long now = Environment.TickCount64;
                    // Only execute if 50ms has passed
                    if (now - window.PlatformWindow.LastResizeRenderTick >= window.ResizeThrottleMs)
                    {
                        window.OnWindowResized();
                        window.PlatformWindow.LastResizeRenderTick = now;
                        window.PlatformWindow.ResizePending = false; 
                    }
                    window.Root?.MarkPaintDirty();
                }
            }

            // Drain actions posted from background threads
            while (_mainThreadQueue.TryDequeue(out var action))
                action();

            // Tick animations (marks elements dirty as needed)
            AnimationManager.Tick(deltaSeconds);

            // Render any dirty windows
            bool anyRendered = false;
            foreach (var window in _windows)
            {
                if (!window.IsOpen || !window.NeedsRender) continue;
                window.PlatformWindow.MakeCurrent();
                window.Performance.BeginFrame();
                window.RenderFrame();
                window.Performance.EndFrame();
                anyRendered = true;
            }

            if (!anyRendered && !AnimationManager.HasActiveAnimations)
                Thread.Sleep(16);

            // Clean up closed windows
            CleanupClosedWindows();
        }
    }

    private void OnWindowCloseRequested(SDL3Window platformWindow)
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
    /// Forces all component hosts to rebuild, then marks all windows for re-render.
    /// Called by hot reload when code changes are applied.
    /// </summary>
    internal void ForceRebuildAll()
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

        SDL3Window.ShutdownSDL();
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

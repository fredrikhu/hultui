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
    public Application(string title = "HultUI Application", int width = 800, int height = 600)
    {
        _current = this;
        CreateWindow(title, width, height);
    }

    /// <summary>
    /// Sets the root element of the primary window.
    /// </summary>
    public void SetRoot(Element root) => PrimaryWindow.SetRoot(root);

    /// <summary>
    /// Creates a new window managed by this application. Shares the application's theme.
    /// </summary>
    public Window CreateWindow(string title, int width = 800, int height = 600)
    {
        var sdlWindow = new SDL3Window(title, width, height);
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

        while (!_quitRequested && _windows.Any(w => w.IsOpen))
        {
            // Poll and route all SDL events
            bool alive = SDL3Window.PollAllEvents(onWindowClose: w => OnWindowCloseRequested(w));

            if (!alive)
            {
                _quitRequested = true;
                break;
            }

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

            if (!anyRendered)
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

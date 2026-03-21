using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Platform.SDL3;

namespace HultUI;

public sealed class Application : IDisposable
{
    private readonly SDL3Window _window;
    private readonly LayoutEngine _layoutEngine;
    private Element? _root;
    private bool _disposed;

    public Application(string title = "HultUI Application", int width = 800, int height = 600)
    {
        _window = new SDL3Window(title, width, height);
        _layoutEngine = new LayoutEngine(_window.Surface.MeasureContext);
    }

    public void SetRoot(Element root)
    {
        _root = root;
    }

    public void Run()
    {
        if (_root is null)
            throw new InvalidOperationException("No root element set. Call SetRoot before Run.");

        while (_window.IsOpen)
        {
            _window.PollEvents();
            if (!_window.IsOpen) break;

            // Style resolution (inherited properties flow top-down)
            StyleResolver.Resolve(_root);

            // Layout
            _layoutEngine.PerformLayout(_root, _window.LogicalWidth, _window.LogicalHeight);

            // Render
            var ctx = _window.Surface.RenderContext;
            ctx.BeginFrame(_window.LogicalWidth, _window.LogicalHeight, _window.ScaleFactor);
            ElementRenderer.RenderTree(_root, ctx);
            ctx.EndFrame();

            // Present
            _window.Surface.Present();
            _window.SwapBuffers();

            // Don't spin at 100% CPU — cap at ~60fps
            Thread.Sleep(16);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Dispose();
    }
}

using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Platform.SDL3;

namespace HultUI;

public sealed class Application : IDisposable
{
    private readonly SDL3Window _window;
    private readonly LayoutEngine _layoutEngine;
    private readonly FocusManager _focusManager = new();
    private readonly PointerState _pointerState = new();
    private readonly PerformanceCounters _perf = new();
    private readonly DevToolsOverlay _devTools;
    private Element? _root;
    private Theme _theme = Theme.Light;
    private bool _disposed;
    private Element? _lastHoveredElement;

    public IClipboard Clipboard { get; }
    public PerformanceCounters Performance => _perf;
    public DevToolsOverlay DevTools => _devTools;

    public Theme Theme
    {
        get => _theme;
        set => _theme = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Application(string title = "HultUI Application", int width = 800, int height = 600)
    {
        _window = new SDL3Window(title, width, height);
        _layoutEngine = new LayoutEngine(_window.Surface.MeasureContext);
        _window.EventTranslator.OnInputEvent += HandleInputEvent;
        Clipboard = new SDL3Clipboard(_window);
        _devTools = new DevToolsOverlay(_perf);
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
            _perf.BeginFrame();

            _window.PollEvents();
            if (!_window.IsOpen) break;

            // Style resolution
            _perf.BeginStyle();
            StyleResolver.Resolve(_root, _theme.ToResolvedStyle());
            _perf.EndStyle();

            // Layout
            _perf.BeginLayout();
            _layoutEngine.PerformLayout(_root, _window.LogicalWidth, _window.LogicalHeight);
            _perf.EndLayout();

            // Render
            _perf.BeginRender();
            var ctx = _window.Surface.RenderContext;
            ctx.BeginFrame(_window.LogicalWidth, _window.LogicalHeight, _window.ScaleFactor, _theme.Background);
            ElementRenderer.RenderTree(_root, ctx);

            // DevTools overlay (renders on top of everything)
            _perf.ElementCount = PerformanceCounters.CountElements(_root);
            _devTools.Render(ctx, _root, _window.LogicalWidth, _window.LogicalHeight);

            ctx.EndFrame();
            _perf.EndRender();

            _perf.EndFrame();

            // Present
            _window.Surface.Present();
            _window.SwapBuffers();

            // Don't spin at 100% CPU — cap at ~60fps
            Thread.Sleep(16);
        }
    }

    private void HandleInputEvent(InputEvent evt)
    {
        if (_root is null) return;

        switch (evt)
        {
            case InputEvent.MouseMoveInput move:
            {
                var pos = new Point(move.X, move.Y);
                var target = HitTester.HitTest(_root, pos);
                _pointerState.UpdateHover(target, pos, move.Modifiers);
                _lastHoveredElement = target;

                // Update cursor based on hovered element's resolved style
                var cursor = target?.ResolvedInherited.Cursor ?? CursorType.Default;
                _window.SetCursor(cursor);

                // Update DevTools inspector
                if (_devTools.IsEnabled)
                    _devTools.HoveredElement = target;

                if (target is not null)
                {
                    EventDispatcher.Dispatch(
                        new PointerMoveEvent { Position = pos, Delta = new Point(move.DeltaX, move.DeltaY), Modifiers = move.Modifiers },
                        target);
                }
                break;
            }

            case InputEvent.MouseButtonInput button:
            {
                var pos = new Point(button.X, button.Y);
                var target = HitTester.HitTest(_root, pos);
                if (target is null) break;

                if (button.IsDown)
                {
                    _focusManager.SetFocus(target);
                    var downEvt = _pointerState.HandlePointerDown(target, pos, button.Button, button.Modifiers);
                    EventDispatcher.Dispatch(downEvt, target);
                }
                else
                {
                    var (upEvt, clickEvt, dblClickEvt) = _pointerState.HandlePointerUp(target, pos, button.Button, button.Modifiers);
                    EventDispatcher.Dispatch(upEvt, target);
                    if (clickEvt is not null)
                        EventDispatcher.Dispatch(clickEvt, target);
                    if (dblClickEvt is not null)
                        EventDispatcher.Dispatch(dblClickEvt, target);
                }
                break;
            }

            case InputEvent.MouseWheelInput wheel:
            {
                var pos = new Point(wheel.MouseX, wheel.MouseY);
                var target = HitTester.HitTest(_root, pos);
                if (target is not null)
                {
                    EventDispatcher.Dispatch(
                        new ScrollEvent { Position = pos, DeltaX = wheel.DeltaX, DeltaY = wheel.DeltaY, Modifiers = wheel.Modifiers },
                        target);
                }
                break;
            }

            case InputEvent.KeyInput key:
            {
                // F12 toggles DevTools
                if (key.IsDown && key.KeyCode == KeyCode.F12)
                {
                    _devTools.IsEnabled = !_devTools.IsEnabled;
                    _devTools.ShowBoundingBoxes = _devTools.IsEnabled;
                    if (!_devTools.IsEnabled)
                        _devTools.HoveredElement = null;
                    break;
                }

                var target = _focusManager.FocusedElement ?? _root;
                if (key.IsDown)
                {
                    EventDispatcher.Dispatch(
                        new KeyDownEvent { Key = key.KeyCode, Modifiers = key.Modifiers, IsRepeat = key.IsRepeat },
                        target);
                }
                else
                {
                    EventDispatcher.Dispatch(
                        new KeyUpEvent { Key = key.KeyCode, Modifiers = key.Modifiers },
                        target);
                }
                break;
            }

            case InputEvent.TextInputInput textInput:
            {
                var target = _focusManager.FocusedElement ?? _root;
                EventDispatcher.Dispatch(new TextInputEvent { Text = textInput.Text }, target);
                break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Dispose();
    }
}

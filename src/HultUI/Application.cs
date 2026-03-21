using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
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
    private Element? _root;
    private Theme _theme = Theme.Light;
    private bool _disposed;

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

            // Style resolution (inherited properties flow top-down from theme)
            StyleResolver.Resolve(_root, _theme.ToResolvedStyle());

            // Layout
            _layoutEngine.PerformLayout(_root, _window.LogicalWidth, _window.LogicalHeight);

            // Render
            var ctx = _window.Surface.RenderContext;
            ctx.BeginFrame(_window.LogicalWidth, _window.LogicalHeight, _window.ScaleFactor, _theme.Background);
            ElementRenderer.RenderTree(_root, ctx);
            ctx.EndFrame();

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

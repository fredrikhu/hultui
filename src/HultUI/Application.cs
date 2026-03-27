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
    private ScrollViewElement? _scrollbarDragTarget;
    private TextInputElement? _textInputDragTarget;

    public IClipboard Clipboard { get; }
    public PerformanceCounters Performance => _perf;
    public DevToolsOverlay DevTools => _devTools;

    public Theme Theme
    {
        get => _theme;
        set
        {
            _theme = value ?? throw new ArgumentNullException(nameof(value));
            _root?.MarkStyleDirty();
        }
    }

    public Application(string title = "HultUI Application", int width = 800, int height = 600)
    {
        _window = new SDL3Window(title, width, height);
        _layoutEngine = new LayoutEngine(_window.Surface.MeasureContext);
        _window.EventTranslator.OnInputEvent += HandleInputEvent;
        _window.Resized += OnWindowResized;
        Clipboard = new SDL3Clipboard(_window);
        _devTools = new DevToolsOverlay(_perf);
    }

    public void SetRoot(Element root)
    {
        _root = root;
    }

    private void OnWindowResized()
    {
        if (_root is null) return;
        _root.MarkLayoutDirty();
        _root.MarkPaintDirty();
        // Render immediately so the user sees live resize preview
        RenderFrame();
    }

    public void Run()
    {
        if (_root is null)
            throw new InvalidOperationException("No root element set. Call SetRoot before Run.");

        while (_window.IsOpen)
        {
            _window.PollEvents();
            if (!_window.IsOpen) break;

            // Skip the entire pipeline when nothing has changed
            if (_root.DirtyFlags == DirtyFlags.None)
            {
                Thread.Sleep(16);
                continue;
            }

            _perf.BeginFrame();
            RenderFrame();
            _perf.EndFrame();
        }
    }

    private void RenderFrame()
    {
        if (_root is null) return;

        // Style resolution (only if any element has style changes)
        _perf.BeginStyle();
        if (_root.DirtyFlags.HasFlag(DirtyFlags.Style))
        {
            StyleResolver.Resolve(_root, _theme.ToResolvedStyle());
            ClearFlags(_root, DirtyFlags.Style);
        }
        _perf.EndStyle();

        // Layout (only if any element has layout changes)
        _perf.BeginLayout();
        if (_root.DirtyFlags.HasFlag(DirtyFlags.Layout))
            _layoutEngine.PerformLayout(_root, _window.LogicalWidth, _window.LogicalHeight);
        _perf.EndLayout();

        // Render (user content only — DevTools excluded from timing)
        var ctx = _window.Surface.RenderContext;
        ctx.BeginFrame(_window.LogicalWidth, _window.LogicalHeight, _window.ScaleFactor, _theme.Background);
        _perf.BeginRender();
        ElementRenderer.RenderTree(_root, ctx);
        _perf.EndRender();

        // DevTools overlay (second pass, not timed)
        _perf.ElementCount = PerformanceCounters.CountElements(_root);
        _devTools.Render(ctx, _root, _window.LogicalWidth, _window.LogicalHeight);

        ctx.EndFrame();

        // Clear remaining dirty flags after full render
        ClearAllFlags(_root);

        // Present + swap
        _perf.BeginPresent();
        _window.Surface.Present();
        _window.SwapBuffers();
        _perf.EndPresent();
    }

    private void HandleInputEvent(InputEvent evt)
    {
        if (_root is null) return;

        switch (evt)
        {
            case InputEvent.MouseMoveInput move:
            {
                var pos = new Point(move.X, move.Y);

                // If dragging a scrollbar, route directly to the drag target
                if (_scrollbarDragTarget is not null)
                {
                    _scrollbarDragTarget.UpdateScrollbarDrag(pos);
                    break;
                }

                // If dragging in a text input, route move to the capture target
                if (_textInputDragTarget is not null)
                {
                    _textInputDragTarget.UpdateDragPosition(pos);
                    break;
                }

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

                // End scrollbar drag on button up
                if (!button.IsDown && _scrollbarDragTarget is not null)
                {
                    _scrollbarDragTarget.EndScrollbarDrag();
                    _scrollbarDragTarget = null;
                    break;
                }

                // End text input drag on button up
                if (!button.IsDown && _textInputDragTarget is not null)
                {
                    _textInputDragTarget.EndDrag();
                    _textInputDragTarget = null;
                    // Don't break — still dispatch PointerUp/Click to the target
                }

                var target = HitTester.HitTest(_root, pos);
                if (target is null) break;

                if (button.IsDown)
                {
                    // Check if click is on a scrollbar — walk up from target to find a ScrollViewElement
                    if (button.Button == 0 && TryStartScrollbarDrag(target, pos))
                        break;

                    _focusManager.SetFocus(target);

                    // Provide clipboard to text inputs when focused
                    if (target is TextInputElement textInput)
                        textInput.Clipboard = Clipboard;

                    var downEvt = _pointerState.HandlePointerDown(target, pos, button.Button, button.Modifiers);
                    EventDispatcher.Dispatch(downEvt, target);

                    // Start pointer capture for text input drag selection
                    if (button.Button == 0 && target is TextInputElement dragInput && dragInput.IsDragging)
                        _textInputDragTarget = dragInput;
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

        // Any input event may have triggered visual changes via handlers,
        // so ensure the render pipeline runs this frame.
        _root.MarkPaintDirty();
    }

    /// <summary>
    /// Walks from the hit target up through ancestors looking for a ScrollViewElement
    /// whose scrollbar was clicked. Returns true if a drag was started or track was clicked.
    /// </summary>
    private bool TryStartScrollbarDrag(Element target, Point pos)
    {
        Element? current = target;
        while (current is not null)
        {
            if (current is ScrollViewElement sv && sv.TryStartScrollbarDrag(pos))
            {
                if (sv.IsDraggingScrollbar)
                    _scrollbarDragTarget = sv;
                return true;
            }
            current = current.Parent;
        }
        return false;
    }

    private static void ClearFlags(Element element, DirtyFlags flags)
    {
        element.DirtyFlags &= ~flags;
        foreach (var child in element.Children)
            ClearFlags(child, flags);
    }

    private static void ClearAllFlags(Element element)
    {
        element.DirtyFlags = DirtyFlags.None;
        foreach (var child in element.Children)
            ClearAllFlags(child);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Dispose();
    }
}

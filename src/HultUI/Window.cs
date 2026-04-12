using HultUI.Core.Animation;
using HultUI.Core.Builders;
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

public sealed class Window : IOverlayHost, IDisposable
{
    private readonly LayoutEngine _layoutEngine;
    private readonly FocusManager _focusManager = new();
    private readonly PointerState _pointerState = new();
    private readonly PerformanceCounters _perf = new();
    private readonly DevToolsOverlay _devTools;
    private Element? _root;
    private Element? _lastHoveredElement;
    private ScrollViewElement? _scrollbarDragTarget;
    private TextInputElement? _textInputDragTarget;
    private TextAreaElement? _textAreaDragTarget;
    private bool _disposed;

    // ──── Overlay state ────
    private readonly List<OverlayEntry> _overlays = [];
    private int _nextOverlayId = 1;

    // ──── Tooltip state ────
    private Element? _tooltipOwner;
    private TweenHandle? _tooltipDelayTween;
    private OverlayHandle _tooltipOverlay;

    // ──── Context menu state ────
    private OverlayHandle _contextMenuOverlay;
    private ContextMenuElement? _contextMenuElement;

    // ──── Drag & Drop state ────
    private const float DragThreshold = 5f;
    private Element? _potentialDragSource;
    private Element? _dragSource;
    private Point _dragStartPos;
    private bool _isDragging;
    private DragData? _dragData;
    private Element? _dragHoveredElement;
    private Element? _dragVisual;
    private bool _dragVisualLayoutDone;
    private Point _lastDragPos;
    private bool _dragAccepted;
    private bool _isExternalDrag;

    internal SDL3Window PlatformWindow { get; }
    public long ResizeThrottleMs { get; set; } = 16;
    internal Application Application { get; }

    public IClipboard Clipboard { get; }
    public PerformanceCounters Performance => _perf;
    public DevToolsOverlay DevTools => _devTools;
    public bool IsOpen => PlatformWindow.IsOpen && !_disposed;
    public WindowStyle Style => PlatformWindow.Style;

    // ──── Window management (custom chrome) ────
    public bool IsMaximized => PlatformWindow.IsMaximized;
    public void Minimize() => PlatformWindow.Minimize();
    public void Maximize() => PlatformWindow.Maximize();
    public void Restore() => PlatformWindow.Restore();
    public void ToggleMaximize() { if (IsMaximized) Restore(); else Maximize(); }

    /// <summary>Width in pixels of the invisible resize border for borderless windows.</summary>
    public int ResizeBorderWidth { get; set; } = 6;

    public Theme? ActiveTheme => Application.Theme;

    public event Action? Closed;

    internal Window(Application application, SDL3Window platformWindow)
    {
        Application = application;
        PlatformWindow = platformWindow;
        _layoutEngine = new LayoutEngine(platformWindow.Surface.MeasureContext);
        Clipboard = new SDL3Clipboard(platformWindow);
        _devTools = new DevToolsOverlay(_perf);
        PlatformWindow.EventTranslator.OnInputEvent += HandleInputEvent;
        PlatformWindow.Resized += OnWindowResized;

        if (platformWindow.Style == WindowStyle.Borderless)
            platformWindow.HitTestHandler = HandleWindowHitTest;

        _focusManager.FocusChanged += OnFocusChanged;
    }

    private void OnFocusChanged(Element? element)
    {
        if (element is TextInputElement textInput)
            textInput.Clipboard = Clipboard;
        else if (element is TextAreaElement textArea)
            textArea.Clipboard = Clipboard;
    }

    public void SetRoot(Element root)
    {
        _root = root;
        root.OverlayHost = this;
        root.FocusManager = _focusManager;
    }

    internal Element? Root => _root;

    internal void MarkStyleDirty()
    {
        _root?.MarkStyleDirty();
    }

    internal bool NeedsRender => (_root is not null && _root.DirtyFlags != DirtyFlags.None)
        || _overlays.Count > 0;

    internal void OnWindowResized()
    {
        if (_root is null) return;
        _root.MarkLayoutDirty();
        _root.MarkPaintDirty();
    }

    internal void RenderFrame()
    {
        if (_root is null) return;

        var theme = Application.Theme;

        // Style resolution (only if any element has style changes)
        _perf.BeginStyle();
        if (_root.DirtyFlags.HasFlag(DirtyFlags.Style))
        {
            StyleResolver.Resolve(_root, theme);
            ClearFlags(_root, DirtyFlags.Style);
        }
        _perf.EndStyle();

        // Layout (only if any element has layout changes)
        _perf.BeginLayout();
        if (_root.DirtyFlags.HasFlag(DirtyFlags.Layout))
            _layoutEngine.PerformLayout(_root, PlatformWindow.LogicalWidth, PlatformWindow.LogicalHeight);
        _perf.EndLayout();

        // Render (user content only — DevTools excluded from timing)
        var ctx = PlatformWindow.Surface.RenderContext;
        ctx.BeginFrame(PlatformWindow.LogicalWidth, PlatformWindow.LogicalHeight, PlatformWindow.ScaleFactor, theme.Background);
        _perf.BeginRender();
        ElementRenderer.RenderTree(_root, ctx);
        _perf.EndRender();

        // Drag ghost overlay
        if (_isDragging && !_isExternalDrag)
            RenderDragGhost(ctx);

        // Overlay layer (tooltips, context menus, popups)
        RenderOverlays(ctx);

        // DevTools overlay (second pass, not timed)
        _perf.ElementCount = PerformanceCounters.CountElements(_root);
        _devTools.Render(ctx, _root, PlatformWindow.LogicalWidth, PlatformWindow.LogicalHeight);

        ctx.EndFrame();

        // Clear remaining dirty flags after full render
        ClearAllFlags(_root);
        foreach (var overlay in _overlays)
            ClearAllFlags(overlay.Content);

        // Present + swap
        _perf.BeginPresent();
        PlatformWindow.Surface.Present();
        PlatformWindow.SwapBuffers();
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

                if (_textAreaDragTarget is not null)
                {
                    _textAreaDragTarget.UpdateDragPosition(pos);
                    break;
                }

                // If actively dragging (internal), update drag hover
                if (_isDragging && !_isExternalDrag)
                {
                    UpdateDragHover(pos);
                    break;
                }

                // Check if a potential drag should activate
                if (_potentialDragSource is not null)
                {
                    float dx = pos.X - _dragStartPos.X;
                    float dy = pos.Y - _dragStartPos.Y;
                    if (dx * dx + dy * dy >= DragThreshold * DragThreshold)
                    {
                        StartInternalDrag(pos);
                        break;
                    }
                }

                // Check overlays first (topmost layer), then main tree
                var (overlayMoveTarget, overlayMoveOffset) = HitTestOverlays(pos);
                var target = overlayMoveTarget ?? HitTester.HitTest(_root, pos);
                var effectivePos = overlayMoveTarget is not null
                    ? new Point(pos.X - overlayMoveOffset.X, pos.Y - overlayMoveOffset.Y)
                    : pos;
                _pointerState.UpdateHover(target, effectivePos, move.Modifiers);
                _lastHoveredElement = target;

                // Update cursor based on hovered element's resolved style
                var cursor = target?.ResolvedInherited.Cursor ?? CursorType.Default;
                PlatformWindow.SetCursor(cursor);

                // Tooltip tracking
                UpdateTooltipHover(target);

                // Update DevTools inspector
                if (_devTools.IsEnabled)
                    _devTools.HoveredElement = target;

                if (target is not null)
                {
                    EventDispatcher.Dispatch(
                        new PointerMoveEvent { Position = effectivePos, Delta = new Point(move.DeltaX, move.DeltaY), Modifiers = move.Modifiers },
                        target);
                }
                break;
            }

            case InputEvent.MouseButtonInput button:
            {
                var pos = new Point(button.X, button.Y);

                // Hide tooltip on any mouse button press
                if (button.IsDown)
                    HideTooltip();

                // End internal drag on button up
                if (!button.IsDown && _isDragging && !_isExternalDrag)
                {
                    EndInternalDrag(pos);
                    break;
                }

                // Cancel potential drag on button up (didn't reach threshold)
                if (!button.IsDown && _potentialDragSource is not null)
                    _potentialDragSource = null;

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

                if (!button.IsDown && _textAreaDragTarget is not null)
                {
                    _textAreaDragTarget.EndDrag();
                    _textAreaDragTarget = null;
                }

                // Check overlays first, then main tree
                var (overlayTarget, overlayOffset) = HitTestOverlays(pos);
                var target = overlayTarget ?? HitTester.HitTest(_root, pos);
                if (target is null) break;

                bool isOverlayHit = overlayTarget is not null;
                var effectiveBtnPos = isOverlayHit
                    ? new Point(pos.X - overlayOffset.X, pos.Y - overlayOffset.Y)
                    : pos;

                if (button.IsDown)
                {
                    // Dismiss overlays when clicking outside them
                    if (!isOverlayHit && _overlays.Count > 0)
                        DismissOverlaysOnOutsideClick();

                    // Check if click is on a scrollbar — walk up from target to find a ScrollViewElement
                    if (!isOverlayHit && button.Button == 0 && TryStartScrollbarDrag(target, pos))
                        break;

                    // Don't change focus when clicking overlay elements
                    // (e.g. context menu on TextInput should keep selection visible)
                    if (!isOverlayHit)
                    {
                        var focusTarget = FindFocusableAncestor(target);
                        _focusManager.SetFocus(focusTarget);

                        // Provide clipboard to text inputs when focused
                        if (focusTarget is TextInputElement textInput)
                            textInput.Clipboard = Clipboard;
                        else if (focusTarget is TextAreaElement textArea)
                            textArea.Clipboard = Clipboard;
                    }

                    var downEvt = _pointerState.HandlePointerDown(target, effectiveBtnPos, button.Button, button.Modifiers);
                    EventDispatcher.Dispatch(downEvt, target);

                    // Start pointer capture for text input drag selection
                    if (!isOverlayHit && button.Button == 0 && target is TextInputElement dragInput && dragInput.IsDragging)
                        _textInputDragTarget = dragInput;
                    else if (!isOverlayHit && button.Button == 0 && target is TextAreaElement dragArea && dragArea.IsDragging)
                        _textAreaDragTarget = dragArea;

                    // Record potential drag source if the element (or ancestor) is draggable
                    if (!isOverlayHit && button.Button == 0 && _textInputDragTarget is null && _textAreaDragTarget is null)
                    {
                        var draggable = FindDraggableAncestor(target);
                        if (draggable is not null)
                        {
                            _potentialDragSource = draggable;
                            _dragStartPos = pos;
                        }
                    }

                    // Right-click: show context menu
                    if (!isOverlayHit && button.Button == 2)
                        TryShowContextMenu(target, pos);
                }
                else
                {
                    var (upEvt, clickEvt, dblClickEvt) = _pointerState.HandlePointerUp(target, effectiveBtnPos, button.Button, button.Modifiers);
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

                // Context menu keyboard navigation
                if (key.IsDown && _contextMenuElement is not null)
                {
                    switch (key.KeyCode)
                    {
                        case KeyCode.Up:
                            _contextMenuElement.MoveSelection(-1);
                            break;
                        case KeyCode.Down:
                            _contextMenuElement.MoveSelection(1);
                            break;
                        case KeyCode.Enter:
                            _contextMenuElement.ActivateSelected();
                            break;
                        case KeyCode.Escape:
                            CloseContextMenu();
                            break;
                        default:
                            goto normalKeyHandling;
                    }
                    break;
                }
                normalKeyHandling:

                // Tab / Shift+Tab focus traversal
                if (key.IsDown && key.KeyCode == KeyCode.Tab)
                {
                    if (key.Modifiers.HasFlag(KeyModifiers.Shift))
                        _focusManager.MoveFocusPrevious(_root);
                    else
                        _focusManager.MoveFocusNext(_root);

                    // Provide clipboard to text inputs when focused via Tab
                    if (_focusManager.FocusedElement is TextInputElement tabFocusedInput)
                        tabFocusedInput.Clipboard = Clipboard;
                    else if (_focusManager.FocusedElement is TextAreaElement tabFocusedArea)
                        tabFocusedArea.Clipboard = Clipboard;
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

            // ──── OS-level drag & drop ────
            case InputEvent.DropBeginInput:
            {
                _isExternalDrag = true;
                _isDragging = true;
                _dragData = new DragData();
                _dragHoveredElement = null;
                _dragAccepted = false;
                break;
            }

            case InputEvent.DropPositionInput dropPos:
            {
                if (!_isExternalDrag) break;
                var pos = new Point(dropPos.X, dropPos.Y);
                UpdateDragHover(pos);
                break;
            }

            case InputEvent.DropFileInput dropFile:
            {
                if (_dragData is not null)
                    _dragData.Files.Add(dropFile.Path);
                break;
            }

            case InputEvent.DropTextInput dropText:
            {
                if (_dragData is not null)
                    _dragData.Text = dropText.Text;
                break;
            }

            case InputEvent.DropCompleteInput:
            {
                EndExternalDrag();
                break;
            }
        }

        // Any input event may have triggered visual changes via handlers,
        // so ensure the render pipeline runs this frame.
        _root.MarkPaintDirty();
    }

    /// <summary>
    /// Walks from the hit target up through ancestors to find the nearest focusable element.
    /// Returns null if no focusable ancestor exists (clicking on non-interactive area clears focus).
    /// </summary>
    private static Element? FindFocusableAncestor(Element? target)
    {
        var current = target;
        while (current is not null)
        {
            if (current.IsFocusable)
                return current;
            current = current.Parent;
        }
        return null;
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

    // ──── Drag ghost rendering ────

    private void RenderDragGhost(IRenderContext ctx)
    {
        if (_dragVisual is not null)
        {
            // Custom visual: resolve styles + layout once, then render at cursor
            if (!_dragVisualLayoutDone)
            {
                StyleResolver.Resolve(_dragVisual, Application.Theme);
                _layoutEngine.PerformLayout(_dragVisual, PlatformWindow.LogicalWidth, PlatformWindow.LogicalHeight, forceRootFill: false);
                _dragVisualLayoutDone = true;
            }

            float offsetX = _lastDragPos.X - _dragVisual.Layout.Width / 2;
            float offsetY = _lastDragPos.Y - _dragVisual.Layout.Height / 2;
            ctx.PushTranslate(offsetX, offsetY);
            ctx.PushOpacity(0.8f);
            ElementRenderer.RenderTree(_dragVisual, ctx);
            ctx.Pop();
            ctx.Pop();
        }
        else if (_dragSource is not null)
        {
            // Default: re-render the source element at cursor position
            float offsetX = _lastDragPos.X - _dragSource.Layout.AbsoluteX - _dragSource.Layout.Width / 2;
            float offsetY = _lastDragPos.Y - _dragSource.Layout.AbsoluteY - _dragSource.Layout.Height / 2;
            ctx.PushTranslate(offsetX, offsetY);
            ctx.PushOpacity(0.7f);
            ElementRenderer.RenderTree(_dragSource, ctx);
            ctx.Pop();
            ctx.Pop();
        }
    }

    // ──── Drag & Drop helpers ────

    private static Element? FindDraggableAncestor(Element? target)
    {
        var current = target;
        while (current is not null)
        {
            if (current.Draggable)
                return current;
            current = current.Parent;
        }
        return null;
    }

    private void StartInternalDrag(Point pos)
    {
        _dragSource = _potentialDragSource!;
        _potentialDragSource = null;
        _isDragging = true;
        _isExternalDrag = false;
        _dragData = new DragData();
        _dragHoveredElement = null;
        _dragAccepted = false;

        var startEvt = new DragStartEvent { Position = pos, DragData = _dragData };
        EventDispatcher.DispatchDirect(startEvt, _dragSource);

        // Capture drag visual (custom or source element as default)
        _dragVisual = startEvt.DragVisual;
        _dragVisualLayoutDone = false;

        PlatformWindow.SetCursor(CursorType.Move);
        UpdateDragHover(pos);
    }

    private void UpdateDragHover(Point pos)
    {
        if (_root is null || _dragData is null) return;
        _lastDragPos = pos;

        var target = HitTester.HitTest(_root, pos);

        // Fire DragLeave / DragEnter when the hovered element changes
        if (target != _dragHoveredElement)
        {
            if (_dragHoveredElement is not null)
                EventDispatcher.Dispatch(new DragLeaveEvent { Position = pos, DragData = _dragData }, _dragHoveredElement);

            _dragHoveredElement = target;
            _dragAccepted = false;

            if (target is not null)
                EventDispatcher.Dispatch(new DragEnterEvent { Position = pos, DragData = _dragData }, target);
        }

        // Fire DragOver (bubbles) so ancestors can accept
        if (target is not null)
        {
            var overEvt = new DragOverEvent { Position = pos, DragData = _dragData };
            EventDispatcher.Dispatch(overEvt, target);
            _dragAccepted = overEvt.Accepted;
        }
        else
        {
            _dragAccepted = false;
        }

        PlatformWindow.SetCursor(_dragAccepted ? CursorType.Move : CursorType.NotAllowed);
    }

    private void EndInternalDrag(Point pos)
    {
        if (_root is null || _dragData is null) { ResetDragState(); return; }

        var target = HitTester.HitTest(_root, pos);

        // Fire final DragOver to confirm acceptance
        bool accepted = false;
        if (target is not null)
        {
            var overEvt = new DragOverEvent { Position = pos, DragData = _dragData };
            EventDispatcher.Dispatch(overEvt, target);
            accepted = overEvt.Accepted;

            if (accepted)
                EventDispatcher.Dispatch(new DropEvent { Position = pos, DragData = _dragData }, target);
        }

        // Fire DragLeave on previously hovered element
        if (_dragHoveredElement is not null)
            EventDispatcher.Dispatch(new DragLeaveEvent { Position = pos, DragData = _dragData }, _dragHoveredElement);

        // Fire DragEnd on the source
        if (_dragSource is not null)
            EventDispatcher.DispatchDirect(new DragEndEvent { Position = pos, DragData = _dragData, WasAccepted = accepted }, _dragSource);

        // Restore cursor
        var cursorTarget = HitTester.HitTest(_root, pos);
        PlatformWindow.SetCursor(cursorTarget?.ResolvedInherited.Cursor ?? CursorType.Default);

        ResetDragState();
    }

    private void EndExternalDrag()
    {
        if (_dragData is not null && _dragHoveredElement is not null && _dragAccepted)
            EventDispatcher.Dispatch(new DropEvent { Position = _lastDragPos, DragData = _dragData }, _dragHoveredElement);

        if (_dragHoveredElement is not null && _dragData is not null)
            EventDispatcher.Dispatch(new DragLeaveEvent { Position = _lastDragPos, DragData = _dragData }, _dragHoveredElement);

        PlatformWindow.SetCursor(CursorType.Default);
        ResetDragState();
    }

    private void ResetDragState()
    {
        _potentialDragSource = null;
        _dragSource = null;
        _isDragging = false;
        _isExternalDrag = false;
        _dragData = null;
        _dragHoveredElement = null;
        _dragVisual = null;
        _dragVisualLayoutDone = false;
        _dragAccepted = false;
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

    public void Close()
    {
        PlatformWindow.Close();
        Closed?.Invoke();
    }

    internal void NotifyClosed() => Close();

    // ──── Overlay system (IOverlayHost) ────

    public OverlayHandle ShowOverlay(Element content, float x, float y, bool dismissOnOutsideClick = true, bool hitTestable = true)
    {
        var id = _nextOverlayId++;
        var entry = new OverlayEntry
        {
            Id = id,
            Content = content,
            X = x,
            Y = y,
            DismissOnOutsideClick = dismissOnOutsideClick,
            IsHitTestable = hitTestable,
        };

        // Set overlay host so elements inside the overlay can show/close overlays
        content.OverlayHost = this;

        // Resolve style and layout immediately so we can clamp to viewport
        var theme = Application.Theme;
        StyleResolver.Resolve(content, theme);
        entry.StyleResolved = true;

        _layoutEngine.PerformLayout(content, PlatformWindow.LogicalWidth, PlatformWindow.LogicalHeight, forceRootFill: false);
        entry.LayoutDone = true;

        // Clamp to viewport so the overlay stays on screen
        float vw = PlatformWindow.LogicalWidth;
        float vh = PlatformWindow.LogicalHeight;
        if (entry.X + content.Layout.Width > vw)
            entry.X = Math.Max(0, vw - content.Layout.Width);
        if (entry.Y + content.Layout.Height > vh)
            entry.Y = Math.Max(0, vh - content.Layout.Height);

        _overlays.Add(entry);
        _root?.MarkPaintDirty();
        return new OverlayHandle { Id = id };
    }

    public void CloseOverlay(OverlayHandle handle)
    {
        for (int i = _overlays.Count - 1; i >= 0; i--)
        {
            if (_overlays[i].Id == handle.Id)
            {
                _overlays.RemoveAt(i);
                _root?.MarkPaintDirty();
                return;
            }
        }
    }

    public void UpdateOverlayPosition(OverlayHandle handle, float x, float y)
    {
        for (int i = 0; i < _overlays.Count; i++)
        {
            if (_overlays[i].Id == handle.Id)
            {
                _overlays[i].X = x;
                _overlays[i].Y = y;
                _root?.MarkPaintDirty();
                return;
            }
        }
    }

    private void RenderOverlays(IRenderContext ctx)
    {
        var theme = Application.Theme;
        foreach (var overlay in _overlays)
        {
            var content = overlay.Content;

            if (!overlay.StyleResolved || content.DirtyFlags.HasFlag(DirtyFlags.Style))
            {
                StyleResolver.Resolve(content, theme);
                overlay.StyleResolved = true;
            }

            if (!overlay.LayoutDone || content.DirtyFlags.HasFlag(DirtyFlags.Layout))
            {
                _layoutEngine.PerformLayout(content,
                    PlatformWindow.LogicalWidth, PlatformWindow.LogicalHeight, forceRootFill: false);
                overlay.LayoutDone = true;
            }

            ctx.PushTranslate(overlay.X, overlay.Y);
            ElementRenderer.RenderTree(content, ctx);
            ctx.Pop();
        }
    }

    private (Element? element, Point offset) HitTestOverlays(Point pos)
    {
        // Test back-to-front: last overlay = topmost, checked first
        for (int i = _overlays.Count - 1; i >= 0; i--)
        {
            var overlay = _overlays[i];
            if (!overlay.IsHitTestable) continue;
            var localPoint = new Point(pos.X - overlay.X, pos.Y - overlay.Y);
            var hit = HitTester.HitTest(overlay.Content, localPoint);
            if (hit is not null)
                return (hit, new Point(overlay.X, overlay.Y));
        }
        return (null, default);
    }

    private void DismissOverlaysOnOutsideClick()
    {
        for (int i = _overlays.Count - 1; i >= 0; i--)
        {
            if (_overlays[i].DismissOnOutsideClick)
            {
                // Clear context menu state if this overlay is the context menu
                if (_overlays[i].Id == _contextMenuOverlay.Id)
                {
                    _contextMenuOverlay = default;
                    _contextMenuElement = null;
                }
                _overlays.RemoveAt(i);
                _root?.MarkPaintDirty();
            }
        }
    }

    // ──── Tooltip ────

    private void UpdateTooltipHover(Element? hitTarget)
    {
        var (owner, text) = FindTooltipOwner(hitTarget);

        // Same owner — nothing to do
        if (owner == _tooltipOwner) return;

        // Owner changed — hide current tooltip/cancel pending timer
        HideTooltip();

        if (owner is null || text is null) return;

        // Start show delay
        _tooltipOwner = owner;
        var theme = Application.Theme;
        float delay = theme.Tooltip.ShowDelay;
        _tooltipDelayTween = Animate.Value(0f, 1f, _ => { })
            .Duration(delay)
            .OnComplete(() => ShowTooltipOverlay(owner, text))
            .Start();
    }

    private void ShowTooltipOverlay(Element owner, string text)
    {
        _tooltipDelayTween = null;

        // Owner may have been removed from tree during the delay
        if (owner.OverlayHost is null) return;

        var theme = Application.Theme;
        var tt = theme.Tooltip;

        var tooltip = BoxElement.New()
            .Background(tt.Background)
            .CornerRadius(tt.CornerRadius)
            .Padding(tt.Padding)
            .Children(
                TextElement.New(text)
                    .FontSize(tt.FontSize)
                    .TextColor(tt.TextColor)
            );

        // Position below the owner element, centered horizontally
        float x = owner.Layout.AbsoluteContentBox.X;
        float y = owner.Layout.AbsoluteContentBox.Y + owner.Layout.Height + tt.Gap;

        _tooltipOverlay = ShowOverlay(tooltip, x, y, dismissOnOutsideClick: false, hitTestable: false);
    }

    private void HideTooltip()
    {
        if (_tooltipDelayTween is { } tween)
        {
            tween.Cancel();
            _tooltipDelayTween = null;
        }

        if (_tooltipOverlay.IsValid)
        {
            CloseOverlay(_tooltipOverlay);
            _tooltipOverlay = default;
        }

        _tooltipOwner = null;
    }

    private static (Element? owner, string? text) FindTooltipOwner(Element? element)
    {
        var current = element;
        while (current is not null)
        {
            if (current.TooltipText is not null)
                return (current, current.TooltipText);
            current = current.Parent;
        }
        return (null, null);
    }

    // ──── Context Menu ────

    private void TryShowContextMenu(Element target, Point pos)
    {
        var (owner, itemsProvider) = FindContextMenuOwner(target);
        if (owner is null || itemsProvider is null) return;

        var items = itemsProvider();
        if (items.Count == 0) return;

        // Close any existing context menu first
        CloseContextMenu();

        var theme = Application.Theme;
        var menuElement = new ContextMenuElement(items, theme.ContextMenu, CloseContextMenu);
        _contextMenuElement = menuElement;
        _contextMenuOverlay = ShowOverlay(menuElement, pos.X, pos.Y, dismissOnOutsideClick: true, hitTestable: true);
    }

    private void CloseContextMenu()
    {
        if (_contextMenuOverlay.IsValid)
        {
            CloseOverlay(_contextMenuOverlay);
            _contextMenuOverlay = default;
        }
        _contextMenuElement = null;
    }

    private static (Element? owner, Func<IReadOnlyList<ContextMenuItem>>? itemsProvider) FindContextMenuOwner(Element? element)
    {
        var current = element;
        while (current is not null)
        {
            if (current.ContextMenuItems is not null)
                return (current, current.ContextMenuItems);
            current = current.Parent;
        }
        return (null, null);
    }

    // ──── Window chrome hit test ────

    private int HandleWindowHitTest(int x, int y)
    {
        int w = PlatformWindow.LogicalWidth;
        int h = PlatformWindow.LogicalHeight;
        int border = ResizeBorderWidth;

        // Don't show resize handles when maximized
        if (!IsMaximized)
        {
            bool left = x < border;
            bool right = x >= w - border;
            bool top = y < border;
            bool bottom = y >= h - border;

            if (top && left) return 2;     // SDL_HITTEST_RESIZE_TOPLEFT
            if (top && right) return 4;    // SDL_HITTEST_RESIZE_TOPRIGHT
            if (bottom && left) return 8;  // SDL_HITTEST_RESIZE_BOTTOMLEFT
            if (bottom && right) return 6; // SDL_HITTEST_RESIZE_BOTTOMRIGHT
            if (top) return 3;             // SDL_HITTEST_RESIZE_TOP
            if (bottom) return 7;          // SDL_HITTEST_RESIZE_BOTTOM
            if (left) return 9;            // SDL_HITTEST_RESIZE_LEFT
            if (right) return 5;           // SDL_HITTEST_RESIZE_RIGHT
        }

        // Check element tree for drag regions.
        // If the hit element itself has event handlers (e.g. a button inside the title bar),
        // return NORMAL so the click reaches our event system instead of starting a window drag.
        if (_root is not null)
        {
            var hit = HitTester.HitTest(_root, new Point(x, y));
            if (hit is not null && !hit.HasAnyEventHandlers && HasWindowDragAncestor(hit))
                return 1; // SDL_HITTEST_DRAGGABLE
        }

        return 0; // SDL_HITTEST_NORMAL
    }

    private static bool HasWindowDragAncestor(Element element)
    {
        // Walk from the hit element up toward the root. If we find an element with
        // event handlers before finding a drag region, the click belongs to that
        // interactive element (e.g. a button inside the title bar), not the drag region.
        Element? current = element;
        while (current is not null)
        {
            if (current.IsWindowDragRegion)
                return true;
            if (current.HasAnyEventHandlers)
                return false;
            current = current.Parent;
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PlatformWindow.EventTranslator.OnInputEvent -= HandleInputEvent;
        PlatformWindow.Resized -= OnWindowResized;
        PlatformWindow.Dispose();
    }
}

using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI;

// Touch text editing (docs/design/SOFT-KEYBOARD.md §4), for the three controls that implement
// ITouchTextEditing: a tap shows the insertion handle under the caret for a few seconds, a long
// press or a double tap selects a word and shows two handles and the edit bar, a handle's drag
// moves its end, and a tap elsewhere, typing, the focus leaving or the mouse put them away. The
// handles are drawn by the window above everything; the bar is an overlay above the selection.
public sealed partial class Window
{
    private enum TouchHandle { None, Insertion, Start, End }

    /// <summary>The handles' radius: 22 layout units across, what a finger can take.</summary>
    internal const float TouchHandleRadius = 11f;

    /// <summary>How far from a handle's middle a finger still takes it.</summary>
    private const float TouchHandleReach = 24f;

    /// <summary>How long the insertion handle shows after a tap, as on Android.</summary>
    private const float InsertionHandleSeconds = 4f;

    /// <summary>The field showing handles, or null.</summary>
    private Element? _touchText;

    /// <summary>Two handles (a selection) rather than the insertion handle.</summary>
    private bool _touchTextSelects;

    private TouchHandle _draggedHandle;
    private long _handleFinger = NoFinger;

    /// <summary>From the finger to the dragged caret's middle, kept for the drag: the finger holds
    /// the handle below the line, and the text it moves to is the line's.</summary>
    private Point _handleGrab;
    private Point _handleDownAt;
    private bool _handleMoved;

    private OverlayHandle? _editBar;
    private TweenHandle? _insertionHandleTimer;

    /// <summary>A finger landed off the handles and the bar: the handles go if it is a tap, and
    /// stay, following the text, if it scrolls (the bar comes back when it ends).</summary>
    private bool _touchTextPendingHide;

    /// <summary>A finger pressed nothing focusable: the focus is cleared if it lifts as a tap, as a
    /// click there clears it, and kept if it scrolls, so a field keeps its keyboard and its
    /// selection while the page under it moves.</summary>
    private bool _touchTapClearsFocus;

    /// <summary>The handles shown, for tests: none, the insertion handle, or a selection's two.</summary>
    internal int TouchHandleCount => _touchText is null ? 0 : _touchTextSelects ? 2 : 1;

    /// <summary>The handles drawn now, in the window, for tests: the start's then the end's, or
    /// the insertion handle's.</summary>
    internal List<Rect> TouchHandleBoxes()
    {
        var boxes = new List<Rect>();
        foreach (var (handle, caret) in ShownHandles()) boxes.Add(HandleBox(handle, caret));
        return boxes;
    }

    /// <summary>The edit bar's content while it is open, for tests.</summary>
    internal Element? TouchEditBar
    {
        get
        {
            if (_editBar is not { } bar) return null;
            for (int i = 0; i < _overlays.Count; i++)
                if (_overlays[i].Id == bar.Id) return _overlays[i].Content;
            return null;
        }
    }

    /// <summary>The text control <paramref name="element"/> is, or is inside: what a finger on it edits.</summary>
    private static Element? TouchTextOwnerOf(Element? element)
    {
        for (var e = element; e is not null; e = e.Parent)
            if (e is ITouchTextEditing) return e.IsEnabledForAccessibility ? e : null;
        return null;
    }

    private static ITouchTextEditing? TouchTextOf(Element? element) => TouchTextOwnerOf(element) as ITouchTextEditing;

    /// <summary>Where <paramref name="element"/>'s layout boxes are drawn in the window: its
    /// ancestors' scroll, sticky shift and overlay place.</summary>
    private static Point LayoutToWindow(Element element)
    {
        var drawn = element.WindowBorderBox;
        var laid = element.Layout.AbsoluteBorderBox;
        return new Point(drawn.X - laid.X, drawn.Y - laid.Y);
    }

    private static Rect Offset(Rect r, Point by) => new(r.X + by.X, r.Y + by.Y, r.Width, r.Height);

    /// <summary>The caret boxes of the field's selection ends, in the window, or null when the
    /// field cannot say yet.</summary>
    private (Rect Start, Rect End)? TouchCaretsInWindow()
    {
        if (_touchText is not { } owner || TouchTextOf(owner) is not { } text) return null;
        if (text.TouchCaretBoxes() is not var (start, end)) return null;
        var by = LayoutToWindow(owner);
        return (Offset(start, by), Offset(end, by));
    }

    /// <summary>A handle's box: under its caret, the start's to the caret's left with its square
    /// corner on the caret, the end's to the right, the insertion handle centred below.</summary>
    private static Rect HandleBox(TouchHandle handle, Rect caret)
    {
        const float d = TouchHandleRadius * 2;
        return handle switch
        {
            TouchHandle.Start => new Rect(caret.X - d, caret.Bottom, d, d),
            TouchHandle.End => new Rect(caret.X + caret.Width, caret.Bottom, d, d),
            _ => new Rect(caret.X + caret.Width / 2 - TouchHandleRadius, caret.Bottom + TouchHandleRadius * 0.5f, d, d),
        };
    }

    /// <summary>Whether a caret is where the field shows text: inside its content and every
    /// scroll view's around it. A handle whose caret scrolled away is not drawn or taken.</summary>
    private bool CaretShown(Rect caret)
    {
        float x = caret.X, y = caret.Y + caret.Height / 2;
        for (var e = _touchText; e is not null; e = e.Parent)
        {
            if (!ReferenceEquals(e, _touchText) && e is not ScrollViewElement) continue;
            var box = e.WindowContentBox;
            if (x < box.X - 1 || x > box.Right + 1 || y < box.Y || y > box.Bottom) return false;
        }
        return true;
    }

    private IEnumerable<(TouchHandle Handle, Rect Caret)> ShownHandles()
    {
        if (TouchCaretsInWindow() is not var (start, end)) yield break;
        if (_touchTextSelects)
        {
            if (CaretShown(start)) yield return (TouchHandle.Start, start);
            if (CaretShown(end)) yield return (TouchHandle.End, end);
        }
        else if (CaretShown(end))
            yield return (TouchHandle.Insertion, end);
    }

    // ──── Showing and hiding ────

    /// <summary>After a tap on a field: the insertion handle for a few seconds, or, where the tap
    /// selected (the second of a double tap), the selection's handles and the bar.</summary>
    private void ShowTouchTextAfterTap(Element field)
    {
        if (TouchTextOf(field) is not { } text) return;
        if (text.HasTouchSelection) ShowTouchSelection(field);
        else ShowInsertionHandle(field);
    }

    private void ShowInsertionHandle(Element field)
    {
        CloseEditBar();
        _touchText = field;
        _touchTextSelects = false;
        RestartInsertionHandleTimer();
        _root?.MarkPaintDirty();
    }

    private void RestartInsertionHandleTimer()
    {
        _insertionHandleTimer?.Cancel();
        _insertionHandleTimer = Animate.After(InsertionHandleSeconds, () =>
        {
            _insertionHandleTimer = null;
            if (_touchText is not null && !_touchTextSelects && _handleFinger == NoFinger && _editBar is null)
                HideTouchText();
        });
    }

    /// <summary>The selection's two handles and the edit bar; with nothing selected (an empty
    /// field), the insertion handle and the bar, for Paste.</summary>
    private void ShowTouchSelection(Element field)
    {
        if (TouchTextOf(field) is not { } text) return;
        _insertionHandleTimer?.Cancel();
        _insertionHandleTimer = null;
        _touchText = field;
        _touchTextSelects = text.HasTouchSelection;
        OpenEditBar();
        _root?.MarkPaintDirty();
    }

    private void HideTouchText()
    {
        if (_touchText is null && _editBar is null) return;
        _insertionHandleTimer?.Cancel();
        _insertionHandleTimer = null;
        if (_handleFinger != NoFinger) TouchTextOf(_touchText)?.TouchEndHandleDrag();
        _handleFinger = NoFinger;
        _draggedHandle = TouchHandle.None;
        _touchText = null;
        CloseEditBar();
        _root?.MarkPaintDirty();
    }

    /// <summary>What puts the handles away besides a touch: a key (typing, or the keyboard's own
    /// keys), the mouse, the wheel. Called for each input before it is handled.</summary>
    private void HideTouchTextFor(InputEvent evt)
    {
        switch (evt)
        {
            case InputEvent.KeyInput { IsDown: true }:
            case InputEvent.TextInputInput:
            case InputEvent.MouseWheelInput:
            case InputEvent.MouseButtonInput { IsDown: true } when !PointerContext.IsTouch:
                HideTouchText();
                break;
        }
    }

    /// <summary>Each frame before drawing: the handles go when their field went, lost the focus,
    /// or lost the selection they were showing (typed over, cut).</summary>
    private void CheckTouchText()
    {
        if (_touchText is not { } owner) return;
        if (!IsShownInWindow(owner) || !ReferenceEquals(_focusManager.FocusedElement, owner)
            || TouchTextOf(owner) is not { } text || (_touchTextSelects && !text.HasTouchSelection))
            HideTouchText();
    }

    // ──── The edit bar ────

    private void OpenEditBar()
    {
        CloseEditBar();
        if (_touchText is not { } owner || TouchTextOf(owner) is not { } text) return;
        var theme = Application.Theme.ContextMenu;
        var bar = Flex.Row().Padding(new Thickness(2, 4)).Background(theme.Background)
            .Border(new Thickness(theme.BorderWidth), theme.BorderColor).CornerRadius(theme.CornerRadius + 2)
            .AccessibleRole(AccessibilityRole.Group);
        var menu = text.TouchEditItems();
        var buttons = new List<Element>();
        // Android shows what can be done now: a disabled Paste is left out, not greyed.
        foreach (var item in TouchEdit.BarItems(menu))
        {
            var action = item.Action!;
            buttons.Add(EditBarButton(item.Label, theme, () => RunEditBarItem(action)));
        }
        // The rest of the field's menu (a link's items, a table's), which a long press no longer opens.
        if (TouchEdit.HasMore(menu))
            buttons.Add(EditBarButton("⋯", theme, OpenTouchTextMenu, accessibleName: "More"));
        if (buttons.Count == 0) return;
        bar.Children(buttons.ToArray());
        _editBar = ShowAnchoredOverlay(bar, owner, PlaceEditBar, dismissOnOutsideClick: false,
            onDismissed: () => _editBar = null);
    }

    /// <summary>One item of the bar: no focus of its own, so the field keeps the keyboard and its
    /// selection while a finger presses it.</summary>
    private static BoxElement EditBarButton(string label, ContextMenuTheme theme, Action run, string? accessibleName = null)
    {
        var button = Box.New().Padding(new Thickness(10, 12)).CornerRadius(theme.CornerRadius)
            .AccessibleRole(AccessibilityRole.Button).AccessibleName(accessibleName ?? label)
            .Children(TextB.New(label).FontSize(theme.FontSize + 1).TextColor(theme.TextColor).MaxLines(1));
        button.OnClick(_ => run());
        return button;
    }

    /// <summary>The bar's "more": the field's whole context menu, at the selection.</summary>
    private void OpenTouchTextMenu()
    {
        if (_touchText is not { } owner || TouchCaretsInWindow() is not var (_, end)) return;
        HideTouchText();
        TryShowContextMenu(owner, new Point(end.X, end.Bottom));
    }

    private void CloseEditBar()
    {
        if (_editBar is not { } bar) return;
        _editBar = null;
        CloseOverlay(bar);
    }

    /// <summary>Above the selection, centred on it, or below its handles where there is no room
    /// above; inside the window either way.</summary>
    private Point PlaceEditBar(Size bar, Size window)
    {
        if (TouchCaretsInWindow() is not var (start, end)) return new Point(-10000, -10000);
        float top = Math.Min(start.Y, end.Y), bottom = Math.Max(start.Bottom, end.Bottom);
        float middle = start.Y == end.Y ? (start.X + end.Right) / 2 : (top == start.Y ? start.X : end.X);
        float x = Math.Clamp(middle - bar.Width / 2, 4, Math.Max(4, window.Width - bar.Width - 4));
        float y = top - bar.Height - 8;
        if (y < SafeArea.Top + 4) y = bottom + TouchHandleRadius * 2 + 8;
        return new Point(x, y);
    }

    /// <summary>An item of the bar: its action, then the handles for what is left. A Select all
    /// shows the new selection's handles and the bar again; a Copy leaves the selection and puts
    /// them away, as do a Cut and a Paste, which leave none.</summary>
    private void RunEditBarItem(Action action)
    {
        var owner = _touchText;
        var before = TouchCaretsInWindow();
        action();
        if (owner is null || TouchTextOf(owner) is not { } text) { HideTouchText(); return; }
        // The action's change is laid out in the next frame; the ends are read from the control.
        var after = TouchCaretsInWindow();
        if (text.HasTouchSelection && !Equals(before, after)) ShowTouchSelection(owner);
        else HideTouchText();
    }

    // ──── Fingers on the handles ────

    /// <summary>A primary finger's down: on a handle, it takes the handle (true); anywhere but the
    /// bar, the handles and the bar go.</summary>
    private bool TryTakeTouchHandle(long finger, Point at)
    {
        if (_touchText is null) return false;
        foreach (var (handle, caret) in ShownHandles())
        {
            var box = HandleBox(handle, caret);
            float cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2;
            float dx = at.X - cx, dy = at.Y - cy;
            if (dx * dx + dy * dy > TouchHandleReach * TouchHandleReach) continue;
            if (TouchTextOf(_touchText) is not { } text) break;
            _handleFinger = finger;
            _draggedHandle = handle;
            _handleDownAt = at;
            _handleMoved = false;
            _handleGrab = new Point(caret.X + caret.Width / 2 - at.X, caret.Y + caret.Height / 2 - at.Y);
            _insertionHandleTimer?.Cancel();
            _insertionHandleTimer = null;
            CloseEditBar();
            text.TouchBeginHandleDrag(movesEnd: handle != TouchHandle.Start);
            _root?.MarkPaintDirty();
            return true;
        }
        if (!OnEditBar(at))
        {
            CloseEditBar();
            _touchTextPendingHide = true;
        }
        return false;
    }

    /// <summary>The primary finger lifted after a scroll or a long press that took it. After a
    /// scroll the handles stay where the text went, and the bar comes back for a selection.</summary>
    private void AfterTouchGesture(bool longPressed)
    {
        _touchTapClearsFocus = false;   // a scroll or a long press is no tap on empty space
        if (!_touchTextPendingHide) return;
        _touchTextPendingHide = false;
        if (longPressed || _touchText is null) return;   // the long press decided for itself
        if (_touchTextSelects) OpenEditBar();
        else RestartInsertionHandleTimer();
    }

    /// <summary>The primary finger lifted as a tap, or was cancelled: the old handles go, and a
    /// tap on a focused field shows its own.</summary>
    private void AfterTouchTap(Element? pressed, bool cancel, bool tap)
    {
        if (_touchTapClearsFocus)
        {
            _touchTapClearsFocus = false;
            if (tap && !cancel) _focusManager.SetFocus(null, FocusReason.Pointer);
        }
        if (_touchTextPendingHide)
        {
            _touchTextPendingHide = false;
            HideTouchText();
        }
        if (!cancel && tap && TouchTextOwnerOf(pressed) is { } field && ReferenceEquals(_focusManager.FocusedElement, field))
            ShowTouchTextAfterTap(field);
    }

    /// <summary>A long press on text (SOFT-KEYBOARD.md §4): the word under the finger, its handles
    /// and the edit bar, in place of the context menu. False where the finger is on no text.</summary>
    private bool SelectWordByTouch(Element target, Point at)
    {
        _touchTextPendingHide = false;
        if (TouchTextOwnerOf(target) is not { } field || TouchTextOf(field) is not { } text)
        {
            HideTouchText();
            return false;
        }
        if (!ReferenceEquals(_focusManager.FocusedElement, field)) _focusManager.SetFocus(field, FocusReason.Pointer);
        var by = LayoutToWindow(field);
        text.TouchSelectWordAt(new Point(at.X - by.X, at.Y - by.Y));
        ShowTouchSelection(field);
        return true;
    }

    private bool OnEditBar(Point at)
    {
        if (_editBar is null || TouchEditBar is not { } bar) return false;
        var (hit, _) = HitTestOverlays(at);
        for (var e = hit; e is not null; e = e.Parent)
            if (ReferenceEquals(e, bar)) return true;
        return false;
    }

    private void DragTouchHandle(Point at)
    {
        if (_touchText is not { } owner || TouchTextOf(owner) is not { } text) return;
        if (!_handleMoved && Distance(at, _handleDownAt) <= PointerState.TouchSlop) return;
        _handleMoved = true;
        // The point the caret goes to, in the space the field is laid out in.
        var by = LayoutToWindow(owner);
        text.TouchDragHandleTo(new Point(at.X + _handleGrab.X - by.X, at.Y + _handleGrab.Y - by.Y));
        _root?.MarkPaintDirty();
    }

    private void ReleaseTouchHandle(bool cancel)
    {
        var owner = _touchText;
        var dragged = _draggedHandle;
        bool moved = _handleMoved;
        _handleFinger = NoFinger;
        _draggedHandle = TouchHandle.None;
        if (owner is null || TouchTextOf(owner) is not { } text) return;
        text.TouchEndHandleDrag();
        if (cancel) { HideTouchText(); return; }
        // A tap on the insertion handle offers Paste and Select all; a drag of it shows it again
        // for a while. A selection's handle let go brings the bar back.
        if (dragged == TouchHandle.Insertion && moved) ShowInsertionHandle(owner);
        else ShowTouchSelection(owner);
    }

    // ──── Drawing ────

    /// <summary>After the overlays, so a handle is never under a menu's edge or a dialog's
    /// panel it belongs to: the handles of the field being edited, in the theme's primary.</summary>
    private void RenderTouchHandles(IRenderContext ctx)
    {
        if (_touchText is null) return;
        var color = Application.Theme.Primary;
        const float r = TouchHandleRadius;
        foreach (var (handle, caret) in ShownHandles())
        {
            var box = HandleBox(handle, caret);
            var corners = handle switch
            {
                TouchHandle.Start => new CornerRadius(r, 0, r, r),
                TouchHandle.End => new CornerRadius(0, r, r, r),
                _ => new CornerRadius(r),
            };
            if (handle == TouchHandle.Insertion)   // a stem from the caret to the knob
                ctx.DrawRect(new Rect(caret.X + caret.Width / 2 - 1, caret.Bottom, 2, box.Y - caret.Bottom + r), color);
            ctx.DrawRoundedRect(box, corners, color, Color.Transparent, default);
        }
    }
}

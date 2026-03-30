using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public class TextInputElement : BoxElement
{
    private string _text = "";
    private int _cursorPos;
    private int _selectionStart;
    private bool _focused;
    private bool _isDragging;
    private float _scrollOffset; // horizontal scroll for text wider than the box
    private IRenderContext? _renderCtx; // cached for hit-testing with real font metrics

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? "";
            _cursorPos = Math.Min(_cursorPos, _text.Length);
            _selectionStart = _cursorPos;
            MarkPaintDirty();
        }
    }

    public int CursorPosition
    {
        get => _cursorPos;
        set
        {
            _cursorPos = Math.Clamp(value, 0, _text.Length);
            _selectionStart = _cursorPos;
        }
    }

    public int SelectionStart => Math.Min(_selectionStart, _cursorPos);
    public int SelectionEnd => Math.Max(_selectionStart, _cursorPos);
    public bool HasSelection => _selectionStart != _cursorPos;
    public string SelectedText => HasSelection ? _text[SelectionStart..SelectionEnd] : "";

    public string Placeholder { get; set; } = "";
    public Color PlaceholderColor { get; set; } = new(150, 150, 150, 255);
    public Color SelectionColor { get; set; } = new(66, 135, 245, 80);
    public Color CursorColor { get; set; } = new(0, 0, 0, 255);
    public Color FocusBorderColor { get; set; } = new(66, 135, 245, 255);
    public float CursorWidth { get; set; } = 1.5f;

    public Action<string>? TextChanged { get; set; }

    /// <summary>
    /// Clipboard reference — set by Application when the element is focused.
    /// </summary>
    public IClipboard? Clipboard { get; set; }

    protected internal override void ApplyTheme(Theme theme)
    {
        var ti = theme.TextInput;
        CornerRadius = ti.CornerRadius;
        Box.Padding = ti.Padding;
        Box.BorderWidth = ti.BorderWidth;
        Inherited.FontSize = ti.FontSize;
        BackgroundColor = ti.Background ?? theme.Background;
        BorderColor = ti.BorderColor ?? theme.Border;
        FocusBorderColor = ti.FocusBorderColor ?? theme.Primary;
        PlaceholderColor = ti.PlaceholderColor ?? theme.Secondary;
        SelectionColor = ti.SelectionColor ?? new Color(theme.Primary.R, theme.Primary.G, theme.Primary.B, 80);
        CursorColor = ti.CursorColor ?? theme.TextColor;
    }

    public TextInputElement()
    {
        IsFocusable = true;
        Box.Padding = new Thickness(4, 8, 4, 8);
        Inherited.FontSize = 14f;
        Inherited.Cursor = CursorType.Text;

        On<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        On<BlurEvent>(_ => { _focused = false; MarkPaintDirty(); });
        On<TextInputEvent>(OnTextInput);
        On<KeyDownEvent>(OnKeyDown);
        On<PointerDownEvent>(OnPointerDown);
        On<PointerMoveEvent>(OnPointerMove);
        On<PointerUpEvent>(OnPointerUp);
        On<ClickEvent>(OnClick);

        ContextMenuItems = BuildContextMenuItems;
    }

    private IReadOnlyList<ContextMenuItem> BuildContextMenuItems()
    {
        bool hasSelection = HasSelection;
        bool hasClipboard = Clipboard?.GetText() is not null;

        return
        [
            new ContextMenuItem { Label = "Cut", ShortcutHint = "Ctrl+X", IsDisabled = !hasSelection,
                Action = () => { if (HasSelection && Clipboard is not null) { Clipboard.SetText(SelectedText); DeleteSelection(); NotifyChanged(); MarkPaintDirty(); } } },
            new ContextMenuItem { Label = "Copy", ShortcutHint = "Ctrl+C", IsDisabled = !hasSelection,
                Action = () => { if (HasSelection && Clipboard is not null) Clipboard.SetText(SelectedText); } },
            new ContextMenuItem { Label = "Paste", ShortcutHint = "Ctrl+V", IsDisabled = !hasClipboard,
                Action = () => { if (Clipboard?.GetText() is { } t) { DeleteSelection(); _text = _text.Insert(_cursorPos, t); _cursorPos += t.Length; _selectionStart = _cursorPos; NotifyChanged(); MarkPaintDirty(); } } },
            ContextMenuItem.Separator(),
            new ContextMenuItem { Label = "Select All", ShortcutHint = "Ctrl+A", IsDisabled = _text.Length == 0,
                Action = () => { _selectionStart = 0; _cursorPos = _text.Length; MarkPaintDirty(); } },
        ];
    }

    /// <summary>True while the user is dragging to select text. Used by Application for pointer capture.</summary>
    public bool IsDragging => _isDragging;

    public static TextInputElement New(string text = "") => new() { _text = text, _cursorPos = text.Length, _selectionStart = text.Length };

    private void OnTextInput(TextInputEvent evt)
    {
        if (!_focused) return;
        DeleteSelection();
        _text = _text.Insert(_cursorPos, evt.Text);
        _cursorPos += evt.Text.Length;
        _selectionStart = _cursorPos;
        NotifyChanged();
        MarkPaintDirty();
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (!_focused) return;

        bool shift = evt.Modifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = evt.Modifiers.HasFlag(KeyModifiers.Ctrl);

        switch (evt.Key)
        {
            case KeyCode.Left:
                if (ctrl)
                    MoveCursorWordLeft(shift);
                else
                    MoveCursor(-1, shift);
                break;

            case KeyCode.Right:
                if (ctrl)
                    MoveCursorWordRight(shift);
                else
                    MoveCursor(1, shift);
                break;

            case KeyCode.Home:
                MoveCursorTo(0, shift);
                break;

            case KeyCode.End:
                MoveCursorTo(_text.Length, shift);
                break;

            case KeyCode.Backspace:
                if (HasSelection)
                    DeleteSelection();
                else if (_cursorPos > 0)
                {
                    if (ctrl)
                    {
                        int target = FindWordBoundaryLeft(_cursorPos);
                        _text = _text.Remove(target, _cursorPos - target);
                        _cursorPos = target;
                    }
                    else
                    {
                        _text = _text.Remove(_cursorPos - 1, 1);
                        _cursorPos--;
                    }
                    _selectionStart = _cursorPos;
                }
                NotifyChanged();
                break;

            case KeyCode.Delete:
                if (HasSelection)
                    DeleteSelection();
                else if (_cursorPos < _text.Length)
                {
                    if (ctrl)
                    {
                        int target = FindWordBoundaryRight(_cursorPos);
                        _text = _text.Remove(_cursorPos, target - _cursorPos);
                    }
                    else
                    {
                        _text = _text.Remove(_cursorPos, 1);
                    }
                }
                NotifyChanged();
                break;

            case KeyCode.A when ctrl:
                _selectionStart = 0;
                _cursorPos = _text.Length;
                break;

            case KeyCode.C when ctrl:
                if (HasSelection && Clipboard is not null)
                    Clipboard.SetText(SelectedText);
                break;

            case KeyCode.X when ctrl:
                if (HasSelection && Clipboard is not null)
                {
                    Clipboard.SetText(SelectedText);
                    DeleteSelection();
                    NotifyChanged();
                }
                break;

            case KeyCode.V when ctrl:
                if (Clipboard is not null)
                {
                    var clipText = Clipboard.GetText();
                    if (clipText is not null)
                    {
                        DeleteSelection();
                        _text = _text.Insert(_cursorPos, clipText);
                        _cursorPos += clipText.Length;
                        _selectionStart = _cursorPos;
                        NotifyChanged();
                    }
                }
                break;

            default:
                return; // Don't mark paint dirty for unhandled keys
        }

        MarkPaintDirty();
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.Button != 0) return;

        int pos = PositionFromPoint(evt.Position);

        bool shift = evt.Modifiers.HasFlag(KeyModifiers.Shift);
        _cursorPos = pos;
        if (!shift)
            _selectionStart = pos;

        _isDragging = true;
        MarkPaintDirty();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (!_isDragging) return;

        _cursorPos = PositionFromPoint(evt.Position);
        MarkPaintDirty();
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        _isDragging = false;
    }

    private void OnClick(ClickEvent evt)
    {
        if (evt.ClickCount == 2)
        {
            // Double-click: select word under cursor
            int pos = PositionFromPoint(evt.Position);
            _selectionStart = FindWordBoundaryLeft(pos);
            // FindWordBoundaryLeft skips back past the word, but if pos is mid-word
            // we want the start of the current word
            int wordStart = _selectionStart;
            // Walk left from pos to find start of current word
            int ws = pos;
            while (ws > 0 && !char.IsWhiteSpace(_text[ws - 1])) ws--;
            _selectionStart = ws;
            // Walk right from pos to find end of current word
            int we = pos;
            while (we < _text.Length && !char.IsWhiteSpace(_text[we])) we++;
            _cursorPos = we;
            MarkPaintDirty();
        }
        else if (evt.ClickCount >= 3)
        {
            // Triple-click: select all
            _selectionStart = 0;
            _cursorPos = _text.Length;
            MarkPaintDirty();
        }
    }

    /// <summary>
    /// Called by Application to update drag selection when pointer is outside this element's bounds.
    /// </summary>
    public void UpdateDragPosition(Point position)
    {
        if (!_isDragging) return;
        _cursorPos = PositionFromPoint(position);
        MarkPaintDirty();
    }

    /// <summary>
    /// Called by Application when pointer is released during a drag capture.
    /// </summary>
    public void EndDrag()
    {
        _isDragging = false;
    }

    private int PositionFromPoint(Point screenPos)
    {
        var contentBox = Layout.AbsoluteContentBox;
        float clickX = screenPos.X - contentBox.X + _scrollOffset;
        return HitTestTextPosition(clickX);
    }

    private void MoveCursor(int delta, bool extendSelection)
    {
        if (!extendSelection && HasSelection)
        {
            // Collapse selection in the direction of movement
            _cursorPos = delta < 0 ? SelectionStart : SelectionEnd;
            _selectionStart = _cursorPos;
            return;
        }

        _cursorPos = Math.Clamp(_cursorPos + delta, 0, _text.Length);
        if (!extendSelection)
            _selectionStart = _cursorPos;
    }

    private void MoveCursorTo(int pos, bool extendSelection)
    {
        _cursorPos = Math.Clamp(pos, 0, _text.Length);
        if (!extendSelection)
            _selectionStart = _cursorPos;
    }

    private void MoveCursorWordLeft(bool extendSelection)
    {
        MoveCursorTo(FindWordBoundaryLeft(_cursorPos), extendSelection);
    }

    private void MoveCursorWordRight(bool extendSelection)
    {
        MoveCursorTo(FindWordBoundaryRight(_cursorPos), extendSelection);
    }

    private int FindWordBoundaryLeft(int from)
    {
        if (from <= 0) return 0;
        int i = from - 1;
        while (i > 0 && char.IsWhiteSpace(_text[i])) i--;
        while (i > 0 && !char.IsWhiteSpace(_text[i - 1])) i--;
        return i;
    }

    private int FindWordBoundaryRight(int from)
    {
        if (from >= _text.Length) return _text.Length;
        int i = from;
        while (i < _text.Length && !char.IsWhiteSpace(_text[i])) i++;
        while (i < _text.Length && char.IsWhiteSpace(_text[i])) i++;
        return i;
    }

    private void DeleteSelection()
    {
        if (!HasSelection) return;
        int start = SelectionStart;
        int end = SelectionEnd;
        _text = _text.Remove(start, end - start);
        _cursorPos = start;
        _selectionStart = start;
    }

    private void NotifyChanged()
    {
        TextChanged?.Invoke(_text);
    }

    /// <summary>
    /// Given an X position in text space, returns the character index closest to it.
    /// </summary>
    private int HitTestTextPosition(float x)
    {
        if (string.IsNullOrEmpty(_text)) return 0;

        // Binary search for the position
        var style = ResolvedInherited;
        int lo = 0, hi = _text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            float midWidth = MeasureSubstring(0, mid, style);
            if (midWidth < x)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    private float MeasureSubstring(int start, int length, ResolvedInheritedStyle style)
    {
        if (length == 0) return 0;
        var sub = _text.Substring(start, length);
        if (_renderCtx is not null)
            return _renderCtx.MeasureText(sub, style).Width;
        return sub.Length * style.FontSize * 0.6f; // fallback for tests
    }

    public override void Render(IRenderContext ctx)
    {
        _renderCtx = ctx; // cache for hit-testing

        // Draw background/border
        var borderColor = _focused ? FocusBorderColor : BorderColor;
        var origBorder = BorderColor;
        if (_focused) BorderColor = FocusBorderColor;
        base.Render(ctx);
        if (_focused) BorderColor = origBorder;

        var contentBox = Layout.AbsoluteContentBox;
        var style = ResolvedInherited;

        // Measure text widths using real font metrics
        float cursorX = _cursorPos > 0 && !string.IsNullOrEmpty(_text)
            ? ctx.MeasureText(_text[.._cursorPos], style).Width
            : 0;
        float totalTextWidth = !string.IsNullOrEmpty(_text)
            ? ctx.MeasureText(_text, style).Width
            : 0;

        // Auto-scroll to keep cursor visible
        float scrollPadding = style.FontSize; // one em of padding when scrolling
        if (cursorX - _scrollOffset > contentBox.Width - CursorWidth)
            _scrollOffset = cursorX - contentBox.Width + CursorWidth + scrollPadding;
        if (cursorX - _scrollOffset < 0)
            _scrollOffset = cursorX;
        _scrollOffset = Math.Max(0, _scrollOffset);

        ctx.PushClip(contentBox);

        // Draw selection highlight
        if (HasSelection)
        {
            float selStartX = SelectionStart > 0
                ? ctx.MeasureText(_text[..SelectionStart], style).Width
                : 0;
            float selEndX = SelectionEnd > 0
                ? ctx.MeasureText(_text[..SelectionEnd], style).Width
                : 0;
            ctx.DrawRect(new Rect(
                contentBox.X + selStartX - _scrollOffset, contentBox.Y,
                selEndX - selStartX, contentBox.Height), SelectionColor);
        }

        // Draw text or placeholder
        if (string.IsNullOrEmpty(_text) && !string.IsNullOrEmpty(Placeholder) && !_focused)
        {
            var placeholderStyle = style with { TextColor = PlaceholderColor };
            ctx.DrawText(Placeholder, contentBox, placeholderStyle, TextOverflow.Clip, 1);
        }
        else if (!string.IsNullOrEmpty(_text))
        {
            var textRect = new Rect(contentBox.X - _scrollOffset, contentBox.Y, totalTextWidth + scrollPadding, contentBox.Height);
            ctx.DrawText(_text, textRect, style, TextOverflow.Clip, 1);
        }

        // Draw cursor (sized to text line height, centered vertically)
        if (_focused)
        {
            float cx = contentBox.X + cursorX - _scrollOffset;
            float textHeight = ctx.MeasureText("X", style).Height;
            float cy = contentBox.Y + (contentBox.Height - textHeight) / 2;
            ctx.DrawRect(new Rect(cx, cy, CursorWidth, textHeight), CursorColor);
        }

        ctx.Pop(); // clip
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is TextInputElement input)
        {
            Placeholder = input.Placeholder;
            PlaceholderColor = input.PlaceholderColor;
            SelectionColor = input.SelectionColor;
            CursorColor = input.CursorColor;
            FocusBorderColor = input.FocusBorderColor;
            CursorWidth = input.CursorWidth;
            TextChanged = input.TextChanged;
            Text = input.Text;
        }
    }
}

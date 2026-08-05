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
    private CompositionState _composition;

    public string Text
    {
        get => _text;
        set
        {
            var newValue = value ?? "";
            bool changed = _text != newValue;
            _text = newValue;
            _cursorPos = Math.Min(_cursorPos, _text.Length);
            _selectionStart = _cursorPos;
            MarkPaintDirty();
            if (changed)
                NotifyAccessibilityPropertyChanged(AccessibilityProperties.Value);
        }
    }

    public int CursorPosition
    {
        get => _cursorPos;
        set
        {
            _cursorPos = SnapToBoundary(Math.Clamp(value, 0, _text.Length));
            _selectionStart = _cursorPos;
        }
    }

    // ──── UTF-16 surrogate-pair stepping ────
    // Emojis and other supplementary-plane codepoints occupy two UTF-16 chars.
    // Treat them as a single unit for cursor movement / deletion so we never
    // leave an orphan surrogate in _text (which would later crash the shaper).
    private int StepLeft(int pos)
    {
        if (pos <= 0) return 0;
        if (pos >= 2 && char.IsLowSurrogate(_text[pos - 1]) && char.IsHighSurrogate(_text[pos - 2]))
            return pos - 2;
        return pos - 1;
    }

    private int StepRight(int pos)
    {
        if (pos >= _text.Length) return _text.Length;
        if (pos + 1 < _text.Length && char.IsHighSurrogate(_text[pos]) && char.IsLowSurrogate(_text[pos + 1]))
            return pos + 2;
        return pos + 1;
    }

    // If pos lands between halves of a surrogate pair, snap to the pair's start.
    private int SnapToBoundary(int pos)
    {
        if (pos > 0 && pos < _text.Length
            && char.IsLowSurrogate(_text[pos])
            && char.IsHighSurrogate(_text[pos - 1]))
            return pos - 1;
        return pos;
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

    /// <summary>Render the value as bullets (password entry). The real text is still held
    /// in <see cref="Text"/> and reported through <see cref="TextChanged"/>; only the
    /// on-screen glyphs are masked.</summary>
    public bool Masked { get; set; }

    public Action<string>? TextChanged { get; set; }

    /// <summary>
    /// Clipboard reference — set by Application when the element is focused.
    /// </summary>
    public IClipboard? Clipboard { get; set; }

    /// <summary>
    /// IME host — set by Window when this element gains focus. Used to position
    /// the OS candidate window and cancel composition on blur.
    /// </summary>
    public ITextInputHost? TextInputHost { get; set; }

    /// <summary>
    /// Current IME preedit string. Empty when no composition is active.
    /// Exposed for tests and custom rendering hooks.
    /// </summary>
    public string CompositionText => _composition.Text ?? "";
    public int CompositionSegmentStart => _composition.SegmentStart;
    public int CompositionSegmentLength => _composition.SegmentLength;
    public bool IsComposing => _composition.IsActive;

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
        On<BlurEvent>(_ =>
        {
            _focused = false;
            if (_composition.IsActive)
            {
                _composition.Clear();
                TextInputHost?.ClearComposition();
            }
            MarkPaintDirty();
        });
        On<TextInputEvent>(OnTextInput);
        On<TextEditingEvent>(OnTextEditing);
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
        // IME commit arrives as a normal TextInput — the selection was already
        // consumed when composition started, so only clear the preedit.
        _composition.Clear();
        DeleteSelection();
        _text = _text.Insert(_cursorPos, evt.Text);
        _cursorPos += evt.Text.Length;
        _selectionStart = _cursorPos;
        NotifyChanged();
        MarkPaintDirty();
    }

    private void OnTextEditing(TextEditingEvent evt)
    {
        if (!_focused) return;
        // IMEs replace the current selection on first composition event.
        if (!_composition.IsActive && evt.Text.Length > 0 && HasSelection)
            DeleteSelection();
        _composition.Set(evt.Text, evt.SegmentStart, evt.SegmentLength);
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
                    int target = ctrl ? FindWordBoundaryLeft(_cursorPos) : StepLeft(_cursorPos);
                    _text = _text.Remove(target, _cursorPos - target);
                    _cursorPos = target;
                    _selectionStart = _cursorPos;
                }
                NotifyChanged();
                break;

            case KeyCode.Delete:
                if (HasSelection)
                    DeleteSelection();
                else if (_cursorPos < _text.Length)
                {
                    int target = ctrl ? FindWordBoundaryRight(_cursorPos) : StepRight(_cursorPos);
                    _text = _text.Remove(_cursorPos, target - _cursorPos);
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
        return SnapToBoundary(HitTestTextPosition(clickX));
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

        // Step by codepoint, not UTF-16 code unit, so emojis move as one.
        _cursorPos = delta < 0 ? StepLeft(_cursorPos) : StepRight(_cursorPos);
        if (!extendSelection)
            _selectionStart = _cursorPos;
    }

    private void MoveCursorTo(int pos, bool extendSelection)
    {
        _cursorPos = SnapToBoundary(Math.Clamp(pos, 0, _text.Length));
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
        NotifyAccessibilityPropertyChanged(AccessibilityProperties.Value);
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
        var origBorder = BorderColor;
        if (_focused) BorderColor = FocusBorderColor;
        base.Render(ctx);
        if (_focused) BorderColor = origBorder;

        var contentBox = Layout.AbsoluteContentBox;
        var style = ResolvedInherited;

        // When composing, the preedit is visually inserted at the caret but not
        // part of _text. displayText contains both for measurement/rendering.
        bool composing = _composition.IsActive;
        string displayText = composing
            ? string.Concat(_text.AsSpan(0, _cursorPos), _composition.Text, _text.AsSpan(_cursorPos))
            : _text;
        // Password masking: substitute bullets for the value (not while composing).
        if (Masked && !composing) displayText = new string('●', _text.Length);

        // IME caret placement. SDL's start/length are ambiguous (Wayland
        // sends cursor_begin/cursor_end, other backends send active-segment
        // start/length). When the IME doesn't specify anything non-trivial
        // (both zero), put the caret at the end of the preedit — the
        // behavior users expect and what Firefox/Chrome/GTK do. Otherwise
        // trust the IME's specified position.
        int caretIndex;
        if (composing)
        {
            if (_composition.SegmentStart == 0 && _composition.SegmentLength == 0)
                caretIndex = _cursorPos + _composition.Text.Length;
            else
                caretIndex = _cursorPos + _composition.SegmentStart + _composition.SegmentLength;
        }
        else
        {
            caretIndex = _cursorPos;
        }

        float cursorX = caretIndex > 0 && displayText.Length > 0
            ? ctx.MeasureText(displayText[..caretIndex], style).Width
            : 0;
        float totalTextWidth = displayText.Length > 0
            ? ctx.MeasureText(displayText, style).Width
            : 0;

        // Auto-scroll to keep cursor visible. Clamp to [0, maxScroll] so that
        // when the text shrinks (e.g. after an IME commit replaces a wide
        // preedit with shorter committed text) the offset snaps back and the
        // caret doesn't end up pinned to the left edge.
        float scrollPadding = style.FontSize;
        float maxScroll = Math.Max(0, totalTextWidth + scrollPadding - contentBox.Width);
        if (cursorX - _scrollOffset > contentBox.Width - CursorWidth)
            _scrollOffset = cursorX - contentBox.Width + CursorWidth + scrollPadding;
        if (cursorX - _scrollOffset < 0)
            _scrollOffset = cursorX;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);

        ctx.PushClip(contentBox);

        // Selection highlight (suppressed during composition — IMEs don't show it)
        if (HasSelection && !composing)
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
        if (displayText.Length == 0 && !string.IsNullOrEmpty(Placeholder) && !_focused)
        {
            var placeholderStyle = style with { TextColor = PlaceholderColor };
            ctx.DrawText(Placeholder, contentBox, placeholderStyle, TextOverflow.Clip, 1);
        }
        else if (displayText.Length > 0)
        {
            var textRect = new Rect(contentBox.X - _scrollOffset, contentBox.Y, totalTextWidth + scrollPadding, contentBox.Height);
            ctx.DrawText(displayText, textRect, style, TextOverflow.Clip, 1);
        }

        float textHeight = ctx.MeasureText("X", style).Height;
        float textTop = contentBox.Y + (contentBox.Height - textHeight) / 2;

        // Preedit underline
        if (composing)
        {
            float preStart = ctx.MeasureText(displayText[.._cursorPos], style).Width;
            float preEnd = ctx.MeasureText(displayText[..(_cursorPos + _composition.Text.Length)], style).Width;
            float underlineY = textTop + textHeight;

            ctx.DrawRect(new Rect(
                contentBox.X + preStart - _scrollOffset, underlineY,
                preEnd - preStart, 1f), CursorColor);

            if (_composition.SegmentLength > 0)
            {
                int segStartIdx = _cursorPos + _composition.SegmentStart;
                int segEndIdx = segStartIdx + _composition.SegmentLength;
                float segStartX = ctx.MeasureText(displayText[..segStartIdx], style).Width;
                float segEndX = ctx.MeasureText(displayText[..segEndIdx], style).Width;
                ctx.DrawRect(new Rect(
                    contentBox.X + segStartX - _scrollOffset, underlineY,
                    segEndX - segStartX, 2f), CursorColor);
            }
        }

        // Caret + IME area
        if (_focused)
        {
            float cx = contentBox.X + cursorX - _scrollOffset;
            var caretRect = new Rect(cx, textTop, CursorWidth, textHeight);
            ctx.DrawRect(caretRect, CursorColor);
            TextInputHost?.SetInputArea(caretRect, 0);
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

            // Preserve the caret and selection across a reconcile patch. The Text
            // setter resets both to an end-collapsed default, which would wipe a
            // live selection (or jump the caret) whenever a MarkDirty() rebuilds
            // the tree while the user is editing — e.g. a Shift+Home selection
            // vanishing because an unrelated signal fired. When the incoming text
            // is the same string the user is already editing (the common
            // rebuild-on-keystroke case), restore the caret/anchor they placed.
            int caret = _cursorPos;
            int anchor = _selectionStart;
            var newText = input.Text ?? "";
            bool sameText = _text == newText;
            Text = newText;
            if (sameText)
            {
                _cursorPos = SnapToBoundary(Math.Clamp(caret, 0, _text.Length));
                _selectionStart = SnapToBoundary(Math.Clamp(anchor, 0, _text.Length));
            }
        }
    }
}

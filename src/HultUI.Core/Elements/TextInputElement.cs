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
    private SelectionUnit _dragUnit;
    private (int Start, int End) _dragAnchor;
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

    /// <summary>Optional paint-only colour spans over the value (e.g. mask-token highlighting).
    /// The caret, selection, and clipboard still operate on the raw text; ignored while masked
    /// or composing.</summary>
    public IReadOnlyList<Rendering.TextSpan>? Spans { get; set; }

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

    /// <summary>
    /// Whether this input draws a box of its own. Set false — see the <c>Chromeless()</c>
    /// builder — and the theme's background, border, corner radius and padding are left alone
    /// so the caller owns them.
    ///
    /// <para>For an input that lives inside a control someone else composed — a search box with
    /// a magnifier beside the text, a cell editor, the text half of a combo box. Without it
    /// there is no way to get one: <c>ApplyTheme</c> runs on every style pass and re-imposes the
    /// theme's box, so a background set on the instance survives exactly until the next pass and
    /// then silently reverts. The colours that are about the <i>text</i> — placeholder, caret,
    /// selection — still come from the theme, because those are not chrome.</para>
    ///
    /// <para>The element keeps whatever padding it was given (its own default, if none), since
    /// a caller composing the box usually wants to space the parts itself.</para>
    /// </summary>
    public bool DrawsChrome { get; set; } = true;

    // ──── Underline (see TextInputTheme.UnderlineColor) ────

    public Color? UnderlineColor { get; set; }
    public Color? FocusUnderlineColor { get; set; }
    public float UnderlineThickness { get; set; } = 1f;
    public float FocusUnderlineThickness { get; set; } = 2f;

    /// <summary>Fill while focused; null keeps <see cref="BoxElement.BackgroundColor"/>.</summary>
    public Color? FocusBackground { get; set; }

    private void DrawUnderline(IRenderContext ctx)
    {
        // Chromeless means the caller owns the box, and the underline is part of the box.
        if (!DrawsChrome) return;

        var color = _focused ? FocusUnderlineColor ?? UnderlineColor : UnderlineColor;
        if (color is not { } c) return;

        TextFieldUnderline.Draw(ctx, Layout.AbsoluteBorderBox, CornerRadius, c,
            _focused ? FocusUnderlineThickness : UnderlineThickness);
    }

    protected internal override void ApplyTheme(Theme theme)
    {
        var ti = theme.TextInput;
        if (DrawsChrome)
        {
            CornerRadius = ti.CornerRadius;
            Box.Padding = ti.Padding;
            Box.BorderWidth = ti.BorderWidth;
            BackgroundColor = ti.Background ?? theme.Background;
            BorderColor = ti.BorderColor ?? theme.Border;
        }
        // Theme font size is a default: an explicit builder FontSize wins and isn't
        // re-clobbered on the StyleResolver passes that aren't preceded by a rebuild
        // (matches TextAreaElement).
        Inherited.FontSize ??= ti.FontSize;
        FocusBorderColor = ti.FocusBorderColor ?? theme.Primary;
        UnderlineColor = ti.UnderlineColor;
        FocusUnderlineColor = ti.FocusUnderlineColor;
        UnderlineThickness = ti.UnderlineThickness;
        FocusUnderlineThickness = ti.FocusUnderlineThickness;
        FocusBackground = ti.FocusBackground;
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

        OnOwn<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        OnOwn<BlurEvent>(_ =>
        {
            _focused = false;
            if (_composition.IsActive)
            {
                _composition.Clear();
                TextInputHost?.ClearComposition();
            }
            MarkPaintDirty();
        });
        OnOwn<TextInputEvent>(OnTextInput);
        OnOwn<TextEditingEvent>(OnTextEditing);
        OnOwn<KeyDownEvent>(OnKeyDown);
        OnOwn<PointerDownEvent>(OnPointerDown);
        OnOwn<PointerMoveEvent>(OnPointerMove);
        OnOwn<PointerUpEvent>(OnPointerUp);

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

    public static TextInputElement New(string text = "", [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new TextInputElement { _text = text, _cursorPos = text.Length, _selectionStart = text.Length }
            .WithOrigin(file, line);

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

        // The word (or the whole value) is taken on the second press, not on the release that
        // completes the double-click: the button is still down, so the drag that follows keeps
        // extending the selection a word at a time.
        _dragUnit = DragSelection.UnitFor(evt.ClickCount);
        bool shift = evt.Modifiers.HasFlag(KeyModifiers.Shift);
        if (shift && _dragUnit == SelectionUnit.Character)
        {
            _cursorPos = pos;
        }
        else
        {
            _dragAnchor = UnitRangeAt(pos);
            (_selectionStart, _cursorPos) = _dragAnchor;
        }

        _isDragging = true;
        MarkPaintDirty();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (!_isDragging) return;

        DragTo(evt.Position);
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        _isDragging = false;
    }

    /// <summary>The range one drag step covers at <paramref name="pos"/>, under the unit the
    /// press that started the drag settled on.</summary>
    private (int Start, int End) UnitRangeAt(int pos) => _dragUnit switch
    {
        SelectionUnit.Word => WordBoundary.RangeAt(_text, pos),
        // One line is the whole value here — an input holds no line breaks to divide it.
        SelectionUnit.Line => (0, _text.Length),
        _ => (pos, pos),
    };

    private void DragTo(Point position)
    {
        int pos = PositionFromPoint(position);
        if (_dragUnit == SelectionUnit.Character)
            _cursorPos = pos;
        else
            (_selectionStart, _cursorPos) = DragSelection.Extend(_dragAnchor, UnitRangeAt(pos));
        MarkPaintDirty();
    }

    /// <summary>
    /// Called by Application to update drag selection when pointer is outside this element's bounds.
    /// </summary>
    public void UpdateDragPosition(Point position)
    {
        if (!_isDragging) return;
        DragTo(position);
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

        // On a line that mixes directions the arrow keys move the caret VISUALLY:
        // pressing Right on the last Latin character before a Hebrew word lands on
        // the right-hand end of that word, because that is what is next on screen.
        // A line that reads left to right says so and keeps the logical step below.
        if (_renderCtx is not null
            && _renderCtx.TryMoveCaretVisually(_text, ResolvedInherited, _cursorPos, delta > 0, out int visual))
        {
            _cursorPos = SnapToBoundary(Math.Clamp(visual, 0, _text.Length));
            if (!extendSelection)
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

    private int FindWordBoundaryLeft(int from) => WordBoundary.WordLeft(_text, from);

    private int FindWordBoundaryRight(int from) => WordBoundary.WordRight(_text, from);

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

        var style = ResolvedInherited;
        if (_renderCtx is not null)
            return _renderCtx.TextOffsetAtX(_text, style, x);

        // No render context (tests): fall back to the same nearest-boundary search
        // over the estimated widths MeasureSubstring hands out.
        int lo = 0, hi = _text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (MeasureSubstring(0, mid, style) < x) lo = mid + 1;
            else hi = mid;
        }
        int before = Math.Max(0, lo - 1);
        return Math.Abs(MeasureSubstring(0, before, style) - x) <= Math.Abs(MeasureSubstring(0, lo, style) - x)
            ? before
            : lo;
    }

    private float MeasureSubstring(int start, int length, ResolvedInheritedStyle style)
    {
        if (length == 0) return 0;
        var sub = _text.Substring(start, length);
        if (_renderCtx is not null)
            return _renderCtx.MeasureText(sub, style).Width;
        return sub.Length * style.FontSize * 0.6f; // fallback for tests
    }

    /// <summary>Default content width for an input with no text and no explicit width,
    /// so a bare flex child still occupies a visible box.</summary>
    private const float DefaultContentWidth = 120f;

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        var style = ResolvedInherited;
        // Height comes from the font's line box. Without this, a bare flex-child
        // input (no explicit Height) measures its content as zero and paints
        // nothing — text, caret and placeholder all live inside a collapsed box.
        float lineHeight = ctx.MeasureText("Ag", style, float.PositiveInfinity, 1).Height;
        if (lineHeight <= 0) lineHeight = style.FontSize * 1.4f;

        // A default content width keeps an ungrown input visible; a flex-grown or
        // explicitly-sized input still wins (grow and forced sizing override the
        // measured content size).
        float textWidth = string.IsNullOrEmpty(_text)
            ? 0f
            : ctx.MeasureText(_text, style, float.PositiveInfinity, 1).Width;
        float width = Math.Max(textWidth + CursorWidth, DefaultContentWidth);

        return new Size(width, lineHeight);
    }

    public override void Render(IRenderContext ctx)
    {
        _renderCtx = ctx; // cache for hit-testing

        // Draw background/border
        var origBorder = BorderColor;
        var origBackground = BackgroundColor;
        if (_focused)
        {
            BorderColor = FocusBorderColor;
            if (FocusBackground is { } fb) BackgroundColor = fb;
        }
        base.Render(ctx);
        if (_focused)
        {
            BorderColor = origBorder;
            BackgroundColor = origBackground;
        }

        DrawUnderline(ctx);

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

        float cursorX = displayText.Length > 0
            ? ctx.TextCaretX(displayText, style, caretIndex)
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
            // One rect per contiguous visual stretch: a selection spanning a
            // direction change covers two pieces of the line, not the gap between.
            foreach (var r in ctx.TextSelectionRanges(_text, style, SelectionStart, SelectionEnd))
                ctx.DrawRect(new Rect(
                    contentBox.X + r.X0 - _scrollOffset, contentBox.Y,
                    r.X1 - r.X0, contentBox.Height), SelectionColor);
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
            // Paint-only syntax spans (e.g. mask token highlighting) — only when the display
            // text is the real value (not masked bullets or an IME preedit).
            var spans = Masked || composing ? null : Spans;
            ctx.DrawText(displayText, textRect, style, TextOverflow.Clip, 1, spans);
        }

        float textHeight = ctx.MeasureText("X", style).Height;
        float textTop = contentBox.Y + (contentBox.Height - textHeight) / 2;

        // Preedit underline
        if (composing)
        {
            float underlineY = textTop + textHeight;

            foreach (var r in ctx.TextSelectionRanges(displayText, style,
                _cursorPos, _cursorPos + _composition.Text.Length))
                ctx.DrawRect(new Rect(
                    contentBox.X + r.X0 - _scrollOffset, underlineY,
                    r.X1 - r.X0, 1f), CursorColor);

            if (_composition.SegmentLength > 0)
            {
                int segStartIdx = _cursorPos + _composition.SegmentStart;
                foreach (var r in ctx.TextSelectionRanges(displayText, style,
                    segStartIdx, segStartIdx + _composition.SegmentLength))
                    ctx.DrawRect(new Rect(
                        contentBox.X + r.X0 - _scrollOffset, underlineY,
                        r.X1 - r.X0, 2f), CursorColor);
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
            DrawsChrome = input.DrawsChrome;
            PlaceholderColor = input.PlaceholderColor;
            SelectionColor = input.SelectionColor;
            CursorColor = input.CursorColor;
            FocusBorderColor = input.FocusBorderColor;
            UnderlineColor = input.UnderlineColor;
            FocusUnderlineColor = input.FocusUnderlineColor;
            UnderlineThickness = input.UnderlineThickness;
            FocusUnderlineThickness = input.FocusUnderlineThickness;
            FocusBackground = input.FocusBackground;
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

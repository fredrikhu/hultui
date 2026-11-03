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
    private readonly TextUndoHistory _history = new();

    /// <summary>The value. Setting a different one from code also forgets the undo history,
    /// whose recorded offsets describe the text it replaced; a rebuild handing back the value
    /// the user is already editing keeps it.</summary>
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
            {
                _history.Clear();
                NotifyAccessibilityPropertyChanged(AccessibilityProperties.Value);
            }
        }
    }

    public int CursorPosition
    {
        get => _cursorPos;
        set
        {
            _cursorPos = SnapToBoundary(Math.Clamp(value, 0, _text.Length));
            _selectionStart = _cursorPos;
            _history.Seal();
        }
    }

    /// <summary>Whether <see cref="Undo"/> has an edit to take back.</summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>Whether <see cref="Redo"/> has an undone edit to re-apply.</summary>
    public bool CanRedo => _history.CanRedo;

    /// <summary>
    /// Takes back the last edit — what Ctrl+Z does. Typing undoes a word at a time, a run of
    /// Backspaces or Deletes as one, a paste or cut on its own, and the selection the edit was
    /// made from comes back with the text. Reported through <see cref="TextChanged"/> like any
    /// other edit. False when there was nothing to undo, or while an input method is composing.
    /// </summary>
    public bool Undo()
    {
        if (_composition.IsActive || !_history.Undo(ref _text, out _cursorPos, out _selectionStart))
            return false;
        NotifyChanged();
        MarkPaintDirty();
        return true;
    }

    /// <summary>Re-applies the last undone edit — Ctrl+Y or Ctrl+Shift+Z. Any new edit discards
    /// what could have been redone.</summary>
    public bool Redo()
    {
        if (_composition.IsActive || !_history.Redo(ref _text, out _cursorPos, out _selectionStart))
            return false;
        NotifyChanged();
        MarkPaintDirty();
        return true;
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

    /// <summary>Selects the whole value and puts the caret at its end — what Ctrl+A does, for
    /// a caller that wants the same effect on a field it just focused: a find bar re-opened
    /// with a query still in it, where the next keystroke should replace rather than append.
    /// A no-op on an empty field.</summary>
    public void SelectAll()
    {
        _selectionStart = 0;
        _cursorPos = _text.Length;
        _history.Seal();
        MarkPaintDirty();
    }

    public string Placeholder { get; set; } = "";
    public Color PlaceholderColor { get; set; } = new(150, 150, 150, 255);
    public Color SelectionColor { get; set; } = new(66, 135, 245, 80);
    public Color CursorColor { get; set; } = new(0, 0, 0, 255);
    public Color FocusBorderColor { get; set; } = new(66, 135, 245, 255);
    public float CursorWidth { get; set; } = 1.5f;

    /// <summary>Password entry: the value is drawn as bullets, assistive tech is told it is a
    /// password and given the bullets too, and Cut and Copy do nothing (Paste still works).
    /// The real text is still held in <see cref="Text"/> and reported through
    /// <see cref="TextChanged"/>.</summary>
    public bool Masked
    {
        get => _masked;
        set
        {
            if (_masked == value) return;
            _masked = value;
            _history.BreaksAtWords = !value;
            MarkLayoutDirty(); // the content is measured on what is shown
            MarkPaintDirty();
            // A reveal toggle must reach a reader now, not on the next layout-driven rebuild.
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.IsPassword);
            // And the platform's input method, if the field has the keyboard.
            if (_focused) TextInputHost?.StartTextInput(InputKind);
        }
    }
    private bool _masked;

    /// <summary>What this field asks of the platform's text input when it has focus.</summary>
    internal TextInputKind InputKind => Masked ? TextInputKind.Password : TextInputKind.Text;

    /// <summary>What stands in for each character of a masked value.</summary>
    internal const char MaskGlyph = '●';

    /// <summary>The value as it is shown: bullets while <see cref="Masked"/>, the text itself
    /// otherwise. What a screen reader is given, so it learns no more than the screen shows.</summary>
    internal string ShownText => Masked ? new string(MaskGlyph, _text.Length) : _text;

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

    /// <summary>
    /// This field's own look, layered over the theme's: each knob resolves from here, then
    /// <see cref="TextInputTheme"/>, then the palette. Null: the theme's field. Compared by
    /// reference, so hand the same instance each build — a static on the app's UI helpers.
    /// Where the theme's field is flush, this is how a view gets a bordered one without a
    /// wrapper box.
    /// </summary>
    public TextInputStyle? Style
    {
        get => _style;
        set
        {
            if (ReferenceEquals(_style, value)) return;
            _style = value;
            MarkStyleDirty();
        }
    }
    private TextInputStyle? _style;

    protected internal override void ApplyTheme(Theme theme)
    {
        var ti = theme.TextInput;
        var s = _style;
        if (DrawsChrome)
        {
            CornerRadius = s?.CornerRadius ?? ti.CornerRadius;
            // Another padding or border (a theme, a style set on the live field) lays the field
            // out again: a rebuild no longer does it for every field it patches.
            var box = BoxRead;
            var padding = s?.Padding ?? ti.Padding;
            var border = s?.BorderWidth ?? ti.BorderWidth;
            if (box.Padding != padding || box.BorderWidth != border)
            {
                Box.Padding = padding;
                Box.BorderWidth = border;
                MarkLayoutDirty();
            }
            BackgroundColor = s?.Background ?? ti.Background ?? theme.Background;
            BorderColor = s?.Border ?? ti.BorderColor ?? theme.Border;
        }
        // Theme font size is a default: an explicit builder FontSize wins and isn't
        // re-clobbered on the StyleResolver passes that aren't preceded by a rebuild
        // (matches TextAreaElement).
        Inherited.FontSize ??= s?.FontSize ?? ti.FontSize;
        FocusBorderColor = s?.FocusBorder ?? ti.FocusBorderColor ?? theme.Primary;
        UnderlineColor = s?.Underline ?? ti.UnderlineColor;
        FocusUnderlineColor = s?.FocusUnderline ?? ti.FocusUnderlineColor;
        UnderlineThickness = s?.UnderlineThickness ?? ti.UnderlineThickness;
        FocusUnderlineThickness = s?.FocusUnderlineThickness ?? ti.FocusUnderlineThickness;
        FocusBackground = s?.FocusBackground ?? ti.FocusBackground;
        PlaceholderColor = s?.Placeholder ?? ti.PlaceholderColor ?? theme.Secondary;
        SelectionColor = s?.Selection ?? ti.SelectionColor ?? new Color(theme.Primary.R, theme.Primary.G, theme.Primary.B, 80);
        CursorColor = s?.Cursor ?? ti.CursorColor ?? theme.TextColor;
    }

    public TextInputElement()
    {
        IsFocusable = true;
        Box.Padding = new Thickness(4, 8, 4, 8);
        // No font size of its own: ApplyTheme's `??=` fills it from the style or the theme,
        // and a builder .FontSize() set before that wins. A default here made the theme's
        // FontSize dead — the coalesce never saw a null.
        Inherited.Cursor = CursorType.Text;

        OnOwn<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        OnOwn<BlurEvent>(_ =>
        {
            _focused = false;
            _history.Seal();
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
        bool canCopy = HasSelection && !Masked;
        bool hasClipboard = Clipboard?.GetText() is not null;

        return
        [
            new ContextMenuItem { Label = "Undo", ShortcutHint = "Ctrl+Z", IsDisabled = !CanUndo,
                Action = () => Undo() },
            new ContextMenuItem { Label = "Redo", ShortcutHint = "Ctrl+Y", IsDisabled = !CanRedo,
                Action = () => Redo() },
            ContextMenuItem.Separator(),
            new ContextMenuItem { Label = "Cut", ShortcutHint = "Ctrl+X", IsDisabled = !canCopy,
                Action = () => { Cut(); MarkPaintDirty(); } },
            new ContextMenuItem { Label = "Copy", ShortcutHint = "Ctrl+C", IsDisabled = !canCopy,
                Action = Copy },
            new ContextMenuItem { Label = "Paste", ShortcutHint = "Ctrl+V", IsDisabled = !hasClipboard,
                Action = () => { Paste(); MarkPaintDirty(); } },
            ContextMenuItem.Separator(),
            new ContextMenuItem { Label = "Select All", ShortcutHint = "Ctrl+A", IsDisabled = _text.Length == 0,
                Action = SelectAll },
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
        Replace(SelectionStart, SelectionEnd, evt.Text, TextUndoHistory.EditKind.Typing);
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

        // Letter chords go by the letter on the key, not its position: on a German keyboard the
        // key KeyCode calls Y is the one printed Z, and Ctrl+Z there must undo, not redo.
        if (ctrl && TryLetterShortcut(evt.ShortcutLetter, shift))
        {
            MarkPaintDirty();
            return;
        }

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
                    Replace(target, _cursorPos, "", TextUndoHistory.EditKind.DeleteBackward);
                }
                NotifyChanged();
                break;

            case KeyCode.Delete:
                if (HasSelection)
                    DeleteSelection();
                else if (_cursorPos < _text.Length)
                {
                    int target = ctrl ? FindWordBoundaryRight(_cursorPos) : StepRight(_cursorPos);
                    Replace(_cursorPos, target, "", TextUndoHistory.EditKind.DeleteForward);
                }
                NotifyChanged();
                break;

            default:
                return; // Don't mark paint dirty for unhandled keys
        }

        MarkPaintDirty();
    }

    /// <summary>Ctrl plus a letter: select all, the clipboard, undo and redo. False for a letter
    /// the field has no use for, which leaves the key to the rest of the handler.</summary>
    private bool TryLetterShortcut(char letter, bool shift)
    {
        switch (letter)
        {
            case 'a': SelectAll(); return true;
            case 'c': Copy(); return true;
            case 'x': Cut(); return true;
            case 'v': Paste(); return true;
            case 'z': if (shift) Redo(); else Undo(); return true;
            case 'y': Redo(); return true;
            default: return false;
        }
    }

    // A masked value never reaches the clipboard, where any application can read it — what
    // browsers do with a password field. Cut does nothing at all, rather than delete what it
    // would not copy.
    private void Copy()
    {
        if (HasSelection && !Masked && Clipboard is not null)
            Clipboard.SetText(SelectedText);
    }

    private void Cut()
    {
        if (!HasSelection || Masked || Clipboard is null) return;
        Clipboard.SetText(SelectedText);
        DeleteSelection();
        NotifyChanged();
    }

    private void Paste()
    {
        if (Clipboard?.GetText() is not { } clipText) return;
        Replace(SelectionStart, SelectionEnd, clipText, TextUndoHistory.EditKind.Other);
        NotifyChanged();
    }

    /// <summary>What an assistive tool's set-value does (UIA's value pattern): the whole value
    /// replaced as a paste over Ctrl+A would, one edit Ctrl+Z takes back, told through
    /// <see cref="TextChanged"/>. Nothing while an input method is composing.</summary>
    internal void SetValueFromAssistiveTool(string value)
    {
        if (_composition.IsActive || value == _text) return;
        Replace(0, _text.Length, value, TextUndoHistory.EditKind.Other);
        NotifyChanged();
        MarkPaintDirty();
    }

    /// <summary>The one way the value is edited: the range [<paramref name="start"/>,
    /// <paramref name="end"/>) becomes <paramref name="inserted"/>, the caret lands collapsed
    /// after it, and the undo history hears about it. An edit that bypasses this is one Ctrl+Z
    /// cannot take back — and the next undo then finds text it does not describe and drops the
    /// history.</summary>
    private void Replace(int start, int end, string inserted, TextUndoHistory.EditKind kind)
    {
        _history.Record(kind, start, _text[start..end], inserted, _cursorPos, _selectionStart);
        _text = string.Concat(_text.AsSpan(0, start), inserted, _text.AsSpan(end));
        _cursorPos = _selectionStart = start + inserted.Length;
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

        _history.Seal();
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
        SelectionUnit.Word => Masked ? (0, _text.Length) : WordBoundary.RangeAt(_text, pos),
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
        _history.Seal();
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
        // A line that reads left to right says so and keeps the logical step below — as a masked
        // value's bullets do, so the caret's path cannot trace where the text changes direction.
        if (_renderCtx is not null
            && _renderCtx.TryMoveCaretVisually(ShownText, ResolvedInherited, _cursorPos, delta > 0, out int visual))
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
        _history.Seal();
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

    // A masked value is one word, as a browser's password field is: a caret that stopped at
    // its word boundaries would show where its spaces and punctuation are.
    private int FindWordBoundaryLeft(int from) => Masked ? 0 : WordBoundary.WordLeft(_text, from);

    private int FindWordBoundaryRight(int from) => Masked ? _text.Length : WordBoundary.WordRight(_text, from);

    private void DeleteSelection()
    {
        if (!HasSelection) return;
        Replace(SelectionStart, SelectionEnd, "", TextUndoHistory.EditKind.Other);
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
        // Hit what is drawn: bullets, for a masked value.
        var shown = ShownText;
        if (shown.Length == 0) return 0;

        var style = ResolvedInherited;
        if (_renderCtx is not null)
            return _renderCtx.TextOffsetAtX(shown, style, x);

        // No render context (tests): fall back to the same nearest-boundary search
        // over the estimated widths MeasureSubstring hands out.
        int lo = 0, hi = shown.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (MeasureSubstring(shown, mid, style) < x) lo = mid + 1;
            else hi = mid;
        }
        int before = Math.Max(0, lo - 1);
        return Math.Abs(MeasureSubstring(shown, before, style) - x) <= Math.Abs(MeasureSubstring(shown, lo, style) - x)
            ? before
            : lo;
    }

    private float MeasureSubstring(string text, int length, ResolvedInheritedStyle style)
    {
        if (length == 0) return 0;
        var sub = text[..length];
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
        // The bullets' width for a masked value: a field that grows with its content would
        // otherwise trace the text with its edge.
        float textWidth = string.IsNullOrEmpty(_text)
            ? 0f
            : ctx.MeasureText(ShownText, style, float.PositiveInfinity, 1).Width;
        float width = Math.Max(textWidth + CursorWidth, DefaultContentWidth);

        return new Size(width, lineHeight);
    }

    // The line the field reserves — an "X" — centred in the content box, as Render places it.
    // Measured on that "X" rather than the value, so the field's baseline does not move with
    // what is typed in it.
    internal override float? MeasureBaseline(Size borderBox, IMeasureContext ctx)
    {
        var style = ResolvedInherited;
        float line = ctx.MeasureText("X", style, float.PositiveInfinity, 1).Height;
        return ContentTop + (ContentSizeOf(borderBox).Height - line) / 2
            + ctx.MeasureFirstBaseline("X", style, float.PositiveInfinity, 1);
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
        // Bullets for a masked value, the preedit among them while an input method composes —
        // it is what is being typed, and the input method's own window shows it anyway. Every
        // width below is measured on this, never on _text: a highlight or a caret placed by the
        // real glyphs would trace what the bullets hide.
        string shown = ShownText;
        string displayText = composing
            ? string.Concat(shown.AsSpan(0, _cursorPos), _composition.Text, shown.AsSpan(_cursorPos))
            : shown;

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
            foreach (var r in ctx.TextSelectionRanges(shown, style, SelectionStart, SelectionEnd))
                ctx.DrawRect(new Rect(
                    contentBox.X + r.X0 - _scrollOffset, contentBox.Y,
                    r.X1 - r.X0, contentBox.Height), SelectionColor);
        }

        // The line sits in the middle of the content box, however tall the box is — the same
        // place the caret goes. Drawn into the whole box it sat at the top, which is why every
        // fixed-height field used to be an input inside a centring row.
        // A box shorter than the line trims both edges evenly rather than lopping off the
        // descenders, which is the kinder failure.
        float textHeight = ctx.MeasureText("X", style).Height;
        float textTop = contentBox.Y + (contentBox.Height - textHeight) / 2;

        // Draw text or placeholder
        if (displayText.Length == 0 && !string.IsNullOrEmpty(Placeholder) && !_focused)
        {
            var placeholderStyle = style with { TextColor = PlaceholderColor };
            ctx.DrawText(Placeholder, new Rect(contentBox.X, textTop, contentBox.Width, textHeight),
                placeholderStyle, TextOverflow.Clip, 1);
        }
        else if (displayText.Length > 0)
        {
            var textRect = new Rect(contentBox.X - _scrollOffset, textTop, totalTextWidth + scrollPadding, textHeight);
            // Paint-only syntax spans (e.g. mask token highlighting) — only when the display
            // text is the real value (not masked bullets or an IME preedit).
            var spans = Masked || composing ? null : Spans;
            ctx.DrawText(displayText, textRect, style, TextOverflow.Clip, 1, spans);
        }

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

    // Its chrome is the theme's (compared on a themed twin); of what it copies below, the content
    // is measured on the value and the caret's width, so a change to either marks layout here.
    private protected override bool PatchMarksOwnLayout => GetType() == typeof(TextInputElement);
    private protected override bool PatchThemesTwin => GetType() == typeof(TextInputElement);

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is TextInputElement input)
        {
            if (CursorWidth != input.CursorWidth || _text != (input.Text ?? ""))
                MarkLayoutDirty();
            Placeholder = input.Placeholder;
            DrawsChrome = input.DrawsChrome;
            Style = input.Style;
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
            // Unkeyed siblings match by position, so the live element patched here can be one
            // that was built unmasked — a username field the password field moved into.
            Masked = input.Masked;
            Spans = input.Spans;

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

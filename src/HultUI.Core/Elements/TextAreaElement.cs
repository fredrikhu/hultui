using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public class TextAreaElement : BoxElement, IScrollbarDraggable, IAutoScrollable
{
    private string _text = "";
    private int _cursorPos;
    private int _selectionStart;
    private bool _focused;
    private bool _isDragging;
    private float _scrollOffsetY;
    private IRenderContext? _renderCtx;
    private CompositionState _composition;

    // Wrapped-line cache: each entry is the char index where that line starts.
    // Line i spans _lineStarts[i] .. _lineStarts[i+1]-1 (or end of text).
    private List<int>? _lineStarts;
    private float _lastWrapWidth;
    private float _lineHeight;

    // Preferred column (in pixels) for up/down navigation
    private float _preferredX = -1;

    // Set when the cursor moves; cleared after Render scrolls to it.
    // This prevents scroll-wheel offset from being overridden every frame.
    private bool _needsScrollToCursor;

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
            InvalidateLines();
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

    // ──── UTF-16 surrogate-pair stepping (see TextInputElement for rationale) ────
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

    public Action<string>? TextChanged { get; set; }

    /// <summary>When true the buffer can be selected, scrolled, and copied, but not
    /// edited (no typing / Enter / Backspace / Delete / Cut / Paste), and no caret is
    /// drawn — a selectable read-only viewer (e.g. a code preview).</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Optional per-range text colors (syntax highlighting and similar), in
    /// whole-buffer UTF-16 offsets. Purely a paint concern — never affects wrapping or
    /// measurement. Each visible line slices the spans it overlaps.</summary>
    public IReadOnlyList<TextSpan>? Spans { get; set; }

    public IClipboard? Clipboard { get; set; }
    public ITextInputHost? TextInputHost { get; set; }

    public string CompositionText => _composition.Text ?? "";
    public int CompositionSegmentStart => _composition.SegmentStart;
    public int CompositionSegmentLength => _composition.SegmentLength;
    public bool IsComposing => _composition.IsActive;

    /// <summary>True while the user is dragging to select text.</summary>
    public bool IsDragging => _isDragging;

    // Scrollbar appearance
    public Color ScrollbarColor { get; set; } = new(128, 128, 128, 100);
    public Color ScrollbarThumbColor { get; set; } = new(128, 128, 128, 180);
    public float ScrollbarWidth { get; set; } = 6f;
    public float ScrollSpeed { get; set; } = 40f;

    protected internal override void ApplyTheme(Theme theme)
    {
        var ti = theme.TextInput;
        CornerRadius = ti.CornerRadius;
        Box.Padding = ti.Padding;
        Box.BorderWidth = ti.BorderWidth;
        // Theme font size is a default: an explicit builder FontSize (e.g. a code
        // preview) wins and isn't re-clobbered every StyleResolver pass.
        Inherited.FontSize ??= ti.FontSize;
        BackgroundColor = ti.Background ?? theme.Background;
        BorderColor = ti.BorderColor ?? theme.Border;
        FocusBorderColor = ti.FocusBorderColor ?? theme.Primary;
        PlaceholderColor = ti.PlaceholderColor ?? theme.Secondary;
        SelectionColor = ti.SelectionColor ?? new Color(theme.Primary.R, theme.Primary.G, theme.Primary.B, 80);
        CursorColor = ti.CursorColor ?? theme.TextColor;
    }

    public TextAreaElement()
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
        OnOwn<ClickEvent>(OnClick);
        OnOwn<ScrollEvent>(OnScroll);

        ContextMenuItems = BuildContextMenuItems;
    }

    private IReadOnlyList<ContextMenuItem> BuildContextMenuItems()
    {
        bool hasSelection = HasSelection;
        bool hasClipboard = Clipboard?.GetText() is not null;

        // Read-only: a viewer only copies and selects — no Cut / Paste.
        if (ReadOnly)
            return
            [
                new ContextMenuItem { Label = "Copy", ShortcutHint = "Ctrl+C", IsDisabled = !hasSelection,
                    Action = () => { if (HasSelection && Clipboard is not null) Clipboard.SetText(SelectedText); } },
                new ContextMenuItem { Label = "Select All", ShortcutHint = "Ctrl+A", IsDisabled = _text.Length == 0,
                    Action = () => { _selectionStart = 0; _cursorPos = _text.Length; MarkPaintDirty(); } },
            ];

        return
        [
            new ContextMenuItem { Label = "Cut", ShortcutHint = "Ctrl+X", IsDisabled = !hasSelection,
                Action = () => { if (HasSelection && Clipboard is not null) { Clipboard.SetText(SelectedText); DeleteSelection(); NotifyChanged(); MarkPaintDirty(); } } },
            new ContextMenuItem { Label = "Copy", ShortcutHint = "Ctrl+C", IsDisabled = !hasSelection,
                Action = () => { if (HasSelection && Clipboard is not null) Clipboard.SetText(SelectedText); } },
            new ContextMenuItem { Label = "Paste", ShortcutHint = "Ctrl+V", IsDisabled = !hasClipboard,
                Action = () => { if (Clipboard?.GetText() is { } t) { DeleteSelection(); InsertText(t); NotifyChanged(); MarkPaintDirty(); } } },
            ContextMenuItem.Separator(),
            new ContextMenuItem { Label = "Select All", ShortcutHint = "Ctrl+A", IsDisabled = _text.Length == 0,
                Action = () => { _selectionStart = 0; _cursorPos = _text.Length; MarkPaintDirty(); } },
        ];
    }

    public static TextAreaElement New(string text = "", [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new TextAreaElement { _text = text, _cursorPos = text.Length, _selectionStart = text.Length }
            .WithOrigin(file, line);

    // ──── Line Wrapping ────

    private void InvalidateLines() => _lineStarts = null;

    /// <summary>
    /// Computes line starts using word wrapping for the given width.
    /// Each entry is the character index where a wrapped line begins.
    /// </summary>
    internal List<int> GetLineStarts(float wrapWidth, ResolvedInheritedStyle style)
    {
        if (_lineStarts is not null && Math.Abs(_lastWrapWidth - wrapWidth) < 0.5f)
            return _lineStarts;

        _lastWrapWidth = wrapWidth;
        _lineStarts = ComputeLineStarts(wrapWidth, style);
        return _lineStarts;
    }

    private List<int> ComputeLineStarts(float wrapWidth, ResolvedInheritedStyle style)
    {
        var starts = new List<int> { 0 };
        if (string.IsNullOrEmpty(_text))
            return starts;

        int pos = 0;
        while (pos < _text.Length)
        {
            // Find the end of the current hard line (up to next \n or end)
            int hardEnd = _text.IndexOf('\n', pos);
            if (hardEnd < 0) hardEnd = _text.Length;

            // Wrap the segment [pos..hardEnd) within wrapWidth
            int segStart = pos;
            while (segStart < hardEnd)
            {
                // How many chars fit on this wrapped line?
                int lineEnd = FindLineBreak(segStart, hardEnd, wrapWidth, style);
                if (lineEnd < hardEnd)
                {
                    starts.Add(lineEnd);
                    segStart = lineEnd;
                }
                else
                {
                    break;
                }
            }

            // Move past the \n
            pos = hardEnd + 1;
            if (pos <= _text.Length)
                starts.Add(pos);
        }

        // If text ends with \n, we already added the final empty line above.
        // If text doesn't end with \n, the last line is already covered.
        return starts;
    }

    /// <summary>
    /// Given a segment [start..hardEnd), find where to break the first line
    /// to fit within wrapWidth. Returns the char index where the next line starts.
    /// If the whole segment fits, returns hardEnd.
    /// </summary>
    private int FindLineBreak(int start, int hardEnd, float wrapWidth, ResolvedInheritedStyle style)
    {
        if (start >= hardEnd) return hardEnd;

        float segWidth = MeasureSubstring(start, hardEnd - start, style);
        if (segWidth <= wrapWidth)
            return hardEnd;

        // Binary search for how many chars fit
        int lo = 1, hi = hardEnd - start;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            float w = MeasureSubstring(start, mid, style);
            if (w <= wrapWidth)
                lo = mid;
            else
                hi = mid - 1;
        }

        int fitEnd = start + lo;

        // Try to break at a word boundary (walk back to last space)
        int breakAt = fitEnd;
        while (breakAt > start && !char.IsWhiteSpace(_text[breakAt - 1]))
            breakAt--;

        if (breakAt > start)
        {
            // Skip trailing spaces at the break point
            return breakAt;
        }

        // No word boundary found — break at character
        return fitEnd;
    }

    private int LineCount(float wrapWidth, ResolvedInheritedStyle style) =>
        GetLineStarts(wrapWidth, style).Count;

    /// <summary>Number of visible lines in the viewport, used for PageUp/PageDown.</summary>
    private int GetPageLineCount()
    {
        float viewportHeight = Layout.AbsoluteContentBox.Height;
        float lh = GetLineHeight(ResolvedInherited);
        return Math.Max(1, (int)(viewportHeight / lh) - 1);
    }

    /// <summary>
    /// Returns (lineIndex, columnOffset) for a given cursor position.
    /// </summary>
    internal (int line, int col) CursorToLineCol(int pos, float wrapWidth, ResolvedInheritedStyle style)
    {
        var starts = GetLineStarts(wrapWidth, style);
        // Binary search for the last line where lineStart <= pos
        int lo = 0, hi = starts.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (starts[mid] <= pos)
                lo = mid;
            else
                hi = mid - 1;
        }
        return (lo, pos - starts[lo]);
    }

    /// <summary>
    /// Returns the flat string position for a (line, col) coordinate.
    /// </summary>
    private int LineColToCursor(int line, int col, float wrapWidth, ResolvedInheritedStyle style)
    {
        var starts = GetLineStarts(wrapWidth, style);
        line = Math.Clamp(line, 0, starts.Count - 1);
        int lineStart = starts[line];
        int lineEnd = line + 1 < starts.Count ? starts[line + 1] : _text.Length;

        // Don't count trailing \n as part of the line for column clamping
        if (lineEnd > lineStart && lineEnd <= _text.Length && lineEnd - 1 >= 0 && lineEnd - 1 < _text.Length && _text[lineEnd - 1] == '\n')
            lineEnd--;

        int maxCol = lineEnd - lineStart;
        return lineStart + Math.Clamp(col, 0, maxCol);
    }

    /// <summary>Length of visible characters on the given wrapped line (excluding trailing \n).</summary>
    private int GetLineLength(int lineIndex, float wrapWidth, ResolvedInheritedStyle style)
    {
        var starts = GetLineStarts(wrapWidth, style);
        int lineStart = starts[lineIndex];
        int lineEnd = lineIndex + 1 < starts.Count ? starts[lineIndex + 1] : _text.Length;
        if (lineEnd > lineStart && lineEnd <= _text.Length && lineEnd - 1 >= 0 && lineEnd - 1 < _text.Length && _text[lineEnd - 1] == '\n')
            lineEnd--;
        return lineEnd - lineStart;
    }

    // ──── Text Measurement ────

    private float MeasureSubstring(int start, int length, ResolvedInheritedStyle style)
    {
        if (length <= 0) return 0;
        var sub = _text.Substring(start, length);
        if (_renderCtx is not null)
            return _renderCtx.MeasureText(sub, style).Width;
        return sub.Length * style.FontSize * 0.6f; // fallback for tests
    }

    private float GetLineHeight(ResolvedInheritedStyle style)
    {
        if (_lineHeight > 0) return _lineHeight;
        if (_renderCtx is not null)
        {
            _lineHeight = _renderCtx.MeasureText("X", style).Height;
            return _lineHeight;
        }
        return style.FontSize; // fallback
    }

    // ──── Event Handlers ────

    private void OnTextInput(TextInputEvent evt)
    {
        if (!_focused || ReadOnly) return;
        _composition.Clear();
        DeleteSelection();
        InsertText(evt.Text);
        NotifyChanged();
        MarkPaintDirty();
    }

    private void OnTextEditing(TextEditingEvent evt)
    {
        if (!_focused || ReadOnly) return;
        if (!_composition.IsActive && evt.Text.Length > 0 && HasSelection)
            DeleteSelection();
        _composition.Set(evt.Text, evt.SegmentStart, evt.SegmentLength);
        MarkPaintDirty();
    }

    private void InsertText(string t)
    {
        _text = _text.Insert(_cursorPos, t);
        _cursorPos += t.Length;
        _selectionStart = _cursorPos;
        _preferredX = -1;
        _needsScrollToCursor = true;
        InvalidateLines();
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (!_focused) return;

        bool shift = evt.Modifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = evt.Modifiers.HasFlag(KeyModifiers.Ctrl);

        switch (evt.Key)
        {
            case KeyCode.Left:
                if (ctrl) MoveCursorWordLeft(shift);
                else MoveCursor(-1, shift);
                _preferredX = -1;
                break;

            case KeyCode.Right:
                if (ctrl) MoveCursorWordRight(shift);
                else MoveCursor(1, shift);
                _preferredX = -1;
                break;

            // In a read-only viewer there's no visible caret to chase, so plain
            // navigation scrolls the content like a pager (matching the old keyboard
            // scroll). Shift+navigation still moves the caret to extend a selection.
            case KeyCode.Up:
                if (ReadOnly && !shift) { ScrollLines(-1); break; }
                MoveCursorVertical(-1, shift);
                break;

            case KeyCode.Down:
                if (ReadOnly && !shift) { ScrollLines(1); break; }
                MoveCursorVertical(1, shift);
                break;

            case KeyCode.PageUp:
                if (ReadOnly && !shift) { ScrollPage(-1); break; }
                MoveCursorVertical(-GetPageLineCount(), shift);
                break;

            case KeyCode.PageDown:
                if (ReadOnly && !shift) { ScrollPage(1); break; }
                MoveCursorVertical(GetPageLineCount(), shift);
                break;

            case KeyCode.Home:
                if (ReadOnly && !shift) { _scrollOffsetY = 0; break; }
                if (ctrl)
                    MoveCursorTo(0, shift);
                else
                    MoveToLineStart(shift);
                _preferredX = -1;
                break;

            case KeyCode.End:
                if (ReadOnly && !shift) { _scrollOffsetY = MaxScrollY(); break; }
                if (ctrl)
                    MoveCursorTo(_text.Length, shift);
                else
                    MoveToLineEnd(shift);
                _preferredX = -1;
                break;

            case KeyCode.Enter:
                if (ReadOnly) return;
                DeleteSelection();
                InsertText("\n");
                NotifyChanged();
                break;

            case KeyCode.Backspace:
                if (ReadOnly) return;
                if (HasSelection)
                    DeleteSelection();
                else if (_cursorPos > 0)
                {
                    int target = ctrl ? FindWordBoundaryLeft(_cursorPos) : StepLeft(_cursorPos);
                    _text = _text.Remove(target, _cursorPos - target);
                    _cursorPos = target;
                    _selectionStart = _cursorPos;
                    _needsScrollToCursor = true;
                    InvalidateLines();
                }
                _preferredX = -1;
                NotifyChanged();
                break;

            case KeyCode.Delete:
                if (ReadOnly) return;
                if (HasSelection)
                    DeleteSelection();
                else if (_cursorPos < _text.Length)
                {
                    int target = ctrl ? FindWordBoundaryRight(_cursorPos) : StepRight(_cursorPos);
                    _text = _text.Remove(_cursorPos, target - _cursorPos);
                    InvalidateLines();
                }
                _preferredX = -1;
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
                if (ReadOnly) return;
                if (HasSelection && Clipboard is not null)
                {
                    Clipboard.SetText(SelectedText);
                    DeleteSelection();
                    NotifyChanged();
                }
                break;

            case KeyCode.V when ctrl:
                if (ReadOnly) return;
                if (Clipboard?.GetText() is { } clipText)
                {
                    DeleteSelection();
                    InsertText(clipText);
                    NotifyChanged();
                }
                break;

            default:
                return;
        }

        MarkPaintDirty();
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.Button != 0) return;

        int pos = PositionFromPoint(evt.Position);
        bool shift = evt.Modifiers.HasFlag(KeyModifiers.Shift);
        _cursorPos = pos;
        if (!shift) _selectionStart = pos;
        _preferredX = -1;
        _needsScrollToCursor = true;
        _isDragging = true;
        MarkPaintDirty();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (!_isDragging) return;
        _cursorPos = PositionFromPoint(evt.Position);
        _preferredX = -1;
        _needsScrollToCursor = true;
        MarkPaintDirty();
    }

    private void OnPointerUp(PointerUpEvent evt) => _isDragging = false;

    private void OnClick(ClickEvent evt)
    {
        if (evt.ClickCount == 2)
        {
            var (ws, we) = WordBoundary.RangeAt(_text, PositionFromPoint(evt.Position));
            _selectionStart = ws;
            _cursorPos = we;
            _needsScrollToCursor = true;
            MarkPaintDirty();
        }
        else if (evt.ClickCount >= 3)
        {
            // Triple-click: select the current line
            var style = ResolvedInherited;
            float wrapWidth = GetWrapWidth();
            var (line, _) = CursorToLineCol(_cursorPos, wrapWidth, style);
            var starts = GetLineStarts(wrapWidth, style);
            _selectionStart = starts[line];
            int lineEnd = line + 1 < starts.Count ? starts[line + 1] : _text.Length;
            _cursorPos = lineEnd;
            _needsScrollToCursor = true;
            MarkPaintDirty();
        }
    }

    private void OnScroll(ScrollEvent evt)
    {
        var style = ResolvedInherited;
        float wrapWidth = GetWrapWidth();
        float totalHeight = LineCount(wrapWidth, style) * GetLineHeight(style);
        float viewportHeight = Layout.AbsoluteContentBox.Height;
        float maxScroll = Math.Max(0, totalHeight - viewportHeight);

        _scrollOffsetY = Math.Clamp(_scrollOffsetY - evt.DeltaY * ScrollSpeed, 0, maxScroll);
        evt.StopPropagation = true;
        MarkPaintDirty();
    }

    /// <summary>Current vertical scroll offset in pixels. Setting it clamps to the
    /// scrollable range, so a caller need not know the content height.</summary>
    public float ScrollOffsetY
    {
        get => _scrollOffsetY;
        set
        {
            float clamped = Math.Clamp(value, 0, MaxScrollY());
            if (clamped == _scrollOffsetY) return;
            _scrollOffsetY = clamped;
            MarkPaintDirty();
        }
    }

    // ---- Autoscroll (IAutoScrollable) ----

    /// <inheritdoc cref="ScrollViewElement.MiddleClickAutoScroll"/>
    public bool MiddleClickAutoScroll { get; set; } = true;

    /// <summary>Whether the text is taller than the box holding it. Horizontal never counts:
    /// the area wraps to its width, so there is no sideways axis to scroll.</summary>
    public bool CanAutoScroll => TotalContentHeight() > Layout.AbsoluteContentBox.Height + 0.5f;

    /// <summary>Moves the view down by <paramref name="dy"/>. The horizontal delta is
    /// discarded rather than ignored quietly for lack of a use: wrapped text has no
    /// overflow to the right to reach.</summary>
    public bool ScrollBy(float dx, float dy)
    {
        float before = _scrollOffsetY;
        ScrollOffsetY = before + dy;
        return _scrollOffsetY != before;
    }

    /// <summary>Largest valid vertical scroll offset (content height minus viewport).</summary>
    private float MaxScrollY() => Math.Max(0, TotalContentHeight() - Layout.AbsoluteContentBox.Height);

    /// <summary>
    /// Brings a text line into view, scrolling only when it is not already there — so walking
    /// matches down a file does not yank the viewport on every step.
    ///
    /// <para>Counted in <i>logical</i> lines, which is what a caller searching the text has.
    /// The body is drawn in <i>wrapped</i> rows, and every row is one line height tall, so the
    /// two only agree while nothing wraps. Multiplying the logical index by the line height —
    /// which is what this used to do — therefore lands short by one row per wrap above the
    /// target, and the further down the file the match is, the further above the viewport's
    /// bottom edge it stops. The line's character offset is resolved through the same wrap
    /// cache the renderer uses, so the answer is the row the line actually starts on.</para>
    /// </summary>
    public void ScrollLineIntoView(int line) =>
        ScrollOffsetIntoView(OffsetOfLogicalLine(line));

    /// <summary>
    /// Brings the character at <paramref name="offset"/> into view. Same scroll-only-when-needed
    /// behaviour as <see cref="ScrollLineIntoView"/>, but exact for a caller that already holds
    /// an absolute offset: on a logical line long enough to wrap several times, this lands on
    /// the row carrying that character rather than the row the line began on.
    /// </summary>
    public void ScrollOffsetIntoView(int offset)
    {
        var style = ResolvedInherited;
        float lh = GetLineHeight(style);
        if (lh <= 0) return;
        var (row, _) = CursorToLineCol(Math.Clamp(offset, 0, _text.Length), GetWrapWidth(), style);
        float viewport = Layout.AbsoluteContentBox.Height;
        float top = row * lh;

        if (top < _scrollOffsetY)
            _scrollOffsetY = top;
        else if (top + lh > _scrollOffsetY + viewport)
            _scrollOffsetY = top + lh - viewport;
        else
            return;   // already on screen — leave the viewport where the reader put it

        _scrollOffsetY = Math.Clamp(_scrollOffsetY, 0, MaxScrollY());
        MarkPaintDirty();
    }

    /// <summary>Character offset where hard line <paramref name="line"/> begins. Past the last
    /// line clamps to that line's start, so a stale index scrolls to the end rather than to 0.</summary>
    private int OffsetOfLogicalLine(int line)
    {
        int offset = 0;
        for (int seen = 0; seen < line; seen++)
        {
            int nl = _text.IndexOf('\n', offset);
            if (nl < 0) break;
            offset = nl + 1;
        }
        return offset;
    }

    /// <summary>Scrolls the viewport by <paramref name="lines"/> text lines (read-only pager nav).</summary>
    private void ScrollLines(int lines) =>
        _scrollOffsetY = Math.Clamp(_scrollOffsetY + lines * GetLineHeight(ResolvedInherited), 0, MaxScrollY());

    /// <summary>Scrolls the viewport a page (a viewport minus one line of context) up/down.</summary>
    private void ScrollPage(int dir)
    {
        float lh = GetLineHeight(ResolvedInherited);
        float page = Math.Max(lh, Layout.AbsoluteContentBox.Height - lh);
        _scrollOffsetY = Math.Clamp(_scrollOffsetY + dir * page, 0, MaxScrollY());
    }

    public void UpdateDragPosition(Point position)
    {
        if (!_isDragging) return;
        _cursorPos = PositionFromPoint(position);
        _preferredX = -1;
        _needsScrollToCursor = true;
        MarkPaintDirty();
    }

    public void EndDrag() => _isDragging = false;

    // ──── Scrollbar thumb drag (IScrollbarDraggable) ────
    private bool _draggingScrollbar;
    private float _scrollbarGrabOffset;   // pointer Y minus thumb top at grab time

    public bool IsDraggingScrollbar => _draggingScrollbar;

    private float TotalContentHeight() =>
        LineCount(GetWrapWidth(), ResolvedInherited) * GetLineHeight(ResolvedInherited);

    public bool TryStartScrollbarDrag(Point position)
    {
        var contentBox = Layout.AbsoluteContentBox;
        float totalHeight = TotalContentHeight();
        if (totalHeight <= contentBox.Height) return false;   // nothing to scroll ⇒ no bar

        float trackX = contentBox.X + contentBox.Width - ScrollbarWidth;
        if (position.X < trackX || position.X > trackX + ScrollbarWidth
            || position.Y < contentBox.Y || position.Y > contentBox.Y + contentBox.Height)
            return false;   // not on the scrollbar

        float trackHeight = contentBox.Height;
        float thumbHeight = Math.Max(20, trackHeight * (contentBox.Height / totalHeight));
        float maxScroll = totalHeight - contentBox.Height;
        float scrollRatio = _scrollOffsetY / Math.Max(1, maxScroll);
        float thumbY = contentBox.Y + scrollRatio * (trackHeight - thumbHeight);

        if (position.Y >= thumbY && position.Y <= thumbY + thumbHeight)
        {
            _draggingScrollbar = true;
            _scrollbarGrabOffset = position.Y - thumbY;
        }
        else
        {
            // Track click — center the thumb on the pointer.
            float clickRatio = Math.Clamp((position.Y - contentBox.Y - thumbHeight / 2) / (trackHeight - thumbHeight), 0, 1);
            _scrollOffsetY = clickRatio * maxScroll;
            MarkPaintDirty();
        }
        return true;
    }

    public bool IsPointOverScrollbar(Point position)
    {
        var contentBox = Layout.AbsoluteContentBox;
        if (TotalContentHeight() <= contentBox.Height) return false;   // no bar drawn
        float trackX = contentBox.X + contentBox.Width - ScrollbarWidth;
        return position.X >= trackX && position.X <= trackX + ScrollbarWidth
            && position.Y >= contentBox.Y && position.Y <= contentBox.Y + contentBox.Height;
    }

    public void UpdateScrollbarDrag(Point position)
    {
        if (!_draggingScrollbar) return;
        var contentBox = Layout.AbsoluteContentBox;
        float totalHeight = TotalContentHeight();
        float trackHeight = contentBox.Height;
        float thumbHeight = Math.Max(20, trackHeight * (contentBox.Height / totalHeight));
        float thumbTop = position.Y - _scrollbarGrabOffset;
        float scrollRatio = Math.Clamp((thumbTop - contentBox.Y) / (trackHeight - thumbHeight), 0, 1);
        _scrollOffsetY = scrollRatio * (totalHeight - contentBox.Height);
        MarkPaintDirty();
    }

    public void EndScrollbarDrag() => _draggingScrollbar = false;

    // ──── Cursor Movement ────

    private void MoveCursor(int delta, bool extendSelection)
    {
        if (!extendSelection && HasSelection)
        {
            _cursorPos = delta < 0 ? SelectionStart : SelectionEnd;
            _selectionStart = _cursorPos;
            _needsScrollToCursor = true;
            return;
        }

        // Visual movement within the caret's own visual line, when that line mixes
        // directions — see TextInputElement.MoveCursor. A move that would leave the
        // line has nothing visual to say, so the logical step below carries the
        // caret onto the next one.
        if (TryMoveCursorVisually(delta > 0, out int visual))
        {
            _cursorPos = SnapToBoundary(Math.Clamp(visual, 0, _text.Length));
            if (!extendSelection)
                _selectionStart = _cursorPos;
            _needsScrollToCursor = true;
            return;
        }

        _cursorPos = delta < 0 ? StepLeft(_cursorPos) : StepRight(_cursorPos);
        if (!extendSelection)
            _selectionStart = _cursorPos;
        _needsScrollToCursor = true;
    }

    private bool TryMoveCursorVisually(bool right, out int position)
    {
        position = _cursorPos;
        if (_renderCtx is null) return false;

        var style = ResolvedInherited;
        var starts = GetLineStarts(GetWrapWidth(), style);
        var (line, _) = CursorToLineCol(_cursorPos, GetWrapWidth(), style);
        var (lineStart, lineEnd) = VisualLineRange(starts, line);
        if (lineEnd <= lineStart) return false;

        if (!_renderCtx.TryMoveCaretVisually(
                _text[lineStart..lineEnd], style, _cursorPos - lineStart, right, out int rel))
            return false;

        position = lineStart + rel;
        return position != _cursorPos;
    }

    private void MoveCursorTo(int pos, bool extendSelection)
    {
        _cursorPos = SnapToBoundary(Math.Clamp(pos, 0, _text.Length));
        if (!extendSelection)
            _selectionStart = _cursorPos;
        _needsScrollToCursor = true;
    }

    private void MoveCursorVertical(int lineDelta, bool extendSelection)
    {
        var style = ResolvedInherited;
        float wrapWidth = GetWrapWidth();
        var (line, col) = CursorToLineCol(_cursorPos, wrapWidth, style);
        var starts = GetLineStarts(wrapWidth, style);

        // Compute preferred X from current position if not set
        if (_preferredX < 0)
        {
            var (lineStart, lineEnd) = VisualLineRange(starts, line);
            _preferredX = CaretXOnLine(lineStart, lineEnd, _cursorPos, style);
        }

        int targetLine = line + lineDelta;
        if (targetLine < 0)
        {
            MoveCursorTo(0, extendSelection);
            return;
        }
        if (targetLine >= starts.Count)
        {
            MoveCursorTo(_text.Length, extendSelection);
            return;
        }

        // Find the position on the target line closest to preferredX
        int targetLineStart = starts[targetLine];
        int targetLineEnd = targetLine + 1 < starts.Count ? starts[targetLine + 1] : _text.Length;
        if (targetLineEnd > targetLineStart && targetLineEnd <= _text.Length && _text[targetLineEnd - 1] == '\n')
            targetLineEnd--;

        int targetPos = HitTestLinePosition(targetLineStart, targetLineEnd, _preferredX, style);
        MoveCursorTo(targetPos, extendSelection);
    }

    private void MoveToLineStart(bool extendSelection)
    {
        var style = ResolvedInherited;
        float wrapWidth = GetWrapWidth();
        var (line, _) = CursorToLineCol(_cursorPos, wrapWidth, style);
        var starts = GetLineStarts(wrapWidth, style);
        MoveCursorTo(starts[line], extendSelection);
    }

    private void MoveToLineEnd(bool extendSelection)
    {
        var style = ResolvedInherited;
        float wrapWidth = GetWrapWidth();
        var (line, _) = CursorToLineCol(_cursorPos, wrapWidth, style);
        var starts = GetLineStarts(wrapWidth, style);
        int lineEnd = line + 1 < starts.Count ? starts[line + 1] : _text.Length;
        if (lineEnd > 0 && lineEnd <= _text.Length && lineEnd - 1 >= 0 && _text[lineEnd - 1] == '\n')
            lineEnd--;
        MoveCursorTo(lineEnd, extendSelection);
    }

    private void MoveCursorWordLeft(bool extendSelection) =>
        MoveCursorTo(FindWordBoundaryLeft(_cursorPos), extendSelection);

    private void MoveCursorWordRight(bool extendSelection) =>
        MoveCursorTo(FindWordBoundaryRight(_cursorPos), extendSelection);

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
        _preferredX = -1;
        _needsScrollToCursor = true;
        InvalidateLines();
    }

    private void NotifyChanged()
    {
        TextChanged?.Invoke(_text);
        NotifyAccessibilityPropertyChanged(AccessibilityProperties.Value);
    }

    // ──── Hit Testing ────

    private float GetWrapWidth()
    {
        float w = Layout.AbsoluteContentBox.Width;
        return w > 0 ? w : 200f; // fallback for pre-layout
    }

    private int PositionFromPoint(Point screenPos)
    {
        var contentBox = Layout.AbsoluteContentBox;
        var style = ResolvedInherited;
        float wrapWidth = GetWrapWidth();
        float lh = GetLineHeight(style);
        var starts = GetLineStarts(wrapWidth, style);

        float localY = screenPos.Y - contentBox.Y + _scrollOffsetY;
        int line = Math.Clamp((int)(localY / lh), 0, starts.Count - 1);

        float localX = screenPos.X - contentBox.X;
        int lineStart = starts[line];
        int lineEnd = line + 1 < starts.Count ? starts[line + 1] : _text.Length;
        if (lineEnd > lineStart && lineEnd <= _text.Length && _text[lineEnd - 1] == '\n')
            lineEnd--;

        return SnapToBoundary(HitTestLinePosition(lineStart, lineEnd, localX, style));
    }

    /// <summary>
    /// Given a line from lineStart to lineEnd and an X offset, find the closest char position.
    /// </summary>
    private int HitTestLinePosition(int lineStart, int lineEnd, float x, ResolvedInheritedStyle style)
    {
        if (lineStart >= lineEnd) return lineStart;

        if (_renderCtx is not null)
            return lineStart + _renderCtx.TextOffsetAtX(_text[lineStart..lineEnd], style, x);

        // No render context (tests): nearest boundary over the estimated widths.
        int lo = lineStart, hi = lineEnd;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (MeasureSubstring(lineStart, mid - lineStart, style) < x) lo = mid + 1;
            else hi = mid;
        }
        int before = Math.Max(lineStart, lo - 1);
        return Math.Abs(MeasureSubstring(lineStart, before - lineStart, style) - x)
               <= Math.Abs(MeasureSubstring(lineStart, lo - lineStart, style) - x)
            ? before
            : lo;
    }

    /// <summary>
    /// Visual x, relative to the start of a visual line, of the caret at flat
    /// position <paramref name="pos"/>. For text that reads left to right this is
    /// the width of the prefix; once a line contains right-to-left script it is
    /// not, so the mapping goes through the render context's laid-out line.
    /// </summary>
    private float CaretXOnLine(int lineStart, int lineEnd, int pos, ResolvedInheritedStyle style)
        => _renderCtx is null
            ? MeasureSubstring(lineStart, pos - lineStart, style)
            : _renderCtx.TextCaretX(_text[lineStart..lineEnd], style, pos - lineStart);

    /// <summary>Visual x-ranges covering <c>[from, to)</c> on one visual line.</summary>
    private IReadOnlyList<TextRangeBox> SelectionRangesOnLine(
        int lineStart, int lineEnd, int from, int to, ResolvedInheritedStyle style)
    {
        if (to <= from) return [];
        if (_renderCtx is null)
        {
            float a = MeasureSubstring(lineStart, from - lineStart, style);
            float b = MeasureSubstring(lineStart, to - lineStart, style);
            return b > a ? [new TextRangeBox(a, b)] : [];
        }
        return _renderCtx.TextSelectionRanges(
            _text[lineStart..lineEnd], style, from - lineStart, to - lineStart);
    }

    /// <summary>The character range of a visual line, excluding a trailing newline.</summary>
    private (int Start, int End) VisualLineRange(IReadOnlyList<int> starts, int lineIndex)
    {
        int lineStart = starts[lineIndex];
        int lineEnd = lineIndex + 1 < starts.Count ? starts[lineIndex + 1] : _text.Length;
        if (lineEnd > lineStart && lineEnd <= _text.Length && _text[lineEnd - 1] == '\n')
            lineEnd--;
        return (lineStart, lineEnd);
    }

    // ──── Rendering ────

    public override void Render(IRenderContext ctx)
    {
        if (_renderCtx != ctx)
        {
            _renderCtx = ctx;
            _lineHeight = 0; // re-measure only when context changes
        }

        var origBorder = BorderColor;
        if (_focused) BorderColor = FocusBorderColor;
        base.Render(ctx);
        if (_focused) BorderColor = origBorder;

        var contentBox = Layout.AbsoluteContentBox;
        var style = ResolvedInherited;
        float wrapWidth = contentBox.Width;
        var starts = GetLineStarts(wrapWidth, style);
        float lh = GetLineHeight(style);
        int lineCount = starts.Count;
        float totalHeight = lineCount * lh;

        float maxScroll = Math.Max(0, totalHeight - contentBox.Height);
        int cursorLine = -1;

        // Auto-scroll to keep cursor visible — only when cursor moved
        if (_needsScrollToCursor)
        {
            _needsScrollToCursor = false;
            (cursorLine, _) = CursorToLineCol(_cursorPos, wrapWidth, style);
            float cursorY = cursorLine * lh;
            if (cursorY - _scrollOffsetY < 0)
                _scrollOffsetY = cursorY;
            if (cursorY + lh - _scrollOffsetY > contentBox.Height)
                _scrollOffsetY = cursorY + lh - contentBox.Height;
        }
        _scrollOffsetY = Math.Clamp(_scrollOffsetY, 0, maxScroll);

        ctx.PushClip(contentBox);

        // Determine visible line range
        int firstVisible = Math.Max(0, (int)(_scrollOffsetY / lh));
        int lastVisible = Math.Min(lineCount - 1, (int)((_scrollOffsetY + contentBox.Height) / lh));

        // Draw selection highlight
        if (HasSelection)
            RenderSelection(ctx, contentBox, style, starts, lh, firstVisible, lastVisible);

        // Draw text or placeholder
        if (string.IsNullOrEmpty(_text) && !string.IsNullOrEmpty(Placeholder) && !_focused)
        {
            var placeholderStyle = style with { TextColor = PlaceholderColor };
            ctx.DrawText(Placeholder, contentBox, placeholderStyle, TextOverflow.Clip, 1);
        }
        else
        {
            for (int i = firstVisible; i <= lastVisible; i++)
            {
                int lineStart = starts[i];
                int lineEnd = i + 1 < starts.Count ? starts[i + 1] : _text.Length;
                // Strip trailing \n for display
                int displayEnd = lineEnd;
                if (displayEnd > lineStart && displayEnd <= _text.Length && _text[displayEnd - 1] == '\n')
                    displayEnd--;

                if (displayEnd > lineStart)
                {
                    string lineText = _text[lineStart..displayEnd];
                    float lineY = contentBox.Y + i * lh - _scrollOffsetY;
                    var lineRect = new Rect(contentBox.X, lineY, wrapWidth, lh);
                    var lineSpans = SpansForLine(lineStart, displayEnd);
                    if (lineSpans is not null)
                        ctx.DrawText(lineText, lineRect, style, TextOverflow.Clip, 1, lineSpans);
                    else
                        ctx.DrawText(lineText, lineRect, style, TextOverflow.Clip, 1);
                }
            }
        }

        // Draw cursor + preedit (a read-only viewer shows selection only, no caret).
        if (_focused && !ReadOnly)
        {
            if (cursorLine < 0)
                (cursorLine, _) = CursorToLineCol(_cursorPos, wrapWidth, style);
            var (cursorLineStart, cursorLineEnd) = VisualLineRange(starts, cursorLine);
            float cursorBaseX = contentBox.X + CaretXOnLine(cursorLineStart, cursorLineEnd, _cursorPos, style);
            float cy = contentBox.Y + cursorLine * lh - _scrollOffsetY;

            float caretX = cursorBaseX;

            // Preedit overlay — rendered inline at the caret on its line. We do NOT
            // re-wrap the text around it; long preedits may overflow the right edge.
            // CJK preedits are almost always short, so this is a deliberate v1 trade.
            if (_composition.IsActive)
            {
                var preedit = _composition.Text;
                float preeditWidth = _renderCtx is not null
                    ? _renderCtx.MeasureText(preedit, style).Width
                    : preedit.Length * style.FontSize * 0.6f;
                var preRect = new Rect(cursorBaseX, cy, preeditWidth, lh);
                ctx.DrawText(preedit, preRect, style, TextOverflow.Clip, 1);

                // Thin underline across the preedit
                ctx.DrawRect(new Rect(cursorBaseX, cy + lh - 1f, preeditWidth, 1f), CursorColor);

                // Thick underline under the active segment
                if (_composition.SegmentLength > 0 && _renderCtx is not null)
                {
                    float segX = _composition.SegmentStart > 0
                        ? _renderCtx.MeasureText(preedit[.._composition.SegmentStart], style).Width
                        : 0;
                    float segEnd = _renderCtx.MeasureText(
                        preedit[..(_composition.SegmentStart + _composition.SegmentLength)], style).Width;
                    ctx.DrawRect(new Rect(cursorBaseX + segX, cy + lh - 2f, segEnd - segX, 2f), CursorColor);
                }

                // IME caret placement — see TextInputElement for rationale.
                // (0,0) from SDL is ambiguous and usually means "no preference";
                // default to the end of the preedit. Non-zero values pin the
                // caret to the IME-specified position.
                int caretChars = _composition.SegmentStart == 0 && _composition.SegmentLength == 0
                    ? preedit.Length
                    : _composition.SegmentStart + _composition.SegmentLength;
                float caretOffsetInPreedit = _renderCtx is not null
                    ? _renderCtx.MeasureText(preedit[..caretChars], style).Width
                    : caretChars * style.FontSize * 0.6f;
                caretX = cursorBaseX + caretOffsetInPreedit;
            }

            var caretRect = new Rect(caretX, cy, CursorWidth, lh);
            ctx.DrawRect(caretRect, CursorColor);
            TextInputHost?.SetInputArea(caretRect, 0);
        }

        ctx.Pop(); // clip

        // Draw scrollbar
        if (totalHeight > contentBox.Height)
        {
            float trackHeight = contentBox.Height;
            float thumbRatio = contentBox.Height / totalHeight;
            float thumbHeight = Math.Max(20, trackHeight * thumbRatio);
            float scrollRatio = _scrollOffsetY / Math.Max(1, maxScroll);
            float thumbY = contentBox.Y + scrollRatio * (trackHeight - thumbHeight);
            float trackX = contentBox.X + contentBox.Width - ScrollbarWidth;

            ctx.DrawRect(new Rect(trackX, contentBox.Y, ScrollbarWidth, trackHeight), ScrollbarColor);
            ctx.DrawRect(new Rect(trackX, thumbY, ScrollbarWidth, thumbHeight), ScrollbarThumbColor);
        }
    }

    /// <summary>The subset of <see cref="Spans"/> overlapping the visible line
    /// [<paramref name="lineStart"/>, <paramref name="lineEnd"/>), clipped and rebased
    /// to line-local offsets so the per-line DrawText colours the right characters.
    /// Returns null when the element has no spans or none touch this line.</summary>
    private List<TextSpan>? SpansForLine(int lineStart, int lineEnd)
    {
        if (Spans is not { Count: > 0 } spans) return null;
        List<TextSpan>? result = null;
        foreach (var s in spans)
        {
            if (s.End <= lineStart) continue;      // entirely before this line
            if (s.Start >= lineEnd) break;         // sorted ⇒ nothing left overlaps
            int start = Math.Max(s.Start, lineStart);
            int end = Math.Min(s.End, lineEnd);
            if (end <= start) continue;
            (result ??= []).Add(new TextSpan(start - lineStart, end - start, s.Color, s.Background));
        }
        return result;
    }

    private void RenderSelection(IRenderContext ctx, Rect contentBox, ResolvedInheritedStyle style,
        List<int> starts, float lh, int firstVisible, int lastVisible)
    {
        int selStart = SelectionStart;
        int selEnd = SelectionEnd;

        for (int i = firstVisible; i <= lastVisible; i++)
        {
            int lineStart = starts[i];
            int lineEnd = i + 1 < starts.Count ? starts[i + 1] : _text.Length;
            int displayEnd = lineEnd;
            if (displayEnd > lineStart && displayEnd <= _text.Length && _text[displayEnd - 1] == '\n')
                displayEnd--;

            // Clip selection to this line
            int ls = Math.Max(selStart, lineStart);
            int le = Math.Min(selEnd, displayEnd);
            if (ls >= le && !(selStart < lineEnd && selEnd > lineStart && lineStart == displayEnd))
                continue;

            float lineY = contentBox.Y + i * lh - _scrollOffsetY;

            // The selection continues onto a later visual line — fill this one to the
            // right edge (like a browser). selEnd > displayEnd covers both a hard "\n"
            // (displayEnd = lineEnd - 1) and a soft wrap (displayEnd = lineEnd), so
            // wrapped mid-selection lines extend just like hard-broken ones.
            bool continues = selEnd > displayEnd && selStart <= displayEnd;
            float leadingX = ls > lineStart ? CaretXOnLine(lineStart, displayEnd, ls, style) : 0;
            if (continues)
            {
                if (contentBox.Width > leadingX)
                    ctx.DrawRect(new Rect(contentBox.X + leadingX, lineY,
                        contentBox.Width - leadingX, lh), SelectionColor);
                continue;
            }

            // One rect per contiguous visual stretch — a selection that crosses a
            // direction boundary covers two pieces of the line, not the gap between.
            foreach (var r in SelectionRangesOnLine(lineStart, displayEnd, ls, le, style))
                if (r.X1 > r.X0)
                    ctx.DrawRect(new Rect(contentBox.X + r.X0, lineY, r.X1 - r.X0, lh), SelectionColor);
        }
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is TextAreaElement area)
        {
            Placeholder = area.Placeholder;
            PlaceholderColor = area.PlaceholderColor;
            SelectionColor = area.SelectionColor;
            CursorColor = area.CursorColor;
            FocusBorderColor = area.FocusBorderColor;
            CursorWidth = area.CursorWidth;
            TextChanged = area.TextChanged;
            ReadOnly = area.ReadOnly;
            Spans = area.Spans;
            MiddleClickAutoScroll = area.MiddleClickAutoScroll;

            // Preserve the caret / selection across a reconcile patch. The Text setter
            // collapses the selection to an end-anchored caret, which would wipe a live
            // selection (or drag) whenever an unrelated MarkDirty() rebuilds the tree
            // while the user is selecting. When the incoming text is the same string
            // being viewed (the common rebuild case), restore the caret/anchor.
            int caret = _cursorPos;
            int anchor = _selectionStart;
            var newText = area.Text ?? "";
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

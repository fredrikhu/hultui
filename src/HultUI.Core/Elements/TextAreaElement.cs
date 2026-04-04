using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public class TextAreaElement : BoxElement
{
    private string _text = "";
    private int _cursorPos;
    private int _selectionStart;
    private bool _focused;
    private bool _isDragging;
    private float _scrollOffsetY;
    private IRenderContext? _renderCtx;

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
            _text = value ?? "";
            _cursorPos = Math.Min(_cursorPos, _text.Length);
            _selectionStart = _cursorPos;
            InvalidateLines();
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

    public IClipboard? Clipboard { get; set; }

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
        Inherited.FontSize = ti.FontSize;
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

        On<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        On<BlurEvent>(_ => { _focused = false; MarkPaintDirty(); });
        On<TextInputEvent>(OnTextInput);
        On<KeyDownEvent>(OnKeyDown);
        On<PointerDownEvent>(OnPointerDown);
        On<PointerMoveEvent>(OnPointerMove);
        On<PointerUpEvent>(OnPointerUp);
        On<ClickEvent>(OnClick);
        On<ScrollEvent>(OnScroll);

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
                Action = () => { if (Clipboard?.GetText() is { } t) { DeleteSelection(); InsertText(t); NotifyChanged(); MarkPaintDirty(); } } },
            ContextMenuItem.Separator(),
            new ContextMenuItem { Label = "Select All", ShortcutHint = "Ctrl+A", IsDisabled = _text.Length == 0,
                Action = () => { _selectionStart = 0; _cursorPos = _text.Length; MarkPaintDirty(); } },
        ];
    }

    public static TextAreaElement New(string text = "") => new() { _text = text, _cursorPos = text.Length, _selectionStart = text.Length };

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
        if (!_focused) return;
        DeleteSelection();
        InsertText(evt.Text);
        NotifyChanged();
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

            case KeyCode.Up:
                MoveCursorVertical(-1, shift);
                break;

            case KeyCode.Down:
                MoveCursorVertical(1, shift);
                break;

            case KeyCode.PageUp:
                MoveCursorVertical(-GetPageLineCount(), shift);
                break;

            case KeyCode.PageDown:
                MoveCursorVertical(GetPageLineCount(), shift);
                break;

            case KeyCode.Home:
                if (ctrl)
                    MoveCursorTo(0, shift);
                else
                    MoveToLineStart(shift);
                _preferredX = -1;
                break;

            case KeyCode.End:
                if (ctrl)
                    MoveCursorTo(_text.Length, shift);
                else
                    MoveToLineEnd(shift);
                _preferredX = -1;
                break;

            case KeyCode.Enter:
                DeleteSelection();
                InsertText("\n");
                NotifyChanged();
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
                    _needsScrollToCursor = true;
                    InvalidateLines();
                }
                _preferredX = -1;
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
                if (HasSelection && Clipboard is not null)
                {
                    Clipboard.SetText(SelectedText);
                    DeleteSelection();
                    NotifyChanged();
                }
                break;

            case KeyCode.V when ctrl:
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
            int pos = PositionFromPoint(evt.Position);
            int ws = pos;
            while (ws > 0 && !IsWordBreak(_text[ws - 1])) ws--;
            _selectionStart = ws;
            int we = pos;
            while (we < _text.Length && !IsWordBreak(_text[we])) we++;
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

    public void UpdateDragPosition(Point position)
    {
        if (!_isDragging) return;
        _cursorPos = PositionFromPoint(position);
        _preferredX = -1;
        _needsScrollToCursor = true;
        MarkPaintDirty();
    }

    public void EndDrag() => _isDragging = false;

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

        _cursorPos = Math.Clamp(_cursorPos + delta, 0, _text.Length);
        if (!extendSelection)
            _selectionStart = _cursorPos;
        _needsScrollToCursor = true;
    }

    private void MoveCursorTo(int pos, bool extendSelection)
    {
        _cursorPos = Math.Clamp(pos, 0, _text.Length);
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
            int lineStart = starts[line];
            _preferredX = MeasureSubstring(lineStart, _cursorPos - lineStart, style);
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
        _preferredX = -1;
        _needsScrollToCursor = true;
        InvalidateLines();
    }

    private void NotifyChanged() => TextChanged?.Invoke(_text);

    private static bool IsWordBreak(char c) => char.IsWhiteSpace(c) || char.IsPunctuation(c);

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

        return HitTestLinePosition(lineStart, lineEnd, localX, style);
    }

    /// <summary>
    /// Given a line from lineStart to lineEnd and an X offset, find the closest char position.
    /// </summary>
    private int HitTestLinePosition(int lineStart, int lineEnd, float x, ResolvedInheritedStyle style)
    {
        if (lineStart >= lineEnd) return lineStart;

        int lo = lineStart, hi = lineEnd;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            float midWidth = MeasureSubstring(lineStart, mid - lineStart, style);
            if (midWidth < x)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
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
                    ctx.DrawText(lineText, lineRect, style, TextOverflow.Clip, 1);
                }
            }
        }

        // Draw cursor
        if (_focused)
        {
            if (cursorLine < 0)
                (cursorLine, _) = CursorToLineCol(_cursorPos, wrapWidth, style);
            int cursorLineStart = starts[cursorLine];
            float cx = contentBox.X + MeasureSubstring(cursorLineStart, _cursorPos - cursorLineStart, style);
            float cy = contentBox.Y + cursorLine * lh - _scrollOffsetY;
            ctx.DrawRect(new Rect(cx, cy, CursorWidth, lh), CursorColor);
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
            float x1 = ls > lineStart
                ? MeasureSubstring(lineStart, ls - lineStart, style)
                : 0;
            float x2 = le > lineStart
                ? MeasureSubstring(lineStart, le - lineStart, style)
                : 0;

            // If selection spans past this line end (into \n), extend highlight to edge
            if (selEnd > displayEnd && selStart <= displayEnd && lineEnd > displayEnd)
                x2 = contentBox.Width;

            if (x2 > x1)
            {
                ctx.DrawRect(new Rect(contentBox.X + x1, lineY, x2 - x1, lh), SelectionColor);
            }
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
            Text = area.Text;
        }
    }
}

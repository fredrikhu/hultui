using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public class TextElement : Element
{
    private string _content = "";

    public string Content
    {
        get => _content;
        set
        {
            if (_content == value) return;
            _content = value;
            // Keep any active selection within the new text.
            _anchor = Math.Clamp(_anchor, 0, _content.Length);
            _caret = Math.Clamp(_caret, 0, _content.Length);
            MarkLayoutDirty();
            MarkPaintDirty();
            // Text content is this element's accessible name (and, when it labels
            // a button, the button's name-from-content), so a change is an
            // a11y-visible property change.
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.Name);
        }
    }

    public TextOverflow Overflow { get; set; } = TextOverflow.Clip;
    public int? MaxLines { get; set; }

    private IReadOnlyList<TextSpan>? _spans;

    /// <summary>
    /// Optional per-range text colors (syntax highlighting and similar). Spans are
    /// character ranges into <see cref="Content"/>, sorted and non-overlapping;
    /// uncovered characters use the element's text color. Purely a paint concern —
    /// layout and measurement are unaffected.
    /// </summary>
    public IReadOnlyList<TextSpan>? Spans
    {
        get => _spans;
        set
        {
            // Value-compare so reconciliation (which rebuilds span lists each
            // frame) doesn't mark paint dirty when the highlighting is unchanged.
            if (ReferenceEquals(_spans, value) || SpansEqual(_spans, value)) return;
            _spans = value;
            MarkPaintDirty();
        }
    }

    private static bool SpansEqual(IReadOnlyList<TextSpan>? a, IReadOnlyList<TextSpan>? b)
    {
        if (a is null || b is null) return false;   // both-null is caught by ReferenceEquals
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    public static TextElement New(string content, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new TextElement { _content = content }.WithOrigin(file, line);

    // ──── Text selection (opt-in) ────

    private bool _selectable;
    private bool _selectionWired;
    private int _anchor;  // fixed end of the selection
    private int _caret;   // moving end of the selection
    private bool _isDragging;

    // Cached from the last render so pointer handling can hit-test with the same
    // wrapped layout and font metrics that were drawn.
    private IRenderContext? _renderCtx;
    private IReadOnlyList<TextLineBox> _lines = [];
    private Rect _contentBox;

    /// <summary>
    /// When true the label's text can be selected with the mouse and copied
    /// (drag to select, double/triple-click, Ctrl+C, right-click Copy). Off by
    /// default — plain labels stay non-interactive.
    /// </summary>
    public bool Selectable
    {
        get => _selectable;
        set
        {
            if (_selectable == value) return;
            _selectable = value;
            IsFocusable = value;
            Inherited.Cursor = value ? CursorType.Text : CursorType.Default;
            if (value)
                WireSelection();
            else
            {
                ClearSelection();
                _isDragging = false;
            }
            MarkPaintDirty();
        }
    }

    /// <summary>Highlight colour for the selected range.</summary>
    private Color _selectionColor = new(66, 135, 245, 80);
    public Color SelectionColor
    {
        get => _selectionColor;
        set { if (_selectionColor == value) return; _selectionColor = value; MarkPaintDirty(); }
    }

    /// <summary>Clipboard reference — set by the window when this label is focused.</summary>
    public IClipboard? Clipboard { get; set; }

    /// <summary>
    /// Opt-in token that lets a mouse drag select across several labels at once. Give
    /// every label that should share one selection the <b>same</b> (stable, non-null)
    /// reference — e.g. all the code-line labels of a diff. A drag then spans from the
    /// pressed label to the one under the pointer, and Ctrl+C / Copy yields every
    /// covered label's text joined by newlines. Null (the default) keeps a label's
    /// selection self-contained.
    /// </summary>
    public object? SelectionGroup { get; set; }

    private int SelectionStart => Math.Min(_anchor, _caret);
    private int SelectionEnd => Math.Max(_anchor, _caret);
    public bool HasSelection => _anchor != _caret;
    public string SelectedText => HasSelection ? _content[SelectionStart..SelectionEnd] : "";

    /// <summary>True while the user is dragging to select. Used by the window for pointer capture.</summary>
    public bool IsDragging => _isDragging;

    // ── Cross-label (group) selection support, driven by the window ──
    internal int ContentLength => _content.Length;
    internal int CaretOffset => _caret;
    /// <summary>Maps a screen point to a caret offset in this label's text.</summary>
    internal int OffsetAt(Point screenPos) => PositionFromPoint(screenPos);
    /// <summary>Sets this label's selection range directly (window group-drag driver).</summary>
    internal void SetSelectionRange(int anchor, int caret)
    {
        int n = _content.Length;
        _anchor = Math.Clamp(anchor, 0, n);
        _caret = Math.Clamp(caret, 0, n);
        MarkPaintDirty();
    }

    protected internal override void ApplyTheme(Theme theme)
    {
        base.ApplyTheme(theme);
        SelectionColor = new Color(theme.Primary.R, theme.Primary.G, theme.Primary.B, 80);
    }

    private void WireSelection()
    {
        if (_selectionWired) return;
        _selectionWired = true;

        OnOwn<PointerDownEvent>(OnPointerDown);
        OnOwn<PointerMoveEvent>(OnPointerMove);
        OnOwn<PointerUpEvent>(OnPointerUp);
        OnOwn<ClickEvent>(OnClick);
        OnOwn<KeyDownEvent>(OnKeyDown);
        OnOwn<BlurEvent>(_ => { if (HasSelection) { ClearSelection(); MarkPaintDirty(); } });

        ContextMenuItems = BuildContextMenuItems;
    }

    private void ClearSelection() => _anchor = _caret = 0;

    private IReadOnlyList<ContextMenuItem> BuildContextMenuItems()
    {
        bool hasSelection = HasSelection;
        return
        [
            new ContextMenuItem { Label = "Copy", ShortcutHint = "Ctrl+C", IsDisabled = !hasSelection,
                Action = () => { if (HasSelection) Clipboard?.SetText(SelectedText); } },
            new ContextMenuItem { Label = "Select All", ShortcutHint = "Ctrl+A", IsDisabled = _content.Length == 0,
                Action = () => { _anchor = 0; _caret = _content.Length; MarkPaintDirty(); } },
        ];
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (!_selectable || evt.Button != 0) return;
        int pos = PositionFromPoint(evt.Position);
        _caret = pos;
        if (!evt.Modifiers.HasFlag(KeyModifiers.Shift))
            _anchor = pos;
        _isDragging = true;
        MarkPaintDirty();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (!_isDragging) return;
        _caret = PositionFromPoint(evt.Position);
        MarkPaintDirty();
    }

    private void OnPointerUp(PointerUpEvent evt) => _isDragging = false;

    private void OnClick(ClickEvent evt)
    {
        if (!_selectable) return;
        if (evt.ClickCount == 2)
        {
            int pos = PositionFromPoint(evt.Position);
            int start = pos, end = pos;
            while (start > 0 && !char.IsWhiteSpace(_content[start - 1])) start--;
            while (end < _content.Length && !char.IsWhiteSpace(_content[end])) end++;
            _anchor = start;
            _caret = end;
            MarkPaintDirty();
        }
        else if (evt.ClickCount >= 3)
        {
            _anchor = 0;
            _caret = _content.Length;
            MarkPaintDirty();
        }
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (!_selectable) return;
        bool ctrl = evt.Modifiers.HasFlag(KeyModifiers.Ctrl);
        switch (evt.Key)
        {
            case KeyCode.C when ctrl:
                // A grouped label's copy spans the whole group; the window handles it
                // (it knows the sibling labels), so don't also copy just this line.
                if (SelectionGroup is null && HasSelection) Clipboard?.SetText(SelectedText);
                break;
            case KeyCode.A when ctrl:
                _anchor = 0;
                _caret = _content.Length;
                MarkPaintDirty();
                break;
        }
    }

    /// <summary>Called by the window to extend the selection while the pointer is captured outside this label.</summary>
    public void UpdateDragPosition(Point position)
    {
        if (!_isDragging) return;
        _caret = PositionFromPoint(position);
        MarkPaintDirty();
    }

    /// <summary>Called by the window when the pointer is released during a drag capture.</summary>
    public void EndDrag() => _isDragging = false;

    // Maps a screen point to a character index in Content, using the wrapped
    // line layout cached at render time.
    private int PositionFromPoint(Point screenPos)
    {
        if (_renderCtx is null || _lines.Count == 0) return 0;
        var style = ResolvedInherited;

        float localY = screenPos.Y - _contentBox.Y;
        var line = _lines[0];
        for (int i = 0; i < _lines.Count; i++)
        {
            line = _lines[i];
            if (localY < line.OffsetY + line.Height) break;
        }

        float localX = screenPos.X - _contentBox.X;
        string lineStr = _content.Substring(line.Start, line.Length);
        int k = HitTestInLine(lineStr, localX, style);
        return SnapToBoundary(line.Start + k);
    }

    private int HitTestInLine(string lineStr, float x, ResolvedInheritedStyle style)
    {
        if (lineStr.Length == 0 || _renderCtx is null) return 0;
        int lo = 0, hi = lineStr.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            float w = _renderCtx.MeasureText(lineStr[..mid], style).Width;
            if (w < x) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    // If pos lands between halves of a surrogate pair, snap to the pair's start.
    private int SnapToBoundary(int pos) =>
        pos > 0 && pos < _content.Length && char.IsLowSurrogate(_content[pos]) && char.IsHighSurrogate(_content[pos - 1])
            ? pos - 1
            : pos;

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is TextElement text)
        {
            Content = text.Content;
            Overflow = text.Overflow;
            MaxLines = text.MaxLines;
            Spans = text.Spans;
            // Carry the opt-in flag from the freshly built element, but keep our
            // own live selection (the incoming element has none). Content's setter
            // already clamped the offsets to the new text.
            Selectable = text.Selectable;
            SelectionColor = text.SelectionColor;
            SelectionGroup = text.SelectionGroup;
        }
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) =>
        ctx.MeasureText(Content, ResolvedInherited, availableWidth, MaxLines);

    public override void Render(IRenderContext ctx)
    {
        if (string.IsNullOrEmpty(Content))
        {
            if (_selectable) { _renderCtx = ctx; _lines = []; }
            return;
        }

        var contentBox = Layout.AbsoluteContentBox;
        var style = ResolvedInherited;

        if (_selectable)
        {
            _renderCtx = ctx;
            _contentBox = contentBox;
            _lines = ctx.LayoutTextLines(Content, style, contentBox.Width, MaxLines);
            if (HasSelection)
                DrawSelection(ctx, style);
        }

        if (_spans is { Count: > 0 } spans)
            ctx.DrawText(Content, contentBox, style, Overflow, MaxLines, spans);
        else
            ctx.DrawText(Content, contentBox, style, Overflow, MaxLines);
    }

    private void DrawSelection(IRenderContext ctx, ResolvedInheritedStyle style)
    {
        int selStart = SelectionStart, selEnd = SelectionEnd;
        foreach (var line in _lines)
        {
            int ls = Math.Clamp(selStart - line.Start, 0, line.Length);
            int le = Math.Clamp(selEnd - line.Start, 0, line.Length);
            if (le <= ls && !SelectionCrossesLineEnd(line, selStart, selEnd)) continue;

            string lineStr = _content.Substring(line.Start, line.Length);
            float x0 = ls > 0 ? ctx.MeasureText(lineStr[..ls], style).Width : 0;
            float x1 = le > 0 ? ctx.MeasureText(lineStr[..le], style).Width : 0;

            // When the selection continues past this line (onto a wrapped or
            // newline-separated line), extend a little past the last glyph so the
            // spanned line break reads as selected, like a browser does.
            if (SelectionCrossesLineEnd(line, selStart, selEnd))
                x1 += style.FontSize * 0.35f;

            if (x1 <= x0) continue;
            ctx.DrawRect(new Rect(
                _contentBox.X + x0, _contentBox.Y + line.OffsetY,
                x1 - x0, line.Height), SelectionColor);
        }
    }

    // True when the selection starts at/before this line's text and extends
    // beyond its end (i.e. the following line break is part of the selection).
    private static bool SelectionCrossesLineEnd(TextLineBox line, int selStart, int selEnd) =>
        selStart <= line.Start + line.Length && selEnd > line.Start + line.Length;
}

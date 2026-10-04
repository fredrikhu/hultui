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
            // Metric-affecting spans (bold, italic, size) change measurement, so a
            // list gaining, losing or moving one has to re-layout, not just repaint.
            bool metric = AnyMetricAffecting(_spans) || AnyMetricAffecting(value);
            _spans = value;
            if (metric) MarkLayoutDirty();
            MarkPaintDirty();
        }
    }

    private static bool AnyMetricAffecting(IReadOnlyList<TextSpan>? spans)
    {
        if (spans is null) return false;
        for (int i = 0; i < spans.Count; i++)
            if (spans[i].IsMetricAffecting) return true;
        return false;
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
    private SelectionUnit _dragUnit;
    private (int Start, int End) _dragAnchor;

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

    // ──── Clickable ranges (opt-in) ────

    private IReadOnlyList<TextLink>? _links;
    private bool _clickWired;

    /// <summary>
    /// Ranges of <see cref="Content"/> that act as links. Clicking inside one calls
    /// <see cref="LinkActivated"/> with its target. Independent of
    /// <see cref="Selectable"/> — a plain label can carry links, and a selectable one can
    /// still be dragged across them. Style them with <see cref="Spans"/>.
    /// </summary>
    public IReadOnlyList<TextLink>? Links
    {
        get => _links;
        set
        {
            _links = value;
            if (value is { Count: > 0 }) WireClick();
        }
    }

    /// <summary>Runs with the <see cref="TextLink.Target"/> of the link that was clicked.</summary>
    public Action<string>? LinkActivated { get; set; }

    // One click handler serves both opt-ins, whichever turns up first.
    private void WireClick()
    {
        if (_clickWired) return;
        _clickWired = true;
        OnOwn<ClickEvent>(OnClick);
    }

    /// <summary>The link under a screen point, if any.</summary>
    private TextLink? LinkAt(Point screenPos)
    {
        if (_links is not { Count: > 0 } || _renderCtx is null || _lines.Count == 0) return null;
        int offset = PositionFromPoint(screenPos);
        foreach (var link in _links)
            if (link.Contains(offset)) return link;
        return null;
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
    /// Opt-in group that lets a mouse drag select across several labels at once. Give every
    /// label that should share one selection the <b>same</b> group and its own
    /// <see cref="SelectionIndex"/> — e.g. all the code-line labels of a diff. A drag then
    /// spans from the pressed label to the one under the pointer, and Ctrl+C / Copy yields
    /// the whole span. Null (the default) keeps a label's selection self-contained.
    /// </summary>
    public TextSelectionGroup? SelectionGroup { get; set; }

    /// <summary>
    /// This label's line number within its <see cref="SelectionGroup"/>. Ordering comes from
    /// this, not from tree order, so the label can be rebuilt or recycled freely. -1 (the
    /// default) means the label takes no part in a group selection.
    /// </summary>
    public int SelectionIndex { get; set; } = -1;

    private bool InGroup => SelectionGroup is not null && SelectionIndex >= 0;

    // The selection is *derived* when the label belongs to a group: the group holds it in
    // model coordinates and the label works out its own share on demand. Nothing has to be
    // pushed back into the element, so a row recycled off-screen and later rebuilt shows the
    // right highlight the moment it is drawn, with no re-apply pass anywhere.
    private (int Anchor, int Caret) Sel =>
        InGroup ? SelectionGroup!.RangeFor(SelectionIndex, _content.Length) : (_anchor, _caret);

    private int SelectionStart { get { var (a, c) = Sel; return Math.Min(a, c); } }
    private int SelectionEnd { get { var (a, c) = Sel; return Math.Max(a, c); } }
    public bool HasSelection { get { var (a, c) = Sel; return a != c; } }
    public string SelectedText =>
        HasSelection ? _content[Math.Clamp(SelectionStart, 0, _content.Length)..Math.Clamp(SelectionEnd, 0, _content.Length)] : "";

    /// <summary>True while the user is dragging to select. Used by the window for pointer capture.</summary>
    public bool IsDragging => _isDragging;

    // ── Cross-label (group) selection support, driven by the window ──
    internal int ContentLength => _content.Length;
    internal int CaretOffset => _caret;
    /// <summary>The unit the press that started this drag selects by, and the range it
    /// covers in this label — what the window's group drag anchors on.</summary>
    internal SelectionUnit DragUnit => _dragUnit;
    internal (int Start, int End) DragAnchorRange => _dragAnchor;
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
        WireClick();
        OnOwn<KeyDownEvent>(OnKeyDown);
        // A grouped label keeps its selection on blur: focus moves between the group's own
        // labels during a drag, and a virtualized row loses focus merely by scrolling out of
        // view. The window clears the group when the user presses outside it.
        OnOwn<BlurEvent>(_ => { if (!InGroup && HasSelection) { ClearSelection(); MarkPaintDirty(); } });

        ContextMenuItems = BuildContextMenuItems;
    }

    private void ClearSelection() => _anchor = _caret = 0;

    /// <summary>The text a copy should yield: the whole group span when grouped, else this label's.</summary>
    private string GroupAwareSelectedText() =>
        InGroup ? SelectionGroup!.SelectedText : (HasSelection ? SelectedText : "");

    /// <summary>Select-all: the whole group when grouped, else this label's own text.</summary>
    private void SelectAll()
    {
        if (InGroup) SelectionGroup!.SelectAll();
        else { _anchor = 0; _caret = _content.Length; }
    }

    /// <summary>Sets a selection within this one label, routed through the group if there is one.</summary>
    private void SelectLocalRange(int from, int to)
    {
        if (InGroup)
        {
            SelectionGroup!.Anchor(SelectionIndex, from);
            SelectionGroup!.ExtendTo(SelectionIndex, to);
        }
        else { _anchor = from; _caret = to; }
    }

    private IReadOnlyList<ContextMenuItem> BuildContextMenuItems()
    {
        // In a group both commands act on the whole span, not just this line.
        bool hasSelection = InGroup ? SelectionGroup!.HasSelection : HasSelection;
        return
        [
            new ContextMenuItem { Label = "Copy", ShortcutHint = "Ctrl+C", IsDisabled = !hasSelection,
                Action = () => { if (GroupAwareSelectedText() is { Length: > 0 } t) Clipboard?.SetText(t); } },
            new ContextMenuItem { Label = "Select All", ShortcutHint = "Ctrl+A",
                IsDisabled = InGroup ? SelectionGroup!.Count == 0 : _content.Length == 0,
                Action = () => { SelectAll(); MarkPaintDirty(); } },
        ];
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (!_selectable || evt.Button != 0) return;
        int pos = PositionFromPoint(evt.Position);
        // The word (or the line) is taken here, on the press that completes the double or
        // triple click, rather than on the release: the button is still down, so the motion
        // after it goes on extending the selection in whole words.
        _dragUnit = DragSelection.UnitFor(evt.ClickCount);
        _dragAnchor = UnitRangeAt(pos, _dragUnit);
        if (evt.Modifiers.HasFlag(KeyModifiers.Shift) && _dragUnit == SelectionUnit.Character)
        {
            _caret = pos;
        }
        else
        {
            (_anchor, _caret) = _dragAnchor;
        }
        _isDragging = true;
        MarkPaintDirty();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (!_isDragging) return;
        DragTo(evt.Position);
    }

    private void OnPointerUp(PointerUpEvent evt) => _isDragging = false;

    /// <summary>The range one drag step covers at <paramref name="offset"/> under
    /// <paramref name="unit"/>. A label holds one line, so a triple click takes all of it.</summary>
    internal (int Start, int End) UnitRangeAt(int offset, SelectionUnit unit) => unit switch
    {
        SelectionUnit.Word => WordBoundary.RangeAt(_content, offset),
        SelectionUnit.Line => (0, _content.Length),
        _ => (offset, offset),
    };

    private void DragTo(Point position)
    {
        int pos = PositionFromPoint(position);
        if (_dragUnit == SelectionUnit.Character)
            _caret = pos;
        else
            (_anchor, _caret) = DragSelection.Extend(_dragAnchor, UnitRangeAt(pos, _dragUnit));
        MarkPaintDirty();
    }

    private void OnClick(ClickEvent evt)
    {
        // A plain click on a link follows it; a second click is the word-select gesture, so
        // only the first one counts. Links work whether or not the label is selectable.
        if (evt.ClickCount == 1 && LinkActivated is { } activate && LinkAt(evt.Position) is { } link)
        {
            activate(link.Target);
            evt.StopPropagation = true;
            return;
        }
        // Word and line selection happen on the press that completes the multi-click, not
        // here — see OnPointerDown.
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (!_selectable) return;
        bool ctrl = evt.Modifiers.HasFlag(KeyModifiers.Ctrl);
        if (evt.Modifiers.HasFlag(KeyModifiers.Shift) && ExtendSelectionByKey(evt.Key, ctrl))
        {
            // Consumed: without this the list underneath would scroll on the same key and
            // the selection would appear to jump two lines for every one the user asked for.
            evt.StopPropagation = true;
            MarkPaintDirty();
            return;
        }
        switch (evt.Key)
        {
            case KeyCode.C when ctrl:
                // A grouped label's copy spans the whole group; the window handles it (it
                // tracks the group holding the live selection, which outlives any one
                // label), so don't also copy just this line.
                if (!InGroup && HasSelection) Clipboard?.SetText(SelectedText);
                break;
            case KeyCode.A when ctrl:
                SelectAll();
                MarkPaintDirty();
                break;
        }
    }

    /// <summary>
    /// Shift+navigation over the selection, the way a text editor moves its caret. Grouped,
    /// the loose end travels through the whole model — past lines that were never realized —
    /// because the group holds it as a line number rather than as a position in an element.
    /// Ungrouped there is only this one line to move along. Returns false for keys that mean
    /// nothing here, which then carry on to whoever else wants them.
    /// </summary>
    private bool ExtendSelectionByKey(KeyCode key, bool ctrl)
    {
        if (InGroup)
        {
            var g = SelectionGroup!;
            // A label that has focus but no live anchor — clicked once, or arrived at by Tab
            // — starts the selection where its own caret sits.
            if (g.FocusIndex < 0) g.Anchor(SelectionIndex, _caret);
            return key switch
            {
                KeyCode.Left => g.ExtendByCharacter(-1),
                KeyCode.Right => g.ExtendByCharacter(1),
                KeyCode.Up => g.ExtendByLine(-1),
                KeyCode.Down => g.ExtendByLine(1),
                KeyCode.PageUp => g.ExtendByLine(-LinesPerPage()),
                KeyCode.PageDown => g.ExtendByLine(LinesPerPage()),
                KeyCode.Home => ctrl ? g.ExtendToDocumentEdge(-1) : g.ExtendToLineEdge(-1),
                KeyCode.End => ctrl ? g.ExtendToDocumentEdge(1) : g.ExtendToLineEdge(1),
                _ => false,
            };
        }

        int caret = key switch
        {
            KeyCode.Left => _caret - 1,
            KeyCode.Right => _caret + 1,
            KeyCode.Home or KeyCode.PageUp => 0,
            KeyCode.End or KeyCode.PageDown => _content.Length,
            _ => -1,
        };
        if (caret < 0 || caret > _content.Length) return false;
        if (caret == _caret) return true;   // already at the edge; still ours to swallow
        _caret = SnapToBoundary(caret);
        return true;
    }

    /// <summary>
    /// How many lines a page key should cover: what fits in the scrolling ancestor, less one
    /// row of overlap, the way editors page. Falls back to a screenful-ish constant before
    /// layout has run or outside a scroller.
    /// </summary>
    private int LinesPerPage()
    {
        float rowHeight = Layout.AbsoluteBorderBox.Height;
        for (var e = Parent; e is not null; e = e.Parent)
        {
            if (e is not ScrollViewElement scroller) continue;
            float viewport = scroller.Layout.AbsoluteContentBox.Height;
            if (rowHeight > 0 && viewport > rowHeight)
                return Math.Max(1, (int)(viewport / rowHeight) - 1);
            break;
        }
        return 20;
    }

    /// <summary>Called by the window to extend the selection while the pointer is captured outside this label.</summary>
    public void UpdateDragPosition(Point position)
    {
        if (!_isDragging) return;
        DragTo(position);
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
            SelectionIndex = text.SelectionIndex;
            // Links and their handler come from the freshly built element, like any other
            // app-supplied handler: a closure from the previous build captures stale state.
            Links = text.Links;
            LinkActivated = text.LinkActivated;
        }
    }

    // Measured under the mode it will be drawn with. Clip and Ellipsis truncate
    // differently, so measuring under one and painting under the other reports a width
    // the element never occupies — and couples every Ellipsis label to changes in the
    // Clip path, which is how a clipping fix once resized cards that never clip.
    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) =>
        // Spans ride along so metric-affecting ones (bold, italic, size) size the
        // element the way they will draw; the overload ignores paint-only lists.
        ctx.MeasureText(Content, ResolvedInherited, availableWidth, MaxLines, Overflow, _spans);

    // The widest word, not the widest character: measuring text at zero available
    // width wraps it one character per line, which under-reports the width below
    // which the text really breaks.
    internal override float MeasureMinContentWidth(IMeasureContext ctx) =>
        string.IsNullOrEmpty(Content)
            ? 0f
            : ctx.MinContentTextWidth(Content, ResolvedInherited, _spans);

    // Drawn from the top of the content box, wrapped at its width.
    internal override float? MeasureBaseline(Size borderBox, IMeasureContext ctx) =>
        string.IsNullOrEmpty(Content)
            ? null
            : ContentTop + ctx.MeasureFirstBaseline(Content, ResolvedInherited, ContentSizeOf(borderBox).Width,
                MaxLines, Overflow, _spans);

    public override void Render(IRenderContext ctx)
    {
        // A label needs the drawn line layout kept only if something will hit-test against
        // it: a selection, or a link. Links count as much as selection does — without the
        // cache a click cannot be mapped to a character offset, so a link on an otherwise
        // plain label would simply never fire.
        bool hitTestable = _selectable || _links is { Count: > 0 };

        if (string.IsNullOrEmpty(Content))
        {
            if (hitTestable) { _renderCtx = ctx; _lines = []; }
            return;
        }

        var contentBox = Layout.AbsoluteContentBox;
        var style = ResolvedInherited;

        if (hitTestable)
        {
            _renderCtx = ctx;
            _contentBox = contentBox;
            _lines = ctx.LayoutTextLines(Content, style, contentBox.Width, MaxLines);
            if (_selectable && HasSelection)
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

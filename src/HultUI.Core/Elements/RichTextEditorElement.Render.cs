using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.RichText;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public partial class RichTextEditorElement
{
    private float _scrollY;
    private bool _needsScrollToCaret;
    private readonly Scrollbar _bar = new();
    private readonly List<Rect> _boxes = [];

    // Where the caret is drawn when a wrapped line's end and the next line's start are the same
    // offset: true after End or a click past a line's end, so the caret stays where it was put.
    private bool _upstream;

    /// <summary>The box's — the document is clipped to it — while the scrollbar fits inside it.</summary>
    public override Rect? PaintBounds(IRenderContext ctx) =>
        _bar.PaintsInside(Layout.AbsoluteContentBox, vertical: true) ? base.PaintBounds(ctx) : null;

    public override void Render(IRenderContext ctx)
    {
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
        TextFieldUnderline.Draw(ctx, Layout.AbsoluteBorderBox, CornerRadius,
            (_focused ? FocusUnderlineColor ?? UnderlineColor : UnderlineColor) ?? Color.Transparent,
            _focused ? FocusUnderlineThickness : UnderlineThickness);

        var content = Layout.AbsoluteContentBox;
        EnsureLayout(ctx, content.Width);

        if (_needsScrollToCaret)
        {
            _needsScrollToCaret = false;
            ScrollCaretIntoView(content.Height);
        }
        _scrollY = Math.Clamp(_scrollY, 0, MaxScrollY());

        ctx.PushClip(content);
        float originX = content.X, originY = content.Y - _scrollY;

        int first = BlockAt(_scrollY);
        if (first > 0 && _blocks[first].Top > _scrollY) first--;
        float bottom = _scrollY + content.Height;

        var selection = _state.Selection;
        for (int i = first; i < _blocks.Length; i++)
        {
            var bl = _blocks[i];
            if (bl.Top - bl.SpaceAbove > bottom) break;
            DrawChrome(ctx, i, bl, originX, originY, content.Width);
            if (!selection.IsCollapsed) DrawSelection(ctx, i, bl, selection, originX, originY, content.Width);
            if (bl.Paragraph is { } para)
                ctx.DrawParagraph(para, new Point(originX + bl.TextX, originY + bl.Top + bl.TextY));
        }

        if (IsEmpty && Placeholder.Length > 0 && !_composition.IsActive && _blocks.Length > 0)
        {
            var bl = _blocks[0];
            var style = bl.Style with { TextColor = PlaceholderColor };
            ctx.DrawText(Placeholder, new Rect(originX + bl.TextX, originY + bl.Top + bl.TextY,
                Math.Max(0, content.Width - bl.TextX), content.Height), style, TextOverflow.Clip, null);
        }

        if (_focused && !_readOnly)
            DrawCaret(ctx, originX, originY);

        ctx.Pop();
        _bar.Paint(ctx, content, _documentHeight, _scrollY, vertical: true);
    }

    /// <summary>One empty paragraph: nothing typed yet.</summary>
    private bool IsEmpty => _state.Document is { BlockCount: 1 } d && d[0] is { Kind: BlockKind.Paragraph, Text.Length: 0 };

    // ── Block chrome: list markers, check boxes, quote bars, code backgrounds, rules ──

    private void DrawChrome(IRenderContext ctx, int index, BlockLayout bl, float ox, float oy, float width)
    {
        var block = bl.Block;
        float indent = block.Indent * _rt.IndentWidth;
        float top = oy + bl.Top;

        switch (block.Kind)
        {
            case BlockKind.CodeBlock or BlockKind.Raw:
                ctx.DrawRoundedRect(new Rect(ox + indent, top, Math.Max(0, width - indent), bl.Height),
                    new CornerRadius(_rt.CodeBlockCornerRadius), _codeBackground, Color.Transparent, Thickness.Zero);
                break;

            case BlockKind.Quote:
            {
                // A run of quotes is one bar, carried across the gaps between them.
                bool joined = index > 0 && _blocks[index - 1].Block.Kind == BlockKind.Quote;
                float from = joined ? top - bl.SpaceAbove : top;
                ctx.DrawRect(new Rect(ox + indent, from, _rt.QuoteBarWidth, top + bl.Height - from), _quoteBar);
                break;
            }

            case BlockKind.Rule:
            {
                float y = top + (bl.Height - _rt.RuleThickness) / 2;
                ctx.DrawRect(new Rect(ox + indent, y, Math.Max(0, width - indent), _rt.RuleThickness), _rule);
                break;
            }

            case BlockKind.BulletItem or BlockKind.NumberedItem or BlockKind.TaskItem:
                DrawMarker(ctx, bl, ox + indent, top);
                break;
        }
    }

    // A list item's marker in the column before its text, centred on the first line.
    private void DrawMarker(IRenderContext ctx, BlockLayout bl, float columnX, float top)
    {
        if (bl.Paragraph is not { } para) return;
        var block = bl.Block;
        var firstLine = para.Lines[0];
        float lineTop = top + bl.TextY + firstLine.OffsetY;
        float baseline = top + bl.TextY + para.Baseline(0);
        float size = bl.Style.FontSize;
        float column = _rt.IndentWidth;

        switch (block.Kind)
        {
            case BlockKind.BulletItem:
            {
                float d = Math.Max(3f, size * _rt.BulletScale);
                float cx = columnX + column * 0.5f;
                // On the x-height's middle, roughly a third of the size above the baseline.
                float cy = baseline - size * 0.32f;
                var rect = new Rect(cx - d / 2, cy - d / 2, d, d);
                var color = bl.Style.TextColor;
                switch (block.Indent % 3)
                {
                    case 0:
                        ctx.DrawRoundedRect(rect, new CornerRadius(d / 2), color, Color.Transparent, Thickness.Zero);
                        break;
                    case 1:
                        ctx.DrawRoundedRect(rect, new CornerRadius(d / 2), Color.Transparent, color, new Thickness(1f));
                        break;
                    default:
                        ctx.DrawRect(rect, color);
                        break;
                }
                break;
            }

            case BlockKind.NumberedItem:
            {
                string label = bl.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
                var style = bl.Style with { TextAlign = TextAlign.Start, Decoration = TextDecoration.None };
                float w = ctx.MeasureText(label, style).Width;
                // Right-aligned against the text, as a word processor sets them, so 9. and 10. line up.
                float x = columnX + column - w - Math.Max(4f, size * 0.3f);
                ctx.DrawText(label, new Rect(x, lineTop, w + 1, firstLine.Height), style, TextOverflow.Clip, 1);
                break;
            }

            case BlockKind.TaskItem:
            {
                float s = Math.Max(8f, size * _rt.CheckboxScale);
                var box = CheckboxRect(columnX, column, lineTop, firstLine.Height, s);
                if (block.Checked)
                {
                    ctx.DrawRoundedRect(box, new CornerRadius(3f), _checked, Color.Transparent, Thickness.Zero);
                    Span<Point> tick =
                    [
                        new(box.X + s * 0.22f, box.Y + s * 0.52f),
                        new(box.X + s * 0.42f, box.Y + s * 0.72f),
                        new(box.X + s * 0.78f, box.Y + s * 0.3f),
                    ];
                    ctx.DrawPolyline(tick, Color.White, Math.Max(1.5f, s * 0.12f));
                }
                else
                    ctx.DrawRoundedRect(box, new CornerRadius(3f), Color.Transparent, _checkBorder, new Thickness(1f));
                break;
            }
        }
    }

    private static Rect CheckboxRect(float columnX, float column, float lineTop, float lineHeight, float size) =>
        new(columnX + (column - size) / 2 - 2f, lineTop + (lineHeight - size) / 2, size, size);

    // ── Selection ──

    private void DrawSelection(IRenderContext ctx, int index, BlockLayout bl, RichSelection selection,
        float ox, float oy, float width)
    {
        var s = selection.Start;
        var e = selection.End;
        if (index < s.Block || index > e.Block) return;

        if (bl.Paragraph is not { } para)
        {
            // A rule is selected whole, when the selection starts at or before it and goes on past.
            if (s <= new RichPosition(index, 0) && e.Block > index)
                ctx.DrawRect(new Rect(ox + bl.TextX, oy + bl.Top, Math.Max(0, width - bl.TextX), bl.Height), SelectionColor);
            return;
        }

        int from = index == s.Block ? s.Offset : 0;
        // Carrying on into the next block selects this one's end too: one past its length.
        int to = index == e.Block ? e.Offset : bl.Block.Text.Length + 1;

        _boxes.Clear();
        para.SelectionBoxes(from, to, _boxes, bl.Style.FontSize * 0.35f);
        float x0 = ox + bl.TextX, y0 = oy + bl.Top + bl.TextY;
        foreach (var r in _boxes)
            ctx.DrawRect(new Rect(x0 + r.X, y0 + r.Y, r.Width, r.Height), SelectionColor);
    }

    // ── Caret and the input method's text ──

    private void DrawCaret(IRenderContext ctx, float ox, float oy)
    {
        var focus = _state.Selection.Focus;
        var rect = CaretRect(focus, _upstream);
        float x = ox + rect.X, y = oy + rect.Y, h = rect.Height;

        if (_composition.IsActive && _blocks.Length > focus.Block)
        {
            var style = _blocks[focus.Block].Style;
            string preedit = _composition.Text;
            float w = ctx.MeasureText(preedit, style).Width;
            ctx.DrawRect(new Rect(x, y, w, h), BackgroundColor);
            ctx.DrawText(preedit, new Rect(x, y, w + 1, h), style, TextOverflow.Clip, 1);
            ctx.DrawRect(new Rect(x, y + h - 1f, w, 1f), CursorColor);
            if (_composition.SegmentLength > 0)
            {
                float segX = _composition.SegmentStart > 0 ? ctx.MeasureText(preedit[.._composition.SegmentStart], style).Width : 0;
                float segEnd = ctx.MeasureText(preedit[..(_composition.SegmentStart + _composition.SegmentLength)], style).Width;
                ctx.DrawRect(new Rect(x + segX, y + h - 2f, segEnd - segX, 2f), CursorColor);
            }
            int caretChars = _composition.SegmentStart == 0 && _composition.SegmentLength == 0
                ? preedit.Length
                : _composition.SegmentStart + _composition.SegmentLength;
            x += caretChars > 0 ? ctx.MeasureText(preedit[..caretChars], style).Width : 0;
        }

        var caret = new Rect(x, y, CursorWidth, h);
        ctx.DrawRect(caret, CursorColor);
        TextInputHost?.SetInputArea(caret, 0);
    }

    // ── Scrolling ──

    private float MaxScrollY() => Math.Max(0, _documentHeight - Layout.AbsoluteContentBox.Height);

    /// <summary>Current vertical scroll offset in pixels, clamped to the scrollable range.</summary>
    public float ScrollOffsetY
    {
        get => _scrollY;
        set
        {
            float clamped = Math.Clamp(value, 0, MaxScrollY());
            if (clamped == _scrollY) return;
            _scrollY = clamped;
            MarkPaintDirty();
        }
    }

    private void ScrollCaretIntoView(float viewport)
    {
        var caret = CaretRect(_state.Selection.Focus, _upstream);
        if (caret.Y < _scrollY) _scrollY = caret.Y;
        else if (caret.Bottom > _scrollY + viewport) _scrollY = caret.Bottom - viewport;
    }

    /// <inheritdoc cref="ScrollViewElement.MiddleClickAutoScroll"/>
    public bool MiddleClickAutoScroll { get; set; } = true;

    public bool CanAutoScroll => _documentHeight > Layout.AbsoluteContentBox.Height + 0.5f;

    public bool ScrollBy(float dx, float dy)
    {
        float before = _scrollY;
        ScrollOffsetY = before + dy;
        return _scrollY != before;
    }

    private bool _draggingScrollbar;
    private float _scrollbarGrabOffset;

    public bool IsDraggingScrollbar => _draggingScrollbar;

    public bool TryStartScrollbarDrag(Point position)
    {
        var hit = _bar.HitTest(Layout.AbsoluteContentBox, _documentHeight, _scrollY, position,
            vertical: true, out float grab, out float trackOffset);
        switch (hit)
        {
            case ScrollbarHit.Thumb:
                _draggingScrollbar = true;
                _scrollbarGrabOffset = grab;
                return true;
            case ScrollbarHit.Track:
                _scrollY = trackOffset;
                MarkPaintDirty();
                return true;
            default:
                return false;
        }
    }

    public bool IsPointOverScrollbar(Point position) =>
        _bar.Contains(Layout.AbsoluteContentBox, _documentHeight, position, vertical: true);

    public void SetScrollbarHover(Point? position) =>
        _bar.SetHover(position is { } p && IsPointOverScrollbar(p), this);

    public void UpdateScrollbarDrag(Point position)
    {
        if (!_draggingScrollbar) return;
        _scrollY = _bar.OffsetFromDrag(Layout.AbsoluteContentBox, _documentHeight, position, _scrollbarGrabOffset,
            vertical: true);
        MarkPaintDirty();
    }

    public void EndScrollbarDrag() => _draggingScrollbar = false;
}

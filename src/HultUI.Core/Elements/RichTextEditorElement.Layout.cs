using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.RichText;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public partial class RichTextEditorElement
{
    // ── The look of a document, from the theme ──

    private RichTextTheme _rt = new();
    private Color _linkColor, _codeBackground, _quoteBar, _quoteText, _checked, _checkedText, _rule, _rawText;
    private Color _checkBorder;
    private int _themeStamp;

    private void ApplyRichTheme(Theme theme)
    {
        var rt = theme.RichText;
        var link = rt.LinkColor ?? theme.Primary;
        // Tints of the text over whatever the field is, rather than palette fills: a surface or
        // border colour can be the field's own, and a code background or quote bar drawn in it
        // then does not show at all.
        var code = rt.CodeBackground ?? Tint(theme.TextColor, 22);
        var bar = rt.QuoteBarColor ?? Tint(theme.TextColor, 70);
        var quote = rt.QuoteTextColor ?? theme.Secondary;
        var check = rt.CheckedColor ?? theme.Primary;
        var checkedText = rt.CheckedTextColor ?? theme.Secondary;
        var rule = rt.RuleColor ?? Tint(theme.TextColor, 48);
        var raw = rt.RawTextColor ?? theme.Secondary;
        var checkBorder = theme.Secondary;

        if (ReferenceEquals(rt, _rt) && link == _linkColor && code == _codeBackground && bar == _quoteBar
            && quote == _quoteText && check == _checked && checkedText == _checkedText && rule == _rule
            && raw == _rawText && checkBorder == _checkBorder)
            return;

        (_rt, _linkColor, _codeBackground, _quoteBar, _quoteText) = (rt, link, code, bar, quote);
        (_checked, _checkedText, _rule, _rawText, _checkBorder) = (check, checkedText, rule, raw, checkBorder);
        _themeStamp++;
        MarkPaintDirty();
    }

    /// <summary>The style a block's text is laid out in: the element's, sized and weighted for a
    /// heading, in the code face for code, muted for a quote or a done task.</summary>
    private ResolvedInheritedStyle BlockStyle(RichBlock block, ResolvedInheritedStyle body)
    {
        var style = body with
        {
            TextAlign = block.Align switch
            {
                BlockAlign.Center => TextAlign.Center,
                BlockAlign.End => TextAlign.End,
                _ => TextAlign.Start,
            },
        };
        switch (block.Kind)
        {
            case BlockKind.Heading:
                return style with
                {
                    FontSize = body.FontSize * _rt.HeadingScaleOf(block.HeadingLevel),
                    FontWeight = _rt.HeadingWeight,
                };
            case BlockKind.CodeBlock:
                return style with { FontFamily = _rt.CodeFamily, FontSize = body.FontSize * _rt.CodeScale, TextAlign = TextAlign.Start };
            case BlockKind.Raw:
                return style with
                {
                    FontFamily = _rt.CodeFamily, FontSize = body.FontSize * _rt.CodeScale,
                    TextColor = _rawText, TextAlign = TextAlign.Start,
                };
            case BlockKind.Quote:
                return style with { TextColor = _quoteText };
            case BlockKind.TaskItem when block.Checked:
                return style with
                {
                    TextColor = _checkedText,
                    Decoration = _rt.StrikeCheckedText ? TextDecoration.LineThrough : style.Decoration,
                };
            default:
                return style;
        }
    }

    /// <summary>A block's marks as the spans its paragraph is shaped and drawn with: what each
    /// mark looks like is decided here, from the theme.</summary>
    private List<TextSpan>? SpansOf(RichBlock block, ResolvedInheritedStyle style)
    {
        if (block.IsPreformatted || block.Spans.Length == 0) return null;
        var spans = new List<TextSpan>(block.Spans.Length);
        foreach (var span in block.Spans)
        {
            var m = span.Marks;
            bool code = m.Has(InlineStyle.Code);
            var decoration = TextDecoration.None;
            if (m.Has(InlineStyle.Underline) || m.Link is not null) decoration |= TextDecoration.Underline;
            if (m.Has(InlineStyle.Strike)) decoration |= TextDecoration.LineThrough;

            spans.Add(new TextSpan(
                span.Start, span.Length,
                m.Link is not null ? _linkColor : m.Color ?? (m.Highlight is { } marker ? TextOn(marker) : default),
                m.Highlight ?? (code ? _codeBackground : default),
                decoration,
                m.Has(InlineStyle.Bold) ? FontWeight.Bold : null,
                m.Has(InlineStyle.Italic) ? FontSlant.Italic : null,
                m.Size ?? (code ? style.FontSize * _rt.CodeScale : null),
                m.Family ?? (code ? _rt.CodeFamily : null)));
        }
        return spans;
    }

    private static Color Tint(Color c, byte alpha) => new(c.R, c.G, c.B, alpha);

    // Text on a highlight whose own colour was not chosen: dark on a light marker, light on a
    // dark one. The theme's text colour would put white on yellow in a dark theme.
    private static Color TextOn(Color background)
    {
        float luminance = (0.2126f * background.R + 0.7152f * background.G + 0.0722f * background.B) / 255f;
        return luminance > 0.55f ? new Color(17, 17, 17) : new Color(250, 250, 250);
    }

    // ── Block layout ──

    /// <summary>One block as laid out: where it sits and where its text starts.</summary>
    private sealed class BlockLayout
    {
        public required RichBlock Block;
        /// <summary>Null for a rule, which has no text.</summary>
        public required TextParagraph? Paragraph;
        public required ResolvedInheritedStyle Style;
        /// <summary>Top of the block, from the top of the document.</summary>
        public float Top;
        /// <summary>The gap above it, between it and the block before.</summary>
        public float SpaceAbove;
        public float Height;
        /// <summary>Where its text starts, from the content box's left edge.</summary>
        public required float TextX;
        /// <summary>Where its text starts, from the block's top: a code block's padding.</summary>
        public required float TextY;
        /// <summary>A numbered item's number.</summary>
        public int Number;

        public float Bottom => Top + Height;
    }

    private BlockLayout[] _blocks = [];
    private RichDocument? _laidOutDocument;
    private float _laidOutWidth = -1;
    private int _laidOutStamp = -1;
    private ResolvedInheritedStyle _laidOutBody;
    private float _documentHeight;
    private IRenderContext? _renderCtx;

    /// <summary>Lays the document out at <paramref name="width"/> unless it already is. A block
    /// laid out before at the same width, in the same style, keeps its paragraph.</summary>
    private void EnsureLayout(IRenderContext ctx, float width)
    {
        var doc = _state.Document;
        var body = ResolvedInherited;
        bool sameFrame = width == _laidOutWidth && _themeStamp == _laidOutStamp && body == _laidOutBody
            && ReferenceEquals(ctx, _renderCtx);
        if (sameFrame && ReferenceEquals(doc, _laidOutDocument)) return;

        Dictionary<RichBlock, BlockLayout>? reuse = null;
        if (sameFrame && _blocks.Length > 0)
        {
            reuse = new Dictionary<RichBlock, BlockLayout>(_blocks.Length, ReferenceEqualityComparer.Instance);
            foreach (var bl in _blocks) reuse.TryAdd(bl.Block, bl);
        }

        _renderCtx = ctx;
        var blocks = new BlockLayout[doc.BlockCount];
        Span<int> counters = stackalloc int[RichBlock.MaxIndent + 2];
        float y = 0;
        for (int i = 0; i < blocks.Length; i++)
        {
            var block = doc[i];
            var bl = reuse is not null && reuse.Remove(block, out var kept) ? kept : Build(ctx, block, width, body);
            bl.SpaceAbove = i == 0 ? 0 : SpaceBetween(doc[i - 1], block, bl.Style.FontSize);
            y += bl.SpaceAbove;
            bl.Top = y;
            y += bl.Height;
            bl.Number = NumberOf(block, counters);
            blocks[i] = bl;
        }

        _blocks = blocks;
        _laidOutDocument = doc;
        _laidOutWidth = width;
        _laidOutStamp = _themeStamp;
        _laidOutBody = body;
        _documentHeight = y;
    }

    private BlockLayout Build(IRenderContext ctx, RichBlock block, float width, ResolvedInheritedStyle body)
    {
        var style = BlockStyle(block, body);
        float indent = block.Indent * _rt.IndentWidth;
        float textX = indent, textY = 0, padRight = 0, padBottom = 0;

        switch (block.Kind)
        {
            case BlockKind.Rule:
                return new BlockLayout
                {
                    Block = block, Paragraph = null, Style = style, TextX = indent, TextY = 0, Height = _rt.RuleHeight,
                };
            case BlockKind.BulletItem or BlockKind.NumberedItem or BlockKind.TaskItem:
                textX = indent + _rt.IndentWidth;
                break;
            case BlockKind.Quote:
                textX = indent + _rt.QuoteBarWidth + _rt.QuoteGap;
                break;
            case BlockKind.CodeBlock or BlockKind.Raw:
                var pad = _rt.CodeBlockPadding;
                textX = indent + pad.Left;
                textY = pad.Top;
                padRight = pad.Right;
                padBottom = pad.Bottom;
                break;
        }

        float textWidth = Math.Max(1f, width - textX - padRight);
        var paragraph = ctx.LayoutParagraph(block.Text, style, textWidth, SpansOf(block, style));
        return new BlockLayout
        {
            Block = block, Paragraph = paragraph, Style = style, TextX = textX, TextY = textY,
            Height = textY + paragraph.Size.Height + padBottom,
        };
    }

    // Between two list items, a little; above a heading, room for it to stand apart; otherwise
    // the space after a paragraph.
    private float SpaceBetween(RichBlock previous, RichBlock block, float fontSize)
    {
        if (block.Kind == BlockKind.Heading)
            return Math.Max(_rt.BlockSpacing, _rt.HeadingSpaceAbove * fontSize);
        bool listy = previous.IsListItem || (previous.Kind == BlockKind.Paragraph && previous.Indent > 0);
        if (listy && (block.IsListItem || (block.Kind == BlockKind.Paragraph && block.Indent > 0)))
            return _rt.ListItemSpacing;
        if (previous.Kind == BlockKind.Quote && block.Kind == BlockKind.Quote)
            return _rt.ListItemSpacing;
        return _rt.BlockSpacing;
    }

    // Numbering runs per depth: an item continues the count of the items before it at its depth
    // until a block at a shallower depth (or a different list) ends it.
    private static int NumberOf(RichBlock block, Span<int> counters)
    {
        int depth = Math.Clamp(block.Indent, 0, counters.Length - 1);
        if (block.IsListItem)
        {
            counters[(depth + 1)..].Clear();
            if (block.Kind == BlockKind.NumberedItem) return ++counters[depth];
            counters[depth] = 0;
            return 0;
        }
        // A paragraph continuing an item at depth - 1 keeps that item's count; anything else
        // at depth 0 ends every list.
        counters[(block.Kind == BlockKind.Paragraph ? depth : 0)..].Clear();
        return 0;
    }

    /// <summary>
    /// Where a caret at <paramref name="position"/> is drawn, in window coordinates and as
    /// scrolled now: what a link editor or a suggestion list is placed against. Empty before the
    /// editor has been drawn.
    /// </summary>
    public Rect CaretBounds(RichPosition position)
    {
        if (!EnsureFreshLayout()) return default;
        var caret = CaretRect(position, upstream: false);
        var content = WindowContentBox;
        return new Rect(content.X + caret.X, content.Y + caret.Y - _scrollY, CursorWidth, caret.Height);
    }

    // ── Geometry: positions ⇄ points, in document coordinates (before scrolling) ──

    /// <summary>The caret at <paramref name="p"/> as a zero-width box, from the content box's
    /// top-left at no scroll.</summary>
    private Rect CaretRect(RichPosition p, bool upstream)
    {
        if (_blocks.Length == 0) return default;
        p = _state.Document.Clamp(p);
        var bl = _blocks[Math.Min(p.Block, _blocks.Length - 1)];
        if (bl.Paragraph is not { } para)
        {
            float h = bl.Style.FontSize * 1.3f;
            return new Rect(bl.TextX, bl.Top + (bl.Height - h) / 2, 0, h);
        }
        var box = para.CaretBox(Math.Min(p.Offset, para.Text.Length), upstream);
        return new Rect(bl.TextX + box.X, bl.Top + bl.TextY + box.Y, 0, box.Height);
    }

    /// <summary>The block at document y <paramref name="y"/>: in a gap between two, the nearer.</summary>
    private int BlockAt(float y)
    {
        int lo = 0, hi = _blocks.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            var bl = _blocks[mid];
            if (bl.Top - bl.SpaceAbove / 2 <= y) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>
    /// The caret position nearest document point (<paramref name="x"/>, <paramref name="y"/>),
    /// and whether it belongs at the end of its line rather than the start of the next — past the
    /// end of a line that wraps.
    /// </summary>
    private (RichPosition Position, bool Upstream) PositionAt(float x, float y)
    {
        if (_blocks.Length == 0) return (default, false);
        int b = BlockAt(y);
        var bl = _blocks[b];
        if (bl.Paragraph is not { } para) return (new RichPosition(b, 0), false);

        int line = para.LineAt(y - bl.Top - bl.TextY);
        int offset = para.OffsetAtX(line, x - bl.TextX);
        return (new RichPosition(b, offset), EndsWrappedLine(para, line, offset));
    }

    // An offset that is both the end of `line` and the start of the next is drawn on `line`
    // only when asked for — this says when to ask.
    private static bool EndsWrappedLine(TextParagraph para, int line, int offset)
    {
        var lines = para.Lines;
        if (line >= lines.Count - 1) return false;
        return offset == lines[line].Start + lines[line].Length && offset == lines[line + 1].Start;
    }

    /// <summary>The position a line up or down from <paramref name="from"/>, at document x
    /// <paramref name="x"/>; null past the document's ends.</summary>
    private (RichPosition Position, bool Upstream)? LineStep(RichPosition from, bool upstream, int direction, float x)
    {
        var bl = _blocks[from.Block];
        if (bl.Paragraph is { } para)
        {
            int line = para.LineOf(from.Offset, upstream) + direction;
            if (line >= 0 && line < para.Lines.Count)
            {
                int offset = para.OffsetAtX(line, x - bl.TextX);
                return (new RichPosition(from.Block, offset), EndsWrappedLine(para, line, offset));
            }
        }

        int next = from.Block + direction;
        if (next < 0 || next >= _blocks.Length) return null;
        var nb = _blocks[next];
        if (nb.Paragraph is not { } np) return (new RichPosition(next, 0), false);
        int target = direction < 0 ? np.Lines.Count - 1 : 0;
        int o = np.OffsetAtX(target, x - nb.TextX);
        return (new RichPosition(next, o), EndsWrappedLine(np, target, o));
    }

    /// <summary>The start and end of the visual line the caret is on.</summary>
    private (RichPosition Start, RichPosition End, bool EndUpstream) LineBounds(RichPosition p, bool upstream)
    {
        var bl = _blocks[p.Block];
        if (bl.Paragraph is not { } para) return (p, p, false);
        int line = para.LineOf(p.Offset, upstream);
        var box = para.Lines[line];
        int end = box.Start + box.Length;
        return (p with { Offset = box.Start }, p with { Offset = end }, EndsWrappedLine(para, line, end));
    }

    // ── Auto height ──

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        if (_autoHeight is not { } bounds) return base.MeasureContent(availableWidth, availableHeight, ctx);
        float width = float.IsFinite(availableWidth) ? availableWidth : 400f;
        // The bounds are the editor's own height; what is measured here is inside its padding and border.
        float chrome = Box.Padding.Vertical + Box.BorderWidth.Vertical;
        float min = Math.Max(0, bounds.Min - chrome), max = Math.Max(min, bounds.Max - chrome);
        return new Size(width, Math.Clamp(MeasureDocumentHeight(ctx, width), min, max));
    }

    // The height EnsureLayout would come to, from the measure context: each block's text
    // measured with its spans, plus the same padding and spacing.
    private float MeasureDocumentHeight(IMeasureContext ctx, float width)
    {
        var doc = _state.Document;
        var body = ResolvedInherited;
        float y = 0;
        for (int i = 0; i < doc.BlockCount; i++)
        {
            var block = doc[i];
            var style = BlockStyle(block, body);
            if (i > 0) y += SpaceBetween(doc[i - 1], block, style.FontSize);
            if (block.IsVoid) { y += _rt.RuleHeight; continue; }

            float indent = block.Indent * _rt.IndentWidth;
            float textX = indent, pads = 0, padRight = 0;
            if (block.IsListItem) textX = indent + _rt.IndentWidth;
            else if (block.Kind == BlockKind.Quote) textX = indent + _rt.QuoteBarWidth + _rt.QuoteGap;
            else if (block.IsPreformatted)
            {
                textX = indent + _rt.CodeBlockPadding.Left;
                padRight = _rt.CodeBlockPadding.Right;
                pads = _rt.CodeBlockPadding.Vertical;
            }
            float textWidth = Math.Max(1f, width - textX - padRight);
            // Empty text and a final newline each still take a line, as the paragraph does.
            string text = block.Text.Length == 0 || block.Text.EndsWith('\n') ? block.Text + (char)0x200B : block.Text;
            y += pads + ctx.MeasureText(text, style, textWidth, null, TextOverflow.Clip, SpansOf(block, style)).Height;
        }
        return y;
    }
}

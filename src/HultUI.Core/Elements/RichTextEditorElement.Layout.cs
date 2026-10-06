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
    private Color _checkBorder, _tableBorder, _tableHeader, _ring;
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
        var tableBorder = rt.TableBorderColor ?? Tint(theme.TextColor, 48);
        var tableHeader = rt.TableHeaderBackground ?? Tint(theme.TextColor, 14);
        var ring = rt.ImageRingColor ?? theme.Primary;

        if (ReferenceEquals(rt, _rt) && link == _linkColor && code == _codeBackground && bar == _quoteBar
            && quote == _quoteText && check == _checked && checkedText == _checkedText && rule == _rule
            && raw == _rawText && checkBorder == _checkBorder && tableBorder == _tableBorder && tableHeader == _tableHeader
            && ring == _ring)
            return;

        (_rt, _linkColor, _codeBackground, _quoteBar, _quoteText) = (rt, link, code, bar, quote);
        (_checked, _checkedText, _rule, _rawText, _checkBorder) = (check, checkedText, rule, raw, checkBorder);
        (_tableBorder, _tableHeader, _ring) = (tableBorder, tableHeader, ring);
        _themeStamp++;
        MarkPaintDirty();
    }

    /// <summary>The style a block's text is laid out in: the element's, sized and weighted for a
    /// heading, in the code face for code, muted for a quote or a done task, weighted for a
    /// table's header.</summary>
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
            case BlockKind.TableCell when block.Row == 0:
                return style with { FontWeight = _rt.TableHeaderWeight };
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
        /// <summary>Null for a rule or a picture, which have no text.</summary>
        public required TextParagraph? Paragraph;
        public required ResolvedInheritedStyle Style;
        /// <summary>Top of the block, from the top of the document. A cell's is its row's, inside
        /// the grid line above it.</summary>
        public float Top;
        /// <summary>The gap above it, between it and the block before.</summary>
        public float SpaceAbove;
        /// <summary>A cell's is its row's.</summary>
        public float Height;
        /// <summary>Where its text starts, from the content box's left edge.</summary>
        public required float TextX;
        /// <summary>Where its text starts, from the block's top: a code block's or a cell's padding.</summary>
        public required float TextY;
        /// <summary>A numbered item's number.</summary>
        public int Number;

        /// <summary>The table a cell is in; null for any other block.</summary>
        public TableLayout? Table;
        public int CellRow, CellColumn;
        /// <summary>A cell's text on one line, unwrapped: its column's claim on the width.</summary>
        public float NaturalWidth = -1;

        /// <summary>A picture's source as it was resolved, and where it is drawn: x from the
        /// content box's left edge, y from the block's top.</summary>
        public IImageSource? Picture;
        public bool PictureLoaded;
        public Size PictureIntrinsic;
        public Rect PictureRect;
        /// <summary>The resolver answers differently now: lay the block out again.</summary>
        public bool Stale;

        public float Bottom => Top + Height;
    }

    /// <summary>A table's grid: the x of each vertical line and the y of each horizontal one,
    /// the last of each the far edge. A line is <see cref="RichTextTheme.TableBorderWidth"/> thick
    /// from its coordinate; a cell lies between two.</summary>
    private sealed class TableLayout
    {
        public required RichTableRange Range;
        public required float[] ColumnX;
        public required float[] RowY;
        /// <summary>The table's top, from the top of the document.</summary>
        public float Top;
        public float Height => RowY[^1] + Border;
        public required float Border;
    }

    private BlockLayout[] _blocks = [];
    private readonly List<BlockLayout> _pictures = [];
    private RichDocument? _laidOutDocument;
    private float _laidOutWidth = -1;
    private int _laidOutStamp = -1;
    private ResolvedInheritedStyle _laidOutBody;
    private float _documentHeight;
    private IRenderContext? _renderCtx;

    /// <summary>Lays the document out at <paramref name="width"/> unless it already is. A block
    /// laid out before at the same width, in the same style, keeps its paragraph; a table is laid
    /// out whole, keeping the paragraphs of cells whose text and column did not change.</summary>
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
            foreach (var bl in _blocks)
                if (!bl.Stale) reuse.TryAdd(bl.Block, bl);
        }

        _renderCtx = ctx;
        _pictures.Clear();
        var blocks = new BlockLayout[doc.BlockCount];
        Span<int> counters = stackalloc int[RichBlock.MaxIndent + 2];
        float y = 0;
        for (int i = 0; i < blocks.Length; i++)
        {
            var block = doc[i];
            float space = i == 0 ? 0 : SpaceBetween(doc[i - 1], block, BlockStyle(block, body).FontSize);
            y += space;

            if (block.IsTableCell && doc.TableAt(i) is { } range)
            {
                var table = BuildTable(ctx, doc, range, width, body, reuse, blocks);
                table.Top = y;
                for (int k = range.Start; k < range.End; k++)
                {
                    var cell = blocks[k];
                    cell.Top = y + table.RowY[cell.CellRow] + table.Border;
                    cell.SpaceAbove = k == range.Start ? space : 0;
                    cell.Number = NumberOf(cell.Block, counters);
                }
                y += table.Height;
                i = range.End - 1;
                continue;
            }

            var bl = reuse is not null && reuse.Remove(block, out var kept) ? kept : Build(ctx, block, width, body);
            bl.SpaceAbove = space;
            bl.Top = y;
            y += bl.Height;
            bl.Number = NumberOf(block, counters);
            if (block.Kind == BlockKind.Image) _pictures.Add(bl);
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
            case BlockKind.Image:
                return BuildPicture(block, width, style, indent);
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

    // ── Pictures ──

    /// <summary>
    /// Asked for each picture's pixels: the app's loader, which knows its sources — a URL, an
    /// upload, a file. Null, or a source not loaded yet, draws the picture as a line naming it.
    /// Asked again on every rebuild, so a picture that arrives is drawn on the next one.
    /// </summary>
    public Func<string, IImageSource?>? ImageResolver
    {
        get => _imageResolver;
        set
        {
            _imageResolver = value;
            RefreshPictures();
        }
    }
    private Func<string, IImageSource?>? _imageResolver;

    private IImageSource? Resolve(RichImage? image) =>
        image is null || _imageResolver is null ? null : _imageResolver(image.Source);

    // Pictures whose resolver answers differently now are laid out again — those alone.
    private void RefreshPictures()
    {
        bool changed = false;
        foreach (var bl in _pictures)
        {
            var source = Resolve(bl.Block.Image);
            if (ReferenceEquals(source, bl.Picture) && (source?.IsLoaded == true) == bl.PictureLoaded
                && (source?.IntrinsicSize ?? default) == bl.PictureIntrinsic)
                continue;
            bl.Stale = true;
            changed = true;
        }
        if (!changed) return;
        _laidOutDocument = null;
        if (_autoHeight is not null) MarkLayoutDirty();
        MarkPaintDirty();
    }

    private BlockLayout BuildPicture(RichBlock block, float width, ResolvedInheritedStyle style, float indent)
    {
        var source = Resolve(block.Image);
        bool loaded = source is { IsLoaded: true };
        var size = PictureSize(block, source, width - indent, style);
        float x = indent + (block.Align switch
        {
            BlockAlign.Center => (width - indent - size.Width) / 2,
            BlockAlign.End => width - indent - size.Width,
            _ => 0,
        });
        return new BlockLayout
        {
            Block = block, Paragraph = null, Style = style, TextX = indent, TextY = 0, Height = size.Height,
            Picture = source, PictureLoaded = loaded, PictureIntrinsic = source?.IntrinsicSize ?? default,
            PictureRect = new Rect(MathF.Round(x), 0, size.Width, size.Height),
        };
    }

    /// <summary>A picture's size: scaled down to fit, never up, under the theme's ceiling; or, not
    /// loaded, a line the width of the column.</summary>
    private Size PictureSize(RichBlock block, IImageSource? source, float width, ResolvedInheritedStyle style)
    {
        if (source is { IsLoaded: true } && ImageElement.ScaledSize(source.IntrinsicSize, block.Image?.Width,
                Math.Max(1f, width), _rt.ImageMaxHeight) is { Width: > 0, Height: > 0 } scaled)
            return scaled;
        return new Size(Math.Max(1f, width), PlaceholderHeight(style));
    }

    private static float PlaceholderHeight(ResolvedInheritedStyle style) => MathF.Ceiling(style.FontSize * 2.4f);

    // ── Tables ──

    /// <summary>
    /// Lays out the table at <paramref name="range"/> into <paramref name="into"/>: each cell's
    /// one-line width claims a share of the full width — between the theme's floor and ceiling, as
    /// Junction's Markdown view shares it — and each row is as tall as its tallest cell.
    /// </summary>
    private TableLayout BuildTable(IRenderContext ctx, RichDocument doc, RichTableRange range, float width,
        ResolvedInheritedStyle body, Dictionary<RichBlock, BlockLayout>? reuse, BlockLayout[] into)
    {
        float border = _rt.TableBorderWidth;
        var pad = _rt.TableCellPadding;
        var naturals = new float[range.Columns];
        for (int k = range.Start; k < range.End; k++)
        {
            var block = doc[k];
            BlockLayout? kept = null;
            if (reuse is not null && reuse.Remove(block, out var old)) kept = old;
            var style = BlockStyle(block, body);
            float natural = kept is { NaturalWidth: >= 0 } ? kept.NaturalWidth
                : ctx.LayoutParagraph(block.Text, style, float.PositiveInfinity, SpansOf(block, style)).Size.Width;
            into[k] = kept ?? new BlockLayout { Block = block, Paragraph = null, Style = style, TextX = 0, TextY = pad.Top };
            into[k].NaturalWidth = natural;
            int column = range.ColumnOf(k);
            naturals[column] = Math.Max(naturals[column], natural);
        }

        var columnX = ColumnEdges(naturals, width, border, pad);
        var rowY = new float[range.Rows + 1];
        var table = new TableLayout { Range = range, ColumnX = columnX, RowY = rowY, Border = border };
        for (int r = 0; r < range.Rows; r++)
        {
            float tallest = 0;
            for (int c = 0; c < range.Columns; c++)
            {
                int k = range.IndexOf(r, c);
                var bl = into[k];
                float textWidth = Math.Max(1f, columnX[c + 1] - columnX[c] - border - pad.Horizontal);
                if (bl.Paragraph is not { } p || p.MaxWidth != textWidth)
                    bl.Paragraph = ctx.LayoutParagraph(bl.Block.Text, bl.Style, textWidth, SpansOf(bl.Block, bl.Style));
                bl.Table = table;
                bl.CellRow = r;
                bl.CellColumn = c;
                bl.TextX = columnX[c] + border + pad.Left;
                bl.TextY = pad.Top;
                tallest = Math.Max(tallest, pad.Vertical + bl.Paragraph.Size.Height);
            }
            for (int c = 0; c < range.Columns; c++) into[range.IndexOf(r, c)].Height = tallest;
            rowY[r + 1] = rowY[r] + border + tallest;
        }
        return table;
    }

    /// <summary>
    /// Where a table's vertical lines go: the columns share what the lines leave of
    /// <paramref name="width"/> by weight — each its widest cell on one line with padding, held
    /// between the theme's floor and ceiling — on whole pixels, the last line at the far edge.
    /// </summary>
    private float[] ColumnEdges(float[] naturals, float width, float border, Thickness pad)
    {
        int columns = naturals.Length;
        var weights = new float[columns];
        float total = 0;
        for (int c = 0; c < columns; c++)
            total += weights[c] = Math.Clamp(naturals[c] + pad.Horizontal, _rt.TableColumnMinWeight, _rt.TableColumnMaxWeight);
        float free = Math.Max(columns, width - (columns + 1) * border);
        var edges = new float[columns + 1];
        float x = 0;
        for (int c = 0; c < columns; c++)
        {
            edges[c] = MathF.Round(x);
            x += border + free * weights[c] / total;
        }
        edges[columns] = MathF.Max(edges[columns - 1] + border, MathF.Round(width - border));
        return edges;
    }

    // The column of `table` under document x `x`.
    private static int ColumnAt(TableLayout table, float x)
    {
        int c = 0;
        while (c + 1 < table.Range.Columns && x >= table.ColumnX[c + 1]) c++;
        return c;
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
    /// top-left at no scroll. On a picture, the picture's height: the caret there is the picture
    /// selected.</summary>
    private Rect CaretRect(RichPosition p, bool upstream)
    {
        if (_blocks.Length == 0) return default;
        p = _state.Document.Clamp(p);
        var bl = _blocks[Math.Min(p.Block, _blocks.Length - 1)];
        if (bl.Paragraph is not { } para)
        {
            if (bl.Block.Kind == BlockKind.Image)
                return new Rect(bl.PictureRect.X, bl.Top, 0, bl.Height);
            float h = bl.Style.FontSize * 1.3f;
            return new Rect(bl.TextX, bl.Top + (bl.Height - h) / 2, 0, h);
        }
        var box = para.CaretBox(Math.Min(p.Offset, para.Text.Length), upstream);
        return new Rect(bl.TextX + box.X, bl.Top + bl.TextY + box.Y, 0, box.Height);
    }

    /// <summary>The block at document y <paramref name="y"/>: in a gap between two, the nearer.
    /// In a table, the last cell of the row there.</summary>
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

    /// <summary>The block at document point (<paramref name="x"/>, <paramref name="y"/>): in a
    /// table, the cell under the point.</summary>
    private int BlockAt(float x, float y)
    {
        int b = BlockAt(y);
        if (_blocks[b].Table is not { } t) return b;
        return t.Range.IndexOf(_blocks[b].CellRow, ColumnAt(t, x));
    }

    /// <summary>
    /// The caret position nearest document point (<paramref name="x"/>, <paramref name="y"/>),
    /// and whether it belongs at the end of its line rather than the start of the next — past the
    /// end of a line that wraps.
    /// </summary>
    private (RichPosition Position, bool Upstream) PositionAt(float x, float y)
    {
        if (_blocks.Length == 0) return (default, false);
        int b = BlockAt(x, y);
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
    /// <paramref name="x"/>; null past the document's ends. In a table it moves by row in the
    /// same column, and into one it enters the cell under <paramref name="x"/>.</summary>
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
        if (bl.Table is { } table)
        {
            int row = bl.CellRow + direction;
            next = row >= 0 && row < table.Range.Rows
                ? table.Range.IndexOf(row, bl.CellColumn)
                : direction < 0 ? table.Range.Start - 1 : table.Range.End;
        }
        if (next < 0 || next >= _blocks.Length) return null;
        if (_blocks[next].Table is { } entered && !ReferenceEquals(entered, bl.Table))
            next = entered.Range.IndexOf(direction < 0 ? entered.Range.Rows - 1 : 0, ColumnAt(entered, x));

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
            if (block.Kind == BlockKind.Rule) { y += _rt.RuleHeight; continue; }
            if (block.Kind == BlockKind.Image)
            {
                y += PictureSize(block, Resolve(block.Image), width - block.Indent * _rt.IndentWidth, style).Height;
                continue;
            }
            if (block.IsTableCell && doc.TableAt(i) is { } range)
            {
                y += MeasureTableHeight(ctx, doc, range, width, body);
                i = range.End - 1;
                continue;
            }

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
            y += pads + MeasureTextHeight(ctx, block, style, Math.Max(1f, width - textX - padRight));
        }
        return y;
    }

    // BuildTable's arithmetic on the measure context.
    private float MeasureTableHeight(IMeasureContext ctx, RichDocument doc, RichTableRange range, float width,
        ResolvedInheritedStyle body)
    {
        float border = _rt.TableBorderWidth;
        var pad = _rt.TableCellPadding;
        var naturals = new float[range.Columns];
        for (int k = range.Start; k < range.End; k++)
        {
            var block = doc[k];
            var style = BlockStyle(block, body);
            float natural = ctx.MeasureText(block.Text, style, float.PositiveInfinity, null, TextOverflow.Clip, SpansOf(block, style)).Width;
            naturals[range.ColumnOf(k)] = Math.Max(naturals[range.ColumnOf(k)], natural);
        }
        var columnX = ColumnEdges(naturals, width, border, pad);
        float h = border;
        for (int r = 0; r < range.Rows; r++)
        {
            float tallest = 0;
            for (int c = 0; c < range.Columns; c++)
            {
                var block = doc[range.IndexOf(r, c)];
                float textWidth = Math.Max(1f, columnX[c + 1] - columnX[c] - border - pad.Horizontal);
                tallest = Math.Max(tallest, pad.Vertical + MeasureTextHeight(ctx, block, BlockStyle(block, body), textWidth));
            }
            h += tallest + border;
        }
        return h;
    }

    // Empty text and a final newline each still take a line, as the paragraph does.
    private float MeasureTextHeight(IMeasureContext ctx, RichBlock block, ResolvedInheritedStyle style, float width)
    {
        string text = block.Text.Length == 0 || block.Text.EndsWith('\n') ? block.Text + (char)0x200B : block.Text;
        return ctx.MeasureText(text, style, width, null, TextOverflow.Clip, SpansOf(block, style)).Height;
    }
}

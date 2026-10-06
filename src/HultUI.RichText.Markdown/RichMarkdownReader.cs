using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HultUI.Core.RichText;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace HultUI.RichText.Markdown;

/// <summary>
/// Markdown into a <see cref="RichDocument"/>. What the document can hold becomes blocks and
/// marks; what it cannot — a table, a picture, HTML, a list inside a quote — becomes a
/// <see cref="BlockKind.Raw"/> block holding its source exactly, so writing the document back
/// out loses none of it.
/// </summary>
internal static partial class RichMarkdownReader
{
    // CommonMark, with what GitLab adds that the document can hold (strikethrough, task lists)
    // and tables, which it cannot but must recognise as one block to keep them whole. No bare-URL
    // autolinks: a pasted address stays the text it was, and GitLab links it when it renders.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseTaskLists()
        .UsePipeTables()
        .UsePreciseSourceLocation()
        .Build();

    /// <summary>
    /// <paramref name="markdown"/> read as one paragraph's inline content: its text and marks, or
    /// null when it does not read as exactly one paragraph the document can hold. The writer
    /// checks what it wrote with this.
    /// </summary>
    public static (string Text, List<RichSpan> Spans)? ReadParagraph(string markdown)
    {
        var parsed = Markdig.Markdown.Parse(markdown, Pipeline);
        if (parsed.Count == 0) return ("", []);
        if (parsed.Count != 1 || parsed[0] is not ParagraphBlock { Inline: { } inline }) return null;
        var w = new Reader.InlineWriter();
        for (var node = inline.FirstChild; node is not null; node = node.NextSibling)
            if (!w.Walk(node, RichMarks.None)) return null;
        return (w.Text.ToString(), w.Spans);
    }

    public static RichDocument Read(string markdown)
    {
        string source = markdown.ReplaceLineEndings("\n");
        var parsed = Markdig.Markdown.Parse(source, Pipeline);
        var reader = new Reader(source);
        foreach (var block in parsed)
            reader.Block(block, listDepth: -1);
        return new RichDocument(reader.Blocks);
    }

    private sealed partial class Reader(string source)
    {
        public readonly List<RichBlock> Blocks = [];

        /// <summary>A top-level block, or one inside a list item at <paramref name="listDepth"/>
        /// (-1 outside any list): a paragraph there continues the item, one level in.</summary>
        public void Block(Block block, int listDepth)
        {
            int indent = listDepth + 1;
            switch (block)
            {
                case BlankLineBlock:
                    return;

                case HeadingBlock h when listDepth < 0 && h.Inline is { } inline && ReadInline(inline) is { } heading:
                    Blocks.Add(RichBlock.Heading(h.Level, heading.Text.Replace('\n', ' '), [.. heading.Spans]));
                    return;

                case ParagraphBlock p when p.Inline is { } inline && Picture(inline) is { } picture:
                    Blocks.Add(RichBlock.Picture(picture, indent));
                    return;

                case HtmlBlock html when PictureTag(html.Lines.ToString()) is { } tagged:
                    Blocks.Add(RichBlock.Picture(tagged, indent));
                    return;

                case Table table when listDepth < 0 && ReadTable(table) is { } cells:
                    Blocks.AddRange(cells);
                    return;

                case ParagraphBlock p when p.Inline is { } inline && ReadInline(inline) is { } text:
                    Blocks.Add(RichBlock.Paragraph(text.Text, [.. text.Spans]) with { Indent = indent });
                    return;

                case FencedCodeBlock fenced:
                    Blocks.Add(Code(fenced, Info(fenced), indent));
                    return;

                case CodeBlock code and not FencedCodeBlock:
                    Blocks.Add(Code(code, null, indent));
                    return;

                case ThematicBreakBlock:
                    Blocks.Add(RichBlock.Of(BlockKind.Rule, indent: indent));
                    return;

                case ListBlock list:
                    List(list, listDepth + 1);
                    return;

                case QuoteBlock quote when listDepth < 0 && QuoteIsText(quote):
                    Quote(quote, depth: 0);
                    return;

                default:
                    Blocks.Add(Raw(block, indent));
                    return;
            }
        }

        private void List(ListBlock list, int depth)
        {
            var kind = list.IsOrdered ? BlockKind.NumberedItem : BlockKind.BulletItem;
            foreach (var child in list)
            {
                if (child is not ListItemBlock item) continue;
                int first = 0;
                RichBlock head;
                if (item.Count > 0 && item[0] is ParagraphBlock { Inline: { } inline } && ReadItemText(inline) is { } read)
                {
                    head = RichBlock.Paragraph(read.Text.Text, [.. read.Text.Spans]) with
                    {
                        Kind = read.Checked is null ? kind : BlockKind.TaskItem,
                        Checked = read.Checked == true,
                        Indent = depth,
                    };
                    first = 1;
                }
                else
                    // An item that starts with something other than text — nothing at all, or
                    // code — is an empty item with what it holds continuing under it.
                    head = RichBlock.Of(kind, "", depth);
                Blocks.Add(head);
                for (int i = first; i < item.Count; i++)
                    Block(item[i], depth);
            }
        }

        // A quote of paragraphs and quotes of paragraphs, each one text the document can hold:
        // anything else in it — a list, code, a picture — keeps the whole quote raw.
        private static bool QuoteIsText(QuoteBlock quote) =>
            quote.All(b => b is BlankLineBlock
                || (b is ParagraphBlock { Inline: { } inline } && ReadInline(inline) is not null)
                || (b is QuoteBlock inner && QuoteIsText(inner)));

        private void Quote(QuoteBlock quote, int depth)
        {
            foreach (var child in quote)
            {
                if (child is QuoteBlock inner) Quote(inner, depth + 1);
                else if (child is ParagraphBlock { Inline: { } inline } && ReadInline(inline) is { } text)
                    Blocks.Add(RichBlock.Paragraph(text.Text, [.. text.Spans]) with { Kind = BlockKind.Quote, Indent = depth });
            }
        }

        private static RichBlock Code(LeafBlock code, string? info, int indent)
        {
            var lines = code.Lines;
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(lines.Lines[i].Slice.ToString());
            }
            return RichBlock.Of(BlockKind.CodeBlock, sb.ToString(), indent) with { Info = info };
        }

        private static string? Info(FencedCodeBlock fenced)
        {
            string info = fenced.Info ?? "";
            if (!string.IsNullOrEmpty(fenced.Arguments)) info += " " + fenced.Arguments;
            return info.Length == 0 ? null : info;
        }

        /// <summary>
        /// The block's source, exactly. Its first line starts where the block does; the lines
        /// after it lose the prefix their container put in front of them — a list item's
        /// indentation — up to the block's own column, which the writer puts back.
        /// <para>It runs to the line before whatever follows it, not only to where Markdig says
        /// it ends: a table's span stops at its header row.</para>
        /// </summary>
        private RichBlock Raw(Block block, int indent)
        {
            int start = Math.Clamp(block.Span.Start, 0, source.Length);
            int end = Math.Clamp(block.Span.End + 1, start, source.Length);
            if (FollowingLineStart(block) is int next && next > end)
                end = next;
            var lines = source[start..end].TrimEnd('\n', ' ', '\t').Split('\n');
            int column = block.Column;
            for (int i = 1; i < lines.Length; i++)
            {
                int strip = 0;
                while (strip < column && strip < lines[i].Length && lines[i][strip] is ' ' or '\t')
                    strip++;
                lines[i] = lines[i][strip..];
            }
            return new RichBlock { Kind = BlockKind.Raw, Indent = indent }
                .WithContent(string.Join("\n", lines), []);
        }

        // Where the source after a block starts: the next block beside it, or for the last block
        // in a list item, the item after; for the document's last block, the end of the source.
        private int? FollowingLineStart(Block block)
        {
            for (Block? b = block; b is not null; b = b.Parent)
            {
                if (b.Parent is not { } parent) return source.Length;
                int index = parent.IndexOf(b);
                if (index >= 0 && index + 1 < parent.Count)
                    return LineStart(parent[index + 1].Line);
                if (parent is MarkdownDocument) return source.Length;
            }
            return null;
        }

        private int LineStart(int line)
        {
            int at = 0;
            for (int i = 0; i < line && at >= 0; i++)
            {
                at = source.IndexOf('\n', at);
                if (at >= 0) at++;
            }
            return at < 0 ? source.Length : at;
        }

        // ── Pictures ──

        /// <summary>A paragraph holding one picture and nothing else — or one link holding only
        /// a picture — or one HTML <c>&lt;img&gt;</c>. Anything more is a picture inside a line
        /// of text, which the document has no shape for.</summary>
        private static RichImage? Picture(ContainerInline inline)
        {
            switch (Sole(inline))
            {
                case LinkInline { IsImage: true } image:
                    return ImageOf(image, null);
                case LinkInline link when Sole(link) is LinkInline { IsImage: true } inner:
                    return ImageOf(inner, link.Url ?? "");
                default:
                    // <img …> written mid-paragraph is inline HTML, one tag at a time.
                    var tags = new StringBuilder();
                    for (var node = inline.FirstChild; node is not null; node = node.NextSibling)
                    {
                        if (node is HtmlInline html) tags.Append(html.Tag);
                        else if (node is LiteralInline lit && lit.Content.IsEmptyOrWhitespace()) continue;
                        else if (node is LineBreakInline { IsHard: false }) tags.Append('\n');
                        else return null;
                    }
                    return tags.Length > 0 ? PictureTag(tags.ToString()) : null;
            }
        }

        // The one child of `container` that is not blank text; null when there are more.
        private static Inline? Sole(ContainerInline container)
        {
            Inline? found = null;
            for (var node = container.FirstChild; node is not null; node = node.NextSibling)
            {
                if (node is LiteralInline lit && lit.Content.IsEmptyOrWhitespace()) continue;
                if (found is not null) return null;
                found = node;
            }
            return found;
        }

        // A picture's words are text: emphasis in them is not drawn, so it is not kept.
        private static RichImage? ImageOf(LinkInline image, string? link)
        {
            var alt = new InlineWriter();
            for (var node = image.FirstChild; node is not null; node = node.NextSibling)
                if (!alt.Walk(node, RichMarks.None)) return null;
            return new RichImage(image.Url ?? "", alt.Text.ToString(),
                string.IsNullOrEmpty(image.Title) ? null : image.Title, null, link);
        }

        /// <summary>
        /// <c>&lt;img src alt title width&gt;</c>, optionally inside <c>&lt;a href&gt;</c>, and
        /// nothing else: how a picture with a width is written. Any other attribute — a height, a
        /// style, a width in per cent — keeps the source raw.
        /// </summary>
        internal static RichImage? PictureTag(string html)
        {
            var m = PictureHtml().Match(html);
            if (!m.Success) return null;
            string? src = null, alt = null, title = null;
            float? width = null;
            string attributes = m.Groups["attrs"].Value;
            foreach (Match a in HtmlAttribute().Matches(attributes))
            {
                string value = WebUtility.HtmlDecode(a.Groups["v"].Value);
                switch (a.Groups["name"].Value.ToLowerInvariant())
                {
                    case "src": src = value; break;
                    case "alt": alt = value; break;
                    case "title": title = value; break;
                    case "width" when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w > 0:
                        width = w;
                        break;
                    default: return null;
                }
            }
            // Every attribute was one of those, quoted.
            if (src is null || HtmlAttribute().Replace(attributes, "").Trim().Length > 0) return null;
            string? link = m.Groups["href"].Success ? WebUtility.HtmlDecode(m.Groups["href"].Value) : null;
            return new RichImage(src, alt ?? "", title, width, link);
        }

        [GeneratedRegex("""^\s*(?:<a\s+href="(?<href>[^"]*)"\s*>\s*)?<img\s(?<attrs>[^<>]*?)/?>\s*(?(href)</a>\s*)$""",
            RegexOptions.IgnoreCase)]
        private static partial Regex PictureHtml();

        [GeneratedRegex("""(?<name>[A-Za-z-]+)\s*=\s*"(?<v>[^"]*)"\s*""")]
        private static partial Regex HtmlAttribute();

        // ── Tables ──

        /// <summary>
        /// A GFM table as a run of cells: its header row, its rows padded or cut to the header's
        /// width as GitLab draws them, each column aligned as its delimiter says. Null — the table
        /// stays raw — when a cell holds what a cell of the document cannot.
        /// </summary>
        private List<RichBlock>? ReadTable(Table table)
        {
            var rows = table.OfType<TableRow>().ToList();
            if (rows.Count == 0 || !rows[0].IsHeader || rows.Skip(1).Any(r => r.IsHeader)) return null;
            // GFM's width is the delimiter row's. Markdig widens a table to its widest row, with
            // header cells that are in no source, where GitLab drops what overhangs: neither is
            // what was written, so such a table is kept as it is.
            int columns = rows[0].Count;
            if (DelimiterCells(table.Line + 1) != columns) return null;
            if (columns == 0) return null;
            var cells = new List<RichBlock>(rows.Count * columns);
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < columns; c++)
                {
                    var align = c < table.ColumnDefinitions.Count
                        ? table.ColumnDefinitions[c].Alignment switch
                        {
                            TableColumnAlign.Center => BlockAlign.Center,
                            TableColumnAlign.Right => BlockAlign.End,
                            _ => BlockAlign.Start,
                        }
                        : BlockAlign.Start;
                    InlineText? text = new("", []);
                    if (c < rows[r].Count)
                    {
                        if (rows[r][c] is not TableCell { ColumnSpan: 1, RowSpan: 1 } cell) return null;
                        text = CellText(cell);
                        if (text is null) return null;
                    }
                    cells.Add(RichBlock.Cell(r, c, text.Text, [.. text.Spans]) with { Align = align });
                }
            return cells;
        }

        // The cells on source line `line`, a delimiter row: dashes, colons and pipes, nothing
        // escaped, so a split is exact.
        private int DelimiterCells(int line)
        {
            int start = LineStart(line);
            int end = source.IndexOf('\n', start);
            string row = source[start..(end < 0 ? source.Length : end)].Trim();
            if (row.StartsWith('|')) row = row[1..];
            if (row.EndsWith('|')) row = row[..^1];
            return row.Split('|').Length;
        }

        // A cell's one paragraph, where <br> is a line break.
        private static InlineText? CellText(TableCell cell)
        {
            if (cell.Count == 0) return new InlineText("", []);
            if (cell.Count != 1 || cell[0] is not ParagraphBlock { Inline: { } inline }) return null;
            var w = new InlineWriter { InCell = true };
            for (var node = inline.FirstChild; node is not null; node = node.NextSibling)
                if (!w.Walk(node, RichMarks.None)) return null;
            return new InlineText(w.Text.ToString(), w.Spans);
        }

        // ── Inlines ──

        private sealed record InlineText(string Text, List<RichSpan> Spans);

        private sealed record ItemText(InlineText Text, bool? Checked);

        private static ItemText? ReadItemText(ContainerInline inline)
        {
            bool? check = null;
            var first = inline.FirstChild;
            if (first is TaskList task)
            {
                check = task.Checked;
                first = first.NextSibling;
            }
            var text = ReadInlines(first);
            if (text is null) return null;
            if (check is not null && text.Text.StartsWith(' '))
                text = Shift(text, 1);
            return new ItemText(text, check);
        }

        private static InlineText Shift(InlineText text, int by) => new(
            text.Text[by..],
            text.Spans.Select(s => s with { Start = Math.Max(0, s.Start - by) }).Where(s => s.Length > 0).ToList());

        /// <summary>The inline content as text and marks; null when it holds something the
        /// document has no shape for (a picture, inline HTML), and the block is then kept raw.</summary>
        private static InlineText? ReadInline(ContainerInline inline) => ReadInlines(inline.FirstChild);

        private static InlineText? ReadInlines(Inline? first)
        {
            var w = new InlineWriter();
            for (var node = first; node is not null; node = node.NextSibling)
                if (!w.Walk(node, RichMarks.None)) return null;
            return new InlineText(w.Text.ToString(), w.Spans);
        }

        internal sealed class InlineWriter
        {
            public readonly StringBuilder Text = new();
            public readonly List<RichSpan> Spans = [];

            /// <summary>Whether this is a table cell: <c>&lt;br&gt;</c> is a line break there, a row
            /// being one line of source, and <c>\|</c> is a pipe even in code.</summary>
            public bool InCell { get; init; }

            // Styles opened by HTML tags — <strong>, <em>, <del> and their old spellings — which
            // the writer uses where a delimiter cannot open or close. A tag is a sibling of what it
            // wraps, not its parent, so these hold across the walk until the closing tag.
            private int _bold, _italic, _strike;

            public bool Walk(Inline node, RichMarks marks)
            {
                switch (node)
                {
                    case LiteralInline literal:
                        Append(literal.Content.ToString(), marks);
                        return true;
                    case HtmlEntityInline entity:
                        Append(entity.Transcoded.ToString(), marks);
                        return true;
                    case CodeInline code:
                        // GFM unescapes \| in a cell before reading it, code and all; Markdig leaves
                        // it escaped in code, where GitLab's reader would not.
                        Append(InCell ? code.Content.Replace("\\|", "|") : code.Content, marks.With(InlineStyle.Code, true));
                        return true;
                    case LineBreakInline br:
                        // A soft break reads as a space, as it renders; only a hard one breaks the line.
                        Append(br.IsHard ? "\n" : " ", marks);
                        return true;
                    case AutolinkInline auto:
                        Append(auto.Url, marks with { Link = auto.IsEmail ? "mailto:" + auto.Url : auto.Url });
                        return true;
                    case LinkInline link when !link.IsImage:
                        return Children(link, marks with { Link = link.Url ?? "" });
                    case EmphasisInline em:
                        var style = em.DelimiterChar switch
                        {
                            '~' => InlineStyle.Strike,
                            _ when em.DelimiterCount >= 2 => InlineStyle.Bold,
                            _ => InlineStyle.Italic,
                        };
                        return Children(em, marks.With(style, true));
                    case TaskList task:
                        // A box anywhere but an item's start (which ReadItemText takes off) is
                        // the text it was typed as.
                        Append(task.Checked ? "[x]" : "[ ]", marks);
                        return true;
                    case HtmlInline html:
                        return Tag(html.Tag, marks);
                    case ContainerInline container when node.GetType() == typeof(ContainerInline):
                        return Children(container, marks);
                    default:
                        return false;
                }
            }

            private bool Tag(string tag, RichMarks marks)
            {
                switch (tag.Trim().ToLowerInvariant())
                {
                    case "<br>" or "<br/>" or "<br />" when InCell: Append("\n", marks); return true;
                    case "<strong>" or "<b>": _bold++; return true;
                    case "</strong>" or "</b>": _bold = Math.Max(0, _bold - 1); return true;
                    case "<em>" or "<i>": _italic++; return true;
                    case "</em>" or "</i>": _italic = Math.Max(0, _italic - 1); return true;
                    case "<del>" or "<s>" or "<strike>": _strike++; return true;
                    case "</del>" or "</s>" or "</strike>": _strike = Math.Max(0, _strike - 1); return true;
                    default: return false;   // any other HTML keeps the block raw
                }
            }

            private bool Children(ContainerInline container, RichMarks marks)
            {
                for (var child = container.FirstChild; child is not null; child = child.NextSibling)
                    if (!Walk(child, marks)) return false;
                return true;
            }

            private void Append(string text, RichMarks marks)
            {
                if (text.Length == 0) return;
                if (_bold > 0) marks = marks.With(InlineStyle.Bold, true);
                if (_italic > 0) marks = marks.With(InlineStyle.Italic, true);
                if (_strike > 0) marks = marks.With(InlineStyle.Strike, true);
                if (!marks.IsNone) Spans.Add(new RichSpan(Text.Length, text.Length, marks));
                Text.Append(text);
            }
        }
    }
}

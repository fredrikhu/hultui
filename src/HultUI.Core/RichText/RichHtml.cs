using System.Globalization;
using System.Net;
using System.Text;
using HultUI.Core.Primitives;

namespace HultUI.Core.RichText;

/// <summary>
/// Blocks as HTML: a fragment for the clipboard, which browsers, Word and mail read when a rich
/// editor's selection is pasted into them, or a whole document as a web page. List items become
/// nested lists, a continuing paragraph stays in its item, quotes nest, a table has its header
/// row in <c>&lt;thead&gt;</c>, and marks become the elements a browser would use.
/// </summary>
public static class RichHtml
{
    /// <summary><paramref name="blocks"/> as an HTML fragment: structure and marks, no styling
    /// of its own, so it takes on the look of wherever it is pasted.</summary>
    public static string Write(IReadOnlyList<RichBlock> blocks)
    {
        var sb = new StringBuilder();
        new Writer(sb, new RichExportOptions(), document: false).Blocks(blocks);
        return sb.ToString();
    }

    /// <summary>
    /// <paramref name="document"/> as a web page that stands on its own: a UTF-8 HTML5 file with
    /// a small stylesheet carrying the editor's proportions — heading sizes, the code face, the
    /// indent — in dark text on white. A picture is embedded when
    /// <see cref="RichExportOptions.Pictures"/> gives its file, and linked by its source when not.
    /// </summary>
    public static string WriteDocument(RichDocument document, RichExportOptions? options = null)
    {
        options ??= new RichExportOptions();
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<title>").Append(WebUtility.HtmlEncode(options.Title ?? "")).Append("</title>\n")
            .Append("<style>\n");
        Stylesheet(sb, options);
        sb.Append("</style>\n</head>\n<body>\n");
        new Writer(sb, options, document: true).Blocks(document.Blocks);
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    private static void Stylesheet(StringBuilder sb, RichExportOptions options)
    {
        var rt = options.Look;
        string Px(float v) => Num(v) + "px";
        string Pad(Thickness t) => $"{Px(t.Top)} {Px(t.Right)} {Px(t.Bottom)} {Px(t.Left)}";
        string line = RichPaper.Hex(RichPaper.Line);

        sb.Append("body{margin:2em auto;max-width:48em;padding:0 1em;line-height:1.5;")
            .Append("font-family:").Append(options.FontFamily is { } f ? Family(f) + ",sans-serif" : "system-ui,sans-serif").Append(';')
            .Append("font-size:").Append(Px(options.FontSize)).Append(';')
            .Append("color:").Append(RichPaper.Hex(RichPaper.Text)).Append(";background:#FFFFFF}\n");
        for (int level = 1; level <= 6; level++)
            sb.Append('h').Append(level).Append("{font-size:").Append(Num(rt.HeadingScaleOf(level))).Append("em}\n");
        sb.Append("h1,h2,h3,h4,h5,h6{font-weight:").Append((int)rt.HeadingWeight)
            .Append(";line-height:1.25;margin:").Append(Num(rt.HeadingSpaceAbove)).Append("em 0 ").Append(Px(rt.BlockSpacing)).Append("}\n")
            .Append("p,ul,ol,blockquote,pre,table{margin:0 0 ").Append(Px(rt.BlockSpacing)).Append("}\n")
            .Append("li>p,li>ul,li>ol{margin:").Append(Px(rt.ListItemSpacing)).Append(" 0 0}\n")
            .Append("ul,ol{padding-left:").Append(Px(rt.IndentWidth)).Append("}\n")
            .Append("li{margin:").Append(Px(rt.ListItemSpacing)).Append(" 0}\n")
            .Append("li.task{list-style:none}\n")
            .Append("li.task>input{margin:0 0.4em 0 -1.4em}\n")
            .Append("code,pre{font-family:").Append(Family(rt.CodeFamily)).Append(rt.CodeFamily == "monospace" ? "" : ",monospace")
            .Append(";font-size:").Append(Num(rt.CodeScale)).Append("em}\n")
            .Append("code{background:").Append(RichPaper.Hex(RichPaper.Code)).Append(";padding:0.1em 0.3em;border-radius:3px}\n")
            .Append("pre{background:").Append(RichPaper.Hex(RichPaper.Code)).Append(";padding:").Append(Pad(rt.CodeBlockPadding))
            .Append(";border-radius:").Append(Px(rt.CodeBlockCornerRadius)).Append(";overflow:auto}\n")
            .Append("pre code{font-size:1em;background:none;padding:0}\n")
            .Append("blockquote{margin-left:0;margin-right:0;padding-left:").Append(Px(rt.QuoteGap))
            .Append(";border-left:").Append(Px(rt.QuoteBarWidth)).Append(" solid ").Append(line)
            .Append(";color:").Append(RichPaper.Hex(RichPaper.Muted)).Append("}\n")
            .Append("blockquote>p{margin:0 0 ").Append(Px(rt.ListItemSpacing)).Append("}\n")
            .Append("hr{border:0;border-top:").Append(Px(rt.RuleThickness)).Append(" solid ").Append(line).Append("}\n")
            .Append("table{border-collapse:collapse}\n")
            .Append("th,td{border:").Append(Px(rt.TableBorderWidth)).Append(" solid ").Append(line)
            .Append(";padding:").Append(Pad(rt.TableCellPadding)).Append(";vertical-align:top}\n")
            .Append("th{background:").Append(RichPaper.Hex(RichPaper.Header)).Append(";font-weight:").Append((int)rt.TableHeaderWeight).Append("}\n")
            .Append("img{max-width:100%;height:auto}\n")
            .Append("a{color:").Append(RichPaper.Hex(RichPaper.Link)).Append("}\n");
    }

    private static string Num(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // A family name as CSS reads it: a generic one bare, any other quoted.
    private static string Family(string family) =>
        RichPaper.IsGeneric(family) ? family : "'" + family.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    private sealed class Writer(StringBuilder sb, RichExportOptions options, bool document)
    {
        // The lists open around the current block, outermost first, one per depth: each has an
        // item open, which a continuing paragraph or a deeper list goes into.
        private readonly List<string> _lists = [];
        private int _quotes;
        private readonly int[] _counters = new int[RichLists.Depths];
        private readonly float _indent = options.Look.IndentWidth;

        public void Blocks(IReadOnlyList<RichBlock> blocks)
        {
            for (int i = 0; i < blocks.Count;)
            {
                var b = blocks[i];
                int number = RichLists.Next(b, _counters);
                if (b.IsTableCell)
                {
                    Close();
                    i = Table(blocks, i);
                    Break();
                    continue;
                }
                if (b.IsListItem)
                {
                    CloseQuotes();
                    Item(b, number);
                }
                else if (b.Kind == BlockKind.Quote)
                {
                    CloseLists(0);
                    Quote(b);
                }
                else if (b.Indent > 0 && _lists.Count > 0)
                {
                    // Indented under a list item: part of that item, as Markdown reads it.
                    CloseQuotes();
                    CloseLists(Math.Min(b.Indent, _lists.Count));
                    Block(b, indent: 0);
                }
                else
                {
                    Close();
                    Block(b, b.Indent);
                    Break();
                }
                i++;
            }
            Close();
        }

        private void Close()
        {
            bool open = _lists.Count > 0 || _quotes > 0;
            CloseQuotes();
            CloseLists(0);
            if (open) Break();
        }

        // A document is written a top-level block to a line, for whoever opens the file.
        private void Break()
        {
            if (document) sb.Append('\n');
        }

        private void CloseLists(int keep)
        {
            while (_lists.Count > keep)
            {
                sb.Append("</li></").Append(_lists[^1]).Append('>');
                _lists.RemoveAt(_lists.Count - 1);
            }
        }

        private void CloseQuotes()
        {
            for (; _quotes > 0; _quotes--) sb.Append("</blockquote>");
        }

        private void Item(RichBlock b, int number)
        {
            int depth = b.Indent;
            string tag = b.Kind == BlockKind.NumberedItem ? "ol" : "ul";
            CloseLists(depth + 1);
            if (_lists.Count == depth + 1)
            {
                if (_lists[depth] == tag) sb.Append("</li>");
                else CloseLists(depth);
            }
            while (_lists.Count <= depth)
            {
                string t = _lists.Count == depth ? tag : "ul";
                sb.Append('<').Append(t);
                // Numbered as the editor numbers it, should a list ever open past its first item.
                if (t == "ol" && number > 1) sb.Append(" start=\"").Append(number).Append('"');
                sb.Append('>');
                _lists.Add(t);
                // A depth the document skips: an item with no marker to hold the deeper list.
                if (_lists.Count <= depth) sb.Append("<li style=\"list-style:none\">");
            }
            if (b.Kind == BlockKind.TaskItem)
                sb.Append(document ? "<li class=\"task\">" : "<li>")
                    .Append(b.Checked ? "<input type=\"checkbox\" checked disabled> " : "<input type=\"checkbox\" disabled> ");
            else
                sb.Append("<li>");
            Inline(b);
        }

        private void Quote(RichBlock b)
        {
            int depth = b.Indent + 1;
            for (; _quotes > depth; _quotes--) sb.Append("</blockquote>");
            for (; _quotes < depth; _quotes++) sb.Append("<blockquote>");
            sb.Append("<p").Append(Style(b.Align, 0)).Append('>');
            Inline(b);
            sb.Append("</p>");
        }

        private void Block(RichBlock b, int indent)
        {
            string style = Style(b.Align, indent);
            switch (b.Kind)
            {
                case BlockKind.Image when b.Image is { } image:
                    sb.Append("<p").Append(style).Append('>');
                    if (image.Link is { } href) sb.Append("<a href=\"").Append(Encode(href)).Append("\">");
                    sb.Append("<img src=\"").Append(Encode(Source(image))).Append("\" alt=\"").Append(Encode(image.Alt)).Append('"');
                    if (image.Title is { } title) sb.Append(" title=\"").Append(Encode(title)).Append('"');
                    if (image.Width is { } width) sb.Append(" width=\"").Append(Num(width)).Append('"');
                    sb.Append('>');
                    if (image.Link is not null) sb.Append("</a>");
                    sb.Append("</p>");
                    break;
                case BlockKind.Image:
                    break;
                case BlockKind.Heading:
                    sb.Append("<h").Append(b.HeadingLevel).Append(style).Append('>');
                    Inline(b);
                    sb.Append("</h").Append(b.HeadingLevel).Append('>');
                    break;
                case BlockKind.CodeBlock or BlockKind.Raw:
                    sb.Append("<pre").Append(style).Append("><code");
                    if (b.Kind == BlockKind.CodeBlock && Language(b.Info) is { } language)
                        sb.Append(" class=\"language-").Append(language).Append('"');
                    sb.Append('>').Append(Encode(b.Text)).Append("</code></pre>");
                    break;
                case BlockKind.Rule:
                    sb.Append("<hr>");
                    break;
                default:
                    sb.Append("<p").Append(style).Append('>');
                    Inline(b);
                    sb.Append("</p>");
                    break;
            }
        }

        // A picture embedded when the app hands over its file, else linked by its source.
        private string Source(RichImage image)
        {
            if (options.Pictures?.Invoke(image) is { Length: > 0 } bytes && RichPictureFile.Read(bytes) is { } file)
                return "data:" + file.MediaType + ";base64," + Convert.ToBase64String(bytes);
            return image.Source;
        }

        // A code block's language as a class name: what highlighters read, and nothing that
        // could close the attribute.
        private static string? Language(string? info)
        {
            if (string.IsNullOrWhiteSpace(info)) return null;
            var word = info.Trim().Split(' ', 2)[0];
            return word.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '+' or '#' or '.') ? word : null;
        }

        private string Style(BlockAlign align, int indent)
        {
            var css = new List<string>(2);
            if (indent > 0) css.Add("margin-left:" + Num(indent * _indent) + "px");
            if (align == BlockAlign.Center) css.Add("text-align:center");
            else if (align == BlockAlign.End) css.Add("text-align:right");
            return css.Count == 0 ? "" : " style=\"" + string.Join(";", css) + "\"";
        }

        // The cells from `start` to the end of their table, row by row: the header row (row 0)
        // in <thead>, the rest in <tbody>. A fragment may hold only part of a table.
        private int Table(IReadOnlyList<RichBlock> blocks, int start)
        {
            sb.Append("<table>");
            bool body = false;
            int i = start;
            for (; i < blocks.Count && blocks[i].IsTableCell && (i == start || !RichTable.StartsTable(blocks, i)); i++)
            {
                var c = blocks[i];
                if (i == start || c.Row != blocks[i - 1].Row)
                {
                    if (i > start) sb.Append("</tr>");
                    if (i == start && c.Row == 0) sb.Append("<thead>");
                    if (c.Row > 0 && !body)
                    {
                        if (i > start) sb.Append("</thead>");
                        sb.Append("<tbody>");
                        body = true;
                    }
                    sb.Append("<tr>");
                }
                string tag = c.Row == 0 ? "th" : "td";
                sb.Append('<').Append(tag).Append(Style(c.Align, 0)).Append('>');
                Inline(c);
                sb.Append("</").Append(tag).Append('>');
            }
            sb.Append("</tr>").Append(body ? "</tbody>" : "</thead>").Append("</table>");
            return i;
        }

        private void Inline(RichBlock block)
        {
            var close = new Stack<string>();
            foreach (var run in RichSpans.Runs(block.Spans, block.Text.Length))
            {
                var m = run.Marks;
                if (m.Link is { } link) Open("a", $"href=\"{Encode(link)}\"");
                if (m.Has(InlineStyle.Bold)) Open("strong");
                if (m.Has(InlineStyle.Italic)) Open("em");
                if (m.Has(InlineStyle.Underline)) Open("u");
                if (m.Has(InlineStyle.Strike)) Open("s");
                if (m.Has(InlineStyle.Code)) Open("code");
                var css = new List<string>();
                if (m.Color is { } c) css.Add("color:" + RichClipboard.Hex(c));
                if (m.Highlight is { } h) css.Add("background-color:" + RichClipboard.Hex(h));
                if (m.Size is { } size) css.Add("font-size:" + Num(size) + "px");
                if (m.Family is { } family) css.Add("font-family:" + Family(family));
                if (css.Count > 0) Open("span", $"style=\"{Encode(string.Join(";", css))}\"");

                sb.Append(Encode(block.Text.Substring(run.Start, run.Length)).Replace("\n", "<br>"));
                while (close.Count > 0) sb.Append("</").Append(close.Pop()).Append('>');
            }

            void Open(string tag, string? attributes = null)
            {
                sb.Append('<').Append(tag);
                if (attributes is not null) sb.Append(' ').Append(attributes);
                sb.Append('>');
                close.Push(tag);
            }
        }

        private static string Encode(string s) => WebUtility.HtmlEncode(s);
    }
}

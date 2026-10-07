using System.Text;
using HultUI.Core.RichText;

namespace HultUI.RichText.Markdown;

/// <summary>
/// A <see cref="RichDocument"/> as Markdown, written the one way: <c>-</c> bullets, numbers
/// counted from 1, fenced code, <c>**bold**</c>, <c>*italic*</c>, <c>~~struck~~</c>, a backslash
/// for a line break. Characters are escaped only where Markdown would otherwise read them as
/// markup, so a GitLab reference — <c>#12</c>, <c>~label</c>, <c>@user</c> — stays as typed.
/// </summary>
/// <remarks>
/// What Markdown has no syntax for is dropped: underline, colour, highlight, size, face,
/// alignment, and empty paragraphs. An editor on <see cref="RichTextFeatures.Markdown"/> never
/// makes any of them.
/// </remarks>
internal static class RichMarkdownWriter
{
    private const InlineStyle Emphasis = InlineStyle.Bold | InlineStyle.Italic | InlineStyle.Strike;

    public static string Write(RichDocument document)
    {
        var sb = new StringBuilder();
        var state = new ListState();
        RichBlock? previous = null;
        for (int i = 0; i < document.BlockCount; i++)
        {
            var block = document[i];
            if (block.Kind == BlockKind.Paragraph && block.Text.Length == 0) continue;
            if (previous is not null) sb.Append(Separator(previous, block));
            if (block.IsTableCell && document.TableAt(i) is { } table)
            {
                // A table ends any list.
                Array.Clear(state.Counters);
                state.Depth = -1;
                WriteTable(sb, document, table);
                previous = document[table.End - 1];
                i = table.End - 1;
                continue;
            }
            WriteBlock(sb, block, state);
            previous = block;
        }
        return sb.ToString();
    }

    // ── Tables ──

    /// <summary>A GFM pipe table: the header row, the delimiter row with each column's
    /// alignment, the rows. A row is one line of source, so a line break in a cell is
    /// <c>&lt;br&gt;</c>, and every <c>|</c> in a cell is escaped — in code too, as GFM reads it.</summary>
    private static void WriteTable(StringBuilder sb, RichDocument document, RichTableRange table)
    {
        for (int r = 0; r < table.Rows; r++)
        {
            if (r > 0) sb.Append('\n');
            WriteRow(sb, c => Cell(document[table.IndexOf(r, c)]), table.Columns);
            if (r == 0)
            {
                sb.Append('\n');
                WriteRow(sb, c => document[table.IndexOf(0, c)].Align switch
                {
                    BlockAlign.Center => ":---:",
                    BlockAlign.End => "---:",
                    _ => "---",
                }, table.Columns);
            }
        }
    }

    private static void WriteRow(StringBuilder sb, Func<int, string> cell, int columns)
    {
        sb.Append('|');
        for (int c = 0; c < columns; c++)
            sb.Append(' ').Append(cell(c)).Append(" |");
    }

    internal static string Cell(RichBlock cell)
    {
        string inline = Inline(cell.Text, cell.Spans, heading: false);
        return inline.Replace("\\\n", "<br>").Replace("|", "\\|");
    }

    // ── Pictures ──

    /// <summary>A picture as Markdown, <c>![words](address "title")</c>, inside a link if it is
    /// one. A width has no Markdown, so a picture with one is an HTML <c>&lt;img&gt;</c>, which
    /// GitLab and GitHub both draw at that width.</summary>
    internal static string Picture(RichImage image)
    {
        if (image.Width is { } width)
        {
            var tag = new StringBuilder("<img src=\"").Append(Attribute(image.Source))
                .Append("\" alt=\"").Append(Attribute(image.Alt)).Append('"');
            if (image.Title is { } t) tag.Append(" title=\"").Append(Attribute(t)).Append('"');
            tag.Append(" width=\"").Append(width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append("\">");
            // An <a> line of its own opens an HTML block; on one line with the picture, it would
            // be inline HTML in a paragraph.
            return image.Link is { } href ? $"<a href=\"{Attribute(href)}\">\n{tag}\n</a>" : tag.ToString();
        }
        var md = new StringBuilder("![").Append(PictureWords(image.Alt)).Append("](").Append(Destination(image.Source));
        if (image.Title is { } title)
            md.Append(" \"").Append(title.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
        md.Append(')');
        return image.Link is { } link ? $"[{md}]({Destination(link)})" : md.ToString();
    }

    // A picture's words as plain text inside its brackets, escaped like any text.
    private static string PictureWords(string alt) => Inline(alt, [], heading: false);

    // An HTML block's first line must hold its whole opening tag: a line break in a value is a
    // character reference.
    private static string Attribute(string value) => System.Net.WebUtility.HtmlEncode(value).Replace("\n", "&#10;");

    // ── Blocks ──

    // Where list content sits: each depth's content column, and the count a numbered list at
    // each depth is on. Kept as the blocks go by, the way the editor numbers them.
    private sealed class ListState
    {
        public readonly int[] Columns = new int[RichBlock.MaxIndent + 2];
        public readonly int[] Counters = new int[RichBlock.MaxIndent + 2];
        public int Depth = -1;   // the deepest list item seen since the list began; -1 outside one
    }

    private static string Separator(RichBlock previous, RichBlock block)
    {
        if (previous.IsListItem && block.IsListItem) return "\n";
        if (previous.Kind == BlockKind.Quote && block.Kind == BlockKind.Quote)
            // A blank line inside the quote: the next paragraph stays in it.
            return "\n" + QuotePrefix(Math.Min(previous.Indent, block.Indent)).TrimEnd() + "\n";
        return "\n\n";
    }

    private static string QuotePrefix(int depth) => string.Concat(Enumerable.Repeat("> ", depth + 1));

    private static void WriteBlock(StringBuilder sb, RichBlock block, ListState lists)
    {
        // Anything that is not part of a list ends it; a block continuing an item keeps it.
        bool inList = block.IsListItem || (block.Indent > 0 && block.Kind != BlockKind.Quote && lists.Depth >= 0);
        if (!inList)
        {
            Array.Clear(lists.Counters);
            lists.Depth = -1;
        }

        switch (block.Kind)
        {
            case BlockKind.Heading:
            {
                string text = Inline(block.Text.Replace('\n', ' '), block.Spans, heading: true);
                sb.Append('#', Math.Clamp(block.HeadingLevel, 1, 6));
                if (text.Length > 0) sb.Append(' ').Append(text);
                return;
            }

            case BlockKind.BulletItem or BlockKind.NumberedItem or BlockKind.TaskItem:
            {
                int depth = Math.Min(block.Indent, lists.Depth + 1);
                Array.Clear(lists.Counters, depth + 1, lists.Counters.Length - depth - 1);
                string marker = block.Kind switch
                {
                    BlockKind.NumberedItem => (++lists.Counters[depth]).ToString(System.Globalization.CultureInfo.InvariantCulture) + ". ",
                    BlockKind.TaskItem => block.Checked ? "- [x] " : "- [ ] ",
                    _ => "- ",
                };
                if (block.Kind != BlockKind.NumberedItem) lists.Counters[depth] = 0;
                int column = depth == 0 ? 0 : lists.Columns[depth - 1];
                lists.Columns[depth] = column + (block.Kind == BlockKind.TaskItem ? 2 : marker.Length);
                lists.Depth = depth;

                string text = Inline(block.Text, block.Spans, heading: false);
                string first = new string(' ', column) + marker;
                Lines(sb, text, text.Length == 0 ? first.TrimEnd() : first, new string(' ', lists.Columns[depth]));
                return;
            }

            case BlockKind.Quote:
            {
                string prefix = QuotePrefix(block.Indent);
                Lines(sb, Inline(block.Text, block.Spans, heading: false), prefix, prefix);
                return;
            }

            case BlockKind.CodeBlock:
            {
                string pad = Continuation(block, lists);
                int longest = LongestRun(block.Text, '`');
                string fence = new('`', Math.Max(3, longest + 1));
                string info = (block.Info ?? "").Replace("`", "").Replace("\n", " ");
                sb.Append(pad).Append(fence).Append(info).Append('\n');
                if (block.Text.Length > 0)
                {
                    Lines(sb, block.Text, pad, pad);
                    sb.Append('\n');
                }
                sb.Append(pad).Append(fence);
                return;
            }

            case BlockKind.Rule:
                sb.Append(Continuation(block, lists)).Append("---");
                return;

            case BlockKind.Image when block.Image is { } image:
            {
                string pad = Continuation(block, lists);
                Lines(sb, Picture(image), pad, pad);
                return;
            }

            case BlockKind.Raw:
            {
                string pad = Continuation(block, lists);
                Lines(sb, block.Text, pad, pad);
                return;
            }

            default:
            {
                string pad = Continuation(block, lists);
                Lines(sb, Inline(block.Text, block.Spans, heading: false), pad, pad);
                return;
            }
        }
    }

    // A block one level into a list sits at that item's content column. Without an item above
    // to belong to there is nowhere to indent it to: four spaces would make it code.
    private static string Continuation(RichBlock block, ListState lists)
    {
        if (block.Indent <= 0 || lists.Depth < 0) return "";
        int depth = Math.Min(block.Indent - 1, lists.Depth);
        return new string(' ', lists.Columns[depth]);
    }

    // The text a line at a time: the first after `first`, the rest after `rest`. A line with
    // nothing on it keeps only the prefix's non-blank part, so no line ends in spaces.
    private static void Lines(StringBuilder sb, string text, string first, string rest)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            string prefix = i == 0 ? first : rest;
            if (lines[i].Length == 0) sb.Append(prefix.TrimEnd());
            else sb.Append(prefix).Append(lines[i]);
        }
    }

    private static int LongestRun(string text, char c)
    {
        int longest = 0, run = 0;
        foreach (char ch in text)
        {
            run = ch == c ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        return longest;
    }

    // ── Inlines ──

    private readonly record struct Piece(string Text, InlineStyle Style, string? Link)
    {
        public InlineStyle Emphasis => Style & RichMarkdownWriter.Emphasis;
        public bool IsCode => (Style & InlineStyle.Code) != 0;
    }

    /// <summary>
    /// A block's text and marks as inline Markdown. A <c>'\n'</c> is a hard break.
    /// </summary>
    /// <remarks>
    /// Written in two layers: the text — escaped, with its code spans and link syntax — and the
    /// emphasis delimiters, kept apart as insertions into it. A delimiter only opens or closes
    /// where CommonMark's flanking rules let it (<c>a*&amp;*</c> is not emphasis; nor is the
    /// <c>~~</c> in <c>e~~**A</c>, between a letter and the <c>**</c>), and nothing in the text
    /// can always avoid that. So the delimiters are placed, every one is checked where it landed,
    /// and any style whose delimiters cannot work there is written as an HTML tag instead —
    /// <c>&lt;strong&gt;</c>, <c>&lt;em&gt;</c>, <c>&lt;del&gt;</c> — which has no flanking rules
    /// and which the reader reads back as the same mark. Most text never needs one.
    /// </remarks>
    internal static string Inline(string text, IReadOnlyList<RichSpan> spans, bool heading)
    {
        var pieces = Pieces(text, spans);
        var body = new StringBuilder();
        var delimiters = new List<Delimiter>();
        int instances = 0;
        for (int i = 0; i < pieces.Count;)
        {
            string? link = pieces[i].Link;
            int end = i;
            while (end < pieces.Count && pieces[end].Link == link) end++;
            var segment = pieces.GetRange(i, end - i);
            if (link is null)
                Emphasise(body, segment, delimiters, ref instances);
            else if (segment is [{ Style: InlineStyle.None } only] && only.Text == link && IsAutolink(link))
                body.Append('<').Append(link).Append('>');
            else
            {
                // A ! just before it would make the link a picture.
                if (body.Length > 0 && body[^1] == '!' && !IsEscaped(body, body.Length - 1))
                    Splice(body, delimiters, body.Length - 1, 0, "\\");
                body.Append('[');
                // [ ], [x] and [X] in a list item read as its task box, link or not.
                if (segment is [{ Style: InlineStyle.None, Text: " " or "x" or "X" } box])
                    body.Append("&#").Append((int)box.Text[0]).Append(';');
                else
                    Emphasise(body, segment, delimiters, ref instances);
                body.Append("](").Append(Destination(link)).Append(')');
            }
            i = end;
        }

        string source = body.ToString();
        string written = PlaceDelimiters(source, delimiters, instances, allTags: false);
        // Read back what was written: where a parser's own reading of a run of punctuation still
        // differs, every style in the block is written as a tag, which nothing can misread.
        if (delimiters.Count > 0 && !ReadsAs(written, pieces))
        {
            string tagged = PlaceDelimiters(source, delimiters, instances, allTags: true);
            if (ReadsAs(tagged, pieces)) written = tagged;
        }

        // A heading's closing #s are not its text.
        if (heading && written.Length > 0 && written[^1] == '#' && !IsEscaped(written, written.Length - 1))
            written = written[..^1] + "\\#";
        return written;
    }

    // Whether the character at `index` has a backslash before it that is not itself escaped: an
    // odd run of backslashes.
    private static bool IsEscaped(StringBuilder sb, int index) => IsEscaped(sb.ToString(0, index + 1), index);

    private static bool IsEscaped(string s, int index)
    {
        int backslashes = 0;
        for (int i = index - 1; i >= 0 && s[i] == '\\'; i--) backslashes++;
        return backslashes % 2 == 1;
    }

    // Whether `written` reads back as the pieces it was written from.
    private static bool ReadsAs(string written, List<Piece> pieces)
    {
        if (RichMarkdownReader.ReadParagraph(written) is not { } read) return false;
        var text = new StringBuilder();
        var spans = new List<RichSpan>();
        foreach (var p in pieces)
        {
            var marks = new RichMarks(p.Style, Link: p.Link);
            if (!marks.IsNone) spans.Add(new RichSpan(text.Length, p.Text.Length, marks));
            text.Append(p.Text);
        }
        string expected = text.ToString();
        if (read.Text != expected) return false;
        var a = RichBlock.Paragraph(expected, [.. spans]);
        var b = RichBlock.Paragraph(read.Text, [.. read.Spans]);
        return a.Spans.SequenceEqual(b.Spans);
    }

    /// <summary>
    /// Replaces <paramref name="remove"/> characters of the text at <paramref name="index"/> with
    /// <paramref name="insert"/>, moving every delimiter placed after them along with the text.
    /// A delimiter at <paramref name="index"/> itself stays in front.
    /// </summary>
    private static void Splice(StringBuilder body, List<Delimiter> delimiters, int index, int remove, string insert)
    {
        body.Remove(index, remove).Insert(index, insert);
        int shift = insert.Length - remove;
        for (int d = 0; d < delimiters.Count; d++)
            if (delimiters[d].Position > index)
                delimiters[d] = delimiters[d] with { Position = Math.Max(index, delimiters[d].Position + shift) };
    }

    /// <summary>One emphasis delimiter: where in the text it goes, what it opens or closes, and
    /// which opened style it belongs to — its opener and closer share an instance.</summary>
    private readonly record struct Delimiter(int Position, InlineStyle Style, bool Opens, int Instance);

    private static string PlaceDelimiters(string body, List<Delimiter> delimiters, int instances, bool allTags)
    {
        if (delimiters.Count == 0) return body;
        var asTag = new bool[instances];
        if (allTags) Array.Fill(asTag, true);
        var starts = new int[delimiters.Count];
        for (int round = 0; ; round++)
        {
            string written = Render(body, delimiters, asTag, starts);
            bool changed = false;
            for (int d = 0; d < delimiters.Count; d++)
            {
                var delimiter = delimiters[d];
                if (asTag[delimiter.Instance] || Flanks(written, starts[d], DelimiterText(delimiter.Style).Length, delimiter.Opens))
                    continue;
                asTag[delimiter.Instance] = true;
                changed = true;
            }
            if (!changed || round == 8) return written;
        }
    }

    private static string Render(string body, List<Delimiter> delimiters, bool[] asTag, int[] starts)
    {
        var sb = new StringBuilder(body.Length + delimiters.Count * 4);
        int at = 0;
        for (int d = 0; d < delimiters.Count; d++)
        {
            var delimiter = delimiters[d];
            sb.Append(body, at, delimiter.Position - at);
            at = delimiter.Position;
            starts[d] = sb.Length;
            sb.Append(asTag[delimiter.Instance] ? Tag(delimiter.Style, delimiter.Opens) : DelimiterText(delimiter.Style));
        }
        sb.Append(body, at, body.Length - at);
        return sb.ToString();
    }

    /// <summary>Whether the delimiter written at <paramref name="start"/> can open (or close)
    /// there: the run of its character it is part of must be left- (or right-) flanking.</summary>
    private static bool Flanks(string written, int start, int length, bool opens)
    {
        char c = written[start];
        int from = start, to = start + length;
        while (from > 0 && written[from - 1] == c) from--;
        while (to < written.Length && written[to] == c) to++;
        char before = from > 0 ? written[from - 1] : ' ';
        char after = to < written.Length ? written[to] : ' ';
        return opens
            ? !char.IsWhiteSpace(after) && (!IsPunctuation(after) || char.IsWhiteSpace(before) || IsPunctuation(before))
            : !char.IsWhiteSpace(before) && (!IsPunctuation(before) || char.IsWhiteSpace(after) || IsPunctuation(after));
    }

    private static bool IsPunctuation(char c) => char.IsPunctuation(c) || char.IsSymbol(c);

    /// <summary>
    /// The text in runs of equal style and link — what Markdown cares about of the marks. A
    /// delimiter next to a space neither opens nor closes, so a space at the edge of a styled
    /// run moves out of the styles that start or end there (code keeps its spaces: they are
    /// its content).
    /// </summary>
    private static List<Piece> Pieces(string text, IReadOnlyList<RichSpan> spans)
    {
        var raw = new List<Piece>();
        foreach (var run in RichSpans(spans, text.Length))
        {
            var style = run.Marks.Style & (Emphasis | InlineStyle.Code);
            var piece = new Piece(text.Substring(run.Start, run.Length), style, run.Marks.Link);
            if (raw.Count > 0 && raw[^1].Style == piece.Style && raw[^1].Link == piece.Link)
                raw[^1] = raw[^1] with { Text = raw[^1].Text + piece.Text };
            else
                raw.Add(piece);
        }

        var pieces = new List<Piece>(raw.Count);
        for (int i = 0; i < raw.Count; i++)
        {
            var p = raw[i];
            if (p.IsCode || p.Emphasis == InlineStyle.None) { pieces.Add(p); continue; }

            var before = i > 0 && raw[i - 1].Link == p.Link ? raw[i - 1].Emphasis : InlineStyle.None;
            var after = i + 1 < raw.Count && raw[i + 1].Link == p.Link ? raw[i + 1].Emphasis : InlineStyle.None;
            string body = p.Text;
            int lead = body.Length - body.TrimStart().Length;
            int trail = body.Length - body.TrimEnd().Length;
            if (lead == body.Length) { pieces.Add(p with { Style = (p.Style & ~Emphasis) | (p.Emphasis & before & after) }); continue; }

            if (lead > 0 && (p.Emphasis & ~before) != 0)
            {
                pieces.Add(p with { Text = body[..lead], Style = (p.Style & ~Emphasis) | (p.Emphasis & before) });
                body = body[lead..];
            }
            string? tail = null;
            if (trail > 0 && (p.Emphasis & ~after) != 0)
            {
                tail = body[^trail..];
                body = body[..^trail];
            }
            pieces.Add(p with { Text = body });
            if (tail is not null)
                pieces.Add(p with { Text = tail, Style = (p.Style & ~Emphasis) | (p.Emphasis & after) });
        }
        return pieces;
    }

    private static IEnumerable<RichSpan> RichSpans(IReadOnlyList<RichSpan> spans, int length)
    {
        int at = 0;
        foreach (var s in spans)
        {
            if (s.Start > at) yield return new RichSpan(at, s.Start - at, RichMarks.None);
            yield return s;
            at = s.End;
        }
        if (at < length) yield return new RichSpan(at, length - at, RichMarks.None);
    }

    // The text of each piece into `body`, and the delimiters around them into `delimiters`:
    // opened and closed like brackets, strike outermost, italic innermost.
    private static void Emphasise(StringBuilder body, List<Piece> pieces, List<Delimiter> delimiters, ref int instances)
    {
        var open = new List<(InlineStyle Style, int Instance)>();
        foreach (var piece in pieces)
        {
            var want = piece.Emphasis;
            while (open.Count > 0 && open.Any(o => (want & o.Style) == 0))
            {
                delimiters.Add(new Delimiter(body.Length, open[^1].Style, Opens: false, open[^1].Instance));
                open.RemoveAt(open.Count - 1);
            }
            foreach (var s in new[] { InlineStyle.Strike, InlineStyle.Bold, InlineStyle.Italic })
                if ((want & s) != 0 && !open.Any(o => o.Style == s))
                {
                    delimiters.Add(new Delimiter(body.Length, s, Opens: true, instances));
                    open.Add((s, instances++));
                }

            if (piece.IsCode) Code(body, piece.Text, delimiters);
            else Escape(body, piece.Text);
        }
        for (int i = open.Count - 1; i >= 0; i--)
            delimiters.Add(new Delimiter(body.Length, open[i].Style, Opens: false, open[i].Instance));
    }

    private static string DelimiterText(InlineStyle style) => style switch
    {
        InlineStyle.Bold => "**",
        InlineStyle.Italic => "*",
        _ => "~~",
    };

    private static string Tag(InlineStyle style, bool opens)
    {
        string name = style switch
        {
            InlineStyle.Bold => "strong",
            InlineStyle.Italic => "em",
            _ => "del",
        };
        return opens ? "<" + name + ">" : "</" + name + ">";
    }

    // A code span fenced by one more backtick than the longest run inside it, padded with a
    // space when the content starts or ends with a backtick or would lose an edge space. A line
    // break cannot be inside one: it ends the span and starts another on the next line.
    private static void Code(StringBuilder sb, string text, List<Delimiter> delimiters)
    {
        var parts = text.Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append("\\\n");
            string part = parts[i];
            if (part.Length == 0) continue;
            // A backtick just before the span, even an escaped one, would read as part of its
            // fence: spell it as a character reference instead.
            if (sb.Length >= 2 && sb[^1] == '`' && sb[^2] == '\\')
                Splice(sb, delimiters, sb.Length - 2, 2, "&#96;");
            string fence = new('`', LongestRun(part, '`') + 1);
            bool pad = part[0] == '`' || part[^1] == '`'
                || (part.Length > 1 && part[0] == ' ' && part[^1] == ' ' && part.Trim().Length > 0);
            sb.Append(fence);
            if (pad) sb.Append(' ');
            sb.Append(part);
            if (pad) sb.Append(' ');
            sb.Append(fence);
        }
    }

    /// <summary>Plain text, with a backslash before whatever Markdown would otherwise read as
    /// markup there — and nowhere else.</summary>
    private static void Escape(StringBuilder sb, string text)
    {
        bool lineStart = sb.Length == 0 || sb[^1] == '\n';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char prev = i > 0 ? text[i - 1] : sb.Length > 0 ? sb[^1] : '\n';
            // A line break goes out as a backslash, so it is no space beside a delimiter.
            char next = i + 1 < text.Length ? (text[i + 1] == '\n' ? '\\' : text[i + 1]) : '\0';

            if (c == '\n')
            {
                sb.Append("\\\n");
                lineStart = true;
                continue;
            }
            if (lineStart && LineStartEscape(text, i) is int at)
            {
                // The escaped character goes out as it is: escaping it again would escape the
                // backslash instead.
                sb.Append(text, i, at - i).Append('\\').Append(text[at]);
                i = at;
                lineStart = false;
                continue;
            }
            lineStart = false;

            // Just after a code span, a backtick would lengthen its closing fence.
            if (c == '`' && sb.Length > 0 && sb[^1] == '`')
            {
                sb.Append("&#96;");
                continue;
            }

            bool escape = c switch
            {
                '\\' => next is '\0' or '\n' || IsAsciiPunctuation(next),
                '`' or '[' or ']' => true,
                '*' => i == 0 || i == text.Length - 1 || !(IsSpace(prev) && IsSpace(next)),
                // At a piece's edge the neighbour in the output may be a delimiter, not this text.
                '_' => i == 0 || i == text.Length - 1
                    || !((char.IsLetterOrDigit(prev) && char.IsLetterOrDigit(next)) || (IsSpace(prev) && IsSpace(next))),
                // Before anything but a space, < can open a tag or an autolink (<a@b.c> is one).
                '<' => !IsSpace(next),
                '&' => char.IsAsciiLetterOrDigit(next) || next == '#',
                '~' => prev == '~' || next == '~' || i == 0 || i == text.Length - 1,
                _ => false,
            };
            if (escape) sb.Append('\\');
            sb.Append(c);
        }
    }

    // Where a line's start would read as a block — a heading, a quote, a list item, a rule, a
    // fence — the offset of the character to put a backslash before; null when it reads as text.
    private static int? LineStartEscape(string text, int i)
    {
        int lineEnd = text.IndexOf('\n', i);
        string line = lineEnd < 0 ? text[i..] : text[i..lineEnd];
        if (line.Length == 0) return null;
        char c = line[0];

        if (c == '#')
        {
            int hashes = 0;
            while (hashes < line.Length && line[hashes] == '#') hashes++;
            return hashes <= 6 && (hashes == line.Length || line[hashes] is ' ' or '\t') ? i : null;
        }
        if (c == '>') return i;
        if (c is '-' or '+' or '*' or '=' or '_')
        {
            if (line.Length == 1 || line[1] is ' ' or '\t') return i;           // a bullet, or nothing but the mark
            if (line.All(ch => ch == c || ch is ' ' or '\t')) return i;          // a rule or a setext underline
            return null;
        }
        if (char.IsAsciiDigit(c))
        {
            int digits = 0;
            while (digits < line.Length && char.IsAsciiDigit(line[digits])) digits++;
            if (digits <= 9 && digits < line.Length && line[digits] is '.' or ')'
                && (digits + 1 == line.Length || line[digits + 1] is ' ' or '\t'))
                return i + digits;
            return null;
        }
        return null;
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\0';

    private static bool IsAsciiPunctuation(char c) => c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';

    private static bool IsAutolink(string url)
    {
        int colon = url.IndexOf(':');
        if (colon < 2 || colon > 32 || !char.IsAsciiLetter(url[0])) return false;
        for (int i = 1; i < colon; i++)
            if (!(char.IsAsciiLetterOrDigit(url[i]) || url[i] is '+' or '.' or '-')) return false;
        return !url.Any(ch => ch is ' ' or '<' or '>' || char.IsControl(ch));
    }

    private static string Destination(string url)
    {
        if (url.Length == 0) return "<>";
        if (url.Any(ch => ch is ' ' or '<' or '>'))
            return "<" + url.Replace("\\", "\\\\").Replace("<", "\\<").Replace(">", "\\>") + ">";
        var sb = new StringBuilder(url.Length);
        foreach (char ch in url)
        {
            if (ch is '(' or ')' or '\\') sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }
}

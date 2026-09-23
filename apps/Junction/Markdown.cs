using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Emoji;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Junction;

// ── The shapes Junction draws ────────────────────────────────────────────────────────

/// <summary>A body of markdown as the blocks Junction draws. Empty when the source had
/// nothing to show — a template left as comments reads as no description at all.</summary>
internal sealed record MdDocument(IReadOnlyList<MdBlock> Blocks)
{
    public bool IsEmpty => Blocks.Count == 0;
}

internal abstract record MdBlock;
internal sealed record MdParagraph(MdText Text) : MdBlock;
internal sealed record MdHeading(int Level, MdText Text) : MdBlock;
internal sealed record MdCode(string? Language, string Code) : MdBlock;
internal sealed record MdQuote(IReadOnlyList<MdBlock> Blocks) : MdBlock;
internal sealed record MdList(bool Ordered, int Start, bool Loose, IReadOnlyList<MdItem> Items) : MdBlock;
internal sealed record MdRule : MdBlock;

/// <summary>A pipe table. <see cref="Align"/> has one entry per column; a row may have
/// fewer cells than that, and the missing ones read as empty.</summary>
internal sealed record MdTable(IReadOnlyList<MdAlign> Align, IReadOnlyList<MdTableRow> Rows) : MdBlock
{
    public int Columns => Align.Count;
}

internal sealed record MdTableRow(bool Header, IReadOnlyList<MdText> Cells);

internal enum MdAlign : byte { Start, Center, End }

/// <summary>A <c>&lt;details&gt;</c> section. <see cref="Key"/> names it for the reader's
/// open/closed choice — stable for the same body, so the choice survives a rebuild and a
/// refresh; <see cref="Open"/> is what the source asked for.</summary>
internal sealed record MdDetails(string Key, MdText Summary, IReadOnlyList<MdBlock> Blocks, bool Open) : MdBlock;

/// <summary>A picture, as a block of its own: one inside a paragraph splits it. <see cref="Link"/>
/// is where a linked image goes when clicked; <see cref="Width"/> and <see cref="Height"/>
/// are what the source asked for, <c>{width=600}</c> or <c>&lt;img width="50%"&gt;</c>.</summary>
internal sealed record MdImage(string Url, string Alt, string? Link, MdLength? Width, MdLength? Height) : MdBlock;

internal readonly record struct MdLength(float Value, bool Percent);

/// <summary>A list item; <see cref="Checked"/> is null unless it is a task.</summary>
internal sealed record MdItem(bool? Checked, IReadOnlyList<MdBlock> Blocks);

[Flags]
internal enum MdStyle : byte { None = 0, Strong = 1, Emphasis = 2, Strike = 4, Code = 8 }

/// <summary>A styled range of <see cref="MdText.Text"/>. Sorted and non-overlapping.</summary>
internal readonly record struct MdRun(int Start, int Length, MdStyle Style)
{
    public int End => Start + Length;
}

/// <summary>A link as the source spelled it: <see cref="Url"/> may be relative, and is
/// resolved against the project when drawn.</summary>
internal readonly record struct MdLink(int Start, int Length, string Url)
{
    public int End => Start + Length;
}

/// <summary>One paragraph's worth of inline markdown, flattened: the text as it reads, the
/// styled ranges over it, and the links in it. Soft breaks are spaces, hard breaks
/// <c>\n</c>, and the markup itself is gone.</summary>
internal sealed record MdText(string Text, IReadOnlyList<MdRun> Runs, IReadOnlyList<MdLink> Links)
{
    public static readonly MdText Empty = new("", [], []);
    public static MdText Plain(string text) => new(text, [], []);
}

/// <summary>
/// Reads GitLab-flavoured markdown — merge request descriptions and review notes — into
/// <see cref="MdDocument"/>. Markdig does the parsing (CommonMark, plus the extensions GitLab
/// turns on: strikethrough, task lists, bare URLs, pipe tables, <c>:emoji:</c> shortcodes and
/// the <c>{width=…}</c> an image is sized with); this walks its tree into the handful of
/// shapes <c>JunctionApp.Markdown</c> knows how to draw and measure.
/// </summary>
/// <remarks>
/// HTML is not drawn, but it is not shown raw either: comments go (templates are full of
/// them), <c>&lt;br&gt;</c> breaks the line, <c>&lt;img&gt;</c> is a picture,
/// <c>&lt;details&gt;</c> is a section that opens, and any other tag drops out leaving its text.
/// </remarks>
internal static partial class MarkdownReader
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseTaskLists()
        .UseAutoLinks()
        .UsePipeTables()
        // Shortcodes only: GitLab leaves :) as typed.
        .UseEmojiAndSmiley(enableSmileys: false)
        // GitLab sizes a pasted screenshot as ![image](/uploads/…){width=600}; without this
        // the braces read as text after the link. Markdig wants it registered last.
        .UseGenericAttributes()
        .Build();

    // A body is read on every build and every height measure, so the answer is kept. Keyed
    // on the source itself: an edited note is a different string and reads afresh.
    private static readonly Dictionary<string, MdDocument> Cache = new(StringComparer.Ordinal);
    private const int CacheLimit = 512;
    private static readonly object Gate = new();

    public static MdDocument Read(string? source)
    {
        source ??= "";
        lock (Gate)
            if (Cache.TryGetValue(source, out var cached)) return cached;

        var state = new ReadState(source);
        var doc = new MdDocument(ReadBlocks(Markdig.Markdown.Parse(source.Replace("\r\n", "\n"), Pipeline), state));
        lock (Gate)
        {
            if (Cache.Count >= CacheLimit) Cache.Clear();
            Cache[source] = doc;
        }
        return doc;
    }

    /// <summary>The first line of text the body shows, markup gone — what a one-line
    /// preview of a note or a thread reads.</summary>
    public static string FirstLine(string? source)
    {
        foreach (var block in Read(source).Blocks)
            if (FirstText(block) is { Length: > 0 } text)
            {
                int nl = text.IndexOf('\n');
                return (nl < 0 ? text : text[..nl]).Trim();
            }
        return "";
    }

    private static string? FirstText(MdBlock block) => block switch
    {
        MdParagraph p => p.Text.Text,
        MdHeading h => h.Text.Text,
        MdCode c => c.Code,
        MdQuote q => q.Blocks.Select(FirstText).FirstOrDefault(t => t is { Length: > 0 }),
        MdList l => l.Items.SelectMany(i => i.Blocks).Select(FirstText).FirstOrDefault(t => t is { Length: > 0 }),
        MdTable t => t.Rows.SelectMany(r => r.Cells).Select(c => c.Text).FirstOrDefault(t => t.Length > 0),
        MdDetails d => d.Summary.Text,
        MdImage i => i.Alt,
        _ => null,
    };

    /// <summary>What one read of one body carries along: the stem every
    /// <see cref="MdDetails.Key"/> in it starts with, and how many it has handed out.</summary>
    private sealed class ReadState(string source)
    {
        private readonly string _stem = ((uint)StringComparer.Ordinal.GetHashCode(source)).ToString("x8", CultureInfo.InvariantCulture);
        private int _details;
        public string NextDetailsKey() => $"{_stem}:{_details++}";
    }

    // ── Blocks ───────────────────────────────────────────────────────────────────────

    private static List<MdBlock> ReadBlocks(ContainerBlock container, ReadState state)
    {
        int i = 0;
        return ReadRun(container, ref i, state, insideDetails: false, initial: null, out _);
    }

    /// <summary>
    /// Reads <paramref name="container"/>'s blocks from <paramref name="i"/> on. Outside a
    /// details section that is all of them; inside one it stops at the <c>&lt;/details&gt;</c>
    /// that closes it, and hands back whatever HTML followed the tag in the same block — where
    /// the next section usually opens, since nothing but a blank line ends an HTML block.
    /// </summary>
    private static List<MdBlock> ReadRun(ContainerBlock container, ref int i, ReadState state,
        bool insideDetails, string? initial, out string? leftover)
    {
        var blocks = new List<MdBlock>();
        leftover = null;
        string? pending = initial;
        while (pending is not null || i < container.Count)
        {
            string raw;
            if (pending is not null)
            {
                raw = pending;
                pending = null;
            }
            else
            {
                var block = container[i++];
                if (block is not HtmlBlock html) { ReadBlock(block, state, blocks); continue; }
                // A commented-out tag is not a tag.
                raw = Comment().Replace(html.Lines.ToString(), "");
            }

            var open = DetailsOpen().Match(raw);
            var close = insideDetails ? DetailsClose().Match(raw) : Match.Empty;
            if (close.Success && (!open.Success || close.Index < open.Index))
            {
                AddHtml(blocks, raw[..close.Index]);
                leftover = raw[(close.Index + close.Length)..];
                return blocks;
            }
            if (open.Success)
            {
                AddHtml(blocks, raw[..open.Index]);
                blocks.Add(ReadDetails(container, ref i, state, raw[(open.Index + open.Length)..],
                    open.Groups["open"].Success, out var after));
                if (!string.IsNullOrWhiteSpace(after)) pending = after;
                continue;
            }
            AddHtml(blocks, raw);
        }
        return blocks;
    }

    /// <summary>A details section whose opening tag has been read; <paramref name="html"/> is
    /// what followed the tag in its block. Its body runs on through the blocks after it until
    /// the tag that closes it — which may be in the same block, a later one, or never.</summary>
    private static MdDetails ReadDetails(ContainerBlock container, ref int i, ReadState state, string html,
        bool open, out string? after)
    {
        var key = state.NextDetailsKey();
        var summary = "Details";
        var tag = SummaryTag().Match(html);
        var close = DetailsClose().Match(html);
        if (tag.Success && (!close.Success || tag.Index < close.Index))
        {
            if (HtmlText(tag.Groups["text"].Value).Replace('\n', ' ') is { Length: > 0 } said) summary = said;
            html = html[(tag.Index + tag.Length)..];
        }
        var body = ReadRun(container, ref i, state, insideDetails: true, initial: html, out after);
        return new MdDetails(key, MdText.Plain(summary), body, open);
    }

    private static void ReadBlock(Block block, ReadState state, List<MdBlock> blocks)
    {
        switch (block)
        {
            case HeadingBlock h:
                if (ReadInline(h.Inline) is { Text.Length: > 0 } heading) blocks.Add(new MdHeading(h.Level, heading));
                break;
            case ParagraphBlock p when p.Inline is { } inline:
            {
                // Pictures split the paragraph around them: text, picture, text.
                var w = new InlineWriter(blocks);
                Walk(inline, MdStyle.None, w);
                w.Flush();
                break;
            }
            case FencedCodeBlock fenced:
                blocks.Add(new MdCode(FenceLanguage(fenced.Info), CodeText(fenced)));
                break;
            case CodeBlock indented:
                blocks.Add(new MdCode(null, CodeText(indented)));
                break;
            case ThematicBreakBlock:
                blocks.Add(new MdRule());
                break;
            case QuoteBlock quote:
                if (ReadBlocks(quote, state) is { Count: > 0 } quoted) blocks.Add(new MdQuote(quoted));
                break;
            case ListBlock list:
                blocks.Add(ReadList(list, state));
                break;
            case Table table:
                if (ReadTable(table) is { Rows.Count: > 0 } read) blocks.Add(read);
                break;
            case LinkReferenceDefinitionGroup:
                break;                                   // [label]: url lines are not shown
            case ContainerBlock other:
                blocks.AddRange(ReadBlocks(other, state));   // an extension's container: its contents
                break;
            case LeafBlock { Inline: { } leafInline }:
                if (ReadInline(leafInline) is { Text.Length: > 0 } leaf) blocks.Add(new MdParagraph(leaf));
                break;
        }
    }

    private static MdList ReadList(ListBlock list, ReadState state)
    {
        var items = new List<MdItem>();
        foreach (var child in list)
        {
            if (child is not ListItemBlock item) continue;
            bool? task = item.Count > 0 && item[0] is ParagraphBlock { Inline.FirstChild: TaskList box } ? box.Checked : null;
            items.Add(new MdItem(task, ReadBlocks(item, state)));
        }
        int start = list.IsOrdered && int.TryParse(list.OrderedStart, out var s) ? s : 1;
        return new MdList(list.IsOrdered, start, list.IsLoose, items);
    }

    private static MdTable ReadTable(Table table)
    {
        var rows = new List<MdTableRow>();
        foreach (var child in table)
            if (child is TableRow row)
                rows.Add(new MdTableRow(row.IsHeader, [.. row.OfType<TableCell>().Select(CellText)]));
        int columns = rows.Count == 0 ? 0 : rows.Max(r => r.Cells.Count);
        var align = new MdAlign[columns];
        for (int c = 0; c < columns && c < table.ColumnDefinitions.Count; c++)
            align[c] = table.ColumnDefinitions[c].Alignment switch
            {
                TableColumnAlign.Center => MdAlign.Center,
                TableColumnAlign.Right => MdAlign.End,
                _ => MdAlign.Start,
            };
        return new MdTable(align, rows);
    }

    /// <summary>A cell's text. A pipe table's cell is one line, so one paragraph; a picture in
    /// it reads as its alt text, which a cell has room for.</summary>
    private static MdText CellText(TableCell cell) =>
        cell.OfType<ParagraphBlock>().FirstOrDefault() is { Inline: { } inline } ? ReadInline(inline) ?? MdText.Empty : MdText.Empty;

    /// <summary>The fence's language: the first word of its info string, so
    /// <c>```csharp title="x"</c> is C#.</summary>
    private static string? FenceLanguage(string? info)
    {
        if (string.IsNullOrWhiteSpace(info)) return null;
        var word = info.Trim();
        int space = word.IndexOfAny([' ', '\t', '{']);
        return space < 0 ? word : word[..space];
    }

    private static string CodeText(LeafBlock block) =>
        block.Lines.ToString().Replace("\t", "    ").TrimEnd('\n', ' ');

    // ── Inlines ──────────────────────────────────────────────────────────────────────

    /// <summary>Inline markdown as one run of text — a heading, a table cell — where a
    /// picture has no room of its own and reads as its alt text instead.</summary>
    private static MdText? ReadInline(ContainerInline? inline)
    {
        if (inline is null) return null;
        var w = new InlineWriter(null);
        Walk(inline, MdStyle.None, w);
        return w.Finish();
    }

    private static void Walk(ContainerInline container, MdStyle style, InlineWriter w)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case EmojiInline emoji:
                    w.Append(emoji.Content.ToString(), style);
                    break;
                case LiteralInline literal:
                    w.Append(literal.Content.ToString(), style);
                    break;
                case CodeInline code:
                    w.Append(code.Content, style | MdStyle.Code);
                    break;
                case EmphasisInline em:
                    var add = em.DelimiterChar == '~' ? MdStyle.Strike
                        : em.DelimiterCount >= 2 ? MdStyle.Strong : MdStyle.Emphasis;
                    Walk(em, style | add, w);
                    break;
                case LinkInline { IsImage: true } image:
                    if (!w.Picture(ImageOf(image, null))) AltText(image, image.Url, style, w);
                    break;
                case LinkInline link:
                {
                    // [![alt](picture)](target): the picture, going to the target when clicked.
                    if (LoneImage(link) is { } inner && w.Picture(ImageOf(inner, link.Url))) break;
                    int start = w.Length;
                    Walk(link, style, w);
                    if (w.Length == start && link.Url is { Length: > 0 } bare) w.Append(bare, style);
                    if (link.Url is { Length: > 0 } url && w.Length > start) w.Link(start, url);
                    break;
                }
                case AutolinkInline auto:
                {
                    int start = w.Length;
                    w.Append(auto.Url, style);
                    w.Link(start, auto.IsEmail ? "mailto:" + auto.Url : auto.Url);
                    break;
                }
                case LineBreakInline br:
                    w.Append(br.IsHard ? "\n" : " ", style);
                    break;
                case HtmlEntityInline entity:
                    w.Append(entity.Transcoded.ToString(), style);
                    break;
                case HtmlInline html:
                    if (LoneBreakTag().IsMatch(html.Tag)) w.Append("\n", style);
                    else if (LoneImgTag().IsMatch(html.Tag) && ImageFromTag(html.Tag) is { } tagged && !w.Picture(tagged))
                        AltText(tagged, style, w);
                    break;                                   // any other tag drops out, its text stays
                case TaskList:
                    break;                                   // drawn as the item's marker
                case ContainerInline other:
                    Walk(other, style, w);
                    break;
            }
        }
    }

    /// <summary>A picture where there is no room for one: its alt text, linking to it.</summary>
    private static void AltText(LinkInline image, string? url, MdStyle style, InlineWriter w) =>
        AltText(ImageOf(image, null) with { Url = url ?? "" }, style, w);

    private static void AltText(MdImage image, MdStyle style, InlineWriter w)
    {
        int start = w.Length;
        w.Append(image.Alt.Length > 0 ? image.Alt : "image", style);
        if (image.Url.Length > 0 && w.Length > start) w.Link(start, image.Url);
    }

    private static MdImage ImageOf(LinkInline image, string? link)
    {
        var alt = new InlineWriter(null);
        Walk(image, MdStyle.None, alt);
        MdLength? width = null, height = null;
        if (image.TryGetAttributes()?.Properties is { } props)
            foreach (var (name, value) in props)
            {
                if (string.Equals(name, "width", StringComparison.OrdinalIgnoreCase)) width = ParseLength(value);
                else if (string.Equals(name, "height", StringComparison.OrdinalIgnoreCase)) height = ParseLength(value);
            }
        return new MdImage(image.Url ?? "", alt.Finish().Text, link, width, height);
    }

    /// <summary>The picture a link holds and nothing else, or null.</summary>
    private static LinkInline? LoneImage(LinkInline link)
    {
        LinkInline? found = null;
        foreach (var child in link)
        {
            if (child is LinkInline { IsImage: true } image && found is null) found = image;
            else if (child is LiteralInline lit && lit.Content.IsEmptyOrWhitespace()) continue;
            else return null;
        }
        return found;
    }

    private sealed class InlineWriter(List<MdBlock>? output)
    {
        private readonly StringBuilder _text = new();
        private readonly List<MdRun> _runs = [];
        private readonly List<MdLink> _links = [];

        public int Length => _text.Length;

        public void Append(string s, MdStyle style)
        {
            // Nothing leads a paragraph with whitespace: what would is the space after a
            // task box, or a break left behind by a tag that dropped out.
            if (_text.Length == 0) s = s.TrimStart();
            if (s.Length == 0) return;
            int start = _text.Length;
            _text.Append(s);
            if (style == MdStyle.None) return;
            if (_runs.Count > 0 && _runs[^1].Style == style && _runs[^1].End == start)
                _runs[^1] = _runs[^1] with { Length = _runs[^1].Length + s.Length };
            else
                _runs.Add(new MdRun(start, s.Length, style));
        }

        public void Link(int start, string url) => _links.Add(new MdLink(start, _text.Length - start, url));

        /// <summary>Places a picture as a block of its own, ending the paragraph so far — or
        /// answers false where pictures have no room (a heading, a cell).</summary>
        public bool Picture(MdImage? image)
        {
            if (output is null || image is null || image.Url.Length == 0) return false;
            Flush();
            output.Add(image);
            return true;
        }

        /// <summary>The paragraph so far, into the blocks, if it has any text; then empty.</summary>
        public void Flush()
        {
            if (output is null) return;
            if (Finish() is { Text.Length: > 0 } text) output.Add(new MdParagraph(text));
            _text.Clear();
            _runs.Clear();
            _links.Clear();
        }

        public MdText Finish()
        {
            int end = _text.Length;
            while (end > 0 && char.IsWhiteSpace(_text[end - 1])) end--;
            var runs = new List<MdRun>(_runs.Count);
            foreach (var r in _runs)
                if (r.Start < end) runs.Add(r with { Length = Math.Min(r.End, end) - r.Start });
            var links = new List<MdLink>(_links.Count);
            foreach (var l in _links)
                if (l.Start < end && l.Length > 0) links.Add(l with { Length = Math.Min(l.End, end) - l.Start });
            return new MdText(_text.ToString(0, end), runs, links);
        }
    }

    // ── HTML ─────────────────────────────────────────────────────────────────────────

    /// <summary>An HTML block's worth of blocks: its text as paragraphs, and its
    /// <c>&lt;img&gt;</c> tags as the pictures between them.</summary>
    private static void AddHtml(List<MdBlock> blocks, string html)
    {
        int at = 0;
        foreach (Match tag in ImgTag().Matches(html))
        {
            AddHtmlText(blocks, html[at..tag.Index]);
            if (ImageFromTag(tag.Value) is { } image) blocks.Add(image);
            at = tag.Index + tag.Length;
        }
        AddHtmlText(blocks, html[at..]);
    }

    private static void AddHtmlText(List<MdBlock> blocks, string html)
    {
        if (HtmlText(html) is { Length: > 0 } text) blocks.Add(new MdParagraph(MdText.Plain(text)));
    }

    private static MdImage? ImageFromTag(string tag)
    {
        string? src = null, alt = null;
        MdLength? width = null, height = null;
        foreach (Match a in ImgAttribute().Matches(tag))
        {
            var value = WebUtility.HtmlDecode(a.Groups["v"].Value);
            switch (a.Groups["name"].Value.ToLowerInvariant())
            {
                case "src": src = value; break;
                case "alt": alt = value; break;
                case "width": width = ParseLength(value); break;
                case "height": height = ParseLength(value); break;
            }
        }
        return string.IsNullOrWhiteSpace(src) ? null : new MdImage(src.Trim(), alt ?? "", null, width, height);
    }

    /// <summary><c>600</c>, <c>600px</c> or <c>50%</c>; anything else is no size at all.</summary>
    internal static MdLength? ParseLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        bool percent = v.EndsWith('%');
        if (percent) v = v[..^1];
        else if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) v = v[..^2];
        return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 0
            ? new MdLength(n, percent)
            : null;
    }

    /// <summary>What an HTML block reads as with its markup gone: comments dropped, breaks
    /// kept, tags stripped, entities decoded, blank lines closed up.</summary>
    internal static string HtmlText(string html)
    {
        var text = Comment().Replace(html, "");
        text = BreakTag().Replace(text, "\n");
        text = AnyTag().Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }

    // An unclosed comment runs to the end of the block, as it does in a browser.
    [GeneratedRegex(@"<!--[\s\S]*?(-->|$)")]
    private static partial Regex Comment();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTag();

    [GeneratedRegex(@"^<br\s*/?>$", RegexOptions.IgnoreCase)]
    private static partial Regex LoneBreakTag();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTag();

    [GeneratedRegex(@"^<img\b[^>]*>$", RegexOptions.IgnoreCase)]
    private static partial Regex LoneImgTag();

    [GeneratedRegex(@"\b(?<name>src|alt|width|height)\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s""'>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex ImgAttribute();

    [GeneratedRegex(@"<details(?<open>\s[^>]*\bopen\b)?[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex DetailsOpen();

    [GeneratedRegex(@"</details\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex DetailsClose();

    [GeneratedRegex(@"<summary[^>]*>(?<text>[\s\S]*?)</summary\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex SummaryTag();

    [GeneratedRegex(@"</?[A-Za-z][^>]*>")]
    private static partial Regex AnyTag();
}

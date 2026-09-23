using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.TaskLists;
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
    public static MdText Plain(string text) => new(text, [], []);
}

/// <summary>
/// Reads GitLab-flavoured markdown — merge request descriptions and review notes — into
/// <see cref="MdDocument"/>. Markdig does the parsing (CommonMark, plus the strikethrough,
/// task-list and bare-URL extensions GitLab turns on); this walks its tree into the handful
/// of shapes <c>JunctionApp.Markdown</c> knows how to draw and measure.
/// </summary>
/// <remarks>
/// HTML is not drawn, but it is not shown raw either: comments go (templates are full of
/// them), <c>&lt;br&gt;</c> breaks the line, and any other tag drops out leaving its text —
/// so a <c>&lt;details&gt;</c> reads as its summary followed by its body, open.
/// </remarks>
internal static partial class MarkdownReader
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseTaskLists()
        .UseAutoLinks()
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

        var doc = new MdDocument(ReadBlocks(Markdig.Markdown.Parse(source.Replace("\r\n", "\n"), Pipeline)));
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
        _ => null,
    };

    // ── Blocks ───────────────────────────────────────────────────────────────────────

    private static List<MdBlock> ReadBlocks(ContainerBlock container)
    {
        var blocks = new List<MdBlock>();
        foreach (var block in container)
        {
            switch (block)
            {
                case HeadingBlock h:
                    if (ReadInline(h.Inline) is { Text.Length: > 0 } heading) blocks.Add(new MdHeading(h.Level, heading));
                    break;
                case ParagraphBlock p:
                    if (ReadInline(p.Inline) is { Text.Length: > 0 } text) blocks.Add(new MdParagraph(text));
                    break;
                case FencedCodeBlock fenced:
                    blocks.Add(new MdCode(FenceLanguage(fenced.Info), CodeText(fenced)));
                    break;
                case CodeBlock indented:
                    blocks.Add(new MdCode(null, CodeText(indented)));
                    break;
                case HtmlBlock html:
                    if (HtmlText(html.Lines.ToString()) is { Length: > 0 } shown) blocks.Add(new MdParagraph(MdText.Plain(shown)));
                    break;
                case ThematicBreakBlock:
                    blocks.Add(new MdRule());
                    break;
                case QuoteBlock quote:
                    if (ReadBlocks(quote) is { Count: > 0 } quoted) blocks.Add(new MdQuote(quoted));
                    break;
                case ListBlock list:
                    blocks.Add(ReadList(list));
                    break;
                case LinkReferenceDefinitionGroup:
                    break;                                   // [label]: url lines are not shown
                case ContainerBlock other:
                    blocks.AddRange(ReadBlocks(other));      // an extension's container: its contents
                    break;
                case LeafBlock { Inline: { } inline }:
                    if (ReadInline(inline) is { Text.Length: > 0 } leaf) blocks.Add(new MdParagraph(leaf));
                    break;
            }
        }
        return blocks;
    }

    private static MdList ReadList(ListBlock list)
    {
        var items = new List<MdItem>();
        foreach (var child in list)
        {
            if (child is not ListItemBlock item) continue;
            bool? task = item.Count > 0 && item[0] is ParagraphBlock { Inline.FirstChild: TaskList box } ? box.Checked : null;
            items.Add(new MdItem(task, ReadBlocks(item)));
        }
        int start = list.IsOrdered && int.TryParse(list.OrderedStart, out var s) ? s : 1;
        return new MdList(list.IsOrdered, start, list.IsLoose, items);
    }

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

    private static MdText? ReadInline(ContainerInline? inline)
    {
        if (inline is null) return null;
        var w = new InlineWriter();
        Walk(inline, MdStyle.None, w);
        return w.Finish();
    }

    private static void Walk(ContainerInline container, MdStyle style, InlineWriter w)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
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
                case LinkInline link:
                {
                    int start = w.Length;
                    if (link.IsImage)
                    {
                        // Images are not drawn yet; the alt text links to the picture instead.
                        var alt = new InlineWriter();
                        Walk(link, MdStyle.None, alt);
                        var label = alt.Finish().Text;
                        w.Append(label.Length > 0 ? label : "image", style);
                    }
                    else
                    {
                        Walk(link, style, w);
                        if (w.Length == start && link.Url is { Length: > 0 } bare) w.Append(bare, style);
                    }
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
                    if (BreakTag().IsMatch(html.Tag)) w.Append("\n", style);
                    break;                                   // any other tag drops out, its text stays
                case TaskList:
                    break;                                   // drawn as the item's marker
                case ContainerInline other:
                    Walk(other, style, w);
                    break;
            }
        }
    }

    private sealed class InlineWriter
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

    [GeneratedRegex(@"</?[A-Za-z][^>]*>")]
    private static partial Regex AnyTag();
}

using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.GitLab;
using Junction.Theme;

namespace Junction;

/// <summary>
/// Drawing <see cref="MdDocument"/>: a merge request's description and every note in a
/// review thread. Paragraphs are one wrapping label each, with bold, italic, struck and code
/// ranges as spans over it; headings are larger labels; lists hang their text off a marker
/// column; fenced code sits in a box, coloured by its fence's language.
/// </summary>
/// <remarks>
/// Every block has a builder and a height, and both read the constants below. That pairing
/// is load-bearing: a thread card is a row of the virtualised diff, sized from
/// <see cref="MarkdownHeight"/> before it is built, so the two must agree to the pixel.
/// Inline code keeps the body's face — a span cannot change family yet — and is set apart by
/// a tinted ground instead.
/// </remarks>
public partial class JunctionApp
{
    /// <summary>How a body is set: its size and its ink. Headings, code and quotes derive
    /// theirs from it.</summary>
    private readonly record struct MdLook(float Size, Color Ink);

    /// <summary>What links in a body resolve against: the project, for relative URLs, and the
    /// rules that turn <c>#123</c> and <c>!45</c> into links.</summary>
    private readonly record struct MdLinkContext(GitLabRemote? Remote, IReadOnlyList<IssueTrackerRule> Rules);

    private const float MdBlockGap = 8f;
    private const float MdItemGap = 3f, MdLooseItemGap = 8f, MdBulletWidth = 16f;
    private const float MdQuoteBar = 3f, MdQuoteGap = 10f;
    private const float MdCodePadY = 7f, MdCodePadX = 10f, MdCodeBorder = 1f;

    private static float MdHeadingSize(int level, float size) => level switch
    {
        1 => size * 1.4f,
        2 => size * 1.25f,
        3 => size * 1.1f,
        _ => size,
    };

    private static float MdCodeSize(float size) => size - 1f;

    private static float MdLineHeight(float size) => TextMeasure.Measure("X", TextMeasure.StyleFor(size)).Height;

    // ── Build ────────────────────────────────────────────────────────────────────────

    private Element MarkdownView(MdDocument doc, MdLook look) =>
        MdBlocksView(doc.Blocks, look, new MdLinkContext(GitLabRemoteFor(_activeRepoPath), IssueRules()), MdBlockGap, 0);

    private Element MdBlocksView(IReadOnlyList<MdBlock> blocks, MdLook look, MdLinkContext links, float gap, int depth)
    {
        var col = Flex.Column(gap: gap);
        foreach (var block in blocks) col.AddChild(MdBlockView(block, look, links, depth));
        return col;
    }

    private Element MdBlockView(MdBlock block, MdLook look, MdLinkContext links, int depth) => block switch
    {
        MdParagraph p => MdLabel(p.Text, look.Size, FontWeight.Regular, look.Ink, links),
        MdHeading h => MdLabel(h.Text, MdHeadingSize(h.Level, look.Size), FontWeight.SemiBold, Nocturne.Text, links),
        MdCode c => MdCodeView(c, look),
        MdQuote q => MdQuoteView(q, look, links, depth),
        MdList l => MdListView(l, look, links, depth),
        MdRule => Box.New().Height(1).Background(Nocturne.Divider),
        _ => Box.New(),
    };

    private TextElement MdLabel(MdText t, float size, FontWeight weight, Color ink, MdLinkContext links)
    {
        var label = Text.New(t.Text).FontSize(size).TextColor(ink);
        if (weight != FontWeight.Regular) label.FontWeight(weight);
        var targets = MdTargets(t, links);
        if (MdSpans(t, targets) is { Count: > 0 } spans) label.Spans(spans);
        if (targets.Count > 0) label.Links(targets, OpenUrl);
        return label;
    }

    private static Element MdCodeView(MdCode c, MdLook look)
    {
        var code = Ui.Mono(c.Code, MdCodeSize(look.Size), look.Ink);
        if (MdCodeSpans(c) is { Count: > 0 } spans) code.Spans(spans);
        return Flex.Column(gap: 0)
            .Padding(new Thickness(MdCodePadY, MdCodePadX, MdCodePadY, MdCodePadX))
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(MdCodeBorder)
            .CornerRadius(Nocturne.RadiusMd)
            .Children(code);
    }

    private Element MdQuoteView(MdQuote q, MdLook look, MdLinkContext links, int depth)
    {
        var row = Flex.Row(gap: MdQuoteGap).AlignItems(AlignItems.Stretch);
        row.AddChild(Box.New().Width(MdQuoteBar).FlexShrink(0).CornerRadius(MdQuoteBar / 2).Background(Nocturne.On(0.22)));
        row.AddChild(MdBlocksView(q.Blocks, look with { Ink = Nocturne.On(0.62) }, links, MdBlockGap, depth)
            .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0)));
        return row;
    }

    private Element MdListView(MdList l, MdLook look, MdLinkContext links, int depth)
    {
        var (itemGap, innerGap) = MdListGaps(l);
        float markerWidth = MdMarkerWidth(l, look), line = MdLineHeight(look.Size);
        var col = Flex.Column(gap: itemGap);
        for (int i = 0; i < l.Items.Count; i++)
        {
            var item = l.Items[i];
            var row = Flex.Row(gap: 0).AlignItems(AlignItems.FlexStart);
            row.AddChild(MdMarker(l, item, l.Start + i, depth, look).Width(markerWidth).Height(line).FlexShrink(0));
            row.AddChild(MdBlocksView(item.Blocks, look, links, innerGap, depth + 1)
                .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0)));
            col.AddChild(row);
        }
        return col;
    }

    /// <summary>A bullet that changes with depth, the item's number, or its task box.</summary>
    private static Element MdMarker(MdList l, MdItem item, int number, int depth, MdLook look)
    {
        if (item.Checked is { } done)
            return Flex.Column(gap: 0).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.FlexStart)
                .Children(Ui.Icon(done ? Icons.CheckSquare : Icons.Square, look.Size + 1f, done ? Nocturne.Accent300 : Nocturne.On(0.5)));
        var glyph = l.Ordered ? $"{number}." : (depth % 3) switch { 0 => "•", 1 => "◦", _ => "▪" };
        return Text.New(glyph).FontSize(look.Size).TextColor(Nocturne.On(0.55));
    }

    // ── Measure ──────────────────────────────────────────────────────────────────────

    /// <summary>How tall <see cref="MarkdownView"/> draws <paramref name="doc"/> in
    /// <paramref name="width"/>: the same blocks, the same gaps, the same spans.</summary>
    private static float MarkdownHeight(MdDocument doc, MdLook look, float width) =>
        MdBlocksHeight(doc.Blocks, look, width, MdBlockGap);

    private static float MdBlocksHeight(IReadOnlyList<MdBlock> blocks, MdLook look, float width, float gap)
    {
        float h = 0;
        for (int i = 0; i < blocks.Count; i++)
            h += (i > 0 ? gap : 0) + MdBlockHeight(blocks[i], look, width);
        return h;
    }

    private static float MdBlockHeight(MdBlock block, MdLook look, float width) => block switch
    {
        MdParagraph p => MdTextHeight(p.Text, look.Size, FontWeight.Regular, width),
        MdHeading h => MdTextHeight(h.Text, MdHeadingSize(h.Level, look.Size), FontWeight.SemiBold, width),
        MdCode c => TextMeasure.MeasureWrapped(c.Code, TextMeasure.StyleFor(MdCodeSize(look.Size), Fonts.Mono),
                        width - 2 * (MdCodePadX + MdCodeBorder)).Height + 2 * (MdCodePadY + MdCodeBorder),
        MdQuote q => MdBlocksHeight(q.Blocks, look, width - MdQuoteBar - MdQuoteGap, MdBlockGap),
        MdList l => MdListHeight(l, look, width),
        MdRule => 1f,
        _ => 0f,
    };

    // Link spans only paint, so the height is measured without resolving them.
    private static float MdTextHeight(MdText t, float size, FontWeight weight, float width) =>
        TextMeasure.MeasureWrapped(t.Text, TextMeasure.StyleFor(size, fontWeight: weight), width, MdSpans(t, [])).Height;

    private static float MdListHeight(MdList l, MdLook look, float width)
    {
        var (itemGap, innerGap) = MdListGaps(l);
        float marker = MdMarkerWidth(l, look), line = MdLineHeight(look.Size), h = 0;
        for (int i = 0; i < l.Items.Count; i++)
            h += (i > 0 ? itemGap : 0) + Math.Max(line, MdBlocksHeight(l.Items[i].Blocks, look, width - marker, innerGap));
        return h;
    }

    // ── Shared by both ───────────────────────────────────────────────────────────────

    /// <summary>A tight list keeps its items, and the blocks inside each, close; a loose one
    /// (items separated by blank lines) spaces them like paragraphs.</summary>
    private static (float Items, float Inner) MdListGaps(MdList l) =>
        l.Loose ? (MdLooseItemGap, MdBlockGap) : (MdItemGap, MdItemGap);

    private static float MdMarkerWidth(MdList l, MdLook look)
    {
        float w = MdBulletWidth;
        if (l.Items.Any(i => i.Checked is not null)) w = Math.Max(w, look.Size + 9f);
        if (l.Ordered) w = Math.Max(w, TextMeasure.MeasureWidth($"{l.Start + l.Items.Count - 1}.", look.Size) + 6f);
        return MathF.Ceiling(w);
    }

    /// <summary>The label's spans. Bold and italic change how it shapes, which is why the
    /// height passes them too; code, strikethrough and links only paint.</summary>
    private static List<TextSpan> MdSpans(MdText t, IReadOnlyList<TextLink> targets)
    {
        var spans = new List<TextSpan>();
        int n = t.Text.Length;
        if (n == 0 || (t.Runs.Count == 0 && targets.Count == 0)) return spans;
        var style = new MdStyle[n];
        var linked = new bool[n];
        foreach (var r in t.Runs)
            for (int i = r.Start; i < r.End && i < n; i++) style[i] |= r.Style;
        foreach (var l in targets)
            for (int i = l.Start; i < l.End && i < n; i++) linked[i] = true;
        for (int i = 0; i < n;)
        {
            int j = i + 1;
            while (j < n && style[j] == style[i] && linked[j] == linked[i]) j++;
            if (style[i] != MdStyle.None || linked[i]) spans.Add(MdSpan(i, j - i, style[i], linked[i]));
            i = j;
        }
        return spans;
    }

    private static TextSpan MdSpan(int start, int length, MdStyle s, bool linked)
    {
        bool code = s.HasFlag(MdStyle.Code);
        return new TextSpan(start, length,
            linked ? Nocturne.Accent300 : code ? Nocturne.Text : default,
            code ? Nocturne.On(0.1) : default,
            linked ? TextDecoration.Underline : s.HasFlag(MdStyle.Strike) ? TextDecoration.LineThrough : TextDecoration.None,
            s.HasFlag(MdStyle.Strong) ? FontWeight.SemiBold : null,
            s.HasFlag(MdStyle.Emphasis) ? FontSlant.Italic : null);
    }

    /// <summary>The label's clickable ranges: its markdown links that resolve, and the
    /// <c>#123</c> / <c>!45</c> references a commit message would link — except inside code,
    /// or inside a link that already goes somewhere.</summary>
    private static List<TextLink> MdTargets(MdText t, MdLinkContext links)
    {
        var targets = new List<TextLink>();
        foreach (var l in t.Links)
            if (ResolveMarkdownUrl(l.Url, links.Remote) is { } url) targets.Add(new TextLink(l.Start, l.Length, url));
        foreach (var r in IssueTrackerFile.Find(t.Text, links.Rules))
        {
            bool taken = t.Links.Any(l => l.Start < r.End && r.Start < l.End)
                || t.Runs.Any(run => run.Style.HasFlag(MdStyle.Code) && run.Start < r.End && r.Start < run.End);
            if (!taken) targets.Add(new TextLink(r.Start, r.Length, r.Url));
        }
        targets.Sort((a, b) => a.Start.CompareTo(b.Start));
        return targets;
    }

    /// <summary>
    /// Where a link in a description or note goes. Absolute web and mail links go where they
    /// say; <c>/uploads/…</c> is the project's, and any other rooted path the instance's, as
    /// GitLab reads them. Anything else — a bare relative path, an anchor — has no page to
    /// open outside the browser that rendered it, and stays plain text.
    /// </summary>
    internal static string? ResolveMarkdownUrl(string url, GitLabRemote? remote)
    {
        url = url.Trim();
        if (url.Length == 0) return null;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return url;
        if (remote is null || !url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal)) return null;
        return url.StartsWith("/uploads/", StringComparison.Ordinal)
            ? $"{remote.WebBaseUrl}/{remote.ProjectPath}{url}"
            : remote.WebBaseUrl + url;
    }

    // Tokenizing is the one costly step in drawing a body, and a thread card rebuilds on
    // every keystroke in its reply field. Keyed on the syntax theme too: a theme switch
    // recolours code, and spans already handed out keep the colours they were made with.
    private static readonly Dictionary<(string Theme, string Language, string Code), IReadOnlyList<TextSpan>> MdCodeSpanCache = [];

    private static IReadOnlyList<TextSpan> MdCodeSpans(MdCode c)
    {
        if (c.Language is not { Length: > 0 } language || c.Code.Length == 0) return [];
        var key = (Nocturne.Theme.SyntaxTheme, language, c.Code);
        if (MdCodeSpanCache.TryGetValue(key, out var spans)) return spans;
        if (MdCodeSpanCache.Count >= 256) MdCodeSpanCache.Clear();
        return MdCodeSpanCache[key] = SyntaxHighlighter.SpansForLanguage(language, c.Code);
    }
}

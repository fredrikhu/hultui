using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using Junction.Git;
using Junction.GitLab;
using Junction.Theme;

namespace Junction;

/// <summary>
/// Drawing <see cref="MdDocument"/>: a merge request's description and every note in a
/// review thread. Paragraphs are one wrapping label each, with bold, italic, struck and code
/// ranges as spans over it; headings are larger labels; lists hang their text off a marker
/// column; fenced code sits in a box, coloured by its fence's language; tables are rows of
/// flexed cells between hairlines; a details section opens on its summary; pictures load in
/// the background and scale down to the width they are given.
/// </summary>
/// <remarks>
/// Every block has a builder and a height, and both read the constants below. That pairing
/// is load-bearing: a thread card is a row of the virtualised diff, sized from
/// <see cref="MarkdownHeight"/> before it is built, so the two must agree to the pixel. What
/// changes a body's height after it was measured — a picture arriving, a section opening —
/// bumps <see cref="_mdLayoutGen"/>, which the review's row signature reads.
/// Inline code keeps the body's face — a span cannot change family yet — and is set apart by
/// a tinted ground instead.
/// </remarks>
public partial class JunctionApp
{
    /// <summary>How a body is set: its size and its ink. Headings, code and quotes derive
    /// theirs from it.</summary>
    private readonly record struct MdLook(float Size, Color Ink);

    /// <summary>What a body resolves against: the project, for relative URLs and uploads; the
    /// rules that turn <c>#123</c> and <c>!45</c> into links; and who is reading, whose
    /// <c>@name</c> stands out.</summary>
    private readonly record struct MdContext(GitLabRemote? Remote, IReadOnlyList<IssueTrackerRule> Rules, string? Me);

    private const float MdBlockGap = 8f;
    private const float MdItemGap = 3f, MdLooseItemGap = 8f, MdBulletWidth = 16f;
    private const float MdQuoteBar = 3f, MdQuoteGap = 10f;
    private const float MdCodePadY = 7f, MdCodePadX = 10f, MdCodeBorder = 1f;
    private const float MdCellPadY = 4f, MdCellPadX = 8f, MdHairline = 1f;
    private const float MdCaretWidth = 12f, MdCaretGap = 6f, MdDetailsIndent = 18f, MdSummaryGap = 6f;
    private const float MdPictureMaxHeight = 420f, MdPictureLine = 22f;

    /// <summary>Bumped when a body already measured would now draw at another height: a
    /// picture has arrived, a section has opened or closed.</summary>
    private int _mdLayoutGen;

    /// <summary>The sections the reader has opened or closed, by <see cref="MdDetails.Key"/>;
    /// one not here is as its source says.</summary>
    private readonly Dictionary<string, bool> _mdDetailsOpen = new(StringComparer.Ordinal);

    private static float MdHeadingSize(int level, float size) => level switch
    {
        1 => size * 1.4f,
        2 => size * 1.25f,
        3 => size * 1.1f,
        _ => size,
    };

    private static float MdCodeSize(float size) => size - 1f;

    private static float MdLineHeight(float size) => TextMeasure.Measure("X", TextMeasure.StyleFor(size)).Height;

    private MdContext MdContextFor(string repoPath)
    {
        var remote = GitLabRemoteFor(repoPath);
        return new MdContext(remote, IssueRules(), remote is null ? null : _gitlab.For(remote.Host)?.Username);
    }

    // ── Build ────────────────────────────────────────────────────────────────────────

    private Element MarkdownView(MdDocument doc, MdLook look) =>
        MdBlocksView(doc.Blocks, look, MdContextFor(_activeRepoPath), MdBlockGap, 0);

    private Element MdBlocksView(IReadOnlyList<MdBlock> blocks, MdLook look, MdContext cx, float gap, int depth)
    {
        var col = Flex.Column(gap: gap);
        foreach (var block in blocks) col.AddChild(MdBlockView(block, look, cx, depth));
        return col;
    }

    private Element MdBlockView(MdBlock block, MdLook look, MdContext cx, int depth) => block switch
    {
        MdParagraph p => MdLabel(p.Text, look.Size, FontWeight.Regular, look.Ink, cx),
        MdHeading h => MdLabel(h.Text, MdHeadingSize(h.Level, look.Size), FontWeight.SemiBold, Nocturne.Text, cx),
        MdCode c => MdCodeView(c, look),
        MdQuote q => MdQuoteView(q, look, cx, depth),
        MdList l => MdListView(l, look, cx, depth),
        MdTable t => MdTableView(t, look, cx),
        MdDetails d => MdDetailsView(d, look, cx, depth),
        MdImage i => MdImageView(i, look, cx),
        MdRule => Box.New().Height(1).Background(Nocturne.Divider),
        _ => Box.New(),
    };

    private TextElement MdLabel(MdText t, float size, FontWeight weight, Color ink, MdContext cx)
    {
        var label = Text.New(t.Text).FontSize(size).TextColor(ink);
        if (weight != FontWeight.Regular) label.FontWeight(weight);
        var marks = MdMarks(t, cx);
        if (MdSpans(t, marks) is { Count: > 0 } spans) label.Spans(spans);
        var links = marks.Where(m => m.Url is not null).Select(m => new TextLink(m.Start, m.Length, m.Url!)).ToList();
        if (links.Count > 0) label.Links(links, OpenUrl);
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

    private Element MdQuoteView(MdQuote q, MdLook look, MdContext cx, int depth)
    {
        var row = Flex.Row(gap: MdQuoteGap).AlignItems(AlignItems.Stretch);
        row.AddChild(Box.New().Width(MdQuoteBar).FlexShrink(0).CornerRadius(MdQuoteBar / 2).Background(Nocturne.On(0.22)));
        row.AddChild(MdBlocksView(q.Blocks, look with { Ink = Nocturne.On(0.62) }, cx, MdBlockGap, depth)
            .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0)));
        return row;
    }

    private Element MdListView(MdList l, MdLook look, MdContext cx, int depth)
    {
        var (itemGap, innerGap) = MdListGaps(l);
        float markerWidth = MdMarkerWidth(l, look), line = MdLineHeight(look.Size);
        var col = Flex.Column(gap: itemGap);
        for (int i = 0; i < l.Items.Count; i++)
        {
            var item = l.Items[i];
            var row = Flex.Row(gap: 0).AlignItems(AlignItems.FlexStart);
            row.AddChild(MdMarker(l, item, l.Start + i, depth, look).Width(markerWidth).Height(line).FlexShrink(0));
            row.AddChild(MdBlocksView(item.Blocks, look, cx, innerGap, depth + 1)
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

    /// <summary>
    /// A grid of flexed cells between hairlines. Every row's cells grow by the same weights,
    /// so the columns line up without a width being known up front — the description's panel
    /// narrows with the window — and <see cref="MdTableHeight"/> reaches the same widths by
    /// the same arithmetic. The cells carry no padding of their own: a flex item's padding is
    /// a floor under its basis, which would make the widths depend on the engine's rule for
    /// it. The padding is on the label inside.
    /// </summary>
    private Element MdTableView(MdTable t, MdLook look, MdContext cx)
    {
        var weights = MdColumnWeights(t, look);
        var table = Flex.Column(gap: 0).BorderColor(Nocturne.Divider).BorderWidth(MdHairline);
        for (int r = 0; r < t.Rows.Count; r++)
        {
            var source = t.Rows[r];
            if (r > 0) table.AddChild(Box.New().Height(MdHairline).FlexShrink(0).Background(Nocturne.Divider));
            var row = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch);
            if (source.Header) row.Background(Nocturne.On(0.05));
            for (int c = 0; c < t.Columns; c++)
            {
                if (c > 0) row.AddChild(Box.New().Width(MdHairline).FlexShrink(0).Background(Nocturne.Divider));
                var text = c < source.Cells.Count ? source.Cells[c] : MdText.Empty;
                var label = MdLabel(text, look.Size, MdCellWeight(source), source.Header ? Nocturne.Text : look.Ink, cx)
                    .Padding(new Thickness(MdCellPadY, MdCellPadX, MdCellPadY, MdCellPadX))
                    .TextAlign(t.Align[c] switch { MdAlign.Center => TextAlign.Center, MdAlign.End => TextAlign.End, _ => TextAlign.Start });
                row.AddChild(Flex.Column(gap: 0).FlexGrow(weights[c]).FlexShrink(1).FlexBasis(Dimension.Px(0))
                    .MinWidth(Dimension.Px(0)).Children(label));
            }
            table.AddChild(row);
        }
        return table;
    }

    private Element MdDetailsView(MdDetails d, MdLook look, MdContext cx, int depth)
    {
        bool open = MdDetailsIsOpen(d);
        var col = Flex.Column(gap: MdSummaryGap);
        var head = Flex.Row(gap: MdCaretGap).AlignItems(AlignItems.FlexStart).Cursor(CursorType.Pointer);
        head.AddChild(Flex.Column(gap: 0).Width(MdCaretWidth).Height(MdLineHeight(look.Size)).FlexShrink(0)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(open ? Icons.CaretDown : Icons.CaretRight, 11f, Nocturne.On(0.6))));
        head.AddChild(MdLabel(d.Summary, look.Size, FontWeight.Medium, Nocturne.Text, cx)
            .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0)));
        head.AccessibleName = (open ? "Hide " : "Show ") + d.Summary.Text;
        head.AutomationId = "md-details";
        head.OnClick(_ => ToggleDetails(d));
        col.AddChild(head);
        if (open)
            col.AddChild(MdBlocksView(d.Blocks, look, cx, MdBlockGap, depth)
                .Padding(new Thickness(0, 0, 0, MdDetailsIndent)));
        return col;
    }

    private bool MdDetailsIsOpen(MdDetails d) => _mdDetailsOpen.TryGetValue(d.Key, out var open) ? open : d.Open;

    private void ToggleDetails(MdDetails d)
    {
        _mdDetailsOpen[d.Key] = !MdDetailsIsOpen(d);
        _mdLayoutGen++;
        MarkDirty();
    }

    private Element MdImageView(MdImage img, MdLook look, MdContext cx)
    {
        var url = ResolveMarkdownUrl(img.Url, cx.Remote);
        var target = (img.Link is { } link ? ResolveMarkdownUrl(link, cx.Remote) : null) ?? url;
        var picture = MdPictureFor(url, cx);
        if (picture.Source is { IsLoaded: true } source)
        {
            var image = Image.New(source).AlignSelf(HultUI.Core.Layout.AlignSelf.FlexStart);
            image.ScaleDownToFit = true;
            image.NaturalWidth = MdNaturalWidth(img, source.IntrinsicSize);
            image.MaxImageHeight = MdPictureMaxHeight;
            image.AccessibleName = img.Alt.Length > 0 ? img.Alt : "Image";
            image.AutomationId = "md-image";
            if (target is not null)
            {
                image.Cursor(CursorType.Pointer).Tooltip(img.Alt.Length > 0 ? $"{img.Alt} — {target}" : target);
                image.OnClick(_ => OpenUrl(target));
            }
            return image;
        }

        // Not (yet) a picture: a line saying so, which is a link to it once it has failed.
        var alt = img.Alt.Length > 0 ? img.Alt : "image";
        var line = Flex.Row(gap: 6).AlignItems(AlignItems.Center).Height(MdPictureLine);
        line.AddChild(Ui.Icon(picture.Failed ? Icons.LinkSimple : Icons.FileImage, 13f, Nocturne.On(0.5)).FlexShrink(0));
        var text = picture.Failed
            ? new MdText(alt, [], [new MdLink(0, alt.Length, img.Link ?? img.Url)])
            : MdText.Plain(alt + " — loading…");
        line.AddChild(MdLabel(text, look.Size, FontWeight.Regular, Nocturne.On(0.55), cx)
            .MaxLines(1).OverflowMode(TextOverflow.Ellipsis).FlexShrink(1));
        line.AutomationId = picture.Failed ? "md-image-failed" : "md-image-loading";
        return line;
    }

    // ── Measure ──────────────────────────────────────────────────────────────────────

    /// <summary>How tall <see cref="MarkdownView"/> draws <paramref name="doc"/> in
    /// <paramref name="width"/>: the same blocks, the same gaps, the same spans.</summary>
    private float MarkdownHeight(MdDocument doc, MdLook look, float width) =>
        MdBlocksHeight(doc.Blocks, look, MdContextFor(_activeRepoPath), width, MdBlockGap);

    private float MdBlocksHeight(IReadOnlyList<MdBlock> blocks, MdLook look, MdContext cx, float width, float gap)
    {
        float h = 0;
        for (int i = 0; i < blocks.Count; i++)
            h += (i > 0 ? gap : 0) + MdBlockHeight(blocks[i], look, cx, width);
        return h;
    }

    private float MdBlockHeight(MdBlock block, MdLook look, MdContext cx, float width) => block switch
    {
        MdParagraph p => MdTextHeight(p.Text, look.Size, FontWeight.Regular, width),
        MdHeading h => MdTextHeight(h.Text, MdHeadingSize(h.Level, look.Size), FontWeight.SemiBold, width),
        MdCode c => TextMeasure.MeasureWrapped(c.Code, TextMeasure.StyleFor(MdCodeSize(look.Size), Fonts.Mono),
                        width - 2 * (MdCodePadX + MdCodeBorder)).Height + 2 * (MdCodePadY + MdCodeBorder),
        MdQuote q => MdBlocksHeight(q.Blocks, look, cx, width - MdQuoteBar - MdQuoteGap, MdBlockGap),
        MdList l => MdListHeight(l, look, cx, width),
        MdTable t => MdTableHeight(t, look, width),
        MdDetails d => MdDetailsHeight(d, look, cx, width),
        MdImage i => MdImageHeight(i, cx, width),
        MdRule => 1f,
        _ => 0f,
    };

    // Link and mention spans only paint, so the height is measured without resolving them.
    private static float MdTextHeight(MdText t, float size, FontWeight weight, float width) =>
        TextMeasure.MeasureWrapped(t.Text, TextMeasure.StyleFor(size, fontWeight: weight), width, MdSpans(t, [])).Height;

    private float MdListHeight(MdList l, MdLook look, MdContext cx, float width)
    {
        var (itemGap, innerGap) = MdListGaps(l);
        float marker = MdMarkerWidth(l, look), line = MdLineHeight(look.Size), h = 0;
        for (int i = 0; i < l.Items.Count; i++)
            h += (i > 0 ? itemGap : 0) + Math.Max(line, MdBlocksHeight(l.Items[i].Blocks, look, cx, width - marker, innerGap));
        return h;
    }

    private static float MdTableHeight(MdTable t, MdLook look, float width)
    {
        var weights = MdColumnWeights(t, look);
        float total = weights.Sum();
        float free = width - 2 * MdHairline - (t.Columns - 1) * MdHairline;
        float h = 2 * MdHairline;
        for (int r = 0; r < t.Rows.Count; r++)
        {
            var row = t.Rows[r];
            if (r > 0) h += MdHairline;
            float tallest = 0;
            for (int c = 0; c < t.Columns; c++)
            {
                var text = c < row.Cells.Count ? row.Cells[c] : MdText.Empty;
                float cell = free * weights[c] / total;
                tallest = Math.Max(tallest, MdTextHeight(text, look.Size, MdCellWeight(row), cell - 2 * MdCellPadX) + 2 * MdCellPadY);
            }
            h += tallest;
        }
        return h;
    }

    private float MdDetailsHeight(MdDetails d, MdLook look, MdContext cx, float width)
    {
        float head = Math.Max(MdLineHeight(look.Size),
            MdTextHeight(d.Summary, look.Size, FontWeight.Medium, width - MdCaretWidth - MdCaretGap));
        return MdDetailsIsOpen(d)
            ? head + MdSummaryGap + MdBlocksHeight(d.Blocks, look, cx, width - MdDetailsIndent, MdBlockGap)
            : head;
    }

    private float MdImageHeight(MdImage img, MdContext cx, float width) =>
        MdPictureFor(ResolveMarkdownUrl(img.Url, cx.Remote), cx).Source is { IsLoaded: true } source
            ? ImageElement.ScaledSize(source.IntrinsicSize, MdNaturalWidth(img, source.IntrinsicSize), width, MdPictureMaxHeight).Height
            : MdPictureLine;

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

    private static FontWeight MdCellWeight(MdTableRow row) => row.Header ? FontWeight.SemiBold : FontWeight.Regular;

    /// <summary>How the columns share the width: each by its widest cell on one line, held
    /// between a floor that keeps a short column legible and a ceiling that stops one long
    /// cell starving the rest. Whole pixels, so both sides compute the same shares.</summary>
    private static float[] MdColumnWeights(MdTable t, MdLook look)
    {
        var weights = new float[t.Columns];
        foreach (var row in t.Rows)
            for (int c = 0; c < row.Cells.Count && c < t.Columns; c++)
                weights[c] = Math.Max(weights[c], TextMeasure.MeasureWidth(row.Cells[c].Text, look.Size, fontWeight: MdCellWeight(row)));
        for (int c = 0; c < weights.Length; c++)
            weights[c] = MathF.Round(Math.Clamp(weights[c] + 2 * MdCellPadX, 48f, 360f));
        return weights;
    }

    /// <summary>The width the source asked for, in layout units: <c>{width=600}</c>, or a
    /// height alone turned into the width that keeps the ratio. A percentage asks for nothing
    /// — the picture keeps its own width, scaled down to fit as any other.</summary>
    private static float? MdNaturalWidth(MdImage img, Size intrinsic)
    {
        if (img.Width is { Percent: false } w) return w.Value;
        if (img.Height is { Percent: false } h && intrinsic.Height > 0) return h.Value * intrinsic.Width / intrinsic.Height;
        return null;
    }

    /// <summary>How a range of a label is marked beyond its markdown style.</summary>
    private enum MdMark : byte { Link, Mention, MentionOfMe }

    private readonly record struct MdMarked(int Start, int Length, string? Url, MdMark Mark)
    {
        public int End => Start + Length;
    }

    /// <summary>The label's spans. Bold and italic change how it shapes, which is why the
    /// height passes them too; code, strikethrough, links and mentions only paint.</summary>
    private static List<TextSpan> MdSpans(MdText t, IReadOnlyList<MdMarked> marks)
    {
        var spans = new List<TextSpan>();
        int n = t.Text.Length;
        if (n == 0 || (t.Runs.Count == 0 && marks.Count == 0)) return spans;
        var style = new MdStyle[n];
        var mark = new int[n];   // 0 none, else MdMark + 1
        foreach (var r in t.Runs)
            for (int i = r.Start; i < r.End && i < n; i++) style[i] |= r.Style;
        foreach (var m in marks)
            for (int i = m.Start; i < m.End && i < n; i++) mark[i] = (int)m.Mark + 1;
        for (int i = 0; i < n;)
        {
            int j = i + 1;
            while (j < n && style[j] == style[i] && mark[j] == mark[i]) j++;
            if (style[i] != MdStyle.None || mark[i] != 0)
                spans.Add(MdSpan(i, j - i, style[i], mark[i] == 0 ? null : (MdMark)(mark[i] - 1)));
            i = j;
        }
        return spans;
    }

    private static TextSpan MdSpan(int start, int length, MdStyle s, MdMark? mark)
    {
        bool code = s.HasFlag(MdStyle.Code);
        var (ink, ground) = mark switch
        {
            MdMark.Link => (Nocturne.Accent300, default(Color)),
            MdMark.Mention => (Nocturne.Accent300, Nocturne.Alpha(Nocturne.Accent, 0.14)),
            MdMark.MentionOfMe => (Nocturne.Text, Nocturne.Alpha(Nocturne.MarkAmber, 0.28)),
            _ => (code ? Nocturne.Text : default, code ? Nocturne.On(0.1) : default),
        };
        return new TextSpan(start, length, ink, ground,
            mark == MdMark.Link ? TextDecoration.Underline : s.HasFlag(MdStyle.Strike) ? TextDecoration.LineThrough : TextDecoration.None,
            s.HasFlag(MdStyle.Strong) ? FontWeight.SemiBold : null,
            s.HasFlag(MdStyle.Emphasis) ? FontSlant.Italic : null);
    }

    /// <summary>
    /// The label's marked ranges: its markdown links that resolve; the <c>#123</c> / <c>!45</c>
    /// references a commit message would link; and <c>@name</c> mentions, linking to the
    /// person or group, the reader's own set apart as GitLab sets it apart. None inside code,
    /// or inside a link that already goes somewhere.
    /// </summary>
    private static List<MdMarked> MdMarks(MdText t, MdContext cx)
    {
        var marks = new List<MdMarked>();
        foreach (var l in t.Links)
            if (ResolveMarkdownUrl(l.Url, cx.Remote) is { } url) marks.Add(new MdMarked(l.Start, l.Length, url, MdMark.Link));

        bool Taken(int start, int end) =>
            t.Links.Any(l => l.Start < end && start < l.End)
            || t.Runs.Any(run => run.Style.HasFlag(MdStyle.Code) && run.Start < end && start < run.End);

        foreach (var r in IssueTrackerFile.Find(t.Text, cx.Rules))
            if (!Taken(r.Start, r.End)) marks.Add(new MdMarked(r.Start, r.Length, r.Url, MdMark.Link));

        foreach (Match m in Mention().Matches(t.Text))
        {
            var name = m.Groups["name"].Value.TrimEnd('.', '-');
            int start = m.Index, length = name.Length + 1;
            if (name.Length == 0 || Taken(start, start + length)) continue;
            bool everyone = name is "all" or "here";
            bool me = cx.Me is { Length: > 0 } self && string.Equals(name, self, StringComparison.OrdinalIgnoreCase);
            string? url = everyone || cx.Remote is null ? null : $"{cx.Remote.WebBaseUrl}/{name}";
            marks.Add(new MdMarked(start, length, url, me || everyone ? MdMark.MentionOfMe : MdMark.Mention));
        }
        marks.Sort((a, b) => a.Start.CompareTo(b.Start));
        return marks;
    }

    /// <summary>A GitLab username or group path after an <c>@</c> that no word, address or path
    /// runs into — so <c>dana@example.com</c> is not a mention.</summary>
    [GeneratedRegex(@"(?<![\w@./+-])@(?<name>[A-Za-z0-9_][A-Za-z0-9_.-]*(?:/[A-Za-z0-9_][A-Za-z0-9_.-]*)*)")]
    private static partial Regex Mention();

    /// <summary>
    /// Where a link in a description or note goes. Absolute web and mail links go where they
    /// say; <c>/uploads/…</c> is the project's, and any other rooted path the instance's, as
    /// GitLab reads them. Anything else — a bare relative path, an anchor — has no page to
    /// open outside the browser that rendered it, and stays plain text.
    /// </summary>
    internal static string? ResolveMarkdownUrl(string? url, GitLabRemote? remote)
    {
        url = url?.Trim();
        if (string.IsNullOrEmpty(url)) return null;
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

    // ── Pictures ─────────────────────────────────────────────────────────────────────

    /// <summary>A picture's state: loading until <see cref="Source"/> arrives, or failed.</summary>
    private sealed record MdPicture(IImageSource? Source, bool Failed)
    {
        public static readonly MdPicture Loading = new(null, false);
        public static readonly MdPicture Broken = new(null, true);
    }

    // Bounded AND disposing, like the image diff's: every slot pins a decoded bitmap — at
    // most MdPictureMaxPixels on its long side, so the whole cache stays in the low hundreds
    // of megabytes even full of 4K screenshots.
    private readonly KeyedAsyncLoader<string, MdPicture> _mdPictures =
        new(MdPicture.Loading, capacity: 24, onEvicted: p => (p.Source as IDisposable)?.Dispose(), comparer: StringComparer.Ordinal);

    /// <summary>The longest side a picture is kept at. It is drawn at most a panel wide, so a
    /// 4K screenshot decoded whole would hold ~33 MB for pixels nobody sees; this is still
    /// sharp at twice the widest a card gets.</summary>
    private const int MdPictureMaxPixels = 2048;

    private readonly ConcurrentDictionary<string, HttpClient> _mdPlainClients = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the file would not decode as, or is too large to try: moving pictures and
    /// vector drawings are links rather than downloads.</summary>
    private static readonly HashSet<string> MdNotPictures = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".webm", ".ogv", ".avi", ".mkv", ".m4v", ".svg", ".pdf" };

    private MdPicture MdPictureFor(string? url, MdContext cx)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
            || MdNotPictures.Contains(Path.GetExtension(uri.AbsolutePath)))
            return MdPicture.Broken;
        var loader = _mdPictures.For(url);
        var remote = cx.Remote;
        // Arriving changes the height of a row already measured, so the rows are measured again
        // — asked for here, not left to the loader's dependency: that is only kept by the build
        // that last read it, and the heights are read only when the row shape changes. A build
        // in between (a spinner's frame, a hover) dropped it, the picture landed into nobody,
        // and the row then drew it inside the height it had without it.
        loader.Load(url, () => LoadPicture(uri, remote), onLoaded: _ => { _mdLayoutGen++; MarkDirty(); });
        return loader.Value;
    }

    // Runs off the UI thread.
    private MdPicture LoadPicture(Uri uri, GitLabRemote? remote)
    {
        try
        {
            var bytes = FetchUpload(uri, remote) ?? FetchPlain(uri);
            if (bytes is not { Length: > 0 } || SkiaSharp.SKBitmap.Decode(bytes) is not { } decoded) return MdPicture.Broken;
            int longest = Math.Max(decoded.Width, decoded.Height);
            if (longest <= MdPictureMaxPixels) return new MdPicture(new SkiaImageSource(decoded), false);
            using (decoded)
            {
                float scale = (float)MdPictureMaxPixels / longest;
                var info = new SkiaSharp.SKImageInfo(Math.Max(1, (int)(decoded.Width * scale)), Math.Max(1, (int)(decoded.Height * scale)));
                var smaller = decoded.Resize(info, new SkiaSharp.SKSamplingOptions(SkiaSharp.SKCubicResampler.Mitchell));
                return smaller is null ? MdPicture.Broken : new MdPicture(new SkiaImageSource(smaller), false);
            }
        }
        catch (Exception)
        {
            return MdPicture.Broken;   // unreachable, refused, or not a picture after all
        }
    }

    /// <summary>A file pasted into this project, through the API with the account's token —
    /// the only way into a private project's uploads without a browser session. Null for any
    /// other URL, or when the instance predates the endpoint (17.4).</summary>
    private byte[]? FetchUpload(Uri uri, GitLabRemote? remote)
    {
        if (remote is null || UploadOf(uri, remote) is not { } upload || _gitlab.ClientFor(remote.Host) is not { } client)
            return null;
        var project = upload.ProjectId ?? ResolveProjectId(client, remote);
        if (project is null) return null;
        var bytes = Sync(client.GetUploadAsync(project.Value, upload.Secret, upload.File));
        return bytes.Ok ? bytes.Value : null;
    }

    /// <summary>
    /// A plain GET, carrying no token: an image hosted elsewhere, or an upload the API could
    /// not serve (a public project's still loads from its web URL). Through the handler the
    /// accounts use for the host, so it travels — and is tested — the same way.
    /// </summary>
    private byte[]? FetchPlain(Uri uri)
    {
        var http = _mdPlainClients.GetOrAdd(uri.Host, host =>
        {
            var handler = _gitlab.HandlerFor(host);
            var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Junction/1.0 (+https://github.com/fredrikhu/hultui)");
            return client;
        });
        using var response = Sync(http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead));
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > GitLabClient.MaxUploadBytes) return null;
        return Sync(GitLabClient.ReadCappedAsync(response.Content, GitLabClient.MaxUploadBytes, CancellationToken.None));
    }

    /// <summary>The upload a URL names in <paramref name="remote"/>'s project — the
    /// <c>/group/project/uploads/&lt;secret&gt;/&lt;file&gt;</c> markdown links to, or the
    /// <c>/-/project/&lt;id&gt;/uploads/…</c> newer instances write — or null.</summary>
    internal static (long? ProjectId, string Secret, string File)? UploadOf(Uri uri, GitLabRemote remote)
    {
        var web = new Uri(remote.WebBaseUrl);
        if (Uri.Compare(uri, web, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
            return null;
        var path = uri.AbsolutePath;
        var byPath = "/" + remote.ProjectPath + "/uploads/";
        long? id = null;
        string rest;
        if (path.StartsWith(byPath, StringComparison.OrdinalIgnoreCase)) rest = path[byPath.Length..];
        else if (UploadById().Match(path) is { Success: true } m) { id = long.Parse(m.Groups["id"].Value); rest = m.Groups["rest"].Value; }
        else return null;
        int slash = rest.IndexOf('/');
        if (slash <= 0 || slash == rest.Length - 1) return null;
        var secret = rest[..slash];
        if (!secret.All(char.IsAsciiHexDigit)) return null;
        return (id, secret, rest[(slash + 1)..]);
    }

    [GeneratedRegex(@"^/-/project/(?<id>\d+)/uploads/(?<rest>.+)$")]
    private static partial Regex UploadById();

    private void DisposeMarkdown()
    {
        _mdPictures.Clear();
        foreach (var client in _mdPlainClients.Values) client.Dispose();
        _mdPlainClients.Clear();
    }
}

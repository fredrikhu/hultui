using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Text;

public sealed class TextPipeline
{
    private readonly IFontCollection _fonts;
    private readonly ITextShaper _shaper;
    // Fully laid-out results, keyed on width (used only when the text must wrap).
    private readonly LruCache<MeasurementKey, TextLayout> _cache = new(capacity: 2048);
    // Width-independent shaping, so the same string measured at different
    // available widths is shaped once. Covers the common single-line case fully.
    // Sized to hold the working set of a large list laid out in one frame (the
    // measure pass touches every row, then the arrange pass touches them again),
    // so shaping survives between the two passes instead of being evicted. Shaped
    // entries are small and shaping is expensive, so a generous bound is worth it.
    private readonly LruCache<ShapeKey, ShapedText> _shapeCache = new(capacity: 16384);

    // Word-level shape cache. Raw shaping results keyed by space-delimited
    // segment + font instance + size, so distinct strings built from a
    // recurring vocabulary shape each word once instead of missing the
    // whole-string cache every time. Font instances are cache-stable in
    // IFontCollection implementations, so reference keys hold.
    // Known fidelity tradeoff (the same one browser word caches accept):
    // kerning across a segment boundary is lost — a font that kerns
    // letter↔space pairs would measure differently. UI fonts essentially
    // never do, and WordShapeCacheTests verifies equivalence on this system.
    private readonly LruCache<WordShapeKey, ShapedRun> _wordShapeCache = new(capacity: 8192);

    private readonly record struct WordShapeKey(string Segment, IResolvedFont Font, float FontSize);

    // Digit fast path. All-digit segments (row ids, counters) are each unique,
    // so they miss every string-level cache forever and pay a full shaper call.
    // For fonts that provably shape digits context-free, compose them from a
    // per-(font,size) advance table instead — and skip the word cache entirely,
    // so unique ids stop churning its LRU.
    // Prove-then-enable: at first use the font+size shapes every digit and all
    // 100 digit pairs once; any multi-glyph result, nonzero Y, kerning
    // adjustment, or contextual substitution disqualifies it (entry = null) and
    // digits take the normal shaper path. Never enabled on assumption.
    private readonly Dictionary<DigitKey, DigitTable?> _digitTables = new();

    private readonly record struct DigitKey(IResolvedFont Font, float FontSize);

    private sealed class DigitTable
    {
        public readonly uint[] GlyphIds = new uint[10];
        public readonly float[] Advances = new float[10];
    }

    public IFontCollection Fonts => _fonts;
    public ITextShaper Shaper => _shaper;

    public TextPipeline(IFontCollection fonts, ITextShaper shaper)
    {
        _fonts = fonts;
        _shaper = shaper;
    }

    // ── Metric-affecting spans (stage 2 of docs/plans/12-rich-text-spans.md) ──
    //
    // A span carrying Weight/Slant/FontSize changes shaping, so the layout must see
    // it. Style segmentation happens BEFORE font itemization: the string is split at
    // metric-span boundaries, each segment itemized with its effective weight/slant
    // and shaped at its effective size. Line breaking stays one pass — UAX #14 break
    // opportunities are style-independent. Plain and color-only-span text keeps
    // today's paths, caches and keys untouched.

    /// <summary>True when any span would change shaping or measurement.</summary>
    public static bool AnyMetricAffecting(IReadOnlyList<Core.Rendering.TextSpan>? spans)
    {
        if (spans is null) return false;
        for (int i = 0; i < spans.Count; i++)
            if (spans[i].Weight is not null || spans[i].Slant is not null || spans[i].FontSize is not null)
                return true;
        return false;
    }

    // Layout-relevant digest of a span list: offsets and metric fields only. Colors
    // and decorations are paint-only and must not fragment the layout caches.
    private static int MetricDigest(IReadOnlyList<Core.Rendering.TextSpan> spans)
    {
        var hash = new HashCode();
        for (int i = 0; i < spans.Count; i++)
        {
            var s = spans[i];
            if (s.Weight is null && s.Slant is null && s.FontSize is null) continue;
            hash.Add(s.Start); hash.Add(s.Length);
            hash.Add(s.Weight); hash.Add(s.Slant); hash.Add(s.FontSize);
        }
        return hash.ToHashCode();
    }

    /// <summary>Span-aware layout. Falls back to the plain path unless some span is
    /// metric-affecting; spans are absolute offsets into <paramref name="text"/>.</summary>
    public TextLayout Layout(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow, IReadOnlyList<Core.Rendering.TextSpan>? spans)
    {
        if (!AnyMetricAffecting(spans))
            return Layout(text, style, maxWidth, maxLines, overflow);
        if (string.IsNullOrEmpty(text))
            return TextLayout.Empty;

        // Styled text skips the width-independent single-line fast path for now and
        // goes straight to the width-keyed cache; the digest keeps styled and plain
        // results apart without touching plain keys.
        var key = new MeasurementKey(text, style.FontFamily, style.FontSize, style.FontWeight,
            style.FontSlant, maxWidth, maxLines ?? -1, overflow, style.LineHeight,
            style.LetterSpacing, MetricDigest(spans!));
        if (_cache.TryGet(key, out var cached))
            return cached;

        var layout = ComputeWrappedStyled(text, style, spans!, maxWidth, maxLines);
        _cache.Put(key, layout);
        return layout;
    }

    public TextLayout Layout(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow = TextOverflow.Clip)
    {
        if (string.IsNullOrEmpty(text))
            return TextLayout.Empty;

        // Text that can contain right-to-left script — or an Rtl paragraph style —
        // takes the direction-aware path (docs/plans/14-bidi.md), cached under the
        // same width-keyed cache as wrapped text, with Direction in the key.
        //
        // The classifier deliberately stays *before* the caches, running on every
        // call rather than once per string. Consulting the shape cache first would
        // answer the same question for free on a hit (the BiDi path never populates
        // that cache, so a hit proves the string is RTL-free) — measured, it buys
        // 0.16 µs on a 4,700-character warm layout and costs every warm BiDi lookup
        // ~45%, for a more tangled Layout. Not worth it.
        if (NeedsBidi(text, style))
        {
            var bidiKey = new MeasurementKey(text, style.FontFamily, style.FontSize, style.FontWeight,
                style.FontSlant, maxWidth, maxLines ?? -1, overflow, style.LineHeight,
                style.LetterSpacing, 0, style.TextDirection);
            if (_cache.TryGet(bidiKey, out var cachedBidi))
                return cachedBidi;
            var bidiLayout = ComputeWrappedBidi(text, style, maxWidth, maxLines);
            _cache.Put(bidiKey, bidiLayout);
            return bidiLayout;
        }

        var shapeKey = new ShapeKey(text, style.FontFamily, style.FontSize, style.FontWeight,
            style.FontSlant, style.LineHeight, style.LetterSpacing);
        if (!_shapeCache.TryGet(shapeKey, out var shaped))
        {
            shaped = ComputeShaping(text, style);
            _shapeCache.Put(shapeKey, shaped);
        }

        // Single line that fits: the whole layout is width-independent, so the
        // shaped result is returned directly for any width that accommodates it.
        // Nothing is truncated, so the overflow mode is irrelevant here.
        if (!shaped.HasNewline && shaped.TotalWidth <= maxWidth)
            return shaped.SingleLine!;

        // Explicit newlines or overflow: line breaking depends on the width, so
        // fall back to the width-keyed cache. Overflow is part of the key because
        // Ellipsis rewrites the truncated line, producing a different layout.
        var key = new MeasurementKey(text, style.FontFamily, style.FontSize, style.FontWeight,
            style.FontSlant, maxWidth, maxLines ?? -1, overflow, style.LineHeight, style.LetterSpacing);
        if (_cache.TryGet(key, out var cached))
            return cached;

        TextLayout layout;
        if (maxLines == 1 && overflow == TextOverflow.Clip && !shaped.HasNewline)
        {
            // One clipped line is the longest prefix that fits, cut at a character.
            // The wrap path would break it at the last *word* boundary before maxWidth,
            // which for text with few break opportunities — a path, a URL, a minified
            // JSON line — ends the line far short of the box and reads as though the
            // text simply stopped. Clipping is what the caller asked for; breaking is
            // what Ellipsis does, minus the ellipsis.
            layout = ClipToWidth(text, style, maxWidth);
        }
        else
        {
            layout = ComputeWrapped(text, style, maxWidth, maxLines);
            if (overflow == TextOverflow.Ellipsis)
                layout = ApplyEllipsis(text, style, layout, maxWidth);
        }
        _cache.Put(key, layout);
        return layout;
    }

    // The longest prefix of <paramref name="text"/> that fits <paramref name="maxWidth"/>.
    // Only whole characters are kept: DrawText does not clip to its bounds, so emitting the
    // glyph that straddles the edge would let it spill out of the box.
    private TextLayout ClipToWidth(string text, ResolvedInheritedStyle style, float maxWidth)
    {
        // Not even one character fits. The line is still a line: it occupies its font's
        // height, and an empty layout here would tell MeasureContent that a non-empty
        // string needs no vertical space. Flex measures children against a zero basis as a
        // matter of course, so this runs during ordinary layout, and a zero height there
        // collapses whatever contains the text.
        if (maxWidth <= 0) return EmptyLineOfHeight(style);

        int best = FitPrefix(text, 0, text.Length, style, maxWidth);
        if (best == 0) return EmptyLineOfHeight(style);

        var line = ShapeLineText(text[..best], style, 0);
        return new TextLayout([line], new Size(line.Width, line.Height));
    }

    /// <summary>
    /// The largest <c>i</c> in <c>[from, to]</c> whose slice <c>text[from..i]</c> still fits
    /// <paramref name="maxWidth"/>. Returns <paramref name="from"/> when not even one
    /// character does; callers decide what that means.
    /// </summary>
    /// <remarks>
    /// Gallops out from a short slice before bisecting, so every string measured stays within
    /// twice the answer. Bisecting the whole range instead would start by shaping half of what
    /// may be a 25,000-character line to discover that ~200 of them fit.
    /// </remarks>
    private int FitPrefix(string text, int from, int to, ResolvedInheritedStyle style, float maxWidth)
    {
        int span = 1;
        while (from + span < to && MeasureString(text[from..(from + span)], style) <= maxWidth)
            span <<= 1;

        int lo = from + Math.Min(span >> 1, to - from);
        int hi = from + Math.Min(span, to - from);
        int best = lo;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (MeasureString(text[from..mid], style) <= maxWidth) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return SnapPrefix(text, best);   // never split a surrogate pair
    }

    /// <summary>
    /// Cuts <c>text[from..to]</c> into lines at character boundaries, appending all but the
    /// final remainder to <paramref name="lines"/> and returning where that remainder starts.
    ///
    /// <para>For a run with no break opportunity in it — a hash, a base64 blob, a path with
    /// no separators — this is the only thing that can make it fit. Without it the run is
    /// emitted as a single line wider than the box, and simply runs out of it.</para>
    /// </summary>
    private int SplitByCharacter(string text, int from, int to, ResolvedInheritedStyle style,
        float maxWidth, List<TextLine> lines, int? maxLines)
    {
        while (from < to && (!maxLines.HasValue || lines.Count < maxLines.Value))
        {
            int fit = FitPrefix(text, from, to, style, maxWidth);
            // A box too narrow for even one character would otherwise never advance. One
            // character per line is the sane floor, and what a browser does.
            if (fit <= from) fit = SnapPrefix(text, from + 1);
            if (fit >= to) break;             // what is left fits a line of its own
            lines.Add(ShapeLineText(text[from..fit], style, from));
            from = fit;
        }
        return from;
    }

    // A line with no glyphs but the full height of one, taken from the primary font.
    private TextLayout EmptyLineOfHeight(ResolvedInheritedStyle style)
    {
        var line = ShapeLineText("", style, 0);
        return new TextLayout([line], new Size(0, line.Height));
    }

    // Width-independent: itemize + shape the whole string, record its natural
    // width, and (when there is no explicit newline) prebuild the single-line
    // layout that any sufficiently wide measurement will reuse.
    private ShapedText ComputeShaping(string text, ResolvedInheritedStyle style)
    {
        var fontRuns = FontItemizer.Itemize(text, _fonts, style.FontFamily, style.FontWeight, style.FontSlant);
        if (fontRuns.Count == 0)
            return new ShapedText(0, HasNewline: false, TextLayout.Empty);

        var shapedRuns = new List<ShapedRun>(fontRuns.Count);
        float totalWidth = 0;
        foreach (var run in fontRuns)
        {
            var shaped = ShapeWithSpacing(run.Text, run.Font, style.FontSize, style.LetterSpacing);
            shapedRuns.Add(shaped);
            totalWidth += shaped.Width;
        }

        bool hasNewline = text.Contains('\n');
        var singleLine = hasNewline ? null : BuildSingleLineLayout(shapedRuns, totalWidth, text.Length, style.LineHeight);
        return new ShapedText(totalWidth, hasNewline, singleLine);
    }

    // Multi-line path: the text either contains explicit newlines or is wider
    // than the available width. BreakIntoLines re-shapes per line, so it does not
    // reuse the cached single-line shaping.
    private TextLayout ComputeWrapped(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines)
    {
        var fontRuns = FontItemizer.Itemize(text, _fonts, style.FontFamily, style.FontWeight, style.FontSlant);
        if (fontRuns.Count == 0)
            return TextLayout.Empty;

        var shapedRuns = new List<ShapedRun>(fontRuns.Count);
        foreach (var run in fontRuns)
            shapedRuns.Add(ShapeWithSpacing(run.Text, run.Font, style.FontSize, style.LetterSpacing));

        var breakPoints = LineBreaker.FindBreakOpportunities(text);
        return BreakIntoLines(text, shapedRuns, fontRuns, style, breakPoints, maxWidth, maxLines);
    }

    // ── Styled layout internals ──
    // A styled line is segments cut at metric-span boundaries, each itemized with its
    // effective weight/slant and shaped at its effective size. Offsets are absolute
    // into the source string throughout, because span ranges are.

    private ResolvedInheritedStyle EffectiveStyleAt(
        ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan> spans, int offset)
    {
        for (int i = 0; i < spans.Count; i++)
        {
            var s = spans[i];
            if (s.IsMetricAffecting && offset >= s.Start && offset < s.End)
                return style with
                {
                    FontWeight = s.Weight ?? style.FontWeight,
                    FontSlant = s.Slant ?? style.FontSlant,
                    FontSize = s.FontSize ?? style.FontSize,
                };
        }
        return style;
    }

    // The nearest metric-span edge after <paramref name="offset"/>, clamped to limit.
    private static int NextStyleBoundary(IReadOnlyList<Core.Rendering.TextSpan> spans, int offset, int limit)
    {
        int next = limit;
        for (int i = 0; i < spans.Count; i++)
        {
            var s = spans[i];
            if (!s.IsMetricAffecting) continue;
            if (s.Start > offset && s.Start < next) next = s.Start;
            if (s.End > offset && s.End < next) next = s.End;
        }
        return next;
    }

    private float MeasureStyled(string text, int from, int to,
        ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan> spans)
    {
        float width = 0;
        int i = from;
        while (i < to)
        {
            int b = NextStyleBoundary(spans, i, to);
            width += MeasureString(text[i..b], EffectiveStyleAt(style, spans, i));
            i = b;
        }
        return width;
    }

    private TextLine ShapeStyledLine(string text, int from, int to,
        ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan> spans)
    {
        int sourceLength = to - from;
        // Trailing whitespace is trimmed for width, as in ShapeLineText.
        while (to > from && char.IsWhiteSpace(text[to - 1])) to--;

        var runs = new List<ShapedRun>();
        float width = 0, maxAscent = 0, maxDescent = 0, maxLeading = 0;
        int i = from;
        while (i < to)
        {
            int b = NextStyleBoundary(spans, i, to);
            var eff = EffectiveStyleAt(style, spans, i);
            foreach (var fr in FontItemizer.Itemize(text[i..b], _fonts, eff.FontFamily, eff.FontWeight, eff.FontSlant))
            {
                var shaped = ShapeWithSpacing(fr.Text, fr.Font, eff.FontSize, eff.LetterSpacing);
                shaped.OffsetX = width;
                width += shaped.Width;
                runs.Add(shaped);
                // A larger-size run raises its whole line, the same max-across-runs
                // rule font fallback already applies.
                maxAscent = Math.Max(maxAscent, Math.Abs(fr.Font.Ascent) * eff.FontSize);
                maxDescent = Math.Max(maxDescent, fr.Font.Descent * eff.FontSize);
                maxLeading = Math.Max(maxLeading, fr.Font.Leading * eff.FontSize);
            }
            i = b;
        }

        if (runs.Count == 0)
        {
            var primary = _fonts.Resolve(style.FontFamily, style.FontWeight, style.FontSlant);
            maxAscent = Math.Abs(primary.Ascent) * style.FontSize;
            maxDescent = primary.Descent * style.FontSize;
            maxLeading = primary.Leading * style.FontSize;
        }

        (maxAscent, maxLeading) = ApplyLineHeight(maxAscent, maxDescent, maxLeading, style.LineHeight);
        return new TextLine(runs, width, maxAscent, maxDescent, maxLeading) { Start = from, Length = sourceLength };
    }

    // Styled twin of FitPrefix: gallop out, then bisect, measuring with spans.
    private int StyledFitPrefix(string text, int from, int to,
        ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan> spans, float maxWidth)
    {
        int span = 1;
        while (from + span < to && MeasureStyled(text, from, from + span, style, spans) <= maxWidth)
            span <<= 1;

        int lo = from + Math.Min(span >> 1, to - from);
        int hi = from + Math.Min(span, to - from);
        int best = lo;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (MeasureStyled(text, from, mid, style, spans) <= maxWidth) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return SnapPrefix(text, best);
    }

    // The styled twin of ComputeWrapped + BreakIntoLines: same greedy break logic,
    // measurement and line shaping made span-aware. Overflow.Ellipsis on styled text
    // clips instead of ellipsizing for now (the rewrite needs a styled
    // BuildEllipsizedLine; tracked in docs/plans/12-rich-text-spans.md).
    private TextLayout ComputeWrappedStyled(string text, ResolvedInheritedStyle style,
        IReadOnlyList<Core.Rendering.TextSpan> spans, float maxWidth, int? maxLines)
    {
        var breakPoints = LineBreaker.FindBreakOpportunities(text);
        var allBreaks = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') { allBreaks.Add(i); allBreaks.Add(i + 1); }
        foreach (var bp in breakPoints)
            if (!allBreaks.Contains(bp)) allBreaks.Add(bp);
        allBreaks.Add(text.Length);
        allBreaks.Sort();
        for (int i = allBreaks.Count - 1; i > 0; i--)
            if (allBreaks[i] == allBreaks[i - 1]) allBreaks.RemoveAt(i);

        var lines = new List<TextLine>();
        int textOffset = 0;
        float lineWidth = 0;

        for (int s = 1; s < allBreaks.Count; s++)
        {
            int segStart = allBreaks[s - 1];
            int segEnd = allBreaks[s];

            if (segEnd - segStart == 1 && text[segStart] == '\n')
            {
                lines.Add(ShapeStyledLine(text, textOffset, segStart, style, spans));
                textOffset = segEnd;
                lineWidth = 0;
                if (maxLines.HasValue && lines.Count >= maxLines.Value) break;
                continue;
            }

            float segWidth = MeasureStyled(text, segStart, segEnd, style, spans);

            if (lineWidth + segWidth > maxWidth && lineWidth > 0)
            {
                lines.Add(ShapeStyledLine(text, textOffset, segStart, style, spans));
                textOffset = segStart;
                lineWidth = 0;
                if (maxLines.HasValue && lines.Count >= maxLines.Value) break;
            }

            if (lineWidth == 0 && segWidth > maxWidth)
            {
                // Unbreakable overlong segment: cut by character, span-aware.
                while (textOffset < segEnd && (!maxLines.HasValue || lines.Count < maxLines.Value))
                {
                    int fit = StyledFitPrefix(text, textOffset, segEnd, style, spans, maxWidth);
                    if (fit <= textOffset) fit = SnapPrefix(text, textOffset + 1);
                    if (fit >= segEnd) break;
                    lines.Add(ShapeStyledLine(text, textOffset, fit, style, spans));
                    textOffset = fit;
                }
                if (maxLines.HasValue && lines.Count >= maxLines.Value) break;
                lineWidth = MeasureStyled(text, textOffset, segEnd, style, spans);
                continue;
            }

            lineWidth += segWidth;
        }

        if (textOffset < text.Length && (!maxLines.HasValue || lines.Count < maxLines.Value))
            lines.Add(ShapeStyledLine(text, textOffset, text.Length, style, spans));

        float totalHeight = 0, maxLineWidth = 0;
        foreach (var line in lines)
        {
            line.OffsetY = totalHeight;
            totalHeight += line.Height;
            maxLineWidth = Math.Max(maxLineWidth, line.Width);
        }
        return new TextLayout(lines, new Size(maxLineWidth, totalHeight));
    }

    // ── BiDi internals (docs/plans/14-bidi.md, stage 1: display-only) ──

    /// <summary>Cheap classifier: does this text need UAX #9 analysis at all?</summary>
    /// <remarks>
    /// Every LTR layout runs this, on every call, before the cache is even
    /// consulted — it is the one place BiDi taxes text that has no RTL in it. So
    /// it is the vectorized span search rather than a char-at-a-time loop: same
    /// U+0590 threshold browsers use, one SIMD pass.
    /// </remarks>
    private static bool NeedsBidi(string text, ResolvedInheritedStyle style)
    {
        // Auto needs no special case: with nothing at or above U+0590 in the text,
        // first-strong detection can only answer left-to-right, which is what the
        // plain path already does.
        if (style.TextDirection == TextDirection.Rtl) return true;
        // Cast to ushort rather than calling the char overload directly: both are
        // the same vectorized range search, but ContainsAnyExceptInRange<char>
        // boxes its two bounds on every call (96 B, measured — see
        // samples/HultUI.Samples.TextBench) and the ushort one allocates nothing.
        return System.Runtime.InteropServices.MemoryMarshal
            .Cast<char, ushort>(text.AsSpan())
            .ContainsAnyExceptInRange((ushort)0, (ushort)0x058F);
    }

    /// <summary>
    /// Contiguous same-embedding-level segments within <c>[from, to)</c> of an
    /// already-resolved level array.
    /// </summary>
    /// <remarks>
    /// The levels are resolved ONCE for the whole paragraph and sliced here.
    /// Re-running UAX #9 on a substring is not the same computation: resolution is
    /// paragraph-scoped, so a neutral character next to a cut would resolve
    /// against the cut instead of against the text that actually surrounds it —
    /// and it also meant one ICU round trip per break candidate per line.
    /// </remarks>
    private static List<(int Start, int End, byte Level)> SegmentsIn(byte[] levels, int from, int to)
    {
        var segments = new List<(int, int, byte)>();
        int i = from;
        while (i < to)
        {
            byte level = levels[i];
            int j = i + 1;
            while (j < to && levels[j] == level) j++;
            segments.Add((i, j, level));
            i = j;
        }
        return segments;
    }

    /// <summary>
    /// UAX #9 rule L2: from the highest embedding level down to the lowest odd one,
    /// reverse every contiguous run at that level or higher. Returns the visual
    /// sequence of logical segment indices. Implemented directly — it is a dozen
    /// lines — so the reorder does not depend on a second ICU API surface.
    /// </summary>
    private static int[] VisualOrder(IReadOnlyList<byte> levels)
    {
        int n = levels.Count;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;

        byte max = 0, minOdd = byte.MaxValue;
        foreach (var l in levels)
        {
            if (l > max) max = l;
            if ((l & 1) == 1 && l < minOdd) minOdd = l;
        }
        if (minOdd == byte.MaxValue) return order;   // all even: already visual

        for (int level = max; level >= minOdd; level--)
        {
            int i = 0;
            while (i < n)
            {
                if (levels[order[i]] >= level)
                {
                    int j = i;
                    while (j < n && levels[order[j]] >= level) j++;
                    Array.Reverse(order, i, j - i);
                    i = j;
                }
                else i++;
            }
        }
        return order;
    }

    private float MeasureBidi(string text, int from, int to, ResolvedInheritedStyle style, byte[] levels)
    {
        if (to <= from) return 0;
        float width = 0;
        foreach (var (s, e, level) in SegmentsIn(levels, from, to))
            foreach (var fr in FontItemizer.Itemize(text[s..e], _fonts,
                style.FontFamily, style.FontWeight, style.FontSlant))
                width += ShapeWithSpacing(fr.Text, fr.Font, style.FontSize, style.LetterSpacing,
                    rtl: (level & 1) == 1).Width;
        return width;
    }

    /// <summary>
    /// One line, direction-aware: segments shaped with their resolved direction and
    /// placed in visual order. <see cref="TextLine.Runs"/> stays in LOGICAL order —
    /// painting reads each run's own <see cref="ShapedRun.OffsetX"/>, and the
    /// char→x mapping (spans, selection) walks runs logically — only the offsets
    /// carry the reordering.
    /// </summary>
    private TextLine ShapeBidiLine(string text, int from, int to, ResolvedInheritedStyle style, byte[] paraLevels)
    {
        int sourceLength = to - from;
        while (to > from && char.IsWhiteSpace(text[to - 1])) to--;

        var segments = SegmentsIn(paraLevels, from, to);
        var segRuns = new List<ShapedRun>[segments.Count];
        var levels = new byte[segments.Count];
        float maxAscent = 0, maxDescent = 0, maxLeading = 0;

        for (int s = 0; s < segments.Count; s++)
        {
            var (segStart, segEnd, level) = segments[s];
            levels[s] = level;
            var runs = new List<ShapedRun>();
            foreach (var fr in FontItemizer.Itemize(text[segStart..segEnd], _fonts,
                style.FontFamily, style.FontWeight, style.FontSlant))
            {
                var shaped = ShapeWithSpacing(fr.Text, fr.Font, style.FontSize, style.LetterSpacing,
                    rtl: (level & 1) == 1);
                shaped.Level = level;
                runs.Add(shaped);
                maxAscent = Math.Max(maxAscent, Math.Abs(fr.Font.Ascent) * style.FontSize);
                maxDescent = Math.Max(maxDescent, fr.Font.Descent * style.FontSize);
                maxLeading = Math.Max(maxLeading, fr.Font.Leading * style.FontSize);
            }
            segRuns[s] = runs;
        }

        // Place in visual order; within an RTL segment the fallback sub-runs flip too.
        float x = 0;
        foreach (int logical in VisualOrder(levels))
        {
            var runs = segRuns[logical];
            bool rtl = (levels[logical] & 1) == 1;
            for (int r = 0; r < runs.Count; r++)
            {
                var run = runs[rtl ? runs.Count - 1 - r : r];
                run.OffsetX = x;
                x += run.Width;
            }
        }

        var logicalRuns = new List<ShapedRun>();
        foreach (var runs in segRuns) logicalRuns.AddRange(runs);

        if (logicalRuns.Count == 0)
        {
            var primary = _fonts.Resolve(style.FontFamily, style.FontWeight, style.FontSlant);
            maxAscent = Math.Abs(primary.Ascent) * style.FontSize;
            maxDescent = primary.Descent * style.FontSize;
            maxLeading = primary.Leading * style.FontSize;
        }

        (maxAscent, maxLeading) = ApplyLineHeight(maxAscent, maxDescent, maxLeading, style.LineHeight);
        return new TextLine(logicalRuns, x, maxAscent, maxDescent, maxLeading) { Start = from, Length = sourceLength };
    }

    // The direction-aware twin of ComputeWrapped: same greedy break logic over
    // logical order, measurement and line shaping direction-resolved. Ellipsis on
    // bidi text clips for now (as the styled path does; both tracked in the plan).
    private TextLayout ComputeWrappedBidi(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines)
    {
        // One UAX #9 resolution for the whole paragraph; every line, break
        // candidate and prefix measurement below slices into it.
        var (levels, rtlParagraph) = BidiAnalyzer.Levels(text, style.TextDirection);
        var breakPoints = LineBreaker.FindBreakOpportunities(text);
        var allBreaks = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') { allBreaks.Add(i); allBreaks.Add(i + 1); }
        foreach (var bp in breakPoints)
            if (!allBreaks.Contains(bp)) allBreaks.Add(bp);
        allBreaks.Add(text.Length);
        allBreaks.Sort();
        for (int i = allBreaks.Count - 1; i > 0; i--)
            if (allBreaks[i] == allBreaks[i - 1]) allBreaks.RemoveAt(i);

        var lines = new List<TextLine>();
        int textOffset = 0;
        float lineWidth = 0;

        for (int s = 1; s < allBreaks.Count; s++)
        {
            int segStart = allBreaks[s - 1];
            int segEnd = allBreaks[s];

            if (segEnd - segStart == 1 && text[segStart] == '\n')
            {
                lines.Add(ShapeBidiLine(text, textOffset, segStart, style, levels));
                textOffset = segEnd;
                lineWidth = 0;
                if (maxLines.HasValue && lines.Count >= maxLines.Value) break;
                continue;
            }

            float segWidth = MeasureBidi(text, segStart, segEnd, style, levels);

            if (lineWidth + segWidth > maxWidth && lineWidth > 0)
            {
                lines.Add(ShapeBidiLine(text, textOffset, segStart, style, levels));
                textOffset = segStart;
                lineWidth = 0;
                if (maxLines.HasValue && lines.Count >= maxLines.Value) break;
            }

            if (lineWidth == 0 && segWidth > maxWidth)
            {
                while (textOffset < segEnd && (!maxLines.HasValue || lines.Count < maxLines.Value))
                {
                    int fit = BidiFitPrefix(text, textOffset, segEnd, style, levels, maxWidth);
                    if (fit <= textOffset) fit = SnapPrefix(text, textOffset + 1);
                    if (fit >= segEnd) break;
                    lines.Add(ShapeBidiLine(text, textOffset, fit, style, levels));
                    textOffset = fit;
                }
                if (maxLines.HasValue && lines.Count >= maxLines.Value) break;
                lineWidth = MeasureBidi(text, textOffset, segEnd, style, levels);
                continue;
            }

            lineWidth += segWidth;
        }

        if (textOffset < text.Length && (!maxLines.HasValue || lines.Count < maxLines.Value))
            lines.Add(ShapeBidiLine(text, textOffset, text.Length, style, levels));

        float totalHeight = 0, maxLineWidth = 0;
        foreach (var line in lines)
        {
            line.OffsetY = totalHeight;
            totalHeight += line.Height;
            maxLineWidth = Math.Max(maxLineWidth, line.Width);
        }
        return new TextLayout(lines, new Size(maxLineWidth, totalHeight), rtlParagraph);
    }

    private int BidiFitPrefix(string text, int from, int to,
        ResolvedInheritedStyle style, byte[] levels, float maxWidth)
    {
        int span = 1;
        while (from + span < to && MeasureBidi(text, from, from + span, style, levels) <= maxWidth)
            span <<= 1;

        int lo = from + Math.Min(span >> 1, to - from);
        int hi = from + Math.Min(span, to - from);
        int best = lo;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (MeasureBidi(text, from, mid, style, levels) <= maxWidth) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return SnapPrefix(text, best);
    }

    private const string EllipsisString = "…"; // …

    // Rewrites the last line of an already-broken (clipped) layout with a
    // trailing ellipsis when content was truncated. Truncation happens either
    // because a maxLines limit dropped trailing text, or because a single
    // unbreakable token is wider than the available width. Only the final line
    // is touched; earlier lines keep their exact shaping and positions.
    private TextLayout ApplyEllipsis(string text, ResolvedInheritedStyle style, TextLayout clip, float maxWidth)
    {
        if (clip.Lines.Count == 0)
            return clip;

        var last = clip.Lines[^1];
        int coveredEnd = last.Start + last.Length;
        bool droppedTail = coveredEnd < text.Length;
        // A greedy line never exceeds maxWidth unless it is a lone token that
        // could not be broken — that still needs to be clipped with an ellipsis.
        bool lineOverflows = last.Width > maxWidth + 0.5f;
        if (!droppedTail && !lineOverflows)
            return clip;

        // The truncated line's source is everything from where the last line
        // begins up to the next explicit newline (or the end of the string).
        int nl = text.IndexOf('\n', last.Start);
        int tailEnd = nl < 0 ? text.Length : nl;
        string tail = text[last.Start..tailEnd];

        var ellipsized = BuildEllipsizedLine(tail, last.Start, style, maxWidth);
        ellipsized.OffsetY = last.OffsetY;

        var lines = new List<TextLine>(clip.Lines.Count);
        for (int i = 0; i < clip.Lines.Count - 1; i++)
            lines.Add(clip.Lines[i]);
        lines.Add(ellipsized);

        float maxLineWidth = 0;
        foreach (var line in lines)
            maxLineWidth = Math.Max(maxLineWidth, line.Width);

        // The replacement line uses the same font metrics, so the overall height
        // is unchanged; only the max width can shrink.
        return new TextLayout(lines, new Size(maxLineWidth, clip.Size.Height));
    }

    // Builds a single line holding the longest prefix of <paramref name="tail"/>
    // that leaves room for the ellipsis within <paramref name="maxWidth"/>,
    // followed by the ellipsis glyph.
    private TextLine BuildEllipsizedLine(string tail, int start, ResolvedInheritedStyle style, float maxWidth)
    {
        float ellipsisWidth = MeasureString(EllipsisString, style);

        // Binary search for the widest prefix that fits alongside the ellipsis.
        // Width is monotonic in prefix length, so this converges in O(log n)
        // shaping calls even for a pathologically long unbreakable token.
        int lo = 0, hi = tail.Length, best = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            float width = MeasureString(tail[..mid], style);
            if (width + ellipsisWidth <= maxWidth)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        // Never split a surrogate pair. Snapping down only narrows the prefix, so
        // it still fits.
        best = SnapPrefix(tail, best);

        string content = tail[..best] + EllipsisString;
        return ShapeLineText(content, style, start);
    }

    // Total advance width of a string, itemized and shaped the same way as a line.
    private float MeasureString(string s, ResolvedInheritedStyle style)
    {
        if (s.Length == 0)
            return 0;

        var runs = FontItemizer.Itemize(s, _fonts, style.FontFamily, style.FontWeight, style.FontSlant);
        float width = 0;
        foreach (var run in runs)
            width += ShapeWithSpacing(run.Text, run.Font, style.FontSize, style.LetterSpacing).Width;
        return width;
    }

    // Shapes a run, then applies letter spacing by widening each glyph's advance
    // and shifting the following glyphs. Returns the shaper's result unchanged
    // when spacing is zero (the common case), so there is no allocation cost.
    private ShapedRun ShapeWithSpacing(string text, IResolvedFont font, float fontSize, float letterSpacing,
        bool rtl = false)
    {
        // RTL runs bypass the word cache and digit fast path in stage 1: correctness
        // first, and the caches' space-split assumptions are unproven for RTL
        // (Arabic joining does not cross spaces, but "likely sound" is not proven —
        // docs/plans/14-bidi.md). LTR text keeps every cache untouched.
        var run = rtl
            ? _shaper.Shape(text, font, fontSize, rtl: true)
            : ShapeByWords(text, font, fontSize);
        if (letterSpacing == 0f || run.Glyphs.Length == 0)
            return run;

        var glyphs = run.Glyphs;
        var spaced = new GlyphPosition[glyphs.Length];
        float shift = 0;
        for (int i = 0; i < glyphs.Length; i++)
        {
            var g = glyphs[i];
            spaced[i] = g with { X = g.X + shift, Advance = g.Advance + letterSpacing };
            shift += letterSpacing;
        }

        return new ShapedRun(run.Text, run.Font, run.FontSize,
            run.Width + letterSpacing * glyphs.Length, spaced);
    }

    // Shapes a font run by splitting it into maximal space / non-space
    // segments, shaping each through the word cache, and concatenating. Single
    // segments (no space) go straight through the cache.
    private ShapedRun ShapeByWords(string text, IResolvedFont font, float fontSize)
    {
        if (text.IndexOf(' ') < 0)
        {
            var run = GetOrShapeSegment(text, font, fontSize);
            // Fresh ShapedRun per call site: OffsetX is mutated by layout, so the
            // cached instance must never be handed out. The glyph array is
            // read-only in practice and safe to share.
            return new ShapedRun(text, font, fontSize, run.Width, run.Glyphs);
        }

        var glyphs = new List<GlyphPosition>(text.Length);
        float x = 0;
        int i = 0;
        while (i < text.Length)
        {
            bool isSpace = text[i] == ' ';
            int start = i;
            while (i < text.Length && (text[i] == ' ') == isSpace) i++;
            var seg = GetOrShapeSegment(text[start..i], font, fontSize);
            foreach (var g in seg.Glyphs)
                glyphs.Add(g with { X = g.X + x });
            x += seg.Width;
        }
        return new ShapedRun(text, font, fontSize, x, glyphs.ToArray());
    }

    private ShapedRun GetOrShapeSegment(string segment, IResolvedFont font, float fontSize)
    {
        if (TryShapeDigits(segment, font, fontSize) is { } digits)
            return digits;

        var key = new WordShapeKey(segment, font, fontSize);
        if (_wordShapeCache.TryGet(key, out var cached))
            return cached;

        var run = _shaper.Shape(segment, font, fontSize);
        _wordShapeCache.Put(key, run);
        return run;
    }

    // Composes an all-digit segment from the proven advance table, or returns
    // null when the segment isn't pure digits / the font+size failed the proof.
    private ShapedRun? TryShapeDigits(string segment, IResolvedFont font, float fontSize)
    {
        if (segment.Length == 0)
            return null;
        foreach (char c in segment)
            if (c is < '0' or > '9')
                return null;

        var key = new DigitKey(font, fontSize);
        if (!_digitTables.TryGetValue(key, out var table))
        {
            table = ProveDigitsContextFree(font, fontSize);
            _digitTables[key] = table;
        }
        if (table is null)
            return null;

        var glyphs = new GlyphPosition[segment.Length];
        float x = 0;
        for (int i = 0; i < segment.Length; i++)
        {
            int d = segment[i] - '0';
            glyphs[i] = new GlyphPosition(table.GlyphIds[d], x, 0, table.Advances[d]);
            x += table.Advances[d];
        }
        return new ShapedRun(segment, font, fontSize, x, glyphs);
    }

    // One-time proof per font+size (110 shaper calls): every digit must shape
    // to exactly one zero-offset glyph, and every digit pair must shape to the
    // two glyphs at exactly the summed nominal advances. Passing means digit
    // strings of any length compose identically to real shaping (pair-local
    // kerning is all a GPOS table can apply to a digit run).
    private DigitTable? ProveDigitsContextFree(IResolvedFont font, float fontSize)
    {
        const float Tol = 0.01f;
        var table = new DigitTable();

        for (int d = 0; d < 10; d++)
        {
            var run = _shaper.Shape(((char)('0' + d)).ToString(), font, fontSize);
            if (run.Glyphs.Length != 1)
                return null;
            var g = run.Glyphs[0];
            if (Math.Abs(g.X) > Tol || Math.Abs(g.Y) > Tol)
                return null;
            table.GlyphIds[d] = g.GlyphId;
            table.Advances[d] = run.Width;
        }

        for (int a = 0; a < 10; a++)
        {
            for (int b = 0; b < 10; b++)
            {
                var run = _shaper.Shape($"{(char)('0' + a)}{(char)('0' + b)}", font, fontSize);
                if (run.Glyphs.Length != 2
                    || run.Glyphs[0].GlyphId != table.GlyphIds[a]
                    || run.Glyphs[1].GlyphId != table.GlyphIds[b]
                    || Math.Abs(run.Glyphs[0].X) > Tol
                    || Math.Abs(run.Glyphs[0].Y) > Tol
                    || Math.Abs(run.Glyphs[1].Y) > Tol
                    || Math.Abs(run.Glyphs[1].X - table.Advances[a]) > Tol
                    || Math.Abs(run.Width - (table.Advances[a] + table.Advances[b])) > Tol)
                {
                    return null;
                }
            }
        }

        return table;
    }

    // Adjusts a line's ascent/leading so its total height equals an explicit
    // line-height, centering the text within the extra space (half above, half
    // below). A line-height smaller than the natural text height is ignored.
    private static (float Ascent, float Leading) ApplyLineHeight(
        float ascent, float descent, float naturalLeading, float lineHeight)
    {
        if (lineHeight <= 0f)
            return (ascent, naturalLeading);

        float extra = lineHeight - (ascent + descent);
        if (extra <= 0f)
            return (ascent, 0f);

        float half = extra / 2f;
        return (ascent + half, half);
    }

    // Moves an index back off the trailing half of a surrogate pair.
    private static int SnapPrefix(string text, int index) =>
        index > 0 && index < text.Length && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1])
            ? index - 1
            : index;

    private static TextLayout BuildSingleLineLayout(List<ShapedRun> runs, float totalWidth, int textLength, float lineHeight)
    {
        float maxAscent = 0, maxDescent = 0, maxLeading = 0;
        float x = 0;
        foreach (var run in runs)
        {
            run.OffsetX = x;
            x += run.Width;
            // Font metrics are at reference size 1 — scale by actual fontSize
            maxAscent = Math.Max(maxAscent, Math.Abs(run.Font.Ascent) * run.FontSize);
            maxDescent = Math.Max(maxDescent, run.Font.Descent * run.FontSize);
            maxLeading = Math.Max(maxLeading, run.Font.Leading * run.FontSize);
        }

        (maxAscent, maxLeading) = ApplyLineHeight(maxAscent, maxDescent, maxLeading, lineHeight);
        var line = new TextLine(runs, totalWidth, maxAscent, maxDescent, maxLeading) { Start = 0, Length = textLength };
        return new TextLayout([line], new Size(totalWidth, line.Height));
    }

    private TextLayout BreakIntoLines(string text, List<ShapedRun> shapedRuns, List<FontRun> fontRuns,
        ResolvedInheritedStyle style, List<int> breakPoints, float maxWidth, int? maxLines)
    {
        // Build a character→width map from shaped runs
        // For simplicity, we re-shape segments when breaking lines
        var lines = new List<TextLine>();
        int textOffset = 0;

        // Add text start and ensure break at newlines
        var allBreaks = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                allBreaks.Add(i);
                allBreaks.Add(i + 1);
            }
        }
        foreach (var bp in breakPoints)
        {
            if (!allBreaks.Contains(bp))
                allBreaks.Add(bp);
        }
        allBreaks.Add(text.Length);
        allBreaks.Sort();
        // Remove duplicates
        for (int i = allBreaks.Count - 1; i > 0; i--)
            if (allBreaks[i] == allBreaks[i - 1])
                allBreaks.RemoveAt(i);

        // Greedy line breaking
        float lineWidth = 0;

        for (int s = 1; s < allBreaks.Count; s++)
        {
            int segEnd = allBreaks[s];
            string segment = text[allBreaks[s - 1]..segEnd];

            // Check for explicit newline
            bool isNewline = segment.Length == 1 && segment[0] == '\n';
            if (isNewline)
            {
                // Emit current line
                string lineText = text[textOffset..allBreaks[s - 1]];
                lines.Add(ShapeLineText(lineText, style, textOffset));
                textOffset = segEnd;
                lineWidth = 0;
                if (maxLines.HasValue && lines.Count >= maxLines.Value)
                    break;
                continue;
            }

            // Measure segment
            var segRuns = FontItemizer.Itemize(segment, _fonts, style.FontFamily, style.FontWeight, style.FontSlant);
            float segWidth = 0;
            foreach (var run in segRuns)
                segWidth += ShapeWithSpacing(run.Text, run.Font, style.FontSize, style.LetterSpacing).Width;

            if (lineWidth + segWidth > maxWidth && lineWidth > 0)
            {
                // Break here — emit line up to previous segment
                string lineText = text[textOffset..allBreaks[s - 1]];
                lines.Add(ShapeLineText(lineText, style, textOffset));
                textOffset = allBreaks[s - 1];
                lineWidth = 0;
                if (maxLines.HasValue && lines.Count >= maxLines.Value)
                    break;
            }

            // A segment that overflows a line all by itself offers nowhere to break: cut it
            // by character rather than let it run out of the box. Only reachable at the start
            // of a line — anywhere else the block above has just broken.
            if (lineWidth == 0 && segWidth > maxWidth)
            {
                textOffset = SplitByCharacter(text, allBreaks[s - 1], segEnd, style, maxWidth, lines, maxLines);
                if (maxLines.HasValue && lines.Count >= maxLines.Value)
                    break;
                lineWidth = MeasureString(text[textOffset..segEnd], style);
                continue;
            }

            lineWidth += segWidth;
        }

        // Emit final line. A trailing run with nowhere to break needs the same character-level
        // treatment as one in the middle: the loop above never sees the last segment overflow,
        // because there is no following segment to trigger a break.
        if (textOffset < text.Length && (!maxLines.HasValue || lines.Count < maxLines.Value))
        {
            textOffset = SplitByCharacter(text, textOffset, text.Length, style, maxWidth, lines, maxLines);
            if (textOffset < text.Length && (!maxLines.HasValue || lines.Count < maxLines.Value))
                lines.Add(ShapeLineText(text[textOffset..], style, textOffset));
        }

        // Position lines vertically
        float totalHeight = 0;
        float maxLineWidth = 0;
        foreach (var line in lines)
        {
            line.OffsetY = totalHeight;
            totalHeight += line.Height;
            maxLineWidth = Math.Max(maxLineWidth, line.Width);
        }

        return new TextLayout(lines, new Size(maxLineWidth, totalHeight));
    }

    // Cached width-independent shaping of a string. <see cref="SingleLine"/> is
    // the prebuilt layout used whenever the text fits one line; it is null only
    // when the text contains an explicit newline (which always needs breaking).
    private sealed record ShapedText(float TotalWidth, bool HasNewline, TextLayout? SingleLine);

    private TextLine ShapeLineText(string lineText, ResolvedInheritedStyle style, int start)
    {
        // Trim trailing whitespace for width calculation but keep for shaping
        var trimmed = lineText.TrimEnd();
        var fontRuns = FontItemizer.Itemize(trimmed, _fonts, style.FontFamily, style.FontWeight, style.FontSlant);

        var runs = new List<ShapedRun>();
        float width = 0;
        float maxAscent = 0, maxDescent = 0, maxLeading = 0;

        foreach (var fr in fontRuns)
        {
            var shaped = ShapeWithSpacing(fr.Text, fr.Font, style.FontSize, style.LetterSpacing);
            shaped.OffsetX = width;
            width += shaped.Width;
            runs.Add(shaped);
            // Font metrics are at reference size 1 — scale by actual fontSize
            maxAscent = Math.Max(maxAscent, Math.Abs(fr.Font.Ascent) * style.FontSize);
            maxDescent = Math.Max(maxDescent, fr.Font.Descent * style.FontSize);
            maxLeading = Math.Max(maxLeading, fr.Font.Leading * style.FontSize);
        }

        // If no runs (empty line), use primary font metrics scaled by fontSize
        if (runs.Count == 0)
        {
            var primaryFont = _fonts.Resolve(style.FontFamily, style.FontWeight, style.FontSlant);
            maxAscent = Math.Abs(primaryFont.Ascent) * style.FontSize;
            maxDescent = primaryFont.Descent * style.FontSize;
            maxLeading = primaryFont.Leading * style.FontSize;
        }

        (maxAscent, maxLeading) = ApplyLineHeight(maxAscent, maxDescent, maxLeading, style.LineHeight);
        return new TextLine(runs, width, maxAscent, maxDescent, maxLeading) { Start = start, Length = lineText.Length };
    }
}

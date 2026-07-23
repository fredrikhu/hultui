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

    public TextLayout Layout(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow = TextOverflow.Clip)
    {
        if (string.IsNullOrEmpty(text))
            return TextLayout.Empty;

        // Shape once, width-independently. The layout pass measures the same
        // string many times per frame at different available widths (Phase 1
        // measure vs Phase 6 arrange, plus flex-grow re-measures); without this,
        // each distinct width re-itemized and re-shaped the text.
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

        var layout = ComputeWrapped(text, style, maxWidth, maxLines);
        if (overflow == TextOverflow.Ellipsis)
            layout = ApplyEllipsis(text, style, layout, maxWidth);
        _cache.Put(key, layout);
        return layout;
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
    private ShapedRun ShapeWithSpacing(string text, IResolvedFont font, float fontSize, float letterSpacing)
    {
        var run = ShapeByWords(text, font, fontSize);
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

            lineWidth += segWidth;
        }

        // Emit final line
        if (textOffset < text.Length && (!maxLines.HasValue || lines.Count < maxLines.Value))
        {
            string lineText = text[textOffset..];
            if (lineText.Length > 0)
                lines.Add(ShapeLineText(lineText, style, textOffset));
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

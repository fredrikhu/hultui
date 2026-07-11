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

    public IFontCollection Fonts => _fonts;
    public ITextShaper Shaper => _shaper;

    public TextPipeline(IFontCollection fonts, ITextShaper shaper)
    {
        _fonts = fonts;
        _shaper = shaper;
    }

    public TextLayout Layout(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines)
    {
        if (string.IsNullOrEmpty(text))
            return TextLayout.Empty;

        // Shape once, width-independently. The layout pass measures the same
        // string many times per frame at different available widths (Phase 1
        // measure vs Phase 6 arrange, plus flex-grow re-measures); without this,
        // each distinct width re-itemized and re-shaped the text.
        var shapeKey = new ShapeKey(text, style.FontFamily, style.FontSize, style.FontWeight);
        if (!_shapeCache.TryGet(shapeKey, out var shaped))
        {
            shaped = ComputeShaping(text, style);
            _shapeCache.Put(shapeKey, shaped);
        }

        // Single line that fits: the whole layout is width-independent, so the
        // shaped result is returned directly for any width that accommodates it.
        if (!shaped.HasNewline && shaped.TotalWidth <= maxWidth)
            return shaped.SingleLine!;

        // Explicit newlines or overflow: line breaking depends on the width, so
        // fall back to the width-keyed cache.
        var key = new MeasurementKey(text, style.FontFamily, style.FontSize, style.FontWeight, maxWidth, maxLines ?? -1);
        if (_cache.TryGet(key, out var cached))
            return cached;

        var layout = ComputeWrapped(text, style, maxWidth, maxLines);
        _cache.Put(key, layout);
        return layout;
    }

    // Width-independent: itemize + shape the whole string, record its natural
    // width, and (when there is no explicit newline) prebuild the single-line
    // layout that any sufficiently wide measurement will reuse.
    private ShapedText ComputeShaping(string text, ResolvedInheritedStyle style)
    {
        var fontRuns = FontItemizer.Itemize(text, _fonts, style.FontFamily, style.FontWeight, FontSlant.Normal);
        if (fontRuns.Count == 0)
            return new ShapedText(0, HasNewline: false, TextLayout.Empty);

        var shapedRuns = new List<ShapedRun>(fontRuns.Count);
        float totalWidth = 0;
        foreach (var run in fontRuns)
        {
            var shaped = _shaper.Shape(run.Text, run.Font, style.FontSize);
            shapedRuns.Add(shaped);
            totalWidth += shaped.Width;
        }

        bool hasNewline = text.Contains('\n');
        var singleLine = hasNewline ? null : BuildSingleLineLayout(shapedRuns, totalWidth, text.Length);
        return new ShapedText(totalWidth, hasNewline, singleLine);
    }

    // Multi-line path: the text either contains explicit newlines or is wider
    // than the available width. BreakIntoLines re-shapes per line, so it does not
    // reuse the cached single-line shaping.
    private TextLayout ComputeWrapped(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines)
    {
        var fontRuns = FontItemizer.Itemize(text, _fonts, style.FontFamily, style.FontWeight, FontSlant.Normal);
        if (fontRuns.Count == 0)
            return TextLayout.Empty;

        var shapedRuns = new List<ShapedRun>(fontRuns.Count);
        foreach (var run in fontRuns)
            shapedRuns.Add(_shaper.Shape(run.Text, run.Font, style.FontSize));

        var breakPoints = LineBreaker.FindBreakOpportunities(text);
        return BreakIntoLines(text, shapedRuns, fontRuns, style, breakPoints, maxWidth, maxLines);
    }

    private static TextLayout BuildSingleLineLayout(List<ShapedRun> runs, float totalWidth, int textLength)
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
            var segRuns = FontItemizer.Itemize(segment, _fonts, style.FontFamily, style.FontWeight, FontSlant.Normal);
            float segWidth = 0;
            foreach (var run in segRuns)
                segWidth += _shaper.Shape(run.Text, run.Font, style.FontSize).Width;

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
        var fontRuns = FontItemizer.Itemize(trimmed, _fonts, style.FontFamily, style.FontWeight, FontSlant.Normal);

        var runs = new List<ShapedRun>();
        float width = 0;
        float maxAscent = 0, maxDescent = 0, maxLeading = 0;

        foreach (var fr in fontRuns)
        {
            var shaped = _shaper.Shape(fr.Text, fr.Font, style.FontSize);
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
            var primaryFont = _fonts.Resolve(style.FontFamily, style.FontWeight, FontSlant.Normal);
            maxAscent = Math.Abs(primaryFont.Ascent) * style.FontSize;
            maxDescent = primaryFont.Descent * style.FontSize;
            maxLeading = primaryFont.Leading * style.FontSize;
        }

        return new TextLine(runs, width, maxAscent, maxDescent, maxLeading) { Start = start, Length = lineText.Length };
    }
}

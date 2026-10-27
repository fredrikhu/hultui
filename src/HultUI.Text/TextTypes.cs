using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Text;

/// <summary>
/// Result of text layout: a list of positioned lines.
/// </summary>
public sealed class TextLayout
{
    public static readonly TextLayout Empty = new([], Size.Zero);

    public IReadOnlyList<TextLine> Lines { get; }
    public Size Size { get; }

    /// <summary>
    /// The paragraph direction this text actually resolved to. Only interesting
    /// for <see cref="TextDirection.Auto"/>, where it is the answer to UAX #9
    /// rules P2/P3 and cannot be known before laying the text out — but it is what
    /// <see cref="TextAlign.Start"/> and <see cref="TextAlign.End"/> align to for
    /// any direction.
    /// </summary>
    public bool IsRtlParagraph { get; }

    public TextLayout(IReadOnlyList<TextLine> lines, Size size, bool isRtlParagraph = false)
    {
        Lines = lines;
        Size = size;
        IsRtlParagraph = isRtlParagraph;
    }
}

/// <summary>
/// A single line of text, containing one or more shaped runs.
/// </summary>
public sealed class TextLine
{
    public IReadOnlyList<ShapedRun> Runs { get; }
    public float Width { get; }
    public float Ascent { get; }
    public float Descent { get; }
    public float Leading { get; }
    public float Height => Ascent + Descent + Leading;
    public float OffsetY { get; internal set; }

    /// <summary>Index of this line's first character in the original string.</summary>
    public int Start { get; internal set; }

    /// <summary>
    /// Number of characters of the original string this line spans, including any
    /// trailing whitespace consumed at the wrap point (but not an explicit
    /// newline, which sits between lines). Used to map selection offsets and
    /// pointer positions back to the source text.
    /// </summary>
    public int Length { get; internal set; }

    /// <summary>
    /// Characters actually covered by <see cref="Runs"/>. This is <see cref="Length"/>
    /// minus any trailing whitespace trimmed at shaping time, and is the domain of
    /// every offset↔x mapping below.
    /// </summary>
    public int DrawnLength { get; }

    public TextLine(IReadOnlyList<ShapedRun> runs, float width, float ascent, float descent, float leading)
    {
        Runs = runs;
        Width = width;
        Ascent = ascent;
        Descent = descent;
        Leading = leading;
        int drawn = 0;
        for (int i = 0; i < runs.Count; i++) drawn += runs[i].Text.Length;
        DrawnLength = drawn;
    }

    // ── Offset ↔ x mapping (docs/plans/14-bidi.md, stage 2) ──
    //
    // Runs are stored in LOGICAL order and each carries its own visual OffsetX, so
    // a logical offset maps to a visual x by finding its run and asking where
    // inside it the boundary sits — forwards for an LTR run, backwards for an RTL
    // one. In text with no RTL in it every run has level 0 and all of this reduces
    // to "add up the widths from the left", exactly as before.
    //
    // measurePrefix is the escape hatch for runs where shaping merged clusters
    // (ligatures, marks, emoji) so glyphs no longer map 1:1 to characters: it
    // measures a prefix of the run's text the same way the run itself was
    // measured. Callers that cannot measure pass null and get a proportional
    // split of the run's width, which never moves a glyph but may sit a hair off
    // inside a cluster.
    //
    // Each method takes it in two forms. A TextPrefixMeasurer is handed the run's text and the
    // prefix's length, and can measure the prefix where it stands; a Func<string, float> is
    // handed the prefix as a string of its own, cut out for every boundary asked about.

    /// <summary>
    /// Visual x (line-relative) of the caret at line-relative logical
    /// <paramref name="offset"/>. At a boundary between two direction runs the
    /// caret is placed on the leading edge of the run that *contains* the offset.
    /// </summary>
    public float CaretXAt(int offset, Func<string, float>? measurePrefix = null) =>
        CaretXAt(offset, Cutting(measurePrefix));

    /// <inheritdoc cref="CaretXAt(int, Func{string, float})"/>
    public float CaretXAt(int offset, TextPrefixMeasurer? measurePrefix)
    {
        if (Runs.Count == 0) return 0f;

        int start = 0;
        for (int i = 0; i < Runs.Count; i++)
        {
            var run = Runs[i];
            int len = run.Text.Length;
            if (offset <= start) return RunX(run, 0, measurePrefix);
            if (offset < start + len) return RunX(run, offset - start, measurePrefix);
            start += len;
        }
        var last = Runs[^1];
        return RunX(last, last.Text.Length, measurePrefix);
    }

    /// <summary>
    /// The line-relative logical offset whose caret sits nearest visual x
    /// <paramref name="x"/> — the hit-test half of <see cref="CaretXAt(int, Func{string, float})"/>.
    /// </summary>
    public int OffsetAtX(float x, Func<string, float>? measurePrefix = null) =>
        OffsetAtX(x, Cutting(measurePrefix));

    /// <inheritdoc cref="OffsetAtX(float, Func{string, float})"/>
    public int OffsetAtX(float x, TextPrefixMeasurer? measurePrefix)
    {
        if (Runs.Count == 0) return 0;

        int start = 0, bestOffset = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < Runs.Count; i++)
        {
            var run = Runs[i];
            int len = run.Text.Length;
            bool inside = x >= run.OffsetX && (x <= run.OffsetX + run.Width || i == Runs.Count - 1);
            if (inside)
            {
                var (rel, _) = NearestInRun(run, x, measurePrefix);
                return start + rel;
            }
            // x is outside every run only when it is off the ends of the line (or
            // in the gap a zero-width run would leave); keep the closest boundary.
            var (fallbackRel, distance) = NearestInRun(run, x, measurePrefix);
            if (distance < bestDistance) { bestDistance = distance; bestOffset = start + fallbackRel; }
            start += len;
        }
        return bestOffset;
    }

    /// <summary>
    /// Visual x-ranges covering the logical range <c>[from, to)</c>. A range that
    /// crosses a direction boundary is visually discontiguous, so this returns one
    /// entry per contiguous piece (always exactly one for text with no RTL in it).
    /// </summary>
    public IReadOnlyList<VisualRange> VisualRanges(int from, int to, Func<string, float>? measurePrefix = null) =>
        VisualRanges(from, to, Cutting(measurePrefix));

    /// <inheritdoc cref="VisualRanges(int, int, Func{string, float})"/>
    public IReadOnlyList<VisualRange> VisualRanges(int from, int to, TextPrefixMeasurer? measurePrefix)
    {
        var ranges = new List<VisualRange>();
        VisualRanges(from, to, ranges, measurePrefix);
        return ranges;
    }

    /// <summary>
    /// <see cref="VisualRanges(int, int, Func{string, float})"/> into a caller-owned
    /// buffer, which it clears first. Paint paths call this per span per line, so
    /// they reuse one list instead of allocating one each time.
    /// </summary>
    public void VisualRanges(int from, int to, List<VisualRange> into, Func<string, float>? measurePrefix = null) =>
        VisualRanges(from, to, into, Cutting(measurePrefix));

    /// <inheritdoc cref="VisualRanges(int, int, List{VisualRange}, Func{string, float})"/>
    public void VisualRanges(int from, int to, List<VisualRange> into, TextPrefixMeasurer? measurePrefix)
    {
        into.Clear();
        if (Runs.Count == 0 || to <= from) return;

        // Indexed, as everywhere in this class: Runs is an interface, and a foreach over it
        // would box an enumerator on every call — per span per line, on the paint path.
        var ranges = into;
        int start = 0;
        for (int i = 0; i < Runs.Count; i++)
        {
            var run = Runs[i];
            int len = run.Text.Length;
            int s = Math.Max(from, start), e = Math.Min(to, start + len);
            if (e > s)
            {
                float a = RunX(run, s - start, measurePrefix);
                float b = RunX(run, e - start, measurePrefix);
                ranges.Add(new VisualRange(Math.Min(a, b), Math.Max(a, b)));
            }
            start += len;
        }
        if (ranges.Count < 2) return;

        // Merge in place: runs that meet visually (font fallback inside one
        // direction run always does) are one highlight, not two abutting ones.
        ranges.Sort(static (l, r) => l.X0.CompareTo(r.X0));
        int write = 0;
        for (int i = 1; i < ranges.Count; i++)
        {
            var last = ranges[write];
            if (ranges[i].X0 <= last.X1 + 0.01f)
                ranges[write] = new VisualRange(last.X0, Math.Max(last.X1, ranges[i].X1));
            else
                ranges[++write] = ranges[i];
        }
        ranges.RemoveRange(write + 1, ranges.Count - write - 1);
    }

    /// <summary>
    /// The caret position one step to the visual left or right of
    /// <paramref name="offset"/> — what an arrow key should do on a line that
    /// mixes directions, where the next character in reading order can be on the
    /// far side of the line.
    /// </summary>
    /// <returns>
    /// False when this line has no right-to-left run in it, meaning visual and
    /// logical movement are the same thing and the caller should keep doing
    /// whatever it already did (which knows about surrogate pairs and clusters;
    /// this does not).
    /// </returns>
    public bool TryMoveCaretVisually(int offset, bool right, out int result,
        Func<string, float>? measurePrefix = null) =>
        TryMoveCaretVisually(offset, right, out result, Cutting(measurePrefix));

    /// <inheritdoc cref="TryMoveCaretVisually(int, bool, out int, Func{string, float})"/>
    public bool TryMoveCaretVisually(int offset, bool right, out int result, TextPrefixMeasurer? measurePrefix)
    {
        result = offset;
        bool anyRtl = false;
        for (int i = 0; i < Runs.Count; i++)
            if (Runs[i].IsRtl) { anyRtl = true; break; }
        if (!anyRtl) return false;

        // The nearest boundary strictly beyond the caret's current x, in the
        // requested direction. Boundaries that share an x — the two sides of a
        // direction change — are one caret position, so stepping never stalls.
        const float Epsilon = 0.01f;
        float x = CaretXAt(offset, measurePrefix);
        float bestX = right ? float.MaxValue : float.MinValue;
        bool found = false;

        int start = 0;
        for (int i = 0; i < Runs.Count; i++)
        {
            var run = Runs[i];
            int len = run.Text.Length;
            for (int rel = 0; rel <= len; rel++)
            {
                float bx = RunX(run, rel, measurePrefix);
                bool better = right ? bx > x + Epsilon && bx < bestX : bx < x - Epsilon && bx > bestX;
                if (!better) continue;
                bestX = bx;
                result = start + rel;
                found = true;
            }
            start += len;
        }
        return found;
    }

    // A measurer that cuts each prefix out and hands it to measure; null for null. Its own method,
    // so the closure is made only when there is something to close over (C# makes a captured
    // parameter's closure on entry to the method that declares the lambda).
    private static TextPrefixMeasurer? Cutting(Func<string, float>? measure) =>
        measure is null ? null : CuttingFor(measure);

    private static TextPrefixMeasurer CuttingFor(Func<string, float> measure) =>
        (text, length) => measure(text[..length]);

    // Line-relative x of the boundary before the run-relative logical index rel.
    private static float RunX(ShapedRun run, int rel, TextPrefixMeasurer? measurePrefix)
    {
        int len = run.Text.Length;
        rel = Math.Clamp(rel, 0, len);
        bool oneToOne = run.Glyphs.Length == len;

        if (!run.IsRtl)
        {
            if (rel == 0) return run.OffsetX;
            if (rel >= len) return run.OffsetX + run.Width;
            return run.OffsetX + (oneToOne ? run.Glyphs[rel].X : Prefix(run, rel, measurePrefix));
        }

        // RTL: the logical prefix occupies the RIGHT of the run, and the shaper
        // returned glyphs in visual order (RtlGlyphOrderTests pins this), so the
        // boundary before logical `rel` is the left edge of glyph `len - rel`.
        if (rel == 0) return run.OffsetX + run.Width;
        if (rel >= len) return run.OffsetX;
        return oneToOne
            ? run.OffsetX + run.Glyphs[len - rel].X
            : run.OffsetX + run.Width - Prefix(run, rel, measurePrefix);
    }

    private static float Prefix(ShapedRun run, int rel, TextPrefixMeasurer? measurePrefix)
        => measurePrefix is not null
            ? measurePrefix(run.Text, rel)
            : run.Width * rel / run.Text.Length;

    // RunX is monotonic in rel — increasing for an LTR run, decreasing for an RTL
    // one — so the nearest boundary is a binary search, not a scan. That matters
    // for a long line hit-tested on every pointer move during a drag-select.
    private static (int Rel, float Distance) NearestInRun(
        ShapedRun run, float x, TextPrefixMeasurer? measurePrefix)
    {
        int len = run.Text.Length;
        int lo = 0, hi = len;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            float mx = RunX(run, mid, measurePrefix);
            if (run.IsRtl ? mx > x : mx < x) lo = mid + 1;
            else hi = mid;
        }

        int before = Math.Max(0, lo - 1);
        float distanceBefore = Math.Abs(RunX(run, before, measurePrefix) - x);
        float distanceAt = Math.Abs(RunX(run, lo, measurePrefix) - x);
        return distanceBefore <= distanceAt ? (before, distanceBefore) : (lo, distanceAt);
    }
}

/// <summary>A visual x-range on one line, in line-relative coordinates.</summary>
public readonly record struct VisualRange(float X0, float X1);

/// <summary>
/// The width of <c>text[..length]</c> laid out on a line of its own, in the style of the line
/// whose run <paramref name="text"/> is: what <see cref="TextLine"/>'s caret, hit-test and
/// selection mapping asks for inside a run whose shaping merged clusters (ligatures, marks,
/// emoji), where glyphs no longer map one to one onto characters. Handed the run's whole text and
/// the prefix's length, it can measure the prefix where it stands, without cutting it out as a
/// string: <see cref="TextPipeline.MeasureWidth"/> takes a span.
/// </summary>
public delegate float TextPrefixMeasurer(string text, int length);

/// <summary>
/// A run of shaped text — a contiguous segment rendered with a single font.
/// </summary>
public sealed class ShapedRun
{
    public string Text { get; }
    public IResolvedFont Font { get; }
    public float FontSize { get; }
    public float Width { get; }
    public float OffsetX { get; internal set; }

    /// <summary>
    /// UAX #9 embedding level (even = left-to-right, odd = right-to-left). Zero on
    /// every non-BiDi path, which is exactly "left to right".
    /// </summary>
    public byte Level { get; internal set; }

    /// <summary>True when this run's characters read right to left.</summary>
    public bool IsRtl => (Level & 1) == 1;

    /// <summary>Glyph positions relative to the run's origin, for drawing.</summary>
    /// <remarks>
    /// A run the pipeline put together from its word cache — a run of several words — knows its
    /// width and how many glyphs it has from the start, and puts its glyph array together the
    /// first time this is read, from the same words: most runs laid out are never drawn (the
    /// lines of a paragraph off screen, the layouts at the widths a resize passes through), and
    /// their glyphs were most of what wrapping allocated. Read it on the thread that lays text
    /// out with the pipeline that made the run — the UI thread — as the pipeline itself is used.
    /// </remarks>
    public GlyphPosition[] Glyphs => _glyphs as GlyphPosition[] ?? ComposeGlyphs();

    // The glyph array; or, while a run's glyphs are not put together yet, the pipeline that puts
    // them together. One field for both: one more would make every run 8 bytes bigger, and a
    // scroll pays for each run it lays out.
    private object _glyphs;

    // How many glyphs a run whose glyphs are not put together yet has: what the caches weigh it
    // by. 16 bits fit in the room the run's other fields leave; a run of more glyphs is put
    // together when it is made.
    private readonly ushort _pendingGlyphCount;

    /// <summary>How many glyphs the run has, without putting its glyph array together.</summary>
    internal int GlyphCount => _glyphs is GlyphPosition[] glyphs ? glyphs.Length : _pendingGlyphCount;

    /// <summary>Whether the glyph array exists yet. While it does not, a reader that only copies
    /// the glyphs on writes them straight where it copies them to (<see cref="ComposeGlyphsInto"/>).</summary>
    internal bool GlyphsComposed => _glyphs is GlyphPosition[];

    /// <summary>
    /// Whether the run's glyphs follow from its face, size and text alone: the pipeline made it
    /// from its word cache (<see cref="TextPipeline.ShapeByWords"/>), whose segments are shaped by
    /// their text, face and size and set side by side at the widths of those before them, and
    /// nothing moved a glyph after (letter spacing makes a run of its own, which is not marked; nor
    /// is a right-to-left run, which the shaper makes whole). Two marked runs of the same text,
    /// face and size hold the same glyphs at the same places, so the renderer's blob maps match
    /// them by their text, without reading their glyphs — which, for a run of several words, would
    /// put its glyph array together only to find a blob made before.
    /// </summary>
    internal bool GlyphsFollowText { get; init; }

    public ShapedRun(string text, IResolvedFont font, float fontSize, float width, GlyphPosition[] glyphs)
    {
        Text = text;
        Font = font;
        FontSize = fontSize;
        Width = width;
        _glyphs = glyphs;
    }

    // A run whose glyphs the pipeline puts together from its word cache when they are first read
    // (TextPipeline.ComposeGlyphs): so they follow from its text.
    internal ShapedRun(string text, IResolvedFont font, float fontSize, float width, ushort glyphCount,
        TextPipeline composer)
    {
        Text = text;
        Font = font;
        FontSize = fontSize;
        Width = width;
        _pendingGlyphCount = glyphCount;
        _glyphs = composer;
        GlyphsFollowText = true;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private GlyphPosition[] ComposeGlyphs()
    {
        var glyphs = ((TextPipeline)_glyphs).ComposeGlyphs(this, _pendingGlyphCount);
        _glyphs = glyphs;
        return glyphs;
    }

    /// <summary>
    /// Writes the run's <see cref="GlyphCount"/> glyphs into the start of <paramref name="into"/>,
    /// as <see cref="Glyphs"/> holds them, without putting the run's glyph array together when it
    /// is not yet: for a reader that only copies them on, such as a text blob, so a run that is
    /// only drawn never makes an array of its own. Same thread as <see cref="Glyphs"/>.
    /// </summary>
    internal void ComposeGlyphsInto(Span<GlyphPosition> into)
    {
        if (_glyphs is TextPipeline composer)
            composer.ComposeGlyphs(this, into[.._pendingGlyphCount]);
        else
            ((GlyphPosition[])_glyphs).CopyTo(into);
    }
}

/// <summary>
/// A positioned glyph within a shaped run.
/// </summary>
public readonly record struct GlyphPosition(uint GlyphId, float X, float Y, float Advance);

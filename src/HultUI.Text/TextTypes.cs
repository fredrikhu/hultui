using HultUI.Core.Primitives;

namespace HultUI.Text;

/// <summary>
/// Result of text layout: a list of positioned lines.
/// </summary>
public sealed class TextLayout
{
    public static readonly TextLayout Empty = new([], Size.Zero);

    public IReadOnlyList<TextLine> Lines { get; }
    public Size Size { get; }

    public TextLayout(IReadOnlyList<TextLine> lines, Size size)
    {
        Lines = lines;
        Size = size;
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

    /// <summary>
    /// Visual x (line-relative) of the caret at line-relative logical
    /// <paramref name="offset"/>. At a boundary between two direction runs the
    /// caret is placed on the leading edge of the run that *contains* the offset.
    /// </summary>
    public float CaretXAt(int offset, Func<string, float>? measurePrefix = null)
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
    /// <paramref name="x"/> — the hit-test half of <see cref="CaretXAt"/>.
    /// </summary>
    public int OffsetAtX(float x, Func<string, float>? measurePrefix = null)
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
    public IReadOnlyList<VisualRange> VisualRanges(int from, int to, Func<string, float>? measurePrefix = null)
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
    public void VisualRanges(int from, int to, List<VisualRange> into, Func<string, float>? measurePrefix = null)
    {
        into.Clear();
        if (Runs.Count == 0 || to <= from) return;

        var ranges = into;
        int start = 0;
        foreach (var run in Runs)
        {
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

    // Line-relative x of the boundary before the run-relative logical index rel.
    private static float RunX(ShapedRun run, int rel, Func<string, float>? measurePrefix)
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

    private static float Prefix(ShapedRun run, int rel, Func<string, float>? measurePrefix)
        => measurePrefix is not null
            ? measurePrefix(run.Text[..rel])
            : run.Width * rel / run.Text.Length;

    // RunX is monotonic in rel — increasing for an LTR run, decreasing for an RTL
    // one — so the nearest boundary is a binary search, not a scan. That matters
    // for a long line hit-tested on every pointer move during a drag-select.
    private static (int Rel, float Distance) NearestInRun(
        ShapedRun run, float x, Func<string, float>? measurePrefix)
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
    public GlyphPosition[] Glyphs { get; }

    public ShapedRun(string text, IResolvedFont font, float fontSize, float width, GlyphPosition[] glyphs)
    {
        Text = text;
        Font = font;
        FontSize = fontSize;
        Width = width;
        Glyphs = glyphs;
    }
}

/// <summary>
/// A positioned glyph within a shaped run.
/// </summary>
public readonly record struct GlyphPosition(uint GlyphId, float X, float Y, float Advance);

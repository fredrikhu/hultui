using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Rendering;

/// <summary>
/// One paragraph of text laid out at a width and kept, so that everything interactive about
/// it — where a caret goes, what a click means, what a selection covers — reads the same lines
/// and the same glyph positions it is drawn from (<see cref="IRenderContext.DrawParagraph"/>).
/// </summary>
/// <remarks>
/// The string-and-style methods on <see cref="IRenderContext"/> answer those questions for one
/// line in one style. They cannot for styled text: a bold word is wider than the element style
/// measures it, and a larger one moves where the paragraph wraps. A paragraph is laid out once
/// with its spans and answers from that layout. Offsets are absolute into <see cref="Text"/>
/// throughout; x and y are relative to the paragraph's top-left corner.
/// <para>There is always at least one line. Empty text has one empty line of the style's
/// height, and text ending in <c>'\n'</c> has an empty last line after it — the line a caret
/// sits on after Enter — unless <see cref="MaxLines"/> are already used up.</para>
/// </remarks>
public abstract class TextParagraph
{
    protected TextParagraph(string text, ResolvedInheritedStyle style, float maxWidth, IReadOnlyList<TextSpan>? spans,
        int? maxLines = null, TextOverflow overflow = TextOverflow.Clip)
    {
        Text = text;
        Style = style;
        MaxWidth = maxWidth;
        Spans = spans;
        MaxLines = maxLines;
        Overflow = overflow;
    }

    /// <summary>
    /// A paragraph built from <paramref name="ctx"/>'s wrapped lines and one-line caret methods,
    /// for a context that does not lay text out itself: spans are drawn but do not change the
    /// layout. What <see cref="IRenderContext.LayoutParagraph"/> answers by default.
    /// </summary>
    public static TextParagraph FromLines(IRenderContext ctx, string text, ResolvedInheritedStyle style,
        float maxWidth, IReadOnlyList<TextSpan>? spans, int? maxLines = null, TextOverflow overflow = TextOverflow.Clip) =>
        new LineBoxTextParagraph(ctx, text, style, maxWidth, spans, maxLines, overflow);

    public string Text { get; }
    public ResolvedInheritedStyle Style { get; }

    /// <summary>The width it was wrapped at; lines are aligned within it.</summary>
    public float MaxWidth { get; }

    /// <summary>The spans it was laid out and is drawn with.</summary>
    public IReadOnlyList<TextSpan>? Spans { get; }

    /// <summary>The most lines it holds; null for all of them. A label's limit: what it draws is
    /// what it hit-tests.</summary>
    public int? MaxLines { get; }

    /// <summary>What happens to text past <see cref="MaxLines"/> (or past a one-line width).</summary>
    public TextOverflow Overflow { get; }

    /// <summary>Each line's character range and vertical place. Never empty.</summary>
    public abstract IReadOnlyList<TextLineBox> Lines { get; }

    /// <summary>The widest line's width and the lines' total height.</summary>
    public abstract Size Size { get; }

    /// <summary>Where line <paramref name="line"/> starts, from the paragraph's left edge: what
    /// the style's alignment leaves before it. Zero at an unbounded width.</summary>
    public abstract float LineLeft(int line);

    /// <summary>The baseline of line <paramref name="line"/>, from the paragraph's top.</summary>
    public abstract float Baseline(int line);

    /// <summary>The x of the caret at <paramref name="offset"/> on line <paramref name="line"/>,
    /// relative to the line's start. The offset is clamped into the line.</summary>
    protected abstract float CaretXInLine(int line, int offset);

    /// <summary>The offset on line <paramref name="line"/> whose caret is nearest
    /// <paramref name="x"/>, relative to the line's start.</summary>
    protected abstract int OffsetAtXInLine(int line, float x);

    /// <summary>The visual pieces of <c>[from, to)</c> on line <paramref name="line"/>, relative to
    /// the line's start, into <paramref name="into"/> (cleared first). One piece for text that
    /// reads one way; more where the range crosses a change of direction.</summary>
    protected abstract void RangesInLine(int line, int from, int to, List<TextRangeBox> into);

    /// <summary>
    /// The caret one step to the visual left or right of <paramref name="offset"/> on line
    /// <paramref name="line"/>, for a line that mixes directions. False when the line reads
    /// one way throughout and the caller should step logically, which knows about surrogate
    /// pairs and clusters.
    /// </summary>
    public virtual bool TryMoveCaretVisually(int line, int offset, bool right, out int result)
    {
        result = offset;
        return false;
    }

    /// <summary>The line at <paramref name="y"/>: the first or last when above or below them.</summary>
    public int LineAt(float y)
    {
        var lines = Lines;
        int lo = 0, hi = lines.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (lines[mid].OffsetY <= y) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>
    /// The line a caret at <paramref name="offset"/> is drawn on. Where a line wraps, its end
    /// and the next line's start are the same offset: the caret goes to the start of the next
    /// line, as typing there would, unless <paramref name="upstream"/> asks for the end of the
    /// first — where End leaves it.
    /// </summary>
    public int LineOf(int offset, bool upstream = false)
    {
        var lines = Lines;
        int lo = 0, hi = lines.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (lines[mid].Start <= offset) lo = mid;
            else hi = mid - 1;
        }
        if (upstream && lo > 0 && lines[lo].Start == offset
            && lines[lo - 1].Start + lines[lo - 1].Length == offset)
            return lo - 1;
        return lo;
    }

    /// <summary>The caret's x at <paramref name="offset"/>, from the paragraph's left edge, on
    /// line <paramref name="line"/>.</summary>
    public float CaretX(int line, int offset) =>
        LineLeft(line) + CaretXInLine(line, ClampToLine(line, offset));

    /// <summary>The caret at <paramref name="offset"/> as a zero-width box the height of its line.</summary>
    public Rect CaretBox(int offset, bool upstream = false)
    {
        int line = LineOf(offset, upstream);
        var box = Lines[line];
        return new Rect(CaretX(line, offset), box.OffsetY, 0, box.Height);
    }

    /// <summary>The offset on line <paramref name="line"/> nearest <paramref name="x"/>, from the
    /// paragraph's left edge: what a click there, or Up and Down holding that x, lands on.</summary>
    public int OffsetAtX(int line, float x)
    {
        var box = Lines[line];
        int offset = OffsetAtXInLine(line, x - LineLeft(line));
        // Past the last glyph of a wrapped line the caret belongs before the next line's first
        // character, which is this line's end only when nothing was consumed at the wrap: a
        // trailing space eaten by the break is not somewhere a caret can be shown.
        return Math.Clamp(offset, box.Start, box.Start + box.Length);
    }

    /// <summary>The offset nearest the point (<paramref name="x"/>, <paramref name="y"/>),
    /// from the paragraph's top-left corner.</summary>
    public int OffsetAt(float x, float y) => OffsetAtX(LineAt(y), x);

    /// <summary>
    /// The boxes a selection of <c>[from, to)</c> paints, from the paragraph's top-left corner,
    /// into <paramref name="into"/> (not cleared). Where the selection carries on past a line's
    /// end — over a wrap or a newline — the line's box runs <paramref name="lineEndWidth"/> past
    /// its last glyph, so the break reads as selected.
    /// </summary>
    public void SelectionBoxes(int from, int to, List<Rect> into, float lineEndWidth = 0)
    {
        if (to < from) (from, to) = (to, from);
        var lines = Lines;
        var ranges = _ranges ??= [];
        for (int i = LineOf(from); i < lines.Count; i++)
        {
            var box = lines[i];
            int lineEnd = box.Start + box.Length;
            if (box.Start > to || (box.Start == to && to > from)) break;

            int s = Math.Clamp(from, box.Start, lineEnd);
            int e = Math.Clamp(to, box.Start, lineEnd);
            float left = LineLeft(i);
            int firstOfLine = into.Count;
            RangesInLine(i, s, e, ranges);
            foreach (var r in ranges)
                if (r.X1 > r.X0)
                    into.Add(new Rect(left + r.X0, box.OffsetY, r.X1 - r.X0, box.Height));

            bool crossesEnd = from <= lineEnd && to > lineEnd;
            if (crossesEnd && lineEndWidth > 0)
            {
                // One box where the line's own highlight reaches its end, a box of its own where
                // it does not (an empty line, or a range that ended short of a trailing space).
                float endX = left + box.Width;
                int last = into.Count - 1;
                if (last >= firstOfLine && into[last].Right >= endX - 0.01f)
                    into[last] = into[last] with { Width = into[last].Width + lineEndWidth };
                else
                    into.Add(new Rect(endX, box.OffsetY, lineEndWidth, box.Height));
            }
        }
    }

    private int ClampToLine(int line, int offset)
    {
        var box = Lines[line];
        return Math.Clamp(offset, box.Start, box.Start + box.Length);
    }

    private List<TextRangeBox>? _ranges;
}

/// <summary>
/// A paragraph built from what any <see cref="IRenderContext"/> can say: its wrapped lines
/// (<see cref="IRenderContext.LayoutTextLines"/>) and its one-line caret methods. Spans are
/// drawn but do not change the layout — the answer every context gave before spans could. The
/// default for contexts that do not lay text out themselves: recorders and test doubles.
/// </summary>
internal sealed class LineBoxTextParagraph : TextParagraph
{
    private readonly IRenderContext _ctx;
    private readonly TextLineBox[] _lines;
    private readonly float _baseline;

    public LineBoxTextParagraph(IRenderContext ctx, string text, ResolvedInheritedStyle style, float maxWidth,
        IReadOnlyList<TextSpan>? spans, int? maxLines, TextOverflow overflow)
        : base(text, style, maxWidth, spans, maxLines, overflow)
    {
        _ctx = ctx;
        var metrics = ctx.MeasureFont(style);
        float emptyHeight = style.LineHeight > 0 ? style.LineHeight : metrics.LineHeight;
        _baseline = metrics.FirstBaseline(style.LineHeight);

        var lines = new List<TextLineBox>(text.Length == 0 ? [] : ctx.LayoutTextLines(text, style, maxWidth, maxLines));
        if (lines.Count == 0 || (text.EndsWith('\n') && (maxLines is null || lines.Count < maxLines)))
        {
            float top = lines.Count == 0 ? 0 : lines[^1].OffsetY + lines[^1].Height;
            lines.Add(new TextLineBox(text.Length, 0, top, emptyHeight, 0));
        }
        _lines = [.. lines];

        float width = 0;
        foreach (var l in _lines) width = Math.Max(width, l.Width);
        Size = new Size(width, _lines[^1].OffsetY + _lines[^1].Height);
    }

    public override IReadOnlyList<TextLineBox> Lines => _lines;
    public override Size Size { get; }

    public override float LineLeft(int line)
    {
        float factor = TextAlignment.Factor(Style.TextAlign, rtlParagraph: false);
        float spare = MaxWidth - _lines[line].Width;
        return factor > 0 && spare > 0 && !float.IsInfinity(MaxWidth) ? spare * factor : 0;
    }

    public override float Baseline(int line) => _lines[line].OffsetY + _baseline;

    protected override float CaretXInLine(int line, int offset) =>
        _ctx.TextCaretX(LineText(line), Style, offset - _lines[line].Start);

    protected override int OffsetAtXInLine(int line, float x) =>
        _lines[line].Start + _ctx.TextOffsetAtX(LineText(line), Style, x);

    protected override void RangesInLine(int line, int from, int to, List<TextRangeBox> into)
    {
        into.Clear();
        int start = _lines[line].Start;
        into.AddRange(_ctx.TextSelectionRanges(LineText(line), Style, from - start, to - start));
    }

    public override bool TryMoveCaretVisually(int line, int offset, bool right, out int result)
    {
        int start = _lines[line].Start;
        bool moved = _ctx.TryMoveCaretVisually(LineText(line), Style, offset - start, right, out int rel);
        result = start + rel;
        return moved;
    }

    private string LineText(int line) => Text.Substring(_lines[line].Start, _lines[line].Length);
}

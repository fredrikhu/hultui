using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Text;

/// <summary>
/// A <see cref="TextParagraph"/> over the pipeline's own <see cref="TextLayout"/>: the caret,
/// hit-test and selection answers come from the shaped runs a renderer draws, so they agree with
/// the glyphs on screen for styled runs and for lines that mix directions alike.
/// </summary>
public sealed class PipelineTextParagraph : TextParagraph
{
    private readonly TextPipeline _pipeline;
    private readonly TextLine[] _textLines;
    private TextLineBox[]? _boxes;
    private readonly float _alignFactor;
    // RangesInLine's scratch, made the first time a selection or a span background is drawn on
    // this paragraph: most paragraphs never are.
    private List<VisualRange>? _visual;

    internal PipelineTextParagraph(TextPipeline pipeline, string text, ResolvedInheritedStyle style, float maxWidth,
        IReadOnlyList<Core.Rendering.TextSpan>? spans, int? maxLines, TextOverflow overflow, TextLayout layout)
        : base(text, style, maxWidth, spans, maxLines, overflow)
    {
        _pipeline = pipeline;
        Layout = layout;
        _alignFactor = TextAlignment.Factor(style.TextAlign, layout.IsRtlParagraph);

        // The pipeline gives empty text no line, and puts none after a final newline: the
        // newline sits between lines and nothing follows it. A caret needs a line in both. With
        // no line to add, the layout's own array of its lines, shared with the layout cache; else
        // one array of exactly the lines, copied straight from the layout's. A copy every time
        // and the boxes made up front were 13 % of what opening a long rich text document
        // allocated (plan 17, round 8).
        var laid = layout.Lines;
        int count = laid.Count;
        bool addEmpty = count == 0 || (text.EndsWith('\n') && (maxLines is null || count < maxLines));
        if (!addEmpty && laid is TextLine[] exact)
            _textLines = exact;
        else
        {
            var lines = new TextLine[count + (addEmpty ? 1 : 0)];
            for (int i = 0; i < count; i++)   // indexed: Lines is an interface, and a foreach boxes
                lines[i] = laid[i];
            if (addEmpty)
            {
                var empty = pipeline.EmptyLine(style, text.Length);
                empty.OffsetY = layout.Size.Height;
                lines[count] = empty;
            }
            _textLines = lines;
        }

        float width = 0;
        for (int i = 0; i < _textLines.Length; i++)
            width = Math.Max(width, _textLines[i].Width);
        var last = _textLines[^1];
        Size = new Size(width, last.OffsetY + last.Height);
    }

    // The boxes, the first time Lines is read. Without a reader the rich text editor reads them
    // only for the caret's block, a selection, find hits and list markers it draws; a reader reads
    // every paragraph's.
    private TextLineBox[] MakeBoxes()
    {
        var boxes = new TextLineBox[_textLines.Length];
        for (int i = 0; i < boxes.Length; i++)
        {
            var l = _textLines[i];
            boxes[i] = new TextLineBox(l.Start, l.Length, l.OffsetY, l.Height, l.Width);
        }
        return boxes;
    }

    /// <summary>The layout as the pipeline returned it, shared with its cache.</summary>
    public TextLayout Layout { get; }

    /// <summary>The lines to draw: <see cref="TextLayout.Lines"/>, and the empty line the
    /// paragraph adds after a final newline or for empty text. Read-only: when the paragraph adds
    /// no line this is the layout's own list, shared with the pipeline's layout cache.</summary>
    public IReadOnlyList<TextLine> TextLines => _textLines;

    public override IReadOnlyList<TextLineBox> Lines => _boxes ??= MakeBoxes();
    public override Size Size { get; }

    public override float LineLeft(int line)
    {
        float spare = MaxWidth - _textLines[line].Width;
        return _alignFactor > 0f && spare > 0f && !float.IsInfinity(MaxWidth) ? spare * _alignFactor : 0f;
    }

    public override float Baseline(int line) => _textLines[line].OffsetY + _textLines[line].Ascent;

    protected override float CaretXInLine(int line, int offset)
    {
        var l = _textLines[line];
        return l.CaretXAt(offset - l.Start, PrefixMeasurer(l));
    }

    protected override int OffsetAtXInLine(int line, float x)
    {
        var l = _textLines[line];
        return l.Start + l.OffsetAtX(x, PrefixMeasurer(l));
    }

    protected override void RangesInLine(int line, int from, int to, List<TextRangeBox> into)
    {
        into.Clear();
        var l = _textLines[line];
        var visual = _visual ??= [];
        l.VisualRanges(from - l.Start, to - l.Start, visual, PrefixMeasurer(l));
        foreach (var r in visual)
            into.Add(new TextRangeBox(r.X0, r.X1));
    }

    public override bool TryMoveCaretVisually(int line, int offset, bool right, out int result)
    {
        var l = _textLines[line];
        bool moved = l.TryMoveCaretVisually(offset - l.Start, right, out int rel, PrefixMeasurer(l));
        result = l.Start + rel;
        return moved;
    }

    /// <summary>
    /// For a line whose shaping merged clusters (ligatures, marks, emoji), so glyphs no longer
    /// map one to one onto characters: measures a prefix of a run's text the way the renderer's
    /// own caret methods do, in the paragraph's style, where it stands in the run's text
    /// (<see cref="TextPipeline.MeasureWidth"/>: the width its own line has, to the bit). Null —
    /// no closure — for the common case. The pipeline keeps one per style: a selection drawn over
    /// such a line asks for it every frame.
    /// </summary>
    internal TextPrefixMeasurer? PrefixMeasurer(TextLine line)
    {
        // Indexed: Runs is an interface, and a foreach over it boxes an enumerator per call.
        var runs = line.Runs;
        for (int i = 0; i < runs.Count; i++)
            if (runs[i].GlyphCount != runs[i].Text.Length)
                return _pipeline.PrefixMeasurer(Style);
        return null;
    }
}

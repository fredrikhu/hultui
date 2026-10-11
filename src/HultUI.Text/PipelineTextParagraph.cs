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
    private readonly TextLineBox[] _boxes;
    private readonly float _alignFactor;
    private readonly List<VisualRange> _visual = [];

    internal PipelineTextParagraph(TextPipeline pipeline, string text, ResolvedInheritedStyle style, float maxWidth,
        IReadOnlyList<Core.Rendering.TextSpan>? spans, int? maxLines, TextOverflow overflow, TextLayout layout)
        : base(text, style, maxWidth, spans, maxLines, overflow)
    {
        _pipeline = pipeline;
        Layout = layout;
        _alignFactor = TextAlignment.Factor(style.TextAlign, layout.IsRtlParagraph);

        // The pipeline gives empty text no line, and puts none after a final newline: the
        // newline sits between lines and nothing follows it. A caret needs a line in both.
        var lines = new List<TextLine>(layout.Lines);
        if (lines.Count == 0 || (text.EndsWith('\n') && (maxLines is null || lines.Count < maxLines)))
        {
            var empty = pipeline.EmptyLine(style, text.Length);
            empty.OffsetY = layout.Size.Height;
            lines.Add(empty);
        }
        _textLines = [.. lines];

        _boxes = new TextLineBox[_textLines.Length];
        float width = 0;
        for (int i = 0; i < _textLines.Length; i++)
        {
            var l = _textLines[i];
            _boxes[i] = new TextLineBox(l.Start, l.Length, l.OffsetY, l.Height, l.Width);
            width = Math.Max(width, l.Width);
        }
        var last = _textLines[^1];
        Size = new Size(width, last.OffsetY + last.Height);
    }

    /// <summary>The layout as the pipeline returned it, shared with its cache.</summary>
    public TextLayout Layout { get; }

    /// <summary>The lines to draw: <see cref="TextLayout.Lines"/>, and the empty line the
    /// paragraph adds after a final newline or for empty text.</summary>
    public IReadOnlyList<TextLine> TextLines => _textLines;

    public override IReadOnlyList<TextLineBox> Lines => _boxes;
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
        l.VisualRanges(from - l.Start, to - l.Start, _visual, PrefixMeasurer(l));
        foreach (var r in _visual)
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
    /// no closure — for the common case.
    /// </summary>
    internal TextPrefixMeasurer? PrefixMeasurer(TextLine line)
    {
        // Indexed: Runs is an interface, and a foreach over it boxes an enumerator per call.
        var runs = line.Runs;
        for (int i = 0; i < runs.Count; i++)
            if (runs[i].Glyphs.Length != runs[i].Text.Length)
                return (text, length) => _pipeline.MeasureWidth(text.AsSpan(0, length), Style);
        return null;
    }
}

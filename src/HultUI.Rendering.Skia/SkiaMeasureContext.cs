using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;

namespace HultUI.Rendering.Skia;

public sealed class SkiaMeasureContext : IMeasureContext
{
    private readonly TextPipeline _pipeline;

    public TextPipeline Pipeline => _pipeline;

    public SkiaMeasureContext(TextPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow = TextOverflow.Clip)
    {
        if (string.IsNullOrEmpty(text))
            return Size.Zero;

        var layout = _pipeline.Layout(text, style, maxWidth, maxLines, overflow);
        return layout.Size;
    }

    public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow, IReadOnlyList<TextSpan>? spans)
    {
        if (string.IsNullOrEmpty(text))
            return Size.Zero;

        var layout = _pipeline.Layout(text, style, maxWidth, maxLines, overflow, spans);
        return layout.Size;
    }

    public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow, IReadOnlyList<TextSpan>? spans, out float sameFromWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            sameFromWidth = float.NegativeInfinity;   // nothing to lay out, at any width
            return Size.Zero;
        }

        var layout = _pipeline.Layout(text, style, maxWidth, maxLines, overflow, spans, out sameFromWidth);
        return layout.Size;
    }

    public float MinContentTextWidth(string text, ResolvedInheritedStyle style,
        IReadOnlyList<TextSpan>? spans = null)
        => _pipeline.MinContentWidth(text, style, spans);

    public FontMetrics MeasureFont(ResolvedInheritedStyle style) => _pipeline.MeasureFont(style);

    public bool HasGlyph(ResolvedInheritedStyle style, int codePoint) => _pipeline.HasGlyph(style, codePoint);

    // The first line's ascent is where DrawText puts its baseline, fallback fonts and an
    // explicit line height included — so this is exact, not the font-metric estimate.
    public float MeasureFirstBaseline(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow = TextOverflow.Clip, IReadOnlyList<TextSpan>? spans = null)
    {
        var layout = string.IsNullOrEmpty(text)
            ? TextLayout.Empty
            : _pipeline.Layout(text, style, maxWidth, maxLines, overflow, spans);
        return layout.Lines.Count > 0
            ? layout.Lines[0].OffsetY + layout.Lines[0].Ascent
            : MeasureFont(style).FirstBaseline(style.LineHeight);
    }
}

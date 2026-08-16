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

    public float MinContentTextWidth(string text, ResolvedInheritedStyle style,
        IReadOnlyList<TextSpan>? spans = null)
        => _pipeline.MinContentWidth(text, style, spans);
}

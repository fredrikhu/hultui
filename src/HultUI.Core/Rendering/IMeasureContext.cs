using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Rendering;

public interface IMeasureContext
{
    /// <summary>
    /// Measures <paramref name="text"/> as it would be laid out in a box
    /// <paramref name="maxWidth"/> wide.
    ///
    /// <para><paramref name="overflow"/> must match what the caller will draw with:
    /// the modes disagree about what happens to text that does not fit, so measuring
    /// under one and drawing under the other reports a size the element never takes.
    /// It defaults to <see cref="TextOverflow.Clip"/> because that is what
    /// <c>DrawText</c> defaults to, and because at an unbounded width — how most
    /// callers here measure — nothing is truncated and the mode cannot matter.</para>
    /// </summary>
    Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow = TextOverflow.Clip);

    /// <summary>
    /// Span-aware measurement: metric-affecting spans (weight, slant, size) change
    /// the answer. Default ignores the spans so measure contexts that predate them
    /// keep working; contexts backed by the real pipeline override it.
    /// </summary>
    Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow, IReadOnlyList<TextSpan>? spans)
        => MeasureText(text, style, maxWidth, maxLines, overflow);

    /// <summary>
    /// The narrowest width at which <paramref name="text"/> does not overflow — the
    /// widest segment between line-break opportunities, i.e. usually the longest
    /// word. This is what CSS calls min-content width, and it is what an automatic
    /// minimum size protects.
    /// </summary>
    /// <remarks>
    /// The default measures at zero available width, which wraps at every character
    /// and so answers with the widest *character*. That is a floor below the real
    /// one — conservative, never over-pinning — and it is what every measure context
    /// answered before this method existed. Contexts backed by the real pipeline
    /// override it and answer properly.
    /// </remarks>
    float MinContentTextWidth(string text, ResolvedInheritedStyle style,
        IReadOnlyList<TextSpan>? spans = null)
        => MeasureText(text, style, 0f, null, TextOverflow.Clip, spans).Width;

    /// <summary>
    /// The metrics of the font <paramref name="style"/> names, at its size and in its weight and
    /// slant. The default estimates them from a measured <c>"0"</c>
    /// (<see cref="FontMetrics.Estimate"/>); contexts backed by the real pipeline read the font.
    /// </summary>
    FontMetrics MeasureFont(ResolvedInheritedStyle style)
        => FontMetrics.Estimate(style, MeasureText("0", style, float.PositiveInfinity, 1));

    /// <summary>
    /// Whether the face <paramref name="style"/> resolves to has its own glyph for
    /// <paramref name="codePoint"/>, without falling back to another font. Text draws a missing
    /// character from a fallback font, whose advance need not match: a grid that steps by
    /// <see cref="FontMetrics.CharacterWidth"/> draws such a character on its own. The default,
    /// for contexts that cannot read a font, says yes.
    /// </summary>
    bool HasGlyph(ResolvedInheritedStyle style, int codePoint) => true;

    /// <summary>
    /// How far below the top of the box <paramref name="text"/> is laid out in its first line's
    /// baseline sits — what <c>AlignItems.Baseline</c> lines text up by. The default works it out
    /// from <see cref="MeasureFont"/>, placing the baseline where the pipeline does: the ascent,
    /// plus half of whatever an explicit line height adds. Contexts backed by the real pipeline
    /// override it with the laid-out first line, which knows about fallback fonts and spans.
    /// </summary>
    float MeasureFirstBaseline(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow = TextOverflow.Clip, IReadOnlyList<TextSpan>? spans = null)
        => MeasureFont(style).FirstBaseline(style.LineHeight);
}

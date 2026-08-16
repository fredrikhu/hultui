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
}

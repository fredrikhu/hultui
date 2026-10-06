using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Headless.Recording;

/// <summary>
/// Measures text with fixed synthetic metrics instead of real font data:
/// every glyph is <paramref name="charWidthRatio"/> × font size wide, every
/// line <paramref name="lineHeightRatio"/> × font size tall.
/// </summary>
/// <remarks>
/// <para>
/// Real measurement goes through the system font manager, so glyph advances —
/// and therefore every laid-out position downstream — differ between Windows
/// and Linux. That is what makes naive cross-platform snapshots flaky. Fixed
/// metrics trade fidelity for determinism: positions no longer match what a
/// user sees, but they are identical on every machine, so a baseline recorded
/// on a laptop compares clean on a CI runner.
/// </para>
/// <para>
/// Use it for layout/paint regression snapshots that must run everywhere. Use
/// real metrics when the test is pinned to one platform and you care that the
/// geometry reflects actual rendering.
/// </para>
/// </remarks>
public sealed class FixedMetricsMeasureContext(
    float charWidthRatio = 0.5f,
    float lineHeightRatio = 1.2f) : IMeasureContext
{
    // The overflow mode is accepted and ignored: this context exists to give render-log
    // baselines identical metrics on every OS, and both modes truncate to the same box.
    public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow = TextOverflow.Clip)
    {
        if (string.IsNullOrEmpty(text))
            return Size.Zero;

        float charWidth = style.FontSize * charWidthRatio;
        float lineHeight = style.LineHeight > 0f ? style.LineHeight : style.FontSize * lineHeightRatio;

        float totalWidth = text.Length * charWidth;
        if (totalWidth <= maxWidth)
            return new Size(totalWidth, lineHeight);

        int charsPerLine = Math.Max(1, (int)(maxWidth / charWidth));
        int lineCount = (int)Math.Ceiling((double)text.Length / charsPerLine);
        if (maxLines is { } max)
            lineCount = Math.Min(lineCount, max);

        return new Size(Math.Min(totalWidth, maxWidth), lineCount * lineHeight);
    }

    // As the real pipeline: one line that fits is the same at any width it fits in.
    public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow, IReadOnlyList<TextSpan>? spans, out float sameFromWidth)
    {
        var size = MeasureText(text, style, maxWidth, maxLines, overflow);
        float totalWidth = string.IsNullOrEmpty(text) ? 0f : text.Length * style.FontSize * charWidthRatio;
        sameFromWidth = totalWidth <= maxWidth ? totalWidth : float.NaN;
        return size;
    }
}

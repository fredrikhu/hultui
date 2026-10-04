using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Rendering;

/// <summary>
/// A font's metrics at a size, in layout units: what a grid of characters needs — a terminal,
/// a code view, a hex dump — to place its cells and lines without measuring strings.
/// Positions are measured from the baseline, positive downwards, to the top of the stroke.
/// </summary>
/// <param name="FamilyName">The face actually used. When the family asked for is not installed
/// this names the face that stood in for it, which is how a caller can tell.</param>
/// <param name="Ascent">From the baseline up to the top of the line box; positive.</param>
/// <param name="Descent">From the baseline down to the bottom of the line box; positive.</param>
/// <param name="LineGap">The extra space the font asks for between lines; usually 0.</param>
/// <param name="CharacterWidth">The advance of the digit zero as this style draws it: CSS's
/// <c>ch</c> unit, and in a monospace font every character's advance, which is the cell width.
/// Exact, not rounded: text steps by it.</param>
/// <param name="IsFixedPitch">Whether the font says every glyph has the same advance.</param>
/// <param name="UnderlinePosition">Where an underline's stroke starts below the baseline.</param>
/// <param name="UnderlineThickness">An underline's thickness.</param>
/// <param name="StrikeoutPosition">Where a strikeout's stroke starts; negative, since it is
/// above the baseline.</param>
/// <param name="StrikeoutThickness">A strikeout's thickness.</param>
public readonly record struct FontMetrics(
    string FamilyName,
    float Ascent,
    float Descent,
    float LineGap,
    float CharacterWidth,
    bool IsFixedPitch,
    float UnderlinePosition,
    float UnderlineThickness,
    float StrikeoutPosition,
    float StrikeoutThickness)
{
    /// <summary>The height of one line as text draws it: ascent, descent and line gap. A style's
    /// <see cref="ResolvedInheritedStyle.LineHeight"/> is not applied.</summary>
    public float LineHeight => Ascent + Descent + LineGap;

    /// <summary>Where a line of this font puts its baseline, below the top of the line: the
    /// ascent, plus half of whatever an explicit <paramref name="lineHeight"/> (0 for none) adds
    /// above the text's own height — the text pipeline centres a line in a taller line box.</summary>
    public float FirstBaseline(float lineHeight)
    {
        float extra = lineHeight - (Ascent + Descent);
        return lineHeight > 0 && extra > 0 ? Ascent + extra / 2 : Ascent;
    }

    /// <summary>
    /// Metrics worked out from the size of a measured <c>"0"</c>, for a measure context that
    /// cannot read a font. The decoration lines are where the renderer draws them for a font
    /// that does not say.
    /// </summary>
    public static FontMetrics Estimate(ResolvedInheritedStyle style, Size zero)
    {
        float height = zero.Height > 0 ? zero.Height : style.FontSize * 1.2f;
        float ascent = height * 0.8f;
        float thickness = MathF.Max(1f, style.FontSize * 0.07f);
        return new FontMetrics(
            FamilyName: style.FontFamily,
            Ascent: ascent,
            Descent: height - ascent,
            LineGap: 0,
            CharacterWidth: zero.Width > 0 ? zero.Width : style.FontSize * 0.6f,
            IsFixedPitch: false,
            UnderlinePosition: MathF.Max(1f, style.FontSize * 0.12f),
            UnderlineThickness: thickness,
            StrikeoutPosition: -(ascent * 0.33f + thickness / 2f),
            StrikeoutThickness: thickness);
    }
}

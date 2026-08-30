using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// The slider's colours. Added late: the slider was the last interactive control still taking
/// its palette straight off <see cref="Theme"/> with nothing an app could override, so a design
/// system whose unfilled track is a mid grey and whose thumb is an accent dot inside a pale
/// ring — Fluent's, among others — had no way to say so.
///
/// <para>Sizes are deliberately not here. <see cref="Elements.SliderElement.TrackHeight"/> and
/// <see cref="Elements.SliderElement.ThumbRadius"/> stay per-instance, because
/// <c>ApplyTheme</c> runs on every style pass and a themed size would overwrite whatever the
/// caller set — and, being a size, it would need a layout invalidation to go with it.</para>
/// </summary>
public sealed class SliderTheme : IThemeExtension<SliderTheme>
{
    /// <summary>The unfilled part of the track. Falls back to <see cref="Theme.Border"/>.</summary>
    public Color? TrackColor { get; init; }

    /// <summary>The filled part, left of the thumb. Falls back to <see cref="Theme.Primary"/>.</summary>
    public Color? FillColor { get; init; }

    /// <summary>The thumb's fill. Falls back to white, which is what it has always been.</summary>
    public Color? ThumbColor { get; init; }

    /// <summary>The 2px ring around the thumb. Falls back to the fill colour, so a slider that
    /// sets nothing here looks exactly as it did before this type existed.</summary>
    public Color? ThumbBorderColor { get; init; }

    /// <summary>
    /// The built-in defaults are literal rather than derived from the palette: they are the
    /// values this control already shipped with, and deriving them here would repaint every
    /// existing app. New extensions should prefer deriving from the theme — see
    /// <see cref="IThemeExtension{TSelf}"/>.
    /// </summary>
    public static SliderTheme CreateDefault(Theme theme) => new();
}

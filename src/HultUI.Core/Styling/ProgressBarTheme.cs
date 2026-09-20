using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// The track and fill of a <see cref="Elements.ProgressBarElement"/>. The colours are
/// nullable so an app can override one knob (<c>With(new ProgressBarTheme { Height = 8f })</c>)
/// and keep the palette's: the fill is the primary colour on a border-coloured track.
/// </summary>
public sealed class ProgressBarTheme : IThemeExtension<ProgressBarTheme>
{
    public float Height { get; init; } = 4f;
    /// <summary>Corner radius of track and fill. Null rounds fully (a pill).</summary>
    public float? CornerRadius { get; init; }
    /// <summary>Track colour; null takes the theme's border colour.</summary>
    public Color? Track { get; init; }
    /// <summary>Fill colour; null takes the theme's primary.</summary>
    public Color? Fill { get; init; }

    /// <summary>Seconds one indeterminate sweep takes to cross the track.</summary>
    public float IndeterminateSeconds { get; init; } = 1.4f;
    /// <summary>Width of the indeterminate segment as a fraction of the track.</summary>
    public float IndeterminateFraction { get; init; } = 0.35f;

    public static ProgressBarTheme CreateDefault(Theme theme) => new();
}

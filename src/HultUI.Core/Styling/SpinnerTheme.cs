using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// The dots of a <see cref="Elements.SpinnerElement"/>: a ring of them, the leading one
/// at full colour and the rest fading behind it. The colour is nullable so an app can
/// override one knob (<c>With(new SpinnerTheme { Size = 24f })</c>) and keep the
/// palette's primary for the rest.
/// </summary>
public sealed class SpinnerTheme : IThemeExtension<SpinnerTheme>
{
    /// <summary>Dot colour; null takes the theme's primary.</summary>
    public Color? Color { get; init; }
    public float Size { get; init; } = 16f;
    public int DotCount { get; init; } = 8;
    /// <summary>Seconds for one full turn.</summary>
    public float PeriodSeconds { get; init; } = 0.9f;
    /// <summary>Alpha of the dot furthest behind the leading one, 0..1.</summary>
    public float TailAlpha { get; init; } = 0.15f;

    public static SpinnerTheme CreateDefault(Theme theme) => new();
}

using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// The pill a <see cref="Elements.ToastElement"/> draws. Colours are nullable so a
/// one-knob override keeps the palette's: the pill is a raised surface with the theme's
/// border, the action verb is the primary colour, and the severity accents have fixed
/// defaults because the palette has no success or warning colour of its own. Timings
/// live on the <see cref="Builders.ToastHost"/>, which owns the clock.
/// </summary>
public sealed class ToastTheme : IThemeExtension<ToastTheme>
{
    /// <summary>Pill fill; null takes the theme's surface.</summary>
    public Color? Background { get; init; }
    /// <summary>Pill outline; null takes the theme's border.</summary>
    public Color? Border { get; init; }
    /// <summary>Message colour; null takes the theme's text colour.</summary>
    public Color? TextColor { get; init; }
    /// <summary>The action verb's colour; null takes the theme's primary.</summary>
    public Color? ActionColor { get; init; }
    /// <summary>Accent bar for <see cref="Elements.ToastSeverity.Success"/>.</summary>
    public Color Success { get; init; } = new(34, 197, 94);     // green-500
    /// <summary>Accent bar for <see cref="Elements.ToastSeverity.Warning"/>.</summary>
    public Color Warning { get; init; } = new(245, 158, 11);    // amber-500
    /// <summary>Accent bar for <see cref="Elements.ToastSeverity.Error"/>; null takes the theme's danger colour.</summary>
    public Color? Error { get; init; }

    public float CornerRadius { get; init; } = 8f;
    public Thickness Padding { get; init; } = new(9, 16);
    public float FontSize { get; init; } = 12.5f;
    /// <summary>Space between the message and the action verb.</summary>
    public float ActionGap { get; init; } = 12f;
    /// <summary>Width of the severity bar at the pill's leading edge.</summary>
    public float AccentWidth { get; init; } = 3f;
    /// <summary>Space between the severity bar and the message.</summary>
    public float AccentGap { get; init; } = 10f;

    public static ToastTheme CreateDefault(Theme theme) => new();
}

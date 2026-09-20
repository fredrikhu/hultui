using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// The pill and knob of a <see cref="Elements.ToggleSwitchElement"/>. The colours are
/// nullable so an app can override one knob (<c>With(new ToggleSwitchTheme { TrackWidth = 48f })</c>)
/// and keep the palette's: the on-state track is the primary colour, the off state an
/// outlined neutral.
/// </summary>
public sealed class ToggleSwitchTheme : IThemeExtension<ToggleSwitchTheme>
{
    public float TrackWidth { get; init; } = 40f;
    public float TrackHeight { get; init; } = 20f;
    public float KnobSize { get; init; } = 14f;

    /// <summary>Track fill while on; null takes the theme's primary.</summary>
    public Color? OnTrack { get; init; }
    /// <summary>Track fill while off; null takes the theme's surface variant.</summary>
    public Color? OffTrack { get; init; }
    /// <summary>Outline around the off-state track, so an unfilled switch still reads as
    /// a control on a same-coloured surface; null takes the theme's secondary.</summary>
    public Color? OffBorder { get; init; }
    public float OffBorderWidth { get; init; } = 1f;
    /// <summary>Knob colour while on; null takes the theme's primary text colour.</summary>
    public Color? OnKnob { get; init; }
    /// <summary>Knob colour while off; null takes the theme's secondary.</summary>
    public Color? OffKnob { get; init; }

    /// <summary>How long the knob takes to slide across. Zero snaps.</summary>
    public float TransitionSeconds { get; init; } = 0.15f;

    public static ToggleSwitchTheme CreateDefault(Theme theme) => new();
}

using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// The pill and knob of a <see cref="Elements.ToggleSwitchElement"/>. Derived from the
/// palette by default, so a themed app gets a matching switch without setting anything:
/// the on-state track is the primary colour, the off state is an outlined neutral.
/// </summary>
public sealed class ToggleSwitchTheme : IThemeExtension<ToggleSwitchTheme>
{
    public float TrackWidth { get; init; } = 40f;
    public float TrackHeight { get; init; } = 20f;
    public float KnobSize { get; init; } = 14f;

    /// <summary>Track fill while on.</summary>
    public Color OnTrack { get; init; }
    /// <summary>Track fill while off.</summary>
    public Color OffTrack { get; init; }
    /// <summary>Outline drawn around the off-state track, so an unfilled switch still
    /// reads as a control on a same-coloured surface. Transparent to omit.</summary>
    public Color OffBorder { get; init; }
    public float OffBorderWidth { get; init; } = 1f;
    /// <summary>Knob colour while on.</summary>
    public Color OnKnob { get; init; }
    /// <summary>Knob colour while off.</summary>
    public Color OffKnob { get; init; }

    /// <summary>How long the knob takes to slide across. Zero snaps.</summary>
    public float TransitionSeconds { get; init; } = 0.15f;

    public static ToggleSwitchTheme CreateDefault(Theme theme) => new()
    {
        OnTrack = theme.Primary,
        OffTrack = theme.SurfaceVariant,
        OffBorder = theme.Secondary,
        OnKnob = theme.PrimaryText,
        OffKnob = theme.Secondary,
    };
}

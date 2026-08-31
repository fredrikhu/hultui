using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// The two-ring focus visual: a thick outer ring with a thin one inside it, in colours that are
/// each other's opposite.
///
/// <para>One ring can only ever contrast with one background. A white ring vanishes on a light
/// card and a dark one vanishes on a dark button, and a focus indicator that disappears on some
/// controls is worse than none, because you stop looking for it. Two rings of opposing colours
/// contrast with everything: whichever half the surface swallows, the other half shows. Windows
/// draws it this way, and so does every other system that has thought about keyboard use.</para>
///
/// <para>The defaults derive from the palette rather than being literal, and get the relationship
/// right in both: <see cref="Theme.TextColor"/> on the outside is dark on a light theme and light
/// on a dark one, with <see cref="Theme.Background"/> inside it doing the opposite.</para>
/// </summary>
public sealed class FocusVisualTheme : IThemeExtension<FocusVisualTheme>
{
    /// <summary>The outer, thicker ring — WinUI's <c>FocusStrokeColorOuter</c>.</summary>
    public Color OuterColor { get; init; }

    /// <summary>The inner, thinner ring, between the outer one and the control —
    /// <c>FocusStrokeColorInner</c>.</summary>
    public Color InnerColor { get; init; }

    public float OuterThickness { get; init; } = 2f;
    public float InnerThickness { get; init; } = 1f;

    /// <summary>Gap between the control's edge and the first ring. The visual is drawn outside
    /// the control, so a tight row needs a little space reserved for it — which is why this is
    /// opt-in per element rather than on for anything focusable.</summary>
    public float Margin { get; init; } = 1f;

    /// <summary>
    /// Whether the visual only appears when focus arrived from the keyboard.
    ///
    /// <para>On by default, and it is the difference between a focus ring and an annoyance:
    /// clicking a button gives it focus too, and a ring that lights up on every click reads as
    /// something being stuck. Windows shows it for Tab and not for the mouse.</para>
    /// </summary>
    public bool KeyboardOnly { get; init; } = true;

    public static FocusVisualTheme CreateDefault(Theme theme) => new()
    {
        OuterColor = theme.TextColor,
        InnerColor = theme.Background,
    };
}

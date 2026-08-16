using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

public sealed class ButtonTheme : IThemeExtension<ButtonTheme>
{
    public CornerRadius CornerRadius { get; init; } = new(6f);
    public Thickness Padding { get; init; } = new(8, 16, 8, 16);
    public float FontSize { get; init; } = 14f;
    public FontWeight FontWeight { get; init; } = FontWeight.SemiBold;

    /// <summary>Opacity multiplier for hover state (applied to the variant color).</summary>
    public float HoverDarken { get; init; } = 0.85f;

    /// <summary>Opacity multiplier for pressed state (applied to the variant color).</summary>
    public float PressedDarken { get; init; } = 0.7f;

    /// <summary>
    /// The built-in defaults are literal rather than derived from the palette: they are the
    /// values these controls already shipped with, and deriving them here would repaint
    /// every existing app. New extensions should prefer deriving from the theme — see
    /// <see cref="IThemeExtension{TSelf}"/>.
    /// </summary>
    public static ButtonTheme CreateDefault(Theme theme) => new();
}

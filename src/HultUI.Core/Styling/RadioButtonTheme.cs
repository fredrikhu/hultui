using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

public sealed class RadioButtonTheme : IThemeExtension<RadioButtonTheme>
{
    public float Size { get; init; } = 18f;
    public Color? BorderColor { get; init; }
    /// <summary>Stroke of the unselected ring. Null keeps the element's own
    /// <see cref="Elements.RadioButtonElement.BorderWidth"/> (2px).</summary>
    public float? BorderWidth { get; init; }
    public Color? SelectedColor { get; init; }
    public Color? DotColor { get; init; }

    /// <summary>
    /// The built-in defaults are literal rather than derived from the palette: they are the
    /// values these controls already shipped with, and deriving them here would repaint
    /// every existing app. New extensions should prefer deriving from the theme — see
    /// <see cref="IThemeExtension{TSelf}"/>.
    /// </summary>
    public static RadioButtonTheme CreateDefault(Theme theme) => new();
}

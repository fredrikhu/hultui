using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

public sealed class CheckboxTheme : IThemeExtension<CheckboxTheme>
{
    public float Size { get; init; } = 18f;
    public CornerRadius CornerRadius { get; init; } = new(3f);
    public Color? BorderColor { get; init; }
    /// <summary>Stroke of the unchecked box. Null keeps the element's own
    /// <see cref="Elements.CheckboxElement.BorderWidth"/> (2px).</summary>
    public float? BorderWidth { get; init; }
    public Color? CheckedBackground { get; init; }
    public Color? CheckMarkColor { get; init; }

    /// <summary>
    /// The built-in defaults are literal rather than derived from the palette: they are the
    /// values these controls already shipped with, and deriving them here would repaint
    /// every existing app. New extensions should prefer deriving from the theme — see
    /// <see cref="IThemeExtension{TSelf}"/>.
    /// </summary>
    public static CheckboxTheme CreateDefault(Theme theme) => new();
}

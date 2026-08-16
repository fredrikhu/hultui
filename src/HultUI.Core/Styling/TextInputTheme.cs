using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

public sealed class TextInputTheme : IThemeExtension<TextInputTheme>
{
    public CornerRadius CornerRadius { get; init; } = new(4f);
    public Thickness Padding { get; init; } = new(4, 8, 4, 8);
    public Thickness BorderWidth { get; init; } = new(1f);
    public float FontSize { get; init; } = 14f;
    public Color? Background { get; init; }
    public Color? BorderColor { get; init; }
    public Color? FocusBorderColor { get; init; }
    public Color? PlaceholderColor { get; init; }
    public Color? SelectionColor { get; init; }
    public Color? CursorColor { get; init; }

    /// <summary>
    /// The built-in defaults are literal rather than derived from the palette: they are the
    /// values these controls already shipped with, and deriving them here would repaint
    /// every existing app. New extensions should prefer deriving from the theme — see
    /// <see cref="IThemeExtension{TSelf}"/>.
    /// </summary>
    public static TextInputTheme CreateDefault(Theme theme) => new();
}

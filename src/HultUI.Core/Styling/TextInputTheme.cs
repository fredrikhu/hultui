using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

public sealed class TextInputTheme
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
}

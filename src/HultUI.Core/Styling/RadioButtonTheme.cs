using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

public sealed class RadioButtonTheme
{
    public float Size { get; init; } = 18f;
    public Color? BorderColor { get; init; }
    public Color? SelectedColor { get; init; }
    public Color? DotColor { get; init; }
}

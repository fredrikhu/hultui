using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

public sealed class CheckboxTheme
{
    public float Size { get; init; } = 18f;
    public CornerRadius CornerRadius { get; init; } = new(3f);
    public Color? BorderColor { get; init; }
    public Color? CheckedBackground { get; init; }
    public Color? CheckMarkColor { get; init; }
}

using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

public sealed class ButtonTheme
{
    public CornerRadius CornerRadius { get; init; } = new(6f);
    public Thickness Padding { get; init; } = new(8, 16, 8, 16);
    public float FontSize { get; init; } = 14f;
    public FontWeight FontWeight { get; init; } = FontWeight.SemiBold;

    /// <summary>Opacity multiplier for hover state (applied to the variant color).</summary>
    public float HoverDarken { get; init; } = 0.85f;

    /// <summary>Opacity multiplier for pressed state (applied to the variant color).</summary>
    public float PressedDarken { get; init; } = 0.7f;
}

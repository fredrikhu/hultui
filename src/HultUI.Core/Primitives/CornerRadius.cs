namespace HultUI.Core.Primitives;

public readonly record struct CornerRadius(float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
    public CornerRadius(float uniform) : this(uniform, uniform, uniform, uniform) { }

    public bool IsUniform => TopLeft == TopRight && TopRight == BottomRight && BottomRight == BottomLeft;
    public bool IsZero => TopLeft == 0 && TopRight == 0 && BottomRight == 0 && BottomLeft == 0;

    public static CornerRadius Zero => default;
}

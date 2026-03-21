namespace HultUI.Core.Primitives;

public readonly record struct Size(float Width, float Height)
{
    public static Size Zero => default;
}

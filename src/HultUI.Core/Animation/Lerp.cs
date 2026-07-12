using HultUI.Core.Primitives;

namespace HultUI.Core.Animation;

public static class Lerp
{
    public static float Float(float a, float b, float t) => a + (b - a) * t;

    public static Color Color(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t),
        (byte)(a.A + (b.A - a.A) * t));

    public static Thickness Thickness(Thickness a, Thickness b, float t) => new(
        Float(a.Top, b.Top, t),
        Float(a.Right, b.Right, t),
        Float(a.Bottom, b.Bottom, t),
        Float(a.Left, b.Left, t));

    public static CornerRadius CornerRadius(CornerRadius a, CornerRadius b, float t) => new(
        Float(a.TopLeft, b.TopLeft, t),
        Float(a.TopRight, b.TopRight, t),
        Float(a.BottomRight, b.BottomRight, t),
        Float(a.BottomLeft, b.BottomLeft, t));

    public static Dimension Dimension(Dimension a, Dimension b, float t)
    {
        if (a.Unit == DimensionUnit.Pixels && b.Unit == DimensionUnit.Pixels)
            return Primitives.Dimension.Px(Float(a.Value, b.Value, t));
        return t < 1f ? a : b;
    }

    public static Transform2D Transform(Transform2D a, Transform2D b, float t) => new(
        Float(a.RotationDegrees, b.RotationDegrees, t),
        Float(a.ScaleX, b.ScaleX, t),
        Float(a.ScaleY, b.ScaleY, t),
        Float(a.TranslateX, b.TranslateX, t),
        Float(a.TranslateY, b.TranslateY, t));

    public static Point Point(Point a, Point b, float t) =>
        new(Float(a.X, b.X, t), Float(a.Y, b.Y, t));

    public static Size Size(Size a, Size b, float t) =>
        new(Float(a.Width, b.Width, t), Float(a.Height, b.Height, t));
}

using HultUI.Core.Primitives;

namespace HultUI.Core.Animation;

public static class Lerp
{
    public static float Float(float a, float b, float t) => a + (b - a) * t;

    public static Color Color(Color a, Color b, float t)
    {
        // Interpolate in premultiplied-alpha space so fading to or from a
        // transparent endpoint (whose RGB is black, e.g. Color.Transparent) does
        // not pass through dark colours. For opaque endpoints this reduces to a
        // plain per-channel lerp.
        float aA = a.A / 255f, bA = b.A / 255f;
        float pr = Float(a.R * aA, b.R * bA, t);
        float pg = Float(a.G * aA, b.G * bA, t);
        float pb = Float(a.B * aA, b.B * bA, t);
        float pa = Float(aA, bA, t);

        if (pa <= 0f)
            return new Color(0, 0, 0, 0);

        return new Color(
            (byte)Math.Clamp(pr / pa, 0f, 255f),
            (byte)Math.Clamp(pg / pa, 0f, 255f),
            (byte)Math.Clamp(pb / pa, 0f, 255f),
            (byte)Math.Clamp(pa * 255f, 0f, 255f));
    }

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

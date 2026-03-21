namespace HultUI.Core.Primitives;

public readonly record struct Point(float X, float Y)
{
    public static Point Zero => default;

    public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
    public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
}

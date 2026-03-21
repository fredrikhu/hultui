namespace HultUI.Core.Primitives;

public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public bool Contains(Point p) =>
        p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    public Rect Deflate(Thickness t) =>
        new(X + t.Left, Y + t.Top,
            Math.Max(0, Width - t.Left - t.Right),
            Math.Max(0, Height - t.Top - t.Bottom));

    public Rect Inflate(Thickness t) =>
        new(X - t.Left, Y - t.Top,
            Width + t.Left + t.Right,
            Height + t.Top + t.Bottom);

    public static Rect Zero => default;
}

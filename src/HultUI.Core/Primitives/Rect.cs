namespace HultUI.Core.Primitives;

public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public bool Contains(Point p) =>
        p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    public Rect Deflate(Thickness t)
    {
        float w = Width - t.Left - t.Right;
        float h = Height - t.Top - t.Bottom;
        return new(X + t.Left, Y + t.Top,
            float.IsNegative(w) ? 0 : w,
            float.IsNegative(h) ? 0 : h);
    }

    public Rect Deflate(Thickness a, Thickness b)
    {
        float w = Width - a.Left - a.Right - b.Left - b.Right;
        float h = Height - a.Top - a.Bottom - b.Top - b.Bottom;
        return new(X + a.Left + b.Left, Y + a.Top + b.Top,
            float.IsNegative(w) ? 0 : w,
            float.IsNegative(h) ? 0 : h);
    }

    public Rect Inflate(Thickness t) =>
        new(X - t.Left, Y - t.Top,
            Width + t.Left + t.Right,
            Height + t.Top + t.Bottom);

    public static Rect Zero => default;
}

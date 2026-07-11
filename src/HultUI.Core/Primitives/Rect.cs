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

    /// <summary>Shifts the rect by (dx, dy) without changing its size.</summary>
    public Rect Translate(float dx, float dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>True when this rect and <paramref name="other"/> share any area.</summary>
    public bool IntersectsWith(Rect other) =>
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    /// <summary>The overlapping region of the two rects, or <see cref="Zero"/> if they don't overlap.</summary>
    public Rect Intersect(Rect other)
    {
        float x = X > other.X ? X : other.X;
        float y = Y > other.Y ? Y : other.Y;
        float right = Right < other.Right ? Right : other.Right;
        float bottom = Bottom < other.Bottom ? Bottom : other.Bottom;
        return right > x && bottom > y ? new Rect(x, y, right - x, bottom - y) : Zero;
    }

    public static Rect Zero => default;
}

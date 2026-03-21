namespace HultUI.Core.Primitives;

public readonly record struct Thickness(float Top, float Right, float Bottom, float Left)
{
    public Thickness(float uniform) : this(uniform, uniform, uniform, uniform) { }

    public Thickness(float vertical, float horizontal) : this(vertical, horizontal, vertical, horizontal) { }

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    public static Thickness Zero => default;

    public static Thickness operator +(Thickness a, Thickness b) =>
        new(a.Top + b.Top, a.Right + b.Right, a.Bottom + b.Bottom, a.Left + b.Left);
}

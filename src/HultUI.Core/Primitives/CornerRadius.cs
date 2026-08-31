namespace HultUI.Core.Primitives;

public readonly record struct CornerRadius(float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
    public CornerRadius(float uniform) : this(uniform, uniform, uniform, uniform) { }

    public bool IsUniform => TopLeft == TopRight && TopRight == BottomRight && BottomRight == BottomLeft;
    public bool IsZero => TopLeft == 0 && TopRight == 0 && BottomRight == 0 && BottomLeft == 0;

    public static CornerRadius Zero => default;

    /// <summary>
    /// This radius grown by <paramref name="by"/>, so a ring drawn that far outside the box
    /// stays concentric with it instead of squaring off at the corners.
    ///
    /// <para>A square corner stays square: adding to a zero radius would round a corner that
    /// was never meant to be round, which on a flat-cornered control reads as the ring not
    /// belonging to it. Negative growth clamps at zero for the same reason.</para>
    /// </summary>
    public CornerRadius Inflate(float by) => new(
        TopLeft > 0 ? MathF.Max(0f, TopLeft + by) : 0f,
        TopRight > 0 ? MathF.Max(0f, TopRight + by) : 0f,
        BottomRight > 0 ? MathF.Max(0f, BottomRight + by) : 0f,
        BottomLeft > 0 ? MathF.Max(0f, BottomLeft + by) : 0f);
}

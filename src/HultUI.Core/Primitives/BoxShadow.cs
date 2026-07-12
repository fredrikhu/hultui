namespace HultUI.Core.Primitives;

/// <summary>
/// A drop shadow cast by a box, modelled on CSS <c>box-shadow</c> (outset only).
/// <see cref="Blur"/> is the blur diameter in pixels; <see cref="Spread"/> grows
/// (or, when negative, shrinks) the shadow shape before blurring.
/// </summary>
public readonly record struct BoxShadow(float OffsetX, float OffsetY, float Blur, float Spread, Color Color)
{
    public BoxShadow(float offsetX, float offsetY, float blur, Color color)
        : this(offsetX, offsetY, blur, 0f, color) { }
}

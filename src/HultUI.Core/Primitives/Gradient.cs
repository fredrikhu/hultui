namespace HultUI.Core.Primitives;

public enum GradientKind : byte
{
    Linear,
    Radial
}

/// <summary>A single colour stop, positioned at <paramref name="Offset"/> in 0..1.</summary>
public readonly record struct GradientStop(float Offset, Color Color);

/// <summary>
/// A linear or radial gradient fill. For linear gradients <see cref="AngleDegrees"/>
/// is measured clockwise from the positive X axis: 0 runs left→right, 90 runs
/// top→bottom. Radial gradients emanate from the box centre out to its corner.
/// </summary>
public sealed class Gradient
{
    public GradientKind Kind { get; }
    public float AngleDegrees { get; }
    public IReadOnlyList<GradientStop> Stops { get; }

    private Gradient(GradientKind kind, float angleDegrees, IReadOnlyList<GradientStop> stops)
    {
        Kind = kind;
        AngleDegrees = angleDegrees;
        Stops = stops;
    }

    public static Gradient Linear(float angleDegrees, params GradientStop[] stops) =>
        new(GradientKind.Linear, angleDegrees, stops);

    public static Gradient Linear(float angleDegrees, Color from, Color to) =>
        new(GradientKind.Linear, angleDegrees, [new GradientStop(0f, from), new GradientStop(1f, to)]);

    public static Gradient Horizontal(Color from, Color to) => Linear(0f, from, to);

    public static Gradient Vertical(Color from, Color to) => Linear(90f, from, to);

    public static Gradient Radial(params GradientStop[] stops) =>
        new(GradientKind.Radial, 0f, stops);

    public static Gradient Radial(Color inner, Color outer) =>
        new(GradientKind.Radial, 0f, [new GradientStop(0f, inner), new GradientStop(1f, outer)]);
}

namespace HultUI.Core.Primitives;

/// <summary>
/// Sentinel for an available size that is <em>indefinite</em> — the main axis
/// of a scroll container, where content is free to overflow and scroll rather
/// than being compressed to fit.
/// </summary>
/// <remarks>
/// Deliberately large-but-finite rather than <see cref="float.PositiveInfinity"/>:
/// downstream layout arithmetic (subtracting padding, summing child sizes)
/// stays well-defined, and no NaN can escape into element geometry. Detection
/// uses a generous threshold so a value that has had padding and borders
/// subtracted from it still reads as unbounded.
/// </remarks>
public static class LayoutSpace
{
    /// <summary>An indefinite available size.</summary>
    public const float Unbounded = float.MaxValue / 2;

    /// <summary>True when <paramref name="availableSize"/> is (or derives from) <see cref="Unbounded"/>.</summary>
    public static bool IsUnbounded(float availableSize) => availableSize >= float.MaxValue / 4;
}

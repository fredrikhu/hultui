using HultUI.Core.Primitives;

namespace HultUI.Core.Rendering;

/// <summary>
/// A colored range of a text element's content, in UTF-16 character offsets
/// (syntax highlighting and similar). Spans must be sorted by <see cref="Start"/>
/// and non-overlapping; characters not covered by any span render in the
/// element's own text color. Colors are purely a paint concern — they never
/// affect shaping, wrapping, or measurement.
/// </summary>
public readonly record struct TextSpan(int Start, int Length, Color Color)
{
    public int End => Start + Length;
}

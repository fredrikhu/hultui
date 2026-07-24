using HultUI.Core.Primitives;

namespace HultUI.Core.Rendering;

/// <summary>
/// A styled range of a text element's content, in UTF-16 character offsets
/// (syntax highlighting and similar). Spans must be sorted by <see cref="Start"/>
/// and non-overlapping; characters not covered by any span render in the
/// element's own text color. A transparent <see cref="Color"/> (A == 0) means
/// "keep the element's text color" — useful for background-only spans.
/// <see cref="Background"/> fills the span's line rect behind the glyphs
/// (diff word highlights, search matches); transparent means none. Both are
/// purely a paint concern — they never affect shaping, wrapping, or measurement.
/// </summary>
public readonly record struct TextSpan(int Start, int Length, Color Color, Color Background = default)
{
    public int End => Start + Length;
}

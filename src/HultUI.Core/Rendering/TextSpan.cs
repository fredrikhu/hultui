using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Rendering;

/// <summary>
/// A styled range of a text element's content, in UTF-16 character offsets
/// (syntax highlighting and similar). Spans must be sorted by <see cref="Start"/>
/// and non-overlapping; characters not covered by any span render in the
/// element's own text color. A transparent <see cref="Color"/> (A == 0) colours
/// nothing: the text keeps the element's colour — useful for background-only spans —
/// or, where spans overlap (a TextArea's may), the colour another span gives it.
/// <see cref="Background"/> fills the span's line rect behind the glyphs
/// (diff word highlights, search matches); transparent means none.
/// <see cref="Decoration"/> underlines or strikes the span's range, in the
/// span's effective text color. All of these are purely a paint concern — they
/// never affect shaping, wrapping, or measurement.
/// <see cref="Weight"/>, <see cref="Slant"/>, <see cref="FontSize"/> and
/// <see cref="FontFamily"/> do: the range is shaped in its own face and wraps as part
/// of the paragraph (docs/plans/12-rich-text-spans.md). Null keeps the element's.
/// </summary>
public readonly record struct TextSpan(
    int Start, int Length, Color Color, Color Background = default,
    TextDecoration Decoration = TextDecoration.None,
    FontWeight? Weight = null, FontSlant? Slant = null, float? FontSize = null,
    string? FontFamily = null)
{
    public int End => Start + Length;

    /// <summary>True when this span changes shaping or measurement (weight, slant, size
    /// or family override) rather than only paint.</summary>
    public bool IsMetricAffecting =>
        Weight is not null || Slant is not null || FontSize is not null || FontFamily is not null;
}

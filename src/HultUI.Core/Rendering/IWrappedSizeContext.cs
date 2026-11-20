using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Rendering;

/// <summary>
/// A measure context that answers <see cref="TextMeasure.MeasureWrapped"/> without making the layout
/// <see cref="IMeasureContext.MeasureText(string, ResolvedInheritedStyle, float, int?, TextOverflow, IReadOnlyList{TextSpan}?)"/>
/// makes for it: the same size, to the bit, at any number of lines, clipped. A list of wrapped rows asks
/// every row's height and draws a few rows, so the layouts made for the rest were thrown away at once.
/// </summary>
internal interface IWrappedSizeContext
{
    Size MeasureWrapped(string text, ResolvedInheritedStyle style, float maxWidth, IReadOnlyList<TextSpan>? spans);
}

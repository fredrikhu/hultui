using HultUI.Core.Layout;
using HultUI.Core.Primitives;

namespace HultUI.Core.Rendering;

/// <summary>
/// A render context that can draw a still image at an opacity in one draw, so a faded image
/// with no children needs no offscreen group (<see cref="HultUI.Core.Elements.ImageElement"/>). Blending one
/// image at an opacity is the same as drawing it with its alpha scaled by that much, give or take
/// a level of rounding. Internal: the Skia renderer and the headless recorder implement it.
/// </summary>
internal interface IFadingImageContext
{
    /// <summary>
    /// Draws <paramref name="source"/> as <see cref="IRenderContext.DrawImage(IImageSource, Rect, ImageFit)"/>
    /// does, at <paramref name="opacity"/>. Returns false, having drawn nothing, for a source it
    /// cannot draw that way — a GPU or frame-stream source, which keeps its group.
    /// </summary>
    bool TryDrawImage(IImageSource source, Rect destRect, ImageFit fit, float opacity);
}

using HultUI.Core.Rendering;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

/// <summary>
/// A surface that can composite a frame some other API rendered.
///
/// <para>The render context used to name <see cref="SkiaGLRenderSurface"/> outright and call
/// its <c>WrapTexture</c>, which was true while GL was the only surface that could do this.
/// A Vulkan surface can too — over an image it lent out through
/// <see cref="IExternalGLSurface.TryAcquireFrameTarget"/> — but it has no GL texture to wrap
/// and could never have satisfied a method named for one.</para>
///
/// <para>Internal because <see cref="SKImage"/> is: this is the seam inside the renderer, not
/// part of its public surface.</para>
/// </summary>
internal interface ISkiaExternalSurface : IExternalGLSurface
{
    /// <summary>
    /// The frame as something the canvas can draw, or null when it cannot be wrapped. The
    /// image reads the frame's storage as it is when the canvas flushes; the caller owns the
    /// returned image and the storage stays the producer's.
    /// </summary>
    SKImage? WrapFrame(in GpuFrame frame);

    /// <summary>
    /// What the frame has to be multiplied by on the way into this surface's canvas, or null
    /// when it can be drawn as it is.
    ///
    /// <para>A frame of linear light says how many nits its 1.0 stands for, and the canvas has
    /// its own idea of what 1.0 means; the two rarely agree. A plain
    /// <see cref="SKColorFilter.CreateColorMatrix"/> cannot do this, because it clamps at 1
    /// and the values above 1 are the entire point of an HDR frame.</para>
    ///
    /// <para>Null from a GL surface, always: it composites an encoded picture into an encoded
    /// target and there is nothing to convert.</para>
    /// </summary>
    SKColorFilter? FrameColorFilter(in GpuFrame frame) => null;
}

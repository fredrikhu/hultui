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
}

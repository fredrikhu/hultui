namespace HultUI.Core.Rendering;

/// <summary>
/// A render surface that lets foreign code draw with OpenGL inside the frame — a
/// video decoder rendering into a texture the renderer then composites. Implemented
/// by the GL-backed Skia surface; a raster surface is not one, and a producer drawn
/// on it must fall back to <see cref="IPixelImageSource"/>.
/// </summary>
public interface IExternalGLSurface : IRenderSurface
{
    /// <summary>
    /// Resolves GL entry points in this surface's context, the same resolver the
    /// renderer was built with, so foreign code lands in the same context.
    /// </summary>
    Func<string, nint> GetProcAddress { get; }

    /// <summary>
    /// Runs <paramref name="draw"/> now, with the renderer's pending work flushed
    /// first and its GL state cache invalidated afterwards, so neither side sees the
    /// other's bindings. Only valid on the render thread inside a frame — from
    /// <see cref="IGpuImageSource.TryRenderFrame"/>, in practice. The callee may bind
    /// its own framebuffer and textures and need not restore anything.
    /// </summary>
    void RunExternalGL(Action draw);
}

/// <summary>
/// A GL texture the renderer composites, as an <see cref="IGpuImageSource"/> describes
/// its rendered frame. The texture belongs to the source, which keeps it alive until
/// the frame after it stops being returned.
/// </summary>
/// <param name="TextureId">The GL texture name (a 2D texture).</param>
/// <param name="Width">Texture width in pixels.</param>
/// <param name="Height">Texture height in pixels.</param>
/// <param name="BottomLeftOrigin">True when row 0 is the bottom, as for a texture the
/// source rendered into through a framebuffer; false for one uploaded top-down.</param>
/// <param name="InternalFormat">The GL internal format; <c>GL_RGBA8</c> (0x8058) unless
/// the source says otherwise.</param>
public readonly record struct GpuFrame(
    uint TextureId, int Width, int Height, bool BottomLeftOrigin, uint InternalFormat = GpuFrame.Rgba8)
{
    /// <summary><c>GL_RGBA8</c>.</summary>
    public const uint Rgba8 = 0x8058;
}

/// <summary>
/// An image the producer renders on the GPU during the frame, into a texture of its
/// own, which the renderer composites without a copy. The renderer asks on every draw,
/// on a surface that is an <see cref="IExternalGLSurface"/>; on any other surface it
/// never asks, and draws the source as an <see cref="IPixelImageSource"/> if it is
/// one, or nothing.
/// </summary>
/// <remarks>
/// <see cref="TryRenderFrame"/> runs on the render thread inside the draw. The source
/// does its GL work inside <see cref="IExternalGLSurface.RunExternalGL"/> — creating
/// its texture and framebuffer on first use, then rendering the current frame — and
/// returns the texture. Returning false draws nothing this frame. A producer on
/// another thread only signals (marks the element dirty); it never touches GL.
/// </remarks>
public interface IGpuImageSource : IImageSource
{
    bool TryRenderFrame(IExternalGLSurface surface, out GpuFrame frame);
}

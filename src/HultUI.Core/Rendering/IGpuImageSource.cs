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

    /// <summary>
    /// Offers a render target of this size for the source to draw into, so that the
    /// renderer owns it rather than the source.
    ///
    /// <para>False — the default, and what a GL surface says — means the source makes its
    /// own texture and framebuffer, which is what it has always done and what the whole
    /// arrangement was built around: a GL renderer can composite a GL texture directly, so
    /// there is nothing to gain by owning it.</para>
    ///
    /// <para>A renderer that <b>cannot</b> composite a plain GL texture has to say true. A
    /// Vulkan one is the case in point: it draws Vulkan images, so the framebuffer it hands
    /// back is one imported over an image of its own, and the source rendering into it puts
    /// the pixels somewhere the renderer can already use. Without this the source's texture
    /// would have to be read back through main memory, which is what the software path does
    /// today and costs twice the CPU at 1080p.</para>
    ///
    /// <para>Called inside <see cref="RunExternalGL"/>, where a GL context is current. The
    /// target belongs to the renderer and stays valid until the next call with a different
    /// size, or until the surface is disposed.</para>
    /// </summary>
    bool TryAcquireFrameTarget(int width, int height, out GpuFrameTarget target)
    {
        target = default;
        return false;
    }
}

/// <summary>
/// A render target the renderer owns and lends to a source for one frame — see
/// <see cref="IExternalGLSurface.TryAcquireFrameTarget"/>.
/// </summary>
/// <param name="Framebuffer">The GL framebuffer name to render into.</param>
/// <param name="TextureId">Its colour attachment, for a source that wants to bind it.</param>
/// <param name="InternalFormat">The GL internal format it was created with — not always
/// <c>GL_RGBA8</c>, since a renderer wanting more than eight bits is half the reason for
/// this to exist.</param>
public readonly record struct GpuFrameTarget(uint Framebuffer, uint TextureId, uint InternalFormat);

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
/// <param name="RendererOwned">True when the texture came from
/// <see cref="IExternalGLSurface.TryAcquireFrameTarget"/> rather than from the source. The
/// renderer then composites whatever it lent out and ignores <paramref name="TextureId"/>,
/// which on a Vulkan surface names nothing it can draw.</param>
public readonly record struct GpuFrame(
    uint TextureId, int Width, int Height, bool BottomLeftOrigin, uint InternalFormat = GpuFrame.Rgba8,
    bool RendererOwned = false)
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

namespace HultUI.Core.Rendering;

/// <summary>
/// A render surface that lets foreign code draw with OpenGL inside the frame — a
/// video decoder rendering into a texture the renderer then composites. Implemented
/// by the GL-backed Skia surface, and by the Vulkan one, which shares an image with a
/// hidden GL context when the driver allows it. A raster surface is not one, and a
/// producer drawn on it must fall back to <see cref="IPixelImageSource"/>.
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
    /// Whether foreign GL code can actually run here, asked before anything is drawn.
    ///
    /// <para>True for a GL surface, which is one by construction. A Vulkan surface is an
    /// external surface only if it can make a GL context beside its device and share an
    /// image with it, and that is a property of the driver rather than of the renderer —
    /// a second GPU, or a missing external-memory extension, and it cannot.</para>
    ///
    /// <para>False means the producer is drawn by whatever else it is, which for a video
    /// is its pixel path: slower, and working. The alternative is a renderer that composites
    /// a GL texture it has no way to read, which draws nothing at all.</para>
    ///
    /// <para>Reading this may do one-off setup and take a driver load's worth of time. It is
    /// read on the render thread, on the first frame that has a GPU source to draw.</para>
    /// </summary>
    bool SupportsExternalGL => true;

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
    /// <para>Each <paramref name="owner"/> — one per GPU source — gets a target of its own, so a
    /// video and a web page can both draw in one frame. Called inside <see cref="RunExternalGL"/>,
    /// where a GL context is current. The target belongs to the renderer and stays valid until
    /// the owner's next call with a different size, until <see cref="ReleaseFrameTarget"/>, or
    /// until the surface is disposed.</para>
    /// </summary>
    bool TryAcquireFrameTarget(object owner, int width, int height, out GpuFrameTarget target)
    {
        target = default;
        return false;
    }

    /// <summary>
    /// <paramref name="owner"/> will not draw again: its target, if it was lent one, can go.
    /// Any thread.
    /// </summary>
    void ReleaseFrameTarget(object owner) { }
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
/// <param name="WantsLinearLight">True when the renderer will composite this target as linear
/// light rather than as an encoded SDR picture, and the source should render for that.
///
/// <para>The other half of the reason for sixteen bits. A target that only ever holds an
/// encoded 0..1 picture has more precision and no more range, so HDR content is tone-mapped
/// away before it arrives and the extra bits carry nothing. A producer told this can keep the
/// range instead.</para></param>
/// <param name="PeakNits">What the display can actually show at its brightest, for a producer
/// that must tone-map to it. Zero when unknown, which means the producer should assume rather
/// than believe.</param>
/// <param name="WideGamut">True when the renderer carries colours outside BT.709 as negative
/// channel values — as scRGB does — so a linear-light producer should write them that way
/// rather than squeeze them into BT.709's gamut first.
///
/// <para>The third half of HDR, after range and precision. Most HDR content is mastered in P3
/// inside a BT.2020 container, and a producer told only "linear BT.709" maps every colour
/// outside that gamut inwards before it reaches the target: BT.2020 green arrived as (0, 0.41,
/// 0.12), shifted toward cyan, where the target could have held (-0.26, 0.50, -0.04). Only
/// meaningful with <paramref name="WantsLinearLight"/>.</para></param>
public readonly record struct GpuFrameTarget(
    uint Framebuffer, uint TextureId, uint InternalFormat,
    bool WantsLinearLight = false, float PeakNits = 0f, bool WideGamut = false);

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
/// <param name="ReferenceWhiteNits">Zero — the default — means the frame holds an ordinary
/// encoded picture where 1.0 is white, which is what every source wrote before HDR. Anything
/// else means the frame holds <b>linear light</b>, and says how many nits the value 1.0
/// stands for, so the renderer can put it at the right luminance in its own space.
///
/// <para>The producer describes what it wrote rather than the renderer assuming: the number
/// belongs to whatever rendered the frame. libplacebo, which is what mpv uses, normalises its
/// linear output to the BT.2408 reference white of 203 nits — measured exactly, not read from
/// a document, in <c>spikes/HdrInterop --range</c>.</para></param>
public readonly record struct GpuFrame(
    uint TextureId, int Width, int Height, bool BottomLeftOrigin, uint InternalFormat = GpuFrame.Rgba8,
    bool RendererOwned = false, float ReferenceWhiteNits = 0f)
{
    /// <summary><c>GL_RGBA8</c>.</summary>
    public const uint Rgba8 = 0x8058;

    /// <summary>Whether this frame holds linear light rather than an encoded picture.</summary>
    public bool IsLinearLight => ReferenceWhiteNits > 0f;
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

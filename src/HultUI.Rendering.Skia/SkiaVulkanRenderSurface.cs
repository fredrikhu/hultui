using HultUI.Core.Rendering;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

/// <summary>
/// Skia drawing through Vulkan, for a window whose swapchain the platform layer owns.
///
/// <para>Phase 1 of <c>docs/design/HDR.md</c>. It exists because HDR needs an HDR-capable
/// swapchain and OpenGL on Windows cannot ask for one; Vulkan can, on Windows and Linux
/// both. This phase is deliberately <b>SDR and sRGB</b> — the aim is output indistinguishable
/// from <see cref="SkiaGLRenderSurface"/>, so that everything after it is a change of colour
/// handling rather than a change of renderer.</para>
///
/// <para><b>How it differs from the GL surface.</b> A GL frame has no beginning: Skia draws
/// into framebuffer 0 whenever it likes and the platform swaps buffers afterwards. A Vulkan
/// frame has to acquire an image from the swapchain first, and that image is one of several
/// that rotate. So the surface is not one <see cref="SKSurface"/> but a small cache of them,
/// one per swapchain image, rebuilt whenever the swapchain is.</para>
///
/// <para>This assembly knows nothing about Vulkan beyond what SkiaSharp needs, which is
/// handles and integers. <see cref="IVulkanSurfaceHost"/> is the whole of the contract.</para>
/// </summary>
public sealed class SkiaVulkanRenderSurface : IRenderSurface, ISkiaExternalSurface
{
    private readonly IVulkanSurfaceHost _host;
    private readonly GRVkExtensions _extensions;
    private readonly GRVkBackendContext _backend;
    private readonly GRContext _grContext;
    private readonly SkiaRenderContext _renderContext = new();
    private readonly SkiaTextServices _text;

    /// <summary>
    /// This frame's wrapped image, thrown away at the end of it.
    ///
    /// <para>Caching one per swapchain image is the obvious optimisation and it is wrong.
    /// Skia tracks a render target's image layout inside the target, and this class
    /// transitions the image to PRESENT_SRC_KHR behind Skia's back on the way to the screen
    /// — SkiaSharp exposes no <c>setVkImageLayout</c> to tell it so. The next time that
    /// image came round Skia would transition it from a layout it no longer had, which makes
    /// the contents undefined: with three images, the first three frames drew and every
    /// frame after was black. Building the target fresh each frame, declaring UNDEFINED,
    /// costs a validation and is always right.</para>
    /// </summary>
    private GRBackendRenderTarget? _target;
    private SKSurface? _surface;

    /// <summary>
    /// Where the frame is drawn when the swapchain needs values past 1 — see
    /// <see cref="ColorSpaceFor"/>. Plain 0..1 linear sRGB, so a glyph and a fill agree,
    /// and multiplied once on the way to the swapchain.
    /// </summary>
    private SKSurface? _offscreen;
    private SKPaint? _blit;
    private int _offscreenWidth, _offscreenHeight;

    private int _width;
    private int _height;
    private bool _frameOpen;

    public IRenderContext RenderContext => _renderContext;
    public IMeasureContext MeasureContext => _text.MeasureContext;

    /// <param name="host">The window's Vulkan device and swapchain.</param>
    /// <param name="text">A text pipeline to adopt rather than build. Nothing in it touches
    /// the GPU, so a caller that warmed one on another thread can hand it over — the same
    /// arrangement <see cref="SkiaGLRenderSurface"/> has.</param>
    public SkiaVulkanRenderSurface(IVulkanSurfaceHost host, SkiaTextServices? text = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;

        // Skia has to be told what is enabled before it will validate anything against the
        // device. Leaving this null lets the context be created and then makes every surface
        // over a perfectly ordinary BGRA swapchain image come back null.
        _extensions = GRVkExtensions.Create(host.GetProcedureAddress,
            host.Instance, host.PhysicalDevice,
            host.EnabledInstanceExtensions, host.EnabledDeviceExtensions);

        _backend = new GRVkBackendContext
        {
            VkInstance = host.Instance,
            VkPhysicalDevice = host.PhysicalDevice,
            VkDevice = host.Device,
            VkQueue = host.GraphicsQueue,
            GraphicsQueueIndex = host.GraphicsQueueFamily,
            MaxAPIVersion = host.ApiVersion,
            GetProcedureAddress = host.GetProcedureAddress,
            Extensions = _extensions,
            VkPhysicalDeviceFeatures2 = host.PhysicalDeviceFeatures2,
        };

        _grContext = GRContext.CreateVulkan(_backend)
            ?? throw new InvalidOperationException("Failed to create a Skia Vulkan context");

        _text = text ?? new SkiaTextServices();
        _renderContext.SetPipeline(_text.Pipeline);

        // Offered unconditionally; whether it is taken up is decided per frame by
        // SupportsExternalGL, which is the only thing that knows whether this host can
        // actually share an image with a GL context.
        _renderContext.SetGLSurface(this);
    }

    // ──── Foreign GL inside the frame: phase 4 of docs/design/HDR.md ────

    private IVulkanInteropHost? Interop => _host as IVulkanInteropHost;

    /// <summary>The image lent out this frame, so that <see cref="WrapFrame"/> knows what it
    /// is wrapping — the <see cref="GpuFrame"/> itself carries GL names, which name nothing
    /// a Vulkan renderer can draw.</summary>
    private VulkanInteropImage? _lent;

    /// <inheritdoc />
    /// <remarks>
    /// The first read does the work: a hidden GL context, a check that it landed on the same
    /// GPU, a check that the driver will import external memory. False leaves the producer to
    /// its pixel path, which is what a video did on this renderer before phase 4 — slower,
    /// and working.
    /// </remarks>
    public bool SupportsExternalGL => Interop?.TryEnableInterop() ?? false;

    /// <inheritdoc />
    public Func<string, nint> GetProcAddress =>
        Interop?.GlGetProcAddress ?? (_ => 0);

    /// <inheritdoc />
    /// <remarks>
    /// Unlike the GL surface there is no Skia state to protect here: Skia is drawing with
    /// Vulkan and shares nothing with this context. What does need care is <i>order</i> —
    /// Skia reads the image later in the frame, when the canvas flushes, so GL has to be
    /// finished with it by then. That is what <c>FinishExternalFrame</c> is, and it runs even
    /// if the draw threw, because an unordered image is worse than a missed frame.
    /// </remarks>
    public void RunExternalGL(Action draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        if (Interop is not { } interop) return;
        try
        {
            interop.RunOnGLContext(draw);
        }
        finally
        {
            interop.FinishExternalFrame();
        }
    }

    /// <inheritdoc />
    public bool TryAcquireFrameTarget(int width, int height, out GpuFrameTarget target)
    {
        target = default;
        if (Interop?.LendInteropImage(width, height) is not { } image) return false;

        _lent = image;
        target = new GpuFrameTarget(image.GlFramebuffer, image.GlTexture, image.GlInternalFormat);
        return true;
    }

    /// <summary>
    /// The lent image as something the canvas can draw.
    ///
    /// <para>The layout declared here is the one the host's barrier left it in, and it is
    /// declared afresh every frame — the render context never caches a renderer-owned
    /// wrapper, for the same reason this class never caches a render target: Skia tracks
    /// layout inside the wrapper and moves it as it draws, so a reused one describes an image
    /// that no longer exists in that state.</para>
    ///
    /// <para>The colour space is sRGB rather than the swapchain's. mpv writes ordinary
    /// sRGB-encoded values into the half-float target; saying so is what lets Skia convert
    /// them when the frame is being drawn into a linear offscreen, instead of treating
    /// encoded values as linear light and showing a washed-out picture.</para>
    /// </summary>
    SKImage? ISkiaExternalSurface.WrapFrame(in GpuFrame frame)
    {
        if (!frame.RendererOwned) return null;
        if (_lent is not { } image) return null;

        var info = new GRVkImageInfo
        {
            Image = image.Image,
            Alloc = new GRVkAlloc { Memory = image.Memory, Offset = 0, Size = image.MemorySize, Flags = 0 },
            ImageTiling = VkImageTilingOptimal,
            ImageLayout = image.Layout,
            Format = image.Format,
            ImageUsageFlags = image.Usage,
            SampleCount = 1,
            LevelCount = 1,
            CurrentQueueFamily = _host.GraphicsQueueFamily,
            SharingMode = VkSharingModeExclusive,
        };

        var texture = new GRBackendTexture(frame.Width, frame.Height, info);
        if (!texture.IsValid)
        {
            texture.Dispose();
            return null;
        }

        return SKImage.FromTexture(_grContext, texture,
            frame.BottomLeftOrigin ? GRSurfaceOrigin.BottomLeft : GRSurfaceOrigin.TopLeft,
            SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
    }

    /// <summary>
    /// A new size means a new swapchain, and a new swapchain means every wrapped surface
    /// names an image that no longer exists. They go first, deliberately: destroying a
    /// swapchain while Skia still holds render targets over its images is a use-after-free
    /// that the validation layers catch and a release build does not.
    ///
    /// <para>Only a new <i>pixel</i> size. The scale is not a property of the swapchain, and
    /// on Wayland a move between outputs arrives as two calls — the new pixels at the old
    /// scale, then the same pixels at the new one — so keying on the scale too built every
    /// crossing's swapchain twice, a device-wide wait each time.</para>
    /// </summary>
    public void Resize(int widthPx, int heightPx, float scaleFactor)
    {
        if (widthPx == _width && heightPx == _height)
            return;

        _width = widthPx;
        _height = heightPx;

        DropWrappedSurfaces();
        if (widthPx > 0 && heightPx > 0) _host.RecreateSwapchain(widthPx, heightPx);
    }

    /// <summary>
    /// Takes the next swapchain image and points the render context at it. Returns false
    /// when there is nothing to draw into — a resize in flight, or a minimised window — and
    /// the caller skips the frame rather than treating it as a failure.
    /// </summary>
    public bool BeginFrame()
    {
        if (_frameOpen) return true;
        if (_host.AcquireNextImage() is not { } frame) return false;

        var surface = SurfaceFor(frame);
        if (surface is null) return false;

        _renderContext.SetCanvas((OffscreenFor(frame) ?? surface).Canvas);
        _frameOpen = true;
        return true;
    }

    /// <summary>
    /// Flushes Skia's work, submits it, and hands the image to the presentation engine.
    ///
    /// <para>The submit is not optional here, and that is the part that differs from GL. A
    /// flush only records; presenting an image whose drawing has not been submitted shows
    /// the frame before last, intermittently, in a way that looks like a tearing bug.</para>
    /// </summary>
    public void Present()
    {
        if (!_frameOpen) return;

        // The frame was drawn at 0..1; this is where it gets the display's SDR white level,
        // once, as an image rather than as a property of every paint.
        if (_offscreen is { } offscreen && _surface is { } swapchainSurface)
        {
            offscreen.Flush();
            using var image = offscreen.Snapshot();
            swapchainSurface.Canvas.DrawImage(image, 0, 0, _blit);
        }

        _grContext.Flush();
        _grContext.Submit();
        _host.PresentAcquiredImage();
        _frameOpen = false;
    }

    private SKSurface? SurfaceFor(in VulkanFrameTarget frame)
    {
        ReleaseFrameSurface();

        var imageInfo = new GRVkImageInfo
        {
            Image = frame.Image,
            // A swapchain image's memory belongs to the presentation engine, so there is no
            // allocation to describe — this is the documented shape for wrapping one.
            Alloc = new GRVkAlloc { Memory = 0, Offset = 0, Size = 0, Flags = 0 },
            ImageTiling = VkImageTilingOptimal,
            // Undefined, always: the whole frame is about to be overwritten, so discarding
            // is free, and it is the one layout that is true whatever happened to the image
            // between presents.
            ImageLayout = VkImageLayoutUndefined,
            Format = frame.Format,
            ImageUsageFlags = frame.Usage,
            SampleCount = 1,
            LevelCount = 1,
            CurrentQueueFamily = _host.GraphicsQueueFamily,
            SharingMode = VkSharingModeExclusive,
        };

        var target = new GRBackendRenderTarget(frame.WidthPx, frame.HeightPx, imageInfo);
        if (!target.IsValid)
        {
            target.Dispose();
            return null;
        }

        // The colour type has to match the swapchain's format or SKSurface.Create quietly
        // returns null — which looks exactly like a driver refusing the render target, and
        // is not that. Most Windows surfaces offer BGRA first.
        var surface = SKSurface.Create(_grContext, target, GRSurfaceOrigin.TopLeft,
            ColorTypeFor(frame.Format), ColorSpaceFor(frame.ColorSpace));
        if (surface is null)
        {
            target.Dispose();
            return null;
        }

        _target = target;
        _surface = surface;
        return surface;
    }

    /// <summary>
    /// The offscreen to draw into this frame, or null when the swapchain can be drawn on
    /// directly. Needed only when white has to exceed 1, which is scRGB's case and not an
    /// ordinary sRGB swapchain's.
    /// </summary>
    private SKSurface? OffscreenFor(in VulkanFrameTarget frame)
    {
        if (frame.ColorSpace != VkColorSpaceExtendedSrgbLinear || _host.SdrWhiteScale <= 1.0001f)
        {
            ReleaseOffscreen();
            return null;
        }

        if (_offscreen is not null && _offscreenWidth == frame.WidthPx && _offscreenHeight == frame.HeightPx)
            return _offscreen;

        ReleaseOffscreen();

        var info = new SKImageInfo(frame.WidthPx, frame.HeightPx, SKColorType.RgbaF16,
            SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear());
        _offscreen = SKSurface.Create(_grContext, budgeted: true, info);
        if (_offscreen is null) return null;

        _offscreenWidth = frame.WidthPx;
        _offscreenHeight = frame.HeightPx;

        // A shader rather than a colour matrix: the matrix filter clamps to 1, which is the
        // whole thing being worked around. Src rather than SrcOver — the offscreen is the
        // entire frame and there is nothing underneath worth blending with.
        using var effect = SKRuntimeEffect.CreateColorFilter(
            "uniform half k; half4 main(half4 c) { return half4(c.rgb * k, c.a); }", out _);
        if (effect is null) { ReleaseOffscreen(); return null; }

        var uniforms = new SKRuntimeEffectUniforms(effect) { ["k"] = _host.SdrWhiteScale };
        _blit = new SKPaint { ColorFilter = effect.ToColorFilter(uniforms), BlendMode = SKBlendMode.Src };
        return _offscreen;
    }

    private void ReleaseOffscreen()
    {
        _offscreen?.Dispose();
        _blit?.Dispose();
        _offscreen = null;
        _blit = null;
        _offscreenWidth = _offscreenHeight = 0;
    }

    private void ReleaseFrameSurface()
    {
        _surface?.Dispose();
        _target?.Dispose();
        _surface = null;
        _target = null;
    }

    /// <summary>
    /// The colour space of the swapchain, told to Skia so that it converts rather than
    /// assumes — phase 2 of <c>docs/design/HDR.md</c>.
    ///
    /// <para>Untagged, Skia writes an <see cref="SKColor"/>'s bytes through untransformed,
    /// which is right only because the destination happens to be sRGB too. Say so, and the
    /// output is identical while every later step — a linear F16 surface, an HDR colour
    /// space — becomes a conversion Skia performs instead of a mismatch nobody notices.</para>
    ///
    /// <para>A null here is not "no colour management": it is Skia's legacy path, where
    /// blending happens in whatever space the bytes are already in.</para>
    /// </summary>
    /// <summary>
    /// The colour space of the swapchain, told to Skia so that it converts rather than
    /// assumes — phase 2 of <c>docs/design/HDR.md</c>.
    ///
    /// <para><b>The SDR white scale is deliberately not here.</b> scRGB fixes 1.0 at 80 nits
    /// while Windows draws SDR white at whatever its brightness slider says, so an scRGB
    /// surface has to emit more than 1 for white — and the obvious place to put that factor,
    /// the destination colour space, does not work. Dividing the primaries by k makes fills
    /// come out at k and leaves glyphs at 1: <c>spikes/HdrInterop --text</c> shows it, and
    /// also shows that Skia converts text and fills identically for a real colour space
    /// (sRGB red into linear Display P3 lands at 0.823 for both). <b>No paint can put a
    /// glyph past 1</b> — not a scaled colour space, not a colour matrix, not an SkSL colour
    /// filter. The scale has to stop being a property of the drawing and become a property
    /// of the finished image: draw at 0..1 offscreen, multiply once on the way to the
    /// swapchain. That is built and proven in the spike and not yet here, which is why
    /// HULTUI_VULKAN_F16 stays off and why the F16 path is uniformly dim rather than
    /// unevenly bright.</para>
    /// </summary>
    private static SKColorSpace ColorSpaceFor(uint colorSpace) => colorSpace switch
    {
        VkColorSpaceExtendedSrgbLinear => SKColorSpace.CreateSrgbLinear(),
        _ => SKColorSpace.CreateSrgb(),
    };

    /// <summary>
    /// The Skia colour type for a <c>VkFormat</c>.
    /// </summary>
    private static SKColorType ColorTypeFor(uint format) => format switch
    {
        VkFormatR8G8B8A8Unorm or VkFormatR8G8B8A8Srgb => SKColorType.Rgba8888,
        VkFormatB8G8R8A8Unorm or VkFormatB8G8R8A8Srgb => SKColorType.Bgra8888,
        VkFormatR16G16B16A16Sfloat => SKColorType.RgbaF16,
        VkFormatA2B10G10R10UnormPack32 => SKColorType.Rgba1010102,
        _ => SKColorType.Bgra8888,
    };

    private void DropWrappedSurfaces()
    {
        if (_surface is null && _target is null) return;

        // Everything Skia has in flight against this image has to have finished before the
        // swapchain that owns it is destroyed.
        _grContext.Flush();
        _grContext.Submit(synchronous: true);

        ReleaseFrameSurface();
        ReleaseOffscreen();
        _frameOpen = false;
    }

    public void Dispose()
    {
        DropWrappedSurfaces();
        _renderContext.Dispose();
        _grContext.Dispose();
        _backend.Dispose();
        _extensions.Dispose();
    }

    // The handful of Vulkan enum values this file needs, as literals rather than as a
    // dependency on a Vulkan binding — see the note on IVulkanSurfaceHost.
    private const uint VkImageTilingOptimal = 0;
    private const uint VkSharingModeExclusive = 0;
    private const uint VkImageUsageTransferDst = 0x00000002;
    private const uint VkImageUsageColorAttachment = 0x00000010;
    private const uint VkImageLayoutUndefined = 0;
    private const uint VkColorSpaceExtendedSrgbLinear = 1000104002;
    private const uint VkFormatR8G8B8A8Unorm = 37;
    private const uint VkFormatR8G8B8A8Srgb = 43;
    private const uint VkFormatB8G8R8A8Unorm = 44;
    private const uint VkFormatB8G8R8A8Srgb = 50;
    private const uint VkFormatA2B10G10R10UnormPack32 = 64;
    private const uint VkFormatR16G16B16A16Sfloat = 97;
}

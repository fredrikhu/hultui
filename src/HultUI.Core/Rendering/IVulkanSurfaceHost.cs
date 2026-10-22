namespace HultUI.Core.Rendering;

/// <summary>
/// One image of a swapchain, as the renderer needs to see it: a handle, its format, and
/// what layout it is in. Everything else about the swapchain is the platform's business.
/// </summary>
/// <param name="Image">The <c>VkImage</c> handle.</param>
/// <param name="Format">Its <c>VkFormat</c>, as the raw enum value.</param>
/// <param name="Layout">Its current <c>VkImageLayout</c>, as the raw enum value.</param>
/// <param name="WidthPx">Width in device pixels.</param>
/// <param name="HeightPx">Height in device pixels.</param>
/// <param name="Index">Which image of the swapchain this is: what a render target kept per
/// image is found by.</param>
/// <param name="Usage">The <c>VkImageUsageFlags</c> the swapchain was created with. Skia
/// validates the render target against them, so guessing here fails in a way that looks
/// like the driver refusing the image.</param>
/// <param name="ColorSpace">The swapchain's <c>VkColorSpaceKHR</c>. What the renderer needs
/// in order to convert into it rather than assume it matches.</param>
public readonly record struct VulkanFrameTarget(
    ulong Image, uint Format, uint Layout, int WidthPx, int HeightPx, int Index, uint Usage,
    uint ColorSpace)
{
    /// <summary>
    /// The size the swapchain's images were allocated at, when that is larger than the window: a
    /// swapchain over-allocated for a drag and shown one-to-one, which the next steps of the drag
    /// keep. Zero when the images are the window's size, <see cref="WidthPx"/> by
    /// <see cref="HeightPx"/>. A renderer drawing through a surface of its own can make it this
    /// size, and keep it while the swapchain is kept.
    /// </summary>
    public int AllocatedWidthPx { get; init; }

    /// <inheritdoc cref="AllocatedWidthPx"/>
    public int AllocatedHeightPx { get; init; }
}

/// <summary>
/// What a Vulkan-backed window has to provide for Skia to draw on it.
///
/// <para>The split is the same one the GL path already makes: the platform owns the device
/// and the swapchain because it owns the window, and the renderer owns the Skia context and
/// the surfaces wrapped over whatever images it is handed. The GL surface takes a
/// <c>Func&lt;string, IntPtr&gt;</c> and nothing else; this takes a little more only because
/// a Vulkan frame has a beginning and an end where a GL frame has neither.</para>
///
/// <para>Every handle here is a raw <c>nint</c> or <c>ulong</c> deliberately. It keeps the
/// renderer assembly free of any Vulkan binding — SkiaSharp's own Vulkan surface is nothing
/// but <c>IntPtr</c> and <c>uint</c> — so exactly one assembly in the tree needs to know what
/// a <c>VkDevice</c> is.</para>
/// </summary>
public interface IVulkanSurfaceHost
{
    nint Instance { get; }
    nint PhysicalDevice { get; }
    nint Device { get; }

    /// <summary>The queue Skia submits on, and its family index.</summary>
    nint GraphicsQueue { get; }
    uint GraphicsQueueFamily { get; }

    /// <summary>The Vulkan API version the instance was created with, as
    /// <c>VK_MAKE_API_VERSION</c> packs it.</summary>
    uint ApiVersion { get; }

    /// <summary>How many images the swapchain has.</summary>
    int ImageCount { get; }

    /// <summary>
    /// The window size the swapchain was last built for, in device pixels. The renderer starts
    /// from it, so that being told the same size again — which the window does once, after the
    /// surface is built, because the GL surface needs it — rebuilds nothing.
    /// </summary>
    int WidthPx { get; }
    int HeightPx { get; }

    /// <summary>
    /// The extensions actually enabled on the instance and the device. Skia validates every
    /// render target against these, and without them it refuses formats the device plainly
    /// supports — a failure that surfaces as a null surface with no reason given.
    /// </summary>
    string[] EnabledInstanceExtensions { get; }
    string[] EnabledDeviceExtensions { get; }

    /// <summary>
    /// A pointer to a live <c>VkPhysicalDeviceFeatures2</c> for the device. Skia builds its
    /// capability table from this; given neither it nor plain features, it assumes the
    /// minimum and then declines render targets the device plainly supports. The host owns
    /// the memory and keeps it alive for as long as the renderer.
    /// </summary>
    nint PhysicalDeviceFeatures2 { get; }

    /// <summary>
    /// What to multiply linear output by so that 1.0 lands on the display's SDR white.
    ///
    /// <para>1 for an ordinary sRGB swapchain, where the colour space already says what
    /// white is. For scRGB it is the display's SDR white level over scRGB's own reference of
    /// 80 nits — because scRGB fixes 1.0 at 80 and Windows composites SDR content at
    /// whatever its brightness slider says, commonly 240. Writing a correct 1.0 into that
    /// surface produces a window three times too dim beside every other window, and no
    /// capture shows it: the swapchain image is right and the compositor is obeying it.</para>
    /// </summary>
    float SdrWhiteScale { get; }

    /// <summary>
    /// What this display can show at its brightest, in nits, or zero when nobody knows.
    ///
    /// <para>The window sets it from Windows' <c>DisplayInformation</c> when display-colour
    /// events are on, and otherwise from SDL's HDR headroom times the SDR white. Zero means the
    /// display did not say, and a producer told zero assumes a common mastering peak instead.
    /// <c>HULTUI_VULKAN_PEAK_NITS</c> overrides it for a display that reports wrongly.</para>
    /// </summary>
    float DisplayPeakNits => 0f;

    /// <summary>
    /// Whether the compositor shows an ordinary sRGB window through a pure 2.2 gamma rather
    /// than the piecewise sRGB curve — and so whether an scRGB window has to decode its own
    /// sRGB content the same way to look like its neighbours.
    ///
    /// <para>True under KWin: side by side on HDR Wayland, the scRGB window's shadows were
    /// visibly lighter and greyer than the GL window's — the video's blacks and the UI's navy
    /// alike — while white matched, which is where the two curves meet. False on Windows,
    /// whose compositor is not known to differ from the piecewise curve Skia uses.</para>
    /// </summary>
    bool SdrIsGamma22 => false;

    /// <summary>
    /// Copies the image acquired for this frame, as drawn and submitted so far, into
    /// <paramref name="rgba"/>: <see cref="WidthPx"/> × <see cref="HeightPx"/> pixels of opaque
    /// 8-bit sRGB RGBA, top row first, whatever the swapchain's format, an scRGB one divided by
    /// <see cref="SdrWhiteScale"/> first. For saving a frame (<c>Window.SaveFrame</c>), never per
    /// frame: it waits for the GPU. Called after the renderer's work is submitted and before
    /// <see cref="PresentAcquiredImage"/>. False when the host cannot, which is the default.
    /// </summary>
    bool ReadAcquiredImage(byte[] rgba) => false;

    /// <summary>Resolves a Vulkan entry point by name. <paramref name="device"/> is zero for
    /// instance-level commands, which is the convention Skia's own resolver uses.</summary>
    nint GetProcedureAddress(string name, nint instance, nint device);

    /// <summary>
    /// The image to draw this frame into, or null when one could not be had — a swapchain
    /// that went out of date on a resize, or a window with no area. A null is not an error
    /// and the frame is simply skipped.
    /// </summary>
    VulkanFrameTarget? AcquireNextImage();

    /// <summary>
    /// Hands the drawn image back to the presentation engine. Called after Skia has flushed
    /// and submitted its own work, so the platform only has to present.
    /// </summary>
    void PresentAcquiredImage();

    /// <summary>
    /// Resizes the swapchain for a new size: rebuilt, or kept when it was allocated large enough
    /// and is shown one-to-one. A renderer whose host calls <see cref="SetSwapchainReleasing"/>
    /// leaves its wrapped surfaces to that call, which comes only when the images go; any other
    /// drops them first, because they may name images about to be destroyed.
    /// </summary>
    void RecreateSwapchain(int widthPx, int heightPx);

    /// <summary>
    /// Asks the host to call <paramref name="beforeImagesGo"/> just before it destroys a
    /// swapchain's images, for any reason — a rebuild it starts itself (out of date, suboptimal,
    /// a new display format, a minimized window), or its own disposal — and returns whether it
    /// will. Null stops the calls.
    ///
    /// <para>A host that returns true promises two things, and a renderer may then keep a render
    /// target per image for the swapchain's life instead of wrapping each image every frame:
    /// that no image goes before the call, so no render target outlives its image; and that
    /// <see cref="TransitionAcquiredImage"/> works. The default, false, leaves the renderer
    /// wrapping every frame.</para>
    /// </summary>
    bool SetSwapchainReleasing(Action? beforeImagesGo) => false;

    /// <summary>
    /// Moves the acquired image to <c>COLOR_ATTACHMENT_OPTIMAL</c>, the layout Skia leaves a
    /// render target in, in a submit of its own; false when there is no acquired image or the host
    /// cannot. The host moves the image to <c>PRESENT_SRC_KHR</c> behind Skia's back on the way to
    /// the screen, so a render target kept over it believes a layout the image no longer has
    /// until this runs. It must be submitted before any of the renderer's work on the image is:
    /// before Skia draws on it, or at least before Skia submits. Once per acquire; asked again for
    /// the same image it does nothing.
    /// </summary>
    bool TransitionAcquiredImage() => false;
}

/// <summary>
/// A Vulkan image that an OpenGL context can render into, described from both sides.
///
/// <para>One allocation, two names for it. The Vulkan half is what Skia needs to draw the
/// result; the GL half is what a decoder needs to render into it. Nothing is copied between
/// them — that is the whole point, and phase 0 of <c>docs/design/HDR.md</c> is the proof
/// that drivers allow it.</para>
/// </summary>
/// <param name="Image">The <c>VkImage</c> handle.</param>
/// <param name="Memory">Its <c>VkDeviceMemory</c>. A swapchain image has none to describe;
/// this one is an ordinary allocation and Skia wants to know about it.</param>
/// <param name="MemorySize">The allocation's size in bytes, as Vulkan reported it — GL is
/// told the same number on import and a mismatch fails there rather than here.</param>
/// <param name="Format">Its <c>VkFormat</c>.</param>
/// <param name="Usage">The <c>VkImageUsageFlags</c> it was created with.</param>
/// <param name="Layout">The <c>VkImageLayout</c> it is in when the renderer gets it back —
/// what <see cref="IVulkanInteropHost.FinishExternalFrame"/> leaves it in.</param>
/// <param name="WidthPx">Width in device pixels.</param>
/// <param name="HeightPx">Height in device pixels.</param>
/// <param name="GlFramebuffer">A GL framebuffer whose colour attachment is this image.</param>
/// <param name="GlTexture">That attachment, as a GL texture name.</param>
/// <param name="GlInternalFormat">The GL internal format it was imported as — the number a
/// producer has to be told so it stops assuming eight bits.</param>
public readonly record struct VulkanInteropImage(
    ulong Image, ulong Memory, ulong MemorySize, uint Format, uint Usage, uint Layout,
    int WidthPx, int HeightPx, uint GlFramebuffer, uint GlTexture, uint GlInternalFormat);

/// <summary>
/// The half of a Vulkan host that lets OpenGL code draw inside the frame — phase 4 of
/// <c>docs/design/HDR.md</c>.
///
/// <para>It is separate from <see cref="IVulkanSurfaceHost"/> because it is optional in a way
/// the swapchain is not. A host that cannot do it still renders; a video on such a host goes
/// the software route instead, which works and costs far more CPU. Nothing here is on
/// the path of a window that never shows video.</para>
///
/// <para><b>Why a Vulkan renderer needs this at all.</b> mpv renders with OpenGL and nothing
/// else — its render API defines exactly two types, <c>opengl</c> and <c>sw</c>. A Vulkan
/// window has no GL context, so one is made beside it, and the two APIs are given one image
/// to share rather than a copy each.</para>
/// </summary>
public interface IVulkanInteropHost
{
    /// <summary>
    /// Whether GL code can run against this host, doing whatever one-off setup that needs —
    /// creating the hidden context, checking it landed on the same GPU as the Vulkan device,
    /// checking the driver offers the external-memory extensions.
    ///
    /// <para>Deliberately not done at start-up. Loading a GL driver costs around 150 ms (see
    /// <c>docs/NATIVE-AOT.md</c>) and most windows never show a video; this runs on the
    /// first frame that actually wants it. Asked more than once, it answers from cache.</para>
    /// </summary>
    bool TryEnableInterop();

    /// <summary>Resolves GL entry points in the hidden context.</summary>
    Func<string, nint> GlGetProcAddress { get; }

    /// <summary>Runs <paramref name="draw"/> with the hidden GL context current.</summary>
    void RunOnGLContext(Action draw);

    /// <summary>
    /// <paramref name="owner"/>'s image, of this size, for GL to render into — creating or
    /// resizing it as needed — or null when interop is unavailable. Each owner (a GPU source:
    /// a video, a web page) has an image of its own, so several can draw in one frame. The host
    /// owns it; it stays valid until the owner's next call with a different size, until
    /// <see cref="ForgetInteropImage"/>, or until the host is disposed.
    /// </summary>
    VulkanInteropImage? LendInteropImage(object owner, int widthPx, int heightPx);

    /// <summary>
    /// <paramref name="owner"/> is done with its image. Any thread; the image is freed on the
    /// render thread at the next frame, once the GPU has finished with it.
    /// </summary>
    void ForgetInteropImage(object owner);

    /// <summary>
    /// Orders what GL just wrote before what Vulkan is about to read, and hands the images GL
    /// drew into back to the Vulkan queue. No-op when nothing was drawn.
    ///
    /// <para>With a shared semaphore, GL signals it and the Vulkan barrier waits on it on the
    /// GPU, so the CPU does not block. Without one it falls back to <c>glFinish</c> and a queue
    /// wait, which is correct and costs: 0.74 to 5.33 ms a frame under a 4K decode, and 156 ms of
    /// CPU a second against the semaphore's 62.</para>
    /// </summary>
    void FinishExternalFrame();
}

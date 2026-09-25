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
/// <param name="Index">Which image of the swapchain this is, so the renderer can keep one
/// wrapped surface per image rather than rebuilding every frame.</param>
/// <param name="Usage">The <c>VkImageUsageFlags</c> the swapchain was created with. Skia
/// validates the render target against them, so guessing here fails in a way that looks
/// like the driver refusing the image.</param>
public readonly record struct VulkanFrameTarget(
    ulong Image, uint Format, uint Layout, int WidthPx, int HeightPx, int Index, uint Usage);

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

    /// <summary>How many images the swapchain has, so the renderer can size its cache.</summary>
    int ImageCount { get; }

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
    /// Rebuilds the swapchain for a new size. The renderer drops every wrapped surface
    /// first, because they name images that are about to be destroyed.
    /// </summary>
    void RecreateSwapchain(int widthPx, int heightPx);
}

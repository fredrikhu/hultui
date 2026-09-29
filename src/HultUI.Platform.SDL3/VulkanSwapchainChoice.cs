using Vortice.Vulkan;

namespace HultUI.Platform.SDL3;

/// <summary>
/// The swapchain decisions that depend only on what the surface offers, kept apart from the
/// Vulkan calls that ask it, so they can be tested without a GPU. <see cref="SdlVulkanHost"/>
/// asks the surface and hands the answers here; the reasons behind each choice are documented
/// where the host makes it.
/// </summary>
internal static class VulkanSwapchainChoice
{
    /// <summary>What became of a request for the scRGB swapchain.</summary>
    internal enum WideColor
    {
        /// <summary>Not asked for.</summary>
        NotAsked,
        /// <summary>Asked for and chosen.</summary>
        Chosen,
        /// <summary>Asked for, but the instance lacks <c>VK_EXT_swapchain_colorspace</c>, so the
        /// surface's listing of it cannot be used even when it is there.</summary>
        NoExtension,
        /// <summary>Asked for, and the surface does not offer it.</summary>
        NotOffered,
    }

    /// <summary>
    /// The present mode: <paramref name="wanted"/> when the surface offers it, and otherwise
    /// MAILBOX, then IMMEDIATE, then FIFO, which every surface must support.
    /// </summary>
    /// <param name="wantedOffered">Whether <paramref name="wanted"/> was honoured, so the caller
    /// can say so rather than claim a mode it did not get.</param>
    internal static VkPresentModeKHR PresentMode(ReadOnlySpan<VkPresentModeKHR> offered,
        VkPresentModeKHR? wanted, out bool wantedOffered)
    {
        wantedOffered = wanted is { } mode && Offers(offered, mode);
        if (wantedOffered) return wanted!.Value;
        if (Offers(offered, VkPresentModeKHR.Mailbox)) return VkPresentModeKHR.Mailbox;
        if (Offers(offered, VkPresentModeKHR.Immediate)) return VkPresentModeKHR.Immediate;
        return VkPresentModeKHR.Fifo;
    }

    /// <summary>
    /// The swapchain format: scRGB half-float when asked for and allowed, otherwise the first
    /// plain 8-bit BGRA or RGBA in sRGB-nonlinear, otherwise whatever the surface lists first.
    /// The <c>_SRGB</c> formats are never preferred.
    /// </summary>
    internal static VkSurfaceFormatKHR SurfaceFormat(ReadOnlySpan<VkSurfaceFormatKHR> offered,
        bool wantsWideColor, bool colorSpaceExtensionEnabled, out WideColor wide)
    {
        wide = WideColor.NotAsked;
        if (offered.IsEmpty)
            return new VkSurfaceFormatKHR { format = VkFormat.B8G8R8A8Unorm, colorSpace = VkColorSpaceKHR.SrgbNonLinear };

        if (wantsWideColor && !colorSpaceExtensionEnabled)
        {
            wide = WideColor.NoExtension;
        }
        else if (wantsWideColor)
        {
            foreach (var candidate in offered)
            {
                if (candidate.format == VkFormat.R16G16B16A16Sfloat
                    && candidate.colorSpace == VkColorSpaceKHR.ExtendedSrgbLinearEXT)
                {
                    wide = WideColor.Chosen;
                    return candidate;
                }
            }
            wide = WideColor.NotOffered;
        }

        foreach (var candidate in offered)
        {
            if (candidate.colorSpace == VkColorSpaceKHR.SrgbNonLinear
                && candidate.format is VkFormat.B8G8R8A8Unorm or VkFormat.R8G8B8A8Unorm)
                return candidate;
        }
        return offered[0];
    }

    /// <summary>
    /// Whether a resize can keep the swapchain it has and present a smaller region of it. Only
    /// with a fixed extent available: without ONE_TO_ONE the presented extent must equal the
    /// window's, and keeping a larger swapchain gets it scaled, which is the resize artefact.
    /// The caller also checks that the format would not change.
    /// </summary>
    /// <param name="force">The driver said the swapchain is out of date, which no fit overrides.</param>
    internal static bool CanKeepSwapchain(bool force, bool fixedExtentAvailable, bool haveSwapchain,
        int widthPx, int heightPx, int allocatedWidthPx, int allocatedHeightPx) =>
        !force && fixedExtentAvailable && haveSwapchain
        && widthPx > 0 && heightPx > 0
        && widthPx <= allocatedWidthPx && heightPx <= allocatedHeightPx;

    private static bool Offers(ReadOnlySpan<VkPresentModeKHR> offered, VkPresentModeKHR mode)
    {
        foreach (var candidate in offered)
            if (candidate == mode) return true;
        return false;
    }
}

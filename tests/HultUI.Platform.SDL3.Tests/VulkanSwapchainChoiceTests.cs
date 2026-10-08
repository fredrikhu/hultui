using HultUI.Platform.SDL3;
using Vortice.Vulkan;
using Xunit;
using static HultUI.Platform.SDL3.VulkanSwapchainChoice;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>
/// The Vulkan swapchain's choices over what a surface offers. The lists are shaped like real
/// ones: the Windows NVIDIA surface from the renderer's start-up line, and X11, which offers
/// only sRGB-nonlinear.
/// </summary>
public class VulkanSwapchainChoiceTests
{
    private static VkSurfaceFormatKHR F(VkFormat format, VkColorSpaceKHR space) => new() { format = format, colorSpace = space };

    private static readonly VkSurfaceFormatKHR[] WindowsNvidia =
    [
        F(VkFormat.B8G8R8A8Unorm, VkColorSpaceKHR.SrgbNonLinear),
        F(VkFormat.B8G8R8A8Srgb, VkColorSpaceKHR.SrgbNonLinear),
        F(VkFormat.R8G8B8A8Unorm, VkColorSpaceKHR.SrgbNonLinear),
        F(VkFormat.R8G8B8A8Srgb, VkColorSpaceKHR.SrgbNonLinear),
        F(VkFormat.R16G16B16A16Sfloat, VkColorSpaceKHR.ExtendedSrgbLinearEXT),
        F(VkFormat.A2B10G10R10UnormPack32, VkColorSpaceKHR.Hdr10St2084EXT),
        F(VkFormat.A2B10G10R10UnormPack32, VkColorSpaceKHR.SrgbNonLinear),
    ];

    private static readonly VkSurfaceFormatKHR[] X11 =
    [
        F(VkFormat.B8G8R8A8Srgb, VkColorSpaceKHR.SrgbNonLinear),
        F(VkFormat.B8G8R8A8Unorm, VkColorSpaceKHR.SrgbNonLinear),
    ];

    [Fact]
    public void WideColour_IsTheHalfFloatScRgbFormat_WhenAskedAndAllowed()
    {
        var chosen = SurfaceFormat(WindowsNvidia, wantsWideColor: true, colorSpaceExtensionEnabled: true, out var wide);
        Assert.Equal(VkFormat.R16G16B16A16Sfloat, chosen.format);
        Assert.Equal(VkColorSpaceKHR.ExtendedSrgbLinearEXT, chosen.colorSpace);
        Assert.Equal(WideColor.Chosen, wide);
    }

    [Fact]
    public void WideColour_IsRefusedWithoutTheColourSpaceExtension_EvenWhenListed()
    {
        // Drivers list EXTENDED_SRGB_LINEAR whether or not the instance enabled
        // VK_EXT_swapchain_colorspace; using it without the extension is invalid.
        var chosen = SurfaceFormat(WindowsNvidia, wantsWideColor: true, colorSpaceExtensionEnabled: false, out var wide);
        Assert.Equal(VkFormat.B8G8R8A8Unorm, chosen.format);
        Assert.Equal(VkColorSpaceKHR.SrgbNonLinear, chosen.colorSpace);
        Assert.Equal(WideColor.NoExtension, wide);
    }

    [Fact]
    public void WideColour_FallsBackTo8Bit_WhereTheSurfaceHasNone()
    {
        var chosen = SurfaceFormat(X11, wantsWideColor: true, colorSpaceExtensionEnabled: true, out var wide);
        Assert.Equal(VkFormat.B8G8R8A8Unorm, chosen.format);
        Assert.Equal(WideColor.NotOffered, wide);
    }

    [Fact]
    public void The8BitChoice_SkipsTheSrgbFormats_WhicheverComesFirst()
    {
        // X11 lists B8G8R8A8_SRGB first. Taking it would have the hardware encode, which is a
        // different answer from the GL path's.
        var chosen = SurfaceFormat(X11, wantsWideColor: false, colorSpaceExtensionEnabled: true, out var wide);
        Assert.Equal(VkFormat.B8G8R8A8Unorm, chosen.format);
        Assert.Equal(WideColor.NotAsked, wide);
    }

    [Fact]
    public void ASurfaceWithNoPlain8BitFormat_GetsItsFirst()
    {
        VkSurfaceFormatKHR[] odd = [F(VkFormat.A2B10G10R10UnormPack32, VkColorSpaceKHR.SrgbNonLinear)];
        Assert.Equal(VkFormat.A2B10G10R10UnormPack32, SurfaceFormat(odd, false, true, out _).format);
        Assert.Equal(VkFormat.B8G8R8A8Unorm, SurfaceFormat([], false, true, out _).format);
    }

    [Fact]
    public void ThePresentMode_PrefersMailbox_ThenImmediate_ThenFifo()
    {
        // MAILBOX over IMMEDIATE: IMMEDIATE let the compositor stretch the old image into a
        // resized window, on 10 of 40 resizes.
        Assert.Equal(VkPresentModeKHR.Mailbox, PresentMode(
            [VkPresentModeKHR.Fifo, VkPresentModeKHR.Immediate, VkPresentModeKHR.Mailbox], null, out _));
        Assert.Equal(VkPresentModeKHR.Immediate, PresentMode(
            [VkPresentModeKHR.Fifo, VkPresentModeKHR.Immediate], null, out _));
        Assert.Equal(VkPresentModeKHR.Fifo, PresentMode([VkPresentModeKHR.Fifo], null, out _));
        Assert.Equal(VkPresentModeKHR.Fifo, PresentMode([], null, out _));
    }

    [Fact]
    public void AnOverride_CountsOnlyWhenTheSurfaceOffersIt()
    {
        Assert.Equal(VkPresentModeKHR.Fifo, PresentMode(
            [VkPresentModeKHR.Fifo, VkPresentModeKHR.Mailbox], VkPresentModeKHR.Fifo, out bool forced));
        Assert.True(forced);

        Assert.Equal(VkPresentModeKHR.Mailbox, PresentMode(
            [VkPresentModeKHR.Fifo, VkPresentModeKHR.Mailbox], VkPresentModeKHR.FifoRelaxed, out forced));
        Assert.False(forced);
    }

    [Fact]
    public void AResize_KeepsTheSwapchain_OnlyWhenItFitsAndIsShownUnscaled()
    {
        // Allocated 2048x1280 for a window of 1650x1050.
        Assert.True(CanKeepSwapchain(false, fixedExtentAvailable: true, haveSwapchain: true, 1600, 1000, 2048, 1280));
        Assert.True(CanKeepSwapchain(false, true, true, 2048, 1280, 2048, 1280));
        Assert.False(CanKeepSwapchain(false, true, true, 2049, 1000, 2048, 1280));   // grew past it
        Assert.False(CanKeepSwapchain(false, true, true, 1600, 1281, 2048, 1280));
    }

    [Fact]
    public void WithoutAFixedExtent_EvenAShrinkRebuilds()
    {
        // The HULTUI_VULKAN_FIXED_EXTENT=0 regression: skipping the rebuild on a shrink without
        // ONE_TO_ONE leaves a larger swapchain being scaled into the window, which is the very
        // artefact the fixed extent exists to remove.
        Assert.False(CanKeepSwapchain(false, fixedExtentAvailable: false, haveSwapchain: true, 800, 600, 2048, 1280));
    }

    [Fact]
    public void OutOfDate_NoSwapchain_OrAnEmptyWindow_AlwaysRebuild()
    {
        Assert.False(CanKeepSwapchain(force: true, true, true, 800, 600, 2048, 1280));
        Assert.False(CanKeepSwapchain(false, true, haveSwapchain: false, 800, 600, 2048, 1280));
        Assert.False(CanKeepSwapchain(false, true, true, 0, 600, 2048, 1280));
        Assert.False(CanKeepSwapchain(false, true, true, 800, 0, 2048, 1280));
    }

    [Fact]
    public void AnScRgbMailboxSwapchainOnWindows_IsDestroyedBeforeItsReplacement()
    {
        // Handed over as oldSwapchain, the next present waited about 100 ms on every resize,
        // and the compositor showed the old image scaled meanwhile.
        Assert.True(DestroyBeforeReplacing(isWindows: true, VkFormat.R16G16B16A16Sfloat, VkPresentModeKHR.Mailbox));
    }

    [Fact]
    public void EveryOtherSwapchain_IsStillHandedOver()
    {
        // IMMEDIATE and FIFO never stalled, and destroying first made their resizes slower. The
        // 8-bit swapchain never stalled either. Linux was never measured.
        Assert.False(DestroyBeforeReplacing(isWindows: true, VkFormat.R16G16B16A16Sfloat, VkPresentModeKHR.Immediate));
        Assert.False(DestroyBeforeReplacing(isWindows: true, VkFormat.R16G16B16A16Sfloat, VkPresentModeKHR.Fifo));
        Assert.False(DestroyBeforeReplacing(isWindows: true, VkFormat.R16G16B16A16Sfloat, VkPresentModeKHR.FifoRelaxed));
        Assert.False(DestroyBeforeReplacing(isWindows: true, VkFormat.B8G8R8A8Unorm, VkPresentModeKHR.Mailbox));
        Assert.False(DestroyBeforeReplacing(isWindows: true, VkFormat.R8G8B8A8Unorm, VkPresentModeKHR.Mailbox));
        Assert.False(DestroyBeforeReplacing(isWindows: false, VkFormat.R16G16B16A16Sfloat, VkPresentModeKHR.Mailbox));
    }
}

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
        Assert.True(CanKeepSwapchain(false, oneToOne: true, haveSwapchain: true, 1600, 1000, 2048, 1280));
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
        Assert.False(CanKeepSwapchain(false, oneToOne: false, haveSwapchain: true, 800, 600, 2048, 1280));
    }

    [Fact]
    public void OnlyMailbox_ShowsASwapchainOneToOne()
    {
        // Under FIFO a one-to-one swapchain stalled for good a few drags in; under IMMEDIATE the
        // first one of a drag cost 172-196 ms and the old image showed stretched (NVIDIA 610.62).
        Assert.True(PresentModeAllowsOneToOne(VkPresentModeKHR.Mailbox));
        Assert.False(PresentModeAllowsOneToOne(VkPresentModeKHR.Immediate));
        Assert.False(PresentModeAllowsOneToOne(VkPresentModeKHR.Fifo));
        Assert.False(PresentModeAllowsOneToOne(VkPresentModeKHR.FifoRelaxed));
    }

    [Fact]
    public void ASuboptimalPresent_KeepsAOneToOneSwapchain_WhileTheWindowFits()
    {
        // A swapchain larger than its window is SUBOPTIMAL on every present (NVIDIA 610). Rebuilt
        // for it, HULTUI_VULKAN_FIXED_EXTENT=1 built the swapchain again every frame of a drag.
        Assert.True(KeepSuboptimal(builtOneToOne: true, overAllocating: true, 1600, 1000, 2048, 1280));
        Assert.True(KeepSuboptimal(true, true, 2048, 1280, 2048, 1280));
        // Grown past it: the images are too small.
        Assert.False(KeepSuboptimal(true, true, 2049, 1000, 2048, 1280));
        Assert.False(KeepSuboptimal(true, true, 1600, 1281, 2048, 1280));
        // An exact swapchain's SUBOPTIMAL means what it always meant: rebuild.
        Assert.False(KeepSuboptimal(builtOneToOne: false, overAllocating: true, 1600, 1000, 2048, 1280));
        // Built one-to-one, but no longer wanted (a drag that ended): rebuild at the exact size.
        Assert.False(KeepSuboptimal(builtOneToOne: true, overAllocating: false, 1600, 1000, 2048, 1280));
        Assert.False(KeepSuboptimal(true, true, 0, 1000, 2048, 1280));
    }

    [Fact]
    public void ByDefault_OnlyASizingLoopOverAllocates()
    {
        // An edge drag, or a loop nobody said was a move: over-allocate from its first size change.
        Assert.True(OverAllocate(available: true, fixedExtentOverride: null, SizeMoveLoop.Sizing));
        // A title-bar move runs the same loop; a size change inside it (another monitor's scale,
        // or out of the maximized state) rebuilds exact, once, as before.
        Assert.False(OverAllocate(true, null, SizeMoveLoop.Moving));
        // Snap, maximize, a program's SetWindowPos: no loop, exact.
        Assert.False(OverAllocate(true, null, SizeMoveLoop.None));
    }

    [Fact]
    public void TheEndOfALoop_RebuildsExact_OnlyWhatTheDragOverAllocated()
    {
        Assert.True(RebuildWhenSizeMoveEnds(haveSwapchain: true, builtOneToOne: true, overAllocatingAfter: false));
        // A move, or a drag that changed no size: the exact swapchain stays.
        Assert.False(RebuildWhenSizeMoveEnds(haveSwapchain: true, builtOneToOne: false, overAllocatingAfter: false));
        // HULTUI_VULKAN_FIXED_EXTENT=1: over-allocated for good.
        Assert.False(RebuildWhenSizeMoveEnds(haveSwapchain: true, builtOneToOne: true, overAllocatingAfter: true));
        // Minimized: nothing to rebuild; the restore builds one.
        Assert.False(RebuildWhenSizeMoveEnds(haveSwapchain: false, builtOneToOne: true, overAllocatingAfter: false));
    }

    [Fact]
    public void ADragsSuboptimal_KeepsTheSwapchain_AndOnceItEndsNoLonger()
    {
        // Built one-to-one when the drag began; the window then moves around inside the images.
        bool during = OverAllocate(true, null, SizeMoveLoop.Sizing), after = OverAllocate(true, null, SizeMoveLoop.None);
        Assert.True(KeepSuboptimal(builtOneToOne: true, during, 1600, 1000, 2048, 1280));
        Assert.True(CanKeepSwapchain(false, oneToOne: during, true, 1700, 1100, 2048, 1280));
        // Once it ended, the same swapchain is rebuilt by either path rather than kept.
        Assert.False(KeepSuboptimal(builtOneToOne: true, after, 1600, 1000, 2048, 1280));
        Assert.False(CanKeepSwapchain(false, oneToOne: after, true, 1700, 1100, 2048, 1280));
    }

    [Fact]
    public void AKeptResize_AsksTheSurfaceForFormats_OnlyWhenTheAnswerMayHaveChanged()
    {
        // Chosen for the wish that holds now: not asked again (1.37 ms a kept step of a drag).
        Assert.True(FormatStillChosen(formatChosenForWide: true, wantsWideColor: true));
        Assert.True(FormatStillChosen(formatChosenForWide: false, wantsWideColor: false));
        // The wish changed (HDR switched on or off), or the display did (forgotten): asked.
        Assert.False(FormatStillChosen(formatChosenForWide: true, wantsWideColor: false));
        Assert.False(FormatStillChosen(formatChosenForWide: false, wantsWideColor: true));
        Assert.False(FormatStillChosen(formatChosenForWide: null, wantsWideColor: true));
        Assert.False(FormatStillChosen(formatChosenForWide: null, wantsWideColor: false));
    }

    [Fact]
    public void TheFixedExtentOverride_DecidesAlone_AndOnlyWhereOneToOneIsOffered()
    {
        foreach (var loop in new[] { SizeMoveLoop.None, SizeMoveLoop.Moving, SizeMoveLoop.Sizing })
        {
            Assert.True(OverAllocate(available: true, fixedExtentOverride: true, loop));
            Assert.False(OverAllocate(available: true, fixedExtentOverride: false, loop));
            Assert.False(OverAllocate(available: false, fixedExtentOverride: true, loop));
            Assert.False(OverAllocate(available: false, fixedExtentOverride: null, loop));
        }
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

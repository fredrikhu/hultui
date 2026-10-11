using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// The bookkeeping of the Vulkan surface's kept render targets, one per swapchain image. A
/// target kept for an image the swapchain no longer has, or at a size or colour space the frame
/// no longer uses, draws somewhere wrong; one never disposed leaks an image view and a
/// framebuffer. The GPU half is <c>VulkanProbeTests.EveryCapture_OfAReusedImage_ShowsTheFrameJustDrawn</c>.
/// </summary>
public class SwapchainTargetsTests
{
    private sealed class Target : IDisposable
    {
        public int Disposed;
        public void Dispose() => Disposed++;
    }

    private static SwapchainTargetKey Key(ulong image = 0x10, int width = 1800, int height = 1170,
        uint format = 97, uint usage = 0x17, uint colorSpace = 1000104002,
        ScRgbFramePath path = ScRgbFramePath.Offscreen, float whiteScale = 0f) =>
        new(image, width, height, format, usage, colorSpace, path, whiteScale);

    [Fact]
    public void TheSameImage_ComesBackAsTheSameTarget_FrameAfterFrame()
    {
        var targets = new SwapchainTargets<Target>();
        var first = new Target();
        targets.Put(1, Key(), first);

        for (int frame = 0; frame < 10; frame++)
        {
            Assert.True(targets.TryGet(1, Key(), out var kept));
            Assert.Same(first, kept);
        }
        Assert.Equal(0, first.Disposed);
        Assert.Equal(1, targets.Count);
    }

    [Fact]
    public void EachIndex_KeepsItsOwn()
    {
        var targets = new SwapchainTargets<Target>();
        var made = new[] { new Target(), new Target(), new Target() };
        for (int i = 0; i < made.Length; i++) targets.Put(i, Key(image: (ulong)(0x10 + i)), made[i]);

        for (int i = 0; i < made.Length; i++)
        {
            Assert.True(targets.TryGet(i, Key(image: (ulong)(0x10 + i)), out var kept));
            Assert.Same(made[i], kept);
        }
        Assert.False(targets.TryGet(3, Key(image: 0x13), out _));
        Assert.Equal(3, targets.Count);
    }

    [Theory]
    [InlineData("image")]
    [InlineData("size")]
    [InlineData("format")]
    [InlineData("colour space")]
    [InlineData("path")]
    [InlineData("white scale")]
    public void AFrameForSomethingElse_MissesItsSlot_AndTheNewTargetReplacesTheOld(string what)
    {
        var targets = new SwapchainTargets<Target>();
        var old = new Target();
        var key = Key(path: ScRgbFramePath.ScaledSpace, whiteScale: 3f);
        targets.Put(0, key, old);

        var other = what switch
        {
            // The same index after a rebuild is another image.
            "image" => key with { Image = 0x99 },
            // An over-allocated swapchain draws a resized window into the same image.
            "size" => key with { Width = 1500 },
            "format" => key with { Format = 44 },
            "colour space" => key with { ColorSpace = 0 },
            "path" => key with { Path = ScRgbFramePath.Offscreen, WhiteScale = 0f },
            // The scaled colour space carries the white scale: the brightness slider moved.
            "white scale" => key with { WhiteScale = 2.5f },
            _ => throw new ArgumentOutOfRangeException(nameof(what)),
        };
        Assert.False(targets.TryGet(0, other, out _));

        var replacement = new Target();
        targets.Put(0, other, replacement);
        Assert.Equal(1, old.Disposed);
        Assert.True(targets.TryGet(0, other, out var kept));
        Assert.Same(replacement, kept);
        Assert.Equal(1, targets.Count);
    }

    [Fact]
    public void AHostThatCannotRestoreTheLayout_GetsATargetEveryFrame()
    {
        Assert.Equal(SwapchainTargetUse.ThisFrameOnly, SwapchainTargetUses.Choose(hostKeeps: false, throughOffscreen: true, gpuSourceLastFrame: false));
        Assert.Equal(SwapchainTargetUse.ThisFrameOnly, SwapchainTargetUses.Choose(hostKeeps: false, throughOffscreen: false, gpuSourceLastFrame: false));
    }

    [Fact]
    public void StraightOntoTheSwapchain_TheBarrierComesAtOnce()
    {
        // Skia may submit its work on the image at any point after the first draw.
        Assert.Equal(SwapchainTargetUse.KeptTransitionNow, SwapchainTargetUses.Choose(hostKeeps: true, throughOffscreen: false, gpuSourceLastFrame: false));
    }

    [Fact]
    public void ThroughTheOffscreen_TheBarrierWaitsForTheBlit_VideoOrNot()
    {
        // After mpv's GL draw: a submit before it made that draw slower.
        Assert.Equal(SwapchainTargetUse.KeptTransitionBeforeBlit, SwapchainTargetUses.Choose(hostKeeps: true, throughOffscreen: true, gpuSourceLastFrame: false));
        Assert.Equal(SwapchainTargetUse.KeptTransitionBeforeBlit, SwapchainTargetUses.Choose(hostKeeps: true, throughOffscreen: true, gpuSourceLastFrame: true));
    }

    [Fact]
    public void StraightOntoTheSwapchain_AfterAVideoFrame_TheFrameWrapsItsOwnTarget()
    {
        // No barrier to submit before the GL draw, at the old 2 KB a frame.
        Assert.Equal(SwapchainTargetUse.ThisFrameOnly, SwapchainTargetUses.Choose(hostKeeps: true, throughOffscreen: false, gpuSourceLastFrame: true));
    }

    [Fact]
    public void Clear_DisposesEveryTarget_OnceEach_AndForgetsThem()
    {
        // What the surface does before the host destroys the images: a rebuild, a minimize, the
        // window closing.
        var targets = new SwapchainTargets<Target>();
        var made = new[] { new Target(), new Target(), new Target() };
        for (int i = 0; i < made.Length; i++) targets.Put(i, Key(image: (ulong)i), made[i]);

        targets.Clear();
        targets.Clear();

        Assert.All(made, t => Assert.Equal(1, t.Disposed));
        Assert.Equal(0, targets.Count);
        for (int i = 0; i < made.Length; i++) Assert.False(targets.TryGet(i, Key(image: (ulong)i), out _));
    }
}

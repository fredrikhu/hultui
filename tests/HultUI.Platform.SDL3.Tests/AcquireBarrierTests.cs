using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>
/// When the Vulkan host submits the barrier that puts an acquired image back in the layout Skia's
/// kept render target believes: once per acquire, and as late as allowed — owed until just before
/// the renderer's first submit, or the present. Submitted early, before a video's GL draw, it made
/// that draw slower; never submitted, Skia draws into an image in the wrong layout, which NVIDIA
/// forgives and the validation layer does not (<c>VUID-vkCmdDraw-None-09600</c>). The GPU half is
/// <c>VulkanProbeTests.ASteadyVideoFrame_AllocatesAlmostNothing_AndThePictureKeepsChanging</c>.
/// </summary>
public class AcquireBarrierTests
{
    [Fact]
    public void Asked_WhereTheRenderersSubmitsPassTheHost_ItIsOwed_AndGoesBeforeTheFirstSubmitOnly()
    {
        var barrier = new AcquireBarrier();
        barrier.Reset();

        Assert.False(barrier.Ask(canDefer: true));   // not now: before any GL draw of the frame
        Assert.True(barrier.Owed);

        Assert.True(barrier.TakeOwed());             // Skia's first submit
        Assert.False(barrier.TakeOwed());            // its second
        Assert.False(barrier.TakeOwed());            // the present
        Assert.True(barrier.Submitted);
    }

    [Fact]
    public void Owed_AndNeverSubmittedForByTheRenderer_ItGoesAtThePresent()
    {
        var barrier = new AcquireBarrier();
        Assert.False(barrier.Ask(canDefer: true));

        // No submit came; the present takes it, before the capture and the present transition.
        Assert.True(barrier.TakeOwed());
        Assert.False(barrier.Owed);
    }

    [Fact]
    public void Asked_WhereTheHostCannotDefer_ItGoesAtOnce()
    {
        var barrier = new AcquireBarrier();
        Assert.True(barrier.Ask(canDefer: false));
        Assert.False(barrier.Owed);
        Assert.False(barrier.TakeOwed());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AskedTwiceForOneImage_ItGoesOnce(bool canDefer)
    {
        var barrier = new AcquireBarrier();
        int submitted = 0;
        if (barrier.Ask(canDefer)) submitted++;
        if (barrier.Ask(canDefer)) submitted++;
        if (barrier.TakeOwed()) submitted++;
        if (barrier.Ask(canDefer)) submitted++;
        if (barrier.TakeOwed()) submitted++;
        Assert.Equal(1, submitted);
    }

    [Fact]
    public void NotAskedFor_NothingIsOwed()
    {
        // A frame through the offscreen asks only before the blit; Skia's submits before that
        // (the offscreen's own flush) must not take a barrier nobody asked for.
        var barrier = new AcquireBarrier();
        barrier.Reset();
        Assert.False(barrier.TakeOwed());
        Assert.False(barrier.Submitted);
    }

    [Fact]
    public void ANewImage_OrARebuild_StartsAgain()
    {
        var barrier = new AcquireBarrier();
        Assert.False(barrier.Ask(canDefer: true));
        Assert.True(barrier.TakeOwed());

        barrier.Reset();   // the next acquire
        Assert.False(barrier.Submitted);
        Assert.False(barrier.Ask(canDefer: true));
        Assert.True(barrier.Owed);

        barrier.Reset();   // the swapchain went before the renderer submitted: nothing to pay for
        Assert.False(barrier.TakeOwed());
    }
}

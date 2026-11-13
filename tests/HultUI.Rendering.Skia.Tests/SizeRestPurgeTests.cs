using System.Diagnostics;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// When the Vulkan surface frees what Skia kept for the sizes a window no longer has: a stencil a
/// size, shared by a key no scratch purge lets go (Aperture: 9 after 18 size changes, 21.8 MB). The
/// real-window half is <c>VulkanProbeTests.AfterSizeChangesRest_TheWindowKeepsOneStencil</c>.
/// </summary>
public class SizeRestPurgeTests
{
    private static readonly long Second = Stopwatch.Frequency;
    private static readonly long Ms = Stopwatch.Frequency / 1000;

    [Fact]
    public void NothingLetGo_NothingIsOwed()
    {
        var purge = new SizeRestPurge();
        purge.Changed(10 * Second);   // a size the swapchain kept (a drag's over-allocated step)
        purge.Presenting(10 * Second + 5 * Ms);
        Assert.False(purge.Owed);
        Assert.Equal(long.MaxValue, purge.DueAt);
        Assert.False(purge.TakeDue(100 * Second, out _));
    }

    [Fact]
    public void ARelease_IsDueASecondOn_OnlyOnceAFrameWasPresentedAfterIt()
    {
        var purge = new SizeRestPurge();
        purge.Released(10 * Second);
        Assert.True(purge.Owed);
        // No frame since (a minimized window): what is on screen has not been drawn with again.
        Assert.Equal(long.MaxValue, purge.DueAt);
        Assert.False(purge.TakeDue(20 * Second, out _));

        purge.Presenting(10 * Second + 16 * Ms);
        Assert.Equal(11 * Second, purge.DueAt);
        Assert.False(purge.TakeDue(11 * Second - 1, out _));
        Assert.True(purge.TakeDue(11 * Second, out long msNotUsed));
        // Up to the release: what the frames since drew with stays.
        Assert.Equal(1000, msNotUsed);

        // Once.
        Assert.False(purge.Owed);
        Assert.False(purge.TakeDue(30 * Second, out _));
    }

    [Fact]
    public void EverySizeChangePutsItOff_AsADragsStepsDo()
    {
        var purge = new SizeRestPurge();
        purge.Released(10 * Second);
        for (int step = 1; step <= 40; step++)
        {
            purge.Changed(10 * Second + step * 20 * Ms);
            purge.Presenting(10 * Second + step * 20 * Ms + 5 * Ms);
            Assert.False(purge.TakeDue(10 * Second + step * 20 * Ms + 10 * Ms, out _));
        }
        Assert.Equal(10 * Second + 800 * Ms + Second, purge.DueAt);
    }

    [Fact]
    public void AReleaseAfterTheLastFrame_WaitsForTheNextFrame()
    {
        // The half-float offscreen goes at the purge after a frame: that frame drew before it went.
        var purge = new SizeRestPurge();
        purge.Released(10 * Second);
        purge.Presenting(10 * Second + 10 * Ms);
        purge.Released(10 * Second + 12 * Ms);
        Assert.Equal(long.MaxValue, purge.DueAt);
        purge.Presenting(10 * Second + 30 * Ms);
        Assert.True(purge.TakeDue(12 * Second, out long msNotUsed));
        Assert.Equal(1988, msNotUsed);
    }

    [Fact]
    public void ThePurgedUpToTime_IsJustAfterTheRelease()
    {
        // Rounded down to whole milliseconds: at the release or less than 1 ms after it, so the
        // stencils let go with the old targets go.
        var purge = new SizeRestPurge();
        purge.Released(10 * Second + Ms / 2);
        purge.Presenting(10 * Second + 30 * Ms);
        long now = 12 * Second;
        Assert.True(purge.TakeDue(now, out long msNotUsed));
        long purgedUpTo = now - msNotUsed * Ms;
        Assert.InRange(purgedUpTo, 10 * Second + Ms / 2, 10 * Second + Ms + Ms / 2 - 1);
    }

    [Fact]
    public void ThePurgedUpToTime_IsNeverAfterTheLastFrame()
    {
        // A frame begun within a millisecond of the release: what it drew with stays, whatever
        // became of the old stencil.
        var purge = new SizeRestPurge();
        purge.Released(10 * Second);
        purge.Presenting(10 * Second + Ms / 4);
        long now = 12 * Second + Ms / 2;
        Assert.True(purge.TakeDue(now, out long msNotUsed));
        Assert.True(now - msNotUsed * Ms <= 10 * Second + Ms / 4);
    }
}

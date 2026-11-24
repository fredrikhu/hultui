using System.Diagnostics;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// When the Vulkan surface frees what its last frame did not draw with: once a rest, never while
/// frames keep coming, and never anything that frame drew with. The real-window half is
/// <c>VulkanProbeTests.AfterARest_TheWindowPurgesOnce_AndDrawsTheSameFrame</c>.
/// </summary>
public class RestPurgeTests
{
    private static readonly long Second = Stopwatch.Frequency;
    private static readonly long Ms = Stopwatch.Frequency / 1000;

    [Fact]
    public void BeforeTheFirstFrame_NothingIsOwed()
    {
        var purge = new RestPurge(TimeSpan.FromSeconds(30));
        Assert.Equal(long.MaxValue, purge.DueAt);
        Assert.False(purge.TakeDue(100 * Second, out _));
    }

    [Fact]
    public void AFrame_IsPurgedAfterARest_Once()
    {
        var purge = new RestPurge(TimeSpan.FromSeconds(30));
        purge.Presenting(10 * Second);
        Assert.Equal(40 * Second, purge.DueAt);
        Assert.False(purge.TakeDue(40 * Second - 1, out _));
        Assert.True(purge.TakeDue(40 * Second, out long msNotUsed));
        Assert.Equal(30_000, msNotUsed);

        // Once: the next is owed only after the next frame.
        Assert.Equal(long.MaxValue, purge.DueAt);
        Assert.False(purge.TakeDue(100 * Second, out _));
        purge.Presenting(100 * Second);
        Assert.Equal(130 * Second, purge.DueAt);
    }

    [Fact]
    public void FramesThatKeepComing_PutItOffEveryTime()
    {
        // A video at 24 frames a second for two minutes.
        var purge = new RestPurge(TimeSpan.FromSeconds(30));
        long frame = Second / 24;
        for (long at = 10 * Second; at < 130 * Second; at += frame)
        {
            Assert.False(purge.TakeDue(at, out _));
            purge.Presenting(at);
            Assert.Equal(at + 30 * Second, purge.DueAt);
        }
    }

    [Fact]
    public void ThePurgedUpToTime_IsNeverAfterTheLastFrameBegan()
    {
        // Rounded up to whole milliseconds, wherever in its millisecond the frame began.
        var purge = new RestPurge(TimeSpan.FromSeconds(10));
        for (long part = 0; part < 4; part++)
        {
            long began = 10 * Second + part * Ms / 4 + 1;
            purge.Presenting(began);
            long now = began + 10 * Second + Ms / 3;
            Assert.True(purge.TakeDue(now, out long msNotUsed));
            Assert.InRange(now - msNotUsed * Ms, began - Ms, began);
        }
    }
}

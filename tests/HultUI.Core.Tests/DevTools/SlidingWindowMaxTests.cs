using System;
using System.Diagnostics;
using HultUI.Core.DevTools;
using Xunit;

namespace HultUI.Core.Tests.DevTools;

public class SlidingWindowMaxTests
{
    private static readonly long TicksPerSecond = Stopwatch.Frequency;

    [Fact]
    public void Max_IsZero_WhenEmpty()
    {
        var w = new SlidingWindowMax(TimeSpan.FromSeconds(5));
        Assert.Equal(0f, w.Max);
    }

    [Fact]
    public void Max_TracksLargestSampleWithinWindow()
    {
        var w = new SlidingWindowMax(TimeSpan.FromSeconds(5));
        long t = 1000 * TicksPerSecond;

        w.Add(3f, t);
        w.Add(7f, t);
        w.Add(2f, t);

        Assert.Equal(7f, w.Max);
    }

    [Fact]
    public void Max_ExpiresSamplesOlderThanWindow()
    {
        var w = new SlidingWindowMax(TimeSpan.FromSeconds(5));
        long t0 = 1000 * TicksPerSecond;

        // A tall spike at t0…
        w.Add(50f, t0);
        Assert.Equal(50f, w.Max);

        // …still counts at 4s.
        w.Add(5f, t0 + 4 * TicksPerSecond);
        Assert.Equal(50f, w.Max);

        // …but is gone once we're past the 5s window.
        w.Add(5f, t0 + 6 * TicksPerSecond);
        Assert.Equal(5f, w.Max);
    }

    [Fact]
    public void Max_DecaysToMostRecentSample_AfterSpikeExpires()
    {
        var w = new SlidingWindowMax(TimeSpan.FromSeconds(5));
        long t0 = 0;

        w.Add(100f, t0);
        w.Add(10f, t0 + 3 * TicksPerSecond);
        w.Add(20f, t0 + 4 * TicksPerSecond);

        // 100 is still the peak while inside the window.
        Assert.Equal(100f, w.Max);

        // Once the spike ages out, the surviving samples (10, 20) win.
        w.Add(15f, t0 + 7 * TicksPerSecond);
        Assert.Equal(20f, w.Max);
    }

    [Fact]
    public void Max_HandlesMonotonicallyDecreasingSamples()
    {
        var w = new SlidingWindowMax(TimeSpan.FromSeconds(5));
        long t0 = 0;

        w.Add(9f, t0);
        w.Add(8f, t0 + 1 * TicksPerSecond);
        w.Add(7f, t0 + 2 * TicksPerSecond);

        Assert.Equal(9f, w.Max);

        // Expire the leading 9; the next-largest surviving sample is 8.
        w.Add(1f, t0 + 6 * TicksPerSecond);
        Assert.Equal(8f, w.Max);
    }
}

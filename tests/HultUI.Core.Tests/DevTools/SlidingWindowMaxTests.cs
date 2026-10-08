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

    /// <summary>The deque is a ring over an array that starts with room for 16 samples. A
    /// falling run longer than that makes it grow while its head is part-way round, and samples
    /// age out from the middle of the array: every Max must still be the brute-force answer.</summary>
    [Fact]
    public void Max_MatchesABruteForceWindow_WhileTheRingWrapsAndGrows()
    {
        var w = new SlidingWindowMax(TimeSpan.FromSeconds(1));
        long step = TicksPerSecond / 60;
        var rng = new Random(17);
        var history = new List<(long T, float V)>();

        for (int i = 0; i < 2_000; i++)
        {
            // Long falling runs (up to 90 samples, more than one window at 60 Hz) between
            // random jumps, so the deque fills past its first array and drains again.
            float v = i % 90 < 70 ? 1_000f - i % 90 : rng.Next(0, 1_200);
            long t = i * step;
            w.Add(v, t);
            history.Add((t, v));

            long cutoff = t - TicksPerSecond;
            float expected = history.Where(h => h.T >= cutoff).Max(h => h.V);
            Assert.Equal(expected, w.Max);
        }
    }

    /// <summary>Every window feeds eight of these one sample a frame, HUD shown or not, so a
    /// sample must not cost an allocation. A LinkedList node each was ~0.4 KB a frame on frames
    /// that otherwise allocate nothing (plan 17, runtime-frame-floor).</summary>
    [Fact]
    public void Add_AllocatesNothing()
    {
        var w = new SlidingWindowMax(TimeSpan.FromSeconds(5));
        long step = TicksPerSecond / 60;
        long t = 0;
        for (int i = 0; i < 600; i++) w.Add(i % 7, t += step);   // ten seconds: warm and full

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++) w.Add(i % 7, t += step);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}

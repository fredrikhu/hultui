using HultUI.Core.DevTools;
using HultUI.Testing;
using Xunit;

namespace HultUI.Core.Tests.DevTools;

public class PerformanceCountersTests
{
    private static void Frame(PerformanceCounters perf)
    {
        perf.BeginFrame();
        perf.BeginStyle(); perf.EndStyle();
        perf.BeginLayout(); perf.EndLayout();
        perf.BeginRender(); perf.EndRender();
        perf.BeginOverlay(); perf.EndOverlay();
        perf.BeginFlush(); perf.EndFlush();
        perf.BeginPresent(); perf.EndPresent();
        perf.EndFrame();
    }

    /// <summary>Every window keeps these counters every frame, the HUD shown or not, so a frame
    /// allocates nothing in them. Eight peak trackers took a LinkedList node each per frame
    /// (~0.4 KB), and the frame-rate queue grew by doubling whenever the rate rose. Two thousand
    /// frames run far faster than any display, past the rate the counters keep a second of.</summary>
    [Fact]
    public void AFrame_AllocatesNothing()
    {
        var perf = new PerformanceCounters();
        for (int i = 0; i < 100; i++) Frame(perf);

        long allocated = Allocations.Least(() =>
        {
            for (int i = 0; i < 2_000; i++) Frame(perf);
        });

        Assert.Equal(0, allocated);
        Assert.True(perf.Fps > 0);
    }
}

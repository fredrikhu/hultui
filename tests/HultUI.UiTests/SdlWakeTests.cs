using System.Diagnostics;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// The SDL platform's idle wait must really park, and must really return when
/// <see cref="SdlWindowingPlatform.Wake"/> is called from another thread. The headless
/// suite proves the application wires wakes up; only a real SDL window can prove the
/// wait honours them, because SDL's cross-thread wakeup goes through the window.
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed class SdlWakeTests
{
    [UiFact]
    public void Wake_FromAnotherThread_ReturnsWaitForEventsAtOnce()
    {
        using var guard = ForegroundGuard.Begin();
        var platform = new SdlWindowingPlatform();
        using var app = new Application(platform, "wake", 200, 100);
        app.RunFrame(TimeSpan.Zero, forceRender: true); // shown after its first frame

        // Prove the wait parks at all, so the wake assertion below is not vacuous.
        // The window has just appeared, so a few OS events trickle in; pump until a
        // wait runs its full course.
        bool parked = false;
        for (int attempt = 0; attempt < 20 && !parked; attempt++)
        {
            platform.PumpEvents(null);
            var idle = Stopwatch.StartNew();
            platform.WaitForEvents(200);
            parked = idle.ElapsedMilliseconds >= 150;
        }
        Assert.True(parked, "WaitForEvents never parked for its full timeout");

        var waker = new Thread(() =>
        {
            Thread.Sleep(100);
            platform.Wake();
        });
        waker.Start();
        var woken = Stopwatch.StartNew();
        platform.WaitForEvents(3000);
        waker.Join();

        Assert.True(woken.ElapsedMilliseconds < 1000,
            $"WaitForEvents ran {woken.ElapsedMilliseconds} ms after a wake at 100 ms");
    }
}

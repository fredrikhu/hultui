using HultUI.Core.Platform;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Work posted from another thread must start a frame now, not when the idle wait
/// times out. <c>Application.Run</c> parks in <see cref="IWindowingPlatform.WaitForEvents"/>
/// between frames — 16 ms — and before <see cref="IWindowingPlatform.Wake"/> existed,
/// <c>InvokeOnMainThread</c> and every <c>await</c> continuation completing off-thread
/// sat in the queue for the rest of that wait. These pin the wiring: what wakes the
/// platform, and what must not. Whether the SDL platform's wait really returns on a
/// wake is a real-window question, answered in HultUI.UiTests.
/// </summary>
public class WakeTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private static void OffThread(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "off-thread work did not finish");
        Assert.Null(caught);
    }

    [Fact]
    public void InvokeOnMainThread_FromAnotherThread_WakesThePlatform()
    {
        var platform = new CountingPlatform();
        using var app = new Application(platform, "wake", 200, 100);
        bool ran = false;

        OffThread(() => Application.InvokeOnMainThread(() => ran = true));

        Assert.Equal(1, platform.Wakes);
        app.RunFrame(Frame);
        Assert.True(ran);
    }

    [Fact]
    public void InvokeOnMainThread_OnTheUIThread_DoesNotWake()
    {
        // The UI thread cannot be parked while it runs this. A wake here would only
        // queue an event that makes the next idle wait return at once, for nothing.
        var platform = new CountingPlatform();
        using var app = new Application(platform, "wake", 200, 100);
        bool ran = false;

        Application.InvokeOnMainThread(() => ran = true);

        Assert.Equal(0, platform.Wakes);
        app.RunFrame(Frame);
        Assert.True(ran);
    }

    [Fact]
    public void AwaitContinuation_PostedOffThread_WakesThePlatform()
    {
        var platform = new CountingPlatform();
        using var app = new Application(platform, "wake", 200, 100);
        var previous = app.InstallSynchronizationContext();
        try
        {
            var context = SynchronizationContext.Current!;

            OffThread(() => context.Post(_ => { }, null));
            Assert.Equal(1, platform.Wakes);

            // A continuation posted by the UI thread itself (an await whose antecedent
            // completed synchronously) has nothing to wake.
            context.Post(_ => { }, null);
            Assert.Equal(1, platform.Wakes);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}

/// <summary>A raster-surfaced platform that counts wakes instead of waiting.</summary>
internal sealed class CountingPlatform : IWindowingPlatform
{
    private int _wakes;

    public int Wakes => Volatile.Read(ref _wakes);

    public TimeProvider TimeProvider => TimeProvider.System;

    public IPlatformWindow CreateWindow(string title, int width, int height, WindowStyle style)
        => new HandlePlatformWindow(IntPtr.Zero, title, width, height, style);

    public bool PumpEvents(Action<IPlatformWindow>? onWindowClose) => true;
    public void WaitForEvents(int timeoutMs) { }
    public void Wake() => Interlocked.Increment(ref _wakes);
    public void Dispose() { }
}

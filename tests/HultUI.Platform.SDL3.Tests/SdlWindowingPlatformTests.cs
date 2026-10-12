using HultUI.Core.Platform;
using HultUI.Testing;
using SDL;
using Xunit;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>
/// What the SDL platform does on every pass of <c>Application.Run</c>'s loop, on SDL's dummy
/// video driver. The headless suite's frame-allocation tests run on the headless platform,
/// whose pump does nothing, so an allocation in this one was invisible to them: a lambda
/// wrapping the close callback cost every pass of every real window 88 bytes, idle ones
/// included (plan 17, runtime-sdl-pump-closure).
/// </summary>
[Collection(SdlDummyDriver.Collection)]
public sealed class SdlWindowingPlatformTests : IDisposable
{
    public SdlWindowingPlatformTests()
    {
        SDL_SetHint(SDL_HINT_VIDEO_DRIVER, "dummy");
        Assert.True(SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO), SDL_GetError());
    }

    public void Dispose() => SDL_Quit();

    [Fact]
    public void APumpWithACloseCallback_AllocatesNothing()
    {
        using var platform = new SdlWindowingPlatform();
        Action<IPlatformWindow> onClose = _ => { };
        for (int i = 0; i < 100; i++) Assert.True(platform.PumpEvents(onClose));

        long allocated = Allocations.Least(() =>
        {
            for (int i = 0; i < 1_000; i++) platform.PumpEvents(onClose);
        });

        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// An idle <c>Application.Run</c> waits with no timeout when it owns no deadline (plan 17,
    /// runtime-idle-wakeups): SDL's wait must then end on a wake from another thread, on every
    /// platform this suite runs on (Windows, and Linux under WSL). On the dummy driver SDL waits by
    /// polling its queue, which a wake fills.
    /// </summary>
    [Fact]
    public void AWaitWithNoTimeout_EndsOnAWakeFromAnotherThread()
    {
        using var platform = new SdlWindowingPlatform();
        platform.PumpEvents(null);
        var waker = new Thread(() =>
        {
            Thread.Sleep(200);
            platform.Wake();
        });
        var waited = System.Diagnostics.Stopwatch.StartNew();
        waker.Start();

        platform.WaitForEvents(Timeout.Infinite);

        waker.Join();
        Assert.InRange(waited.ElapsedMilliseconds, 150, 5000);
    }

    /// <summary>A wait of 0 polls: the loop's deadline already due.</summary>
    [Fact]
    public void AWaitOfZero_ReturnsAtOnce()
    {
        using var platform = new SdlWindowingPlatform();
        platform.PumpEvents(null);
        var waited = System.Diagnostics.Stopwatch.StartNew();

        platform.WaitForEvents(0);

        Assert.True(waited.ElapsedMilliseconds < 100, $"a wait of 0 took {waited.ElapsedMilliseconds} ms");
    }
}

/// <summary>SDL is process-global: the classes that start it on the dummy driver run one at a
/// time, or one's <c>SDL_Quit</c> or event pump would land in the middle of another's test.</summary>
internal static class SdlDummyDriver
{
    internal const string Collection = "SDL dummy video driver";
}

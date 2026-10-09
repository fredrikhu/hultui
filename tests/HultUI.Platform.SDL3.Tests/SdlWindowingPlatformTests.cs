using HultUI.Core.Platform;
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

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++) platform.PumpEvents(onClose);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}

/// <summary>SDL is process-global: the classes that start it on the dummy driver run one at a
/// time, or one's <c>SDL_Quit</c> or event pump would land in the middle of another's test.</summary>
internal static class SdlDummyDriver
{
    internal const string Collection = "SDL dummy video driver";
}

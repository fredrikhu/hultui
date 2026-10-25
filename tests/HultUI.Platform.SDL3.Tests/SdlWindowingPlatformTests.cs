using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    /// <summary>
    /// A wake whose event the pump itself takes must not stop the next wake from ending a wait
    /// (plan 17, round 4, runtime-sdl-wake-flag-after-poll). Wakes are coalesced by a flag that
    /// says a wake event is queued, and the pump cleared it before it polled: a wake landing
    /// between the clear and the poll set it again and pushed an event the poll then took. The
    /// flag stayed set with nothing queued, so every later wake returned without pushing one, and
    /// an idle loop with no deadline, which waits with no timeout, slept through the posts it was
    /// woken for until some input came.
    ///
    /// <para>The wake is made inside the pump, step by step: a callback queued for SDL's main
    /// thread (<c>SDL_RunOnMainThread</c>, from another thread) runs at the start of SDL's next
    /// pump of the OS, which the pump's first poll does before it takes the queue — after the
    /// pump began, before its poll took the wake's event. Then a worker wakes the loop 50 ms into
    /// a wait that stands for the loop's endless one.</para>
    /// </summary>
    [Fact]
    public unsafe void AWakeThePumpTookItself_LeavesTheNextWakeToEndAWait()
    {
        using var platform = new SdlWindowingPlatform();
        platform.PumpEvents(null);
        // SDL_Init (the constructor, on this thread) made this SDL's main thread.
        Assert.True(SDL_IsMainThread(), "SDL runs main-thread callbacks in this thread's pumps");
        s_wakeInsidePump = platform;
        s_wakesInsidePump = 0;
        try
        {
            bool queued = false;
            var queuer = new Thread(() => queued = SDL_RunOnMainThread(&WakeInsidePump, IntPtr.Zero, false));
            queuer.Start();
            queuer.Join();
            Assert.True(queued, SDL_GetError());

            platform.PumpEvents(null);
        }
        finally
        {
            s_wakeInsidePump = null;
        }
        Assert.Equal(1, s_wakesInsidePump);     // the wake landed inside the pump
        Assert.False(WakeEventQueued());        // and the pump's poll took its event

        var waker = new Thread(() =>
        {
            Thread.Sleep(50);
            platform.Wake();
        });
        var waited = System.Diagnostics.Stopwatch.StartNew();
        waker.Start();
        platform.WaitForEvents(3000);
        waker.Join();

        Assert.True(waited.ElapsedMilliseconds is >= 30 and < 1500,
            $"a wait woken 50 ms in ran {waited.ElapsedMilliseconds} ms");
    }

    /// <summary>The other places a wake can land: before the pump (its event is the pump's to take)
    /// and after the pump's poll, before the wait (its event ends the wait at once).</summary>
    [Fact]
    public void AWakeBeforeThePump_OrAfterIt_EndsTheNextWait()
    {
        using var platform = new SdlWindowingPlatform();
        platform.PumpEvents(null);

        platform.Wake();
        platform.PumpEvents(null);
        Assert.False(WakeEventQueued());

        platform.Wake();
        Assert.True(WakeEventQueued());
        var waited = System.Diagnostics.Stopwatch.StartNew();
        platform.WaitForEvents(3000);
        Assert.True(waited.ElapsedMilliseconds < 1500, $"a wait with a wake queued ran {waited.ElapsedMilliseconds} ms");
    }

    /// <summary>Whether a wake event is on SDL's queue: <c>Wake</c> pushes an event of a type it
    /// registered, and registered types are SDL's user range.</summary>
    private static bool WakeEventQueued() =>
        SDL_HasEvents((uint)SDL_EventType.SDL_EVENT_USER, (uint)SDL_EventType.SDL_EVENT_LAST);

    // The platform the main-thread callback wakes, while a test waits for it; null otherwise, so a
    // callback SDL runs later, or never, touches nothing.
    private static SdlWindowingPlatform? s_wakeInsidePump;
    private static int s_wakesInsidePump;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void WakeInsidePump(IntPtr userdata)
    {
        if (s_wakeInsidePump is not { } platform) return;
        s_wakesInsidePump++;
        platform.Wake();
    }
}

/// <summary>SDL is process-global: the classes that start it on the dummy driver run one at a
/// time, or one's <c>SDL_Quit</c> or event pump would land in the middle of another's test.</summary>
internal static class SdlDummyDriver
{
    internal const string Collection = "SDL dummy video driver";
}

using System.Diagnostics;
using Xunit;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>
/// Windows' timer resolution is asked for (SDL's <c>SDL_HINT_TIMER_RESOLUTION</c>, which SDL turns
/// into <c>timeBeginPeriod</c> and <c>timeEndPeriod</c>) only while the frame loop paces frames
/// (plan 17, round 13, gl-sdl-timer-resolution-at-rest). SDL's default held 1 ms from
/// <c>SDL_Init</c> for the life of the process, which kept the machine's clock at 1,000 interrupts
/// a second under a resting window where nothing else asked for it.
/// </summary>
public sealed class TimerResolutionPolicyTests
{
    private static readonly long Ms = Stopwatch.Frequency / 1000;

    private readonly List<bool> _asked = [];
    private readonly SdlTimerResolution _timer;

    public TimerResolutionPolicyTests() => _timer = new SdlTimerResolution(_asked.Add);

    [Fact]
    public void LongWaitsAlone_AskForNothing()
    {
        Assert.Equal(-1, _timer.BeforeWait(Timeout.Infinite, 0));
        Assert.Equal(-1, _timer.BeforeWait(500, 10 * Ms));
        Assert.Equal(-1, _timer.BeforeWait(SdlTimerResolution.PacedWaitMs, 20 * Ms));
        Assert.Equal(-1, _timer.BeforeWait(0, 30 * Ms));

        Assert.Empty(_asked);
        Assert.False(_timer.Held);
    }

    /// <summary>The remainder of a display interval asks for 1 ms before its wait, once for a run
    /// of them: a tween at 200 Hz is 200 such waits a second.</summary>
    [Fact]
    public void ShortWaits_AskForOneMillisecondBeforeTheFirst_AndOnce()
    {
        Assert.Equal(-1, _timer.BeforeWait(4, 0));
        Assert.Equal([true], _asked);

        for (int i = 1; i <= 200; i++)
            Assert.Equal(-1, _timer.BeforeWait(1 + i % 8, i * 5 * Ms));

        Assert.Equal([true], _asked);
        Assert.True(_timer.Held);
    }

    /// <summary>A rest gives it back a second after the last short wait: inside the wait that
    /// starts the rest, which is told where.</summary>
    [Fact]
    public void ARest_GivesItBackASecondAfterTheLastShortWait()
    {
        _timer.BeforeWait(5, 0);

        Assert.Equal(SdlTimerResolution.HoldMs - 100, _timer.BeforeWait(Timeout.Infinite, 100 * Ms));
        Assert.Equal(SdlTimerResolution.HoldMs - 100, _timer.BeforeWait(5_000, 100 * Ms));
        Assert.True(_timer.Held);   // the caller releases it when that point comes with nothing arrived

        _timer.Release();
        Assert.Equal([true, false], _asked);
        Assert.False(_timer.Held);

        // And the next short wait asks again.
        _timer.BeforeWait(3, 2_000 * Ms);
        Assert.Equal([true, false, true], _asked);
    }

    /// <summary>A wait that ends before the hold does keeps it; one that starts after it gives it
    /// back at once.</summary>
    [Fact]
    public void AWaitThatEndsFirst_KeepsIt_AndOneThatStartsAfter_GivesItBack()
    {
        _timer.BeforeWait(5, 0);

        Assert.Equal(-1, _timer.BeforeWait(500, 100 * Ms));
        Assert.Equal([true], _asked);

        Assert.Equal(-1, _timer.BeforeWait(500, (SdlTimerResolution.HoldMs + 1) * Ms));
        Assert.Equal([true, false], _asked);
        Assert.False(_timer.Held);
    }

    /// <summary>A pause inside a drag (an infinite wait between two moves) keeps it.</summary>
    [Fact]
    public void ShortWaitsWithPausesBetween_AskOnce()
    {
        for (int i = 0; i < 10; i++)
        {
            _timer.BeforeWait(6, i * 300 * Ms);
            Assert.True(_timer.BeforeWait(Timeout.Infinite, (i * 300 + 8) * Ms) > 0);
        }

        Assert.Equal([true], _asked);
    }
}

/// <summary>
/// The same through SDL itself, on its dummy video driver: SDL started as the framework starts it,
/// the hint SDL holds read back, and the platform's real waits.
/// </summary>
[Collection(SdlDummyDriver.Collection)]
public sealed class TimerResolutionHintTests : IDisposable
{
    public TimerResolutionHintTests()
    {
        SDL_SetHint(SDL_HINT_VIDEO_DRIVER, "dummy");
        SDL3Window.EnsureSDLInitialized();
    }

    public void Dispose() => SDL3Window.ShutdownSDL();

    [WindowsFact("SDL asks for a timer resolution on Windows only")]
    public void SdlStarts_AskingForNoTimerResolution()
    {
        Assert.Equal("0", SDL_GetHint(SDL_HINT_TIMER_RESOLUTION));
    }

    /// <summary>A paced wait holds 1 ms; the rest after it gives it back a second later, partway
    /// into a wait that still lasts until its wake.</summary>
    [WindowsFact("SDL asks for a timer resolution on Windows only")]
    public void APacedWait_AsksForOneMillisecond_AndARestGivesItBack()
    {
        using var platform = new SdlWindowingPlatform();
        platform.PumpEvents(null);

        platform.WaitForEvents(5);
        Assert.Equal("1", SDL_GetHint(SDL_HINT_TIMER_RESOLUTION));

        var waited = WaitWokenAfter(platform, 1_600);

        Assert.Equal("0", SDL_GetHint(SDL_HINT_TIMER_RESOLUTION));
        Assert.True(waited >= 1_400, $"a wait woken 1.6 s in ended after {waited} ms");
    }

    /// <summary>Woken before the hold is out, it keeps 1 ms: the next frame may be paced.</summary>
    [WindowsFact("SDL asks for a timer resolution on Windows only")]
    public void AWakeBeforeTheHoldIsOut_KeepsOneMillisecond()
    {
        using var platform = new SdlWindowingPlatform();
        platform.PumpEvents(null);

        platform.WaitForEvents(5);
        WaitWokenAfter(platform, 200);

        Assert.Equal("1", SDL_GetHint(SDL_HINT_TIMER_RESOLUTION));
    }

    /// <summary>A wait with a timeout that runs past the hold gives it back and still lasts its
    /// timeout.</summary>
    [WindowsFact("SDL asks for a timer resolution on Windows only")]
    public void ATimedWaitPastTheHold_GivesItBack_AndLastsItsTimeout()
    {
        using var platform = new SdlWindowingPlatform();
        platform.PumpEvents(null);

        platform.WaitForEvents(5);
        var waited = Stopwatch.StartNew();
        platform.WaitForEvents(1_300);

        Assert.Equal("0", SDL_GetHint(SDL_HINT_TIMER_RESOLUTION));
        Assert.InRange(waited.ElapsedMilliseconds, 1_250, 3_000);
    }

    private static long WaitWokenAfter(SdlWindowingPlatform platform, int ms)
    {
        var waker = new Thread(() =>
        {
            Thread.Sleep(ms);
            platform.Wake();
        });
        var waited = Stopwatch.StartNew();
        waker.Start();
        platform.WaitForEvents(Timeout.Infinite);
        waker.Join();
        return waited.ElapsedMilliseconds;
    }
}

/// <summary>A test of something only Windows has; elsewhere it reports as skipped, with the reason.</summary>
internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute(string reason)
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows only: " + reason;
    }
}

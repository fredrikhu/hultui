using System.Diagnostics;
using HultUI.Core.Rendering;
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
    private readonly List<bool> _asked = [];
    private readonly ManualClock _clock = new();
    private readonly SdlTimerResolution _timer;

    public TimerResolutionPolicyTests() => _timer = new SdlTimerResolution(_clock, _asked.Add);

    /// <summary>A wait of <paramref name="timeoutMs"/> that starts <paramref name="atMs"/> into
    /// the test.</summary>
    private int WaitAt(int timeoutMs, long atMs)
    {
        _clock.At(atMs);
        return _timer.BeforeWait(timeoutMs);
    }

    [Fact]
    public void LongWaitsAlone_AskForNothing()
    {
        Assert.Equal(-1, WaitAt(Timeout.Infinite, 0));
        Assert.Equal(-1, WaitAt(500, 10));
        Assert.Equal(-1, WaitAt(SdlTimerResolution.PacedWaitMs, 20));
        Assert.Equal(-1, WaitAt(0, 30));

        Assert.Empty(_asked);
        Assert.False(_timer.Held);
    }

    /// <summary>The remainder of a display interval asks for 1 ms before its wait, once for a run
    /// of them: a tween at 200 Hz is 200 such waits a second.</summary>
    [Fact]
    public void ShortWaits_AskForOneMillisecondBeforeTheFirst_AndOnce()
    {
        Assert.Equal(-1, WaitAt(4, 0));
        Assert.Equal([true], _asked);

        for (int i = 1; i <= 200; i++)
            Assert.Equal(-1, WaitAt(1 + i % 8, i * 5));

        Assert.Equal([true], _asked);
        Assert.True(_timer.Held);
    }

    /// <summary>A rest gives it back a second after the last short wait: inside the wait that
    /// starts the rest, which is told where.</summary>
    [Fact]
    public void ARest_GivesItBackASecondAfterTheLastShortWait()
    {
        WaitAt(5, 0);

        Assert.Equal(SdlTimerResolution.HoldMs - 100, WaitAt(Timeout.Infinite, 100));
        Assert.Equal(SdlTimerResolution.HoldMs - 100, WaitAt(5_000, 100));
        Assert.True(_timer.Held);   // the caller releases it when that point comes with nothing arrived

        _timer.Release();
        Assert.Equal([true, false], _asked);
        Assert.False(_timer.Held);

        // And the next short wait asks again.
        WaitAt(3, 2_000);
        Assert.Equal([true, false, true], _asked);
    }

    /// <summary>A wait that ends before the hold does keeps it; one that starts after it gives it
    /// back at once.</summary>
    [Fact]
    public void AWaitThatEndsFirst_KeepsIt_AndOneThatStartsAfter_GivesItBack()
    {
        WaitAt(5, 0);

        Assert.Equal(-1, WaitAt(500, 100));
        Assert.Equal([true], _asked);

        Assert.Equal(-1, WaitAt(500, SdlTimerResolution.HoldMs + 1));
        Assert.Equal([true, false], _asked);
        Assert.False(_timer.Held);
    }

    /// <summary>A pause inside a drag (an infinite wait between two moves) keeps it.</summary>
    [Fact]
    public void ShortWaitsWithPausesBetween_AskOnce()
    {
        for (int i = 0; i < 10; i++)
        {
            WaitAt(6, i * 300);
            Assert.True(WaitAt(Timeout.Infinite, i * 300 + 8) > 0);
        }

        Assert.Equal([true], _asked);
    }
}

/// <summary>
/// The same through SDL itself, on its dummy video driver: SDL started as the framework starts it,
/// the hint SDL holds read back, and the platform's real waits. The hold is counted on a clock the
/// test moves, so whether it is out when a wait starts is the test's to say: on the system clock a
/// 2-core CI runner let the second run out before a wake 200 ms in, and the test read 0 for 1.
/// </summary>
[Collection(SdlDummyDriver.Collection)]
public sealed class TimerResolutionHintTests : IDisposable
{
    private readonly ManualClock _clock = new();

    public TimerResolutionHintTests()
    {
        SDL_SetHint(SDL_HINT_VIDEO_DRIVER, "dummy");
        SDL3Window.EnsureSDLInitialized();
    }

    public void Dispose() => SDL3Window.ShutdownSDL();

    private static string? Hint => SDL_GetHint(SDL_HINT_TIMER_RESOLUTION);

    /// <summary>A platform whose hold is counted on the test's clock, after one short wait at its
    /// start: 1 ms is held from there.</summary>
    private SdlWindowingPlatform PacedPlatform()
    {
        var platform = new SdlWindowingPlatform(RendererKind.Default, displayColorEvents: false, _clock);
        platform.PumpEvents(null);
        platform.WaitForEvents(5);
        Assert.Equal("1", Hint);
        return platform;
    }

    [WindowsFact("SDL asks for a timer resolution on Windows only")]
    public void SdlStarts_AskingForNoTimerResolution()
    {
        Assert.Equal("0", Hint);
    }

    /// <summary>A paced wait holds 1 ms; the rest after it gives it back once the hold is out,
    /// partway into a wait that still lasts until its wake.</summary>
    [WindowsFact("SDL asks for a timer resolution on Windows only")]
    public void APacedWait_AsksForOneMillisecond_AndARestGivesItBack()
    {
        using var platform = PacedPlatform();

        // The hold runs out 50 ms into the next wait. The wake comes only once 1 ms is given
        // back, and a while after: the wait must go on past that point and end at the wake.
        _clock.Advance(SdlTimerResolution.HoldMs - 50);
        bool woken = false;
        var waker = new Thread(() =>
        {
            var giveUp = Stopwatch.StartNew();
            while (Hint != "0" && giveUp.Elapsed < TimeSpan.FromSeconds(30)) Thread.Sleep(1);
            Thread.Sleep(100);
            Volatile.Write(ref woken, true);
            platform.Wake();
        });
        waker.Start();
        platform.WaitForEvents(Timeout.Infinite);
        bool endedAtTheWake = Volatile.Read(ref woken);
        waker.Join();

        Assert.Equal("0", Hint);
        Assert.True(endedAtTheWake, "the wait ended where it gave 1 ms back, before its wake");
    }

    /// <summary>Woken before the hold is out, it keeps 1 ms: the next frame may be paced.</summary>
    [WindowsFact("SDL asks for a timer resolution on Windows only")]
    public void AWakeBeforeTheHoldIsOut_KeepsOneMillisecond()
    {
        using var platform = PacedPlatform();

        // 200 ms on, with 800 ms of the hold left, a wake ends the wait.
        _clock.Advance(200);
        platform.Wake();
        platform.WaitForEvents(Timeout.Infinite);

        Assert.Equal("1", Hint);
    }

    /// <summary>A wait with a timeout that runs past the hold gives it back and still lasts its
    /// timeout.</summary>
    [WindowsFact("SDL asks for a timer resolution on Windows only")]
    public void ATimedWaitPastTheHold_GivesItBack_AndLastsItsTimeout()
    {
        using var platform = PacedPlatform();

        // The hold runs out 100 ms into a wait of 300.
        _clock.Advance(SdlTimerResolution.HoldMs - 100);
        var waited = Stopwatch.StartNew();
        platform.WaitForEvents(300);

        Assert.Equal("0", Hint);
        Assert.True(waited.ElapsedMilliseconds >= 250, $"a wait of 300 ms ended after {waited.ElapsedMilliseconds} ms");
    }
}

/// <summary>A clock that stands where the test puts it.</summary>
internal sealed class ManualClock : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Volatile.Read(ref _ticks);

    public void At(long ms) => Volatile.Write(ref _ticks, ms * TimeSpan.TicksPerMillisecond);

    public void Advance(long ms) => Interlocked.Add(ref _ticks, ms * TimeSpan.TicksPerMillisecond);
}

/// <summary>A test of something only Windows has; elsewhere it reports as skipped, with the reason.</summary>
internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute(string reason)
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows only: " + reason;
    }
}

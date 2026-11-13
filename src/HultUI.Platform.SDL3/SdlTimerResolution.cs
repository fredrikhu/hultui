using System.Diagnostics;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3;

/// <summary>
/// The timer resolution SDL asks Windows for (<c>timeBeginPeriod</c>, through its
/// <c>SDL_HINT_TIMER_RESOLUTION</c> hint): 1 ms while the frame loop waits on the clock, and
/// Windows' default of 15.6 ms while it rests.
///
/// <para><b>Why not all the time.</b> SDL's default asks for 1 ms at <c>SDL_Init</c> and keeps it
/// for the life of the process. Where nothing else asks for it (a laptop with no video playing),
/// that one request keeps the machine's clock interrupt at 1,000 a second instead of 64, for an
/// app that spends most of its life open and at rest. A resting window needs no fine clock: it
/// waits for an event or a wake, which end a wait at once whatever the resolution, or for a
/// deadline it owns (a caret's blink, a tooltip's delay), which may then come up to one tick
/// late. A window that draws does need it: the loop waits out what is left of a display
/// interval, a few milliseconds, with a timed wait, and at 15.6 ms a tween would draw about 64
/// frames a second on any display.</para>
///
/// <para><b>When.</b> A wait shorter than <see cref="PacedWaitMs"/> asks for 1 ms before it
/// starts, so the first frame after a rest is on time. A longer wait, or one with no timeout,
/// gives it back once <see cref="HoldMs"/> has passed since the last short one: partway into
/// that wait, which the platform splits there, so the loop does not come round for it. The hold
/// keeps a drag or a run of hover tweens from switching at every pause. SDL calls
/// <c>timeBeginPeriod</c> or <c>timeEndPeriod</c> each time the hint changes (SDL_timer.c,
/// <c>SDL_TimerResolutionChanged</c>). <c>SDL_TIMER_RESOLUTION</c> in the environment wins over
/// all of this, as it does for every SDL hint.</para>
///
/// <para>Windows only: elsewhere the hint does nothing. Since Windows 10 2004 the request is the
/// process's own: another program that holds 1 ms (Steam, a browser playing video) speeds the
/// machine's clock up, but this process's timed waits stay at 15.6 ms unless it asks too. So the
/// request is needed while frames are paced even on a machine where something else holds it, and
/// NVIDIA's Vulkan driver, which asks for 1 ms for its own process, makes the rest moot there.</para>
/// </summary>
internal sealed class SdlTimerResolution
{
    /// <summary>The shortest wait that does not ask for 1 ms: two of Windows' default ticks. A
    /// display interval's remainder is shorter at any refresh rate above 31 Hz.</summary>
    internal const int PacedWaitMs = 32;

    /// <summary>How long the request outlives the last short wait.</summary>
    internal const int HoldMs = 1000;

    private static readonly long HoldTicks = HoldMs * Stopwatch.Frequency / 1000;

    private readonly Action<bool> _ask;
    private bool _held;
    private long _lastPacedAt;

    /// <summary>Asks through SDL's hint.</summary>
    internal SdlTimerResolution() : this(AskSdl)
    {
    }

    /// <param name="ask">Asked for 1 ms (true) or for nothing (false), each time that changes.</param>
    internal SdlTimerResolution(Action<bool> ask) => _ask = ask;

    /// <summary>Whether 1 ms is asked for now.</summary>
    internal bool Held => _held;

    /// <summary>
    /// Before <c>SDL_Init</c>: SDL asks for nothing until the first short wait. SDL reads the hint
    /// when its clock starts, so set later it would ask for 1 ms first and give it back here.
    /// </summary>
    internal static void AtRest()
    {
        if (OperatingSystem.IsWindows())
            SDL_SetHint(SDL_HINT_TIMER_RESOLUTION, "0");
    }

    /// <summary>
    /// Called before a wait of <paramref name="timeoutMs"/> (<see cref="Timeout.Infinite"/> for
    /// none) that starts at <paramref name="now"/>, in <see cref="Stopwatch"/> ticks. A short wait
    /// asks for 1 ms; a longer one that starts after the hold is out gives it back. Returns how
    /// many milliseconds into this wait the hold runs out, when that is before the wait ends: the
    /// caller waits that long, then calls <see cref="Release"/> unless something arrived, and waits
    /// the rest. Otherwise -1.
    /// </summary>
    internal int BeforeWait(int timeoutMs, long now)
    {
        if (timeoutMs is > 0 and < PacedWaitMs)
        {
            _lastPacedAt = now;
            if (!_held)
            {
                _held = true;
                _ask(true);
            }
            return -1;
        }
        // A poll (0) waits for nothing.
        if (!_held || timeoutMs == 0) return -1;
        long left = _lastPacedAt + HoldTicks - now;
        if (left <= 0)
        {
            Release();
            return -1;
        }
        int ms = (int)((left * 1000 + Stopwatch.Frequency - 1) / Stopwatch.Frequency);
        return timeoutMs < 0 || timeoutMs > ms ? ms : -1;
    }

    /// <summary>Gives the 1 ms request back.</summary>
    internal void Release()
    {
        if (!_held) return;
        _held = false;
        _ask(false);
    }

    private static void AskSdl(bool fine) => SDL_SetHint(SDL_HINT_TIMER_RESOLUTION, fine ? "1" : "0");
}

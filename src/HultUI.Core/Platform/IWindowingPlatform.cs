using HultUI.Core.Styling;

namespace HultUI.Core.Platform;

/// <summary>
/// Platform abstraction for the windowing system as a whole: window creation,
/// native event pumping, and the clock. The application loop talks only to
/// this interface; SDL3 implements it for real displays and HultUI.Headless
/// implements it for display-free testing.
/// </summary>
public interface IWindowingPlatform : IDisposable
{
    IPlatformWindow CreateWindow(string title, int width, int height, WindowStyle style);

    /// <summary>
    /// Polls native events, routing input to each window's
    /// <see cref="IPlatformWindow.InputReceived"/>. Returns false when the
    /// platform requests application quit.
    /// </summary>
    bool PumpEvents(Action<IPlatformWindow>? onWindowClose);

    /// <summary>
    /// Parks the loop until a native event arrives, <see cref="Wake"/> is called,
    /// or the timeout elapses. <c>Application.Run</c> calls it when no window rendered
    /// in an iteration: when nothing needs a frame, until the next deadline the loop owns
    /// (an animation's next step, a coalesced accessibility refresh, a poll), or with
    /// <see cref="Timeout.Infinite"/> (-1) when it owns none; and when a window needs a
    /// frame but its display interval has not yet passed. A platform that runs that loop
    /// parks for the whole timeout when nothing arrives, a timeout of 1 ms included: a
    /// wait that returns at once makes the second case a busy loop. A timeout of 0 polls.
    /// </summary>
    void WaitForEvents(int timeoutMs);

    /// <summary>
    /// Unblocks a <see cref="WaitForEvents"/> in progress so the next frame starts
    /// now rather than when the timeout elapses. Thread-safe, and the only member of
    /// this interface that is: it exists for work arriving from other threads — a
    /// posted continuation, a decoded video frame, a native callback — and for work a
    /// callback the wait itself dispatched on the UI thread leaves behind, which would
    /// otherwise wait out the idle wait — with no deadline, for good — before anything
    /// looked at it. A wake that lands between waits ends the next one at once, as an
    /// event queued then would. A platform with no idle wait of its own (a host that
    /// drives <c>RunFrame</c> from its own loop) implements it as a no-op.
    /// </summary>
    void Wake();

    /// <summary>
    /// Clock used for input timing (double-click detection). Real platforms
    /// return <see cref="TimeProvider.System"/>; headless supplies a manually
    /// advanced provider so interaction timing is deterministic.
    /// </summary>
    TimeProvider TimeProvider { get; }
}

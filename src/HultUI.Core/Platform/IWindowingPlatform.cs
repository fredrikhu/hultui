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
    /// Parks the loop until a native event arrives or the timeout elapses.
    /// Called only when no window needed a frame this iteration.
    /// </summary>
    void WaitForEvents(int timeoutMs);

    /// <summary>
    /// Clock used for input timing (double-click detection). Real platforms
    /// return <see cref="TimeProvider.System"/>; headless supplies a manually
    /// advanced provider so interaction timing is deterministic.
    /// </summary>
    TimeProvider TimeProvider { get; }
}

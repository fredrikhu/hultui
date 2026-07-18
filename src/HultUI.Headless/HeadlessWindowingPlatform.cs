using HultUI.Core.Platform;
using HultUI.Core.Styling;

namespace HultUI.Headless;

/// <summary>
/// The display-free windowing platform: creates <see cref="HeadlessPlatformWindow"/>s
/// and supplies the session's virtual clock. There is no native event queue —
/// input arrives only through explicit injection, so <see cref="PumpEvents"/> is
/// a no-op that never requests quit.
/// </summary>
internal sealed class HeadlessWindowingPlatform : IWindowingPlatform
{
    private readonly HeadlessTimeProvider _time;

    public HeadlessWindowingPlatform(HeadlessTimeProvider time)
    {
        _time = time;
    }

    /// <summary>Scale applied to windows created through the Application (which has no scale parameter).</summary>
    public float NextWindowScale { get; set; } = 1f;

    /// <summary>Options applied to windows this platform creates.</summary>
    public HeadlessOptions? Options { get; set; }

    public TimeProvider TimeProvider => _time;

    public IPlatformWindow CreateWindow(string title, int width, int height, WindowStyle style)
        => new HeadlessPlatformWindow(title, width, height, NextWindowScale, style, Options);

    public bool PumpEvents(Action<IPlatformWindow>? onWindowClose) => true;

    public void WaitForEvents(int timeoutMs)
    {
        // Nothing can arrive; sleep a token amount so a caller that loops on
        // this (none in the headless pump, but Run() would) yields the CPU.
        Thread.Sleep(1);
    }

    public void Dispose() { }
}

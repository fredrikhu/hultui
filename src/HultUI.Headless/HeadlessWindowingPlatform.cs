using HultUI.Core.Platform;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;

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

    // One set of text services for every window, as on the real platforms: built with the
    // first window that needs it, given back when the session ends. Pinned windows measure in
    // other fonts, so they share a set of their own.
    private SkiaTextServices? _text;
    private SkiaTextServices? _pinnedText;

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
    {
        var text = Options?.PinnedText == true
            ? _pinnedText ??= PinnedFonts.CreateServices()
            : _text ??= new SkiaTextServices();
        return new HeadlessPlatformWindow(title, width, height, NextWindowScale, style, text, Options);
    }

    public bool PumpEvents(Action<IPlatformWindow>? onWindowClose) => true;

    /// <summary>The period of the event source <see cref="HeadlessSession.RunLoop"/> was given,
    /// or null: then nothing arrives, and a wait runs its whole timeout.</summary>
    internal TimeSpan? EventEvery { get; private set; }

    private Action? _onEvent;
    private TimeSpan _untilEvent;

    /// <summary>Starts (or, with null, stops) an event every <paramref name="every"/> of virtual
    /// time, the first one period from now, each running <paramref name="onEvent"/>.</summary>
    internal void SetEventSource(TimeSpan? every, Action? onEvent)
    {
        if (every is { } period && period <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(every), "An event source needs a period above zero.");
        EventEvery = every;
        _onEvent = every is null ? null : onEvent;
        _untilEvent = every ?? TimeSpan.Zero;
    }

    /// <summary>
    /// Where <c>Application.Run</c> parks between frames. Time here is virtual, so a wait is
    /// time moving on: the whole timeout, or up to the next event of the source
    /// <see cref="HeadlessSession.RunLoop"/> set, which ends the wait early as an arriving
    /// event does on a real platform. Nothing sleeps.
    /// </summary>
    public void WaitForEvents(int timeoutMs)
    {
        var wait = TimeSpan.FromMilliseconds(Math.Max(0, timeoutMs));
        if (EventEvery is { } every)
        {
            if (_untilEvent <= wait)
            {
                _time.Advance(_untilEvent);
                _untilEvent = every;
                _onEvent?.Invoke();
                return;
            }
            _untilEvent -= wait;
        }
        _time.Advance(wait);
    }

    /// <summary>No-op: the session pumps frames explicitly, so there is never a wait to cut short.</summary>
    public void Wake() { }

    /// <summary>Gives back the text services the windows shared; the windows are gone by now.</summary>
    public void Dispose()
    {
        _text?.Dispose();
        _pinnedText?.Dispose();
    }
}

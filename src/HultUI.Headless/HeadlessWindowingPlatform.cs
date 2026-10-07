using HultUI.Core.Platform;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;

namespace HultUI.Headless;

/// <summary>
/// The display-free windowing platform: creates <see cref="HeadlessPlatformWindow"/>s
/// and supplies the session's virtual clock. There is no native event queue —
/// input arrives only through explicit injection, so <see cref="PumpEvents"/>
/// never requests quit; the one thing it delivers is a change to the simulated
/// monitors (<see cref="SetMonitors"/>), as a real platform delivers a hot-plug.
/// </summary>
internal sealed class HeadlessWindowingPlatform : IWindowingPlatform, IMonitorSource
{
    private readonly HeadlessTimeProvider _time;
    private readonly List<HeadlessPlatformWindow> _windows = [];
    private IReadOnlyList<MonitorInfo> _monitors = [];
    private bool _monitorsChangedPending;

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
    public HeadlessOptions? Options
    {
        get => _options;
        set
        {
            _options = value;
            _monitors = value?.Monitors is { Count: > 0 } m ? [.. m] : [];
        }
    }
    private HeadlessOptions? _options;

    public TimeProvider TimeProvider => _time;

    /// <inheritdoc/>
    public IReadOnlyList<MonitorInfo> Monitors => _monitors;

    /// <inheritdoc/>
    public event Action? MonitorsChanged;

    public IPlatformWindow CreateWindow(string title, int width, int height, WindowStyle style)
    {
        var text = Options?.PinnedText == true
            ? _pinnedText ??= PinnedFonts.CreateServices()
            : _text ??= new SkiaTextServices();
        var window = new HeadlessPlatformWindow(title, width, height, NextWindowScale, style, text, Options)
        {
            Desktop = () => _monitors,
        };
        _windows.RemoveAll(w => !w.IsOpen);
        _windows.Add(window);
        return window;
    }

    /// <summary>
    /// Replaces the simulated monitors: a hot-plug. Windows are first moved the way an OS moves
    /// them — one whose monitor went is brought onto the nearest remaining one, a maximized or
    /// full-screen one fills its (possibly resized) monitor again — and the change is announced
    /// from the next <see cref="PumpEvents"/>, as a real display change is.
    /// </summary>
    public void SetMonitors(IReadOnlyList<MonitorInfo> monitors)
    {
        _monitors = [.. monitors];
        foreach (var w in _windows)
            if (w.IsOpen) w.OnDesktopChanged();
        _monitorsChangedPending = true;
    }

    /// <summary>Takes the queued wake, as a real platform's pump takes its wake event, and
    /// announces a pending change of the simulated monitors; never requests quit.</summary>
    public bool PumpEvents(Action<IPlatformWindow>? onWindowClose)
    {
        Volatile.Write(ref _wakePending, false);
        if (_monitorsChangedPending)
        {
            _monitorsChangedPending = false;
            MonitorsChanged?.Invoke();
        }
        return true;
    }

    // A Wake not yet taken by a pump: the next wait returns at once, as a real platform's does
    // with its wake event queued.
    private bool _wakePending;

    /// <summary>How many times a wait was ended by a <see cref="Wake"/> (tests of the producers
    /// that must wake the loop).</summary>
    internal int WakesTaken { get; private set; }

    /// <summary>The virtual time a wait with no deadline and no event to come runs to:
    /// <see cref="HeadlessSession.RunLoop"/>'s limit, past which the run fails. Null outside a run.</summary>
    internal long? Horizon { get; set; }

    /// <summary>The period of the event source <see cref="HeadlessSession.RunLoop"/> was given,
    /// or null: then nothing arrives, and a wait runs its whole timeout.</summary>
    internal TimeSpan? EventEvery { get; private set; }

    private Action? _onEvent;
    private TimeSpan _untilEvent;

    /// <summary>Starts (or, with null, stops) an event every <paramref name="every"/> of virtual
    /// time, the first one period from now, each running <paramref name="onEvent"/>. With
    /// <paramref name="endsWait"/> false an event runs inside the wait without ending it, as a
    /// message SDL makes no event of does (<c>WM_GETOBJECT</c>, a DispatcherQueue item, a
    /// WebView2 callback): only a <see cref="Wake"/> it makes, or the timeout, ends the wait.</summary>
    internal void SetEventSource(TimeSpan? every, Action? onEvent, bool endsWait = true)
    {
        if (every is { } period && period <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(every), "An event source needs a period above zero.");
        EventEvery = every;
        _onEvent = every is null ? null : onEvent;
        _untilEvent = every ?? TimeSpan.Zero;
        _eventsEndWaits = endsWait;
    }

    private bool _eventsEndWaits = true;

    /// <summary>How many times <see cref="Wake"/> was called, from any thread.</summary>
    internal int WakeCalls => Volatile.Read(ref _wakeCalls);
    private int _wakeCalls;

    /// <summary>
    /// Where <c>Application.Run</c> parks between frames. Time here is virtual, so a wait is
    /// time moving on: the whole timeout, or up to the next event of the source
    /// <see cref="HeadlessSession.RunLoop"/> set, which ends the wait early as an arriving
    /// event does on a real platform. Nothing sleeps.
    /// </summary>
    /// <remarks>
    /// A wake queued before the wait (<see cref="Wake"/>) ends it at once, without time moving.
    /// A wait with no limit (<see cref="Timeout.Infinite"/>) runs to the next event of the
    /// source, or, with none, to just past the run's <see cref="Horizon"/>: nothing can arrive,
    /// so the run's condition is asked once more and the run fails if it is still unmet.
    /// </remarks>
    public void WaitForEvents(int timeoutMs)
    {
        if (Volatile.Read(ref _wakePending))
        {
            WakesTaken++;
            return;
        }
        bool infinite = timeoutMs == Timeout.Infinite;
        var wait = TimeSpan.FromMilliseconds(Math.Max(0, timeoutMs));
        if (infinite && Horizon is not { } && !(EventEvery is not null && _eventsEndWaits))
            throw new InvalidOperationException(
                "The loop parked with no deadline and nothing that could wake it, outside HeadlessSession.RunLoop.");
        while (EventEvery is { } every)
        {
            if (!infinite && _untilEvent > wait)
            {
                _untilEvent -= wait;
                break;
            }
            if (infinite && !_eventsEndWaits && Horizon is { } limit && _time.GetTimestamp() + _untilEvent.Ticks > limit)
                break;
            _time.Advance(_untilEvent);
            wait -= _untilEvent;
            _untilEvent = every;
            _onEvent?.Invoke();
            if (_eventsEndWaits) return;
            // An event that does not end the wait: only a wake made in it does.
            if (Volatile.Read(ref _wakePending))
            {
                WakesTaken++;
                return;
            }
        }
        if (infinite)
        {
            long now = _time.GetTimestamp();
            _time.Advance(TimeSpan.FromTicks(Math.Max(0, Horizon!.Value - now) + 1));
            return;
        }
        _time.Advance(wait);
    }

    /// <summary>Ends the next wait at once (the one in progress has already ended: a wait here is
    /// time moving on, not a thread parked). Taken by the next <see cref="PumpEvents"/>, as a real
    /// platform's wake event is.</summary>
    public void Wake()
    {
        Interlocked.Increment(ref _wakeCalls);
        Volatile.Write(ref _wakePending, true);
    }

    /// <summary>Gives back the text services the windows shared; the windows are gone by now.</summary>
    public void Dispose()
    {
        _text?.Dispose();
        _pinnedText?.Dispose();
    }
}

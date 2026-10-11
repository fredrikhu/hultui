using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace HultUI;

/// <summary>What asked the frame loop to wake (<see cref="FrameLog"/>).</summary>
internal enum WakeSource
{
    /// <summary><see cref="Application.InvokeOnMainThread"/>.</summary>
    Post,
    /// <summary>An <c>await</c> continuation, through the UI thread's synchronization context.</summary>
    Await,
    /// <summary>Work done on the UI thread inside the loop's wait (<c>UIThread.Park</c>).</summary>
    Parked,
    /// <summary>An action from assistive technology, queued from its thread.</summary>
    A11yAction,
    /// <summary>A bridge's client attached, left, or switched assistive technology on or off.</summary>
    A11yClient,
    Count,
}

/// <summary>What an idle wait of the frame loop waited for (<see cref="FrameLog"/>).</summary>
internal enum IdleDeadline
{
    /// <summary>Nothing: an event or a wake. For a paced wait, the display's interval.</summary>
    None,
    /// <summary>An animation's next step.</summary>
    Animation,
    /// <summary>The trailing refresh of a coalesced layout change's accessibility bounds.</summary>
    A11yLayout,
    /// <summary>The trailing refresh of a range value told to accessibility.</summary>
    A11yRange,
    /// <summary>A resize the throttle held back.</summary>
    Resize,
    /// <summary>The DevTools inspector's throttled refresh.</summary>
    Inspector,
    /// <summary>A display-colour poll of a Vulkan window without display-colour events.</summary>
    DisplayColor,
    Count,
}

/// <summary>
/// The frame loop's own log, for measuring what an application does between frames: how often
/// the loop passes, renders, ticks animations and waits, what ended each wait early, what was
/// posted to the UI thread and what woke the loop, the input events, and the UI thread's bytes,
/// garbage collections, managed heap and private bytes. Off unless <c>HULTUI_FRAMELOG</c> is set:
/// <c>1</c> writes to stderr, any other value is a file appended to (the apps are GUI binaries).
/// One line every <c>HULTUI_FRAMELOG_SECS</c> seconds (default 5) of the platform's clock, and
/// one when the loop ends.
///
/// <para>Off, it costs one static bool read at each place it counts. It counts on the UI thread
/// without locks; only posts and wakes, which come from any thread, are counted atomically.</para>
/// </summary>
internal static class FrameLog
{
    private static readonly string? Sink = Environment.GetEnvironmentVariable("HULTUI_FRAMELOG");

    /// <summary>True when <c>HULTUI_FRAMELOG</c> is set.</summary>
    internal static readonly bool On = !string.IsNullOrEmpty(Sink);

    private static readonly double ReportSeconds =
        double.TryParse(Environment.GetEnvironmentVariable("HULTUI_FRAMELOG_SECS"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var s) && s > 0 ? s : 5.0;

    // UI thread.
    private static long _passes, _frames, _ticks, _inputs, _inputBytes;
    private static long _framePasses, _frameBytes, _otherBytes;
    private static long _idleWaits, _idleEarly, _idleTicks, _pacedWaits, _pacedEarly, _pacedTicks;
    private static readonly long[] _deadlines = new long[(int)IdleDeadline.Count];
    private static long _origin, _windowStart, _bytesAtStart;
    private static int _gen0, _gen1, _gen2;
    private static int _reports;
    private static TimeProvider? _clock;

    // Any thread.
    private static long _posts;
    private static readonly long[] _wakes = new long[(int)WakeSource.Count];

    private static long BytesNow() => GC.GetAllocatedBytesForCurrentThread();

    /// <summary>A pass of the loop begins; reports when the interval is up.</summary>
    internal static void PassStart(Application app, TimeProvider clock)
    {
        long now = clock.GetTimestamp();
        // A new clock is a new application (a test host runs one session after another, each on
        // its own virtual clock): the interval starts again on it.
        if (_windowStart == 0 || !ReferenceEquals(clock, _clock))
        {
            _clock = clock;
            _origin = now;
            Restart(now);
            return;
        }
        if (clock.GetElapsedTime(_windowStart, now).TotalSeconds >= ReportSeconds)
            Report(app, now);
    }

    /// <summary>A pass ended, having rendered or not, and allocated <paramref name="bytes"/> on
    /// the UI thread.</summary>
    internal static void PassEnd(bool rendered, bool ticked, long bytes)
    {
        _passes++;
        if (ticked) _ticks++;
        if (rendered) { _framePasses++; _frameBytes += bytes; }
        else _otherBytes += bytes;
    }

    /// <summary>A window drew a frame.</summary>
    internal static void Frame() => _frames++;

    /// <summary>An input event was handled, allocating <paramref name="bytes"/>.</summary>
    internal static void Input(long bytes)
    {
        _inputs++;
        _inputBytes += bytes;
    }

    /// <summary>Work was queued for the UI thread.</summary>
    internal static void Post() => Interlocked.Increment(ref _posts);

    /// <summary>The loop was asked to wake, for <paramref name="source"/>.</summary>
    internal static void Wake(WakeSource source) => Interlocked.Increment(ref _wakes[(int)source]);

    /// <summary>
    /// The loop waited: idle (nothing to draw) for <paramref name="deadline"/>, or paced (a frame
    /// waiting for the display's interval). <paramref name="requestedMs"/> is what it asked for,
    /// <see cref="Timeout.Infinite"/> for no limit; <paramref name="tookTicks"/> what it took on
    /// the platform's clock. A wait that returned before nine tenths of what it asked was ended
    /// early, by an event or a wake.
    /// </summary>
    internal static void Waited(bool idle, IdleDeadline deadline, int requestedMs, long tookTicks)
    {
        var clock = _clock;
        double took = clock is null ? 0 : tookTicks * 1000.0 / clock.TimestampFrequency;
        bool early = requestedMs == Timeout.Infinite || took < requestedMs * 0.9;
        if (idle)
        {
            _idleWaits++;
            _idleTicks += tookTicks;
            _deadlines[(int)deadline]++;
            if (early) _idleEarly++;
        }
        else
        {
            _pacedWaits++;
            _pacedTicks += tookTicks;
            if (early) _pacedEarly++;
        }
    }

    /// <summary>Writes what is left: the loop has ended.</summary>
    internal static void LoopEnded(Application app)
    {
        if (_clock is { } clock && _windowStart != 0)
            Report(app, clock.GetTimestamp());
    }

    private static void Restart(long now)
    {
        _windowStart = now;
        _bytesAtStart = BytesNow();
        _gen0 = GC.CollectionCount(0);
        _gen1 = GC.CollectionCount(1);
        _gen2 = GC.CollectionCount(2);
        _passes = _frames = _ticks = _inputs = _inputBytes = 0;
        _framePasses = _frameBytes = _otherBytes = 0;
        _idleWaits = _idleEarly = _idleTicks = _pacedWaits = _pacedEarly = _pacedTicks = 0;
        Array.Clear(_deadlines);
        Interlocked.Exchange(ref _posts, 0);
        for (int i = 0; i < _wakes.Length; i++) Interlocked.Exchange(ref _wakes[i], 0);
    }

    private static void Report(Application app, long now)
    {
        var clock = _clock!;
        double secs = clock.GetElapsedTime(_windowStart, now).TotalSeconds;
        if (secs <= 0) return;
        long bytes = BytesNow() - _bytesAtStart;
        double freq = clock.TimestampFrequency;
        int windows = 0, minimized = 0;
        foreach (var w in app.Windows)
        {
            if (!w.IsOpen) continue;
            windows++;
            if (w.PlatformWindow.IsMinimized) minimized++;
        }
        long priv;
        using (var p = Process.GetCurrentProcess()) priv = p.PrivateMemorySize64;
        long otherPasses = _passes - _framePasses;

        var b = new StringBuilder(512);
        var c = CultureInfo.InvariantCulture;
        b.Append(c, $"report={++_reports} t={clock.GetElapsedTime(_origin, now).TotalSeconds:F1} dt={secs:F2}");
        b.Append(c, $" passes/s={_passes / secs:F1} frames/s={_frames / secs:F1} ticks/s={_ticks / secs:F1}");
        b.Append(c, $" idle/s={_idleWaits / secs:F1} idle-early/s={_idleEarly / secs:F1}");
        b.Append(c, $" paced/s={_pacedWaits / secs:F1} paced-early/s={_pacedEarly / secs:F1}");
        b.Append(c, $" parked-ms/s={(_idleTicks + _pacedTicks) * 1000.0 / freq / secs:F0}");
        for (int i = 0; i < _deadlines.Length; i++)
            if (_deadlines[i] > 0) b.Append(c, $" idle.{Name((IdleDeadline)i)}/s={_deadlines[i] / secs:F1}");
        long wakes = 0;
        for (int i = 0; i < _wakes.Length; i++) wakes += Interlocked.Read(ref _wakes[i]);
        b.Append(c, $" posts/s={Interlocked.Read(ref _posts) / secs:F1} wakes/s={wakes / secs:F1}");
        for (int i = 0; i < _wakes.Length; i++)
        {
            long n = Interlocked.Read(ref _wakes[i]);
            if (n > 0) b.Append(c, $" wake.{Name((WakeSource)i)}/s={n / secs:F1}");
        }
        b.Append(c, $" inputs/s={_inputs / secs:F1} B/input={(_inputs > 0 ? _inputBytes / (double)_inputs : 0):F0}");
        b.Append(c, $" B/frame-pass={(_framePasses > 0 ? _frameBytes / (double)_framePasses : 0):F0}");
        b.Append(c, $" B/other-pass={(otherPasses > 0 ? _otherBytes / (double)otherPasses : 0):F1}");
        b.Append(c, $" UI-B/s={bytes / secs:F0}");
        b.Append(c, $" gc={GC.CollectionCount(0) - _gen0}/{GC.CollectionCount(1) - _gen1}/{GC.CollectionCount(2) - _gen2}");
        b.Append(c, $" heap-MB={GC.GetTotalMemory(false) / 1048576.0:F2} private-MB={priv / 1048576.0:F1}");
        b.Append(c, $" windows={windows} minimized={minimized}");
        Write(b.ToString());

        // The report's own bytes do not count against the next interval.
        Restart(clock.GetTimestamp());
    }

    private static string Name(IdleDeadline d) => d switch
    {
        IdleDeadline.None => "none",
        IdleDeadline.Animation => "animation",
        IdleDeadline.A11yLayout => "a11y-layout",
        IdleDeadline.A11yRange => "a11y-range",
        IdleDeadline.Resize => "resize",
        IdleDeadline.Inspector => "inspector",
        IdleDeadline.DisplayColor => "display-colour",
        _ => "?",
    };

    private static string Name(WakeSource s) => s switch
    {
        WakeSource.Post => "post",
        WakeSource.Await => "await",
        WakeSource.Parked => "parked",
        WakeSource.A11yAction => "a11y-action",
        WakeSource.A11yClient => "a11y-client",
        _ => "?",
    };

    private static void Write(string line)
    {
        try
        {
            if (Sink == "1") Console.Error.WriteLine(line);
            else File.AppendAllText(Sink!, line + Environment.NewLine);
        }
        catch (IOException)
        {
            // A log that cannot be written must never take the app down with it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

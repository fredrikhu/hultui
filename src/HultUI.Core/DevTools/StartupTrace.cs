using System.Diagnostics;

namespace HultUI.Core.DevTools;

/// <summary>
/// Phase timing for process start-up, from before any window exists to the first
/// presented frame. <see cref="FrameTracer"/> can't cover this: it belongs to a
/// Window, and the expensive part of start-up happens before there is one.
///
/// <para>Off unless <c>HULTUI_STARTUP</c> is set. <c>HULTUI_STARTUP=1</c> writes to
/// stderr; any other value is taken as a file path (the apps are GUI subsystem
/// binaries, so a launcher that isn't a console gets nothing from stderr).</para>
///
/// <para>Each line carries the delta since the previous mark and the absolute time
/// since the OS started the process — the latter includes runtime start-up before
/// <c>Main</c>, which no in-process stopwatch can see.</para>
/// </summary>
public static class StartupTrace
{
    private static readonly string? Sink = Environment.GetEnvironmentVariable("HULTUI_STARTUP");

    /// <summary>True when <c>HULTUI_STARTUP</c> is set. Check before building a
    /// detail string, so tracing costs nothing when it's off.</summary>
    public static readonly bool Enabled = !string.IsNullOrEmpty(Sink);

    private static readonly double ProcessStartMs = Enabled ? MeasureProcessStart() : 0;
    private static readonly long Origin = Stopwatch.GetTimestamp();
    private static readonly Lock Gate = new();
    private static long _last = Origin;

    private static double MeasureProcessStart()
    {
        try
        {
            return (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        }
        catch
        {
            // StartTime is unavailable on some sandboxed/containerised hosts.
            return 0;
        }
    }

    /// <summary>Records a phase boundary. No-op unless <see cref="Enabled"/>.</summary>
    public static void Mark(string name)
    {
        if (!Enabled) return;

        long now = Stopwatch.GetTimestamp();
        string line;
        lock (Gate)
        {
            double delta = Ms(now - _last);
            double sinceProcessStart = ProcessStartMs + Ms(now - Origin);
            _last = now;
            line = $"[startup] {name,-28} +{delta,7:F1} ms   t={sinceProcessStart,7:F1} ms";
        }

        if (Sink == "1")
            Console.Error.WriteLine(line);
        else
            AppendToFile(line);
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static void AppendToFile(string line)
    {
        try
        {
            File.AppendAllText(Sink!, line + Environment.NewLine);
        }
        catch (IOException)
        {
            // A trace that can't be written must never take the app down with it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

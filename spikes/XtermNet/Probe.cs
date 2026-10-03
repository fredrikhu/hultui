using System.Diagnostics;
using System.Text;
using XTerm;
using XTerm.Options;

namespace XtermNet;

/// <summary>Helpers every question shares: logging, escape display, a check table, timing.</summary>
internal static class Probe
{
    public static void Log(string line) => Console.WriteLine(line);

    /// <summary>Makes control bytes readable: ESC as <c>\e</c>, the rest as <c>^X</c> or <c>\xNN</c>.</summary>
    public static string Show(string? s)
    {
        if (s is null) return "(null)";
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (c == '\x1b') sb.Append(@"\e");
            else if (c == '\r') sb.Append(@"\r");
            else if (c == '\n') sb.Append(@"\n");
            else if (c == '\x7f') sb.Append("^?");
            else if (c < 0x20) sb.Append('^').Append((char)(c + 64));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>A terminal with every reply it sends captured, as a pty host would forward them.</summary>
    public static (Terminal Term, List<string> Replies) NewTerminal(int cols = 80, int rows = 24, int scrollback = 1000)
    {
        var t = new Terminal(new TerminalOptions { Cols = cols, Rows = rows, Scrollback = scrollback });
        var replies = new List<string>();
        t.DataReceived += (_, e) => replies.Add(e.Data);
        return (t, replies);
    }

    /// <summary>The visible screen, one line per row, right-trimmed, then where the cursor is. (Marking the
    /// cursor inside the text would put it in the wrong place on any row with a wide character, since
    /// columns and string indices part company there.)</summary>
    public static string Screen(Terminal t)
    {
        var b = t.Buffer;
        var sb = new StringBuilder();
        for (var row = 0; row < t.Rows; row++)
            sb.Append($"{row,3}|").AppendLine(b.Lines[b.YDisp + row]?.TranslateToString(trimRight: true) ?? "");
        sb.Append($"    cursor col {b.X} row {b.Y}, {(t.CursorVisible ? "visible" : "hidden")}, "
            + $"{(t.IsAlternateBufferActive ? "alternate" : "normal")} buffer, {t.Cols}x{t.Rows}");
        return sb.ToString();
    }

    /// <summary>Times <paramref name="op"/> until it has run for <paramref name="seconds"/>, after warming it
    /// to convergence: XTerm.NET's own notes measured 3x understatement from timing before tier-1.</summary>
    public static (double NsPerOp, double BytesPerOp, long Ops) Time(Action op, double seconds = 1.0, int minOps = 3)
    {
        // Warm: repeat whole passes until two consecutive ones agree within 10 %, at most 12 passes.
        double last = double.MaxValue;
        for (var pass = 0; pass < 12; pass++)
        {
            var sw0 = Stopwatch.StartNew();
            var n0 = 0;
            while (sw0.Elapsed.TotalSeconds < seconds / 4 || n0 < minOps) { op(); n0++; }
            var per = sw0.Elapsed.TotalNanoseconds / n0;
            if (Math.Abs(per - last) / per < 0.10) break;
            last = per;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        long n = 0;
        while (sw.Elapsed.TotalSeconds < seconds || n < minOps) { op(); n++; }
        sw.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        return (sw.Elapsed.TotalNanoseconds / n, (double)bytes / n, n);
    }

    /// <summary>The first <paramref name="name"/> on PATH, or among <paramref name="fallbacks"/>.</summary>
    public static string? Which(string name, params string[] fallbacks)
    {
        var exts = OperatingSystem.IsWindows() ? new[] { ".exe", "" } : new[] { "" };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            foreach (var ext in exts)
            {
                var p = Path.Combine(dir, name + ext);
                if (File.Exists(p)) return p;
            }
        return fallbacks.FirstOrDefault(File.Exists);
    }

    public static string Stats(List<double> v, string unit = "ms") =>
        v.Count == 0 ? "none" : $"p50 {Percentile(v, 50):0.00} / p95 {Percentile(v, 95):0.00} / max {v.Max():0.00} {unit} (n={v.Count})";

    public static double Percentile(List<double> values, double p)
    {
        if (values.Count == 0) return double.NaN;
        var sorted = values.Order().ToList();
        var i = (int)Math.Clamp(Math.Ceiling(p / 100.0 * sorted.Count) - 1, 0, sorted.Count - 1);
        return sorted[i];
    }
}

/// <summary>A pass/fail table: each row says what was asked, what came back, and whether that is right.</summary>
internal sealed class Checks
{
    private readonly List<(string Name, bool Ok, string Observed)> _rows = [];

    public void Add(string name, Func<(bool Ok, string Observed)> check)
    {
        try
        {
            var (ok, observed) = check();
            _rows.Add((name, ok, observed));
        }
        catch (Exception ex)
        {
            _rows.Add((name, false, $"threw {ex.GetType().Name}: {ex.Message}"));
        }
    }

    public int Print()
    {
        var width = _rows.Max(r => r.Name.Length);
        foreach (var (name, ok, observed) in _rows)
            Probe.Log($"{(ok ? "PASS" : "FAIL")}  {name.PadRight(width)}  {observed}");
        var failed = _rows.Count(r => !r.Ok);
        Probe.Log($"{_rows.Count - failed}/{_rows.Count} passed");
        return failed;
    }
}

using System.Text;
using XTerm;
using XTerm.Options;
using static XtermNet.Probe;

namespace XtermNet;

/// <summary>
/// Question 4: how fast it parses on this machine and what the scrollback costs in memory.
/// Throughput through <c>Write(ReadOnlySpan&lt;byte&gt;)</c>, the pty entry, warmed to convergence;
/// allocation per byte; then the managed heap a scrollback of N lines at C columns holds, because
/// a 24-byte cell makes that a real number at 200 columns.
/// </summary>
internal static class Q4
{
    private const string E = "\x1b";

    public static int Run(string[] args)
    {
        // Memory in a process of its own: the throughput half leaves corpora and terminals behind
        // that a full collect does not always return before the next baseline is taken.
        if (args.FirstOrDefault() == "memory") return Memory();
        var seconds = args.Length > 0 ? double.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 2.0;
        Log($"throughput, 200x50 terminal, 10 000 scrollback, Write(bytes) in 64 KiB chunks, {seconds} s per corpus after warm-up");
        foreach (var (name, corpus) in Corpora())
        {
            var bytes = Encoding.UTF8.GetBytes(corpus);
            var t = new Terminal(new TerminalOptions { Cols = 200, Rows = 50, Scrollback = 10_000 });
            var (ns, alloc, ops) = Time(() =>
            {
                for (var off = 0; off < bytes.Length; off += 65536)
                    t.Write(bytes.AsSpan(off, Math.Min(65536, bytes.Length - off)));
            }, seconds);
            var mibs = bytes.Length / (ns / 1e9) / (1024 * 1024);
            Log($"  {name,-12} {bytes.Length / 1024.0 / 1024.0,6:0.0} MiB/pass  {mibs,7:0.0} MiB/s  {alloc / bytes.Length,8:0.000} B alloc/byte  ({ops} passes)");
        }
        return 0;
    }

    private static int Memory()
    {
        Log("managed heap held by a full scrollback (GC.GetTotalMemory after a full collect); lines are ~15 chars of text");
        foreach (var (cols, lines) in new[] { (80, 10_000), (200, 10_000), (200, 100_000), (300, 10_000) })
        {
            var baseline = Heap();
            var t = new Terminal(new TerminalOptions { Cols = cols, Rows = 50, Scrollback = lines });
            var empty = Heap() - baseline;
            var sb = new StringBuilder();
            for (var i = 0; i < lines + 50; i++)
            {
                sb.Append($"{i} short line\r\n");
                if (sb.Length > 1 << 20) { t.Write(sb.ToString()); sb.Clear(); }
            }
            t.Write(sb.ToString());
            var full = Heap() - baseline;
            Log($"  {cols,3} cols x {lines,7} lines: empty terminal {empty / 1024.0 / 1024.0,7:0.0} MiB, full {full / 1024.0 / 1024.0,7:0.0} MiB"
                + $" = {full / (double)(lines + 50),7:0} B/line = {full / (double)(lines + 50) / cols,5:0.0} B/cell");
            GC.KeepAlive(t);
        }
        return 0;
    }

    private static long Heap()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        return GC.GetTotalMemory(true);
    }

    /// <summary>Deterministic corpora, the shapes XTerm.NET's own bench uses plus a pty-sized flood.</summary>
    public static IEnumerable<(string Name, string Text)> Corpora()
    {
        var rng = new Random(42);
        string Word() => new string((char)('a' + rng.Next(26)), 2 + rng.Next(9));

        var sb = new StringBuilder();
        // Plain scrolling text: a build log, cat of a source file.
        while (sb.Length < 8 << 20)
        {
            var n = rng.Next(12);
            for (var i = 0; i < n; i++) sb.Append(Word()).Append(' ');
            sb.Append("\r\n");
        }
        yield return ("scroll-ascii", sb.ToString());

        // SGR per word: ls --color, a compiler's coloured diagnostics, a syntax-highlighted pager.
        sb.Clear();
        while (sb.Length < 8 << 20)
        {
            var n = rng.Next(12);
            for (var i = 0; i < n; i++)
            {
                sb.Append(rng.Next(3) switch
                {
                    0 => $"{E}[1;3{rng.Next(8)}m",
                    1 => $"{E}[38;5;{rng.Next(256)}m",
                    _ => $"{E}[38;2;{rng.Next(256)};{rng.Next(256)};{rng.Next(256)}m",
                }).Append(Word()).Append($"{E}[0m ");
            }
            sb.Append("\r\n");
        }
        yield return ("sgr-churn", sb.ToString());

        // Full-screen redraws: an editor or htop repainting 200x50 with cursor moves.
        sb.Clear();
        while (sb.Length < 8 << 20)
        {
            sb.Append($"{E}[?2026h");
            for (var row = 1; row <= 50; row++)
            {
                sb.Append($"{E}[{row};1H{E}[48;5;{234 + row % 4}m{E}[38;5;{rng.Next(256)}m");
                sb.Append('│').Append(Word()).Append(' ').Append(Word()).Append(' ', rng.Next(60)).Append('│');
                sb.Append($"{E}[K");
            }
            sb.Append($"{E}[?2026l");
        }
        yield return ("alt-redraw", sb.ToString());

        // Unicode: CJK, emoji, combining marks, box drawing.
        sb.Clear();
        string[] bits = ["東京", "日本語", "한국어", "é", "ñ", "á", "👍", "👍🏽", "🇸🇪", "👨‍👩‍👧", "─", "┼", "│", "Ω", "λ"];
        while (sb.Length < 8 << 20)
        {
            var n = rng.Next(16);
            for (var i = 0; i < n; i++) sb.Append(rng.Next(3) == 0 ? bits[rng.Next(bits.Length)] : Word()).Append(' ');
            sb.Append("\r\n");
        }
        yield return ("unicode", sb.ToString());
    }
}

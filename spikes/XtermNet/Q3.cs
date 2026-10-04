using System.Diagnostics;
using System.Text;
using XTerm;
using static XtermNet.Probe;

namespace XtermNet;

/// <summary>
/// Question 3: reflow and the cost of a resize. A HultUI element resizes the grid every frame of
/// a window drag (and the drag runs inside the OS's modal loop, where work cannot be deferred), so
/// reflow has to be both right and cheap. Right: wrapped lines re-wrap on narrowing and rejoin on
/// widening, the cursor stays with its text, a wide character never splits. Cheap: the time for a
/// one-column change with a full scrollback.
/// </summary>
internal static class Q3
{
    private const string E = "\x1b";

    /// <summary>The two cases that fail, shown screen by screen.</summary>
    public static int Detail()
    {
        var (t, _) = NewTerminal(80, 6);
        t.Write(new string('x', 100) + "$ cmd");
        Log("105 chars at 80 cols:"); Log(Screen(t));
        t.Resize(50, 6);
        Log("after Resize(50, 6):"); Log(Screen(t));
        t.Write("!");
        Log("after writing '!' (where the shell's next echo lands):"); Log(Screen(t));
        t.Resize(80, 6);
        Log("after Resize(80, 6):"); Log(Screen(t));

        (t, _) = NewTerminal(81, 4);
        t.Write(new string('a', 79) + "東東");
        Log("79 a + 東東 at 81 cols:"); Log(Screen(t));
        t.Resize(80, 4);
        Log("after Resize(80, 4):"); Log(Screen(t));
        var l0 = t.Buffer.Lines[t.Buffer.YBase]!;
        Log($"row0 Length {l0.Length}, [78] '{l0[78].Content}' w{l0[78].Width}, [79] '{l0[79].Content}' w{l0[79].Width}");
        return 0;
    }

    public static int Run()
    {
        var c = new Checks();

        c.Add("narrow re-wraps a long line, widen rejoins it", () =>
        {
            var (t, _) = NewTerminal(80, 10);
            var text = string.Concat(Enumerable.Range(0, 20).Select(i => $"{i:00}-abcdefg"));   // 200 chars
            t.Write(text + "\r\nnext");
            t.Resize(40, 10);
            var narrow = Logical(t);
            var rows40 = RowsOf(t, text[..10]);
            t.Resize(80, 10);
            var wide = Logical(t);
            var ok = narrow.Contains(text) && wide.Contains(text) && rows40 == 5 && Rows(t).Count(r => r.Length > 0) == 4;
            return (ok, $"at 40 cols: {rows40} rows; text intact narrow={narrow.Contains(text)} wide={wide.Contains(text)}; rows used at 80: {Rows(t).Count(r => r.Length > 0)}");
        });
        // xterm.js's rule, ported: the logical line holding the cursor is NOT reflowed ("the
        // program will handle fixing up wrapped lines with the cursor"); its rows are cut to the
        // new width. A shell redraws its prompt on SIGWINCH, so the cut is normally painted over;
        // Ghostty, kitty and Windows Terminal reflow it instead. Question 7 shows what pwsh does.
        c.Add("the cursor's own line is reflowed (xterm.js: no, it is cut)", () =>
        {
            var (t, _) = NewTerminal(80, 10);
            t.Write(new string('x', 100) + "$ cmd");
            var before = (t.Buffer.X, t.Buffer.Y);
            t.Resize(50, 10);
            var mid = (t.Buffer.X, t.Buffer.Y);
            var kept = Logical(t).Count(ch => ch == 'x');
            // 105 chars: at 80 cols the cursor is (25,1); reflowed at 50 it would be (5,2).
            return (mid == (5, 2) && kept == 100, $"cursor 80:{before} 50:{mid}; x kept at 50 cols: {kept} of 100");
        });
        c.Add("wide char at the wrap point, cursor elsewhere", () =>
        {
            var (t, _) = NewTerminal(81, 5);
            t.Write(new string('a', 79) + "東東\r\nnext");         // 東 at cols 79-80 of 81, then wraps
            t.Resize(80, 5);                                       // now the first 東 cannot fit at col 79
            var l0 = t.Buffer.Lines[t.Buffer.YBase]!; var l1 = t.Buffer.Lines[t.Buffer.YBase + 1]!;
            return (l0[79].Width != 2 && l1[0].Content == "東" && l1[2].Content == "東",
                $"row0[79] '{l0[79].Content}' w{l0[79].Width}; row1 '{l1.TranslateToString(true)}'");
        });
        c.Add("wide char at the wrap point, cursor on that line", () =>
        {
            var (t, _) = NewTerminal(81, 5);
            t.Write(new string('a', 79) + "東東");
            t.Resize(80, 5);
            var l0 = t.Buffer.Lines[t.Buffer.YBase]!;
            // Cut, not reflowed: a width-2 cell left in the last column, its spacer gone. The
            // renderer has to clip it rather than trust Width.
            return (l0[79].Width != 2, $"row0[79] '{l0[79].Content}' w{l0[79].Width} (line length {l0.Length})");
        });
        c.Add("scrollback reflows, nothing lost", () =>
        {
            var (t, _) = NewTerminal(120, 30, scrollback: 2000);
            var lines = Enumerable.Range(0, 500).Select(i => $"{i:0000} " + new string((char)('a' + i % 26), 20 + i % 150)).ToArray();
            foreach (var l in lines) t.Write(l + "\r\n");
            t.Resize(60, 30);
            t.Resize(120, 30);
            var back = Logical(t);
            var missing = lines.Count(l => !back.Contains(l));
            return (missing == 0, $"{missing} of {lines.Length} lines missing after 120 -> 60 -> 120");
        });
        c.Add("alternate screen is not reflowed (vim redraws itself)", () =>
        {
            var (t, _) = NewTerminal(80, 10);
            t.Write($"{E}[?1049h" + new string('y', 100));
            t.Resize(40, 10);
            var row0 = t.GetLine(t.Buffer.YBase).TrimEnd();
            return (true, $"row0 at 40 cols: {row0.Length} chars, row1 '{t.GetLine(t.Buffer.YBase + 1).TrimEnd()}'");
        });
        c.Add("row-only resize keeps content and pulls scrollback down", () =>
        {
            var (t, _) = NewTerminal(80, 10, scrollback: 100);
            for (var i = 0; i < 30; i++) t.Write($"line {i}\r\n");
            t.Resize(80, 20);
            return (t.GetLine(t.Buffer.YBase).TrimEnd() == "line 11", $"top row after 10 -> 20 rows: '{t.GetLine(t.Buffer.YBase).TrimEnd()}' YBase={t.Buffer.YBase}");
        });
        c.Print();

        // ── Cost: a window drag, one column at a time, with a full scrollback ───────────────────
        Log("");
        Log("resize cost (window drag: width -1/+1 and height -1/+1 alternating; p50 / p95 / max over 200 resizes each)");
        foreach (var (cols, scrollback, fill) in new[] { (120, 1_000, "prose"), (120, 10_000, "prose"), (200, 10_000, "prose"), (200, 10_000, "short"), (200, 100_000, "prose") })
        {
            var (t, _) = NewTerminal(cols, 50, scrollback);
            var rng = new Random(1);
            var sb = new StringBuilder();
            for (var i = 0; i < scrollback + 50; i++)
            {
                var len = fill == "short" ? 10 + rng.Next(30) : 20 + rng.Next(cols * 2);   // prose wraps ~half its lines
                sb.Append($"{E}[3{rng.Next(8)}m").Append((char)('a' + rng.Next(26)), len).Append("\r\n");
                if (sb.Length > 1 << 20) { t.Write(sb.ToString()); sb.Clear(); }
            }
            t.Write(sb.ToString());

            var times = new List<double>();
            var sw = new Stopwatch();
            for (var i = 0; i < 200; i++)
            {
                var w = cols - 1 + (i & 1);   // cols-1, cols, cols-1, ...
                sw.Restart();
                t.Resize(w, 50);
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            // Height only, as a vertical drag does.
            t.Resize(cols, 50);
            var heights = new List<double>();
            for (var i = 0; i < 200; i++)
            {
                sw.Restart();
                t.Resize(cols, 49 + (i & 1));
                heights.Add(sw.Elapsed.TotalMilliseconds);
            }
            // And a big jump, as maximize does.
            sw.Restart(); t.Resize(cols / 2, 50); var half = sw.Elapsed.TotalMilliseconds;
            sw.Restart(); t.Resize(cols, 50); var back = sw.Elapsed.TotalMilliseconds;
            Log($"  {cols,3} cols, {scrollback,7} scrollback, {fill,-5}: width {Percentile(times, 50),7:0.00} / {Percentile(times, 95),7:0.00} / {times.Max(),7:0.00} ms;"
                + $"  height {Percentile(heights, 50),6:0.00} / {Percentile(heights, 95),6:0.00} / {heights.Max(),6:0.00} ms;"
                + $"  to {cols / 2} cols {half,6:0.0} ms, back {back,6:0.0} ms");
        }
        return 0;
    }

    /// <summary>The whole buffer as logical lines: wrapped rows joined, trailing blanks trimmed.</summary>
    private static string Logical(Terminal t)
    {
        var sb = new StringBuilder();
        var lines = t.Buffer.Lines;
        for (var i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l is null) continue;
            if (i > 0 && !l.IsWrapped) sb.Append('\n');
            sb.Append(l.TranslateToString(trimRight: !(i + 1 < lines.Length && lines[i + 1]?.IsWrapped == true)));
        }
        return sb.ToString();
    }

    private static List<string> Rows(Terminal t) =>
        Enumerable.Range(0, t.Rows).Select(r => t.GetLine(t.Buffer.YBase + r).TrimEnd()).ToList();

    private static int RowsOf(Terminal t, string start)
    {
        var lines = t.Buffer.Lines;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i]?.TranslateToString(true).StartsWith(start) != true) continue;
            var n = 1;
            while (i + n < lines.Length && lines[i + n]?.IsWrapped == true) n++;
            return n;
        }
        return 0;
    }
}

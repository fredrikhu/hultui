using System.Diagnostics;
using HultUI.Core.Layout;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using HultUI.Text;

// Text-pipeline micro-benchmark. Exists so a change to HultUI.Text can be shown
// not to cost anything on the paths it does not target — in particular so the
// BiDi work (docs/plans/14-bidi.md) can prove that Latin text still pays the
// price it paid before. Numbers are per operation: median and min nanoseconds
// over `samples` batches of `batch` calls, plus allocated bytes per operation.
//
//   dotnet run -c Release --project samples/HultUI.Samples.TextBench
//   dotnet run -c Release --project samples/HultUI.Samples.TextBench -- --csv

// Invariant culture so a decimal point is a decimal point wherever this runs and
// two captures can be diffed against each other.
System.Globalization.CultureInfo.DefaultThreadCurrentCulture =
    System.Globalization.CultureInfo.InvariantCulture;

// A benchmark competing with whatever else the machine is doing reads high, and
// round-robin sampling means every scenario is equally exposed to it. Raising the
// priority and reporting the MINIMUM sample is what makes two captures
// comparable: the median moves with the machine's mood, the minimum is the cost
// when nothing interfered.
try { System.Diagnostics.Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; }
catch (Exception) { /* not permitted here; the numbers are just noisier */ }

bool csv = args.Contains("--csv");
var scenarios = Scenarios.All().ToList();

// Global pre-warm across EVERY scenario before any measurement, the same
// discipline samples/HultUI.Samples.Benchmark uses. Without it the first
// scenarios absorb JIT tier-up and dynamic PGO for shared code — an LRU lookup's
// comparer calls devirtualize only once PGO has data — and the same cache hit
// reads 250 ns early in a run and 60 ns late in it. That is a five-fold swing
// with nothing to do with the code under test, and it is enough to invent
// regressions that are not there.
//
// Passes until the JIT has finished, not for a fixed time. With tiered PGO a method
// goes tier 0, then instrumented tier 0, then tier 1, and each step needs calls after
// the last: one pass left the caret rows (3 ns of work) at instrumented tier 0 for the
// whole measurement whenever the background compiler had a long queue — ten times
// their cost, in run after run, gone with DOTNET_TC_CallCountingDelayMs=0. So the
// passes go on for at least 3 s and then until the count of compiled methods has not
// risen for a second, or for 30 s at most. The JIT line under the table says how many
// methods were still compiled while the rows were measured: ~130 when this was written,
// rarely called ones (a cache's growth, a font object made) reaching their call count.
//
// And on the paths the rows are measured on. The passes used to call each row with the
// same few indices, every pass: a cold row's strings and a resize row's widths were
// new to its caches in the first pass only, so from the second on it measured cache
// hits, and its own path (shaping, the memo of a string's widths, the ellipsis search)
// was compiled while the rows were measured — ~1,000 methods. Now the passes run on a
// second set of the scenarios, with pipelines of their own, so they can follow the
// measurement's index sequence (a new width every batch, the caret's real offsets)
// without touching what is measured; a cold row gets a new string on every call.
var warm = Stopwatch.StartNew();
long compiledAtStart = System.Runtime.JitInfo.GetCompiledMethodCount();
int passes = WarmUpJit(warm);
long compiledInWarmUp = System.Runtime.JitInfo.GetCompiledMethodCount() - compiledAtStart;
double warmSeconds = warm.Elapsed.TotalSeconds;

// Then each measured row once with indices the measurement never uses, so it starts from
// the state it always started from: a cold row's caches holding a batch of strings it is
// not measured on, a resize row's labels already shaped at a width it is not measured at.
foreach (var s in scenarios)
    for (int i = 0; i < s.Batch; i++) s.Op(-1 - i);

var results = Bench.RunAll(scenarios, out long compiledInMeasurement);

if (csv)
{
    Console.WriteLine("scenario,median_ns,min_ns,bytes_per_op");
    foreach (var r in results)
        Console.WriteLine($"{r.Name},{r.MedianNs:F1},{r.MinNs:F1},{r.BytesPerOp:F0}");
}
else
{
    Console.WriteLine();
    Console.WriteLine("=== HultUI text pipeline ===");
    Console.WriteLine($"{"scenario",-34}{"min",12}{"median",12}{"alloc/op",12}");
    string? group = null;
    foreach (var r in results)
    {
        string g = r.Name.Split('/')[0];
        if (g != group) { Console.WriteLine(); group = g; }
        Console.WriteLine($"{r.Name,-34}{Fmt(r.MinNs),12}{Fmt(r.MedianNs),12}{r.BytesPerOp,10:N0} B");
    }
    Console.WriteLine();
}

(csv ? Console.Error : Console.Out).WriteLine(
    $"JIT: warm-up {passes} passes in {warmSeconds:F1} s compiled {compiledInWarmUp:N0} methods; " +
    $"{compiledInMeasurement:N0} compiled during measurement");

static string Fmt(double ns) => ns < 1_000 ? $"{ns:F0} ns" : $"{ns / 1000:F2} µs";

// The JIT warm-up's passes, on scenarios of its own (their pipelines are garbage once this
// returns, before the measurement's collection). A row is called with the indices it is
// measured with, pass after pass; a cold row, whose strings are numbered, with a new
// negative one every call instead, a string it has not met.
static int WarmUpJit(Stopwatch warm)
{
    var scenarios = Scenarios.All().ToList();
    long compiled = System.Runtime.JitInfo.GetCompiledMethodCount();
    var lastCompiled = warm.Elapsed;
    int passes = 0;
    do
    {
        foreach (var s in scenarios)
            for (int i = 0; i < s.Batch; i++)
            {
                int index = passes * s.Batch + i;
                s.Op(s.Cold ? -1 - index : index);
            }
        passes++;
        long now = System.Runtime.JitInfo.GetCompiledMethodCount();
        if (now != compiled)
        {
            compiled = now;
            lastCompiled = warm.Elapsed;
        }
    }
    while (warm.Elapsed < TimeSpan.FromSeconds(30)
        && (passes < 2 || warm.Elapsed < TimeSpan.FromSeconds(3) || warm.Elapsed - lastCompiled < TimeSpan.FromSeconds(1)));
    return passes;
}

readonly record struct Result(string Name, double MedianNs, double MinNs, double BytesPerOp);

/// <summary>One measured operation: <paramref name="Op"/> is called with a
/// monotonically increasing index so scenarios can drive unique inputs, and with negative
/// ones before the measurement. <paramref name="Cold"/>: every index is an input never seen
/// before, and only so many are made for the measurement, so the JIT warm-up hands it a new
/// negative one on every call.</summary>
sealed record Scenario(string Name, int Batch, Action<int> Op, bool Cold = false);

static class Bench
{
    public const int Samples = 25;

    /// <summary>
    /// Measures every scenario ROUND ROBIN: sample n of each before sample n+1 of
    /// any. Straight sequential measurement lets whatever drifts over the run —
    /// remaining JIT tier-up, heap growth, CPU clock — land entirely on whichever
    /// scenarios ran first. Interleaving spreads it across all of them, and the
    /// paired warm/recheck rows below make the residual visible.
    /// </summary>
    public static List<Result> RunAll(List<Scenario> scenarios, out long compiledMethods)
    {
        var times = new double[scenarios.Count][];
        var indices = new int[scenarios.Count];
        for (int i = 0; i < scenarios.Count; i++) times[i] = new double[Samples];

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var allocBefore = new long[scenarios.Count];
        var allocated = new long[scenarios.Count];

        long compiledBefore = System.Runtime.JitInfo.GetCompiledMethodCount();
        for (int sample = 0; sample < Samples; sample++)
        {
            for (int i = 0; i < scenarios.Count; i++)
            {
                var s = scenarios[i];
                allocBefore[i] = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int n = 0; n < s.Batch; n++) s.Op(indices[i]++);
                long end = Stopwatch.GetTimestamp();
                allocated[i] += GC.GetAllocatedBytesForCurrentThread() - allocBefore[i];
                times[i][sample] = (end - start) * 1_000_000_000.0 / Stopwatch.Frequency / s.Batch;
            }
        }
        compiledMethods = System.Runtime.JitInfo.GetCompiledMethodCount() - compiledBefore;

        var results = new List<Result>(scenarios.Count);
        for (int i = 0; i < scenarios.Count; i++)
        {
            Array.Sort(times[i]);
            results.Add(new Result(scenarios[i].Name, times[i][Samples / 2], times[i][0],
                (double)allocated[i] / (scenarios[i].Batch * (long)Samples)));
        }
        return results;
    }
}

static class Scenarios
{
    private const string Latin =
        "The quick brown fox jumps over the lazy dog near the riverbank today";
    private const string Hebrew =
        "שלום עולם אבג דהו זחט";
    private const string Mixed =
        "commit שלום merged by אבג into main";

    private static ResolvedInheritedStyle Style(TextDirection direction = TextDirection.Ltr) => new(
        FontFamily: "sans-serif", FontSize: 14f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: direction, Cursor: CursorType.Default);

    private static TextPipeline Pipeline() => new(new SkiaFontCollection(), new SkiaTextShaper());

    // A long Latin paragraph: the case where the "does this text need UAX #9?"
    // classifier has the most characters to scan on every single call.
    private static string Paragraph(int words)
    {
        var w = Latin.Split(' ');
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < words; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(w[i % w.Length]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The strings a cold row lays out, each one the pipeline has not seen. Letters only —
    /// no digits, so the digit fast path is not what is being measured. From index 0, one per
    /// measured call, made in advance so nothing is allocated inside the timed region; past
    /// the end it throws rather than wrap, since a "cold" row that wrapped would quietly start
    /// hitting the layout cache and report a fraction of its cost. A negative index is the
    /// warm-up's: a string made there and then, new for every index and never one of the
    /// measured ones.
    /// </summary>
    private sealed class ColdTexts(string template, int batch)
    {
        private readonly string[] _measured = Unique(template, batch * Bench.Samples);

        public string this[int i] => i >= 0 ? _measured[i] : template + ' ' + Alpha(_measured.Length - 1 - i);

        private static string[] Unique(string template, int count)
        {
            var result = new string[count];
            for (int i = 0; i < count; i++)
                result[i] = template + ' ' + Alpha(i);
            return result;
        }
    }

    private static string Alpha(int n)
    {
        Span<char> buf = stackalloc char[6];
        int i = buf.Length;
        n = Math.Abs(n) + 1;
        while (n > 0) { buf[--i] = (char)('a' + n % 26); n /= 26; }
        return new string(buf[i..]);
    }

    public static IEnumerable<Scenario> All()
    {
        var ltr = Style();
        var rtl = Style(TextDirection.Rtl);
        var auto = Style(TextDirection.Auto);

        // ── warm: the same string laid out over and over, the steady state a
        // scrolling list reaches. Everything downstream of the cache lookup is
        // already paid for, so what is left is the lookup and whatever runs
        // before it (the BiDi classifier scans the whole string, every call).
        {
            var p = Pipeline();
            string paragraph = Paragraph(700);
            yield return new("warm/latin-67", 2000, _ => p.Layout(Latin, ltr, 10_000, null));
            yield return new("warm/latin-4700", 2000, _ => p.Layout(paragraph, ltr, 600, null));
            yield return new("warm/mixed-38", 2000, _ => p.Layout(Mixed, ltr, 10_000, null));
            yield return new("warm/hebrew-25", 2000, _ => p.Layout(Hebrew, rtl, 10_000, null));
        }

        // ── cold: every call is a string the pipeline has never seen, the case a
        // freshly built list of labels hits. Shaping dominates; this is where the
        // BiDi path's real cost shows.
        {
            var p = Pipeline();
            var texts = new ColdTexts(Latin, batch: 200);
            yield return new("cold/latin-67", 200, i => p.Layout(texts[i], ltr, 10_000, null), Cold: true);
        }
        {
            var p = Pipeline();
            var texts = new ColdTexts(Paragraph(60), batch: 40);
            yield return new("cold/latin-wrapped", 40, i => p.Layout(texts[i], ltr, 300, null), Cold: true);
        }
        {
            var p = Pipeline();
            var texts = new ColdTexts(Mixed, batch: 200);
            yield return new("cold/mixed-38", 200, i => p.Layout(texts[i], ltr, 10_000, null), Cold: true);
        }
        {
            var p = Pipeline();
            var texts = new ColdTexts(Hebrew, batch: 200);
            yield return new("cold/hebrew-25", 200, i => p.Layout(texts[i], rtl, 10_000, null), Cold: true);
        }

        // Auto direction (stage 3): Latin text must not notice it exists — the
        // classifier still answers before ICU does — and mixed text pays the same
        // analysis it already paid, now also reporting what it resolved to.
        {
            var p = Pipeline();
            var latinTexts = new ColdTexts(Latin, batch: 200);
            var mixedTexts = new ColdTexts(Mixed, batch: 200);
            yield return new("auto/latin-cold", 200, i => p.Layout(latinTexts[i], auto, 10_000, null), Cold: true);
            yield return new("auto/mixed-cold", 200, i => p.Layout(mixedTexts[i], auto, 10_000, null), Cold: true);
        }

        // ── resize: strings already shaped, laid out at a width the layout cache
        // has not seen — the steady state of a window or pane being dragged. 100
        // labels × 40 widths is more keys than the width-keyed cache holds, so
        // every call misses it while the shape and word caches stay warm.
        {
            var p = Pipeline();
            var labels = Labels(100, words: 14);
            yield return new("resize/ellipsis-1line", 100,
                i => p.Layout(labels[Idx(i, 100)], ltr, ResizeWidth(i), 1, TextOverflow.Ellipsis));
        }
        {
            var p = Pipeline();
            var labels = Labels(100, words: 30);
            yield return new("resize/ellipsis-2lines", 100,
                i => p.Layout(labels[Idx(i, 100)], ltr, ResizeWidth(i), 2, TextOverflow.Ellipsis));
        }
        {
            var p = Pipeline();
            var labels = Labels(100, words: 14);
            yield return new("resize/clip-1line", 100,
                i => p.Layout(labels[Idx(i, 100)], ltr, ResizeWidth(i), 1, TextOverflow.Clip));
        }
        {
            var p = Pipeline();
            var labels = Labels(100, words: 30);
            yield return new("resize/wrap", 100,
                i => p.Layout(labels[Idx(i, 100)], ltr, ResizeWidth(i), null));
        }

        // The same for right-to-left and mixed text, which takes the BiDi path: labels that
        // fit at every width (a resizable column of short labels), one ellipsized line, and
        // wrapped paragraphs. The last two never meet a width twice (DragWidth): that path
        // remembers lines and probes by range, and the 40 widths of ResizeWidth come round
        // again and again, which would time a replay rather than a drag.
        {
            var p = Pipeline();
            var labels = BidiLabels(100, words: 5);
            yield return new("resize/bidi-fit", 100,
                i => p.Layout(labels[Idx(i, 100)], rtl, ResizeWidth(i) + 300f, 1, TextOverflow.Ellipsis));
        }
        {
            var p = Pipeline();
            var labels = BidiLabels(100, words: 14);
            yield return new("resize/bidi-ellipsis-1line", 100,
                i => p.Layout(labels[Idx(i, 100)], rtl, DragWidth(i), 1, TextOverflow.Ellipsis));
        }
        {
            var p = Pipeline();
            var labels = BidiLabels(100, words: 30);
            yield return new("resize/bidi-wrap", 100,
                i => p.Layout(labels[Idx(i, 100)], auto, DragWidth(i), null));
        }

        // Labels of several font runs: a monospace face without IPA (Windows' Cascadia Mono),
        // an IPA word in every third place, from a fallback face.
        {
            var p = Pipeline();
            var mono = ltr with { FontFamily = FontFamilies.Monospace };
            var labels = MultiFontLabels(100, words: 14);
            yield return new("resize/multifont-ellipsis-1line", 100,
                i => p.Layout(labels[Idx(i, 100)], mono, DragWidth(i), 1, TextOverflow.Ellipsis));
        }

        // And with metric spans, every third word bold and larger: the styled path for Latin,
        // the BiDi path with spans for Hebrew. Widths from DragWidth, as above.
        {
            var p = Pipeline();
            var labels = Labels(100, words: 30);
            var spans = labels.Select(BoldEveryThirdWord).ToArray();
            yield return new("resize/styled-wrap", 100,
                i => p.Layout(labels[Idx(i, 100)], ltr, DragWidth(i), null, TextOverflow.Clip, spans[Idx(i, 100)]));
        }
        {
            var p = Pipeline();
            var labels = Labels(100, words: 14);
            var spans = labels.Select(BoldEveryThirdWord).ToArray();
            yield return new("resize/styled-ellipsis-1line", 100,
                i => p.Layout(labels[Idx(i, 100)], ltr, DragWidth(i), 1, TextOverflow.Ellipsis, spans[Idx(i, 100)]));
        }
        {
            var p = Pipeline();
            var labels = BidiLabels(100, words: 14);
            var spans = labels.Select(BoldEveryThirdWord).ToArray();
            yield return new("resize/styled-bidi-ellipsis-1line", 100,
                i => p.Layout(labels[Idx(i, 100)], rtl, DragWidth(i), 1, TextOverflow.Ellipsis, spans[Idx(i, 100)]));
        }

        // ── caret: the offset↔x mapping a caret, a click and a selection
        // highlight go through (docs/plans/14-bidi.md, stage 2), measured on an
        // already-laid-out line so this is the mapping alone. The line lookup that
        // precedes it in a real caller is what the warm rows above measure.
        {
            var p = Pipeline();
            var latin = p.Layout(Latin, ltr, 10_000, null).Lines[0];
            var hebrew = p.Layout(Hebrew, rtl, 10_000, null).Lines[0];
            var mixed = p.Layout(Mixed, ltr, 10_000, null).Lines[0];
            float latinMid = latin.CaretXAt(latin.DrawnLength / 2);
            float hebrewMid = hebrew.CaretXAt(hebrew.DrawnLength / 2);

            yield return new("caret/latin-x", 5000, i => latin.CaretXAt(i % latin.DrawnLength));
            yield return new("caret/rtl-x", 5000, i => hebrew.CaretXAt(i % hebrew.DrawnLength));
            yield return new("caret/latin-hit", 5000, _ => latin.OffsetAtX(latinMid));
            yield return new("caret/rtl-hit", 5000, _ => hebrew.OffsetAtX(hebrewMid));
            // Through the buffer overload, the way the paint paths call it.
            var buffer = new List<VisualRange>();
            yield return new("caret/latin-select", 5000, _ => latin.VisualRanges(2, latin.DrawnLength - 2, buffer));
            yield return new("caret/mixed-select", 5000, _ => mixed.VisualRanges(2, mixed.DrawnLength - 2, buffer));
        }

        // The warm rows again, on fresh pipelines: the harness's own control
        // group. Identical work to the warm rows, so the two should agree — a
        // recheck row that disagrees with its twin is measuring position in the
        // run, and neither number can be compared against another capture.
        {
            var p = Pipeline();
            string paragraph = Paragraph(700);
            yield return new("recheck/latin-67", 2000, _ => p.Layout(Latin, ltr, 10_000, null));
            yield return new("recheck/latin-4700", 2000, _ => p.Layout(paragraph, ltr, 600, null));
            yield return new("recheck/mixed-38", 2000, _ => p.Layout(Mixed, ltr, 10_000, null));
            yield return new("recheck/hebrew-25", 2000, _ => p.Layout(Hebrew, rtl, 10_000, null));
        }
    }

    private static int Idx(int i, int length) => (i % length + length) % length;

    // Distinct paragraphs from the same vocabulary, each starting a word further in.
    private static string[] Labels(int count, int words)
    {
        var w = Latin.Split(' ');
        var labels = new string[count];
        for (int n = 0; n < count; n++)
            labels[n] = string.Join(' ', Enumerable.Range(n, words).Select(k => w[k % w.Length])) + $" {Alpha(n)}";
        return labels;
    }

    // Hebrew paragraphs with a Latin word in every third place, as a commit list or a file
    // browser in a right-to-left UI shows them.
    private static string[] BidiLabels(int count, int words)
    {
        string[] hebrew = ["שלום", "עולם", "ספר", "בית", "מחשב", "חלון", "קובץ", "תיקייה", "שורה", "טקסט", "מסך", "עכבר"];
        var latin = Latin.Split(' ');
        var labels = new string[count];
        for (int n = 0; n < count; n++)
            labels[n] = string.Join(' ', Enumerable.Range(n, words)
                .Select(k => k % 3 == 2 ? latin[k % latin.Length] : hebrew[k % hebrew.Length])) + $" {Alpha(n)}";
        return labels;
    }

    private static string[] MultiFontLabels(int count, int words)
    {
        string[] ipa = ["ɐɓɖ", "ɠɦɨ", "ɯɰɱ", "ɕɗɘ", "ɜɝɞ", "ɟɡɤ", "ɥɧɭ", "ɮɳɐ"];
        var latin = Latin.Split(' ');
        var labels = new string[count];
        for (int n = 0; n < count; n++)
            labels[n] = string.Join(' ', Enumerable.Range(n, words)
                .Select(k => k % 3 == 2 ? ipa[k % ipa.Length] : latin[k % latin.Length])) + $" {Alpha(n)}";
        return labels;
    }

    // Every third word bold and two points larger: spans that change shaping and line breaks.
    private static HultUI.Core.Rendering.TextSpan[] BoldEveryThirdWord(string text)
    {
        var spans = new List<HultUI.Core.Rendering.TextSpan>();
        int word = 0, start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != ' ') continue;
            if (word++ % 3 == 1)
                spans.Add(new(start, i - start, new HultUI.Core.Primitives.Color(0, 0, 0, 255),
                    Weight: FontWeight.Bold, FontSize: 16f));
            start = i + 1;
        }
        return spans.ToArray();
    }

    // A drag: one width per batch of 100 calls, 3 px on from the last, never the same twice
    // in a run (25 samples plus the warm-up), all of them truncating.
    private static float DragWidth(int i) => 120f + 3f * (Idx(i, 1 << 20) / 100 % 60);

    // One width per batch of 100 calls, stepping through 40 widths that all truncate.
    private static float ResizeWidth(int i) => 120f + 5f * (Idx(i, 4_000) / 100);
}

using System.Diagnostics;
using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Headless;

namespace PerfSuite;

/// <summary>
/// One scenario: <see cref="Setup"/> builds a UI in a fresh headless session, brings it to
/// a steady state, and hands back the operation to time. The name is "group/what", and the
/// group is what <c>--filter</c> and the report sort on.
/// </summary>
sealed record Scenario(string Name, string Description, Func<Ctx, Op> Setup)
{
    public int Width { get; init; } = 1040;
    public int Height { get; init; } = 760;
    public string Group => Name[..Name.IndexOf('/')];
}

/// <summary>What a scenario's setup hands back: the timed step, and what to undo between steps.</summary>
sealed class Op
{
    /// <summary>
    /// The timed mutation. The harness pumps <see cref="Frames"/> frames straight after it,
    /// inside the timing, so a sample is "mutation → frame rendered", the same span the
    /// js-framework-benchmark sample times. Gets the step index, for ops that alternate.
    /// </summary>
    public required Action<int> Run { get; init; }

    /// <summary>
    /// Untimed work before each <see cref="Run"/> — a clear before a create — followed by
    /// an untimed frame so its own layout and paint are paid before the clock starts.
    /// </summary>
    public Action<int>? Reset { get; init; }

    /// <summary>Frames pumped after <see cref="Run"/>, inside the timing. More than one for animations.</summary>
    public int Frames { get; init; } = 1;

    /// <summary>Virtual time each of those frames advances; default one 60 Hz frame. Longer for a tooltip's delay.</summary>
    public TimeSpan? FrameDelta { get; init; }

    /// <summary>
    /// Proof the step did what the scenario says — the dialog opened, the text was typed —
    /// run after every untimed step (and by <c>--verify</c>). Returns null when it did, or
    /// what went wrong. A scenario whose input missed its target still renders a frame and
    /// times something; only a check tells that it timed the wrong thing.
    /// </summary>
    public Func<int, string?>? Check { get; init; }
}

/// <summary>A scenario's handle on its session.</summary>
sealed class Ctx(HeadlessSession session)
{
    public HeadlessSession Session { get; } = session;
    public HeadlessWindow Window => Session.Window;

    /// <summary>Fixed seed, so every run of a scenario does the same work.</summary>
    public Random Rng { get; } = new(20261005);

    public Element? Root { get; private set; }

    /// <summary>Sets the root and pumps until the first layout and paint have happened.</summary>
    public void Mount(Element root)
    {
        Root = root;
        Window.SetRoot(root);
        Session.PumpFrames(2);
    }

    public void Pump(int frames = 1) => Session.PumpFrames(frames);
}

/// <summary>Every sample of one scenario, across rounds.</summary>
sealed class Samples
{
    public readonly List<double> Total = [], Build = [], Style = [], Layout = [], Render = [], Other = [];
    public readonly List<int> Gcs = [];
    public long AllocatedBytes;
    public int Count;
    public int Elements;
    public int UnrenderedSteps;
}

sealed record Result(
    string Name, string Description, int Elements, int N,
    double MinMs, double MedianMs, double BuildMs, double StyleMs, double LayoutMs,
    double RenderMs, double OtherMs, double AllocKB, int UnrenderedSteps);

sealed record Options
{
    public int Rounds { get; init; } = 5;
    public int PerRound { get; init; } = 5;
    public int Warmup { get; init; } = 2;
}

static class Runner
{
    /// <summary>Print every timed step as it happens (<c>--verbose</c>).</summary>
    public static bool Verbose { get; set; }

    /// <summary>The shortest pre-warm, however few scenarios run (see <see cref="Run"/>).</summary>
    public static TimeSpan MinWarm { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Checks that failed during the untimed steps, as "scenario, step n: what".</summary>
    public static List<string> Failures { get; } = [];

    /// <summary>
    /// Runs every scenario's setup and a few untimed steps with its check after each — the
    /// suite's own test, cheap enough for CI. Returns the failures.
    /// </summary>
    public static List<string> Verify(IReadOnlyList<Scenario> scenarios)
    {
        Failures.Clear();
        foreach (var s in scenarios)
        {
            try { RunScenario(s, warmup: 4, measured: 0, into: null); }
            catch (Exception e) { Failures.Add($"{s.Name}: threw {e.GetType().Name}: {e.Message}"); }
        }
        return Failures;
    }

    /// <summary>
    /// The sampling discipline TextBench learned the hard way, at scenario granularity:
    /// <list type="bullet">
    /// <item>a global pre-warm of every scenario before anything is timed, so shared
    /// framework code is tier-1 and PGO'd before the first sample — otherwise whichever
    /// scenario runs first absorbs the JIT and reads up to 2× high;</item>
    /// <item>round-robin rounds: round n of every scenario before round n+1 of any, so
    /// drift over the run lands on every scenario alike instead of the first ones.</item>
    /// </list>
    /// Each (round, scenario) gets a fresh session — a session owns process-wide state, so
    /// only one can be live at a time — and a forced GC before its timed steps.
    /// </summary>
    public static List<Result> Run(IReadOnlyList<Scenario> scenarios, Options o, Action<string>? log = null)
    {
        // Warm for a length of TIME, not a number of passes. Tiered compilation with dynamic
        // PGO takes a method through tier 0, an instrumented tier and tier 1, each step
        // waiting on call counts and on the background compiler; the steady state arrives
        // after seconds of work, not after N calls. One pass and a sleep left a --filter run
        // of one scenario at 30 ms where the full suite (whose own passes take seconds) reads
        // 5 ms — measured converging over ~2.5 s of repeated rounds. So: passes until two
        // are done and MinWarm has elapsed, then a pause for the last compiles to land.
        log?.Invoke($"pre-warm: {scenarios.Count} scenarios, at least {MinWarm.TotalSeconds:0} s");
        var warm = Stopwatch.StartNew();
        int passes = 0;
        do
        {
            foreach (var s in scenarios)
                RunScenario(s, warmup: o.Warmup + 1, measured: 0, into: null);
            passes++;
            Thread.Sleep(passes == 1 ? 250 : 20);   // the first pause lets call counting start
        }
        while (passes < 2 || warm.Elapsed < MinWarm);
        Thread.Sleep(500);

        var samples = scenarios.Select(_ => new Samples()).ToArray();
        for (int round = 0; round < o.Rounds; round++)
        {
            log?.Invoke($"round {round + 1}/{o.Rounds}");
            for (int i = 0; i < scenarios.Count; i++)
                RunScenario(scenarios[i], o.Warmup, o.PerRound, samples[i]);
        }

        return scenarios.Select((s, i) => Summarize(s, samples[i])).ToList();
    }

    private static void RunScenario(Scenario s, int warmup, int measured, Samples? into)
    {
        using var session = HeadlessSession.Create(s.Width, s.Height, title: s.Name);
        var ctx = new Ctx(session);
        var op = s.Setup(ctx);

        int step = 0;
        for (; step < warmup; step++)
        {
            Step(ctx, op, step, null);
            if (into is null && op.Check?.Invoke(step) is { } failure)
                Failures.Add($"{s.Name}, step {step}: {failure}");
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        for (int k = 0; k < measured; k++, step++) Step(ctx, op, step, into);

        // After the steps, so a create scenario reports the tree it built, not the empty one.
        if (into is not null && ctx.Root is not null)
            into.Elements = PerformanceCounters.CountElements(ctx.Root);
    }

    private static void Step(Ctx ctx, Op op, int step, Samples? into)
    {
        if (op.Reset is not null)
        {
            op.Reset(step);
            ctx.Session.PumpFrame();
        }

        var perf = ctx.Window.Window.Performance;
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        int gc0 = GC.CollectionCount(0);
        long t0 = Stopwatch.GetTimestamp();
        op.Run(step);
        long t1 = Stopwatch.GetTimestamp();

        double style = 0, layout = 0, render = 0;
        int rendered = 0;
        for (int f = 0; f < op.Frames; f++)
        {
            // The counters hold the LAST rendered frame, so read them only for a frame
            // that rendered — an idle pump would otherwise count the previous one again.
            if (!ctx.Session.PumpFrame(op.FrameDelta ?? ctx.Session.DefaultFrameDelta)) continue;
            rendered++;
            style += perf.StyleTimeMs;
            layout += perf.LayoutTimeMs;
            render += perf.RenderTimeMs;
        }
        long t2 = Stopwatch.GetTimestamp();
        long alloc1 = GC.GetAllocatedBytesForCurrentThread();

        if (into is null) return;
        double build = Ms(t1 - t0), total = Ms(t2 - t0);
        into.Total.Add(total);
        into.Build.Add(build);
        into.Style.Add(style);
        into.Layout.Add(layout);
        into.Render.Add(render);
        into.Other.Add(Math.Max(0, total - build - style - layout - render));
        into.AllocatedBytes += alloc1 - alloc0;
        into.Gcs.Add(GC.CollectionCount(0) - gc0);
        into.Count++;
        if (Verbose)
            Console.WriteLine($"    {ctx.Window.Window.Title,-36} step {step,2}  total {total,8:0.000}  build {build,7:0.000}  style {style,6:0.00}  layout {layout,7:0.00}  render {render,7:0.00}  gc {GC.CollectionCount(0) - gc0}");
        // A step that rendered nothing timed nothing but the mutation: the scenario is
        // broken (its change never dirtied anything), and the report says so.
        if (rendered == 0) into.UnrenderedSteps++;
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static Result Summarize(Scenario s, Samples x)
    {
        static double Median(List<double> xs)
        {
            if (xs.Count == 0) return 0;
            var sorted = xs.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }

        return new Result(s.Name, s.Description, x.Elements, x.Count,
            x.Total.Count == 0 ? 0 : x.Total.Min(), Median(x.Total),
            Median(x.Build), Median(x.Style), Median(x.Layout), Median(x.Render), Median(x.Other),
            x.Count == 0 ? 0 : x.AllocatedBytes / 1024.0 / x.Count, x.UnrenderedSteps);
    }
}

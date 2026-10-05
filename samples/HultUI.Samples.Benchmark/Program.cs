using System.Diagnostics;
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

// ─────────────────────────────────────────────────────────────────────────────
// HultUI implementation of the classic "js-framework-benchmark" table benchmark
// (https://github.com/krausest/js-framework-benchmark). It times the standard
// row operations end-to-end — from the state mutation to the resulting frame
// being painted — so the numbers are directly comparable to the per-operation
// milliseconds web frameworks report.
//
// Two backends, selected with --virtual:
//   default    a real, NON-virtualized table — one element subtree per row, like
//              a browser materializing one DOM subtree per <tr>. This is the mode
//              that is comparable to js-framework-benchmark (it forbids
//              virtualization). Layout is O(total rows).
//   --virtual  the SAME operations against a VirtualListElement, which only
//              materializes the visible rows. Not a valid js-framework-benchmark
//              entry, but shows what a real app with a huge list would get:
//              layout is O(visible rows).
//
// Flags: --auto runs the scripted suite and prints medians; --profile prints a
// per-pass breakdown of the create operations. Both close the window when done.
// ─────────────────────────────────────────────────────────────────────────────

bool auto = args.Contains("--auto");
bool profile = args.Contains("--profile");
bool virtualMode = args.Contains("--virtual");

// Scripted runs stay on the performance cores of a hybrid CPU: the scheduler otherwise
// moves the UI thread between core kinds, and an efficiency core runs it ~1.8× slower
// (see samples/HultUI.Samples.PerfSuite/CorePinning.cs). --no-pin opts out.
if ((auto || profile) && !args.Contains("--no-pin") && PerfSuite.CorePinning.PinToFastestCores() is { } pinning)
    Console.WriteLine(pinning);

using var app = new Application("HultUI Benchmark", 1040, 760);
app.Theme = Theme.Light;

var window = app.PrimaryWindow;
var bench = new Benchmark(window, virtualMode);
app.SetRoot(bench.Build());

// Live frame HUD (FPS / frame-time breakdown) in the top-right corner — only
// for interactive use. The HUD is an observer tax (a full element-count tree
// walk per frame plus its own draw), so the scripted --auto/--profile runs
// keep it off: their numbers feed cross-framework and cross-OS comparisons
// and must measure the app, not the instrument.
app.DevTools.IsEnabled = !auto && !profile;

if (auto)
    window.RequestPostFrameCallback(() => bench.RunAll(onComplete: window.Close));
else if (profile)
    window.RequestPostFrameCallback(() => bench.Profile(onComplete: window.Close));

app.Run();

sealed class Benchmark
{
    private readonly Window _window;
    private readonly bool _virtual;
    private readonly IBenchTarget _target;

    private readonly BoxElement _resultsBody = Flex.Column(gap: 2);
    private readonly TextElement _rowCountLabel = Text.New("0 rows").FontSize(13f);

    // Last measured latency per named operation (ms), preserving insertion order.
    private readonly List<(string Name, double Ms)> _results = [];

    public Benchmark(Window window, bool virtualMode)
    {
        _window = window;
        _virtual = virtualMode;
        _target = virtualMode ? new VirtualTarget(Measure) : new TableTarget(Measure);
    }

    public Element Build()
    {
        var controls = Flex.Row(gap: 8).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(12, 16))
            .Background(Color.FromHex("#EEF2FF"))
            .Children(
                Btn("Create 1,000", () => Measure("create 1,000", () => _target.Create(1_000))),
                Btn("Create 10,000", () => Measure("create 10,000", () => _target.Create(10_000))),
                Btn("Append 1,000", () => Measure("append 1,000", () => _target.Append(1_000))),
                Btn("Update every 10th", () => Measure("update every 10th", _target.UpdateEveryTenth)),
                Btn("Swap rows", () => Measure("swap rows", _target.Swap)),
                Btn("Clear", () => Measure("clear", _target.Clear)),
                Box.New().FlexGrow(1),
                Btn("Run all ▶", () => RunAll(), Color.FromHex("#4F46E5"))
            );

        var results = Flex.Column(gap: 8).FlexShrink(0)
            .Width(Dimension.Px(260))
            .Padding(16)
            .Background(Color.FromHex("#F8FAFC"))
            .Children(
                Text.New("Last operation latency").FontSize(13f).FontWeight(FontWeight.Bold),
                Text.New("mutation → painted, milliseconds").FontSize(11f)
                    .TextColor(Color.FromHex("#94A3B8")),
                _resultsBody,
                Box.New().FlexGrow(1),
                _rowCountLabel.TextColor(Color.FromHex("#64748B"))
            );

        return Flex.Column(gap: 0).Children(
            // Title bar
            Box.New().FlexShrink(0).Padding(new Thickness(14, 20))
                .Background(Color.FromHex("#4F46E5")).TextColor(Color.White)
                .Children(
                    Flex.Column(gap: 2).Children(
                        Text.New("HultUI Benchmark" + (_virtual ? " — virtualized" : "")).FontSize(20f).FontWeight(FontWeight.Bold),
                        Text.New(_virtual
                                ? "js-framework-benchmark operations on a virtualized list (only visible rows materialized)"
                                : "js-framework-benchmark operations on a real, non-virtualized table")
                            .FontSize(12f).TextColor(Color.FromHex("#C7D2FE"))
                    )
                ),
            controls,
            // Body: results panel on the left, the row list on the right.
            Flex.Row(gap: 0).FlexGrow(1).Children(results, _target.BuildView())
        );
    }

    // A small styled clickable used for the toolbar buttons.
    private static Element Btn(string label, Action onClick, Color? bg = null)
        => Box.New()
            .Padding(new Thickness(8, 14))
            .Background(bg ?? Color.White)
            .CornerRadius(6f)
            .Cursor(CursorType.Pointer)
            .OnClick(_ => onClick())
            .Children(Text.New(label).FontSize(13f).FontWeight(FontWeight.SemiBold)
                .TextColor(bg is null ? Color.FromHex("#1E293B") : Color.White));

    // ── Measurement ───────────────────────────────────────────────────────────

    // Times a mutation end-to-end: from just before the state change to just
    // after the frame reflecting it has been presented. Because the whole
    // pipeline (rebuild → style → layout → render → present) runs synchronously
    // on the UI thread, the post-frame callback fires once that frame is on
    // screen — the same "click → painted" span the web benchmark measures.
    private void Measure(string name, Action mutate)
    {
        var sw = Stopwatch.StartNew();
        mutate();
        _window.RequestPostFrameCallback(() =>
        {
            sw.Stop();
            Record(name, sw.Elapsed.TotalMilliseconds);
            RefreshRowCount();
        });
    }

    private void Record(string name, double ms)
    {
        int existing = _results.FindIndex(r => r.Name == name);
        if (existing >= 0) _results[existing] = (name, ms);
        else _results.Add((name, ms));
        RefreshResultsPanel();
    }

    private void RefreshResultsPanel()
    {
        var rows = _results.Select(r => (Element)Flex.Row(gap: 8)
            .Children(
                Text.New(r.Name).FontSize(12f).FlexGrow(1),
                Text.New($"{r.Ms:0.0} ms").FontSize(12f).FontWeight(FontWeight.SemiBold)
                    .TextColor(Color.FromHex("#4F46E5"))
            )).ToArray();
        _resultsBody.ReplaceChildren(rows);
    }

    private void RefreshRowCount() => _rowCountLabel.Content = $"{_target.Count:N0} rows";

    // ── Scripted "Run all" suite ──────────────────────────────────────────────

    private const int Warmup = 2;
    private const int Repeats = 5;
    private readonly Queue<BenchStep> _script = new();
    private readonly List<double> _samples = [];
    private string? _currentMetric;
    private Action? _onComplete;

    private record BenchStep(string? MeasureAs, Action Apply);

    public void RunAll(Action? onComplete = null)
    {
        _onComplete = onComplete;
        _script.Clear();
        _samples.Clear();
        _currentMetric = null;

        // Global pre-warm before ANY metric is measured. Without it the first
        // metric in the run absorbs JIT tier-up of the shared framework code
        // (style/layout/text/reconciliation) and reads ~2x its warm cost — an
        // unfair penalty versus a browser, whose V8 is warmed the same way. This
        // exercises the full pipeline on large and small tables, then idles a few
        // frames so the background Tier-1 compiler finishes promoting.
        PreWarm(_script);

        Repeat("create 1,000", setup: _target.Clear, op: () => _target.Create(1_000));
        Repeat("update every 10th", setup: () => _target.Create(1_000), op: _target.UpdateEveryTenth);
        Repeat("select row", setup: () => _target.Create(1_000), op: _target.SelectRandom);
        Repeat("swap rows", setup: () => _target.Create(1_000), op: _target.Swap);
        Repeat("remove row", setup: () => _target.Create(1_000), op: _target.RemoveRandom);
        Repeat("create 10,000", setup: _target.Clear, op: () => _target.Create(10_000));
        // Match js-framework-benchmark's parameters exactly: "append rows to
        // large table" appends 1,000 to a table of 1,000, and "clear rows" clears
        // a table of 1,000.
        Repeat("append 1,000 to 1,000", setup: () => _target.Create(1_000), op: () => _target.Append(1_000));
        Repeat("clear 1,000", setup: () => _target.Create(1_000), op: _target.Clear);

        Console.WriteLine($"\n=== HultUI benchmark ({(_virtual ? "virtualized" : "non-virtualized")}): running suite ===");
        Pump();
    }

    private void Repeat(string metric, Action setup, Action op)
    {
        for (int i = 0; i < Warmup + Repeats; i++)
        {
            _script.Enqueue(new BenchStep(null, setup));           // unmeasured setup
            bool measured = i >= Warmup;
            _script.Enqueue(new BenchStep(measured ? metric : null, op));
        }
    }

    // Runs the full pipeline (build/style/layout/text/render) on both large and
    // small tables to drive the shared framework code past the Tier-0 → Tier-1
    // call-count threshold, then idles a dozen frames so the background compiler
    // has wall-clock time to finish promoting before the first measured metric.
    // All steps are unmeasured. Under NativeAOT (no tiering) this is simply a
    // cheap no-op warmup; under JIT it is what makes the first metric fair.
    private void PreWarm(Queue<BenchStep> into)
    {
        void Q(Action op) => into.Enqueue(new BenchStep(null, op));
        for (int pass = 0; pass < 3; pass++)
        {
            Q(() => _target.Create(10_000));
            Q(_target.Clear);
            Q(() => _target.Create(1_000));
            Q(_target.UpdateEveryTenth);
            Q(_target.Swap);
            Q(_target.Clear);
        }
        for (int i = 0; i < 12; i++)
            Q(static () => { });
    }

    private void Pump()
    {
        if (_script.Count == 0) { FinishSuite(); return; }

        var step = _script.Dequeue();
        // Rolling to a new metric — force a GC first so a collection mid-measure
        // doesn't skew the numbers (the web benchmark isolates runs the same way).
        if (step.MeasureAs is not null && step.MeasureAs != _currentMetric)
        {
            FlushMetric();
            _currentMetric = step.MeasureAs;
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        var sw = Stopwatch.StartNew();
        step.Apply();
        _window.RequestPostFrameCallback(() =>
        {
            sw.Stop();
            if (step.MeasureAs is not null)
                _samples.Add(sw.Elapsed.TotalMilliseconds);
            RefreshRowCount();
            Application.InvokeOnMainThread(Pump);
        });
    }

    private void FlushMetric()
    {
        if (_currentMetric is null || _samples.Count == 0) return;
        _samples.Sort();
        double median = _samples[_samples.Count / 2];
        double min = _samples[0];
        Record($"{_currentMetric} (median)", median);
        Console.WriteLine($"  {_currentMetric,-20}  median {median,7:0.00} ms   min {min,7:0.00} ms   (n={_samples.Count})");
        _samples.Clear();
    }

    private void FinishSuite()
    {
        FlushMetric();
        Console.WriteLine("=== done ===\n");
        _onComplete?.Invoke();
    }

    // ── Per-pass profiling ("--profile") ──────────────────────────────────────
    //
    // Decomposes an operation's end-to-end cost into: build (constructing rows +
    // bookkeeping, timed directly) and the frame's style / layout / render /
    // present passes (read from PerformanceCounters).

    private readonly Queue<BenchStep> _profSteps = new();
    private readonly List<(double Build, double Style, double Layout, double Render, double Present)> _profSamples = [];
    private string? _profMetric;
    private double _profBuildMs;

    public void Profile(Action onComplete)
    {
        _onComplete = onComplete;
        _profSteps.Clear();
        _profSamples.Clear();
        _profMetric = null;

        PreWarm(_profSteps);

        QueueProfile("create 1,000", () => _target.Create(1_000));
        QueueProfile("create 10,000", () => _target.Create(10_000));

        Console.WriteLine($"\n=== profile ({(_virtual ? "virtualized" : "non-virtualized")}): per-pass breakdown, median ms over 5 runs ===");
        Console.WriteLine($"{"operation",-16}{"build",9}{"style",9}{"layout",9}{"render",9}{"present",9}{"sum",9}");
        Pump(_profSteps, measured: PumpProfileSample, done: FinishProfile);
    }

    private void QueueProfile(string metric, Action op)
    {
        for (int i = 0; i < Warmup + Repeats; i++)
        {
            _profSteps.Enqueue(new BenchStep(null, _target.Clear));
            _profSteps.Enqueue(new BenchStep(i >= Warmup ? metric : null, op));
        }
    }

    private void Pump(Queue<BenchStep> steps, Action<string> measured, Action done)
    {
        if (steps.Count == 0) { done(); return; }

        var step = steps.Dequeue();
        if (step.MeasureAs is not null && step.MeasureAs != _profMetric)
        {
            FlushProfile();
            _profMetric = step.MeasureAs;
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        long allocBefore = GC.GetTotalAllocatedBytes(precise: false);
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);

        var sw = Stopwatch.StartNew();
        step.Apply();
        _profBuildMs = sw.Elapsed.TotalMilliseconds;
        long allocAfterBuild = GC.GetTotalAllocatedBytes(precise: false);
        _window.RequestPostFrameCallback(() =>
        {
            if (step.MeasureAs is not null)
            {
                double buildMB = (allocAfterBuild - allocBefore) / (1024.0 * 1024.0);
                double frameMB = (GC.GetTotalAllocatedBytes(precise: false) - allocAfterBuild) / (1024.0 * 1024.0);
                Console.WriteLine($"  [{step.MeasureAs}] buildAlloc={buildMB:0.0}MB frameAlloc={frameMB:0.0}MB " +
                    $"gc=({GC.CollectionCount(0) - gc0}/{GC.CollectionCount(1) - gc1}/{GC.CollectionCount(2) - gc2})");
                measured(step.MeasureAs);
            }
            RefreshRowCount();
            Application.InvokeOnMainThread(() => Pump(steps, measured, done));
        });
    }

    private void PumpProfileSample(string _)
    {
        var p = _window.Performance;
        _profSamples.Add((_profBuildMs, p.StyleTimeMs, p.LayoutTimeMs, p.RenderTimeMs, p.PresentTimeMs));
    }

    private void FlushProfile()
    {
        if (_profMetric is null || _profSamples.Count == 0) return;

        double Median(Func<(double Build, double Style, double Layout, double Render, double Present), double> sel)
        {
            var xs = _profSamples.Select(sel).OrderBy(x => x).ToList();
            return xs[xs.Count / 2];
        }

        double build = Median(s => s.Build), style = Median(s => s.Style), layout = Median(s => s.Layout),
            render = Median(s => s.Render), present = Median(s => s.Present);
        double sum = build + style + layout + render + present;
        Console.WriteLine($"{_profMetric,-16}{build,9:0.0}{style,9:0.0}{layout,9:0.0}{render,9:0.0}{present,9:0.0}{sum,9:0.0}");
        _profSamples.Clear();
    }

    private void FinishProfile()
    {
        FlushProfile();
        Console.WriteLine("=== done ===\n");
        _onComplete?.Invoke();
    }
}

// The set of operations both backends implement, so the same harness can drive a
// real table or a virtualized list.
interface IBenchTarget
{
    int Count { get; }
    Element BuildView();           // the list/table element that fills the body
    void Create(int n);
    void Append(int n);
    void UpdateEveryTenth();
    void Swap();
    void SelectRandom();
    void RemoveRandom();
    void Clear();
}

// Shared label vocabulary (identical word lists to js-framework-benchmark).
static class Labels
{
    private static readonly string[] Adjectives =
    [
        "pretty", "large", "big", "small", "tall", "short", "long", "handsome",
        "plain", "quaint", "clean", "elegant", "easy", "angry", "crazy", "helpful",
        "mushy", "odd", "unsightly", "adorable", "important", "inexpensive",
        "cheap", "expensive", "fancy",
    ];
    private static readonly string[] Colours =
    [
        "red", "yellow", "blue", "green", "pink", "brown", "purple", "brown",
        "white", "black", "orange",
    ];
    private static readonly string[] Nouns =
    [
        "table", "chair", "house", "bbq", "desk", "car", "pony", "cookie",
        "sandwich", "burger", "pizza", "mouse", "keyboard",
    ];

    public static string Next(Random rng)
        => $"{Adjectives[rng.Next(Adjectives.Length)]} {Colours[rng.Next(Colours.Length)]} {Nouns[rng.Next(Nouns.Length)]}";
}

// ── Non-virtualized backend: one retained element subtree per row ─────────────
sealed class TableTarget : IBenchTarget
{
    private readonly Action<string, Action> _measure;
    private readonly Random _rng = new(20260710); // fixed seed → reproducible labels
    private readonly BoxElement _body = Flex.Column(gap: 0).FlexShrink(0);
    private readonly List<RowView> _rows = [];
    private RowView? _selected;
    private int _nextId = 1;

    public TableTarget(Action<string, Action> measure) => _measure = measure;

    public int Count => _rows.Count;
    public Element BuildView() => ScrollView.New().FlexGrow(1).Children(_body);

    public void Create(int count)
    {
        _rows.Clear();
        _selected = null;
        var elements = new Element[count];
        for (int i = 0; i < count; i++)
        {
            var view = NewRow();
            _rows.Add(view);
            elements[i] = view.Root;
        }
        _body.ReplaceChildren(elements);
    }

    public void Append(int count)
    {
        if (_rows.Count == 0) { Create(count); return; }
        for (int i = 0; i < count; i++)
            _rows.Add(NewRow());
        _body.ReplaceChildren(_rows.Select(r => (Element)r.Root).ToArray());
    }

    public void UpdateEveryTenth()
    {
        for (int i = 0; i < _rows.Count; i += 10)
            _rows[i].SetLabel(_rows[i].Label + " !!!");
    }

    public void Swap()
    {
        if (_rows.Count < 999) return;
        (_rows[1], _rows[^2]) = (_rows[^2], _rows[1]);
        _body.ReplaceChildren(_rows.Select(r => (Element)r.Root).ToArray());
    }

    public void SelectRandom() => Select(_rows[_rng.Next(_rows.Count)]);
    public void RemoveRandom() => Remove(_rows[_rng.Next(_rows.Count)]);

    public void Clear()
    {
        _rows.Clear();
        _selected = null;
        _body.ReplaceChildren();
    }

    private void Select(RowView view)
    {
        _selected?.SetSelected(false);
        _selected = view;
        view.SetSelected(true);
    }

    private void Remove(RowView view)
    {
        _rows.Remove(view);
        if (_selected == view) _selected = null;
        _body.ReplaceChildren(_rows.Select(r => (Element)r.Root).ToArray());
    }

    private RowView NewRow()
    {
        var view = new RowView(_nextId++, Labels.Next(_rng));
        view.LabelBox.OnClick(_ => _measure("select row", () => Select(view)));
        view.RemoveBox.OnClick(_ => _measure("remove row", () => Remove(view)));
        return view;
    }
}

// ── Virtualized backend: only visible rows are ever materialized ──────────────
sealed class VirtualTarget : IBenchTarget
{
    private readonly Action<string, Action> _measure;
    private readonly Random _rng = new(20260710);
    private readonly VirtualListElement<RowModel> _list;
    private List<RowModel> _models = [];
    private RowModel? _selected;
    private int _nextId = 1;

    public VirtualTarget(Action<string, Action> measure)
    {
        _measure = measure;
        _list = VirtualListElement<RowModel>.New()
            .ItemHeight(30)
            .RenderWith(RenderRow)
            .Items(_models);
    }

    public int Count => _models.Count;
    public Element BuildView() => _list.FlexGrow(1);

    public void Create(int count)
    {
        _models = new List<RowModel>(count);
        _selected = null;
        for (int i = 0; i < count; i++)
            _models.Add(new RowModel(_nextId++, Labels.Next(_rng)));
        Refresh();
    }

    public void Append(int count)
    {
        for (int i = 0; i < count; i++)
            _models.Add(new RowModel(_nextId++, Labels.Next(_rng)));
        Refresh();
    }

    public void UpdateEveryTenth()
    {
        for (int i = 0; i < _models.Count; i += 10)
            _models[i].Label += " !!!";
        Refresh();
    }

    public void Swap()
    {
        if (_models.Count < 999) return;
        (_models[1], _models[^2]) = (_models[^2], _models[1]);
        Refresh();
    }

    public void SelectRandom() => Select(_models[_rng.Next(_models.Count)]);
    public void RemoveRandom() => Remove(_models[_rng.Next(_models.Count)]);

    public void Clear()
    {
        _models = [];
        _selected = null;
        Refresh();
    }

    private void Select(RowModel m)
    {
        if (_selected is not null) _selected.Selected = false;
        _selected = m;
        m.Selected = true;
        Refresh();
    }

    private void Remove(RowModel m)
    {
        _models.Remove(m);
        if (_selected == m) _selected = null;
        Refresh();
    }

    // Re-assigning Items invalidates the list, so it re-materializes just the
    // visible rows on the next frame — the O(visible) cost the benchmark shows.
    private void Refresh() => _list.Items = _models;

    private Element RenderRow(RowModel m)
    {
        var label = Box.New().FlexGrow(1).Cursor(CursorType.Pointer)
            .Children(Text.New(m.Label).FontSize(13f))
            .OnClick(_ => _measure("select row", () => Select(m)));
        var remove = Box.New().Width(Dimension.Px(24)).Cursor(CursorType.Pointer)
            .Children(Text.New("✕").FontSize(13f).TextColor(Color.FromHex("#EF4444")))
            .OnClick(_ => _measure("remove row", () => Remove(m)));
        return Flex.Row(gap: 12).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 16))
            .Background(m.Selected ? Color.FromHex("#DBEAFE") : Color.White)
            .Children(
                Text.New($"{m.Id}").FontSize(13f).TextColor(Color.FromHex("#94A3B8")).Width(Dimension.Px(64)),
                label,
                remove);
    }
}

// Plain data for a virtualized row — no retained element (the list builds one on
// demand for visible rows only).
sealed class RowModel(int id, string label)
{
    public int Id { get; } = id;
    public string Label { get; set; } = label;
    public bool Selected { get; set; }
}

// One non-virtualized row: its element subtree plus the handles operations mutate
// (label text, selection background). Kept alongside the model so operations can
// update a single row in place without rebuilding the whole table.
sealed class RowView
{
    private static readonly Color RowBackground = Color.White;
    private static readonly Color SelectedBackground = Color.FromHex("#DBEAFE");

    public int Id { get; }
    public string Label { get; private set; }
    public BoxElement Root { get; }
    public BoxElement LabelBox { get; }
    public BoxElement RemoveBox { get; }

    private readonly TextElement _labelText;

    public RowView(int id, string label)
    {
        Id = id;
        Label = label;
        _labelText = Text.New(label).FontSize(13f);

        LabelBox = Box.New().FlexGrow(1).Cursor(CursorType.Pointer).Children(_labelText);
        RemoveBox = Box.New().Width(Dimension.Px(24)).Cursor(CursorType.Pointer)
            .Children(Text.New("✕").FontSize(13f).TextColor(Color.FromHex("#EF4444")));

        Root = Flex.Row(gap: 12).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 16))
            .Background(RowBackground)
            .Children(
                Text.New($"{id}").FontSize(13f).TextColor(Color.FromHex("#94A3B8"))
                    .Width(Dimension.Px(64)),
                LabelBox,
                RemoveBox
            );
    }

    public void SetLabel(string label)
    {
        Label = label;
        _labelText.Content = label;
    }

    public void SetSelected(bool selected)
        => Root.BackgroundColor = selected ? SelectedBackground : RowBackground;
}

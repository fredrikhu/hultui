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
// (https://github.com/krausest/js-framework-benchmark). It builds a *real,
// non-virtualized* table of rows — one element subtree per row, just like a
// browser materializes one DOM subtree per <tr> — and times each standard
// operation end-to-end: from the state mutation to the resulting frame being
// painted on screen.
//
// The measured operations mirror the web benchmark exactly, so the numbers here
// are directly comparable to the per-operation milliseconds those frameworks
// report (React, Vue, Svelte, vanilla JS, …):
//
//   create 1,000       replace the body with 1,000 freshly built rows
//   create 10,000      replace the body with 10,000 freshly built rows
//   append 1,000       add 1,000 rows to an already-large table
//   update every 10th  append " !!!" to every 10th row's label
//   select row         highlight a row (in place)
//   swap rows          swap row 1 and row n-2
//   remove row         delete a single row
//   clear              remove every row
//
// Click a button to run one operation and see its latency, or press "Run all"
// to run the scripted suite (warm-up + repeats, median reported) and print a
// summary table to the console — that's the apples-to-apples number to compare
// against a browser running the same benchmark.
// ─────────────────────────────────────────────────────────────────────────────

// Pass --auto to run the scripted suite once on startup, print the summary to
// the console, and close the window — handy for CI or capturing numbers without
// clicking. Without it, the app is interactive.
bool auto = args.Contains("--auto");

using var app = new Application("HultUI Benchmark", 1040, 760);
app.Theme = Theme.Light;

var window = app.PrimaryWindow;
var bench = new Benchmark(window);
app.SetRoot(bench.Build());

// Live frame HUD (FPS / frame-time breakdown) in the top-right corner.
app.DevTools.IsEnabled = true;

if (auto)
    // Kick off after the first frame is painted, and close once the suite ends.
    window.RequestPostFrameCallback(() => bench.RunAll(onComplete: window.Close));

app.Run();

sealed class Benchmark
{
    private readonly Window _window;
    private readonly Random _rng = new(20260710); // fixed seed → reproducible labels

    // The table model and the retained element per row, kept in lock-step.
    private readonly List<RowView> _rows = [];
    private RowView? _selected;
    private int _nextId = 1;

    // Retained containers we mutate in place.
    private readonly BoxElement _tableBody = Flex.Column(gap: 0).FlexShrink(0);
    private readonly BoxElement _resultsBody = Flex.Column(gap: 2);
    private readonly TextElement _rowCountLabel = Text.New("0 rows").FontSize(13f);

    // Last measured latency per named operation (ms), preserving insertion order.
    private readonly List<(string Name, double Ms)> _results = [];

    // ── Label vocabulary (identical word lists to js-framework-benchmark) ──
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

    public Benchmark(Window window) => _window = window;

    public Element Build()
    {
        var controls = Flex.Row(gap: 8).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(12, 16))
            .Background(Color.FromHex("#EEF2FF"))
            .Children(
                Btn("Create 1,000", () => Measure("create 1,000", () => Create(1_000))),
                Btn("Create 10,000", () => Measure("create 10,000", () => Create(10_000))),
                Btn("Append 1,000", () => Measure("append 1,000", () => Append(1_000))),
                Btn("Update every 10th", () => Measure("update every 10th", UpdateEveryTenth)),
                Btn("Swap rows", () => Measure("swap rows", Swap)),
                Btn("Clear", () => Measure("clear", Clear)),
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

        var table = ScrollView.New().FlexGrow(1).Children(_tableBody);

        return Flex.Column(gap: 0).Children(
            // Title bar
            Box.New().FlexShrink(0).Padding(new Thickness(14, 20))
                .Background(Color.FromHex("#4F46E5")).TextColor(Color.White)
                .Children(
                    Flex.Column(gap: 2).Children(
                        Text.New("HultUI Benchmark").FontSize(20f).FontWeight(FontWeight.Bold),
                        Text.New("js-framework-benchmark operations on a real, non-virtualized table")
                            .FontSize(12f).TextColor(Color.FromHex("#C7D2FE"))
                    )
                ),
            controls,
            // Body: results panel on the left, the row table on the right.
            Flex.Row(gap: 0).FlexGrow(1).Children(results, table)
        );
    }

    // ── Operations ──────────────────────────────────────────────────────────

    private void Create(int count)
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
        _tableBody.ReplaceChildren(elements);
    }

    private void Append(int count)
    {
        if (_rows.Count == 0) { Create(count); return; }
        for (int i = 0; i < count; i++)
            _rows.Add(NewRow());
        _tableBody.ReplaceChildren(_rows.Select(r => (Element)r.Root).ToArray());
    }

    // Append " !!!" to every 10th row's label — mutated in place (no re-parenting).
    private void UpdateEveryTenth()
    {
        for (int i = 0; i < _rows.Count; i += 10)
            _rows[i].SetLabel(_rows[i].Label + " !!!");
    }

    // Swap row 1 and row n-2 (the standard benchmark's indices), reordering the
    // children while reusing the existing, already-mounted element subtrees.
    private void Swap()
    {
        if (_rows.Count < 999) return;
        int a = 1, b = _rows.Count - 2;
        (_rows[a], _rows[b]) = (_rows[b], _rows[a]);
        _tableBody.ReplaceChildren(_rows.Select(r => (Element)r.Root).ToArray());
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
        _tableBody.ReplaceChildren(_rows.Select(r => (Element)r.Root).ToArray());
    }

    private void Clear()
    {
        _rows.Clear();
        _selected = null;
        _tableBody.ReplaceChildren();
    }

    // ── Row construction ──────────────────────────────────────────────────────

    private RowView NewRow()
    {
        int id = _nextId++;
        string label = $"{Pick(Adjectives)} {Pick(Colours)} {Pick(Nouns)}";
        var view = new RowView(id, label);
        // Wire interactions: clicking the label selects; clicking ✕ removes.
        view.LabelBox.OnClick(_ => Measure("select row", () => Select(view)));
        view.RemoveBox.OnClick(_ => Measure("remove row", () => Remove(view)));
        return view;
    }

    private string Pick(string[] words) => words[_rng.Next(words.Length)];

    // A small styled clickable used for the toolbar buttons. A coloured variant
    // (bg set) renders white text; the default is a light chip on dark text.
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

    private void RefreshRowCount() => _rowCountLabel.Content = $"{_rows.Count:N0} rows";

    // ── Scripted "Run all" suite ──────────────────────────────────────────────

    private const int Warmup = 2;
    private const int Repeats = 5;
    private readonly Queue<BenchStep> _script = new();
    private readonly List<double> _samples = [];
    private string? _currentMetric;

    private record BenchStep(string? MeasureAs, Action Apply);

    // Builds the scripted suite. Each measured op gets its own frame; any needed
    // setup (e.g. clearing first, or ensuring 1,000 rows exist) runs in its own
    // unmeasured frame beforehand so it never pollutes the measurement.
    private Action? _onComplete;

    public void RunAll(Action? onComplete = null)
    {
        _onComplete = onComplete;
        _script.Clear();
        _samples.Clear();
        _currentMetric = null;

        Repeat("create 1,000", setup: Clear, op: () => Create(1_000));
        Repeat("update every 10th", setup: () => Create(1_000), op: UpdateEveryTenth);
        Repeat("select row", setup: () => Create(1_000),
            op: () => Select(_rows[_rng.Next(_rows.Count)]));
        Repeat("swap rows", setup: () => Create(1_000), op: Swap);
        Repeat("remove row", setup: () => Create(1_000),
            op: () => Remove(_rows[_rng.Next(_rows.Count)]));
        Repeat("create 10,000", setup: Clear, op: () => Create(10_000));
        // Match js-framework-benchmark's parameters exactly: "append rows to
        // large table" appends 1,000 to a table of 1,000 (not 10,000), and
        // "clear rows" clears a table of 1,000. Using the same sizes is what
        // makes these two rows comparable to the web numbers.
        Repeat("append 1,000 to 1,000", setup: () => Create(1_000), op: () => Append(1_000));
        Repeat("clear 1,000", setup: () => Create(1_000), op: Clear);

        Console.WriteLine("\n=== HultUI benchmark: running suite ===");
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
            // Schedule the next step on the following loop iteration so every op
            // is measured against a clean, dedicated frame.
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
}

// One table row: its element subtree plus the handles we mutate (label text,
// selection background). Kept alongside the model so operations can update a
// single row in place without rebuilding the whole table.
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

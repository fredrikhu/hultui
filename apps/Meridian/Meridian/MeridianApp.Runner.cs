using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Meridian.Core;
using Meridian.Theme;

namespace Meridian;

public partial class MeridianApp
{
    // ── Runner state ─────────────────────────────────────────────────────

    private enum RunState { Idle, Running, Done }

    private RunState _runState = RunState.Idle;
    private readonly List<RunRequestResult> _runResults = [];
    private RunSummary? _runSummary;
    private CancellationTokenSource? _runCts;
    /// <summary>Selected scope: null = whole collection, else a folder path.</summary>
    private string? _runScopePath;
    private bool _runBail;
    private int _runExpanded = -1;
    private IReadOnlyList<CollectionRunner.Request> _lastScope = [];

    internal bool RunnerRunning => _runState == RunState.Running;
    internal RunSummary? LastRunSummaryForTests => _runSummary;
    internal string LastScopeNameForTests => ScopeName();

    // ── Actions ──────────────────────────────────────────────────────────

    /// <summary>The requests the current scope selection covers, editor-truthful:
    /// an open tab contributes its in-memory document.</summary>
    private List<CollectionRunner.Request> ScopeRequests()
    {
        var list = new List<CollectionRunner.Request>();
        void Walk(CollectionFolder folder)
        {
            foreach (var sub in folder.Folders) Walk(sub);
            foreach (var request in folder.Requests)
            {
                var open = _tabs.FirstOrDefault(t => t.Path.Equals(request.Path, StringComparison.OrdinalIgnoreCase));
                var doc = open?.Doc ?? (TryReadFile(request.Path) is { } text ? ReqDocument.Parse(text) : null);
                if (doc != null)
                    list.Add(new CollectionRunner.Request(request.Path, request.DisplayName, doc));
            }
        }
        CollectionFolder? Scoped(CollectionFolder folder) =>
            folder.Path.Equals(_runScopePath, StringComparison.OrdinalIgnoreCase) ? folder
            : folder.Folders.Select(Scoped).FirstOrDefault(f => f != null);
        if (_tree is { } tree)
            Walk(_runScopePath == null ? tree.Root : Scoped(tree.Root) ?? tree.Root);
        return list;
    }

    internal void StartRun() => StartRun(ScopeRequests(), keepExisting: false);

    private async void StartRun(IReadOnlyList<CollectionRunner.Request> scope, bool keepExisting)
    {
        if (_runState == RunState.Running || scope.Count == 0) return;
        _runState = RunState.Running;
        if (!keepExisting) _runResults.Clear();
        _runSummary = null;
        _runExpanded = -1;
        _lastScope = scope;
        var cts = _runCts = new CancellationTokenSource();
        MarkDirty();

        var runner = new CollectionRunner(Engine);
        // Awaits resume on the UI thread, so the stream callback mutates state
        // exactly where Build reads it.
        var summary = await runner.RunAsync(scope, EnvironmentValues(), _runBail,
            result => { _runResults.Add(result); MarkDirty(); }, cts.Token);

        _runSummary = keepExisting
            ? new RunSummary(_runResults.ToList(), summary.Wall, summary.Aborted)
            : summary;
        _runState = RunState.Done;
        _runCts = null;
        WriteRunLog(_runSummary);
        MarkDirty();
    }

    private void AbortRun() => _runCts?.Cancel();

    /// <summary>Resume after an abort: keep everything that ran, execute what
    /// was skipped — chain order within the remainder still holds.</summary>
    private void ResumeRun()
    {
        var skippedPaths = _runResults.Where(r => r.Verdict == RunVerdict.Skipped)
            .Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _runResults.RemoveAll(r => r.Verdict == RunVerdict.Skipped);
        StartRun(_lastScope.Where(r => skippedPaths.Contains(r.Path)).ToList(), keepExisting: true);
    }

    private void RerunProblemsOnly()
    {
        var problemPaths = _runResults.Where(r => r.Verdict is RunVerdict.Fail or RunVerdict.Infra)
            .Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        StartRun(_lastScope.Where(r => problemPaths.Contains(r.Path)).ToList(), keepExisting: false);
    }

    private void WriteRunLog(RunSummary summary)
    {
        if (_workspace is not { } ws) return;
        try
        {
            var dir = Path.Combine(ws.Root, "runs");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss");
            File.AppendAllText(Path.Combine(dir, $"{stamp}-{_envName ?? "no-env"}.run.log"),
                RunReports.Text(summary, ScopeName(), _envName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Flash($"Could not write the run log: {e.Message}");
        }
    }

    private string ScopeName() =>
        _runScopePath == null
            ? (_workspace?.Name ?? "collection") + "/"
            : Path.GetFileName(_runScopePath) + "/";

    // ── The Runner view (2) ──────────────────────────────────────────────

    private Element BuildRunnerRealView()
    {
        if (_workspace == null)
            return EmptyView("Runner", "nothing to run yet",
                "Open a collection in Requests (1); runs stream in here, exactly as CI sees them.");

        var row = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        var rail = BuildRunnerRail(); rail.Key = "runner-rail";
        var main = BuildRunnerMain(); main.Key = "runner-main";
        row.AddChild(rail);
        row.AddChild(main);
        return row;
    }

    private Element BuildRunnerRail()
    {
        bool locked = _runState == RunState.Running;
        var panel = Flex.Column(gap: Azimuth.S2).FlexShrink(0)
            .Width(Azimuth.TreeWidth)
            .Padding(new Thickness(Azimuth.S3, Azimuth.S3, Azimuth.S3, Azimuth.S3))
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 0, 1));

        panel.AddChild(Text.New("Run").FontSize(15f).FontWeight(FontWeight.SemiBold).TextColor(Azimuth.TextDim));

        panel.AddChild(Ui.Mono("SCOPE", 10f, Azimuth.TextMuted));
        int total = ScopeRequestsCountFor(null);
        panel.AddChild(ScopeRow(null, $"{_workspace!.Name}/", total, locked));
        if (_tree is { } tree)
            foreach (var folder in tree.Root.Folders)
                panel.AddChild(ScopeRow(folder.Path, folder.Name + "/", ScopeRequestsCountFor(folder.Path), locked));

        panel.AddChild(Box.New().Height(Azimuth.S2));
        panel.AddChild(Ui.Mono("OPTIONS", 10f, Azimuth.TextMuted));
        var bail = Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center)
            .Cursor(locked ? CursorType.Default : CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Checkbox)
            .AccessibleName($"stop on failure{(_runBail ? ", on" : ", off")}")
            .AutomationId("run-bail");
        bail.AddChild(Ui.Mono(_runBail ? "☑" : "☐", 12f, _runBail ? Azimuth.Accent300 : Azimuth.TextMuted));
        bail.AddChild(Text.New("stop on failure").FontSize(12f).TextColor(Azimuth.TextDim));
        if (!locked) bail.OnClick(_ => { _runBail = !_runBail; MarkDirty(); });
        panel.AddChild(bail);
        // The designed disabled slot — the perf-mode feature is post-v1.
        panel.AddChild(Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center).Opacity(0.45f)
            .Children(
                Text.New("iterations / delay").FontSize(12f).TextColor(Azimuth.TextMuted),
                Ui.Mono("later", 10f, Azimuth.TextFaint)));

        panel.AddChild(Box.New().Height(Azimuth.S3));

        int scopeCount = ScopeRequestsCountFor(_runScopePath);
        if (_runState == RunState.Running)
        {
            var abort = Flex.Row(gap: 6).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
                .Padding(new Thickness(7, Azimuth.S4, 7, Azimuth.S4))
                .CornerRadius(Azimuth.Radius1)
                .BorderColor(Azimuth.StatusWarn).BorderWidth(1f)
                .Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.Button).AccessibleName("Abort run")
                .AutomationId("run-abort");
            abort.AddChild(Text.New("Abort").FontSize(12.5f).FontWeight(FontWeight.Medium).TextColor(Azimuth.StatusWarn));
            abort.AddChild(Ui.Kbd("Esc"));
            abort.OnClick(_ => AbortRun());
            panel.AddChild(abort);
        }
        else if (scopeCount == 0)
        {
            panel.AddChild(Flex.Column(gap: 4)
                .Padding(new Thickness(7, Azimuth.S3, 7, Azimuth.S3))
                .CornerRadius(Azimuth.Radius1)
                .BorderColor(Azimuth.Border).BorderWidth(1f)
                .Opacity(0.45f)
                .Children(Text.New("Run").FontSize(12.5f).TextColor(Azimuth.TextMuted))
                .AutomationId("run-disabled"));
            panel.AddChild(Text.New("0 requests in scope — Run is disabled until the scope is non-empty.")
                .FontSize(11f).TextColor(Azimuth.TextMuted).AutomationId("run-zero-scope"));
        }
        else
        {
            var run = Flex.Row(gap: 6).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
                .Padding(new Thickness(7, Azimuth.S4, 7, Azimuth.S4))
                .CornerRadius(Azimuth.Radius1)
                .BorderColor(Azimuth.Accent).BorderWidth(1f)
                .Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.Button).AccessibleName($"Run {scopeCount} requests")
                .AutomationId("run-start");
            run.AddChild(Text.New($"Run · {scopeCount} req").FontSize(12.5f).FontWeight(FontWeight.Medium).TextColor(Azimuth.Accent));
            run.AddChild(Ui.Kbd("Ctrl R"));
            run.OnClick(_ => StartRun());
            run.HoverBg(Azimuth.Tint(Azimuth.Accent), Color.Transparent);
            panel.AddChild(run);
        }

        panel.AddChild(Box.New().FlexGrow(1));
        panel.AddChild(Ui.Mono("same run in CI:", 10f, Azimuth.TextFaint));
        panel.AddChild(Ui.Mono($"meridian run {ScopeName()} \\\n  --env {_envName ?? "<name>"} --report junit", 10f, Azimuth.TextMuted)
            .AutomationId("ci-hint"));
        return panel;
    }

    private Element ScopeRow(string? path, string label, int count, bool locked)
    {
        bool active = string.Equals(path, _runScopePath, StringComparison.OrdinalIgnoreCase);
        var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowList).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S2, 0, Azimuth.S2))
            .CornerRadius(Azimuth.Radius1)
            .Cursor(locked ? CursorType.Default : CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.RadioButton)
            .AccessibleName($"scope {label}{(active ? ", selected" : "")}")
            .AutomationId("scope-" + (path == null ? "all" : Path.GetFileName(path)));
        if (active) row.Background(Azimuth.Tint(Azimuth.Accent));
        row.AddChild(Ui.Mono(label, 11.5f, active ? Azimuth.Accent300 : Azimuth.TextDim).FlexGrow(1));
        row.AddChild(Ui.Mono($"{count} req", 10f, Azimuth.TextFaint));
        if (!locked) row.OnClick(_ => { _runScopePath = path; MarkDirty(); });
        if (!active && !locked) row.HoverBg(Azimuth.Raised, Color.Transparent);
        return row;
    }

    private int ScopeRequestsCountFor(string? path)
    {
        var keep = _runScopePath;
        _runScopePath = path;
        int count = ScopeRequests().Count;
        _runScopePath = keep;
        return count;
    }

    private Element BuildRunnerMain()
    {
        var col = Flex.Column(gap: 0).FlexGrow(1);

        // Progress line while running / after.
        if (_runState != RunState.Idle)
        {
            int done = _runResults.Count;
            var progress = Flex.Row(gap: Azimuth.S3).Height(32).FlexShrink(0).AlignItems(AlignItems.Center)
                .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
                .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0));
            int planned = _runState == RunState.Running
                ? _runResults.Count(r => !_lastScope.Any(s => s.Path.Equals(r.Path, StringComparison.OrdinalIgnoreCase))) + _lastScope.Count
                : done;
            progress.AddChild(Ui.Mono($"{done} / {planned}{(_runState == RunState.Running ? " · running…" : "")}",
                11.5f, Azimuth.TextDim).AutomationId("run-progress"));
            var counts = new RunSummary(_runResults.ToList(), TimeSpan.Zero, false);
            progress.AddChild(Ui.Mono(
                $"{RunReports.PassGlyph} {counts.Passed}  {RunReports.FailGlyph} {counts.Failed}  {RunReports.InfraGlyph} {counts.Infra}",
                11f, Azimuth.TextMuted));
            col.AddChild(progress);
        }

        var scroll = ScrollView.New();
        var list = Flex.Column(gap: 1).Padding(new Thickness(Azimuth.S1, Azimuth.S2, Azimuth.S2, Azimuth.S2));
        if (_runState == RunState.Idle)
        {
            list.AddChild(Flex.Column(gap: Azimuth.S2).Padding(new Thickness(Azimuth.S4))
                .Children(
                    Text.New("nothing has run yet").FontSize(12f).TextColor(Azimuth.TextMuted),
                    Text.New("Pick a scope and Run — results stream in here, exactly as CI sees them.")
                        .FontSize(11.5f).TextColor(Azimuth.TextFaint)));
        }
        for (int i = 0; i < _runResults.Count; i++)
            AddRunRow(list, i);
        scroll.AddChild(list);
        var scrollBox = Box.New().FlexGrow(1).FlexBasis(Dimension.Px(0));
        scrollBox.AddChild(scroll);
        col.AddChild(scrollBox);

        if (_runSummary is { } summary)
        {
            var card = BuildRunSummary(summary);
            card.Key = "run-summary";
            col.AddChild(card);
        }
        return col;
    }

    private void AddRunRow(BoxElement list, int index)
    {
        var result = _runResults[index];
        var (glyphColor, codeText) = result.Verdict switch
        {
            RunVerdict.Pass => (Azimuth.StatusPass, StatusOf(result)),
            RunVerdict.Fail => (Azimuth.StatusFail, StatusOf(result)),
            RunVerdict.Infra => (Azimuth.StatusWarn, "unreachable"),
            _ => (Azimuth.StatusSkip, "skipped"),
        };

        var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowRun).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S2, 0, Azimuth.S2))
            .CornerRadius(Azimuth.Radius1)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName($"{RunReports.Glyph(result.Verdict)} {result.DisplayName}, {codeText}")
            .AutomationId($"run-row-{index}");
        row.AddChild(Ui.Mono(RunReports.Glyph(result.Verdict), 12f, glyphColor).Width(16).FlexShrink(0));
        row.AddChild(MethodChip(result.Method, treeScale: true));
        row.AddChild(Text.New(result.DisplayName).FontSize(12f).TextColor(Azimuth.TextDim)
            .TextOverflow(TextOverflow.Ellipsis).MaxLines(1).FlexGrow(1));
        row.AddChild(Ui.Mono(codeText, 11f, glyphColor).FlexShrink(0));
        if (result.Send?.Response is { } response)
            row.AddChild(Ui.Mono($"{response.Timings.Total.TotalMilliseconds:0} ms", 10.5f, Azimuth.TextMuted).FlexShrink(0));
        if (result.Assertions.Count > 0)
            row.AddChild(Ui.Mono($"{result.AssertionsPassed}/{result.Assertions.Count} {RunReports.PassGlyph}",
                10.5f, result.AssertionsPassed == result.Assertions.Count ? Azimuth.StatusPass : Azimuth.StatusFail).FlexShrink(0));
        int rowIndex = index;
        row.OnClick(_ => { _runExpanded = _runExpanded == rowIndex ? -1 : rowIndex; MarkDirty(); });
        row.HoverBg(Azimuth.Raised, Color.Transparent);
        list.AddChild(row);

        if (_runExpanded == index)
            list.AddChild(BuildRunRowDetail(result));
    }

    private static string StatusOf(RunRequestResult result) =>
        result.Send?.Response?.StatusCode.ToString() ?? "—";

    private Element BuildRunRowDetail(RunRequestResult result)
    {
        var inset = Flex.Column(gap: Azimuth.S1).FlexShrink(0)
            .Padding(new Thickness(Azimuth.S2, Azimuth.S3, Azimuth.S2, 40))
            .Background(Azimuth.Bg)
            .BorderColor(Azimuth.Border).BorderWidth(1f)
            .CornerRadius(Azimuth.Radius1);
        // A role, or the container never lands in the accessibility tree and
        // FindByAutomationId can't see it (the response-panel lesson).
        inset.AccessibleRole(AccessibilityRole.TabPanel).AccessibleName("Result detail");
        inset.AutomationId("run-detail");

        foreach (var (spec, outcome) in result.Assertions)
        {
            var line = Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center);
            line.AddChild(Ui.Mono(outcome.Passed ? RunReports.PassGlyph : RunReports.FailGlyph, 11f,
                outcome.Passed ? Azimuth.StatusPass : Azimuth.StatusFail));
            line.AddChild(Ui.Mono(Directives.FormatAssert(spec), 11f, Azimuth.TextDim));
            if (!outcome.Passed)
                line.AddChild(Ui.Mono($"expected \"{spec.Expected}\" · actual \"{outcome.Actual}\"", 10.5f, Azimuth.StatusFail));
            inset.AddChild(line);
        }
        if (result.Detail.Length > 0)
            inset.AddChild(Ui.Mono(result.Detail, 11f,
                result.Verdict == RunVerdict.Infra ? Azimuth.StatusWarn : Azimuth.StatusFail));

        if (result.Send != null)
        {
            var open = BarAction("open response", "open-response", () =>
            {
                OpenRequest(result.Path);
                if (ActiveTab is { } tab) tab.Result = result.Send;
                SetView(MeridianView.Requests);
            });
            inset.AddChild(open);
        }
        return inset;
    }

    /// <summary>The summary card — worded by <see cref="RunReports.TextLines"/>,
    /// character for character, so the CI log and this card are the same text.</summary>
    private Element BuildRunSummary(RunSummary summary)
    {
        var card = Flex.Column(gap: Azimuth.S1).FlexShrink(0)
            .Padding(new Thickness(Azimuth.S3, Azimuth.S3, Azimuth.S3, Azimuth.S3))
            .Background(Azimuth.Surface)
            .BorderColor(summary.Failed > 0 ? Azimuth.StatusFail : Azimuth.Border).BorderWidth(1f);
        card.AccessibleRole(AccessibilityRole.TabPanel).AccessibleName("Run summary");
        card.AutomationId("run-summary");

        foreach (var line in RunReports.TextLines(summary, ScopeName(), _envName))
            card.AddChild(Ui.Mono(line, 11.5f, LineColor(line)).FlexShrink(0));

        var actions = Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center)
            .Padding(new Thickness(Azimuth.S2, 0, 0, 0));
        if (summary.Aborted && summary.Skipped > 0)
            actions.AddChild(BarAction($"Resume from #{summary.Results.Count(r => r.Verdict != RunVerdict.Skipped) + 1}",
                "run-resume", ResumeRun));
        if (summary.Failed > 0 || summary.Infra > 0)
            actions.AddChild(BarAction("Re-run failures only", "run-rerun-failures", RerunProblemsOnly));
        actions.AddChild(BarAction("Re-run all", "run-rerun-all", StartRun));
        actions.AddChild(BarAction("export report", "run-export", () => ExportReport(summary)));
        card.AddChild(actions);
        return card;
    }

    private Color LineColor(string line) =>
        line.StartsWith(RunReports.FailGlyph) ? Azimuth.StatusFail
        : line.StartsWith(RunReports.InfraGlyph) ? Azimuth.StatusWarn
        : Azimuth.TextDim;

    private async void ExportReport(RunSummary summary)
    {
        var dest = await _fileDialogs.SaveFileAsync("run-report.txt", _workspace?.Root);
        if (dest == null) return;
        try
        {
            await File.WriteAllTextAsync(dest, RunReports.Text(summary, ScopeName(), _envName));
            Flash($"Saved {Path.GetFileName(dest)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Flash($"Could not save: {e.Message}");
        }
    }
}

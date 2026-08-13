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
    // ── Extractions applied after a response ─────────────────────────────

    /// <summary>Runs the tab's extractions against a fresh response. A row
    /// that matches lands in the store; one that doesn't keeps its
    /// last-matched run number, which is what "last matched n runs ago"
    /// renders. Runs count successful responses, session-wide.</summary>
    private void ApplyExtractions(RequestTab tab, HttpResponseData response)
    {
        _runCounter++;
        foreach (var directive in tab.Doc.Extracts)
        {
            if (Directives.ParseExtract(directive.Args, out _) is not { } spec) continue;
            if (Directives.EvaluateExtract(spec, response) is { } value)
            {
                Extracted[spec.Variable] = value;
                _extractedRun[spec.Variable] = _runCounter;
                _extractLastMatched[(tab.Path, spec.Variable)] = _runCounter;
            }
        }
    }

    private bool PreviewStale(RequestTab tab) =>
        tab.ResultSnapshot != null && tab.ResultSnapshot != tab.Doc.Serialize();

    // ── The Assertions sub-tab: rows with a live preview ─────────────────

    private Element BuildAssertionsEditor(RequestTab tab)
    {
        var scroll = ScrollView.New();
        var col = Flex.Column(gap: 2).Padding(new Thickness(Azimuth.S2, Azimuth.S3, Azimuth.S3, Azimuth.S3));
        var response = tab.Result?.Response;

        if (PreviewStale(tab))
        {
            var banner = Flex.Row(gap: Azimuth.S3).Height(28).FlexShrink(0).AlignItems(AlignItems.Center)
                .Padding(new Thickness(0, Azimuth.S2, 0, Azimuth.S2))
                .Background(Azimuth.Alpha(Azimuth.StatusWarn, 0.10))
                .CornerRadius(Azimuth.Radius1);
            banner.AddChild(Text.New("! preview is stale — request edited since last send")
                .FontSize(11f).TextColor(Azimuth.StatusWarn).AutomationId("stale-banner"));
            banner.AddChild(Box.New().FlexGrow(1));
            banner.AddChild(BarAction("send now", "stale-send", SendActive));
            col.AddChild(banner);
        }

        if (tab.Doc.Asserts.Count == 0)
        {
            // The nudge, not a nag: what a run does with zero rows, and two
            // one-click starters.
            col.AddChild(Flex.Column(gap: Azimuth.S2).Padding(new Thickness(Azimuth.S3))
                .Children(
                    Text.New("No assertions yet. A run counts this request as ✓ if the response is 2xx.")
                        .FontSize(12f).TextColor(Azimuth.TextMuted),
                    Flex.Row(gap: Azimuth.S2).Children(
                        AddRowButton("＋ assert status 200", "starter-status", () => { tab.Doc.AddAssert("status equals 200"); MarkDirty(); }),
                        AddRowButton("＋ assert time under 500 ms", "starter-time", () => { tab.Doc.AddAssert("time under 500 ms"); MarkDirty(); }))));
        }
        else
        {
            for (int i = 0; i < tab.Doc.Asserts.Count; i++)
                col.AddChild(AssertionRow(tab, i, response));
            col.AddChild(AddRowButton("＋ add assertion", "add-assert", () =>
            {
                tab.Doc.AddAssert("status equals 200");
                MarkDirty();
            }));
        }

        col.AddChild(Box.New().Height(Azimuth.S4));
        col.AddChild(Ui.Mono("EXTRACTIONS — after response", 10f, Azimuth.TextMuted).FlexShrink(0));
        for (int i = 0; i < tab.Doc.Extracts.Count; i++)
            col.AddChild(ExtractionRow(tab, i, response));
        col.AddChild(AddRowButton("＋ add extraction", "add-extract", () =>
        {
            tab.Doc.AddExtract("body $.id -> value");
            MarkDirty();
        }));

        scroll.AddChild(col);
        return scroll;
    }

    /// <summary>One assert line: source+selector · operator · expected · the
    /// live verdict against the stored last response. A line that doesn't
    /// parse renders as one raw input plus the parser's words.</summary>
    private Element AssertionRow(RequestTab tab, int index, HttpResponseData? response)
    {
        string args = tab.Doc.Asserts[index].Args;
        var spec = Directives.ParseAssert(args, out var parseError);

        var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowForm).FlexShrink(0).AlignItems(AlignItems.Center);
        if (spec != null && response != null && !Directives.Evaluate(spec, response).Passed)
            row.Background(Azimuth.Alpha(Azimuth.StatusFail, 0.04));

        if (spec == null)
        {
            row.AddChild(FormInput(args, v => { tab.Doc.SetAssert(index, v); MarkDirty(); }, 0, $"assert-raw-{index}"));
            row.AddChild(Text.New($"! {parseError}").FontSize(10.5f).TextColor(Azimuth.StatusWarn).FlexShrink(0));
            row.AddChild(RemoveButton($"Remove assertion {index + 1}", () => { tab.Doc.RemoveAssert(index); MarkDirty(); }));
            return row;
        }

        string sourceText = spec.Qualifier.Length > 0 ? $"{spec.Source} {spec.Qualifier}" : spec.Source;
        row.AddChild(FormInput(sourceText, v => RewriteAssert(tab, index, source: v), 200, $"assert-source-{index}"));

        // Operator: a popover chip cycling equals/contains/under.
        var op = Box.New()
            .Padding(new Thickness(4, Azimuth.S2, 4, Azimuth.S2))
            .CornerRadius(Azimuth.Radius1)
            .BorderColor(Azimuth.Border).BorderWidth(1f)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button).AccessibleName($"operator {spec.Op}")
            .AutomationId($"assert-op-{index}")
            .Children(Ui.Mono(spec.Op + " ▾", 10.5f, Azimuth.TextDim));
        int opIndex = index;
        op.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            var box = e.CurrentTarget.Layout.AbsoluteBorderBox;
            _popoverX = box.X;
            _popoverY = box.Y + box.Height + 4f;
            _popover = Directives.AssertOps
                .Select(o => (o, (Action)(() => RewriteAssert(tab, opIndex, op: o)))).ToList();
            MarkDirty();
        });
        row.AddChild(op);

        row.AddChild(FormInput(spec.Expected, v => RewriteAssert(tab, index, expected: v), 0, $"assert-expected-{index}"));

        row.AddChild(AssertPreview(spec, response));
        row.AddChild(RemoveButton($"Remove assertion {index + 1}", () => { tab.Doc.RemoveAssert(index); MarkDirty(); }));
        return row;
    }

    private Element AssertPreview(Directives.AssertionSpec spec, HttpResponseData? response)
    {
        if (response == null)
            return Ui.Mono("— send to preview", 10.5f, Azimuth.TextFaint).Width(180).FlexShrink(0);
        var outcome = Directives.Evaluate(spec, response);
        return outcome.Passed
            ? Ui.Mono($"✓ {Truncate(outcome.Actual, 22)}", 10.5f, Azimuth.StatusPass).Width(180).FlexShrink(0)
            : Ui.Mono($"✗ got {Truncate(outcome.Actual, 18)}", 10.5f, Azimuth.StatusFail).Width(180).FlexShrink(0);
    }

    private void RewriteAssert(RequestTab tab, int index, string? source = null, string? op = null, string? expected = null)
    {
        if (Directives.ParseAssert(tab.Doc.Asserts[index].Args, out _) is not { } current) return;
        string sourceText = source ?? (current.Qualifier.Length > 0 ? $"{current.Source} {current.Qualifier}" : current.Source);
        tab.Doc.SetAssert(index, $"{sourceText} {op ?? current.Op} {expected ?? current.Expected}");
        MarkDirty();
    }

    /// <summary>One extract line: source+selector · → variable · what it
    /// selects right now. A dead path says when it last worked.</summary>
    private Element ExtractionRow(RequestTab tab, int index, HttpResponseData? response)
    {
        string args = tab.Doc.Extracts[index].Args;
        var spec = Directives.ParseExtract(args, out var parseError);

        var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowForm).FlexShrink(0).AlignItems(AlignItems.Center);
        if (spec == null)
        {
            row.AddChild(FormInput(args, v => { tab.Doc.SetExtract(index, v); MarkDirty(); }, 0, $"extract-raw-{index}"));
            row.AddChild(Text.New($"! {parseError}").FontSize(10.5f).TextColor(Azimuth.StatusWarn).FlexShrink(0));
            row.AddChild(RemoveButton($"Remove extraction {index + 1}", () => { tab.Doc.RemoveExtract(index); MarkDirty(); }));
            return row;
        }

        row.AddChild(FormInput($"{spec.Source} {spec.Selector}",
            v => { tab.Doc.SetExtract(index, $"{v} -> {spec.Variable}"); MarkDirty(); }, 240, $"extract-selector-{index}"));
        row.AddChild(Ui.Mono("→", 11f, Azimuth.TextFaint).FlexShrink(0));
        row.AddChild(FormInput(spec.Variable,
            v => { tab.Doc.SetExtract(index, $"{spec.Source} {spec.Selector} -> {v}"); MarkDirty(); }, 130, $"extract-var-{index}"));

        row.AddChild(ExtractPreview(tab, spec, response));
        row.AddChild(RemoveButton($"Remove extraction {index + 1}", () => { tab.Doc.RemoveExtract(index); MarkDirty(); }));
        return row;
    }

    private Element ExtractPreview(RequestTab tab, Directives.ExtractionSpec spec, HttpResponseData? response)
    {
        if (response == null)
            return Ui.Mono("—", 10.5f, Azimuth.TextFaint).FlexShrink(0);
        if (Directives.EvaluateExtract(spec, response) is { } value)
            return Ui.Mono($"✓ selects \"{Truncate(value, 24)}\"", 10.5f, Azimuth.StatusPass)
                .FlexShrink(1).TextOverflow(TextOverflow.Ellipsis).MaxLines(1);

        string suffix = _extractLastMatched.TryGetValue((tab.Path, spec.Variable), out int run)
            ? $" — last matched {RunsAgo(run)}"
            : "";
        return Ui.Mono($"! matches nothing{suffix}", 10.5f, Azimuth.StatusWarn)
            .FlexShrink(1).TextOverflow(TextOverflow.Ellipsis).MaxLines(1)
            .AutomationId("extract-dead");
    }

    private string RunsAgo(int run)
    {
        int ago = _runCounter - run;
        return ago switch { 0 => "this run", 1 => "1 run ago", _ => $"{ago} runs ago" };
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    // ── The Chain sub-tab: a list, never a graph canvas ──────────────────

    private Element BuildChainEditor(RequestTab tab)
    {
        var nodes = AnalyzeAllRequests();
        var self = Chain.Analyze(tab.Path, tab.Doc);
        var envValues = Resolver();

        var scroll = ScrollView.New();
        var col = Flex.Column(gap: Azimuth.S2).Padding(new Thickness(Azimuth.S2, Azimuth.S3, Azimuth.S3, Azimuth.S3));

        if (self.Uses.Count == 0 && self.Produces.Count == 0)
        {
            col.AddChild(Text.New("This request uses no variables and extracts none — no chain.")
                .FontSize(12f).TextColor(Azimuth.TextMuted));
        }

        foreach (var use in self.Uses)
        {
            var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowList).FlexShrink(0).AlignItems(AlignItems.Center);
            row.AddChild(Ui.Mono("uses", 10f, Azimuth.TextMuted).Width(64).FlexShrink(0));
            row.AddChild(VarChipLabel(use));

            string? producer = nodes.FirstOrDefault(n => n.Produces.Contains(use)
                && !n.Path.Equals(tab.Path, StringComparison.OrdinalIgnoreCase))?.Path;
            bool envDefined = envValues.Lookup(use) != null && !Extracted.ContainsKey(use);
            if (envDefined)
                row.AddChild(Ui.Mono("✓ env", 10.5f, Azimuth.StatusPass));
            else if (producer != null)
            {
                row.AddChild(Ui.Mono($"← {RequestName(producer)}", 10.5f, Azimuth.TextDim));
                row.AddChild(Extracted.ContainsKey(use)
                    ? Ui.Mono("✓ fresh", 10.5f, Azimuth.StatusPass)
                    : Ui.Mono("! not yet produced this session", 10.5f, Azimuth.StatusWarn));
            }
            else if (Extracted.ContainsKey(use))
                row.AddChild(Ui.Mono("✓ extracted", 10.5f, Azimuth.StatusPass));
            else
                row.AddChild(Ui.Mono("! unresolved — nothing produces it", 10.5f, Azimuth.StatusWarn));
            col.AddChild(row);
        }

        foreach (var produced in self.Produces)
        {
            var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowList).FlexShrink(0).AlignItems(AlignItems.Center);
            row.AddChild(Ui.Mono("produces", 10f, Azimuth.TextMuted).Width(64).FlexShrink(0));
            row.AddChild(VarChipLabel(produced));
            var consumers = Chain.ConsumersOf(produced, nodes)
                .Where(p => !p.Equals(tab.Path, StringComparison.OrdinalIgnoreCase))
                .Select(RequestName).ToList();
            row.AddChild(Ui.Mono(consumers.Count > 0 ? $"→ used by {string.Join(", ", consumers)}" : "→ unused",
                10.5f, consumers.Count > 0 ? Azimuth.TextDim : Azimuth.TextFaint));
            col.AddChild(row);
        }

        var (order, cycleError) = Chain.RunOrderFor(tab.Path, nodes);
        col.AddChild(Box.New().Height(Azimuth.S2));
        col.AddChild(cycleError != null
            ? Ui.Mono($"! {cycleError}", 10.5f, Azimuth.StatusWarn).AutomationId("chain-order")
            : Ui.Mono("run order when executed alone: " + string.Join(" → ",
                    order.Select(p => p.Equals(tab.Path, StringComparison.OrdinalIgnoreCase) ? "this" : RequestName(p))),
                10.5f, Azimuth.TextMuted).AutomationId("chain-order"));

        scroll.AddChild(col);
        return scroll;
    }

    private static Element VarChipLabel(string name) =>
        Box.New().Padding(new Thickness(1, 4, 1, 4)).CornerRadius(Azimuth.Radius1)
            .Background(Azimuth.Alpha(Azimuth.Accent300, 0.12)).FlexShrink(0)
            .Children(Ui.Mono("{{" + name + "}}", 11f, Azimuth.Accent300));

    /// <summary>Chain nodes for every request in the tree, parsed on demand.
    /// The open tab contributes its in-memory (possibly unsaved) document.</summary>
    private List<Chain.Node> AnalyzeAllRequests()
    {
        var nodes = new List<Chain.Node>();
        void Walk(CollectionFolder folder)
        {
            foreach (var sub in folder.Folders) Walk(sub);
            foreach (var request in folder.Requests)
            {
                var open = _tabs.FirstOrDefault(t => t.Path.Equals(request.Path, StringComparison.OrdinalIgnoreCase));
                if (open != null)
                    nodes.Add(Chain.Analyze(open.Path, open.Doc));
                else if (TryReadFile(request.Path) is { } text)
                    nodes.Add(Chain.Analyze(request.Path, ReqDocument.Parse(text)));
            }
        }
        if (_tree != null) Walk(_tree.Root);
        return nodes;
    }

    private string RequestName(string path)
    {
        RequestRef? Find(CollectionFolder folder) =>
            folder.Requests.FirstOrDefault(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
            ?? folder.Folders.Select(Find).FirstOrDefault(r => r != null);
        return (_tree != null ? Find(_tree.Root)?.DisplayName : null)
            ?? CollectionScanner.Prettify(System.IO.Path.GetFileNameWithoutExtension(path));
    }
}

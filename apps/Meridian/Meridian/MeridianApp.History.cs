using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using Meridian.Core;
using Meridian.Theme;

namespace Meridian;

public partial class MeridianApp
{
    // ── History state ────────────────────────────────────────────────────

    private List<HistoryEntry> _history = [];
    private bool _historyLoaded;
    private HistoryEntry? _historySelection;
    /// <summary>The keyboard cursor into the filtered rows (the mock's
    /// "↑↓ walk rows · enter open · r restore").</summary>
    private int _historyCursor = -1;
    private readonly TextSearch _historyFind;
    private readonly Ref<TextInputElement> _historyFieldRef = new();
    /// <summary>Active filters. Null means "any".</summary>
    private string? _filterMethod;
    private bool _filterFailuresOnly;
    private bool _filterTodayOnly;
    private string? _filterEnv;

    internal IReadOnlyList<HistoryEntry> History => _history;

    /// <summary>Every send lands here. Called with the response or the failure —
    /// an unreachable host is history too, and the row says so.</summary>
    internal void RecordHistory(string method, string url, SendResult result)
    {
        if (_workspace is not { } ws) return;
        var entry = result.Response is { } response
            ? new HistoryEntry(DateTimeOffset.Now, method, url, response.StatusCode,
                response.Timings.Total.TotalMilliseconds, _envName, ActiveTab?.Path)
            : new HistoryEntry(DateTimeOffset.Now, method, url, 0,
                result.Failure?.Elapsed.TotalMilliseconds ?? 0, _envName, ActiveTab?.Path,
                result.Failure?.Headline ?? "no response");

        HistoryLog.Append(ws.Root, entry);
        _history.Insert(0, entry);
        _historyLoaded = true;
    }

    private void EnsureHistoryLoaded()
    {
        if (_historyLoaded || _workspace is not { } ws) return;
        _history = HistoryLog.Read(ws.Root).ToList();
        _historyLoaded = true;
    }

    /// <summary>The rows the filters and the query leave. The query matches the
    /// URL and the method, which is what someone types when hunting a send.</summary>
    internal List<HistoryEntry> FilteredHistory()
    {
        var now = DateTimeOffset.Now;
        var query = _historyFind.Query.Trim();
        return _history.Where(e =>
            (_filterMethod == null || e.Method.Equals(_filterMethod, StringComparison.OrdinalIgnoreCase))
            && (!_filterFailuresOnly || !e.Reached || e.StatusCode >= 400)
            && (!_filterTodayOnly || HistoryLog.IsToday(e, now))
            && (_filterEnv == null || string.Equals(e.EnvName, _filterEnv, StringComparison.Ordinal))
            && (query.Length == 0
                || e.Url.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.Method.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>The History view's own keys. True when the key was consumed.</summary>
    internal bool HandleHistoryKey(KeyDownEvent e)
    {
        var rows = FilteredHistory();
        switch (e.Key)
        {
            case KeyCode.Down when rows.Count > 0:
                _historyCursor = Math.Min(_historyCursor + 1, rows.Count - 1);
                _historySelection = rows[_historyCursor];
                MarkDirty();
                return true;
            case KeyCode.Up when rows.Count > 0:
                _historyCursor = Math.Max(_historyCursor - 1, 0);
                _historySelection = rows[_historyCursor];
                MarkDirty();
                return true;
            case KeyCode.Enter when _historyCursor >= 0 && _historyCursor < rows.Count:
                _historySelection = rows[_historyCursor];
                MarkDirty();
                return true;
            default:
                // r alone: Shift or Alt and r used to restore too.
                if (Shortcuts.Pressed("history.restore", e) && _historySelection is { } selected)
                {
                    RestoreToEditor(selected);
                    return true;
                }
                return false;
        }
    }

    private bool AnyFilterActive =>
        _filterMethod != null || _filterFailuresOnly || _filterTodayOnly || _filterEnv != null;

    private void ClearHistoryFilters()
    {
        _filterMethod = null;
        _filterFailuresOnly = false;
        _filterTodayOnly = false;
        _filterEnv = null;
        MarkDirty();
    }

    /// <summary>
    /// Restore never overwrites: it opens the original file as a fresh tab and
    /// points its URL at what was actually sent. A history row is a record, not
    /// a draft — reopening one must not silently rewrite the file it came from.
    /// </summary>
    internal void RestoreToEditor(HistoryEntry entry)
    {
        if (entry.RequestPath is { } path && File.Exists(path))
        {
            OpenRequest(path);
            if (ActiveTab is { } tab)
                tab.Doc.SetRequestLine(entry.Method, entry.Url);
        }
        else
        {
            Flash(entry.RequestPath == null
                ? "That send had no request file — copy the URL instead"
                : $"{Path.GetFileName(entry.RequestPath)} is gone");
            return;
        }
        SetView(MeridianView.Requests);
    }

    // ── The History view (4) ─────────────────────────────────────────────

    private Element BuildHistoryRealView()
    {
        if (_workspace == null)
            return EmptyView("History", "no sends yet",
                "Open a collection — everything you send lands here, newest first.");

        EnsureHistoryLoaded();

        var col = Flex.Column(gap: 0).FlexGrow(1);
        col.AddChild(BuildHistoryFilterBar());

        var rows = FilteredHistory();
        var body = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        var list = BuildHistoryList(rows); list.Key = "history-list";
        body.AddChild(list);
        if (_historySelection is { } selected)
        {
            var archive = BuildHistoryArchive(selected); archive.Key = "history-archive";
            body.AddChild(archive);
        }
        col.AddChild(body);

        col.AddChild(Flex.Row(gap: Azimuth.S3).Height(24).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 1, 0, 0))
            .Children(
                Ui.Mono($".meridian/history.log · capped at {HistoryLog.Cap:N0} entries · oldest drop off",
                    10f, Azimuth.TextFaint),
                Box.New().FlexGrow(1),
                Ui.Mono("↑↓ walk · enter open · r restore · ⌃F find", 10f, Azimuth.TextFaint)));
        return col;
    }

    private Element BuildHistoryFilterBar()
    {
        var bar = Flex.Row(gap: Azimuth.S2).Height(36).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0));

        var input = TextInput.New(_historyFind.Query).Style(Ui.Field)
            .Height(Dimension.Px(26)).Width(260).FlexShrink(0);
        input.PlaceholderText("find in history…");
        input.OnTextChanged(v => { _historyFind.SetQuery(v); MarkDirty(); });
        input.FontFamily(Fonts.Mono).FontSize(11.5f).TextColor(Azimuth.Text);
        input.WithRef(_historyFieldRef);
        input.AutomationId("history-search");
        bar.AddChild(input);

        foreach (var method in new[] { "GET", "POST", "PUT", "PATCH", "DELETE" })
            bar.AddChild(FilterChip(method, _filterMethod == method, Azimuth.MethodColor(method),
                "filter-" + method, () =>
                {
                    _filterMethod = _filterMethod == method ? null : method;
                    MarkDirty();
                }));
        bar.AddChild(FilterChip("failures", _filterFailuresOnly, Azimuth.StatusWarn, "filter-failures",
            () => { _filterFailuresOnly = !_filterFailuresOnly; MarkDirty(); }));
        bar.AddChild(FilterChip("today", _filterTodayOnly, Azimuth.Accent300, "filter-today",
            () => { _filterTodayOnly = !_filterTodayOnly; MarkDirty(); }));
        if (_envName is { } env)
            bar.AddChild(FilterChip($"env: {env}", _filterEnv == env, Azimuth.Accent300, "filter-env",
                () => { _filterEnv = _filterEnv == env ? null : env; MarkDirty(); }));

        bar.AddChild(Box.New().FlexGrow(1));
        bar.AddChild(Ui.Mono($"{FilteredHistory().Count} of {_history.Count}", 10.5f, Azimuth.TextMuted)
            .AutomationId("history-count"));
        return bar;
    }

    private Element FilterChip(string label, bool on, Color color, string automationId, Action onClick)
    {
        var chip = Box.New()
            .Padding(new Thickness(3, Azimuth.S2, 3, Azimuth.S2))
            .CornerRadius(Azimuth.Radius1)
            .BorderColor(on ? color : Azimuth.Border).BorderWidth(1f)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName($"{label}{(on ? ", on" : "")}")
            .AutomationId(automationId)
            .Children(Ui.ChipLabel(label, 10f, on ? color : Azimuth.TextMuted));
        if (on) chip.Background(Azimuth.Alpha(color, 0.12));
        chip.OnClick(_ => onClick());
        return chip;
    }

    private Element BuildHistoryList(List<HistoryEntry> rows)
    {
        var col = Flex.Column(gap: 0).FlexGrow(1);
        if (rows.Count == 0)
        {
            col.AddChild(Flex.Column(gap: Azimuth.S2).FlexGrow(1)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(_history.Count == 0
                    ? Text.New("no sends yet").FontSize(12f).TextColor(Azimuth.TextMuted).AutomationId("history-empty")
                    : Text.New($"0 matches for \"{_historyFind.Query}\"").FontSize(12f)
                        .TextColor(Azimuth.TextMuted).AutomationId("history-no-matches"),
                    _history.Count == 0
                        ? Text.New("Everything you send lands here, newest first.").FontSize(11.5f).TextColor(Azimuth.TextFaint)
                        : BuildClearRow()));
            return col;
        }

        var now = DateTimeOffset.Now;
        // VirtualList: history is the one surface that really does grow to
        // thousands of rows, and it is capped at ten thousand.
        var list = VirtualListElement<HistoryEntry>.New()
            .ItemHeight((int)Azimuth.RowRun)
            .Items(rows)
            .RenderWith(entry => HistoryRow(entry, now, rows.IndexOf(entry)))
            .AccessibleItemLabel(e =>
                $"{HistoryLog.TimeLabel(e, now)} {e.Method} {e.Url} {(e.Reached ? e.StatusCode.ToString() : e.Detail)}")
            .OnActivateItem(entry => { _historySelection = entry; MarkDirty(); })
            .FlexGrow(1);
        list.AutomationId("history-rows");
        col.AddChild(list);
        return col;
    }

    private Element BuildClearRow()
    {
        var row = Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center);
        row.AddChild(BarAction("clear search", "history-clear-search",
            () => { _historyFind.SetQuery(""); MarkDirty(); }));
        if (AnyFilterActive)
            row.AddChild(BarAction("drop filters", "history-clear-filters", ClearHistoryFilters));
        return row;
    }

    /// <summary><paramref name="index"/> only names the row for automation —
    /// virtualized rows are recycled, so a shared id would be ambiguous.</summary>
    private Element HistoryRow(HistoryEntry entry, DateTimeOffset now, int index)
    {
        bool today = HistoryLog.IsToday(entry, now);
        bool selected = _historySelection == entry;
        var (glyph, color) = entry.Reached ? StatusFace(entry.StatusCode) : ("!", Azimuth.StatusWarn);

        var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowRun)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName($"{HistoryLog.TimeLabel(entry, now)} {entry.Method} {entry.Url} {(entry.Reached ? entry.StatusCode.ToString() : entry.Detail)}")
            .AutomationId($"hist-row-{Math.Max(index, 0)}");
        if (selected) row.Background(Azimuth.Raised);
        // Older than today reads dimmer — the mock's 60%.
        if (!today) row.Opacity(0.6f);

        row.AddChild(Ui.Mono(HistoryLog.TimeLabel(entry, now), 10.5f, Azimuth.TextMuted).Width(96).FlexShrink(0));
        row.AddChild(MethodChip(entry.Method, treeScale: true));
        row.AddChild(Ui.Mono(entry.Url, 11.5f, Azimuth.TextDim)
            .FlexGrow(1).TextOverflow(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(Ui.Mono(entry.Reached ? $"{glyph} {entry.StatusCode}" : $"! {entry.Detail}", 11f, color)
            .FlexShrink(0));
        row.AddChild(Ui.Mono($"{entry.DurationMs:0} ms", 10.5f, Azimuth.TextMuted).Width(60).FlexShrink(0));
        row.AddChild(Ui.Mono(entry.EnvName ?? "—", 10f, Azimuth.TextFaint).Width(64).FlexShrink(0));

        var captured = entry;
        row.OnClick(_ => { _historySelection = captured; MarkDirty(); });
        if (!selected) row.HoverBg(Azimuth.Raised, Color.Transparent);
        return row;
    }

    /// <summary>The archived send, read-only. What it shows is what the log
    /// kept — the response body isn't stored, and the pane says so rather than
    /// implying an empty body.</summary>
    private Element BuildHistoryArchive(HistoryEntry entry)
    {
        var panel = Flex.Column(gap: Azimuth.S2).FlexShrink(0)
            .Width(Azimuth.ResponseRight)
            .Padding(new Thickness(Azimuth.S3))
            .BorderColor(Azimuth.BorderStrong).BorderWidth(new Thickness(2, 0, 0, 0));
        panel.AccessibleRole(AccessibilityRole.TabPanel).AccessibleName("Archived send");
        panel.AutomationId("history-archive");

        panel.AddChild(Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center).Children(
            Ui.Mono("archived · read-only", 10f, Azimuth.TextFaint),
            Box.New().FlexGrow(1),
            BarAction("Restore to editor", "history-restore", () => RestoreToEditor(entry))));

        panel.AddChild(Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center).Children(
            MethodChip(entry.Method),
            Ui.Mono(entry.Url, 11.5f, Azimuth.Text).FlexShrink(1)
                .TextOverflow(TextOverflow.Ellipsis).MaxLines(2)));

        var (glyph, color) = entry.Reached ? StatusFace(entry.StatusCode) : ("!", Azimuth.StatusWarn);
        panel.AddChild(Flex.Row(gap: Azimuth.S3).AlignItems(AlignItems.Center).Children(
            Ui.Mono(entry.Reached ? $"{glyph} {entry.StatusCode}" : $"! {entry.Detail}", 12f, color),
            Ui.Mono($"{entry.DurationMs:0} ms", 11f, Azimuth.TextMuted),
            Ui.Mono(HistoryLog.TimeLabel(entry, DateTimeOffset.Now), 11f, Azimuth.TextMuted),
            Ui.Mono(entry.EnvName is { } env ? $"env: {env}" : "no environment", 11f, Azimuth.TextMuted)));

        panel.AddChild(Ui.Mono(entry.RequestPath is { } path
            ? Path.GetFileName(path)
            : "no request file — sent ad hoc", 10.5f, Azimuth.TextFaint));

        panel.AddChild(Box.New().Height(Azimuth.S2));
        panel.AddChild(Text.New("The log keeps what was sent and how it went — not the response body.")
            .FontSize(11f).TextColor(Azimuth.TextFaint));
        panel.AddChild(Box.New().FlexGrow(1));
        return panel;
    }
}

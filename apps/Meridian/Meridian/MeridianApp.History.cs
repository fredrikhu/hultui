using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
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
    /// <summary>Bumped whenever <see cref="_history"/> changes, so the filtered rows are kept
    /// while it has not.</summary>
    private int _historyVersion;
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
        _historyVersion++;
        _historyLoaded = true;
    }

    private void EnsureHistoryLoaded()
    {
        if (_historyLoaded || _workspace is not { } ws) return;
        _history = HistoryLog.Read(ws.Root).ToList();
        _historyVersion++;
        _historyLoaded = true;
    }

    /// <summary>Everything <see cref="FilteredHistory"/> reads: the log's version, the filters,
    /// the query, and — with "today" on — the day.</summary>
    private readonly record struct HistoryFilter(int Version, string? Method, bool FailuresOnly, bool TodayOnly,
        DateTime Day, string? Env, string Query);

    private HistoryFilter _filteredFor;
    private List<HistoryEntry>? _filtered;

    /// <summary>The rows the filters and the query leave. The query matches the
    /// URL and the method, which is what someone types when hunting a send.</summary>
    /// <remarks>Kept while nothing it reads has changed: a key in the list asked for it three
    /// times a build, each a walk and a copy of the whole log (up to ten thousand sends). The
    /// list is the caller's to read, not to change.</remarks>
    internal List<HistoryEntry> FilteredHistory()
    {
        var now = DateTimeOffset.Now;
        var query = _historyFind.Query.Trim();
        var filter = new HistoryFilter(_historyVersion, _filterMethod, _filterFailuresOnly, _filterTodayOnly,
            _filterTodayOnly ? now.ToLocalTime().Date : default, _filterEnv, query);
        if (_filtered is { } kept && filter == _filteredFor) return kept;
        _filteredFor = filter;
        return _filtered = _history.Where(e =>
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
                SelectHistoryCursor(rows, Math.Min(_historyCursor + 1, rows.Count - 1));
                RevealHistoryCursor();
                return true;
            case KeyCode.Up when rows.Count > 0:
                // Clamped at the end too: a search or a filter can leave the cursor past the rows.
                SelectHistoryCursor(rows, Math.Clamp(_historyCursor - 1, 0, rows.Count - 1));
                RevealHistoryCursor();
                return true;
            case KeyCode.Enter when _historyCursor >= 0 && _historyCursor < rows.Count:
                SelectHistoryCursor(rows, _historyCursor);
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

    /// <summary>Puts the keyboard cursor on <paramref name="cursor"/> and selects that row's send.
    /// Only the list (which draws the selection) and the archived send beside it show the
    /// selection, so with both on screen the list is told through its ref and the archive builds
    /// alone: the window does not build. Otherwise — no archive yet (the first selection), or the
    /// list on screen made from other rows or on another day than these — the view builds.</summary>
    private void SelectHistoryCursor(List<HistoryEntry> rows, int cursor)
    {
        _historyCursor = cursor;
        var selected = _historySelection = rows[cursor];
        var now = DateTimeOffset.Now;
        if (_historyListRef.Current is not { IsHosted: true } list || _historyArchiveRef.Current is not { IsHosted: true }
            || !ReferenceEquals(rows, _historyListRows) || now.ToLocalTime().Date != _historyRowsDay)
        {
            MarkViewDirty();
            return;
        }
        // What BuildHistoryList hands the list: the index SelectedHistoryIndex finds (the cursor's,
        // the send being the cursor's row), and the highlight for the selected send's day.
        list.SelectedIndex = cursor;
        list.SelectionHighlight(HistoryHighlight(selected, now));
        if (ArchiveStale(selected)) _historyArchiveRevision.Value++;
    }

    /// <summary>Scrolls the list so the keyboard cursor's row is on screen. The arrows come to the
    /// app, not to the list, which is not focused and so does not follow them: a walk past the
    /// bottom left the selection below the screen. By the row's place, since the rows are one
    /// height and the cursor's may not have been made yet; after the build, which patches the list
    /// on screen, so the ref holds the live one.</summary>
    private void RevealHistoryCursor() =>
        _historyListRef.Current?.ScrollRangeIntoView(_historyCursor * Azimuth.RowRun, Azimuth.RowRun);

    private bool AnyFilterActive =>
        _filterMethod != null || _filterFailuresOnly || _filterTodayOnly || _filterEnv != null;

    private void ClearHistoryFilters()
    {
        _filterMethod = null;
        _filterFailuresOnly = false;
        _filterTodayOnly = false;
        _filterEnv = null;
        MarkViewDirty();
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
        // The archived send is a part of its own (see PlaceHistoryArchive), keyed by its host.
        if (_historySelection is { } selected)
            body.AddChild(PlaceHistoryArchive(selected).FlexShrink(0));
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

        // The find box gives way to the filter chips, down to MinWidth: a chip truncated to
        // "G…" no longer says which filter it is.
        var input = TextInput.New(_historyFind.Query).Style(Ui.Field)
            .Height(Dimension.Px(26)).Width(260).MinWidth(Dimension.Px(140));
        input.PlaceholderText("find in history…");
        // The search rebuilds the view itself when its query changes (it was made with
        // MarkViewDirty): a second rebuild here built the window twice a character.
        input.OnTextChanged(_historyFind.SetQuery);
        input.FontFamily(Fonts.Mono).FontSize(11.5f).TextColor(Azimuth.Text);
        input.WithRef(_historyFieldRef);
        input.AutomationId("history-search");
        bar.AddChild(input);

        foreach (var method in new[] { "GET", "POST", "PUT", "PATCH", "DELETE" })
            bar.AddChild(FilterChip(method, _filterMethod == method, Azimuth.MethodColor(method),
                "filter-" + method, () =>
                {
                    _filterMethod = _filterMethod == method ? null : method;
                    MarkViewDirty();
                }));
        bar.AddChild(FilterChip("failures", _filterFailuresOnly, Azimuth.StatusWarn, "filter-failures",
            () => { _filterFailuresOnly = !_filterFailuresOnly; MarkViewDirty(); }));
        bar.AddChild(FilterChip("today", _filterTodayOnly, Azimuth.Accent300, "filter-today",
            () => { _filterTodayOnly = !_filterTodayOnly; MarkViewDirty(); }));
        if (_envName is { } env)
            bar.AddChild(FilterChip($"env: {env}", _filterEnv == env, Azimuth.Accent300, "filter-env",
                () => { _filterEnv = _filterEnv == env ? null : env; MarkViewDirty(); }));

        bar.AddChild(Box.New().FlexGrow(1));
        bar.AddChild(Ui.Mono($"{FilteredHistory().Count} of {_history.Count}", 10.5f, Azimuth.TextMuted)
            .MaxLines(1).FlexShrink(0)
            .AutomationId("history-count"));
        return bar;
    }

    private Element FilterChip(string label, bool on, Color color, string automationId, Action onClick)
    {
        var chip = Box.New()
            .Padding(new Thickness(3, Azimuth.S2, 3, Azimuth.S2))
            .CornerRadius(Azimuth.Radius1)
            .FlexShrink(0)
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

        // A row's time reads against today (a clock time today, "yesterday", a weekday, a
        // date), so a new day makes the rows on screen, and their labels, again.
        var day = DateTimeOffset.Now.ToLocalTime().Date;
        if (day != _historyRowsDay)
        {
            _historyRowsDay = day;
            _historyListRef.Current?.InvalidateRows();
        }

        // VirtualList: history is the one surface that really does grow to
        // thousands of rows, and it is capped at ten thousand. Item rows: the list keeps a row
        // while its entry and its place are what they were, and draws the selection itself, under
        // rows made unselected, so a key makes no row. A row older than today draws its colours at
        // 60 %, its fill with them: the highlight under such a row is the fill at 60 %.
        _historyListRows = rows;
        var list = VirtualListElement<HistoryRowItem>.New()
            .ItemHeight((int)Azimuth.RowRun)
            .Items(new HistoryRowItems(rows))
            .RenderWith((item, _) => HistoryRow(item.Entry, item.Index))
            .SelectionHighlight(HistoryHighlight(_historySelection, DateTimeOffset.Now))
            // One delegate from build to build, so the list keeps each row's label too.
            .AccessibleItemLabel(_historyItemLabel ??= item => HistoryRowName(item.Entry, DateTimeOffset.Now))
            .OnActivateItem(item => { _historySelection = item.Entry; MarkViewDirty(); })
            .SelectedIndex(SelectedHistoryIndex(rows))
            .FlexGrow(1);
        list.AutomationId("history-rows");
        list.WithRef(_historyListRef);
        col.AddChild(list);
        return col;
    }

    /// <summary>A row of the History list as its list sees it: the entry, and where it sits
    /// among the filtered rows (which names the row for automation). Whether it is selected is
    /// the list's.</summary>
    internal readonly record struct HistoryRowItem(HistoryEntry Entry, int Index);

    /// <summary>The filtered rows as the list's items, each made when the list asks for it.</summary>
    private sealed class HistoryRowItems(List<HistoryEntry> rows) : IReadOnlyList<HistoryRowItem>
    {
        public int Count => rows.Count;
        public HistoryRowItem this[int index] => new(rows[index], index);

        public IEnumerator<HistoryRowItem> GetEnumerator()
        {
            for (int i = 0; i < rows.Count; i++) yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private readonly Ref<VirtualListElement<HistoryRowItem>> _historyListRef = new();
    private Func<HistoryRowItem, string?>? _historyItemLabel;
    private DateTime _historyRowsDay;
    /// <summary>The rows the last build handed the list (the filtered rows are kept while nothing
    /// they are made from changes, so the same list means the same rows).</summary>
    private List<HistoryEntry>? _historyListRows;

    /// <summary>The list's selection fill: the row's own, at the 60 % a row older than today is
    /// drawn at when the selected send is older.</summary>
    private static Color HistoryHighlight(HistoryEntry? selected, DateTimeOffset now) =>
        selected is { } shown && HistoryLog.IsToday(shown, now) ? Azimuth.Raised : Azimuth.Alpha(Azimuth.Raised, OlderRowOpacity);

    /// <summary>Rows History's template has made, for tests.</summary>
    internal int HistoryRowsMade { get; private set; }

    /// <summary>Where the selected send is among <paramref name="rows"/>: the keyboard cursor
    /// when it is on it, else the first row holding it; -1 when it is filtered out or none is.
    /// Once a build — the rows used to look their own place up, a walk of the list per row.</summary>
    private int SelectedHistoryIndex(List<HistoryEntry> rows)
    {
        if (_historySelection is not { } selected) return -1;
        if (_historyCursor >= 0 && _historyCursor < rows.Count && rows[_historyCursor] == selected) return _historyCursor;
        return rows.IndexOf(selected);
    }

    private static string HistoryRowName(HistoryEntry entry, DateTimeOffset now) =>
        $"{HistoryLog.TimeLabel(entry, now)} {entry.Method} {entry.Url} {(entry.Reached ? entry.StatusCode.ToString() : entry.Detail)}";

    private Element BuildClearRow()
    {
        var row = Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center);
        row.AddChild(BarAction("clear search", "history-clear-search", () => _historyFind.SetQuery("")));
        if (AnyFilterActive)
            row.AddChild(BarAction("drop filters", "history-clear-filters", ClearHistoryFilters));
        return row;
    }

    /// <summary>How opaque a row older than today is drawn — the mock's 60%. The row fades each
    /// colour it draws, not itself: <c>Opacity</c> on an element with children draws it offscreen,
    /// as a layer of its own, so every visible row was a layer on every History frame (29 at
    /// 1440×900, 20–32 % of a real frame on GL and Vulkan; plan 17 round 8 step 9).</summary>
    private const float OlderRowOpacity = 0.6f;

    /// <summary><paramref name="index"/> only names the row for automation —
    /// virtualized rows are recycled, so a shared id would be ambiguous. The row reads only its
    /// item and today's date (see BuildHistoryList); the list draws whether it is selected.</summary>
    private Element HistoryRow(HistoryEntry entry, int index)
    {
        HistoryRowsMade++;
        var now = DateTimeOffset.Now;
        bool today = HistoryLog.IsToday(entry, now);
        var (glyph, color) = entry.Reached ? StatusFace(entry.StatusCode) : ("!", Azimuth.StatusWarn);

        var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowRun)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(HistoryRowName(entry, now))
            .AutomationId($"hist-row-{Math.Max(index, 0)}");
        // Older than today reads dimmer: every colour the row draws at 60 % (see OlderRowOpacity).
        float opacity = today ? 1f : OlderRowOpacity;

        row.AddChild(Ui.Mono(HistoryLog.TimeLabel(entry, now), 10.5f, Faded(Azimuth.TextMuted, opacity)).Width(96).FlexShrink(0));
        row.AddChild(MethodChip(entry.Method, treeScale: true, opacity));
        row.AddChild(Ui.Mono(entry.Url, 11.5f, Faded(Azimuth.TextDim, opacity))
            .FlexGrow(1).TextOverflow(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(Ui.Mono(entry.Reached ? $"{glyph} {entry.StatusCode}" : $"! {entry.Detail}", 11f, Faded(color, opacity))
            .FlexShrink(0));
        row.AddChild(Ui.Mono($"{entry.DurationMs:0} ms", 10.5f, Faded(Azimuth.TextMuted, opacity)).Width(60).FlexShrink(0));
        row.AddChild(Ui.Mono(entry.EnvName ?? "—", 10f, Faded(Azimuth.TextFaint, opacity)).Width(64).FlexShrink(0));

        var captured = entry;
        row.OnClick(_ => { _historySelection = captured; MarkViewDirty(); });
        // The row's own hover tint, so a hovered row older than today stays at its 60 %: the list's
        // HoverHighlight would draw it whole. Over the highlight it is the highlight's colour again
        // (a row older than today hovered while selected reads a little lighter).
        row.HoverBg(Faded(Azimuth.Raised, opacity), Color.Transparent);
        return row;
    }

    /// <summary>A History row's colour at the row's <paramref name="opacity"/>.</summary>
    private static Color Faded(Color c, float opacity) => opacity < 1f ? Azimuth.Fade(c, opacity) : c;

    // ── The archived send: a part of its own ─────────────────────────────
    //
    // The panel beside the list shows the selected send, and nothing else in the view shows the
    // selection but the list, which draws it. A key built the whole window for it (52 KB a key,
    // 15 KB of it the panel, 23 KB the filter bar and the footer), and a search character built the
    // panel again for a send that had not moved. The panel is now a part (see Part) with a revision
    // of its own: a key tells the list through its ref and writes the revision, so the panel builds
    // alone (SelectHistoryCursor). It builds with the view when the view places it showing another
    // send than the selected one, when it read its time against another day, and after a full
    // rebuild (_partRevision, safe by default); a view-only build that leaves the selection where it
    // was — a search character, a filter, a chip — leaves it as it is.

    private readonly PartGroup _historyParts = new();
    private readonly Signal<int> _historyArchiveRevision = new(0);
    private Part? _historyArchivePart;
    private readonly Ref<BoxElement> _historyArchiveRef = new();
    /// <summary>What the archive was last built from: the send, the day its time reads against,
    /// and the full-rebuild revision.</summary>
    private HistoryEntry? _archiveShown;
    private DateTime _archiveDay;
    private int _archiveRevision = -1;

    /// <summary>Builds of the archived-send panel so far, for tests.</summary>
    internal int HistoryArchiveBuilds => _historyParts.Builds;

    /// <summary>Whether the archive on screen is not what a build of it for <paramref name="selected"/>
    /// makes now.</summary>
    private bool ArchiveStale(HistoryEntry selected) =>
        !ReferenceEquals(_archiveShown, selected) || _archiveDay != DateTimeOffset.Now.ToLocalTime().Date
        || _archiveRevision != _partRevision.Peek();

    /// <summary>The archive's host, for the view's build to place beside the list: built again
    /// first when it is stale (<see cref="ArchiveStale"/>). The host takes the panel's place in the
    /// row: it never shrinks, and the panel fills its height as it filled the row's.</summary>
    private ComponentHost PlaceHistoryArchive(HistoryEntry selected)
    {
        var part = _historyArchivePart ??= new(_historyParts, () =>
        {
            // Placed only beside a selected send, and its own build runs after a key selected one;
            // nothing takes the selection away.
            if (_historySelection is not { } entry) return Box.New();
            _archiveShown = entry;
            _archiveDay = DateTimeOffset.Now.ToLocalTime().Date;
            _archiveRevision = _partRevision.Peek();
            var archive = BuildHistoryArchive(entry);
            archive.Key = "history-archive";
            archive.FlexGrow(1);
            archive.WithRef(_historyArchiveRef);
            return archive;
        }, own: _historyArchiveRevision);
        part.Mount();   // the first build, before asking what it was built from
        return part.Place(stale: ArchiveStale(selected));
    }

    /// <summary>The archived send, read-only. What it shows is what the log
    /// kept — the response body isn't stored, and the pane says so rather than
    /// implying an empty body.</summary>
    private BoxElement BuildHistoryArchive(HistoryEntry entry)
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

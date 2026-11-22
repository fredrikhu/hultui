using HultUI.Core.Events;
using HultUI.Core.Layout;

namespace HultUI.Core.Builders;

/// <summary>
/// The stateful half of a table: its columns, which are shown and how wide, and how the rows
/// are sorted. A model, not a control, like <see cref="TreeView{T}"/>: it sorts the rows,
/// builds the header and lays each row's cells on the same grid as the header, and the app
/// keeps the list, the selection and anything else its rows do.
/// </summary>
/// <remarks>
/// <para><b>Use.</b> Hold one per table. Each build, show <see cref="Header"/> above a virtual
/// list of <see cref="Sort"/>ed items whose template is <see cref="Row"/>. Sorting is memoized
/// on the list's identity, so an unchanged rebuild costs a reference check.</para>
///
/// <para><b>Widths.</b> A column is fixed (its <see cref="TableColumn{T}.Width"/>, then whatever
/// the user dragged it to) or a fill, taking a share of what the fixed ones leave. Header and
/// rows share one set of grid tracks, so a cell can never drift from its header.</para>
///
/// <para><b>State.</b> <see cref="State"/> is the sort, the dragged widths and the hidden
/// columns as plain data; <see cref="StateChanged"/> runs when one of them settles (a click, a
/// drag let go), which is when to save it.</para>
///
/// <para><b>Sorting by the app.</b> With <see cref="SortMode"/> = <see cref="TableSortMode.External"/>
/// the table never sorts. The header shows <see cref="SortKeys"/>, which the app sets with
/// <see cref="SetSortKeys"/> once its rows are in that order, and a press raises
/// <see cref="SortRequested"/> instead of sorting.</para>
/// </remarks>
public sealed partial class TableView<T>
{
    private readonly Action _onChanged;
    private readonly List<TableColumn<T>> _columns;
    private readonly Dictionary<string, float> _widths = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);
    private List<TableColumn<T>>? _visible;

    private string? _sortColumn;
    private SortDirection _sortDirection;
    // Every key, first key first; _sortColumn and _sortDirection mirror the first.
    private TableSortKey[] _sortKeys = [];
    private TableSortMode _sortMode;
    // The id of the column whose header press awaits the app's answer: an id, never an element —
    // header cells are patched by position, so one held across frames may be another column's.
    private string? _announceFor;

    private IReadOnlyList<T>? _sortedFrom;
    private string? _sortedBy;
    private SortDirection _sortedDirection;
    private Func<T, int>? _sortedGroup;
    private IReadOnlyList<T> _sorted = [];

    /// <param name="columns">The columns, in order. Ids must be distinct.</param>
    /// <param name="onChanged">Runs when anything the table shows changes — the app's
    /// <c>MarkDirty</c>.</param>
    public TableView(IEnumerable<TableColumn<T>> columns, Action onChanged)
    {
        _columns = [.. columns];
        _onChanged = onChanged;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in _columns)
            if (!seen.Add(column.Id))
                throw new ArgumentException($"Two columns have the id \"{column.Id}\".", nameof(columns));
    }

    /// <summary>Runs when the sort, a width or the hidden columns settle: save
    /// <see cref="State"/> here.</summary>
    public Action? StateChanged { get; set; }

    /// <summary>
    /// Rows that stay where they are whatever the sort: the group comes first, smallest first,
    /// and only then the column, in either direction — a file list's ".." and its folders above
    /// its files. Null sorts everything together.
    /// </summary>
    public Func<T, int>? Group { get; set; }

    public IReadOnlyList<TableColumn<T>> Columns => _columns;

    /// <summary>The columns shown, in order.</summary>
    public IReadOnlyList<TableColumn<T>> VisibleColumns =>
        _visible ??= _columns.Where(IsVisible).ToList();

    public TableColumn<T>? Column(string id) => _columns.Find(c => c.Id == id);

    // ──── Visibility ────

    public bool IsVisible(TableColumn<T> column) =>
        column.HiddenByDefault ? _shown.Contains(column.Id) : !_hidden.Contains(column.Id);

    /// <summary>Shows or hides a column; a column that cannot be hidden stays.</summary>
    public void SetVisible(string id, bool visible)
    {
        if (Column(id) is not { } column || IsVisible(column) == visible) return;
        if (!visible && !column.CanHide) return;
        if (column.HiddenByDefault)
        {
            if (visible) _shown.Add(id); else _shown.Remove(id);
        }
        else
        {
            if (visible) _hidden.Remove(id); else _hidden.Add(id);
        }
        _visible = null;
        Changed(settled: true);
    }

    // ──── Sorting ────

    /// <summary>The column the rows are sorted by; null for the order they came in.</summary>
    public string? SortColumn => _sortColumn;
    public SortDirection SortDirection => _sortDirection;

    /// <summary>Who sorts the rows. Set it once, where the table is made; switching drops the
    /// remembered sort.</summary>
    public TableSortMode SortMode
    {
        get => _sortMode;
        set
        {
            if (_sortMode == value) return;
            _sortMode = value;
            _sortedFrom = null;
            _sorted = [];
            _announceFor = null;
            if (value == TableSortMode.Internal)
            {
                // The table sorts by one column, and only by one with a Compare.
                if (_sortColumn is { } id && Column(id) is { IsSortable: true })
                    SetPrimary(id, _sortDirection);
                else
                    SetPrimary(null, _sortDirection);
            }
            _onChanged();
        }
    }

    /// <summary>The keys the rows are sorted by, first key first; empty when unsorted. In
    /// <see cref="TableSortMode.Internal"/> mode at most one, the same as <see cref="SortColumn"/>
    /// and <see cref="SortDirection"/>.</summary>
    public IReadOnlyList<TableSortKey> SortKeys => _sortKeys;

    /// <summary>Runs on a press on a sortable header in <see cref="TableSortMode.External"/> mode
    /// (and on <see cref="SortBy"/>): sort the rows, then call <see cref="SetSortKeys"/>. Nothing
    /// changes until then; with no handler, a press does nothing. The next change of the keys after
    /// a header press is announced as its answer, from that header: an app that declines a press
    /// leaves it waiting, until its next request.</summary>
    public Action<TableSortRequest>? SortRequested { get; set; }

    /// <summary>Whether the header sorts by <paramref name="column"/>: it has a
    /// <see cref="TableColumn{T}.Compare"/>, or the app sorts (<see cref="TableSortMode.External"/>)
    /// and the column says <see cref="TableColumn{T}.CanSort"/>.</summary>
    public bool CanSortBy(TableColumn<T> column) =>
        column.Compare is not null || (_sortMode == TableSortMode.External && column.CanSort);

    /// <summary>
    /// Sorts by <paramref name="id"/>: in <paramref name="direction"/> when given, else the way a
    /// header click does — the other way round when it is already the sort column, else the
    /// column's <see cref="TableColumn{T}.FirstSort"/>. A column that cannot sort is ignored. In
    /// <see cref="TableSortMode.External"/> mode it asks the app instead (<see cref="SortRequested"/>).
    /// </summary>
    public void SortBy(string id, SortDirection? direction = null)
    {
        if (Column(id) is not { } column || !CanSortBy(column)) return;
        if (_sortMode == TableSortMode.External)
        {
            Request(column, direction, KeyModifiers.None, fromHeader: false);
            return;
        }
        SetPrimary(id, direction
            ?? (_sortColumn == id
                ? (_sortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending)
                : column.FirstSort));
        Changed(settled: true);
    }

    /// <summary>
    /// Sets the sort from the app's side without reporting it — for an app that keeps the sort
    /// itself (one per tab, say) and sorts its own rows, telling the header what to show. A
    /// column that cannot sort, or null, clears it.
    /// </summary>
    public void SetSort(string? id, SortDirection direction)
    {
        if (_sortMode == TableSortMode.External)
        {
            SetSortKeys(id is null ? [] : [new TableSortKey(id, direction)]);
            return;
        }
        var column = id is null ? null : Column(id);
        string? next = column is not null && CanSortBy(column) ? id : null;
        if (next == _sortColumn && direction == _sortDirection) return;
        SetPrimary(next, direction);
        _onChanged();
    }

    /// <summary>Back to the order the rows came in.</summary>
    public void ClearSort()
    {
        if (_sortMode == TableSortMode.External)
        {
            SetSortKeys([]);
            return;
        }
        if (_sortColumn is null) return;
        SetPrimary(null, _sortDirection);
        Changed(settled: true);
    }

    /// <summary>
    /// Tells the table the order the rows are now in, first key first, without reporting it.
    /// Unknown ids, columns that cannot sort and repeated ids are skipped. Call on the UI thread
    /// once the rows passed to <see cref="Body"/> are in this order. In
    /// <see cref="TableSortMode.Internal"/> mode, one key at most.
    /// </summary>
    /// <exception cref="InvalidOperationException">More than one key, and the table sorts its own rows.</exception>
    public void SetSortKeys(IReadOnlyList<TableSortKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var kept = new List<TableSortKey>(keys.Count);
        foreach (var key in keys)
        {
            if (Column(key.ColumnId) is not { } column || !CanSortBy(column)) continue;
            if (kept.Exists(k => k.ColumnId == key.ColumnId)) continue;
            kept.Add(key);
        }
        if (_sortMode == TableSortMode.Internal)
        {
            if (kept.Count > 1)
                throw new InvalidOperationException(
                    "A table that sorts its own rows sorts by one column; set SortMode to TableSortMode.External to show several sort keys.");
            SetSort(kept.Count == 1 ? kept[0].ColumnId : null, kept.Count == 1 ? kept[0].Direction : _sortDirection);
            return;
        }
        // The same keys again (an app that asserts its sort in every build) change nothing, and
        // leave a press's answer still to be announced.
        if (kept.Count == _sortKeys.Length && kept.SequenceEqual(_sortKeys)) return;
        _sortKeys = [.. kept];
        _sortColumn = kept.Count > 0 ? kept[0].ColumnId : null;
        if (kept.Count > 0) _sortDirection = kept[0].Direction;
        _onChanged();
        if (_announceFor is { } id)
        {
            _announceFor = null;
            AnnounceSort(id);
        }
    }

    private void SetPrimary(string? id, SortDirection direction)
    {
        _sortColumn = id;
        _sortDirection = direction;
        _sortKeys = id is null ? [] : [new TableSortKey(id, direction)];
    }

    /// <summary>Asks the app for a sort (<see cref="SortRequested"/>); a press on a header waits
    /// to have the answer announced.</summary>
    private void Request(TableColumn<T> column, SortDirection? direction, KeyModifiers modifiers, bool fromHeader)
    {
        if (SortRequested is not { } requested) return;
        _announceFor = fromHeader ? column.Id : null;
        requested(new TableSortRequest(column.Id, modifiers, Suggest(column, direction, modifiers)));
    }

    /// <summary>The keys the table would sort by itself: without Shift, the column alone — the other
    /// way round when it is the first key, else its <see cref="TableColumn{T}.FirstSort"/>; with Shift
    /// and a sort, the keys with the column turned round where it is one, else added after them.
    /// A direction given wins.</summary>
    private TableSortKey[] Suggest(TableColumn<T> column, SortDirection? direction, KeyModifiers modifiers)
    {
        if (direction is { } given) return [new TableSortKey(column.Id, given)];
        int rank = RankOf(column.Id);
        if ((modifiers & KeyModifiers.Shift) == 0 || _sortKeys.Length == 0)
            return [new TableSortKey(column.Id, rank == 0 ? Flip(_sortKeys[0].Direction) : column.FirstSort)];
        if (rank < 0) return [.. _sortKeys, new TableSortKey(column.Id, column.FirstSort)];
        var keys = (TableSortKey[])_sortKeys.Clone();
        keys[rank] = keys[rank] with { Direction = Flip(keys[rank].Direction) };
        return keys;
    }

    private static SortDirection Flip(SortDirection direction) =>
        direction == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;

    /// <summary>Where <paramref name="id"/> is among the sort keys; -1 when it is not one.</summary>
    private int RankOf(string id)
    {
        for (int i = 0; i < _sortKeys.Length; i++)
            if (_sortKeys[i].ColumnId == id) return i;
        return -1;
    }

    /// <summary>The sort in words, for a reader: "Sorted by Name, ascending, then Size, descending".</summary>
    private string SortSummary()
    {
        if (_sortKeys.Length == 0) return "Not sorted";
        var parts = new string[_sortKeys.Length];
        for (int i = 0; i < parts.Length; i++)
            parts[i] = $"{Column(_sortKeys[i].ColumnId)!.Header}, {(_sortKeys[i].Direction == SortDirection.Ascending ? "ascending" : "descending")}";
        return "Sorted by " + string.Join(", then ", parts);
    }

    /// <summary>
    /// <paramref name="items"/> in the current sort: the same list when there is none, else a
    /// sorted copy, stable — rows that compare equal keep the order they came in. Remembered
    /// until the list or the sort changes, so call it every build. In
    /// <see cref="TableSortMode.External"/> mode, the list as given: the table never sorts.
    /// </summary>
    public IReadOnlyList<T> Sort(IReadOnlyList<T> items)
    {
        if (_sortMode == TableSortMode.External) return items;
        if (_sortColumn is null || Column(_sortColumn) is not { Compare: { } compare })
            return items;
        if (ReferenceEquals(items, _sortedFrom) && _sortedBy == _sortColumn && _sortedDirection == _sortDirection
            && ReferenceEquals(_sortedGroup, Group))
            return _sorted;

        var order = new int[items.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        int sign = _sortDirection == SortDirection.Ascending ? 1 : -1;
        var group = Group;
        int[]? groups = null;
        if (group is not null)
        {
            groups = new int[items.Count];
            for (int i = 0; i < groups.Length; i++) groups[i] = group(items[i]);
        }
        Array.Sort(order, (a, b) =>
        {
            if (groups is not null && groups[a] != groups[b]) return groups[a].CompareTo(groups[b]);
            int c = compare(items[a], items[b]) * sign;
            return c != 0 ? c : a.CompareTo(b);
        });
        var sorted = new T[order.Length];
        for (int i = 0; i < order.Length; i++) sorted[i] = items[order[i]];

        _sortedFrom = items;
        _sortedBy = _sortColumn;
        _sortedDirection = _sortDirection;
        _sortedGroup = group;
        _sorted = sorted;
        return sorted;
    }

    // ──── Widths ────

    /// <summary>A fixed column's width: what the user dragged it to, else its own. A fill
    /// column's is whatever the layout gives it; this returns its minimum.</summary>
    public float WidthOf(TableColumn<T> column)
    {
        if (column.IsFill) return column.MinWidth;
        float width = _widths.TryGetValue(column.Id, out var w) ? w : column.Width;
        return Math.Clamp(width, column.MinWidth, column.MaxWidth);
    }

    /// <summary>Sets a fixed column's width, clamped to its range. <paramref name="settled"/>
    /// false while a drag is still moving, so the state is saved once, when it lets go.</summary>
    public void SetWidth(string id, float width, bool settled = true)
    {
        if (Column(id) is not { IsFill: false } column) return;
        width = Math.Clamp(width, column.MinWidth, column.MaxWidth);
        if (Math.Abs(width - WidthOf(column)) < 0.5f && !settled) return;
        _widths[id] = width;
        Changed(settled);
    }

    /// <summary>The grid columns the header and every row are laid on.</summary>
    public GridTrack[] Tracks()
    {
        var visible = VisibleColumns;
        var tracks = new GridTrack[visible.Count];
        for (int i = 0; i < tracks.Length; i++)
        {
            var column = visible[i];
            tracks[i] = column.Fill is { } share ? GridTrack.Fr(share) : GridTrack.Fixed(WidthOf(column));
        }
        return tracks;
    }

    // ──── State ────

    /// <summary>The sort, the dragged widths and the hidden columns, for a settings file.</summary>
    public TableState State => new()
    {
        SortColumn = _sortColumn,
        SortDirection = _sortDirection,
        Widths = new Dictionary<string, float>(_widths, StringComparer.Ordinal),
        Hidden = [.. _hidden],
        Shown = [.. _shown],
    };

    /// <summary>Puts back what <see cref="State"/> gave. Columns that no longer exist are
    /// skipped, so a state saved by another version of the app still applies. No
    /// <see cref="StateChanged"/>: this is the app telling the table. In
    /// <see cref="TableSortMode.External"/> mode the sort is left alone: the app sets the keys.</summary>
    public void Restore(TableState? state)
    {
        if (state is null) return;
        _widths.Clear();
        foreach (var (id, width) in state.Widths)
            if (Column(id) is { IsFill: false }) _widths[id] = width;
        _hidden.Clear();
        foreach (var id in state.Hidden)
            if (Column(id) is { CanHide: true }) _hidden.Add(id);
        _shown.Clear();
        foreach (var id in state.Shown)
            if (Column(id) is { HiddenByDefault: true }) _shown.Add(id);
        // The app keeps the sort of a table it sorts: it sets the keys.
        if (_sortMode == TableSortMode.Internal)
            SetPrimary(state.SortColumn is { } sort && Column(sort) is { } sorted && CanSortBy(sorted) ? sort : null,
                state.SortDirection);
        _visible = null;
        _onChanged();
    }

    private void Changed(bool settled)
    {
        _onChanged();
        if (settled) StateChanged?.Invoke();
    }
}

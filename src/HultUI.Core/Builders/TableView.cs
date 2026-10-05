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

    /// <summary>
    /// Sorts by <paramref name="id"/>: in <paramref name="direction"/> when given, else the way a
    /// header click does — the other way round when it is already the sort column, else the
    /// column's <see cref="TableColumn{T}.FirstSort"/>. An unsortable column is ignored.
    /// </summary>
    public void SortBy(string id, SortDirection? direction = null)
    {
        if (Column(id) is not { IsSortable: true } column) return;
        _sortDirection = direction
            ?? (_sortColumn == id
                ? (_sortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending)
                : column.FirstSort);
        _sortColumn = id;
        Changed(settled: true);
    }

    /// <summary>
    /// Sets the sort from the app's side without reporting it — for an app that keeps the sort
    /// itself (one per tab, say) and sorts its own rows, telling the header what to show. A
    /// column that cannot sort, or null, clears it.
    /// </summary>
    public void SetSort(string? id, SortDirection direction)
    {
        var column = id is null ? null : Column(id);
        string? next = column is { IsSortable: true } ? id : null;
        if (next == _sortColumn && direction == _sortDirection) return;
        _sortColumn = next;
        _sortDirection = direction;
        _onChanged();
    }

    /// <summary>Back to the order the rows came in.</summary>
    public void ClearSort()
    {
        if (_sortColumn is null) return;
        _sortColumn = null;
        Changed(settled: true);
    }

    /// <summary>
    /// <paramref name="items"/> in the current sort: the same list when there is none, else a
    /// sorted copy, stable — rows that compare equal keep the order they came in. Remembered
    /// until the list or the sort changes, so call it every build.
    /// </summary>
    public IReadOnlyList<T> Sort(IReadOnlyList<T> items)
    {
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
    /// <see cref="StateChanged"/>: this is the app telling the table.</summary>
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
        _sortColumn = state.SortColumn is { } sort && Column(sort) is { IsSortable: true } ? sort : null;
        _sortDirection = state.SortDirection;
        _visible = null;
        _onChanged();
    }

    private void Changed(bool settled)
    {
        _onChanged();
        if (settled) StateChanged?.Invoke();
    }
}

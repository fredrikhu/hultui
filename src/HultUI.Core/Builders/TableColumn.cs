using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

/// <summary>Which way a <see cref="TableView{T}"/> is sorted.</summary>
public enum SortDirection : byte
{
    Ascending,
    Descending,
}

/// <summary>Where a column's cells and header sit across the column: numbers and sizes go to
/// the end, so their digits line up.</summary>
public enum ColumnAlign : byte
{
    Start,
    Center,
    End,
}

/// <summary>
/// One column of a <see cref="TableView{T}"/>: its header, what a cell shows, how wide it is,
/// and how it sorts. Immutable; the table holds what changes (widths, sort, hidden columns).
/// </summary>
public sealed class TableColumn<T>
{
    /// <summary>Names the column in remembered state (<see cref="TableState"/>), so it must not
    /// change between versions of the app — the header text may.</summary>
    public required string Id { get; init; }

    /// <summary>The header's text, and the column's name to assistive technology.</summary>
    public required string Header { get; init; }

    /// <summary>A plain cell: one line of text, cut with an ellipsis. Ignored when
    /// <see cref="Cell"/> is set.</summary>
    public Func<T, string>? Text { get; init; }

    /// <summary>A cell of any content — an icon and a name, a badge, a progress bar.</summary>
    public Func<T, Element>? Cell { get; init; }

    /// <summary>The starting width, in layout units. A column with <see cref="Fill"/> set
    /// takes its share of what is left instead, and cannot be resized.</summary>
    public float Width { get; init; } = 120f;

    /// <summary>A share of the width the fixed columns leave, as a grid's fr unit: the name
    /// column of a file list, the subject of a commit list. Null for a fixed column.</summary>
    public float? Fill { get; init; }

    /// <summary>The least a resize may make it.</summary>
    public float MinWidth { get; init; } = 32f;

    /// <summary>The most a resize may make it.</summary>
    public float MaxWidth { get; init; } = 2000f;

    public ColumnAlign Align { get; init; } = ColumnAlign.Start;

    /// <summary>Orders two rows by this column, ascending; null makes the column unsortable.
    /// Ties keep the order the rows came in.</summary>
    public Comparison<T>? Compare { get; init; }

    /// <summary>The direction the first click on the header sorts in: ascending for names,
    /// descending for sizes and dates, where the large and the recent are what is wanted.</summary>
    public SortDirection FirstSort { get; init; } = SortDirection.Ascending;

    /// <summary>Whether dragging the divider at its end resizes it. Always false for a
    /// <see cref="Fill"/> column, which takes what is left.</summary>
    public bool Resizable { get; init; } = true;

    /// <summary>Whether the user may hide it. The column that names the row should not.</summary>
    public bool CanHide { get; init; } = true;

    /// <summary>Starts hidden, until the user shows it.</summary>
    public bool HiddenByDefault { get; init; }

    public bool IsSortable => Compare is not null;
    public bool IsFill => Fill is not null;
}

/// <summary>
/// What a <see cref="TableView{T}"/> remembers between runs: the sort, the widths the user
/// dragged, the columns they hid. Plain data, for a settings file.
/// </summary>
public sealed record TableState
{
    public string? SortColumn { get; init; }
    public SortDirection SortDirection { get; init; }
    public Dictionary<string, float> Widths { get; init; } = [];
    public List<string> Hidden { get; init; } = [];
    /// <summary>Hidden-by-default columns the user chose to show.</summary>
    public List<string> Shown { get; init; } = [];
}

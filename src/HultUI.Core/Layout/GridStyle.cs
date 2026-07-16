namespace HultUI.Core.Layout;

public sealed class GridStyle
{
    /// <summary>Shared all-defaults instance for elements that never set grid
    /// properties. Read-only by contract — never mutate.</summary>
    internal static readonly GridStyle Default = new();

    public GridTrack[] ColumnTemplate { get; set; } = [];
    public GridTrack[] RowTemplate { get; set; } = [];

    /// <summary>Used when a cell's row index exceeds <see cref="RowTemplate"/>.</summary>
    public GridTrack ImplicitRow { get; set; } = GridTrack.Auto;

    /// <summary>Used when a cell's column index exceeds <see cref="ColumnTemplate"/>.</summary>
    public GridTrack ImplicitColumn { get; set; } = GridTrack.Auto;

    public float RowGap { get; set; }
    public float ColumnGap { get; set; }

    public JustifyItems JustifyItems { get; set; } = JustifyItems.Stretch;
    public AlignItems AlignItems { get; set; } = AlignItems.Stretch;

    public bool HasTemplate => ColumnTemplate.Length > 0 || RowTemplate.Length > 0;

    internal void CopyFrom(GridStyle source)
    {
        ColumnTemplate = source.ColumnTemplate;
        RowTemplate = source.RowTemplate;
        ImplicitRow = source.ImplicitRow;
        ImplicitColumn = source.ImplicitColumn;
        RowGap = source.RowGap;
        ColumnGap = source.ColumnGap;
        JustifyItems = source.JustifyItems;
        AlignItems = source.AlignItems;
    }
}

public enum JustifyItems : byte
{
    Start,
    End,
    Center,
    Stretch,
}

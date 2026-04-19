using HultUI.Core.Elements;
using HultUI.Core.Layout;

namespace HultUI.Core.Builders;

public static class Grid
{
    /// <summary>
    /// Create a grid container from CSS-grid-style template strings.
    /// Examples: <c>Grid.Of("200px auto 1fr")</c>, <c>Grid.Of("auto 1fr", "auto auto")</c>.
    /// </summary>
    public static BoxElement Of(string columns, string rows = "auto", float gap = 0)
    {
        var box = BoxElement.New();
        box.Grid.ColumnTemplate = GridTemplate.Parse(columns);
        box.Grid.RowTemplate = GridTemplate.Parse(rows);
        box.Grid.RowGap = gap;
        box.Grid.ColumnGap = gap;
        return box;
    }

    /// <summary>
    /// Create a grid container from explicit track arrays. Use when you want to
    /// build templates programmatically without going through the string parser.
    /// </summary>
    public static BoxElement Of(GridTrack[] columns, GridTrack[] rows, float gap = 0)
    {
        var box = BoxElement.New();
        box.Grid.ColumnTemplate = columns;
        box.Grid.RowTemplate = rows;
        box.Grid.RowGap = gap;
        box.Grid.ColumnGap = gap;
        return box;
    }
}

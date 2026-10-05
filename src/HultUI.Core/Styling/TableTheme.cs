using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// How a <see cref="Builders.TableView{T}"/>'s header and cells look. Colours left null come
/// from the palette.
/// </summary>
public sealed class TableTheme : IThemeExtension<TableTheme>
{
    public float HeaderHeight { get; init; } = 28f;
    /// <summary>The header's fill; null leaves it transparent.</summary>
    public Color? HeaderBackground { get; init; }
    /// <summary>A header under the pointer, when it sorts; null takes the palette's surface variant.</summary>
    public Color? HeaderHoverBackground { get; init; }
    /// <summary>The header's text; null takes the palette's secondary colour.</summary>
    public Color? HeaderText { get; init; }
    public float HeaderFontSize { get; init; } = 12f;
    public FontWeight HeaderFontWeight { get; init; } = FontWeight.SemiBold;
    /// <summary>Extra space between the header's letters — for upper-case headers, which read
    /// cramped without it. Null leaves the spacing as it is.</summary>
    public float? HeaderLetterSpacing { get; init; }
    /// <summary>The rule under the header and the marks between its columns; null takes the
    /// palette's border colour.</summary>
    public Color? Divider { get; init; }
    /// <summary>Whether a mark separates the header's columns.</summary>
    public bool ColumnDividers { get; init; } = true;

    /// <summary>Around every cell's content, header and rows alike, so they line up.</summary>
    public Thickness CellPadding { get; init; } = new(0, 8);
    /// <summary>The cells' text size; null inherits.</summary>
    public float? CellFontSize { get; init; }

    /// <summary>The up or down mark beside the sorted column's name; null takes the header text.</summary>
    public Color? SortMark { get; init; }
    /// <summary>How wide the grab area at a column's end is.</summary>
    public float ResizeGrip { get; init; } = 7f;

    public static TableTheme CreateDefault(Theme theme) => new();
}

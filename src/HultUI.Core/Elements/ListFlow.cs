namespace HultUI.Core.Elements;

/// <summary>How a <see cref="VirtualListElement{T}"/> places its items.</summary>
public enum ListFlow : byte
{
    /// <summary>One item per row, top to bottom; the list scrolls vertically. The default.</summary>
    Rows,
    /// <summary>Items fill a column top to bottom, then the next column to the right; the list
    /// scrolls sideways and never vertically (a file manager's Brief view, Windows' list mode).</summary>
    Columns,
}

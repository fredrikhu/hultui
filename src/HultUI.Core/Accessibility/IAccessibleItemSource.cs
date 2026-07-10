namespace HultUI.Core.Accessibility;

/// <summary>
/// Implemented by virtualized containers (e.g. <c>VirtualListElement</c>) so the
/// accessibility layer can expose <em>every</em> item — not just the on-screen
/// rows the container materializes — without forcing it to realize elements.
/// </summary>
/// <remarks>
/// Items are accessed lazily by index. Today the tree builder reads all of them
/// to emit one <see cref="AccessibilityRole.ListItem"/> node per item; the same
/// index-based contract lets a future on-demand provider serve items without
/// materializing them all, so this scales toward very large lists.
/// </remarks>
public interface IAccessibleItemSource
{
    /// <summary>Total number of items — the virtual count, not the realized count.</summary>
    int ItemCount { get; }

    /// <summary>Fixed height of each item in layout units, used to compute item positions.</summary>
    float ItemHeight { get; }

    /// <summary>
    /// Accessible label for the item at <paramref name="index"/>, or <c>null</c>.
    /// Must be cheap: read from item data, never materialize the item's template
    /// element (that would defeat virtualization).
    /// </summary>
    string? GetItemLabel(int index);
}

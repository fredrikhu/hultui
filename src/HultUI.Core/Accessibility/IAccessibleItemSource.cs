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
    /// Index of the currently selected item, or -1 if none. Exposed so assistive
    /// tech can report and move to the selection (UIA SelectionItem). Single-select.
    /// </summary>
    int SelectedIndex { get; }

    /// <summary>
    /// Accessible label for the item at <paramref name="index"/>, or <c>null</c>.
    /// Must be cheap: read from item data, never materialize the item's template
    /// element (that would defeat virtualization).
    /// </summary>
    string? GetItemLabel(int index);

    /// <summary>
    /// Performs the item's primary action (as a click would) when assistive tech
    /// invokes it — the container routes this to the item at <paramref name="index"/>,
    /// so even off-screen, unrealized items can be activated. A no-op if the
    /// container has no activation wired.
    /// </summary>
    void ActivateItem(int index);
}

using System.Linq;

namespace HultUI.Core.Accessibility;

/// <summary>Which item property an <see cref="AccessibleItemQuery"/> matches on.</summary>
public enum AccessibleItemProperty
{
    /// <summary>Match any item (used to enumerate from a starting point).</summary>
    Any,
    /// <summary>Match an item whose accessible label equals <see cref="AccessibleItemQuery.Name"/>.</summary>
    Name,
    /// <summary>Match a selected item.</summary>
    Selected,
}

/// <summary>
/// A property-based item lookup, passed to <see cref="IAccessibleItemSource.FindItem"/>.
/// The platform-neutral form of UIA's <c>ItemContainer.FindItemByProperty</c>, so a
/// source backed by an index can answer without a linear scan.
/// </summary>
public readonly record struct AccessibleItemQuery(AccessibleItemProperty Property, string? Name = null);

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

    /// <summary>
    /// Whether each item should be published as one <see cref="AccessibilityRole.ListItem"/>
    /// node named by <see cref="GetItemLabel"/>. True for an ordinary list, where a row is
    /// one thing with one name and one action.
    ///
    /// <para>False when a row is <i>composite</i> — a grid row holding several cards, each
    /// its own button. Publishing one node per row would then hide every control inside it
    /// from assistive tech and from automation, so the builder walks the realized rows
    /// instead and the container's own children answer for themselves. The cost is that
    /// off-screen items are not in the tree: a container that turns this off is saying its
    /// rows are made of real controls, and unrealized controls cannot be offered.</para>
    /// </summary>
    bool PublishesItemNodes => true;

    /// <summary>Fixed height of each item in layout units, used to compute item positions.</summary>
    float ItemHeight { get; }

    /// <summary>
    /// Where item <paramref name="index"/> sits inside the container's content box, for a
    /// container whose items are not all the same height — a menu with separators, a list
    /// with per-row heights. Null (the default) means the items stack at
    /// <see cref="ItemHeight"/>, which is what a uniform list wants and what the bounds
    /// are computed from when this returns nothing.
    /// </summary>
    (float Top, float Height)? ItemBounds(int index) => null;

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

    // ── Selection (single-select by default; a multi-select source overrides) ──

    /// <summary>Whether more than one item can be selected at once. Default: false.</summary>
    bool CanSelectMultiple => false;

    /// <summary>
    /// Indices of all selected items, ascending. The default derives from
    /// <see cref="SelectedIndex"/> (0 or 1 items), so single-select sources need
    /// not implement it.
    /// </summary>
    IReadOnlyList<int> SelectedIndices =>
        SelectedIndex >= 0 ? new[] { SelectedIndex } : [];

    /// <summary>Whether the item at <paramref name="index"/> is selected.</summary>
    bool IsItemSelected(int index) =>
        CanSelectMultiple ? SelectedIndices.Contains(index) : index == SelectedIndex;

    /// <summary>
    /// Makes <paramref name="index"/> the sole selection (UIA <c>SelectionItem.Select</c>).
    /// The default routes to <see cref="ActivateItem"/> — single-select lists set
    /// their selection through activation; multi-select sources override to replace
    /// the selection set.
    /// </summary>
    void SelectOnly(int index) => ActivateItem(index);

    /// <summary>
    /// Adds or removes <paramref name="index"/> from the selection (UIA
    /// <c>SelectionItem.AddToSelection</c>/<c>RemoveFromSelection</c>). No-op by
    /// default; only meaningful for multi-select sources.
    /// </summary>
    void SetItemSelected(int index, bool selected) { }

    /// <summary>
    /// Finds the first item after <paramref name="startAfter"/> (exclusive; pass
    /// -1 to search from the start) matching <paramref name="query"/>, or -1 if
    /// none. The default scans linearly; a source backed by an index can override
    /// to answer in better than O(n) — the point of UIA's ItemContainer pattern.
    /// </summary>
    int FindItem(int startAfter, AccessibleItemQuery query)
    {
        for (int i = System.Math.Max(startAfter + 1, 0); i < ItemCount; i++)
        {
            bool match = query.Property switch
            {
                AccessibleItemProperty.Any => true,
                AccessibleItemProperty.Name => query.Name is not null && GetItemLabel(i) == query.Name,
                AccessibleItemProperty.Selected => IsItemSelected(i),
                _ => false,
            };
            if (match) return i;
        }
        return -1;
    }
}

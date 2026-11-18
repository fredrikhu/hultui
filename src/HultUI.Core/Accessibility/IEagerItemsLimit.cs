namespace HultUI.Core.Accessibility;

/// <summary>
/// A bridge that has a list's items served on demand (<see cref="AccessibilityNode.IsVirtualItemContainer"/>)
/// past another number of them than <see cref="AccessibilityTreeBuilder.MaxEagerVirtualItems"/>, which every
/// other bridge has. The window reads it when the bridge is put in, and builds, refreshes in place and
/// patches its trees by it (<see cref="AccessibilityTreeBuilder.MaxEagerVirtualItemsOf"/>).
/// <para>The UIA bridge serves a list over 200 items on demand: a UI Automation client asks for an item
/// when it reads it, so an item nobody asks for costs nothing, where an item node is built, diffed and
/// told on every change. A list of 1,000 items whose contents change in place (Junction's History when a
/// commit lands or a ref moves, Vesper's move between two folders of 1,000 files) told a Name change on
/// each item, and UI Automation made 13,000 provider calls for them. The AT-SPI bridge keeps 2,000: an
/// AT-SPI client keeps a copy of each item it read, so a change in it has to be told, and Orca has not
/// been checked with lists served on demand.</para>
/// <para>Internal: <see cref="IAccessibilityBridge"/> is public, and this adds no API. A bridge that
/// wraps another one says the other one's number.</para>
/// </summary>
internal interface IEagerItemsLimit
{
    /// <summary>The most items a list has as item nodes in this bridge's trees; past it, they are served
    /// on demand. Zero or more, and the same for the bridge's life.</summary>
    int MaxEagerVirtualItems { get; }
}

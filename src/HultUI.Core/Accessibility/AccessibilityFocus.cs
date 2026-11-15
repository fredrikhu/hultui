using HultUI.Core.Elements;

namespace HultUI.Core.Accessibility;

/// <summary>
/// Where a reader's focus is while a node holds the keyboard focus, for the bridges to tell. A reader
/// speaks focus moves only, and a keyboard move in a list or a menu moves the selection: the element
/// focus stays on the list. So, as a WPF list box's item has the keyboard focus, the reader's focus is
/// the current item (<see cref="AccessibilityNode.CurrentItem"/>) of the container that holds the
/// focus, or of a list that takes the focus of the node that holds it
/// (<see cref="AccessibilityNode.FocusHolder"/>); with no current item, the node that holds the focus.
/// </summary>
/// <remarks>
/// The holder is the node the window says has the focus (<see cref="IAccessibilityBridge.NotifyFocusChanged(AccessibilityNode?, AccessibilityNode?)"/>):
/// the focused element's, or an open menu's while the keyboard is in it. A bridge reads the target
/// when the holder changes, and once a frame after the frame's changes are told
/// (<see cref="IAccessibilityBridge.NotifyChangesApplied"/>): a few reads of the holder's node, no walk.
/// </remarks>
internal static class AccessibilityFocus
{
    /// <summary>
    /// The id of the node a reader is told has the focus while <paramref name="holder"/> holds it:
    /// the current item of <paramref name="holder"/>, when it tracks one and has one, else of
    /// <paramref name="follower"/> (a list that takes the holder's focus, as its
    /// <see cref="AccessibilityNode.FocusHolder"/> names it), else the holder's own. An item's id is its
    /// node's, or, for an item a list serves on demand, the id its provider has
    /// (<see cref="VirtualItemId.For"/>).
    /// </summary>
    internal static ElementId TargetOf(AccessibilityNode holder, AccessibilityNode? follower)
    {
        if (holder.TracksCurrentItem && CurrentItemId(holder) is { } own) return own;
        if (follower is not null && follower.FocusHolder == holder.ElementId && CurrentItemId(follower) is { } item)
            return item;
        return holder.ElementId;
    }

    /// <summary>The id of <paramref name="container"/>'s current item, or null when it has none (or
    /// the index is past its items, which a change of them told otherwise).</summary>
    internal static ElementId? CurrentItemId(AccessibilityNode container)
    {
        int current = container.CurrentItem;
        if (current < 0) return null;
        if (container.IsVirtualItemContainer)
            return current < container.Served.Count ? VirtualItemId.For(container.ElementId, current) : null;
        var items = container.Children;
        return current < items.Count ? items[current].ElementId : null;
    }
}

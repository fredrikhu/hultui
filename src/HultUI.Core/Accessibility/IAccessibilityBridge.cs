namespace HultUI.Core.Accessibility;

/// <summary>
/// Platform abstraction for exposing the accessibility tree to OS assistive technologies.
/// Implementations map <see cref="AccessibilityNode"/>s to platform-specific APIs:
///   Windows → UIAutomation,
///   macOS   → NSAccessibility,
///   Linux   → AT-SPI2.
/// </summary>
public interface IAccessibilityBridge
{
    /// <summary>
    /// Cheap probe for "is any assistive tech listening right now". Polled per
    /// frame; when false the Window skips building/diffing the a11y tree.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Apply a structural delta computed by <see cref="AccessibilityTreeDiffer"/>.
    /// Called at most once per frame — only when the element tree's <c>Children</c>
    /// dirty flag was set during the frame. <paramref name="root"/> is the current
    /// full snapshot the diff produced; bridges that hit-test or hold per-node
    /// snapshots need it to stay current when only deep structure changed.
    /// </summary>
    void ApplyChanges(TreeDiff diff, AccessibilityNode root);

    /// <summary>
    /// Focus changed to a different element (or to nothing). The node, when
    /// non-null, belongs to the most recently applied tree snapshot.
    /// </summary>
    void NotifyFocusChanged(AccessibilityNode? focused);

    /// <summary>
    /// Focus moved, with no rebuild: the window has marked the move on the most recently
    /// applied tree in place — <paramref name="previous"/>'s <see cref="AccessibilityNode.IsFocused"/>
    /// is now false and <paramref name="focused"/>'s true (either null when the element has no
    /// node) — and the next diff will not report it. A bridge that holds a node's state brings
    /// both up to date, tells assistive technology as a diff would have, and then the focus
    /// change. By default, only the focus change.
    /// </summary>
    void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => NotifyFocusChanged(focused);

    /// <summary>
    /// Properties changed with no rebuild: the window has marked them in place on the most
    /// recently applied tree, <paramref name="root"/>, and the next diff will not report them.
    /// Today a move of a list's selection: <see cref="AccessibilityNode.IsSelected"/> on the item
    /// that had the selection and the one that has it; and a move of a caret or a selection in text
    /// made on request (the rich text editor's): <see cref="AccessibilityProperties.TextSelection"/>
    /// on its node, which carries the text's new version, the change's previous node a stand-in for
    /// it with the old one (no tree's). <paramref name="patch"/> holds property
    /// changes only, of nodes of that tree, in the order a diff would list them. A bridge that
    /// holds a node's state brings those nodes up to date and tells assistive technology as a diff
    /// would have. By default, applies the patch as a diff of that same tree.
    /// </summary>
    void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => ApplyChanges(patch, root);

    /// <summary>
    /// Has assistive technology speak <paramref name="text"/> (<see cref="Elements.Element.Announce"/>):
    /// a UIA notification on Windows, an AT-SPI announcement on Linux. <paramref name="source"/>
    /// is the announcing element's node in the most recently applied tree, or null when it has
    /// none (the bridge then announces from the window's root). Called on the UI thread, only
    /// while <see cref="IsActive"/>. Does nothing by default.
    /// </summary>
    void Announce(AccessibilityNode? source, string text, AnnouncementPoliteness politeness) { }
}

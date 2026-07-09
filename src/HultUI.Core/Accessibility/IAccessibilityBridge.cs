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
}

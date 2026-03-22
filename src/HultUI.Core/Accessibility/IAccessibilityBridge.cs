namespace HultUI.Core.Accessibility;

/// <summary>
/// Platform abstraction for exposing the accessibility tree to OS assistive technologies.
/// Implementations would map AccessibilityNodes to platform-specific APIs:
/// - Windows: UIAutomation
/// - macOS: NSAccessibility
/// - Linux: AT-SPI2
/// </summary>
public interface IAccessibilityBridge
{
    /// <summary>
    /// Called after layout when the accessibility tree has been rebuilt.
    /// The bridge should diff against its previous state and notify the platform
    /// of any changes.
    /// </summary>
    void Update(AccessibilityNode root);

    /// <summary>
    /// Called when focus changes to a new element.
    /// </summary>
    void NotifyFocusChanged(AccessibilityNode? focused);

    /// <summary>
    /// Called when a property of a node changes (e.g., text content, checked state).
    /// </summary>
    void NotifyPropertyChanged(AccessibilityNode node, string propertyName);
}

namespace HultUI.Core.Accessibility;

/// <summary>
/// No-op accessibility bridge used when no platform bridge is configured.
/// </summary>
public sealed class NullAccessibilityBridge : IAccessibilityBridge
{
    public static readonly NullAccessibilityBridge Instance = new();

    public void Update(AccessibilityNode root) { }
    public void NotifyFocusChanged(AccessibilityNode? focused) { }
    public void NotifyPropertyChanged(AccessibilityNode node, string propertyName) { }
}

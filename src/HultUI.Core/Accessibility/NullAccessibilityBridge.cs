namespace HultUI.Core.Accessibility;

/// <summary>
/// No-op accessibility bridge used when no platform bridge is configured or
/// when no assistive technology is listening.
/// </summary>
public sealed class NullAccessibilityBridge : IAccessibilityBridge
{
    public static readonly NullAccessibilityBridge Instance = new();

    public bool IsActive => false;
    public void ApplyChanges(TreeDiff diff, AccessibilityNode root) { }
    public void NotifyFocusChanged(AccessibilityNode? focused) { }
    public void NotifyPropertyChanged(AccessibilityNode node, string propertyName) { }
}

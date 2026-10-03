namespace HultUI.Core.Accessibility;

/// <summary>
/// Property-name constants used when notifying the accessibility bridge of a
/// per-node property change. Bridges map these onto platform-specific property
/// identifiers (UIA property IDs on Windows, AT-SPI property-name strings on Linux).
/// </summary>
public static class AccessibilityProperties
{
    public const string Name = "Name";
    public const string Description = "Description";
    public const string Value = "Value";
    public const string IsToggled = "IsToggled";
    public const string IsSelected = "IsSelected";
    public const string IsFocused = "IsFocused";
    public const string IsEnabled = "IsEnabled";
    public const string BoundingRect = "BoundingRect";

    /// <summary>
    /// <see cref="AccessibilityNode.NativeContent"/> changed: a foreign tree arrived under the
    /// node, left it, or was replaced. A structure change more than a property one — the
    /// node's children are different — and a bridge that joins foreign trees treats it so.
    /// </summary>
    public const string NativeContent = "NativeContent";

    /// <summary>An <see cref="IAccessibleContentSource"/>'s content changed (seam W11).</summary>
    public const string Content = "Content";
}

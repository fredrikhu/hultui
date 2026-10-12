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

    /// <summary><see cref="AccessibilityNode.Role"/> changed: a group became a dialog. UIA's
    /// ControlType, AT-SPI's role.</summary>
    public const string Role = "Role";

    /// <summary><see cref="AccessibilityNode.AutomationId"/> changed. Raises no event on either
    /// platform: the bridge takes the new id, which UI Automation reads on request.</summary>
    public const string AutomationId = "AutomationId";
    public const string Value = "Value";
    public const string IsToggled = "IsToggled";
    public const string IsSelected = "IsSelected";
    public const string IsFocused = "IsFocused";
    public const string IsEnabled = "IsEnabled";
    /// <summary><see cref="AccessibilityNode.Expanded"/> changed: an expander, a tree item, a
    /// dropdown or a menu-bar title opened or closed.</summary>
    public const string IsExpanded = "IsExpanded";
    /// <summary><see cref="AccessibilityNode.IsPassword"/> changed: a field was masked or
    /// revealed. On AT-SPI that is a role change.</summary>
    public const string IsPassword = "IsPassword";
    /// <summary><see cref="AccessibilityNode.Range"/> changed: a slider, a progress bar or a number
    /// field moved, or its bounds or steps did. The change carries the previous node, so a bridge
    /// can tell which. An element raises it for its value's change, which the window refreshes on
    /// a schedule (at most every 250 ms) rather than in every frame of a drag or a playback.</summary>
    public const string RangeValue = "RangeValue";

    /// <summary><see cref="AccessibilityNode.BoundingRect"/> changed within the node's scroll
    /// views: it moved in its content, or resized. Not reported for a node that only scrolled.</summary>
    public const string BoundingRect = "BoundingRect";

    /// <summary><see cref="AccessibilityNode.ScrollOffset"/> changed: the node, a scroll view,
    /// scrolled. The change carries the previous node, so a bridge can tell which way.</summary>
    public const string ScrollPosition = "ScrollPosition";

    /// <summary>
    /// <see cref="AccessibilityNode.NativeContent"/> changed: a foreign tree arrived under the
    /// node, left it, or was replaced. A structure change more than a property one — the
    /// node's children are different — and a bridge that joins foreign trees treats it so.
    /// </summary>
    public const string NativeContent = "NativeContent";

    /// <summary>An <see cref="IAccessibleContentSource"/>'s content changed (seam W11).</summary>
    public const string Content = "Content";

    /// <summary><see cref="AccessibilityNode.Text"/>'s text changed. The change carries the
    /// previous node, so a bridge can say what was removed and inserted.</summary>
    public const string Text = "Text";

    /// <summary><see cref="AccessibilityNode.Text"/>'s caret or selection moved.</summary>
    public const string TextSelection = "TextSelection";
}

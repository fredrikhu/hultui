using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Core.Accessibility;

public sealed class AccessibilityNode
{
    public ElementId ElementId { get; }
    public AccessibilityRole Role { get; }
    public string? Name { get; }
    public string? Description { get; }
    public Rect BoundingRect { get; }
    public bool IsFocusable { get; }
    public bool IsFocused { get; }
    public bool IsEnabled { get; }

    /// <summary>
    /// Toggle state for checkboxes and switch-like elements. <c>null</c> for
    /// elements that don't have a toggle concept (mapped to UIA
    /// <c>ToggleState.Indeterminate</c> or "pattern not supported" depending on
    /// role).
    /// </summary>
    public bool? IsToggled { get; }

    /// <summary>
    /// String value for elements that expose UIA's ValueProvider — text inputs
    /// and (later) sliders. <c>null</c> for elements without a value concept.
    /// </summary>
    public string? Value { get; }

    /// <summary>Stable automation identifier (UIA AutomationId), for test selectors.</summary>
    public string? AutomationId { get; }

    /// <summary>Expanded state for an expandable node, or null if not expandable.</summary>
    public bool? Expanded { get; }

    /// <summary>True when the node is scrolled/clipped out of the visible viewport.</summary>
    public bool IsOffscreen { get; internal set; }

    public AccessibilityNode? Parent { get; private set; }
    public IReadOnlyList<AccessibilityNode> Children => _children;

    /// <summary>
    /// True when this node is a virtualized list whose items are provided on
    /// demand rather than as child nodes (used for very large lists). The bridge
    /// synthesizes item providers by index instead of walking <see cref="Children"/>.
    /// </summary>
    public bool IsVirtualItemContainer { get; internal set; }

    private readonly List<AccessibilityNode> _children = [];

    internal AccessibilityNode(
        ElementId elementId,
        AccessibilityRole role,
        string? name,
        string? description,
        Rect boundingRect,
        bool isFocusable = false,
        bool isFocused = false,
        bool isEnabled = true,
        bool? isToggled = null,
        string? value = null,
        string? automationId = null,
        bool? expanded = null)
    {
        ElementId = elementId;
        Role = role;
        Name = name;
        Description = description;
        BoundingRect = boundingRect;
        IsFocusable = isFocusable;
        IsFocused = isFocused;
        IsEnabled = isEnabled;
        IsToggled = isToggled;
        Value = value;
        AutomationId = automationId;
        Expanded = expanded;
    }

    internal void AddChild(AccessibilityNode child)
    {
        child.Parent = this;
        _children.Add(child);
    }
}

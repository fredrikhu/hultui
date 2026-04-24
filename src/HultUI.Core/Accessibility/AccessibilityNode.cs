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

    public AccessibilityNode? Parent { get; private set; }
    public IReadOnlyList<AccessibilityNode> Children => _children;

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
        string? value = null)
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
    }

    internal void AddChild(AccessibilityNode child)
    {
        child.Parent = this;
        _children.Add(child);
    }
}

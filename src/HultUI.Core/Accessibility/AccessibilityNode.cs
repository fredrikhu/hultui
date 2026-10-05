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

    /// <summary>A password field: its <see cref="Value"/> is the bullets on screen, never the
    /// text, and a reader says what it is (UIA's IsPassword, AT-SPI's password-text role).</summary>
    public bool IsPassword { get; }

    /// <summary>Stable automation identifier (UIA AutomationId), for test selectors.</summary>
    public string? AutomationId { get; }

    /// <summary>Expanded state for an expandable node, or null if not expandable.</summary>
    public bool? Expanded { get; }

    /// <summary>Selected state for one of a set — a radio button, a tab — or null for a node
    /// that is not selectable that way. List items answer through their list instead.</summary>
    public bool? IsSelected { get; }

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

    /// <summary>
    /// Content with an accessibility tree of its own, in windows of its own — a web page's.
    /// The bridge makes that tree this node's children and sends hit tests inside the node
    /// there. Empty for everything else.
    /// </summary>
    public NativeAccessibleContent NativeContent { get; internal set; }

    /// <summary>
    /// The text a reader can move through (UIA's text pattern, AT-SPI's Text), as it was when
    /// this tree was built; null for an element that offers none. The differ compares it to
    /// tell bridges the text, caret or selection changed.
    /// </summary>
    public AccessibleText? Text { get; internal set; }

    /// <summary>Where a bridge reads the text as it is now, from its own thread, between
    /// rebuilds; null with <see cref="Text"/>.</summary>
    public IAccessibleTextSource? TextSource { get; internal set; }

    /// <summary>The text now: the source's, or the snapshot this tree was built with.</summary>
    public AccessibleText? CurrentText => TextSource?.AccessibleText ?? Text;

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
        bool? expanded = null,
        bool isPassword = false,
        bool? isSelected = null)
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
        IsPassword = isPassword;
        IsSelected = isSelected;
    }

    internal void AddChild(AccessibilityNode child)
    {
        child.Parent = this;
        _children.Add(child);
    }
}

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
    public IReadOnlyList<AccessibilityNode> Children => _children;

    private readonly List<AccessibilityNode> _children = [];

    internal AccessibilityNode(
        ElementId elementId,
        AccessibilityRole role,
        string? name,
        string? description,
        Rect boundingRect)
    {
        ElementId = elementId;
        Role = role;
        Name = name;
        Description = description;
        BoundingRect = boundingRect;
    }

    internal void AddChild(AccessibilityNode child) => _children.Add(child);
}

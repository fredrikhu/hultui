using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Headless;

/// <summary>
/// A query result: an accessibility-tree node paired with the live element it
/// snapshots. Read semantic state from the node (what assistive tech sees);
/// interact through the driver methods, which inject real input at the live
/// element's current position.
/// </summary>
public sealed class HeadlessElement
{
    private readonly HeadlessWindow _window;

    internal HeadlessElement(HeadlessWindow window, AccessibilityNode node, Element element)
    {
        _window = window;
        Node = node;
        Element = element;
    }

    /// <summary>The accessibility snapshot at query time. Re-query after interactions for fresh state.</summary>
    public AccessibilityNode Node { get; }

    /// <summary>The live element in the tree.</summary>
    public Element Element { get; }

    public AccessibilityRole Role => Node.Role;
    public string? Name => Node.Name;
    public string? Value => Node.Value;
    public string? AutomationId => Node.AutomationId;
    public bool? IsToggled => Node.IsToggled;
    public bool IsEnabled => Node.IsEnabled;

    /// <summary>Bounds at query time, in layout units.</summary>
    public Rect Bounds => Node.BoundingRect;

    /// <summary>True when the live element currently has keyboard focus.</summary>
    public bool IsFocused => ReferenceEquals(_window.Window.FocusedElement, Element);

    /// <summary>Clicks the live element's current center.</summary>
    public void Click() => _window.Click(Element);

    public void DoubleClick() => _window.DoubleClick(Element);

    public void RightClick() => _window.RightClick(Element);

    /// <summary>Moves keyboard focus here, through the same path click/Tab focus takes.</summary>
    public void Focus() => _window.Window.SetFocus(Element);
}

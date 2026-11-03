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
    private readonly AccessibilityNode? _ownerNode;   // set for a node with no element of its own

    internal HeadlessElement(HeadlessWindow window, AccessibilityNode node, Element element)
    {
        _window = window;
        Node = node;
        Element = element;
    }

    /// <summary>A node with no element of its own (a virtual list's item, an element's described
    /// content), paired with the element that owns it and that element's node.</summary>
    internal HeadlessElement(HeadlessWindow window, AccessibilityNode node, Element owner, AccessibilityNode ownerNode)
        : this(window, node, owner) => _ownerNode = ownerNode;

    /// <summary>The accessibility snapshot at query time. Re-query after interactions for fresh state.</summary>
    public AccessibilityNode Node { get; }

    /// <summary>The live element in the tree. For a node with no element of its own (a virtual
    /// list's item, a locator can find those), the element that owns it: the list.</summary>
    public Element Element { get; }

    /// <summary>False for a node with no element of its own, whose <see cref="Element"/> is its owner.</summary>
    internal bool HasOwnElement => _ownerNode is null;

    /// <summary>Where the node is in the window now: its element's <see cref="Element.WindowBorderBox"/>,
    /// or for an owned node its place relative to its owner's.</summary>
    internal Rect WindowBox
    {
        get
        {
            var box = Element.WindowBorderBox;
            return _ownerNode is null
                ? box
                : Node.BoundingRect.Translate(box.X - _ownerNode.BoundingRect.X, box.Y - _ownerNode.BoundingRect.Y);
        }
    }

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
    public void Click()
    {
        if (_ownerNode is null) _window.Click(Element);
        else _window.Click(Center.X, Center.Y);
    }

    public void DoubleClick()
    {
        if (_ownerNode is null) _window.DoubleClick(Element);
        else _window.DoubleClick(Center.X, Center.Y);
    }

    public void RightClick()
    {
        if (_ownerNode is null) _window.RightClick(Element);
        else _window.RightClick(Center.X, Center.Y);
    }

    private Point Center => WindowBox is var b ? new(b.X + b.Width / 2f, b.Y + b.Height / 2f) : default;

    /// <summary>Moves keyboard focus here, through the same path click/Tab focus takes.</summary>
    public void Focus() => _window.Window.SetFocus(Element);
}

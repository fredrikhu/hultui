namespace HultUI.Core.Elements;

/// <summary>
/// An element's children, and the order they are painted and hit-tested in once that has been
/// worked out (<see cref="Element.ChildrenInZOrder"/>). The order is kept on the list rather
/// than on the element: about half of a tree is leaves, which have no list.
/// </summary>
internal sealed class ChildList : List<Element>
{
    public ChildList() { }

    public ChildList(int capacity) : base(capacity) { }

    /// <summary>The children sorted into z-order; empty when that is the order the list is in;
    /// null when it has not been worked out since the list, or a child's ZIndex or position
    /// type, last changed.</summary>
    public Element[]? ZOrder;
}

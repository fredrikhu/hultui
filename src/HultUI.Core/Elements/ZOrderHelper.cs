using HultUI.Core.Layout;

namespace HultUI.Core.Elements;

/// <summary>
/// Provides z-index sorted child iteration for rendering and hit testing.
/// Returns children sorted by ZIndex (ascending) for rendering (low = back, high = front),
/// a sticky child after the others at its ZIndex — what scrolls under it must not paint over
/// it. The order is kept on the parent's child list (<see cref="Element.ChildrenInZOrder"/>), so
/// a steady frame neither scans nor sorts; it is worked out again, and only then allocates,
/// after the child list or a child's ZIndex or position type changed.
/// </summary>
internal static class ZOrderHelper
{
    /// <summary>
    /// Returns children in z-order (ascending) for rendering.
    /// When no child has a non-zero ZIndex or is sticky, returns the original list.
    /// </summary>
    public static IReadOnlyList<Element> GetChildrenInZOrder(Element parent) => parent.ChildrenInZOrder;

    /// <summary>
    /// The paint order of two or more <paramref name="children"/>: empty when it is the order
    /// they are in (no child has a ZIndex or is sticky), else the children sorted by ZIndex,
    /// then sticky after the rest, then by their place in the list. The place is part of the
    /// key, so the order is stable: List.Sort only looked stable while it insertion-sorted
    /// sixteen or fewer.
    /// </summary>
    internal static Element[] Order(List<Element> children)
    {
        int count = children.Count;
        bool any = false;
        for (int i = 0; i < count; i++)
        {
            var position = children[i].PositionRead;
            if (position.ZIndex != 0 || position.Type == PositionType.Sticky)
            {
                any = true;
                break;
            }
        }
        if (!any)
            return [];

        // One key per child, sorted with the children alongside, so no comparison delegate: the
        // ZIndex in the high half, then the sticky bit above the place in the low half.
        Span<long> keys = count <= StackKeys ? stackalloc long[StackKeys] : new long[count];
        keys = keys[..count];
        var sorted = new Element[count];
        for (int i = 0; i < count; i++)
        {
            var child = children[i];
            var position = child.PositionRead;
            keys[i] = ((long)position.ZIndex << 32)
                | (position.Type == PositionType.Sticky ? 1L << 31 : 0L)
                | (uint)i;
            sorted[i] = child;
        }
        keys.Sort(sorted.AsSpan());
        return sorted;
    }

    private const int StackKeys = 128;
}

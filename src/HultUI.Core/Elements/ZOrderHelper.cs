namespace HultUI.Core.Elements;

/// <summary>
/// Provides z-index sorted child iteration for rendering and hit testing.
/// Returns children sorted by ZIndex (ascending) for rendering (low = back, high = front),
/// a sticky child after the others at its ZIndex — what scrolls under it must not paint over
/// it. Only allocates when a child has a non-zero ZIndex or is sticky.
/// </summary>
internal static class ZOrderHelper
{
    /// <summary>
    /// Returns children in z-order (ascending) for rendering.
    /// When no child has a non-zero ZIndex, returns the original list to avoid allocation.
    /// </summary>
    public static IReadOnlyList<Element> GetChildrenInZOrder(Element parent)
    {
        var children = parent.Children;
        if (children.Count <= 1)
            return children;

        bool hasZIndex = false;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].PositionRead.ZIndex != 0 || children[i].PositionRead.Type == Layout.PositionType.Sticky)
            {
                hasZIndex = true;
                break;
            }
        }

        if (!hasZIndex)
            return children;

        // By ZIndex, then sticky last, then insertion order. The index is part of the key because
        // List.Sort is not stable: it only looked stable while insertion sort handled sixteen or fewer.
        var order = new int[children.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            var pa = children[a].PositionRead;
            var pb = children[b].PositionRead;
            int c = pa.ZIndex.CompareTo(pb.ZIndex);
            if (c != 0) return c;
            c = (pa.Type == Layout.PositionType.Sticky).CompareTo(pb.Type == Layout.PositionType.Sticky);
            return c != 0 ? c : a.CompareTo(b);
        });
        var sorted = new Element[order.Length];
        for (int i = 0; i < order.Length; i++) sorted[i] = children[order[i]];
        return sorted;
    }
}

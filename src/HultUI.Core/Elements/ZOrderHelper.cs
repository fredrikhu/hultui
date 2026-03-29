namespace HultUI.Core.Elements;

/// <summary>
/// Provides z-index sorted child iteration for rendering and hit testing.
/// Returns children sorted by ZIndex (ascending) for rendering (low = back, high = front).
/// Only allocates when at least one child has a non-zero ZIndex.
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
            if (children[i].Position.ZIndex != 0)
            {
                hasZIndex = true;
                break;
            }
        }

        if (!hasZIndex)
            return children;

        // Sort by ZIndex ascending (stable: preserve insertion order for equal ZIndex)
        var sorted = new List<Element>(children);
        sorted.Sort((a, b) => a.Position.ZIndex.CompareTo(b.Position.ZIndex));
        return sorted;
    }
}

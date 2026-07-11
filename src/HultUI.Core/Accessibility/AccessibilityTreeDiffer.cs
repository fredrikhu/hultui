using HultUI.Core.Elements;

namespace HultUI.Core.Accessibility;

/// <summary>
/// Computes a <see cref="TreeDiff"/> between two accessibility-tree snapshots
/// keyed by <see cref="AccessibilityNode.ElementId"/>. Identical IDs across
/// snapshots denote the same logical node so bridges can reuse their wrappers
/// (critical for UIA, which compares provider identity).
/// </summary>
public static class AccessibilityTreeDiffer
{
    public static TreeDiff Diff(AccessibilityNode? previous, AccessibilityNode? current)
    {
        var diff = new TreeDiff();

        if (previous is null && current is null)
            return diff;

        if (previous is null)
        {
            AddRecursive(diff, current!, parent: null);
            return diff;
        }

        if (current is null)
        {
            RemoveRecursive(diff, previous, parent: null);
            return diff;
        }

        if (previous.ElementId != current.ElementId)
        {
            // Root swapped outright — treat as full replacement.
            RemoveRecursive(diff, previous, parent: null);
            AddRecursive(diff, current, parent: null);
            return diff;
        }

        DiffSubtree(diff, previous, current);
        return diff;
    }

    private static void DiffSubtree(TreeDiff diff, AccessibilityNode prev, AccessibilityNode curr)
    {
        CompareProperties(diff, prev, curr);

        if (prev.Children.Count == 0 && curr.Children.Count == 0)
            return;

        // Index previous children by ElementId so we can match them in O(1).
        Dictionary<ElementId, (AccessibilityNode Node, int Index)>? prevById = null;
        if (prev.Children.Count > 0)
        {
            prevById = new Dictionary<ElementId, (AccessibilityNode, int)>(prev.Children.Count);
            for (int i = 0; i < prev.Children.Count; i++)
                prevById[prev.Children[i].ElementId] = (prev.Children[i], i);
        }

        bool orderChanged = false;
        int lastMatchedPrevIndex = -1;

        for (int i = 0; i < curr.Children.Count; i++)
        {
            var currChild = curr.Children[i];
            if (prevById is not null && prevById.TryGetValue(currChild.ElementId, out var entry))
            {
                DiffSubtree(diff, entry.Node, currChild);
                if (entry.Index < lastMatchedPrevIndex)
                    orderChanged = true;
                lastMatchedPrevIndex = entry.Index;
                prevById.Remove(currChild.ElementId);
            }
            else
            {
                AddRecursive(diff, currChild, curr);
            }
        }

        if (prevById is not null)
        {
            foreach (var (_, entry) in prevById)
                RemoveRecursive(diff, entry.Node, prev);
        }

        if (orderChanged)
            diff.Reordered.Add(curr);
    }

    private static void CompareProperties(TreeDiff diff, AccessibilityNode prev, AccessibilityNode curr)
    {
        if (prev.Name != curr.Name)
            diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.Name));
        if (prev.Description != curr.Description)
            diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.Description));
        if (prev.Value != curr.Value)
            diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.Value));
        if (prev.IsToggled != curr.IsToggled)
            diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsToggled));
        if (prev.IsEnabled != curr.IsEnabled)
            diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsEnabled));
        if (prev.IsFocused != curr.IsFocused)
            diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsFocused));
        // Skip bounding-rect changes for nodes that are off-screen before and
        // after: assistive tech doesn't use an off-screen node's exact bounds, and
        // emitting them floods the AT with events. A virtualized list materializes
        // one node per item (most off-screen); resizing shifts every item's X, so
        // without this a single resize frame raises hundreds of change events —
        // saturating the client and dropping the app to single-digit FPS. When an
        // item scrolls into view its IsOffscreen flips, so this condition lets the
        // transition through and its bounds refresh then.
        if (prev.BoundingRect != curr.BoundingRect && !(prev.IsOffscreen && curr.IsOffscreen))
            diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.BoundingRect));
    }

    private static void AddRecursive(TreeDiff diff, AccessibilityNode node, AccessibilityNode? parent)
    {
        diff.Added.Add(new AddedNode(node, parent));
        foreach (var child in node.Children)
            AddRecursive(diff, child, node);
    }

    private static void RemoveRecursive(TreeDiff diff, AccessibilityNode node, AccessibilityNode? parent)
    {
        // Deepest-first so the bridge can tear down leaves before containers.
        foreach (var child in node.Children)
            RemoveRecursive(diff, child, node);
        diff.Removed.Add(new RemovedNode(node, parent));
    }
}

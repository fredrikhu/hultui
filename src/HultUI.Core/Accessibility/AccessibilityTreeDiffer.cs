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

        var prevChildren = prev.Children;
        var currChildren = curr.Children;
        int prevCount = prevChildren.Count, currCount = currChildren.Count;

        // Most rebuilds leave a node's children as they were. Pair them by position while
        // their ids agree, which needs no index: the tree is rebuilt in full on every change a
        // reader can see, and a dictionary per parent was most of what diffing it allocated.
        int first = 0;
        int common = Math.Min(prevCount, currCount);
        while (first < common && prevChildren[first].ElementId == currChildren[first].ElementId)
        {
            DiffSubtree(diff, prevChildren[first], currChildren[first]);
            first++;
        }

        if (first == prevCount)
        {
            // Nothing of the old left: the rest is new.
            for (int i = first; i < currCount; i++)
                AddRecursive(diff, currChildren[i], curr);
            return;
        }
        if (first == currCount)
        {
            // Nothing new left: the rest of the old is gone.
            for (int i = first; i < prevCount; i++)
                RemoveRecursive(diff, prevChildren[i], prev);
            return;
        }

        // From the first difference on, match by ElementId in O(1). Ids are unique within a
        // tree, so this pairs exactly what one index over all the children did.
        var prevById = new Dictionary<ElementId, (AccessibilityNode Node, int Index)>(prevCount - first);
        for (int i = first; i < prevCount; i++)
            prevById[prevChildren[i].ElementId] = (prevChildren[i], i);

        bool orderChanged = false;
        int lastMatchedPrevIndex = first - 1;

        for (int i = first; i < currCount; i++)
        {
            var currChild = currChildren[i];
            if (prevById.Remove(currChild.ElementId, out var entry))
            {
                DiffSubtree(diff, entry.Node, currChild);
                if (entry.Index < lastMatchedPrevIndex)
                    orderChanged = true;
                lastMatchedPrevIndex = entry.Index;
            }
            else
            {
                AddRecursive(diff, currChild, curr);
            }
        }

        // In their old order: a dictionary that only had entries taken out lists the rest in
        // the order they went in.
        foreach (var (_, entry) in prevById)
            RemoveRecursive(diff, entry.Node, prev);

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
        if (prev.IsPassword != curr.IsPassword)
            diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsPassword));
        if (prev.IsToggled != curr.IsToggled)
            diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsToggled));
        if (prev.IsSelected != curr.IsSelected)
            diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsSelected));
        if (prev.IsEnabled != curr.IsEnabled)
            diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsEnabled));
        if (prev.Expanded != curr.Expanded)
            diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsExpanded));
        if (prev.IsFocused != curr.IsFocused)
            diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsFocused));
        if (prev.NativeContent != curr.NativeContent)
            diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.NativeContent));
        if (!ReferenceEquals(prev.Text, curr.Text) && (prev.Text is not null || curr.Text is not null))
        {
            var (a, b) = (prev.Text, curr.Text);
            if (a?.Text != b?.Text)
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.Text, prev));
            if (a?.Caret != b?.Caret || a?.SelectionStart != b?.SelectionStart || a?.SelectionEnd != b?.SelectionEnd)
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.TextSelection, prev));
        }
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
        var children = node.Children;   // by index: foreach over the interface boxes an enumerator
        for (int i = 0; i < children.Count; i++)
            AddRecursive(diff, children[i], node);
    }

    private static void RemoveRecursive(TreeDiff diff, AccessibilityNode node, AccessibilityNode? parent)
    {
        // Deepest-first so the bridge can tear down leaves before containers.
        var children = node.Children;
        for (int i = 0; i < children.Count; i++)
            RemoveRecursive(diff, children[i], node);
        diff.Removed.Add(new RemovedNode(node, parent));
    }
}

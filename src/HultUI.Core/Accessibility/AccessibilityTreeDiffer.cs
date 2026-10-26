using HultUI.Core.Elements;
using HultUI.Core.Primitives;

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

        DiffSubtree(diff, previous, current, default, default);
        return diff;
    }

    /// <param name="prevScroll">How far the scroll containers above <paramref name="prev"/> were
    /// scrolled, summed: what its bounds had subtracted.</param>
    /// <param name="currScroll">The same above <paramref name="curr"/>.</param>
    private static void DiffSubtree(TreeDiff diff, AccessibilityNode prev, AccessibilityNode curr,
        Point prevScroll, Point currScroll)
    {
        CompareProperties(diff.PropertyChanged, prev, curr, prevScroll, currScroll);
        // The children sit in this node's scrolled content.
        prevScroll += prev.ScrollOffset;
        currScroll += curr.ScrollOffset;

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
            DiffSubtree(diff, prevChildren[first], currChildren[first], prevScroll, currScroll);
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
                DiffSubtree(diff, entry.Node, currChild, prevScroll, currScroll);
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

    /// <summary>
    /// The property changes a diff reports between a node and the same node made again in the same
    /// place (a leaf's, <see cref="AccessibilityTreeBuilder.RemakeLeaf"/>), added to
    /// <paramref name="changes"/> in the order a diff lists them.
    /// </summary>
    internal static void CompareNodes(List<PropertyChange> changes, AccessibilityNode prev, AccessibilityNode curr) =>
        CompareProperties(changes, prev, curr, default, default);

    /// <summary>
    /// The property changes a diff reports between a node and the same node made again in the same
    /// place of a tree (a layout refresh's, <see cref="AccessibilityTreeBuilder.TryRefreshInPlace"/>),
    /// added to <paramref name="changes"/> in the order a diff lists them.
    /// </summary>
    /// <param name="prevScroll">How far the scroll views above <paramref name="prev"/> are scrolled,
    /// summed, as a diff sums them on its way down.</param>
    /// <param name="currScroll">The same above <paramref name="curr"/>.</param>
    internal static void CompareNodes(List<PropertyChange> changes, AccessibilityNode prev, AccessibilityNode curr,
        Point prevScroll, Point currScroll) =>
        CompareProperties(changes, prev, curr, prevScroll, currScroll);

    private static void CompareProperties(List<PropertyChange> changes, AccessibilityNode prev, AccessibilityNode curr,
        Point prevScroll, Point currScroll)
    {
        // Most nodes carry none of the rarely set fields (a description, a value, native
        // content, text, a scroll offset); then there is nothing of them to compare.
        bool rare = prev.EitherHasRareFields(curr);
        // A group became a dialog: first, so a reader re-reads the rest as the new kind of thing.
        // It carries the previous node. Never compared before: a caching client (Orca) kept the
        // old role, and UIA answered the new one only once something else had the tree applied.
        if (prev.Role != curr.Role)
            changes.Add(new PropertyChange(curr, AccessibilityProperties.Role, prev));
        if (prev.Name != curr.Name)
            changes.Add(new PropertyChange(curr, AccessibilityProperties.Name));
        if (rare && prev.Description != curr.Description)
            changes.Add(new PropertyChange(curr, AccessibilityProperties.Description));
        // No event for it: in the diff so the bridge takes the node, whose id UIA reads on request.
        // Left out, a tree whose only change it was went unapplied, and the old id was answered.
        if (rare && prev.AutomationId != curr.AutomationId)
            changes.Add(new PropertyChange(curr, AccessibilityProperties.AutomationId));
        // A node that offers text tells a change of it as a text change (UIA TextChanged, AT-SPI
        // text-changed), not as its value: the value of a rich text editor is the whole document,
        // and a keystroke raised it in a UIA property change (a 749,017-character BSTR) and an
        // AT-SPI accessible-value signal besides the text change. Chromium raises no text in a
        // contenteditable's ValueValue change (Blink does not send the value of a non-atomic text
        // field; ax_object.cc), and on Linux accessible-value only for a range value
        // (ax_platform_node_auralinux.cc, OnValueChanged). A reader still reads the value on
        // request. A text input with no text pattern keeps its value change, as an <input> does.
        if (rare && !(prev.OffersText && curr.OffersText) && prev.Value != curr.Value)
            changes.Add(new PropertyChange(curr, AccessibilityProperties.Value));
        // A slider, a progress bar or a number field moved, or its bounds or steps changed: the
        // bridge tells which (UIA RangeValue.Value, Minimum, Maximum; AT-SPI accessible-value).
        if (rare && prev.Range != curr.Range)
            changes.Add(new PropertyChange(curr, AccessibilityProperties.RangeValue, prev));
        // The states share one field: when it is the same, none of them changed.
        if (prev.States != curr.States)
        {
            if (prev.IsPassword != curr.IsPassword)
                changes.Add(new PropertyChange(curr, AccessibilityProperties.IsPassword));
            if (prev.IsToggled != curr.IsToggled)
                changes.Add(new PropertyChange(curr, AccessibilityProperties.IsToggled));
            if (prev.IsSelected != curr.IsSelected)
                changes.Add(new PropertyChange(curr, AccessibilityProperties.IsSelected));
            if (prev.IsEnabled != curr.IsEnabled)
                changes.Add(new PropertyChange(curr, AccessibilityProperties.IsEnabled));
            if (prev.Expanded != curr.Expanded)
                changes.Add(new PropertyChange(curr, AccessibilityProperties.IsExpanded));
            if (prev.IsFocused != curr.IsFocused)
                changes.Add(new PropertyChange(curr, AccessibilityProperties.IsFocused));
        }
        if (rare && prev.NativeContent != curr.NativeContent)
            changes.Add(new PropertyChange(curr, AccessibilityProperties.NativeContent));
        if (rare && prev.TextVersion is { } pv && curr.TextVersion is { } cv && pv.GetType() == cv.GetType())
        {
            // Text made on request: the versions say what changed without making either. A long
            // document's text was made on every rebuild, and compared here character by character.
            if (!cv.SameText(pv))
                changes.Add(new PropertyChange(curr, AccessibilityProperties.Text, prev));
            if (!cv.SameSelection(pv))
                changes.Add(new PropertyChange(curr, AccessibilityProperties.TextSelection, prev));
        }
        else if (rare && (prev.OffersText || curr.OffersText)
            && !ReferenceEquals(prev.Text, curr.Text) && (prev.Text is not null || curr.Text is not null))
        {
            var (a, b) = (prev.Text, curr.Text);
            if (a?.Text != b?.Text)
                changes.Add(new PropertyChange(curr, AccessibilityProperties.Text, prev));
            if (a?.Caret != b?.Caret || a?.SelectionStart != b?.SelectionStart || a?.SelectionEnd != b?.SelectionEnd)
                changes.Add(new PropertyChange(curr, AccessibilityProperties.TextSelection, prev));
        }
        // Skip bounding-rect changes for nodes that are off-screen before and
        // after: assistive tech doesn't use an off-screen node's exact bounds, and
        // emitting them floods the AT with events. A virtualized list materializes
        // one node per item (most off-screen); resizing shifts every item's X, so
        // without this a single resize frame raises hundreds of change events —
        // saturating the client and dropping the app to single-digit FPS.
        //
        // A node's bounds are on screen, so a scroll moves every node under the scroll view. What
        // moved is told as browsers tell it (Chromium keeps a node's bounds relative to its scroll
        // container and applies the offset when they are read): a node whose place within its
        // scroll views changed — it moved in its content, or resized, or is sticky — has new
        // bounds; the rest only scrolled, which is the scroll view's own change, ScrollPosition,
        // and that includes a node scrolling into view or out of it. A reader that asks still gets
        // the bounds on screen and the offscreen state: the bridges read them from the current
        // tree. Each rebuild during a wheel turn over 1,000 rows raised a bounds event for each of
        // the ~130 rows on screen.
        if (!(prev.IsOffscreen && curr.IsOffscreen) && Moved(prev.UnscrolledRect, curr.UnscrolledRect, prevScroll, currScroll))
            changes.Add(new PropertyChange(curr, AccessibilityProperties.BoundingRect));
        if (rare && prev.ScrollOffset != curr.ScrollOffset)
            changes.Add(new PropertyChange(curr, AccessibilityProperties.ScrollPosition, prev));
        // A list whose items are served on demand has no item nodes: the number of its items and
        // which are selected are its own, and a change of either is told on it, with the previous
        // node, where a list of item nodes tells nodes added or removed and their IsSelected. After
        // the list's own properties, as its items' changes come after them. A list that crossed the
        // number its items are served on demand from has item nodes on one side, which tell it.
        if (rare && prev.IsVirtualItemContainer && curr.IsVirtualItemContainer)
        {
            var (was, now) = (prev.Served, curr.Served);
            if (was.Count != now.Count)
                changes.Add(new PropertyChange(curr, AccessibilityProperties.ItemCount, prev));
            if (!was.Selected.SequenceEqual(now.Selected))
                changes.Add(new PropertyChange(curr, AccessibilityProperties.ItemSelection, prev));
        }
    }

    /// <summary>
    /// Whether a node moved or resized within its scroll views: its box in their content
    /// (<see cref="AccessibilityNode.UnscrolledRect"/>, the box on screen with the scroll above it
    /// added back), compared. With the same scroll above, exactly; scrolled, to a hundredth of a
    /// unit, as when the scroll was added back to the box on screen here and each could round a
    /// float.
    /// </summary>
    internal static bool Moved(Rect prev, Rect curr, Point prevScroll, Point currScroll)
    {
        if (prevScroll == currScroll)
            return prev != curr;
        const float tolerance = 0.01f;
        return MathF.Abs(prev.X - curr.X) > tolerance
            || MathF.Abs(prev.Y - curr.Y) > tolerance
            || MathF.Abs(prev.Width - curr.Width) > tolerance
            || MathF.Abs(prev.Height - curr.Height) > tolerance;
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

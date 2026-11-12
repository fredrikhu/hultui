using HultUI.Core.Elements;

namespace HultUI.Core.Accessibility;

/// <summary>
/// Produced by <see cref="AccessibilityTreeDiffer"/>. Describes how the latest
/// accessibility tree differs from the previous snapshot so a bridge can emit
/// the right platform-specific change notifications without recomputing the
/// whole tree.
/// </summary>
public sealed class TreeDiff
{
    /// <summary>Nodes present in the new tree that were absent before.</summary>
    public List<AddedNode> Added { get; } = [];

    /// <summary>Nodes present in the previous tree that are no longer in the new one.</summary>
    public List<RemovedNode> Removed { get; } = [];

    /// <summary>
    /// Parent nodes (of the new tree) whose children that were there before are in a different
    /// order now; some may have been added or removed besides. The bridge should reshuffle sibling
    /// indices under these nodes.
    /// </summary>
    public List<AccessibilityNode> Reordered { get; } = [];

    /// <summary>Per-node property changes. The node reference is from the *new* tree.</summary>
    public List<PropertyChange> PropertyChanged { get; } = [];

    public bool IsEmpty =>
        Added.Count == 0 && Removed.Count == 0 &&
        Reordered.Count == 0 && PropertyChanged.Count == 0;

    /// <summary>Empties the four lists, keeping their room: the window's diff that it lends a
    /// bridge (<see cref="ITreeDiffBorrower"/>), after each call, so it holds no node of the tree.</summary>
    internal void Clear()
    {
        Added.Clear();
        Removed.Clear();
        Reordered.Clear();
        PropertyChanged.Clear();
    }

    /// <summary>
    /// The distinct parents that actually gained children in this diff,
    /// excluding parents that are themselves newly added. A structure-changed
    /// notification is a re-query hint on a parent, not a per-child record —
    /// so a freshly added parent needs no announcement of its own children
    /// (announcing the topmost added ancestor's pre-existing parent covers the
    /// whole subtree, and clients discover the rest by walking it). A bulk
    /// insert of N subtrees under one container thus coalesces from N×subtree
    /// events to one. Null in the result means the root itself was added.
    /// </summary>
    public List<ElementId?> CoalescedAddedParents() =>
        CoalesceParents(Added, static a => (a.Node.ElementId, a.Parent?.ElementId));

    /// <summary>
    /// The distinct parents that survive this diff and lost children —
    /// parents that were themselves removed are skipped for the same
    /// topmost-boundary reason as <see cref="CoalescedAddedParents"/>.
    /// Null in the result means the root itself was removed.
    /// </summary>
    public List<ElementId?> CoalescedRemovedParents() =>
        CoalesceParents(Removed, static r => (r.Node.ElementId, r.Parent?.ElementId));

    private static List<ElementId?> CoalesceParents<T>(
        List<T> entries, Func<T, (ElementId Node, ElementId? Parent)> ids)
    {
        var result = new List<ElementId?>();
        if (entries.Count == 0) return result;
        var changed = TakeIds(entries.Count);
        var seen = t_seen ?? [];
        t_seen = null;
        try
        {
            foreach (var entry in entries)
                changed.Add(ids(entry).Node);

            foreach (var entry in entries)
            {
                var parent = ids(entry).Parent;
                if (parent is { } pid && changed.Contains(pid))
                    continue; // interior of a changed subtree — covered by its topmost boundary
                if (seen.Add(parent))
                    result.Add(parent);
            }
            return result;
        }
        finally
        {
            GiveBack(changed);
            bool keep = seen.Count <= KeptIdsLimit;
            seen.Clear();
            if (keep) t_seen = seen;
        }
    }

    // The ids of a diff's entries and the parents seen, for the searches here: kept per thread and
    // emptied after each, so a diff that adds or removes a list's rows builds no set of them. Each key
    // of Junction's commit file walk with a reader (a file's 633 rows out, the next file's in) built
    // two sets of the rows, 30.6 KB, every key. A set that held more ids than this is let go after.
    [ThreadStatic] private static HashSet<ElementId>? t_ids;
    [ThreadStatic] private static HashSet<ElementId?>? t_seen;
    private const int KeptIdsLimit = 4096;

    /// <summary>The thread's set of ids, empty, with room for <paramref name="count"/>; taken until
    /// <see cref="GiveBack"/>, so a call made meanwhile gets a set of its own.</summary>
    private static HashSet<ElementId> TakeIds(int count)
    {
        var ids = t_ids;
        t_ids = null;
        if (ids is null) return new HashSet<ElementId>(count);
        ids.EnsureCapacity(count);
        return ids;
    }

    private static void GiveBack(HashSet<ElementId> ids)
    {
        bool keep = ids.Count <= KeptIdsLimit;
        ids.Clear();
        if (keep) t_ids = ids;
    }

    /// <summary>
    /// The added nodes that sit at the boundary of a changed subtree — i.e. whose
    /// parent is <em>not</em> itself newly added. Interior nodes (parent also added)
    /// are dropped: a client learns about them by walking their freshly-added
    /// parent, so a per-interior-node structural signal is pure redundancy.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="CoalescedAddedParents"/>, which yields distinct <em>parents</em>
    /// for a parent-level structure hint (UIA), this keeps each boundary <em>entry</em>
    /// so a protocol whose child signal names the specific child and index (AT-SPI's
    /// <c>object:children-changed</c>) can emit one accurate signal per boundary child.
    /// A bulk insert of a container plus its N children collapses to the single
    /// entry for the container.
    /// </remarks>
    public List<AddedNode> BoundaryAdded() =>
        Boundary(Added, static a => a.Node.ElementId, static a => a.Parent?.ElementId);

    /// <summary>
    /// The removed nodes at the boundary of a changed subtree — those whose parent
    /// was not itself removed. The AT-SPI counterpart to <see cref="BoundaryAdded"/>.
    /// </summary>
    public List<RemovedNode> BoundaryRemoved() =>
        Boundary(Removed, static r => r.Node.ElementId, static r => r.Parent?.ElementId);

    private static List<T> Boundary<T>(
        List<T> entries, Func<T, ElementId> id, Func<T, ElementId?> parentId)
    {
        var result = new List<T>();
        if (entries.Count == 0) return result;
        var changed = TakeIds(entries.Count);
        try
        {
            foreach (var entry in entries)
                changed.Add(id(entry));

            foreach (var entry in entries)
            {
                var parent = parentId(entry);
                if (parent is { } pid && changed.Contains(pid))
                    continue; // interior of a changed subtree — its parent's walk covers it
                result.Add(entry);
            }
            return result;
        }
        finally { GiveBack(changed); }
    }
}

/// <summary>
/// A node that was newly introduced. <see cref="Parent"/> refers to the new
/// tree's parent (null iff this is the new root).
/// </summary>
public readonly record struct AddedNode(AccessibilityNode Node, AccessibilityNode? Parent);

/// <summary>
/// A node that was removed. <see cref="Node"/> and <see cref="Parent"/> are
/// both from the *previous* tree snapshot.
/// </summary>
public readonly record struct RemovedNode(AccessibilityNode Node, AccessibilityNode? Parent);

/// <summary>
/// Announces that a single property on <see cref="Node"/> (from the new tree)
/// changed value. <see cref="PropertyName"/> is one of
/// <see cref="AccessibilityProperties"/>. <see cref="Previous"/> is the node in the previous
/// tree, for a change a bridge describes as a difference (the text's).
/// </summary>
public readonly record struct PropertyChange(AccessibilityNode Node, string PropertyName, AccessibilityNode? Previous = null);

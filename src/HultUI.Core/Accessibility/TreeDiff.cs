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
    /// Parent nodes whose children kept the same set of <see cref="AccessibilityNode.ElementId"/>s
    /// but in a different order. The bridge should reshuffle sibling indices under these nodes.
    /// </summary>
    public List<AccessibilityNode> Reordered { get; } = [];

    /// <summary>Per-node property changes. The node reference is from the *new* tree.</summary>
    public List<PropertyChange> PropertyChanged { get; } = [];

    public bool IsEmpty =>
        Added.Count == 0 && Removed.Count == 0 &&
        Reordered.Count == 0 && PropertyChanged.Count == 0;
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
/// <see cref="AccessibilityProperties"/>.
/// </summary>
public readonly record struct PropertyChange(AccessibilityNode Node, string PropertyName);

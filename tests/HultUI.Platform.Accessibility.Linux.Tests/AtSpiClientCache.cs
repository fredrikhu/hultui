using HultUI.Platform.Accessibility.Linux;

namespace HultUI.Platform.Accessibility.Linux.Tests;

/// <summary>
/// What a caching AT-SPI client (libatspi, which Orca runs on) keeps of each object's children,
/// fed the bridge's signals in the order they are sent. The rules are libatspi's, each seen in a
/// live WSL run (plan 17, round 3, step 15: a pyatspi client in <c>Atspi.event_main</c>, one list
/// per case, never cleared, against the real bridge on the a11y bus; this model reproduced all
/// eleven cases there):
/// <list type="bullet">
/// <item>A cache-add places the object at its index in its parent's cached children, in place of
/// whatever was there, growing the list when the index is past its end; it gives the object a
/// cached list as long as its child count says, empty slots read from the bus.</item>
/// <item>A children-changed:add takes the child out of the parent's list and puts it back at the
/// index the signal names; past the end, the parent's list is dropped and read from the bus.</item>
/// <item>A children-changed:remove takes the child out of the parent's list.</item>
/// <item>A cache-remove disposes the object: it leaves its parent's list.</item>
/// </list>
/// </summary>
internal sealed class AtSpiClientCache : IAtSpiSignals
{
    /// <summary>Each object's parent, as its last cache-add said.</summary>
    private readonly Dictionary<string, string> _parent = [];

    /// <summary>Each cached list of children; null entries are read from the bus when asked.</summary>
    private readonly Dictionary<string, List<string?>> _children = [];

    /// <summary>Objects whose list the client dropped: their children are read from the bus.</summary>
    private readonly HashSet<string> _uncached = [];

    /// <summary>The children a client reads for <paramref name="path"/>: its cached list, an empty
    /// slot read from <paramref name="tree"/>; all of it from the tree when the list was dropped. A
    /// list that serves its items on demand answers each by its index (GetChildAtIndex).</summary>
    public List<string> ChildrenOf(IAtSpiTree tree, string path)
    {
        var acc = tree.Find(path);
        IReadOnlyList<string> real = acc?.Served is { } served
            ? [.. Enumerable.Range(0, served.Count).Select(served.PathOf)]
            : acc?.ChildPaths ?? [];
        if (_uncached.Contains(path) || !_children.TryGetValue(path, out var cached)) return [.. real];
        var result = new List<string>(cached.Count);
        for (int i = 0; i < cached.Count; i++)
            result.Add(cached[i] ?? (i < real.Count ? real[i] : "<none>"));
        return result;
    }

    public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node)
    {
        _parent[node.Path] = node.ParentPath;
        // Its own list sized to its child count, keeping what it held (g_ptr_array_set_size).
        if (!_children.TryGetValue(node.Path, out var own)) _children[node.Path] = own = [];
        if (own.Count > node.ChildCount) own.RemoveRange(node.ChildCount, own.Count - node.ChildCount);
        while (own.Count < node.ChildCount) own.Add(null);
        _uncached.Remove(node.Path);
        if (node.IndexInParent < 0 || !_children.TryGetValue(node.ParentPath, out var siblings)) return;
        while (siblings.Count <= node.IndexInParent) siblings.Add(null);
        siblings[node.IndexInParent] = node.Path;
    }

    public void EmitCacheRemove(string path)
    {
        if (_parent.Remove(path, out var parent) && _children.TryGetValue(parent, out var siblings))
            siblings.Remove(path);
        _children.Remove(path);
    }

    public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd)
    {
        if (!_children.TryGetValue(parentPath, out var siblings) || _uncached.Contains(parentPath)) return;
        siblings.Remove(childPath);
        if (!isAdd) return;
        if (index < 0 || index > siblings.Count)
        {
            _uncached.Add(parentPath);
            return;
        }
        siblings.Insert(index, childPath);
    }

    public void EmitPropertyChange(string path, string atSpiProperty, string newValue) { }
    public void EmitValueChanged(string path, double value) { }
    public void EmitRoleChange(string path, uint role) { }
    public void EmitStateChanged(string path, string state, bool active) { }
    public void EmitTextChanged(string path, bool insert, int offset, string text) { }
    public void EmitTextCaretMoved(string path, int offset) { }
    public void EmitTextSelectionChanged(string path) { }
    public void EmitAnnouncement(string path, string text, int politeness) { }
}

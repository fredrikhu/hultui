namespace HultUI.Core.Builders;

/// <summary>
/// One row of a tree presented as a list: either a folder gathering a
/// <c>/</c>-separated name segment, or a leaf carrying an item.
/// </summary>
/// <remarks>
/// Nodes are rebuilt whenever the bound items change (they are cheap value-ish
/// objects over the item list), so <see cref="Depth"/> lives on the node rather
/// than being recomputed while flattening.
/// </remarks>
public sealed class TreeNode<T>
{
    /// <summary>What this node is called on screen — the last path segment, never the
    /// whole name. "auth" for <c>feature/auth</c>, whether folder or leaf.</summary>
    public required string Segment { get; init; }

    /// <summary>The full slash path. For a folder, the prefix it gathers
    /// (<c>origin/feature</c>); for a leaf, the item's own full name.</summary>
    public required string Path { get; init; }

    /// <summary>Distance from the top of the tree; roots are 0.</summary>
    public int Depth { get; internal set; }

    public bool IsFolder { get; internal init; }

    /// <summary>The item this leaf carries. Meaningless on a folder.</summary>
    public T Item { get; internal init; } = default!;

    /// <summary>Sub-folders and leaves. Empty on a leaf.</summary>
    public List<TreeNode<T>> Children { get; } = [];

    /// <summary>Leaves anywhere below this node (1 for a leaf itself).</summary>
    public int LeafCount { get; internal set; }

    /// <summary>How many of those satisfied the caller's flag — remote-tracking, for a
    /// branch tree. Lets a folder holding nothing but flagged items say so.</summary>
    public int FlaggedCount { get; internal set; }

    /// <summary>True when every leaf below this node carries the caller's flag.</summary>
    public bool AllFlagged => LeafCount > 0 && FlaggedCount == LeafCount;
}

/// <summary>
/// Groups slash-separated names into a folder tree and flattens the expanded part of
/// it back into a flat row list.
///
/// <para>The flattening is the point: virtualized tables need one flat list of
/// equal-height rows, and building nested element subtrees per folder would undo the
/// virtualization. The tree exists only as data — the collapsed parts are never rows
/// at all, and the expanded ones are rows carrying a depth. Stateless; the stateful
/// half (expansion, binding, keyboard steps) is <see cref="TreeView{T}"/>.</para>
///
/// <para>Ordering is <b>first appearance</b> by default, folders and leaves
/// interleaved, so a deliberately ordered item list carries its order into the tree:
/// a folder sits where its most recent item would have, and names containing no
/// slash at all produce exactly the flat list back. Pass <paramref name="sort"/>
/// (e.g. <see cref="FoldersFirst{T}"/>) to impose an order instead.</para>
/// </summary>
public static class Tree
{
    /// <summary>The tree for <paramref name="items"/>, as a forest of root nodes.
    /// <paramref name="nameOf"/> yields the full slash-separated name;
    /// <paramref name="flag"/> is the optional per-item predicate aggregated into
    /// <see cref="TreeNode{T}.FlaggedCount"/>; <paramref name="sort"/> orders each
    /// node's children recursively when given.</summary>
    public static List<TreeNode<T>> Build<T>(
        IReadOnlyList<T> items, Func<T, string> nameOf, Func<T, bool>? flag = null,
        Comparison<TreeNode<T>>? sort = null)
    {
        var roots = new List<TreeNode<T>>();
        if (items.Count == 0) return roots;

        // Pass 1: every path that any item uses as a prefix is a folder. Collected up
        // front because an item can arrive before the one that turns its own name into
        // a folder — `release` may be listed ahead of `release/1.2`, and it still
        // belongs inside the folder rather than beside it.
        var folderPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var segs = Segments(nameOf(item));
            for (var i = 1; i < segs.Length; i++)
                folderPaths.Add(string.Join('/', segs, 0, i));
        }

        // Pass 2: place each item, creating the folders on its path as they are first
        // needed so folder order follows the item order.
        var folders = new Dictionary<string, TreeNode<T>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var name = nameOf(item) ?? "";
            var segs = Segments(name);

            // How deep the owning folder chain runs. An item whose own name is also a
            // folder prefix goes *inside* that folder (`release` sits under the folder
            // `release`, beside `1.2`); otherwise its parent is the path minus the last
            // segment. A name with nothing but separators ("", "/") has no segments to
            // group by at all and lands at the root under its own name.
            var isOwnFolder = segs.Length > 0 && folderPaths.Contains(string.Join('/', segs));
            var depth = isOwnFolder ? segs.Length : Math.Max(0, segs.Length - 1);
            var siblings = depth > 0 ? FolderAt(segs, depth).Children : roots;

            var leaf = new TreeNode<T>
            {
                Segment = segs.Length > 0 ? segs[^1] : name,
                Path = name,
                IsFolder = false,
                Item = item,
                Depth = depth,
                LeafCount = 1,
                FlaggedCount = flag?.Invoke(item) == true ? 1 : 0,
            };
            siblings.Add(leaf);

            // Roll the counts up the chain of folders this item sits under.
            for (var i = depth; i >= 1; i--)
            {
                var node = folders[string.Join('/', segs, 0, i)];
                node.LeafCount += leaf.LeafCount;
                node.FlaggedCount += leaf.FlaggedCount;
            }
        }

        if (sort is not null) SortRecursive(roots, sort);
        return roots;

        // The folder at segs[0..count], creating it (and any missing ancestor) in place.
        TreeNode<T> FolderAt(string[] segs, int count)
        {
            TreeNode<T>? node = null;
            for (var i = 1; i <= count; i++)
            {
                var path = string.Join('/', segs, 0, i);
                if (!folders.TryGetValue(path, out var next))
                {
                    next = new TreeNode<T>
                    {
                        Segment = segs[i - 1],
                        Path = path,
                        IsFolder = true,
                        Depth = i - 1,
                    };
                    folders[path] = next;
                    (node?.Children ?? roots).Add(next);
                }
                node = next;
            }
            return node!;
        }
    }

    /// <summary>
    /// Merges every folder that holds nothing but one sub-folder into that sub-folder, so
    /// the chain <c>src</c> → <c>HultUI.Core</c> → <c>Elements</c> becomes a single row
    /// labelled <c>src/HultUI.Core/Elements</c>.
    ///
    /// <para>This is what keeps a tree of deep paths worth having. A commit touching three
    /// files four directories down is nine rows of chrome without it — and the folders it
    /// drops carry no information: a folder with one sub-folder and nothing else is a step
    /// on the way, not a grouping. Folders that genuinely branch are left alone, so the
    /// rows that survive are exactly the ones where the tree divides.</para>
    ///
    /// <para>A merged node keeps the <b>deepest</b> path, so expansion state, reveal, and
    /// everything else keyed on a folder's path still names the folder its rows sit in;
    /// only <see cref="TreeNode{T}.Segment"/> grows the joined label. Nodes are rewritten
    /// in place where they can be, so call this on a forest fresh out of
    /// <see cref="Build{T}"/> rather than one something else is holding.</para>
    /// </summary>
    public static List<TreeNode<T>> Compact<T>(IReadOnlyList<TreeNode<T>> roots)
    {
        var result = new List<TreeNode<T>>(roots.Count);
        foreach (var node in roots) result.Add(CompactNode(node, node.Depth));
        return result;
    }

    private static TreeNode<T> CompactNode<T>(TreeNode<T> node, int depth)
    {
        node.Depth = depth;
        if (!node.IsFolder) return node;

        // Walk down while this folder's only child is itself a folder, joining the
        // segments as we go. A single *leaf* child stops the walk: that is a folder
        // holding one file, which is a grouping and stays one.
        var deepest = node;
        var segment = node.Segment;
        while (deepest.Children is [{ IsFolder: true } only])
        {
            segment += '/' + only.Segment;
            deepest = only;
        }

        // The counts are the same all the way down a single-child chain, so the merged
        // node can take them from either end.
        var merged = ReferenceEquals(deepest, node) ? node : new TreeNode<T>
        {
            Segment = segment,
            Path = deepest.Path,
            IsFolder = true,
            Depth = depth,
            LeafCount = deepest.LeafCount,
            FlaggedCount = deepest.FlaggedCount,
        };
        if (!ReferenceEquals(merged, node)) merged.Children.AddRange(deepest.Children);

        // Everything below moved up by however many levels the chain lost.
        var children = merged.Children;
        for (var i = 0; i < children.Count; i++)
            children[i] = CompactNode(children[i], depth + 1);
        return merged;
    }

    /// <summary>Folders before leaves, both alphabetical by segment (case-insensitive) —
    /// the file-manager convention for path trees.</summary>
    public static Comparison<TreeNode<T>> FoldersFirst<T>() => static (a, b) =>
        a.IsFolder != b.IsFolder ? (a.IsFolder ? -1 : 1)
        : string.Compare(a.Segment, b.Segment, StringComparison.OrdinalIgnoreCase);

    private static void SortRecursive<T>(List<TreeNode<T>> nodes, Comparison<TreeNode<T>> sort)
    {
        nodes.Sort(sort);
        foreach (var node in nodes)
            if (node.Children.Count > 0)
                SortRecursive(node.Children, sort);
    }

    /// <summary>The expanded tree as one flat, depth-first row list — collapsed folders
    /// contribute their own row and nothing below it. This is what a virtual list gets.
    /// </summary>
    public static List<TreeNode<T>> Flatten<T>(
        IReadOnlyList<TreeNode<T>> roots, Func<TreeNode<T>, bool> isExpanded)
    {
        var rows = new List<TreeNode<T>>(roots.Count);
        Walk(roots);
        return rows;

        void Walk(IReadOnlyList<TreeNode<T>> nodes)
        {
            foreach (var node in nodes)
            {
                rows.Add(node);
                if (node.IsFolder && isExpanded(node)) Walk(node.Children);
            }
        }
    }

    /// <summary>Every folder at or below <paramref name="node"/>, itself included. Used by
    /// expand-all / collapse-all subtree commands.</summary>
    public static IEnumerable<TreeNode<T>> Folders<T>(TreeNode<T> node)
    {
        if (!node.IsFolder) yield break;
        yield return node;
        foreach (var child in node.Children)
            foreach (var nested in Folders(child))
                yield return nested;
    }

    /// <summary>The same items as leaf rows at depth 0 — the flat table, expressed in the
    /// tree's row type so both presentations share one row template and one selection
    /// index.</summary>
    public static List<TreeNode<T>> Flat<T>(IReadOnlyList<T> items, Func<T, string> nameOf)
    {
        var rows = new List<TreeNode<T>>(items.Count);
        foreach (var item in items)
        {
            var name = nameOf(item) ?? "";
            rows.Add(new TreeNode<T>
            {
                Segment = name,
                Path = name,
                IsFolder = false,
                Item = item,
                Depth = 0,
                LeafCount = 1,
            });
        }
        return rows;
    }

    /// <summary>The name split on <c>/</c> with empty segments dropped, so "a//b", "/a"
    /// and "a/" group the same way git itself treats them — as one or two real segments.
    /// </summary>
    internal static string[] Segments(string? name) =>
        string.IsNullOrEmpty(name) ? [] : name.Split('/', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// The stateful half of a tree-shaped list: which folders are open, the visible rows
/// for a virtual list, and the conventional folder keys. A model, not a control — row
/// rendering stays with the app, the way <see cref="TextSearch"/> leaves row painting
/// to the four search bars built on it.
///
/// <para>Bind the items during <c>Build()</c> and read <see cref="Rows"/>; binding is
/// memoized on the item list's identity, so a plain rebuild costs one reference
/// check. Expansion is explicit overrides over <see cref="DefaultExpanded"/>, which
/// makes both conventions one setting: a file tree starts fully collapsed (the
/// default default), a thousand-branch tree starts with only oversized folders
/// folded (<c>node =&gt; node.LeafCount &lt;= 30</c>). Only folders someone actually
/// toggled are remembered; everything else follows the predicate.</para>
///
/// <para>Selection stays with the caller: lists already have selection semantics
/// (an index, a path, pinned rows), and the model would get them wrong. What it
/// offers is <see cref="Step"/>, which turns ←/→ into the folder moves every tree
/// implements and hands back the row to land on.</para>
/// </summary>
public sealed class TreeView<T>
{
    private readonly Action _onChanged;
    private readonly Dictionary<string, bool> _overrides = new(StringComparer.Ordinal);

    private IReadOnlyList<T>? _boundItems;
    private Comparison<TreeNode<T>>? _boundSort;
    private List<TreeNode<T>> _roots = [];
    private bool _compactFolders;

    /// <param name="onChanged">Raised when expansion changes outside a bind — the
    /// app's <c>MarkDirty</c>, so a toggle repaints without every caller remembering
    /// to.</param>
    public TreeView(Action onChanged) => _onChanged = onChanged;

    /// <summary>What a folder nobody has touched does. Null means collapsed — the file
    /// tree convention. A size guard (<c>node =&gt; node.LeafCount &lt;= 30</c>) opens
    /// small trees fully and folds only where a flat list was unreadable anyway.</summary>
    public Func<TreeNode<T>, bool>? DefaultExpanded { get; set; }

    /// <summary>Whether single-child folder chains collapse into one row — see
    /// <see cref="Tree.Compact{T}"/>. Worth it for path trees, where the chains are long
    /// and empty; pointless for ref names, which are two or three segments deep. Setting
    /// it drops the bound tree so the next <see cref="Bind"/> rebuilds under the new
    /// shape.</summary>
    public bool CompactFolders
    {
        get => _compactFolders;
        set
        {
            if (_compactFolders == value) return;
            _compactFolders = value;
            _boundItems = null;
        }
    }

    /// <summary>The current forest. Rebuilt by <see cref="Bind"/> when the items change.</summary>
    public IReadOnlyList<TreeNode<T>> Roots => _roots;

    /// <summary>
    /// Points the tree at the items and groups them by their slash-separated names.
    /// Call every build; the tree is rebuilt only when <paramref name="items"/> is a
    /// different list instance (or the sort changes), so the per-frame cost of an
    /// unchanged tree is a reference check. Expansion overrides survive a rebind —
    /// which folders are open is a place in a list, not a property of the list.
    /// </summary>
    public void Bind(IReadOnlyList<T> items, Func<T, string> nameOf, Func<T, bool>? flag = null,
        Comparison<TreeNode<T>>? sort = null)
    {
        if (ReferenceEquals(_boundItems, items) && Equals(_boundSort, sort)) return;
        _boundItems = items;
        _boundSort = sort;
        _roots = Tree.Build(items, nameOf, flag, sort);
        if (_compactFolders) _roots = Tree.Compact(_roots);
    }

    /// <summary>Forgets the items and the tree. Expansion overrides are kept, so the
    /// next bind of the same repository comes back the way it was left.</summary>
    public void Unbind()
    {
        _boundItems = null;
        _roots = [];
    }

    /// <summary>The visible rows, depth-first: what the virtual list shows. Pass
    /// <paramref name="expandAll"/> while a filter runs — a search has to be able to
    /// show every match, and the collapsed set is untouched and comes back when the
    /// query clears.</summary>
    public List<TreeNode<T>> Rows(bool expandAll = false) =>
        Tree.Flatten(_roots, expandAll ? _ => true : IsExpanded);

    // ──── Expansion ────

    public bool IsExpanded(TreeNode<T> node) =>
        _overrides.TryGetValue(node.Path, out var open)
            ? open
            : DefaultExpanded?.Invoke(node) ?? false;

    public void Toggle(TreeNode<T> node) => SetExpanded(node, !IsExpanded(node));

    public void SetExpanded(TreeNode<T> node, bool open)
    {
        _overrides[node.Path] = open;
        _onChanged();
    }

    /// <summary>Opens or closes every folder at or below <paramref name="node"/> —
    /// the "expand everything below" context-menu command.</summary>
    public void SetSubtreeExpanded(TreeNode<T> node, bool open)
    {
        foreach (var folder in Tree.Folders(node)) _overrides[folder.Path] = open;
        _onChanged();
    }

    /// <summary>Opens every folder on the way to <paramref name="path"/>, so a reveal
    /// (search hit, restored selection) has its row on screen. The path's own folder,
    /// if it names one, is left alone — revealing a folder should not also open it.</summary>
    public void ExpandAncestors(string path)
    {
        var segs = Tree.Segments(path);
        for (var i = 1; i < segs.Length; i++)
            _overrides[string.Join('/', segs, 0, i)] = true;
        if (segs.Length > 1) _onChanged();
    }

    /// <summary>Drops every override, returning the whole tree to
    /// <see cref="DefaultExpanded"/>. The reset for "this is a different commit now".</summary>
    public void ClearOverrides() => _overrides.Clear();

    // ──── Keyboard ────

    /// <summary>
    /// ←/→ on a tree row, the conventional folder keys: right opens a closed folder
    /// and otherwise steps into it; left closes an open folder and otherwise steps out
    /// to the parent. Returns the row to land on — the caller keeps the selection
    /// semantics its list already has. ↑/↓/Home/End are plain index moves the caller
    /// already does; only the tree-shaped keys need the tree.
    /// </summary>
    public int Step(IReadOnlyList<TreeNode<T>> rows, int index, bool right)
    {
        if (index < 0 || index >= rows.Count) return index;
        var node = rows[index];

        if (right)
        {
            if (node.IsFolder && !IsExpanded(node)) { SetExpanded(node, true); return index; }
            // Rows are depth-first, so the first child is simply the next row — when
            // it is deeper than this one.
            return index + 1 < rows.Count && rows[index + 1].Depth > node.Depth ? index + 1 : index;
        }

        if (node.IsFolder && IsExpanded(node)) { SetExpanded(node, false); return index; }
        for (var i = index - 1; i >= 0; i--)
            if (rows[i].Depth < node.Depth) return i;
        return index;
    }
}

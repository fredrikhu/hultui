namespace Junction;

/// <summary>
/// One row of a ref tree: either a folder gathering a <c>/</c>-separated name
/// segment, or a leaf naming an actual ref.
/// </summary>
/// <remarks>
/// Nodes are rebuilt from scratch on every build (they are cheap value-ish objects
/// over the loaded ref list), so <see cref="Depth"/> can live on the node rather
/// than being recomputed while flattening.
/// </remarks>
public sealed class RefNode<T>
{
    /// <summary>What this node is called on screen — the last path segment, never the
    /// whole name. "auth" for <c>feature/auth</c>, whether folder or leaf.</summary>
    public required string Segment { get; init; }

    /// <summary>The full slash path. For a folder, the prefix it gathers
    /// (<c>origin/feature</c>); for a leaf, the ref's own full name.</summary>
    public required string Path { get; init; }

    /// <summary>Distance from the top of the tree; roots are 0.</summary>
    public int Depth { get; internal set; }

    public bool IsFolder { get; internal init; }

    /// <summary>The ref this leaf names. Meaningless on a folder.</summary>
    public T Item { get; internal init; } = default!;

    /// <summary>Sub-folders and leaves, in first-appearance order. Empty on a leaf.</summary>
    public List<RefNode<T>> Children { get; } = [];

    /// <summary>Refs anywhere below this node (1 for a leaf).</summary>
    public int LeafCount { get; internal set; }

    /// <summary>How many of those satisfied the caller's flag — remote-tracking, for
    /// the branch tree. Lets a folder holding nothing but remote refs say so.</summary>
    public int FlaggedCount { get; internal set; }

    /// <summary>True when every ref below this node carries the caller's flag.</summary>
    public bool AllFlagged => LeafCount > 0 && FlaggedCount == LeafCount;
}

/// <summary>
/// Groups ref names into a folder tree and flattens the expanded part of it back
/// into a flat row list.
///
/// <para>The flattening is the point: the Branches and Tags tables feed
/// <c>VirtualListElement</c>, which needs one flat list of equal-height rows. Building
/// nested element subtrees per folder would undo the virtualization that took the
/// 1000-branch tab switch from 700 ms to single digits, so the tree exists only as
/// data — the collapsed parts are never rows at all, and the expanded ones are rows
/// carrying a depth.</para>
///
/// <para>Ordering is <b>first appearance</b> throughout, folders and leaves
/// interleaved. The flat tables are ordered deliberately (current branch pinned to
/// the top, then most-recent first), and first-appearance ordering carries that
/// through: a folder sits where its most recent ref would have, and a repo whose
/// names contain no slash at all produces exactly the flat list back.</para>
/// </summary>
public static class RefTree
{
    /// <summary>The tree for <paramref name="items"/>, as a forest of root nodes.
    /// <paramref name="nameOf"/> yields the full ref name; <paramref name="flag"/> is
    /// the optional per-ref predicate aggregated into <see cref="RefNode{T}.FlaggedCount"/>.
    /// </summary>
    public static List<RefNode<T>> Build<T>(
        IReadOnlyList<T> items, Func<T, string> nameOf, Func<T, bool>? flag = null)
    {
        var roots = new List<RefNode<T>>();
        if (items.Count == 0) return roots;

        // Pass 1: every path that any ref uses as a prefix is a folder. Collected up
        // front because a ref can arrive before the one that turns its own name into a
        // folder — `release` may be listed ahead of `release/1.2`, and it still belongs
        // inside the folder rather than beside it.
        var folderPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var segs = Segments(nameOf(item));
            for (var i = 1; i < segs.Length; i++)
                folderPaths.Add(string.Join('/', segs, 0, i));
        }

        // Pass 2: place each ref, creating the folders on its path as they are first
        // needed so folder order follows the ref order.
        var folders = new Dictionary<string, RefNode<T>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var name = nameOf(item) ?? "";
            var segs = Segments(name);

            // How deep the owning folder chain runs. A ref whose own name is also a
            // folder prefix goes *inside* that folder (`release` sits under the folder
            // `release`, beside `1.2`); otherwise its parent is the path minus the last
            // segment. A name with nothing but separators ("", "/") has no segments to
            // group by at all and lands at the root under its own name.
            var isOwnFolder = segs.Length > 0 && folderPaths.Contains(string.Join('/', segs));
            var depth = isOwnFolder ? segs.Length : Math.Max(0, segs.Length - 1);
            var siblings = depth > 0 ? FolderAt(segs, depth).Children : roots;

            var leaf = new RefNode<T>
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

            // Roll the counts up the chain of folders this ref sits under.
            for (var i = depth; i >= 1; i--)
            {
                var node = folders[string.Join('/', segs, 0, i)];
                node.LeafCount += leaf.LeafCount;
                node.FlaggedCount += leaf.FlaggedCount;
            }
        }

        return roots;

        // The folder at segs[0..count], creating it (and any missing ancestor) in place.
        RefNode<T> FolderAt(string[] segs, int count)
        {
            RefNode<T>? node = null;
            for (var i = 1; i <= count; i++)
            {
                var path = string.Join('/', segs, 0, i);
                if (!folders.TryGetValue(path, out var next))
                {
                    next = new RefNode<T>
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

    /// <summary>The expanded tree as one flat, depth-first row list — collapsed folders
    /// contribute their own row and nothing below it. This is what the virtual list gets.
    /// </summary>
    public static List<RefNode<T>> Flatten<T>(
        IReadOnlyList<RefNode<T>> roots, Func<RefNode<T>, bool> isExpanded)
    {
        var rows = new List<RefNode<T>>(roots.Count);
        Walk(roots);
        return rows;

        void Walk(IReadOnlyList<RefNode<T>> nodes)
        {
            foreach (var node in nodes)
            {
                rows.Add(node);
                if (node.IsFolder && isExpanded(node)) Walk(node.Children);
            }
        }
    }

    /// <summary>Every folder at or below <paramref name="node"/>, itself included. Used by
    /// the expand-all / collapse-all subtree commands.</summary>
    public static IEnumerable<RefNode<T>> Folders<T>(RefNode<T> node)
    {
        if (!node.IsFolder) yield break;
        yield return node;
        foreach (var child in node.Children)
            foreach (var nested in Folders(child))
                yield return nested;
    }

    /// <summary>The same items as leaf rows at depth 0 — the flat table, expressed in the
    /// tree's row type so both presentations share one row template and one selection index.
    /// </summary>
    public static List<RefNode<T>> Flat<T>(IReadOnlyList<T> items, Func<T, string> nameOf)
    {
        var rows = new List<RefNode<T>>(items.Count);
        foreach (var item in items)
        {
            var name = nameOf(item) ?? "";
            rows.Add(new RefNode<T>
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
    private static string[] Segments(string? name) =>
        string.IsNullOrEmpty(name) ? [] : name.Split('/', StringSplitOptions.RemoveEmptyEntries);
}

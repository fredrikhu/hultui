using GitClient.Git;

namespace GitClient.Components.Shared;

/// <summary>
/// A node in a file-path tree. Folders aggregate the files beneath them; leaves carry a
/// <see cref="GitFileChange"/>. <see cref="Segment"/> is the user-visible label and may join
/// multiple path parts when single-folder chains are collapsed (e.g. "src/components/Detail").
/// </summary>
public class PathTreeNode
{
    public string Segment { get; set; } = "";
    public string FullPath { get; set; } = "";
    public List<PathTreeNode> Children { get; } = [];
    public GitFileChange? File { get; init; }

    public bool IsFile => File != null;
    public int FileCount { get; private set; }
    public int Additions { get; private set; }
    public int Deletions { get; private set; }

    internal void ComputeAggregates()
    {
        if (IsFile)
        {
            FileCount = 1;
            Additions = File!.Additions;
            Deletions = File.Deletions;
            return;
        }

        FileCount = 0;
        Additions = 0;
        Deletions = 0;
        foreach (var c in Children)
        {
            c.ComputeAggregates();
            FileCount += c.FileCount;
            Additions += c.Additions;
            Deletions += c.Deletions;
        }
    }
}

public static class PathTree
{
    public static List<PathTreeNode> Build(IEnumerable<GitFileChange> files, bool collapseChains = true)
    {
        var root = new PathTreeNode();
        foreach (var file in files)
        {
            var parts = file.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            Insert(root, parts, 0, "", file);
        }

        if (collapseChains)
            foreach (var child in root.Children) CollapseChains(child);

        Sort(root);
        foreach (var child in root.Children) child.ComputeAggregates();
        return root.Children;
    }

    private static void Insert(PathTreeNode parent, string[] parts, int index, string parentPath, GitFileChange file)
    {
        var seg = parts[index];
        var fullPath = parentPath.Length == 0 ? seg : $"{parentPath}/{seg}";

        if (index == parts.Length - 1)
        {
            parent.Children.Add(new PathTreeNode { Segment = seg, FullPath = fullPath, File = file });
            return;
        }

        var existing = parent.Children.FirstOrDefault(c => !c.IsFile && c.Segment == seg);
        if (existing == null)
        {
            existing = new PathTreeNode { Segment = seg, FullPath = fullPath };
            parent.Children.Add(existing);
        }
        Insert(existing, parts, index + 1, fullPath, file);
    }

    // Post-order: collapse descendants first, then merge this node with its lone folder child.
    // "foo" > "bar" > "baz.txt"  becomes  "foo/bar" > "baz.txt".
    private static void CollapseChains(PathTreeNode node)
    {
        foreach (var child in node.Children) CollapseChains(child);

        while (!node.IsFile && node.Children.Count == 1 && !node.Children[0].IsFile)
        {
            var only = node.Children[0];
            node.Segment = $"{node.Segment}/{only.Segment}";
            node.FullPath = only.FullPath;
            node.Children.Clear();
            foreach (var c in only.Children) node.Children.Add(c);
        }
    }

    private static void Sort(PathTreeNode node)
    {
        node.Children.Sort((a, b) =>
        {
            if (a.IsFile != b.IsFile) return a.IsFile ? 1 : -1;
            return string.Compare(a.Segment, b.Segment, StringComparison.OrdinalIgnoreCase);
        });
        foreach (var c in node.Children) Sort(c);
    }
}

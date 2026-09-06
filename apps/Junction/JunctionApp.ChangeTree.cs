using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.Theme;

namespace Junction;

/// <summary>
/// The folder grouping shared by the two lists of changed files — the Commit view's
/// "files changed" list and the Changes view's staged/unstaged/conflicted lists.
///
/// <para>Both used to be flat, each row carrying its directory as a grey second line.
/// That reads well for a handful of files at the repository root and badly for
/// anything else: the same prefix repeated down twenty rows, sorted by whatever git
/// emitted, with no way to see that a commit touched three areas. Grouping by folder
/// says it in the shape.</para>
///
/// <para>The tree is the framework's <see cref="TreeView{T}"/>, the same model the
/// Files tab browses a whole commit with. Two settings make it a *change* list rather
/// than a file browser: folders start open (the list is small, and the tree is here to
/// group it, not to fold it away), and single-child folder chains join into one row
/// (<see cref="Tree.Compact{T}"/>) — without that, three files four directories down
/// are nine rows of chrome and the tree costs more than the flat list it replaced.</para>
/// </summary>
public partial class JunctionApp
{
    /// <summary>Folders before files, each alphabetical — the file-manager order, and the
    /// one the Files tab already uses. Change lists arrive in git's order, which groups
    /// nothing.</summary>
    private static readonly Comparison<TreeNode<GitFileChange>> ChangeTreeSort =
        Tree.FoldersFirst<GitFileChange>();

    /// <summary>How far one level shifts a row. Narrower than the Files tab's tree: these
    /// lists sit in a 238–330px column beside a diff, so depth has to stay cheap.</summary>
    private const float ChangeTreeIndent = 11f;

    private TreeView<GitFileChange> NewChangeTree() => new(MarkDirty)
    {
        CompactFolders = true,
        DefaultExpanded = _ => true,
    };

    private TreeView<GitFileChange> ChangeTreeFor(ChangeSection section)
    {
        if (!_changeTrees.TryGetValue(section, out var tree))
            _changeTrees[section] = tree = NewChangeTree();
        return tree;
    }

    /// <summary>One section's visible rows. Binding is memoized on the file list's
    /// identity, so calling this per build and again per keyboard step costs a reference
    /// check and the flatten.</summary>
    private List<TreeNode<GitFileChange>> ChangeTreeRows(ChangeSection section, IReadOnlyList<GitFileChange> files)
    {
        var tree = ChangeTreeFor(section);
        tree.Bind(files, f => f.Path, sort: ChangeTreeSort);
        return tree.Rows();
    }

    private static IReadOnlyList<GitFileChange> SectionFiles(ChangesData data, ChangeSection section) =>
        section switch
        {
            ChangeSection.Conflicted => data.Conflicted,
            ChangeSection.Staged => data.Staged,
            _ => data.Unstaged,
        };

    /// <summary>Drops the expansion state when the Changes view moves to another
    /// repository. Which folders are open is a place in one repository's list of changes,
    /// not a preference that should travel.</summary>
    private void SyncChangeTrees(string repoPath)
    {
        if (_changeTreeRepo == repoPath) return;
        _changeTreeRepo = repoPath;
        foreach (var tree in _changeTrees.Values) tree.ClearOverrides();
    }

    /// <summary>The files a folder row stands for, in the list's own order. Taken by path
    /// prefix rather than by walking the node, so a caller holding nothing but a
    /// <see cref="ChangeRef"/> (a context menu built after the fact, a retarget after a
    /// staging action) gets the same answer as one holding the node.</summary>
    private static List<GitFileChange> FilesUnder(IReadOnlyList<GitFileChange> files, string folderPath)
    {
        var prefix = folderPath + "/";
        var under = new List<GitFileChange>();
        foreach (var f in files)
            if (f.Path.StartsWith(prefix, StringComparison.Ordinal))
                under.Add(f);
        return under;
    }

    /// <summary>The additions and deletions of everything below a folder row.</summary>
    private static (int Additions, int Deletions) FolderTotals(IReadOnlyList<GitFileChange> under)
    {
        var (add, del) = (0, 0);
        foreach (var f in under) { add += f.Additions; del += f.Deletions; }
        return (add, del);
    }

    /// <summary>
    /// The chrome a folder row shares between the two lists: the indent, the twisty, the
    /// folder glyph, the joined name and the file count. <paramref name="lead"/> goes
    /// before the twisty — the Changes list puts its staging checkbox there so folder and
    /// file rows line up on the same column — and <paramref name="trail"/> after the count.
    ///
    /// <para>Selection, hover and what a click does stay with the caller: the two lists
    /// disagree about all three, and this is the part they agree on.</para>
    /// </summary>
    private BoxElement ChangeFolderRow(TreeNode<GitFileChange> node, bool expanded,
        Element? lead = null, Element? trail = null)
    {
        // Lead · twisty · folder glyph · name · count · trail, the glyph slots sized to
        // their content since the two icons are not the same width.
        var row = Grid.Of(lead != null ? ChangeFolderColumnsWithLead : ChangeFolderColumns, gap: 6)
            .AlignItems(AlignItems.Center)
            .Padding(ChangeRowPadding(node.Depth)).CornerRadius(Nocturne.RadiusRow)
            .Cursor(CursorType.Pointer).Tooltip(node.Path);
        // Tree semantics for assistive tech, as in the Files tab: a folder row announces
        // itself as one, and says whether it is open.
        row.Role = AccessibilityRole.TreeItem;
        row.AccessibleExpanded = expanded;

        int col = 0;
        if (lead != null) row.AddChild(lead.GridColumn(col++));
        row.AddChild(Ui.Icon(expanded ? Icons.CaretDown : Icons.CaretRight, 9f, Nocturne.On(0.5)).GridColumn(col++));
        row.AddChild(Ui.Icon(expanded ? Icons.FolderOpen : Icons.Folders, 12f, Nocturne.Accent300).GridColumn(col++));
        // The joined label can be long ("src/HultUI.Core/Elements"); it ellipsizes from
        // the end, and the tooltip carries the whole path.
        row.AddChild(Text.New(node.Segment).FontSize(11.5f).FontWeight(FontWeight.Medium)
            .TextColor(Nocturne.On(0.78)).GridColumn(col++)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(Text.New(node.LeafCount.ToString()).FontSize(10f)
            .TextColor(Nocturne.On(0.35)).GridColumn(col++));
        if (trail != null) row.AddChild(trail.GridColumn(col));
        return row;
    }

    // The templates behind the two row kinds. A file row is checkbox · letter · name ·
    // counts, with the letter on a fixed track so names start on one column. A folder
    // row's icon slots size to their glyphs; its name takes the rest either way.
    private const string ChangeRowColumns = "auto 14px 1fr auto";
    private const string ChangeFolderColumns = "auto auto 1fr auto auto";
    private const string ChangeFolderColumnsWithLead = "auto auto auto 1fr auto auto";

    /// <summary>The +/− pair a row shows, or null when nothing changed on either side
    /// (a pure rename, a mode change). Shared so a folder's totals read exactly like the
    /// file rows they add up.</summary>
    private static Element? ChangeCounts(int additions, int deletions) =>
        additions > 0 || deletions > 0
            ? Flex.Row(gap: 4).FlexShrink(0).Children(
                Text.New($"+{additions}").FontSize(10f).TextColor(Nocturne.AddCount),
                Text.New($"−{deletions}").FontSize(10f).TextColor(Nocturne.DelCount))
            : null;

    /// <summary>A row's padding at its depth — the indent is left padding rather than a
    /// spacer element, so a row is one box however deep it sits. A row at the root of the
    /// tree keeps the flat list's padding exactly.</summary>
    private static Thickness ChangeRowPadding(int depth) =>
        new(6, 8, 6, 8 + depth * ChangeTreeIndent);
}

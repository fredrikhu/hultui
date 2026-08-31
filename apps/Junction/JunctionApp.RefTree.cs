using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Theme;

namespace Junction;

/// <summary>
/// The tree presentation shared by the Branches and Tags tables: the flat/tree
/// toggle, the per-repo folder expansion state, the folder row, and the
/// tree-shaped keyboard steps.
///
/// <para>Both tables stay virtualized. The tree is only ever data (see
/// <see cref="Tree"/>); what reaches <c>VirtualListElement</c> is one flat list of
/// equal-height <see cref="TreeNode{T}"/> rows — folder rows and ref rows alike — so
/// the row count follows what is expanded, not how deep the tree is, and nothing
/// off-screen is ever built.</para>
/// </summary>
public partial class JunctionApp
{
    /// <summary>Whether refs group into folders by their <c>/</c> segments. One choice for
    /// both tables (they are the same table of refs, differently sourced), persisted like
    /// the diff's reading modes.</summary>
    private bool _refTreeView;

    // Keys for the per-view expansion sets below.
    private const string RefKindBranch = "branch";
    private const string RefKindTag = "tag";

    /// <summary>How far one tree level shifts a row.</summary>
    private const float RefIndent = 16f;

    /// <summary>Folders holding more refs than this start collapsed. Chosen so a repo whose
    /// refs all fit on a couple of screens opens fully — no folder can exceed the limit —
    /// and the tree only folds itself away where the flat list was unreadable to begin with
    /// (a thousand-branch repo opens as a handful of rows).</summary>
    private const int RefFolderAutoCollapse = 30;

    /// <summary>Folder expansion per view per repo: "<c>&lt;kind&gt;\0&lt;repo&gt;</c>" →
    /// folder path → open. Only folders someone actually clicked are in here; everything
    /// else follows the size default above. Session state — which folders are open is a
    /// place in a list, not a preference.</summary>
    private readonly Dictionary<string, Dictionary<string, bool>> _refExpanded = new(StringComparer.Ordinal);

    private Dictionary<string, bool> RefExpandedSet(string kind)
    {
        var key = kind + "\0" + _activeRepoPath;
        if (!_refExpanded.TryGetValue(key, out var set))
            _refExpanded[key] = set = new Dictionary<string, bool>(StringComparer.Ordinal);
        return set;
    }

    private bool IsFolderExpanded<T>(string kind, TreeNode<T> node) =>
        RefExpandedSet(kind).TryGetValue(node.Path, out var open)
            ? open
            : node.LeafCount <= RefFolderAutoCollapse;

    private void ToggleFolder<T>(string kind, TreeNode<T> node)
    {
        RefExpandedSet(kind)[node.Path] = !IsFolderExpanded(kind, node);
        MarkDirty();
    }

    private void SetSubtreeExpanded<T>(string kind, TreeNode<T> node, bool open)
    {
        var set = RefExpandedSet(kind);
        foreach (var folder in Tree.Folders(node)) set[folder.Path] = open;
        MarkDirty();
    }

    /// <summary>The rows a ref table shows: the flattened expanded tree, or the same refs as
    /// depth-0 leaves when the flat list is selected. One row type either way, so the row
    /// template, the selection index and the virtual list don't care which mode is on.
    /// </summary>
    private List<TreeNode<T>> RefRows<T>(
        IReadOnlyList<T> items, string kind, Func<T, string> nameOf, Func<T, bool>? flag, bool filtering)
    {
        if (!_refTreeView) return Tree.Flat(items, nameOf);
        // Compacted: a folder whose only child is another folder is a step on the way, not
        // a grouping, so `feature` → `24.1` → `payments` becomes one row reading
        // "feature/24.1/payments". Ref names branch late and deep — three rows of chrome
        // to reach one branch was the tree repeating itself, and the compacted node keeps
        // the deepest path, so expansion state and the folder menu still name its rows.
        var roots = Tree.Compact(Tree.Build(items, nameOf, flag));
        // A running search has to be able to show every match, so it opens the tree
        // outright. The collapsed set is untouched and comes back when the query clears.
        return Tree.Flatten(roots, filtering ? _ => true : node => IsFolderExpanded(kind, node));
    }

    // ── Flat / tree toggle ───────────────────────────────────────────────

    private Element RefViewToggle() =>
        Flex.Row(gap: 2).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(2))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusRow)
            .Children(
                RefViewButton("Tree", Icons.TreeStructure, true),
                RefViewButton("List", Icons.Rows, false));

    private Element RefViewButton(string label, string glyph, bool tree)
    {
        var active = _refTreeView == tree;
        var fg = active ? Nocturne.Accent : Nocturne.On(0.58);
        var b = Flex.Row(gap: 5).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(4, 9, 4, 9)).CornerRadius(5f).Cursor(CursorType.Pointer)
            .Tooltip(tree ? "Group refs into folders (t)" : "One flat list (t)");
        // An outline, not a border: these size to their content, so a border on the active
        // one would make it 2px wider and slide every button beside it. Layout never reads
        // an outline.
        if (active) b.Background(Nocturne.Bg).Outline(1f, Nocturne.Accent);
        b.AddChild(Ui.Icon(glyph, 12f, fg));
        if (!_compactChrome)
            b.AddChild(Text.New(label).FontSize(11.5f).FontWeight(FontWeight.Medium).TextColor(fg)
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        // Active and inactive differ in fill and border, so the flip has to replace the
        // element rather than patch over the other state's background.
        b.Key = "refview:" + label + (active ? "#a" : "");
        b.OnClick(e => { e.StopPropagation = true; SetRefTreeView(tree); });
        return b;
    }

    private void ToggleRefTreeView() => SetRefTreeView(!_refTreeView);

    private void SetRefTreeView(bool tree)
    {
        if (_refTreeView == tree) return;
        _refTreeView = tree;
        // The two modes are different row lists, so a caret carried across would land on
        // an unrelated ref. Both tables reset to the top.
        _branchSel = 0;
        _tagSel = 0;
        Persist();
        MarkDirty();
    }

    // ── Folder row ───────────────────────────────────────────────────────

    /// <summary>A folder row. Deliberately the same height as the ref rows around it: the
    /// virtual list positions rows (and hands assistive tech their bounds) from one item
    /// height, and the scroll-into-view maths is index × height.</summary>
    private Element RefFolderRow<T>(
        string kind, TreeNode<T> node, int index, bool focused, float height, Action<int> select)
    {
        var expanded = IsFolderExpanded(kind, node);
        var remote = node.AllFlagged;

        var row = Flex.Row(gap: 7).AlignItems(AlignItems.Center)
            .Height(height).FlexShrink(0)
            .Padding(new Thickness(0, 26, 0, 26))
            .CornerRadius(Nocturne.RadiusRow)
            .BorderColor(Nocturne.On(0.06)).BorderWidth(new Thickness(0, 0, 1, 0))
            .Cursor(CursorType.Pointer);
        // Focus and open state both change the row's look, and a collapsing tree moves
        // every row below it — without a key that carries the state, a patched row keeps
        // the previous occupant's handler and clicking one folder opens another.
        row.Key = $"{kind}dir:{node.Path}{(focused ? "#f" : "")}{(expanded ? "#o" : "")}";
        row.Background(focused ? Nocturne.Neutral900 : Color.Transparent);

        if (node.Depth > 0)
            row.AddChild(Box.New().Width(node.Depth * RefIndent).FlexShrink(0));
        row.AddChild(Ui.Icon(expanded ? Icons.CaretDown : Icons.CaretRight, 10f, Nocturne.On(0.55)).FlexShrink(0));
        row.AddChild(Ui.Icon(remote ? Icons.Cloud : expanded ? Icons.FolderOpen : Icons.Folders, 13f,
            remote ? Nocturne.On(0.5) : Nocturne.Accent300).FlexShrink(0));
        row.AddChild(Ui.Mono(node.Segment, 12.5f, Nocturne.Text)
            .FontWeight(FontWeight.Medium)
            .FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(Ui.Tag(node.LeafCount.ToString(), Nocturne.Neutral800, Nocturne.Neutral300, 9.5f));
        row.AddChild(Box.New().FlexGrow(1));

        row.OnClick(_ => { select(index); ToggleFolder(kind, node); });
        row.On<PointerDownEvent>(e => { if (e.Button == 2) select(index); });
        row.ContextMenu(() => RefFolderMenuItems(kind, node));
        return row;
    }

    /// <summary>What a folder row says to a screen reader. The virtual list never realizes
    /// off-screen rows as elements, so this label is all assistive tech gets.</summary>
    private string RefFolderLabel<T>(string kind, TreeNode<T> node) =>
        $"{node.Segment}, folder, {node.LeafCount} refs, " +
        (IsFolderExpanded(kind, node) ? "expanded" : "collapsed");

    private IReadOnlyList<ContextMenuItem> RefFolderMenuItems<T>(string kind, TreeNode<T> node)
    {
        var expanded = IsFolderExpanded(kind, node);
        return
        [
            Item(expanded ? Icons.CaretRight : Icons.CaretDown,
                expanded ? "Collapse" : "Expand", () => ToggleFolder(kind, node)),
            ContextMenuItem.Separator(),
            Item(Icons.FolderOpen, "Expand everything below", () => SetSubtreeExpanded(kind, node, true)),
            Item(Icons.Folders, "Collapse everything below", () => SetSubtreeExpanded(kind, node, false)),
            ContextMenuItem.Separator(),
            Item(Icons.Copy, "Copy folder path", () => CopyToClipboard(node.Path)),
        ];
    }

    // ── Keyboard ─────────────────────────────────────────────────────────

    /// <summary>Left/right on a tree row, the conventional folder keys: right opens a closed
    /// folder and otherwise steps into it, left closes an open folder and otherwise steps out
    /// to the parent. Returns the row to land on (the caller keeps the selection semantics
    /// its list already has).</summary>
    private int RefTreeStep<T>(IReadOnlyList<TreeNode<T>> rows, string kind, int sel, bool right)
    {
        if (sel < 0 || sel >= rows.Count) return sel;
        var node = rows[sel];

        if (right)
        {
            if (node.IsFolder && !IsFolderExpanded(kind, node)) { ToggleFolder(kind, node); return sel; }
            // Rows are depth-first, so the first child is simply the next row — when it is
            // deeper than this one.
            return sel + 1 < rows.Count && rows[sel + 1].Depth > node.Depth ? sel + 1 : sel;
        }

        if (node.IsFolder && IsFolderExpanded(kind, node)) { ToggleFolder(kind, node); return sel; }
        for (var i = sel - 1; i >= 0; i--)
            if (rows[i].Depth < node.Depth) return i;
        return sel;
    }
}

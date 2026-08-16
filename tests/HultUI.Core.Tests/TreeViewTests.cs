using HultUI.Core.Builders;
using Xunit;

namespace HultUI.Core.Tests;

/// <summary>
/// The stateful half of the tree model: which folders are open, the visible rows,
/// and the ←/→ steps. The pure grouping/flattening underneath is covered in
/// TreeTests (moved here with the algorithm when Junction's RefTree was promoted).
/// </summary>
public class TreeViewTests
{
    private static TreeView<string> View(Action? onChanged = null) =>
        new(onChanged ?? (() => { }));

    [Fact]
    public void FoldersFirstSort_PutsFoldersAboveLeaves_Alphabetically()
    {
        var roots = Tree.Build(
            new[] { "zeta.txt", "src/main.cs", "alpha.txt", "docs/readme.md" },
            n => n, sort: Tree.FoldersFirst<string>());

        Assert.Equal(["docs", "src", "alpha.txt", "zeta.txt"], roots.Select(r => r.Segment));
    }

    // ──── Expansion and rows ────

    [Fact]
    public void UntouchedFolders_FollowTheDefault_CollapsedWhenUnset()
    {
        var view = View();
        view.Bind(["feature/auth", "feature/login", "main"], n => n);

        Assert.Equal(["feature", "main"], view.Rows().Select(r => r.Segment));
    }

    [Fact]
    public void DefaultExpanded_OpensSmallFoldersAndFoldsLargeOnes()
    {
        var view = View();
        view.DefaultExpanded = n => n.LeafCount <= 2;
        view.Bind(["small/a", "small/b", "big/1", "big/2", "big/3"], n => n);

        Assert.Equal(["small", "a", "b", "big"], view.Rows().Select(r => r.Segment));
    }

    [Fact]
    public void AnExplicitToggle_WinsOverTheDefault_AndRaisesOnChanged()
    {
        int changed = 0;
        var view = View(() => changed++);
        view.DefaultExpanded = _ => true;
        view.Bind(["folder/leaf"], n => n);

        view.Toggle(view.Roots[0]);   // default-open folder, explicitly closed

        Assert.Equal(1, changed);
        Assert.Equal(["folder"], view.Rows().Select(r => r.Segment));
    }

    [Fact]
    public void ExpandAll_ShowsEverything_WithoutTouchingTheOverrides()
    {
        var view = View();
        view.Bind(["a/b/c", "a/d"], n => n);

        Assert.Equal(["a", "b", "c", "d"], view.Rows(expandAll: true).Select(r => r.Segment));
        // The collapsed set comes back when the filter clears.
        Assert.Equal(["a"], view.Rows().Select(r => r.Segment));
    }

    [Fact]
    public void ExpandAncestors_OpensThePathToARow_ButNotTheRowItself()
    {
        var view = View();
        view.Bind(["a/b/c/file.txt", "a/x"], n => n);

        view.ExpandAncestors("a/b/c/file.txt");

        Assert.Equal(["a", "b", "c", "file.txt", "x"], view.Rows().Select(r => r.Segment));
    }

    [Fact]
    public void SetSubtreeExpanded_OpensEveryFolderBelow()
    {
        var view = View();
        view.Bind(["a/b/c/d.txt", "a/e/f.txt"], n => n);

        view.SetSubtreeExpanded(view.Roots[0], true);

        Assert.Equal(["a", "b", "c", "d.txt", "e", "f.txt"],
            view.Rows().Select(r => r.Segment));
    }

    [Fact]
    public void ClearOverrides_ReturnsTheTreeToItsDefaults()
    {
        var view = View();
        view.Bind(["folder/leaf"], n => n);
        view.SetExpanded(view.Roots[0], true);

        view.ClearOverrides();

        Assert.Equal(["folder"], view.Rows().Select(r => r.Segment));
    }

    [Fact]
    public void Bind_IsMemoizedOnTheListInstance_AndOverridesSurviveARebind()
    {
        var view = View();
        string[] items = ["folder/leaf"];
        view.Bind(items, n => n);
        var roots = view.Roots;

        view.Bind(items, n => n);
        Assert.Same(roots, view.Roots);   // unchanged list: one reference check

        view.SetExpanded(view.Roots[0], true);
        view.Bind(new[] { "folder/leaf", "folder/other" }, n => n);
        Assert.NotSame(roots, view.Roots);
        // The folder someone opened is still open in the new tree.
        Assert.Equal(["folder", "leaf", "other"], view.Rows().Select(r => r.Segment));
    }

    // ──── Compaction ────

    [Fact]
    public void CompactFolders_JoinsTheChains_AndKeepsTheDeepestPathAsTheKey()
    {
        var view = View();
        view.CompactFolders = true;
        view.DefaultExpanded = _ => true;
        view.Bind(["src/Core/Elements/Element.cs"], n => n);

        Assert.Equal(["src/Core/Elements", "Element.cs"], view.Rows().Select(r => r.Segment));
        // Toggling the joined row is toggling the folder it stands for, so the state
        // survives a rebind the same way an ordinary folder's does.
        view.Toggle(view.Roots[0]);
        Assert.Equal(["src/Core/Elements"], view.Rows().Select(r => r.Segment));
        view.Bind(["src/Core/Elements/Element.cs", "src/Core/Elements/Text.cs"], n => n);
        Assert.Equal(["src/Core/Elements"], view.Rows().Select(r => r.Segment));
    }

    [Fact]
    public void SettingCompactFolders_RebuildsATreeThatWasAlreadyBound()
    {
        var view = View();
        view.DefaultExpanded = _ => true;
        string[] items = ["a/b/c.txt"];
        view.Bind(items, n => n);
        Assert.Equal(["a", "b", "c.txt"], view.Rows().Select(r => r.Segment));

        // Bind is memoized on the list instance, so the flag has to invalidate it —
        // otherwise the same list would keep answering with the old shape.
        view.CompactFolders = true;
        view.Bind(items, n => n);

        Assert.Equal(["a/b", "c.txt"], view.Rows().Select(r => r.Segment));
    }

    [Fact]
    public void ExpandAncestors_StillReachesARowUnderJoinedFolders()
    {
        var view = View();
        view.CompactFolders = true;
        view.Bind(["a/b/c/deep.txt", "a/b/c/other.txt", "top.txt"], n => n);

        view.ExpandAncestors("a/b/c/deep.txt");

        Assert.Equal(["a/b/c", "deep.txt", "other.txt", "top.txt"],
            view.Rows().Select(r => r.Segment));
    }

    // ──── TreeView: keyboard steps ────

    private static (TreeView<string> view, List<TreeNode<string>> rows) SteppingTree()
    {
        var view = View();
        view.Bind(["folder/one", "folder/two", "main"], n => n);
        view.SetExpanded(view.Roots[0], true);
        return (view, view.Rows());   // folder, one, two, main
    }

    [Fact]
    public void RightOnAClosedFolder_OpensIt_AndStays()
    {
        var view = View();
        view.Bind(["folder/leaf"], n => n);
        var rows = view.Rows();

        int landed = view.Step(rows, 0, right: true);

        Assert.Equal(0, landed);
        Assert.True(view.IsExpanded(rows[0]));
    }

    [Fact]
    public void RightOnAnOpenFolder_StepsIntoItsFirstChild()
    {
        var (view, rows) = SteppingTree();

        Assert.Equal(1, view.Step(rows, 0, right: true));
    }

    [Fact]
    public void RightOnALeaf_StaysPut()
    {
        var (view, rows) = SteppingTree();

        Assert.Equal(3, view.Step(rows, 3, right: true));
    }

    [Fact]
    public void LeftOnAnOpenFolder_ClosesIt_AndStays()
    {
        var (view, rows) = SteppingTree();

        int landed = view.Step(rows, 0, right: false);

        Assert.Equal(0, landed);
        Assert.False(view.IsExpanded(rows[0]));
    }

    [Fact]
    public void LeftOnANestedLeaf_StepsOutToItsParentFolder()
    {
        var (view, rows) = SteppingTree();

        Assert.Equal(0, view.Step(rows, 2, right: false));   // "two" → "folder"
    }

    [Fact]
    public void LeftOnARootLeaf_StaysPut()
    {
        var (view, rows) = SteppingTree();

        Assert.Equal(3, view.Step(rows, 3, right: false));
    }
}

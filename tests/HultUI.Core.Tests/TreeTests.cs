using HultUI.Core.Builders;
using Xunit;

namespace HultUI.Core.Tests;

/// <summary>
/// The name → folder-tree grouping and the expanded-tree flattening behind every
/// tree-shaped list (Junction's Branches, Tags and Files trees). Both are pure
/// functions over a list of names, which is why they are tested here rather than
/// through hosted views: the awkward cases (a ref that is also a folder prefix,
/// names that are nothing but separators, a thousand refs under one remote) are one
/// line each at this level. Moved with the algorithm when Junction's RefTree was
/// promoted into the framework; the stateful half is covered in TreeViewTests.
/// </summary>
public class TreeTests
{
    // ── Building ─────────────────────────────────────────────────────────

    [Fact]
    public void NamesWithoutSeparatorsStayAtTheTopInTheOrderTheyCame()
    {
        var roots = Build("main", "develop", "gh-pages");

        Assert.Equal(new[] { "main", "develop", "gh-pages" }, roots.Select(n => n.Segment));
        Assert.All(roots, n => Assert.False(n.IsFolder));
        Assert.All(roots, n => Assert.Equal(0, n.Depth));
    }

    [Fact]
    public void ASlashSplitsTheNameIntoAFolderAndALeaf()
    {
        var roots = Build("feature/login");

        var folder = Assert.Single(roots);
        Assert.True(folder.IsFolder);
        Assert.Equal("feature", folder.Segment);
        Assert.Equal("feature", folder.Path);
        Assert.Equal(0, folder.Depth);

        var leaf = Assert.Single(folder.Children);
        Assert.False(leaf.IsFolder);
        Assert.Equal("login", leaf.Segment);
        Assert.Equal("feature/login", leaf.Path);
        Assert.Equal(1, leaf.Depth);
    }

    [Fact]
    public void RefsUnderTheSamePrefixShareOneFolder()
    {
        var roots = Build("feature/a", "feature/b", "bugfix/c");

        Assert.Equal(new[] { "feature", "bugfix" }, roots.Select(n => n.Segment));
        Assert.Equal(new[] { "a", "b" }, roots[0].Children.Select(n => n.Segment));
        Assert.Equal(new[] { "c" }, roots[1].Children.Select(n => n.Segment));
    }

    [Fact]
    public void FoldersSitWhereTheirFirstRefWouldHave()
    {
        // The flat tables order deliberately (current branch first, then most recent),
        // and first-appearance folder ordering is what carries that through.
        var roots = Build("main", "feature/late", "release/1.0", "feature/later");

        Assert.Equal(new[] { "main", "feature", "release" }, roots.Select(n => n.Segment));
    }

    [Fact]
    public void DeepNamesNestOneFolderPerSegment()
    {
        var roots = Build("a/b/c/d");

        var a = Assert.Single(roots);
        var b = Assert.Single(a.Children);
        var c = Assert.Single(b.Children);
        var d = Assert.Single(c.Children);

        Assert.Equal(new[] { "a", "b", "c" }, new[] { a.Segment, b.Segment, c.Segment });
        Assert.Equal(new[] { "a", "a/b", "a/b/c" }, new[] { a.Path, b.Path, c.Path });
        Assert.Equal(new[] { 0, 1, 2 }, new[] { a.Depth, b.Depth, c.Depth });
        Assert.True(a.IsFolder && b.IsFolder && c.IsFolder);

        Assert.False(d.IsFolder);
        Assert.Equal("d", d.Segment);
        Assert.Equal("a/b/c/d", d.Path);
        Assert.Equal(3, d.Depth);
    }

    [Fact]
    public void ARefThatIsAlsoAFolderPrefixGoesInsideThatFolder()
    {
        // `release` is a branch AND the prefix of `release/1.2`. One grouping, not a
        // leaf beside a folder of the same name.
        var roots = Build("release", "release/1.2");

        var folder = Assert.Single(roots);
        Assert.True(folder.IsFolder);
        Assert.Equal("release", folder.Path);
        Assert.Equal(2, folder.LeafCount);

        Assert.Equal(new[] { "release", "1.2" }, folder.Children.Select(n => n.Segment));
        Assert.All(folder.Children, n => Assert.False(n.IsFolder));
        Assert.All(folder.Children, n => Assert.Equal(1, n.Depth));
        // The leaf keeps its real name, which is what every action still acts on.
        Assert.Equal("release", folder.Children[0].Path);
    }

    [Fact]
    public void ThatHoldsWhenTheBareRefIsListedAfterTheOneBelowIt()
    {
        var roots = Build("release/1.2", "release");

        var folder = Assert.Single(roots);
        Assert.True(folder.IsFolder);
        Assert.Equal(new[] { "1.2", "release" }, folder.Children.Select(n => n.Segment));
    }

    [Fact]
    public void ItHoldsDeeperInTheTreeToo()
    {
        var roots = Build("a/b", "a/b/c");

        var a = Assert.Single(roots);
        var b = Assert.Single(a.Children);
        Assert.True(b.IsFolder);
        Assert.Equal("a/b", b.Path);
        Assert.Equal(new[] { "b", "c" }, b.Children.Select(n => n.Segment));
        Assert.Equal(new[] { "a/b", "a/b/c" }, b.Children.Select(n => n.Path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("//")]
    public void ANameThatIsNothingButSeparatorsLandsAtTheTopUnharmed(string name)
    {
        var roots = Build(name);

        var leaf = Assert.Single(roots);
        Assert.False(leaf.IsFolder);
        Assert.Equal(name, leaf.Path);
        Assert.Equal(0, leaf.Depth);
    }

    [Theory]
    [InlineData("/leading")]
    [InlineData("trailing/")]
    [InlineData("double//slash")]
    public void EmptySegmentsAreDroppedRatherThanBecomingBlankFolders(string name)
    {
        var roots = Build(name);

        // "/leading" and "trailing/" are one real segment; "double//slash" is two.
        Assert.All(Walk(roots), n => Assert.NotEqual("", n.Segment));
        // Whatever the shape, the ref keeps the name git gave it.
        Assert.Equal(name, Walk(roots).Single(n => !n.IsFolder).Path);
    }

    [Fact]
    public void AnEmptyInputBuildsAnEmptyTree()
    {
        Assert.Empty(Tree.Build<string>([], s => s));
    }

    [Fact]
    public void EveryFolderCountsTheRefsBelowIt()
    {
        var roots = Build("a/b/one", "a/b/two", "a/three", "four");

        var a = roots[0];
        Assert.Equal(3, a.LeafCount);
        Assert.Equal(2, a.Children.OfType<TreeNode<string>>().Single(n => n.IsFolder).LeafCount);
        Assert.Equal(1, roots[1].LeafCount);
    }

    [Fact]
    public void AFolderKnowsWhenEverythingBelowItIsFlagged()
    {
        // What lets the branch tree put a cloud on a folder of nothing but
        // remote-tracking refs, and leave it off a folder that mixes them.
        var roots = Tree.Build(
            new[] { "origin/a", "origin/b", "local/c", "local/origin-ish" },
            s => s,
            s => s.StartsWith("origin/", StringComparison.Ordinal));

        Assert.True(roots[0].AllFlagged);
        Assert.Equal(2, roots[0].FlaggedCount);
        Assert.False(roots[1].AllFlagged);
        Assert.Equal(0, roots[1].FlaggedCount);
    }

    [Fact]
    public void AFolderWithNoFlagAtAllIsNotFlagged()
    {
        var roots = Build("a/one", "a/two");
        Assert.False(roots[0].AllFlagged);
    }

    [Fact]
    public void RemoteTrackingRefsGroupUnderTheirRemote()
    {
        // Remote refs already carry the remote as their first segment, so the grouping
        // the branch tree wants falls straight out of the ordinary split.
        var roots = Build("feature/x", "origin/feature/x", "upstream/feature/y");

        Assert.Equal(new[] { "feature", "origin", "upstream" }, roots.Select(n => n.Segment));
        var originFeature = Assert.Single(roots[1].Children);
        Assert.True(originFeature.IsFolder);
        Assert.Equal("origin/feature", originFeature.Path);
        Assert.Equal("origin/feature/x", Assert.Single(originFeature.Children).Path);
    }

    [Fact]
    public void TheSameLeafNameInTwoFoldersStaysTwoRefs()
    {
        var roots = Build("feature/x", "origin/feature/x");

        Assert.Equal(2, Walk(roots).Count(n => !n.IsFolder));
        Assert.Equal(new[] { "feature/x", "origin/feature/x" },
            Walk(roots).Where(n => !n.IsFolder).Select(n => n.Path));
    }

    // ── Flattening ───────────────────────────────────────────────────────

    [Fact]
    public void FlatteningAFullyOpenTreeVisitsEveryNodeDepthFirst()
    {
        var rows = Tree.Flatten(Build("main", "feature/a", "feature/b"), _ => true);

        Assert.Equal(new[] { "main", "feature", "a", "b" }, rows.Select(n => n.Segment));
        Assert.Equal(new[] { 0, 0, 1, 1 }, rows.Select(n => n.Depth));
    }

    [Fact]
    public void ACollapsedFolderCostsOneRowAndHidesEverythingBelowIt()
    {
        // The whole point: a folder of a thousand refs is one row, not a thousand.
        var roots = Build("main", "feature/a", "feature/b", "feature/deep/c");

        var rows = Tree.Flatten(roots, n => n.Segment != "feature");

        Assert.Equal(new[] { "main", "feature" }, rows.Select(n => n.Segment));
    }

    [Fact]
    public void CollapsingOnlyTheInnerFolderKeepsItsSiblings()
    {
        var roots = Build("feature/a", "feature/deep/c", "feature/b");

        var rows = Tree.Flatten(roots, n => n.Path != "feature/deep");

        Assert.Equal(new[] { "feature", "a", "deep", "b" }, rows.Select(n => n.Segment));
    }

    [Fact]
    public void FlatteningATreeOfSlashlessNamesGivesTheFlatListBack()
    {
        // The property that lets one row template and one selection index serve both
        // presentations: with no separators anywhere the tree *is* the list.
        string[] names = ["main", "develop", "gh-pages"];

        var tree = Tree.Flatten(Tree.Build(names, s => s), _ => true);
        var flat = Tree.Flat(names, s => s);

        Assert.Equal(flat.Select(n => n.Path), tree.Select(n => n.Path));
        Assert.Equal(flat.Select(n => n.Depth), tree.Select(n => n.Depth));
    }

    [Fact]
    public void TheFlatRowsCarryTheWholeNameAsTheirLabel()
    {
        var rows = Tree.Flat(new[] { "feature/a/b" }, s => s);

        var row = Assert.Single(rows);
        Assert.False(row.IsFolder);
        Assert.Equal("feature/a/b", row.Segment);
        Assert.Equal("feature/a/b", row.Path);
        Assert.Equal(0, row.Depth);
    }

    [Fact]
    public void FoldersListsAFolderAndEveryFolderBelowIt()
    {
        var roots = Build("a/b/c", "a/d/e", "a/f");

        Assert.Equal(new[] { "a", "a/b", "a/d" }, Tree.Folders(roots[0]).Select(n => n.Path));
        // A leaf contributes nothing — there is nothing under it to expand.
        Assert.Empty(Tree.Folders(roots[0].Children.Single(n => !n.IsFolder)));
    }

    [Fact]
    public void AThousandRefsUnderOneRemoteCollapseToASingleRow()
    {
        var names = Enumerable.Range(0, 1000).Select(i => $"origin/feature/PROJ-{i}").ToArray();

        var roots = Tree.Build(names, s => s);
        var rows = Tree.Flatten(roots, _ => false);

        Assert.Equal(1000, roots[0].LeafCount);
        Assert.Equal("origin", Assert.Single(rows).Segment);
    }

    // ── Compaction ───────────────────────────────────────────────────────
    //
    // What makes a tree of deep paths worth having: the folders that don't divide
    // anything become one row with a joined label. Junction's Commit and Changes lists
    // are the callers — a commit four directories down was more chrome than content.

    [Fact]
    public void AChainOfOnlyChildFoldersBecomesOneRow()
    {
        var rows = Compacted("src/HultUI.Core/Elements/Element.cs");

        Assert.Equal(new[] { "src/HultUI.Core/Elements", "Element.cs" }, rows.Select(n => n.Segment));
        // The label is the only thing that joined: the row still *is* the deepest
        // folder, so expansion, reveal and every other path-keyed lookup still find it.
        Assert.Equal("src/HultUI.Core/Elements", rows[0].Path);
        Assert.Equal(new[] { 0, 1 }, rows.Select(n => n.Depth));
    }

    [Fact]
    public void AFolderThatBranchesKeepsItsOwnRow()
    {
        var rows = Compacted("src/Core/a.cs", "src/Text/b.cs");

        // `src` holds two folders, so it divides something and stays; each child was a
        // chain of its own and joined.
        Assert.Equal(new[] { "src", "Core", "a.cs", "Text", "b.cs" }, rows.Select(n => n.Segment));
        Assert.Equal(new[] { 0, 1, 2, 1, 2 }, rows.Select(n => n.Depth));
    }

    [Fact]
    public void AFolderHoldingOneFileIsAGroupingAndSurvives()
    {
        // The walk stops at a leaf child. `docs` holding one file is a folder with a
        // file in it — joining the two would say the file *is* the folder.
        var rows = Compacted("docs/README.md");

        Assert.Equal(new[] { "docs", "README.md" }, rows.Select(n => n.Segment));
    }

    [Fact]
    public void CompactionOnlyReachesTheFoldersAboveTheBranch()
    {
        var rows = Compacted("a/b/c/one.cs", "a/b/d/two.cs");

        // `a/b` runs together down to where it splits; below the split each arm joins
        // on its own. Nothing above or below crosses the branch.
        Assert.Equal(new[] { "a/b", "c", "one.cs", "d", "two.cs" }, rows.Select(n => n.Segment));
        Assert.Equal(new[] { "a/b", "a/b/c", "a/b/c/one.cs", "a/b/d", "a/b/d/two.cs" },
            rows.Select(n => n.Path));
    }

    [Fact]
    public void ACompactedFolderKeepsTheCountsOfTheChainItReplaced()
    {
        var roots = Tree.Compact(Tree.Build(
            new[] { "x/y/z/1", "x/y/z/2", "x/y/z/3" }, s => s, flag: s => s.EndsWith('1')));

        var folder = Assert.Single(roots);
        Assert.Equal(3, folder.LeafCount);
        Assert.Equal(1, folder.FlaggedCount);
    }

    [Fact]
    public void NamesWithNoFoldersAtAllAreUntouched()
    {
        var rows = Compacted("a.cs", "b.cs");

        Assert.Equal(new[] { "a.cs", "b.cs" }, rows.Select(n => n.Segment));
        Assert.All(rows, n => Assert.Equal(0, n.Depth));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>The compacted tree, fully expanded — what a caller's rows would be.</summary>
    private static List<TreeNode<string>> Compacted(params string[] names) =>
        Tree.Flatten(Tree.Compact(Tree.Build(names, s => s)), _ => true);

    private static List<TreeNode<string>> Build(params string[] names) =>
        Tree.Build(names, s => s);

    private static IEnumerable<TreeNode<string>> Walk(IEnumerable<TreeNode<string>> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Walk(node.Children)) yield return child;
        }
    }
}

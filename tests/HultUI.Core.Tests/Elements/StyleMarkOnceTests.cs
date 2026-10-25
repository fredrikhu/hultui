using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// Marking an element for style walks everything under it once, and then not again until the
/// style pass: <see cref="DirtyFlags.StyleSubtree"/> says the walk has been there. A rebuild
/// patches top-down and every patch marks style, so without it each level walked its whole
/// subtree again, O(n × depth) a rebuild. The claim must never be wrong: an element under a
/// claim that is not marked would keep its old style.
/// </summary>
[Collection("Reactive")]
public class StyleMarkOnceTests
{
    /// <summary>Says whether its patch found it already marked with everything under it.</summary>
    private sealed class ProbeBox : BoxElement
    {
        public static int Patched, FoundMarked;

        internal override void PatchFrom(Element source)
        {
            Patched++;
            if ((DirtyFlags & DirtyFlags.StyleSubtree) != 0) FoundMarked++;
            base.PatchFrom(source);
        }
    }

    private static ProbeBox Tree(int depth)
    {
        var box = new ProbeBox();
        if (depth > 0) box.ReplaceChildren(Tree(depth - 1), Tree(depth - 1), Text.New("leaf"));
        return box;
    }

    // What the window does at the end of a frame.
    private static void ClearAll(Element e)
    {
        e.DirtyFlags = DirtyFlags.None;
        foreach (var c in e.Children) ClearAll(c);
    }

    private static IEnumerable<Element> All(Element e) =>
        e.Children.SelectMany(All).Prepend(e);

    [Fact]
    public void ARebuild_MarksEachElementItPatches_AndWalksNoSubtree()
    {
        var root = Box.New();
        var existing = Tree(depth: 7);
        root.AddChild(existing);
        ClearAll(root);
        int elements = All(existing).Count(e => e is ProbeBox);

        ProbeBox.Patched = ProbeBox.FoundMarked = 0;
        TreeDiffer.Reconcile(root, existing, Tree(depth: 7));

        // Each patch marks the element it patched, and its ancestors so the style pass reaches it
        // (a walk that stops at the first one marked): all 382 are marked, each by its own patch,
        // and none claims a marked subtree. Until plan 17 round 4 the top patch walked all 382
        // and the 254 box patches below it found their subtrees walked already; the style pass
        // then resolved every element under a patched one, patched or not.
        Assert.Equal(255, elements);
        Assert.Equal(elements, ProbeBox.Patched);
        Assert.Equal(0, ProbeBox.FoundMarked);
        Assert.All(All(existing), e => Assert.Equal(DirtyFlags.Style,
            e.DirtyFlags & (DirtyFlags.Style | DirtyFlags.StyleSubtree)));
    }

    [Fact]
    public void ASecondMark_OfAMarkedSubtree_StopsWhereItStarts()
    {
        var root = Tree(depth: 4);
        ClearAll(root);
        root.MarkStyleDirty();

        // Clearing a leaf behind the framework's back shows whether a later walk comes down
        // here again. Nothing under the claim can be unmarked in a real tree.
        var leaf = All(root).Last();
        var middle = root.Children[0].Children[1];
        leaf.DirtyFlags = DirtyFlags.None;
        root.MarkStyleDirty();
        middle.MarkStyleDirty();

        Assert.Equal(DirtyFlags.None, leaf.DirtyFlags);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("insert")]
    [InlineData("replace")]
    public void AChildMovedUnderAMarkedParent_IsMarkedByTheNextMarkAboveIt(string how)
    {
        var a = Box.New().FontSize(20f);
        var root = Box.New().Children(a, Box.New());
        var c = Box.New().Children(Text.New("moved"));
        var elsewhere = Box.New().FontSize(10f).Children(c);
        StyleResolver.Resolve(root, Theme.Light);
        StyleResolver.Resolve(elsewhere, Theme.Light);
        ClearAll(root);
        ClearAll(elsewhere);
        Assert.Equal(10f, c.Children[0].ResolvedInherited.FontSize);

        // A mark at the root marks it with everything under it; then C, unmarked, is moved in
        // under A. Under another parent it is marked for style itself (plan 17 round 4: a patch
        // no longer marks the subtree it lands in), not its subtree; A's claim is over, and so is
        // the root's, so the next mark from either comes down to C's children.
        root.MarkStyleDirty();
        Move(a, c, how);
        Assert.Equal(DirtyFlags.Style, c.DirtyFlags & (DirtyFlags.Style | DirtyFlags.StyleSubtree));
        Assert.Equal(DirtyFlags.None, a.DirtyFlags & DirtyFlags.StyleSubtree);
        Assert.Equal(DirtyFlags.None, root.DirtyFlags & DirtyFlags.StyleSubtree);
        Assert.NotEqual(DirtyFlags.None, root.Children[1].DirtyFlags & DirtyFlags.StyleSubtree);

        root.MarkStyleDirty();
        StyleResolver.Resolve(root, Theme.Light);

        Assert.Equal(20f, c.Children[0].ResolvedInherited.FontSize);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("insert")]
    [InlineData("replace")]
    public void AChildMovedUnderAMarkedParent_IsMarkedByThatParentsNextPatch(string how)
    {
        var a = Box.New().FontSize(20f);
        var root = Box.New().Children(a);
        var c = Box.New().Children(Text.New("moved"));
        var elsewhere = Box.New().FontSize(10f).Children(c);
        StyleResolver.Resolve(root, Theme.Light);
        StyleResolver.Resolve(elsewhere, Theme.Light);
        ClearAll(root);
        ClearAll(elsewhere);

        a.MarkStyleDirty();
        Move(a, c, how);
        a.MarkStyleDirty();
        StyleResolver.Resolve(root, Theme.Light);

        Assert.Equal(20f, c.Children[0].ResolvedInherited.FontSize);
    }

    private static void Move(Element to, Element child, string how)
    {
        switch (how)
        {
            case "add": to.AddChild(child); break;
            case "insert": to.InsertChild(0, child); break;
            default: to.ReplaceChildren([.. to.Children, child]); break;
        }
    }

    [Fact]
    public void AFreshChild_AddedUnderAMarkedParent_IsResolved_AndKeepsTheClaim()
    {
        var a = Box.New().FontSize(20f);
        var root = Box.New().Children(a);
        StyleResolver.Resolve(root, Theme.Light);
        ClearAll(root);

        a.MarkStyleDirty();
        var added = Text.New("new");
        var inserted = Text.New("new");
        a.AddChild(added);
        a.InsertChild(0, inserted);
        // Mounting marks a new child with everything under it, so the claim stands.
        Assert.NotEqual(DirtyFlags.None, a.DirtyFlags & DirtyFlags.StyleSubtree);
        StyleResolver.Resolve(root, Theme.Light);

        Assert.Equal(20f, added.ResolvedInherited.FontSize);
        Assert.Equal(20f, inserted.ResolvedInherited.FontSize);
    }

    [Fact]
    public void ASubtreeMovedFromAMarkedParentToAnUnmarkedOne_ResolvesUnderItsNewParent()
    {
        var c = Box.New().Children(Text.New("moved"));
        var p = Box.New().FontSize(10f).Children(c);
        var q = Box.New().FontSize(30f);
        var root = Box.New().Children(p, q);
        StyleResolver.Resolve(root, Theme.Light);
        ClearAll(root);

        p.MarkStyleDirty();
        q.AddChild(c);
        q.MarkStyleDirty();
        StyleResolver.Resolve(root, Theme.Light);

        Assert.Equal(30f, c.Children[0].ResolvedInherited.FontSize);
    }

    [Fact]
    public void AThemeSwitch_AfterAPatchMarkedPartOfTheTree_ReachesEveryElement()
    {
        var root = Tree(depth: 4);
        StyleResolver.Resolve(root, Theme.Light);
        ClearAll(root);

        // A rebuild patched one branch (marked with its subtree); then the theme changes, which
        // marks the root. The walk stops at the marked branch, which is marked already.
        root.Children[1].MarkStyleDirty();
        root.MarkStyleDirty();
        StyleResolver.Resolve(root, Theme.Dark);

        Assert.All(All(root), e => Assert.Equal(Theme.Dark.TextColor, e.ResolvedInherited.TextColor));
    }

    [Fact]
    public void AKeyedReorder_UnderAParentWhoseFontChanged_ResolvesEveryRow()
    {
        static BoxElement List(float size, bool reversed)
        {
            var rows = Enumerable.Range(0, 20).Select(i =>
            {
                var row = Box.New().Children(Text.New($"row {i}"));
                row.Key = $"r{i}";
                return (Element)row;
            });
            return Box.New().FontSize(size).Children((reversed ? rows.Reverse() : rows).ToArray());
        }

        var existing = List(12f, reversed: false);
        var root = Box.New().Children(existing);
        StyleResolver.Resolve(root, Theme.Light);
        ClearAll(root);

        TreeDiffer.Reconcile(root, existing, List(18f, reversed: true));
        StyleResolver.Resolve(root, Theme.Light);

        Assert.Equal("row 19", ((TextElement)existing.Children[0].Children[0]).Content);
        Assert.All(existing.Children, row => Assert.Equal(18f, row.Children[0].ResolvedInherited.FontSize));
    }

    private sealed class Label : Component
    {
        public override Element Build() => Text.New("kept");
    }

    /// <summary>Hands the same child host to every build: the builder moves it into the new
    /// tree, and the reconciler moves it back under the live one, already patched and marked.</summary>
    private sealed class Panel : Component
    {
        public readonly Signal<float> Size = new(14f);
        public readonly ComponentHost Kept = new Label().Mount();

        public override Element Build() =>
            Box.New().FontSize(Size.Value).Children(Box.New().Children(Kept));
    }

    [Fact]
    public void AThemeSwitch_AfterARebuildMovedAPersistentChildComponent_ReachesIt()
    {
        var panel = new Panel();
        var root = Box.New().Children(panel.Mount());
        StyleResolver.Resolve(root, Theme.Light);
        ClearAll(root);

        panel.Size.Value = 18f;   // rebuilds now: the kept host is moved out and back
        root.MarkStyleDirty();     // the theme changes in the same frame
        StyleResolver.Resolve(root, Theme.Dark);

        var text = panel.Kept.Children[0];
        Assert.Equal(Theme.Dark.TextColor, text.ResolvedInherited.TextColor);
        Assert.Equal(18f, text.ResolvedInherited.FontSize);
    }

    [Fact]
    public void ADetachedSubtree_MarkedWhileOut_IsResolvedWhenItComesBack()
    {
        var c = Box.New().Children(Text.New("out"));
        var p = Box.New().FontSize(10f).Children(c);
        var root = Box.New().Children(p);
        StyleResolver.Resolve(root, Theme.Light);
        ClearAll(root);

        p.RemoveChild(c);
        c.MarkStyleDirty();
        p.FontSize(24f).MarkStyleDirty();
        p.AddChild(c);
        StyleResolver.Resolve(root, Theme.Light);

        Assert.Equal(24f, c.Children[0].ResolvedInherited.FontSize);
    }
}

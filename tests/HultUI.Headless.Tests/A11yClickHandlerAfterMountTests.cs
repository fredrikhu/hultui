using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A box with a click handler and no role of its own is a button to a reader. A click handler added to
/// a box already in a window, or the last one a rebuild drops, changes that with nothing marked: the box
/// draws the same. It was told only when something else walked or built the tree — in
/// MiddleClickAutoScrollTests a scroll's layout refresh five frames later, and before plan 17 round 13's
/// hover rule the frame a pointer move drew, which walked nothing, hid it from the staleness audit —
/// and never when nothing did. Now the window walks the tree in place in the next frame, which it asks for and which draws
/// nothing (plan 17 round 13 step 5 (c)).
/// </summary>
public class A11yClickHandlerAfterMountTests
{
    /// <summary>Keeps the applied tree and what each diff told, as lines read before the call returns:
    /// a walk in place lends its diff.</summary>
    private sealed class Told : IAccessibilityBridge
    {
        public readonly List<string> Lines = [];
        public AccessibilityNode? Tree;
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
        {
            Tree = root;
            foreach (var a in diff.Added) Lines.Add($"+ {a.Node.Role} {a.Node.Name}");
            foreach (var r in diff.Removed) Lines.Add($"- {r.Node.Role} {r.Node.Name}");
            foreach (var c in diff.PropertyChanged) Lines.Add($"{c.PropertyName} {c.Node.Role} {c.Node.Name}");
        }
        public void NotifyFocusChanged(AccessibilityNode? focused) { }
    }

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    /// <summary>The applied tree is the one a build makes now.</summary>
    private static void AssertCurrent(Told told, Element root) =>
        Assert.True(AccessibilityTreeDiffer.Diff(told.Tree, AccessibilityTreeBuilder.Build(root)).IsEmpty,
            "the applied tree is not what a build makes");

    private sealed class Toolbar : Component
    {
        public bool Clickable;
        public readonly HultUI.Core.Reactivity.Ref<BoxElement> Save = new();

        public override Element Build()
        {
            var save = Box.New().Height(Dimension.Px(40)).Children(TextB.New("Save")).WithRef(Save);
            if (Clickable) save.OnClick(_ => { });
            return Flex.Column().Children(save, TextB.New("status"));
        }

        public void Rebuild() => MarkDirty();
    }

    [Fact]
    public void AClickHandlerAddedToAMountedBox_IsToldByTheNextFrame_WithNoPointerMove()
    {
        using var session = HeadlessSession.Create(300, 200);
        var told = session.Window.InstallAccessibilityBridge(new Told());
        var save = Box.New().Height(Dimension.Px(40)).Children(TextB.New("Save"));
        var root = Flex.Column().Children(save, TextB.New("status"));
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        Assert.Null(Find(told.Tree!, save.Id));
        told.Lines.Clear();
        var window = session.Window.Window;
        Assert.False(window.NeedsRender);
        int trees = window.AccessibilityTreesBuilt, walks = window.AccessibilityStructureRefreshesInPlace;

        save.OnClick(_ => { });
        Assert.True(window.NeedsRender, "no frame asked for");
        Assert.False(session.PumpFrame(), "a frame drawn for it");

        var node = Find(told.Tree!, save.Id);
        Assert.NotNull(node);
        Assert.Equal(AccessibilityRole.Button, node!.Role);
        Assert.Equal("Save", node.Name);
        Assert.Contains("+ Button Save", told.Lines);
        Assert.Equal(trees, window.AccessibilityTreesBuilt);
        Assert.Equal(walks + 1, window.AccessibilityStructureRefreshesInPlace);
        AssertCurrent(told, root);
        Assert.False(window.NeedsRender);
    }

    [Fact]
    public void AClickHandlerARebuildAddsOrDrops_IsToldInItsFrame()
    {
        using var session = HeadlessSession.Create(300, 200);
        var told = session.Window.InstallAccessibilityBridge(new Told());
        var toolbar = new Toolbar();
        var root = toolbar.Mount();
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        var save = toolbar.Save.Current!;
        Assert.Null(Find(told.Tree!, save.Id));

        toolbar.Clickable = true;
        toolbar.Rebuild();
        session.PumpFrame();
        Assert.Same(save, toolbar.Save.Current);   // patched, not made again
        Assert.Equal(AccessibilityRole.Button, Find(told.Tree!, save.Id)?.Role);
        AssertCurrent(told, root);

        told.Lines.Clear();
        toolbar.Clickable = false;
        toolbar.Rebuild();
        session.PumpFrame();
        Assert.Same(save, toolbar.Save.Current);
        Assert.Null(Find(told.Tree!, save.Id));
        Assert.Contains("- Button Save", told.Lines);
        AssertCurrent(told, root);
    }

    [Fact]
    public void AClickHandlerThatChangesNothingAReaderIsTold_AsksForNoFrame()
    {
        using var session = HeadlessSession.Create(300, 200);
        var told = session.Window.InstallAccessibilityBridge(new Told());
        var card = Box.New().Height(Dimension.Px(40)).Children(TextB.New("Card")).OnClick(_ => { });
        var button = Button.New("Go");
        var row = Box.New().Height(Dimension.Px(40)).AccessibleRole(AccessibilityRole.ListItem).AccessibleName("Row");
        var root = Flex.Column().Children(card, button, row);
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        var window = session.Window.Window;
        Assert.False(window.NeedsRender);

        card.OnClick(_ => { });          // a second: a button already
        button.OnClick(_ => { });        // a button whatever listens
        row.OnClick(_ => { });           // its own role
        card.On<PointerDownEvent>(_ => { });

        Assert.False(window.NeedsRender, "a frame asked for");
        AssertCurrent(told, root);
    }

    [Fact]
    public void WithNoReader_AClickHandlerAddedToAMountedBox_AsksForNoFrame()
    {
        using var session = HeadlessSession.Create(300, 200);
        var save = Box.New().Height(Dimension.Px(40)).Children(TextB.New("Save"));
        session.Window.SetRoot(Flex.Column().Children(save));
        session.PumpFrames(2);

        save.OnClick(_ => { });

        Assert.False(session.Window.Window.NeedsRender);
    }
}

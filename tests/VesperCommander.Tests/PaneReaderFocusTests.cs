using System.IO;
using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The pane holds the keyboard focus, and what holds it has to be a node for a screen reader to be
/// told of it. The pane was a box with no role or name, which has none, so every focus move told a
/// reader "focus on nothing". It is a group named after its side and its folder.
/// </summary>
public class PaneReaderFocusTests
{
    private static DiffViewTests.Fixture Open()
    {
        var t = new DiffViewTests.Fixture();
        Directory.CreateDirectory(Path.Combine(t.Left, "docs"));
        File.WriteAllText(Path.Combine(t.Left, "docs", "inner.txt"), "x");
        for (int i = 0; i < 5; i++)
        {
            t.WriteLeft($"f{i}.txt", "l");
            t.WriteRight($"g{i}.txt", "r");
        }
        t.Open();
        t.Session.PumpFrames(2);
        return t;
    }

    private static AccessibilityNode? Focused(AccessibilityNode node)
    {
        if (node.IsFocused) return node;
        foreach (var child in node.Children)
            if (Focused(child) is { } found) return found;
        return null;
    }

    /// <summary>The focused pane is a group named after its folder, and the name follows it there.</summary>
    [Fact]
    public void TheFocusedPaneIsAGroupNamedAfterItsFolder()
    {
        using var t = Open();

        var pane = Focused(t.Session.Window.AccessibilityTree);
        Assert.NotNull(pane);
        Assert.Equal(AccessibilityRole.Group, pane!.Role);
        Assert.Equal("Left pane, left", pane.Name);
        // The pane's content is under it: its list, with the cursor's item selected.
        Assert.Contains(pane.Children, c => c.Role == AccessibilityRole.List);

        t.PutCursorOn('L', "docs");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "inner.txt"), System.TimeSpan.FromSeconds(10));
        t.Session.PumpFrames(2);
        Assert.Equal("Left pane, docs", Focused(t.Session.Window.AccessibilityTree)?.Name);
    }

    /// <summary>Tab makes the other pane the active one, and the keyboard focus goes with it: it
    /// stayed on the pane Tab left, so a reader was told nothing. Now it is told the pane it reached,
    /// then that pane's cursor item, selected, and no tree is built for either.</summary>
    [Fact]
    public void TabTakesTheFocusToThePaneItMakesActive_AndAReaderIsToldThePaneThenItsCursor()
    {
        using var t = Open();
        var bridge = t.Session.Window.InstallAccessibilityBridge();
        t.Session.PumpFrames(3);
        var left = t.Field<HultUI.Core.Reactivity.Ref<HultUI.Core.Elements.BoxElement>>("_paneRefL").Current;
        var right = t.Field<HultUI.Core.Reactivity.Ref<HultUI.Core.Elements.BoxElement>>("_paneRefR").Current;
        Assert.Same(left, t.Session.Window.Window.FocusedElement);
        int trees = t.Session.Window.AccessibilityTreesBuilt;

        bridge.ClearDiffs();
        t.Press(KeyCode.Tab);
        t.Session.PumpFrames(2);
        Assert.Same(right, t.Session.Window.Window.FocusedElement);
        Assert.Equal(AccessibilityRole.Group, bridge.FocusedNode?.Role);
        Assert.Equal("Right pane, right", bridge.FocusedNode?.Name);
        // The cursor shows in the active pane only: the right pane's item is told selected.
        Assert.Contains(bridge.Diffs.SelectMany(d => d.PropertyChanged), c =>
            c.PropertyName == AccessibilityProperties.IsSelected && c.Node.IsSelected == true && c.Node.Name == "Parent folder");

        t.Press(KeyCode.Tab);
        t.Session.PumpFrames(2);
        Assert.Same(left, t.Session.Window.Window.FocusedElement);
        Assert.Equal("Left pane, left", bridge.FocusedNode?.Name);
        Assert.Equal(trees, t.Session.Window.AccessibilityTreesBuilt);
    }

    /// <summary>In Brief and Thumbnails a cursor key moves the ring with no build, and a reader is
    /// told the item it reached, by its name, selected, and the one it left not, with no tree: each
    /// cell and tile is an item, and the cursor's is selected, which the window marks on its node (plan
    /// 17 round 9, step 15b). A reader was told nothing: a cell was no node, and a tile was in a row
    /// that was one item named by all its files.</summary>
    [Theory]
    [InlineData("brief")]
    [InlineData("thumbnails")]
    public void ACursorKeyInBriefOrThumbnails_TellsAReaderTheItemItReached_AndBuildsNoTree(string view)
    {
        using var t = Open();
        var bridge = t.Session.Window.InstallAccessibilityBridge();
        t.Session.PumpFrames(3);
        t.Press(view == "brief" ? KeyCode.F2 : KeyCode.F3, KeyModifiers.Ctrl);
        t.Session.PumpFrames(3);
        t.PutCursorOn('L', "f1.txt");
        t.Session.PumpFrames(3);
        var tree = t.Session.Window.AccessibilityTree;
        Assert.Equal(["f1.txt"], Selected(tree).Select(n => n.Name!).ToArray());
        Assert.All(Selected(tree), n => Assert.Equal(AccessibilityRole.ListItem, n.Role));
        int trees = t.Session.Window.AccessibilityTreesBuilt, builds = t.Field<int>("_builds");

        bridge.ClearDiffs();
        t.Press(view == "brief" ? KeyCode.Down : KeyCode.Right);
        t.Session.PumpFrames(2);
        var told = bridge.Diffs.SelectMany(d => d.PropertyChanged)
            .Where(c => c.PropertyName == AccessibilityProperties.IsSelected)
            .Select(c => $"{c.Node.Name}={c.Node.IsSelected}").ToArray();
        Assert.Equal(["f1.txt=False", "f2.txt=True"], told);
        Assert.Equal(["f2.txt"], Selected(t.Session.Window.AccessibilityTree).Select(n => n.Name!).ToArray());
        Assert.Equal(builds, t.Field<int>("_builds"));   // the ring moved with no build
        Assert.Equal(trees, t.Session.Window.AccessibilityTreesBuilt);
    }

    private static IEnumerable<AccessibilityNode> Selected(AccessibilityNode node) =>
        node.Children.SelectMany(Selected).Prepend(node).Where(n => n.IsSelected == true);
}

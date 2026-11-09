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
}

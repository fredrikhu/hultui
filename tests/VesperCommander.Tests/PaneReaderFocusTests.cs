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
}

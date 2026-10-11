using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The comparison, the viewer and the thumbnail grid name their items for a screen reader.
/// None set a label, so the framework's fallback, the item's <c>ToString()</c>, was read out:
/// "DiffRow { Line = Line { Op = Change, LeftNo = 2, … } }" for a line of the comparison, and
/// "0", "1", "2" for the viewer's lines and the grid's rows of tiles. The labels are also made
/// once and kept, because the tree asks for every item's label each time it is built.
/// </summary>
public class ItemLabelTests
{
    [Fact]
    public void TheComparisonNamesEachLineByWhatHappenedToIt()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "one\ntwo\nthree\n\n");
        t.WriteRight("a.txt", "one\nTWO\nthree\n\nfour\n");
        t.Open();
        t.OpenDiffOn("a.txt");

        var names = ItemNames(t.Session.Window.AccessibilityTree);
        Assert.Contains("line 1: one", names);
        Assert.Contains("changed, left line 2: two; right line 2: TWO", names);
        Assert.Contains("line 4: blank", names);
        Assert.Contains("added, line 5: four", names);
        Assert.DoesNotContain(names, n => n.StartsWith("DiffRow", System.StringComparison.Ordinal));

        // Made once: the next tree reads the same strings rather than new ones.
        var again = ItemNames(t.Session.Window.AccessibilityTree);
        Assert.Same(names.First(n => n.StartsWith("changed", System.StringComparison.Ordinal)),
            again.First(n => n.StartsWith("changed", System.StringComparison.Ordinal)));
    }

    [Fact]
    public void TheViewerNamesEachLineByItsNumberAndText()
    {
        using var t = new ViewerTests.Fixture();
        t.Write("a.txt", "alpha\n\nbeta\n");
        t.Open();
        t.ViewFile("a.txt");

        var names = ItemNames(t.Session.Window.AccessibilityTree);
        Assert.Contains("line 1: alpha", names);
        Assert.Contains("line 2: blank", names);
        Assert.Contains("line 3: beta", names);
        Assert.DoesNotContain("0", names);

        var again = ItemNames(t.Session.Window.AccessibilityTree);
        Assert.Same(names.First(n => n.StartsWith("line 1:", System.StringComparison.Ordinal)),
            again.First(n => n.StartsWith("line 1:", System.StringComparison.Ordinal)));
    }

    [Fact]
    public void AThumbnailRowIsNamedByTheFilesOnIt()
    {
        using var t = new DiffViewTests.Fixture();
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(t.Left, "docs"));
        for (int i = 0; i < 12; i++) t.WriteLeft($"f{i:00}.txt", "x");
        t.Open();
        t.Press(KeyCode.F3, KeyModifiers.Ctrl);   // view: Thumbnails
        t.Session.PumpFrames(2);

        var names = ItemNames(t.Session.Window.AccessibilityTree);
        Assert.Contains(names, n => n.StartsWith("Parent folder; docs, folder; f00.txt; f01.txt", System.StringComparison.Ordinal));
        Assert.DoesNotContain("0", names);
        Assert.DoesNotContain("1", names);
    }

    private static List<string> ItemNames(AccessibilityNode root) =>
        Walk(root).Where(n => n.Role == AccessibilityRole.ListItem).Select(n => n.Name ?? "").ToList();

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node) =>
        node.Children.SelectMany(Walk).Prepend(node);
}

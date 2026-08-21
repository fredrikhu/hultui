using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// A tab is named after its folder, and a folder can be named anything. The label truncates
/// rather than letting the tab take whatever room the name wants: uncapped, a long name laid
/// out several times wider than the tab and pushed the strip taller than the pane's other one.
///
/// <para>A width cap alone would not do it — wrapping is decided at measure time, before any
/// cap applies — so the one-line clamp and the ellipsis are asked for as well. That is what
/// these tests are pinned to: the cap, and the strip staying the height a tab row should be.</para>
/// </summary>
public class TabLabelTests
{
    // Long enough to matter: uncapped, a name this size lays out ~450 wide in a ~600 pane
    // and takes the tab strip from 31 to 46 units tall.
    private const string LongName =
        "a-really-quite-remarkably-long-folder-name-that-nobody-would-ever-type-twice-"
        + "and-yet-here-it-is-sitting-in-a-tab-asking-for-more-room-than-the-pane-has";

    /// <summary>The width one tab label is allowed, read off the app so this moves with it.</summary>
    private static float MaxLabelWidth => (float)typeof(CommanderApp)
        .GetField("TabLabelMaxWidth", BindingFlags.NonPublic | BindingFlags.Static)!
        .GetRawConstantValue()!;

    private static Element Pane(DiffViewTests.Fixture t, char side) =>
        t.Field<Ref<BoxElement>>(side == 'L' ? "_paneRefL" : "_paneRefR").Current
        ?? throw new Xunit.Sdk.XunitException($"the {side} pane is not mounted");

    /// <summary>The tab strip is the pane's first child.</summary>
    private static Element Strip(DiffViewTests.Fixture t, char side) => Pane(t, side).Children[0];

    private static TextElement TabLabel(DiffViewTests.Fixture t, char side) =>
        Labels(Strip(t, side).Children[0]).FirstOrDefault()
        ?? throw new Xunit.Sdk.XunitException($"the {side} tab has no label");

    private static IEnumerable<TextElement> Labels(Element root)
    {
        if (root is TextElement text) yield return text;
        foreach (var c in root.Children)
            foreach (var d in Labels(c)) yield return d;
    }

    /// <summary>A fixture whose left pane sits in a folder with a very long name. The right
    /// pane keeps its ordinary one ("right"), so it is the reference for an untroubled tab.</summary>
    private static DiffViewTests.Fixture WithLongFolder(out string folder)
    {
        var t = new DiffViewTests.Fixture();
        folder = Path.Combine(t.Left, LongName);
        Directory.CreateDirectory(folder);
        t.Open();
        var target = folder;
        t.Invoke("NavigateTo", 'L', target, null, false);
        t.Session.PumpUntil(() => t.Dir('L') == target, TimeSpan.FromSeconds(10));
        // The navigation's own frame builds the new tree; the layout that measures it lands on
        // a later one. Reading geometry off an unlaid-out subtree reports zeroes, which would
        // make every assertion here vacuously true.
        t.Session.PumpFrames(3);
        return t;
    }

    [Fact]
    public void ALongTabNameIsCappedToTheLabelWidth()
    {
        using var t = WithLongFolder(out _);
        var label = TabLabel(t, 'L');

        Assert.True(label.Layout.Width > 0, "the tab strip has not been laid out");
        Assert.True(label.Layout.Width <= MaxLabelWidth + 1f,
            $"the tab label laid out {label.Layout.Width} wide, past the {MaxLabelWidth} cap");
        // And it is the long name being cut down, not some other label picked up by accident.
        Assert.StartsWith(label.Content[..4], LongName, StringComparison.Ordinal);
    }

    /// <summary>The visible symptom: a tab that takes more room than it is given makes the
    /// whole strip taller, and the strip is the same row in both panes.</summary>
    [Fact]
    public void TheTabStripIsNoTallerThanThePaneWithAnOrdinaryName()
    {
        using var t = WithLongFolder(out _);

        Assert.Equal(Strip(t, 'R').Layout.Height, Strip(t, 'L').Layout.Height, 1);
    }

    /// <summary>The strip still has room for what sits beside the tabs — the "+" button and
    /// the history arrows, which an unbounded name would shoulder off the end.</summary>
    [Fact]
    public void TheHistoryClusterStaysInsideThePane()
    {
        using var t = WithLongFolder(out _);

        var back = t.Session.Window.FindByAutomationId("hist-back-L");
        Assert.NotNull(back);
        var box = back!.Element.Layout.AbsoluteBorderBox;
        Assert.True(box.Width > 0, "the history cluster has not been laid out");
        Assert.True(box.Right <= Pane(t, 'L').Layout.AbsoluteBorderBox.Right + 1f,
            "the history cluster was pushed outside the pane by the tab strip");
    }

    /// <summary>A truncated tab is unreadable without its tooltip, so it carries the whole
    /// path — the one place a shortened tab can still say what it is.</summary>
    [Fact]
    public void ATabCarriesItsFullPathAsTheTooltip()
    {
        using var t = WithLongFolder(out var folder);

        Assert.Equal(folder, Strip(t, 'L').Children[0].TooltipText);
    }
}

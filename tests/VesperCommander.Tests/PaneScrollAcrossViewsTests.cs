using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The viewer, the diff and sync replace the panes with the view. When it closed, the panes
/// were built again from nothing and their lists opened at the top — so a file viewed from
/// row 100 of a long folder came back with row 1 on screen and the cursor somewhere below
/// the fold. The offset is noted on the way in and put back on the way out.
/// </summary>
public class PaneScrollAcrossViewsTests
{
    private static VirtualListElement<CommanderApp.PaneRowItem> LeftList(ViewerTests.Fixture t) =>
        t.Field<Ref<VirtualListElement<CommanderApp.PaneRowItem>>>("_vlistL").Current
        ?? throw new Xunit.Sdk.XunitException("the left pane has no list");

    /// <summary>A folder long enough to scroll, scrolled well down. Returns the offset the
    /// list actually settled on (it clamps to its content).</summary>
    private static float ScrollDown(ViewerTests.Fixture t)
    {
        LeftList(t).ScrollOffsetY = 1200f;
        t.Session.PumpFrame();
        float before = LeftList(t).ScrollOffsetY;
        Assert.True(before > 500f, $"the list did not scroll (offset {before})");
        return before;
    }

    private static void Populate(ViewerTests.Fixture t)
    {
        for (int i = 1; i <= 120; i++) t.Write($"a{i:D3}.txt", "x");
    }

    [Fact]
    public void ClosingTheViewerPutsThePaneBackWhereItWas()
    {
        using var t = new ViewerTests.Fixture();
        Populate(t);
        t.Open();
        float before = ScrollDown(t);

        t.ViewFile("a100.txt");
        Assert.True(t.ViewerOpen);
        t.Press(KeyCode.Escape);
        t.Session.PumpFrame();
        t.Session.PumpFrame();

        Assert.False(t.ViewerOpen);
        Assert.Equal(before, LeftList(t).ScrollOffsetY);
    }

    [Fact]
    public void ClosingTheDiffPutsThePaneBackWhereItWas()
    {
        using var t = new ViewerTests.Fixture();
        Populate(t);
        t.WriteOther("a100.txt", "y");
        t.Open();
        float before = ScrollDown(t);

        t.DiffFile("a100.txt");
        t.Press(KeyCode.Escape);
        t.Session.PumpFrame();
        t.Session.PumpFrame();

        Assert.False(t.Field<bool>("_diffOpen"));
        Assert.Equal(before, LeftList(t).ScrollOffsetY);
    }

    /// <summary>A pane that was at the top stays at the top — the restore must not invent
    /// an offset, and a second open must not put back a stale one.</summary>
    [Fact]
    public void APaneAtTheTopStaysAtTheTop()
    {
        using var t = new ViewerTests.Fixture();
        Populate(t);
        t.Open();
        ScrollDown(t);
        t.ViewFile("a100.txt");
        t.Press(KeyCode.Escape);
        t.Session.PumpFrame();
        t.Session.PumpFrame();

        LeftList(t).ScrollOffsetY = 0f;
        t.Session.PumpFrame();
        t.ViewFile("a001.txt");
        t.Press(KeyCode.Escape);
        t.Session.PumpFrame();
        t.Session.PumpFrame();

        Assert.Equal(0f, LeftList(t).ScrollOffsetY);
    }
}

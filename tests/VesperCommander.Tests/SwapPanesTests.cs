using System.IO;
using System.Linq;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Ctrl+U exchanges the two panes. The report to check was "including history, not just
/// paths" — so these drive the real chord and then ask each pane for the things that make it
/// that pane: its folder, its Back/Fwd stacks, its marks, cursor, view mode and sort, and the
/// listing already read for it.
/// </summary>
public class SwapPanesTests
{
    [Fact]
    public void CtrlUIsTheChordBoundToSwapPanes()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "a");
        t.Open();

        var map = t.Field<System.Collections.Generic.Dictionary<Chord, Cmd>>("_keyReverse");
        Assert.True(map.TryGetValue(new Chord(KeyCode.U, Ctrl: true, Alt: false, Shift: false), out var cmd),
            "no command is bound to Ctrl+U");
        Assert.Equal(Cmd.Swap, cmd);
    }

    [Fact]
    public void CtrlUExchangesTheFolders()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("left-only.txt", "l");
        t.WriteRight("right-only.txt", "r");
        t.Open();

        t.Press(KeyCode.U, KeyModifiers.Ctrl);

        Assert.Equal(t.Right, t.Dir('L'));
        Assert.Equal(t.Left, t.Dir('R'));
    }

    /// <summary>The complaint the request was about: history has to travel with its folder.
    /// It lives on the tab, so the swap carries it — this pins that down from the keyboard,
    /// including that Alt+← afterwards walks the stack that arrived rather than the one that
    /// left.</summary>
    [Fact]
    public void EachPanesHistoryTravelsWithIt()
    {
        using var t = new DiffViewTests.Fixture();
        Directory.CreateDirectory(Path.Combine(t.Left, "sub"));
        t.WriteRight("r.txt", "r");
        t.Open();

        // Only the left pane has been anywhere.
        t.PutCursorOn('L', "sub");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Dir('L') == Path.Combine(t.Left, "sub"));
        Assert.True(CanGoBack(t, 'L'));
        Assert.False(CanGoBack(t, 'R'));

        t.Press(KeyCode.U, KeyModifiers.Ctrl);

        Assert.False(CanGoBack(t, 'L'));
        Assert.True(CanGoBack(t, 'R'), "the Back stack stayed behind on the left");

        // And it is a working stack, not merely a non-empty one.
        t.Press(KeyCode.Tab);                       // focus the pane that now holds it
        t.Press(KeyCode.Left, KeyModifiers.Alt);
        t.Session.PumpUntil(() => t.Dir('R') == t.Left);
        Assert.Equal(t.Left, t.Dir('R'));
    }

    /// <summary>Marks, cursor, view mode and sort are per-tab too, so they travel as well.
    /// The marks are the interesting one: they are what the next F5 would act on.</summary>
    [Fact]
    public void MarksCursorViewModeAndSortTravelWithThePane()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "a");
        t.WriteLeft("b.txt", "b");
        t.WriteRight("r.txt", "r");
        t.Open();

        t.Mark('L', "a.txt");
        t.PutCursorOn('L', "b.txt");
        int cursor = t.Cursor('L');
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);     // view: Brief
        t.Invoke("SetSort", 'L', SortMode.Size);
        t.Session.PumpFrame();

        t.Press(KeyCode.U, KeyModifiers.Ctrl);

        Assert.Equal(new[] { Path.Combine(t.Left, "a.txt") }, t.MarkSet('R').ToArray());
        Assert.Empty(t.MarkSet('L'));
        Assert.Equal(cursor, t.Cursor('R'));
        Assert.Equal(ViewMode.Brief, (ViewMode)t.Invoke("ViewOf", 'R')!);
        Assert.Equal(SortMode.Size, (SortMode)t.Invoke("SortOf", 'R')!);
    }

    /// <summary>The listing already read for a pane belongs to that pane's tabs, not to the
    /// half of the screen it was showing on. Left behind, each pane kept the *other* side's
    /// entries under the new breadcrumb until a fresh read landed — a folder shown under the
    /// wrong path, and briefly operable.
    ///
    /// <para>Asserted on the loader objects rather than on the entries: that is the mechanism,
    /// and it settles in one frame instead of racing a background directory read.</para></summary>
    [Fact]
    public void TheListingSwapsWithThePaneInsteadOfBeingRereadAfterwards()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("left-only.txt", "l");
        t.WriteRight("right-only.txt", "r");
        t.Open();

        var loaderL = t.Field<object>("_listL");
        var loaderR = t.Field<object>("_listR");

        t.Press(KeyCode.U, KeyModifiers.Ctrl);

        Assert.Same(loaderR, t.Field<object>("_listL"));
        Assert.Same(loaderL, t.Field<object>("_listR"));
        // Which is what makes the entries right on the very next frame.
        Assert.Contains(t.Rows('L'), e => e.Name == "right-only.txt");
        Assert.DoesNotContain(t.Rows('L'), e => e.Name == "left-only.txt");
    }

    /// <summary>Swapping back is the identity — nothing accumulates, and in particular the
    /// generation counters stay paired with the loaders they key.</summary>
    [Fact]
    public void SwappingTwiceLeavesEverythingWhereItStarted()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("left-only.txt", "l");
        t.WriteRight("right-only.txt", "r");
        t.Open();

        t.Press(KeyCode.U, KeyModifiers.Ctrl);
        t.Press(KeyCode.U, KeyModifiers.Ctrl);

        Assert.Equal(t.Left, t.Dir('L'));
        Assert.Equal(t.Right, t.Dir('R'));
        Assert.Contains(t.Rows('L'), e => e.Name == "left-only.txt");
        Assert.Contains(t.Rows('R'), e => e.Name == "right-only.txt");
    }

    /// <summary>The keyboard stays in the pane you are looking at: Ctrl+U moves the folders
    /// past the cursor, it does not move the cursor to the other side of the screen.</summary>
    [Fact]
    public void TheActivePaneStaysWhereItWas()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "a");
        t.WriteRight("r.txt", "r");
        t.Open();
        Assert.Equal('L', t.Field<char>("_active"));

        t.Press(KeyCode.U, KeyModifiers.Ctrl);

        Assert.Equal('L', t.Field<char>("_active"));
    }

    private static bool CanGoBack(DiffViewTests.Fixture t, char side) =>
        (bool)t.Invoke("CanGoBack", side)!;
}

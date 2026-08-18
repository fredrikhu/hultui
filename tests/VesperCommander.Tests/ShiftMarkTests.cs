using System.IO;
using System.Linq;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Shift + a navigation key marks the range the cursor sweeps over. Anchored rather than
/// step-by-step: every press recomputes the range from where the sweep began, which is what
/// lets turning around give the marks back instead of piling more on.
/// </summary>
public class ShiftMarkTests
{
    private static DiffViewTests.Fixture FiveFiles()
    {
        var t = new DiffViewTests.Fixture();
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt", "e.txt" })
            t.WriteLeft(name, name);
        t.Open();
        return t;
    }

    [Fact]
    public void ShiftDownMarksTheRowsItPassesOver()
    {
        using var t = FiveFiles();
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.Down, KeyModifiers.Shift);
        t.Press(KeyCode.Down, KeyModifiers.Shift);

        // The row the sweep started on is in the range, not just the ones stepped onto.
        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt" }, t.MarkNames('L').Order());
        Assert.Equal("c.txt", t.Rows('L')[t.Cursor('L')].Name);
    }

    [Fact]
    public void TurningAroundShrinksTheRangeAgain()
    {
        using var t = FiveFiles();
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.Down, KeyModifiers.Shift);
        t.Press(KeyCode.Down, KeyModifiers.Shift);
        t.Press(KeyCode.Up, KeyModifiers.Shift);

        Assert.Equal(new[] { "a.txt", "b.txt" }, t.MarkNames('L').Order());
    }

    /// <summary>Past the anchor and out the other side: the range follows, and the rows on
    /// the far side of where it started are no longer in it.</summary>
    [Fact]
    public void SweepingBackPastTheAnchorMarksTheOtherDirection()
    {
        using var t = FiveFiles();
        t.PutCursorOn('L', "c.txt");

        t.Press(KeyCode.Down, KeyModifiers.Shift);   // c, d
        t.Press(KeyCode.Up, KeyModifiers.Shift);     // c
        t.Press(KeyCode.Up, KeyModifiers.Shift);     // b, c

        Assert.Equal(new[] { "b.txt", "c.txt" }, t.MarkNames('L').Order());
    }

    [Fact]
    public void ShiftEndMarksToTheBottomAndShiftHomeToTheTop()
    {
        using var t = FiveFiles();
        t.PutCursorOn('L', "c.txt");
        t.Press(KeyCode.End, KeyModifiers.Shift);
        Assert.Equal(new[] { "c.txt", "d.txt", "e.txt" }, t.MarkNames('L').Order());

        t.Press(KeyCode.Home, KeyModifiers.Shift);
        // Home reaches the ".." row, which never takes a mark.
        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt" }, t.MarkNames('L').Order());
        Assert.DoesNotContain("..", t.MarkNames('L'));
    }

    [Fact]
    public void ShiftPageDownMarksAPageOfRows()
    {
        using var t = FiveFiles();
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.PageDown, KeyModifiers.Shift);

        // However tall the pane is, everything between the anchor and where it landed.
        var rows = t.Rows('L');
        int cur = t.Cursor('L');
        var expected = rows.Skip(1).Take(cur).Select(e => e.Name);   // a.txt is row 1, after ".."
        Assert.Equal(expected.Order(), t.MarkNames('L').Order());
        Assert.Contains("a.txt", t.MarkNames('L'));
    }

    /// <summary>Marks made before the sweep are left alone — a sweep adds a range, it does
    /// not replace the selection.</summary>
    [Fact]
    public void MarksMadeEarlierSurviveASweepElsewhere()
    {
        using var t = FiveFiles();
        t.PutCursorOn('L', "e.txt");
        t.Press(KeyCode.Space);                      // e marked by hand
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.Down, KeyModifiers.Shift);

        Assert.Equal(new[] { "a.txt", "b.txt", "e.txt" }, t.MarkNames('L').Order());
    }

    /// <summary>Moving without Shift ends the sweep, so the next one starts a fresh range
    /// rather than reaching back to where the last one began.</summary>
    [Fact]
    public void APlainMoveEndsTheSweep()
    {
        using var t = FiveFiles();
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.Down, KeyModifiers.Shift);   // a, b
        t.Press(KeyCode.Down);                       // plain move to c — sweep over
        t.Press(KeyCode.Down, KeyModifiers.Shift);   // new range: c, d

        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt", "d.txt" }, t.MarkNames('L').Order());
    }

    [Fact]
    public void ShiftDownOnTheLastRowStillMarksIt()
    {
        using var t = FiveFiles();
        t.PutCursorOn('L', "e.txt");

        t.Press(KeyCode.Down, KeyModifiers.Shift);

        Assert.Equal(new[] { "e.txt" }, t.MarkNames('L').Order());
    }

    /// <summary>The other pane is untouched: marks are per tab, and so is the anchor.</summary>
    [Fact]
    public void TheOtherPaneKeepsItsOwnMarks()
    {
        using var t = FiveFiles();
        t.WriteRight("r.txt", "r");
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.Down, KeyModifiers.Shift);

        Assert.Empty(t.MarkNames('R'));
    }
}

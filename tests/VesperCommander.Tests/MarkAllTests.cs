using System.Collections.Generic;
using System.IO;
using System.Linq;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>Ctrl+A: mark everything in the active pane — files and folders, never the
/// ".." parent, and only what a type-to-find filter is showing while one is active.</summary>
public class MarkAllTests
{
    [Fact]
    public void CtrlA_MarksEveryFileAndFolder()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "one");
        t.WriteLeft("b.txt", "two");
        Directory.CreateDirectory(Path.Combine(t.Left, "sub"));
        t.Open();

        t.Press(KeyCode.A, KeyModifiers.Ctrl);

        var marks = Marks(t);
        Assert.Equal(new[] { "a.txt", "b.txt", "sub" }, marks.Order());
        Assert.DoesNotContain("..", marks);
    }

    [Fact]
    public void CtrlA_UnderAFilter_MarksOnlyTheRowsItShows()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("alpha.txt", "one");
        t.WriteLeft("beta.txt", "two");
        t.Open();

        t.Press(KeyCode.B);                    // type-to-find: only beta.txt matches
        Assert.DoesNotContain(t.Rows('L'), e => e.Name == "alpha.txt");
        t.Press(KeyCode.A, KeyModifiers.Ctrl);

        Assert.Equal(new[] { "beta.txt" }, Marks(t).Order());
    }

    /// <summary>Ctrl+A when everything is already marked unmarks it all — the toggle.</summary>
    [Fact]
    public void CtrlA_Twice_DeselectsEverything()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "one");
        t.WriteLeft("b.txt", "two");
        t.Open();

        t.Press(KeyCode.A, KeyModifiers.Ctrl);
        Assert.Equal(2, Marks(t).Count);
        t.Press(KeyCode.A, KeyModifiers.Ctrl);

        Assert.Empty(Marks(t));
    }

    /// <summary>Partially marked is not "all marked": Ctrl+A completes the set first,
    /// and only the press after that clears it.</summary>
    [Fact]
    public void CtrlA_WithAPartialSelection_MarksTheRestBeforeToggling()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "one");
        t.WriteLeft("b.txt", "two");
        t.Open();

        t.PutCursorOn('L', "a.txt");
        t.Press(KeyCode.Space);                 // one marked by hand
        t.Press(KeyCode.A, KeyModifiers.Ctrl);

        Assert.Equal(new[] { "a.txt", "b.txt" }, Marks(t).Order());
    }

    /// <summary>The toggle sees only what a filter shows — a mark hidden by the filter
    /// survives deselect-all, just as it was out of reach for select-all.</summary>
    [Fact]
    public void CtrlA_UnderAFilter_TogglesOnlyTheRowsItShows()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("alpha.txt", "one");
        t.WriteLeft("beta.txt", "two");
        t.Open();

        t.PutCursorOn('L', "alpha.txt");
        t.Press(KeyCode.Space);                 // alpha marked, then hidden by the filter
        t.Press(KeyCode.B);                     // type-to-find: only beta.txt shows
        t.Press(KeyCode.A, KeyModifiers.Ctrl);  // beta now marked → all visible marked
        t.Press(KeyCode.A, KeyModifiers.Ctrl);  // toggle clears the visible only

        Assert.Equal(new[] { "alpha.txt" }, Marks(t).Order());
    }

    /// <summary>Leaf names of the left pane's marks — the set itself is keyed by full
    /// path (CommanderApp.MarkKey), which these single-folder tests don't need to spell out.</summary>
    private static HashSet<string> Marks(DiffViewTests.Fixture t) => t.MarkNames('L');
}

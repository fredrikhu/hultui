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

    /// <summary>Ctrl+A again after marking is idempotent — the set, not a toggle.</summary>
    [Fact]
    public void CtrlA_Twice_KeepsEverythingMarked()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "one");
        t.Open();

        t.Press(KeyCode.A, KeyModifiers.Ctrl);
        t.Press(KeyCode.A, KeyModifiers.Ctrl);

        Assert.Equal(new[] { "a.txt" }, Marks(t).Order());
    }

    private static HashSet<string> Marks(DiffViewTests.Fixture t) =>
        (HashSet<string>)t.Invoke("Marks", 'L')!;
}

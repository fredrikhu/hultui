using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Dropping files onto a pane from another application (Explorer, or Vesper's own other
/// pane — both arrive as the same OS drop). Shift is what makes it a move; without it a
/// drop copies, whichever disk the files came from.
/// </summary>
public class DragDropTests
{
    private static Element Pane(DiffViewTests.Fixture t, char side) =>
        t.Field<Ref<BoxElement>>(side == 'L' ? "_paneRefL" : "_paneRefR").Current
        ?? throw new Xunit.Sdk.XunitException($"the {side} pane is not mounted");

    /// <summary>A point inside the pane's list area — below the tab strip, breadcrumb and
    /// column header, so the drop lands on the pane itself rather than its chrome.</summary>
    private static Point InPaneList(DiffViewTests.Fixture t, char side)
    {
        var box = Pane(t, side).Layout.AbsoluteBorderBox;
        return new Point(box.X + box.Width / 2, box.Y + box.Height - 60f);
    }

    private static void Drop(DiffViewTests.Fixture t, char side, KeyModifiers mods, params string[] paths)
    {
        t.Session.Window.DropFiles(InPaneList(t, side), paths, mods);
        t.Session.PumpFrame();
    }

    private static void RunToCompletion(DiffViewTests.Fixture t) =>
        t.Session.PumpUntil(() => t.Field<object?>("_op") == null, TimeSpan.FromSeconds(30));

    [Fact]
    public void DroppingOnAPaneCopiesIntoItsFolder()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        t.Open();

        Drop(t, 'R', KeyModifiers.None, Path.Combine(t.Left, "a.txt"));
        RunToCompletion(t);

        Assert.Equal("A", File.ReadAllText(Path.Combine(t.Right, "a.txt")));
        Assert.True(File.Exists(Path.Combine(t.Left, "a.txt")), "a plain drop must not remove the original");
    }

    [Fact]
    public void HoldingShiftMakesTheDropAMove()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        t.Open();

        Drop(t, 'R', KeyModifiers.Shift, Path.Combine(t.Left, "a.txt"));
        RunToCompletion(t);

        Assert.Equal("A", File.ReadAllText(Path.Combine(t.Right, "a.txt")));
        Assert.False(File.Exists(Path.Combine(t.Left, "a.txt")), "Shift means move — the original should be gone");
    }

    [Fact]
    public void SeveralFilesArriveInOneBatch()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        t.WriteLeft("b.txt", "B");
        t.Open();

        Drop(t, 'R', KeyModifiers.None,
            Path.Combine(t.Left, "a.txt"), Path.Combine(t.Left, "b.txt"));
        RunToCompletion(t);

        Assert.Equal("A", File.ReadAllText(Path.Combine(t.Right, "a.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(t.Right, "b.txt")));
    }

    /// <summary>A drop back where the files already live is a no-op, not a folder of
    /// "a (2).txt" — the gesture is easy to make by accident while aiming.</summary>
    [Fact]
    public void DroppingIntoTheFolderTheyAreAlreadyInDoesNothing()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        t.Open();

        Drop(t, 'L', KeyModifiers.None, Path.Combine(t.Left, "a.txt"));
        t.Session.PumpFrames(3);

        Assert.Null(t.Field<object?>("_op"));
        Assert.Single(Directory.GetFiles(t.Left));
    }

    /// <summary>A locked tab is read-only, and that has to hold for a gesture as easy to
    /// misfire as a drag — not just for the keys that ask first.</summary>
    [Fact]
    public void ALockedTabRefusesTheDrop()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        t.Open();
        t.Invoke("ToggleTabLock", 'R', 0);
        t.Session.PumpFrame();

        Drop(t, 'R', KeyModifiers.None, Path.Combine(t.Left, "a.txt"));
        t.Session.PumpFrames(3);

        Assert.False(File.Exists(Path.Combine(t.Right, "a.txt")));
    }

    /// <summary>Dropping a folder onto itself would copy it into itself, forever.</summary>
    [Fact]
    public void AFolderCannotBeDroppedIntoItself()
    {
        using var t = new DiffViewTests.Fixture();
        var nested = Path.Combine(t.Left, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "inner.txt"), "I");
        t.Open();

        // Aim the drop at the pane showing `nested` itself.
        t.Invoke("NavigateTo", 'R', nested, null, false);
        t.Session.PumpUntil(() => t.Dir('R') == nested, TimeSpan.FromSeconds(10));

        Drop(t, 'R', KeyModifiers.None, nested);
        t.Session.PumpFrames(3);

        Assert.Null(t.Field<object?>("_op"));
        Assert.False(Directory.Exists(Path.Combine(nested, "nested")));
    }

    /// <summary>Every entry row is a drag source — that is the outbound half. The gesture
    /// itself hands off to the OS (a modal loop with no place in a headless run), so what is
    /// pinned here is that the rows are armed for it at all.</summary>
    [Fact]
    public void EntryRowsAreDraggableWhereThePlatformCanStartOne()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        t.Open();

        // Headless cannot start a native drag, so it reports the capability absent and rows
        // stay un-armed rather than offering a gesture that would go nowhere.
        Assert.False((bool)Property(t.App, "CanDragOut")!);
        Assert.DoesNotContain(Rows(Pane(t, 'L')), r => r.Draggable);
    }

    private static IEnumerable<Element> Rows(Element root)
    {
        foreach (var c in root.Children)
        {
            yield return c;
            foreach (var d in Rows(c)) yield return d;
        }
    }

    private static object? Property(CommanderApp app, string name) =>
        typeof(CommanderApp).GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app);
}

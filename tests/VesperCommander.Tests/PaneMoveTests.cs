using System.IO;
using System.Linq;
using HultUI.Core.Elements;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The pointer moves Vesper listens to: the root's (the Find dialog's corner grip, and a
/// right-button hold that a move turns into a drag) and each row's (a right-drag marking the
/// rows it crosses). Both handlers are passive and listen only while one of those runs (plan 17,
/// round 4): as ordinary handlers they drew a full frame for every move over the panes, 60
/// frames for 60 idle moves along one row. These pin that idle moves draw nothing and that each
/// gesture still works.
/// </summary>
public class PaneMoveTests
{
    private static DiffViewTests.Fixture FiveFiles()
    {
        var t = new DiffViewTests.Fixture();
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt", "e.txt" })
            t.WriteLeft(name, name);
        t.Open();
        t.Session.PumpFrames(2);
        return t;
    }

    /// <summary>The middle of a file's name in the left pane, in window coordinates.</summary>
    private static (float X, float Y) NameAt(DiffViewTests.Fixture t, string name)
    {
        var label = LayoutProbe.Descendants<TextElement>(t.Root, e => e.Content == name)
            .First(e => e.WindowBorderBox.X < 600);
        var box = label.WindowBorderBox;
        return (box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    [Fact]
    public void AMoveAlongARow_DrawsNoFrame()
    {
        using var t = FiveFiles();
        var (x, y) = NameAt(t, "b.txt");
        t.Session.Window.MoveMouse(x, y);
        t.Session.PumpFrames(2);

        for (int i = 1; i <= 3; i++)
        {
            t.Session.Window.MoveMouse(x + i, y);
            Assert.False(t.Session.PumpFrame(), $"idle move {i} along a row drew a frame");
        }
    }

    [Fact]
    public void ARightDragMarksEveryRowItCrosses()
    {
        using var t = FiveFiles();
        var (x, y) = NameAt(t, "b.txt");
        var (_, yD) = NameAt(t, "d.txt");
        float step = (yD - y) / 2;
        try
        {
            t.Session.Window.MouseDown(x, y, MouseButton.Right);
            // Sideways past the hold's slop first: on Windows that ends the hold, and from then
            // on only the rows listen.
            t.Session.Window.MoveMouse(x + 8, y);
            t.Session.PumpFrame();
            t.Session.Window.MoveMouse(x + 8, y + step);
            t.Session.PumpFrame();
            t.Session.Window.MoveMouse(x + 8, y + 2 * step);
            t.Session.PumpFrame();
            t.Session.Window.MouseUp(x + 8, y + 2 * step, MouseButton.Right);
            t.Session.PumpFrame();

            Assert.Equal(new[] { "b.txt", "c.txt", "d.txt" }, t.MarkNames('L').Order());
        }
        finally { CancelHold(t); }
    }

    /// <summary>The hold is watched at the root: a press held still opens the shell's context
    /// menu, and moving off the rows — where no row hears the pointer — must still end it.</summary>
    [WindowsFact("the right-button hold opens the shell's context menu")]
    public void AMoveOffTheRowsEndsTheContextHold()
    {
        using var t = FiveFiles();
        var (x, y) = NameAt(t, "b.txt");
        try
        {
            t.Session.Window.MouseDown(x, y, MouseButton.Right);
            Assert.True(t.Field<bool>("_holdArmed"), "the right press armed no hold");
            t.Session.Window.MoveMouse(x + 2, y + 1);   // inside the slop: the hand shaking
            Assert.True(t.Field<bool>("_holdArmed"), "a move inside the slop ended the hold");

            t.Session.Window.MoveMouse(x, y + 400);     // below the five rows, over the empty pane
            Assert.False(t.Field<bool>("_holdArmed"), "a move off the rows left the hold armed");
            t.Session.Window.MouseUp(x, y + 400, MouseButton.Right);
            t.Session.PumpFrame();
        }
        finally { CancelHold(t); }
    }

    [Fact]
    public void TheFindGripResizesTheDialog()
    {
        using var t = FiveFiles();
        t.Invoke("OpenFind", 'L');
        t.Session.PumpFrames(2);
        var grip = LayoutProbe.Descendants<Element>(t.Root, e => e.TooltipText == "Drag to resize").Single();
        var box = grip.WindowBorderBox;
        float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
        float before = t.Field<float>("_findW");

        t.Session.Window.MoveMouse(x, y);
        t.Session.PumpFrame();
        t.Session.Window.MouseDown(x, y);
        t.Session.PumpFrame();
        t.Session.Window.MoveMouse(x + 30, y + 20);
        Assert.True(t.Session.PumpFrame(), "the resize drew no frame");
        // The card is centred, so it grows by twice the move to keep its edge under the pointer.
        Assert.Equal(before + 60, t.Field<float>("_findW"), 1);
        t.Session.Window.MouseUp(x + 30, y + 20);
        t.Session.PumpFrame();
        Assert.False(t.Field<bool>("_findResizing"));

        t.Session.Window.MoveMouse(x + 60, y + 40);
        t.Session.PumpFrame();
        Assert.Equal(before + 60, t.Field<float>("_findW"), 1);
    }

    /// <summary>A hold left armed opens the shell's menu from its timer; never leave one behind.</summary>
    private static void CancelHold(DiffViewTests.Fixture t) => t.Invoke("CancelContextHold");
}

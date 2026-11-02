using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Vesper's overlays are cards on a dim backdrop whose click closes them (CommanderApp.Backdrop).
/// Each card is a dialog to assistive technology, named by its title, directly inside the backdrop,
/// so the backdrop is no button (the user approved what a reader hears on 2026-10-27). Before, a
/// box with a click around the card was a button named after all of the card's text, and a text
/// change inside it could not be patched: every tick of a copy's progress built the window's whole
/// accessibility tree.
/// </summary>
public class DialogRoleTests
{
    private static AccessibilityNode? Find(AccessibilityNode? node, Func<AccessibilityNode, bool> match)
    {
        if (node is null) return null;
        if (match(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, match) is { } found) return found;
        return null;
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in Walk(child)) yield return n;
    }

    [Fact]
    public void AProgressTick_PatchesTheDialog_AndBuildsNoTree()
    {
        using var t = new DiffViewTests.Fixture();
        for (int i = 0; i < 30; i++) t.WriteLeft($"f{i:00}.txt", "x");
        t.Open();
        var bridge = t.Session.Window.InstallAccessibilityBridge();
        var opType = typeof(CommanderApp).GetNestedType("TransferOp", BindingFlags.NonPublic)!;
        var op = Activator.CreateInstance(opType, t.Rows('L').Where(e => !e.IsParent).Take(5).ToList())!;
        opType.GetField("TotalBytes")!.SetValue(op, 1000L);
        opType.GetField("DestDir")!.SetValue(op, t.Right);
        t.SetField("_op", op);
        t.SetField("_progressOpen", true);
        t.MarkDirty();
        for (int i = 0; i < 4; i++) t.Session.PumpFrame(TimeSpan.FromMilliseconds(200));

        int trees = t.Session.Window.AccessibilityTreesBuilt;
        for (int i = 1; i <= 10; i++)
        {
            opType.GetField("TotalBytesDone")!.SetValue(op, i * 50L);
            t.Invoke("Repaint", false);
            t.Session.PumpFrame(TimeSpan.FromMilliseconds(50));
        }
        for (int i = 0; i < 3; i++) t.Session.PumpFrame(TimeSpan.FromMilliseconds(200));
        Assert.Equal(0, t.Session.Window.AccessibilityTreesBuilt - trees);
        Assert.NotNull(Find(bridge.CurrentTree, n => n.Name == "50%"));

        // The card is the dialog, named by its title; nothing around it is a button reading it.
        var tree = bridge.CurrentTree!;
        var dialog = Find(tree, n => n.Role == AccessibilityRole.Dialog);
        Assert.NotNull(dialog);
        Assert.Equal("Copying", dialog!.Name);
        Assert.DoesNotContain(Walk(tree), n => n.Role == AccessibilityRole.Button && n.Name?.Contains("Copying") == true);

        t.SetField("_op", null);
        t.SetField("_progressOpen", false);
        t.MarkDirty();
        t.Session.PumpFrames(2);
    }

    /// <summary>The backdrop still closes its card on a click beside it, and Escape still closes it;
    /// the card is a dialog named by its title.</summary>
    [Fact]
    public void AClickBesideTheCard_AndEscape_StillCloseIt()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "x");
        t.Open();
        t.Session.Window.InstallAccessibilityBridge();
        for (int round = 0; round < 2; round++)
        {
            t.Press(KeyCode.D, KeyModifiers.Ctrl);   // the bookmark manager
            t.Session.PumpFrames(2);
            Assert.True(t.Field<bool>("_bookmarksOpen"));
            var card = t.Session.Window.FindByRole(AccessibilityRole.Dialog, "Bookmarks");
            Assert.NotNull(card);
            if (round == 0)
            {
                var box = card!.Element.Layout.AbsoluteBorderBox;
                t.Session.Window.Click(box.X - 20, box.Y + 10);
            }
            else t.Session.Window.PressKey(KeyCode.Escape);
            t.Session.PumpFrames(2);
            Assert.False(t.Field<bool>("_bookmarksOpen"));
            Assert.Null(t.Session.Window.FindByRole(AccessibilityRole.Dialog, "Bookmarks"));
        }
    }
}

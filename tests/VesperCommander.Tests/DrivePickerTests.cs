using System.Collections.Generic;
using System.IO;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The Alt+F1 / Alt+F2 drive picker, brought up to Total Commander's behaviour: a letter
/// key picks its drive outright, and picking the volume the other pane is already on lands
/// on that pane's folder rather than the bare root.
/// </summary>
public class DrivePickerTests
{
    /// <summary>Both panes live under the temp folder, so they share a volume — pressing that
    /// volume's letter has to follow the other pane in rather than dropping at the root. This
    /// covers the letter key and the same-drive rule in one press, which is how they are
    /// actually used.</summary>
    [Fact]
    public void PickingTheDriveTheOtherPaneIsOnOpensTheFolderItIsShowing()
    {
        using var t = new DiffViewTests.Fixture();
        t.Open();

        t.Press(KeyCode.F1, KeyModifiers.Alt);
        t.Press(LetterKeyOf(t.Left));

        Assert.Equal(t.Right, t.Dir('L'));
    }

    /// <summary>The other half of the rule: a volume the other pane is not on opens where the
    /// picker says. The temp folder stands in for a second volume — it is a real, navigable
    /// path that is not the other pane's root, which is the only property the rule tests.</summary>
    [Fact]
    public void PickingAnotherDriveOpensWhatThePickerNames()
    {
        using var t = new DiffViewTests.Fixture();
        t.Open();

        t.Press(KeyCode.F1, KeyModifiers.Alt);
        var elsewhere = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        Only(t, Drive(elsewhere));
        t.Press(KeyCode.Enter);

        Assert.Equal(elsewhere, t.Dir('L'));
        Assert.NotEqual(t.Right, t.Dir('L'));
    }

    [Fact]
    public void ALetterJumpsPastTheDrivesBeforeIt()
    {
        using var t = new DiffViewTests.Fixture();
        t.Open();

        t.Press(KeyCode.F1, KeyModifiers.Alt);
        Only(t, Drive(@"C:\"), Drive(@"D:\"), Drive(@"E:\"));
        t.Press(KeyCode.E);

        Assert.Equal(2, t.Field<int>("_driveCur"));
        Assert.Null(t.Field<char?>("_drivePicker"));   // and it committed, not just highlighted
    }

    /// <summary>A letter no volume answers to leaves the picker exactly as it was — better
    /// than closing it or jumping somewhere arbitrary, since the next keystroke is usually
    /// the letter the user meant.</summary>
    [Fact]
    public void ALetterWithNoDriveBehindItDoesNothing()
    {
        using var t = new DiffViewTests.Fixture();
        t.Open();
        var before = t.Dir('L');

        t.Press(KeyCode.F1, KeyModifiers.Alt);
        Only(t, Drive(@"C:\"));
        t.Press(KeyCode.Q);

        Assert.Equal('L', t.Field<char?>("_drivePicker"));
        Assert.Equal(before, t.Dir('L'));
    }

    /// <summary>A mount with no drive letter — a UNC share — is skipped rather than counted,
    /// so the letter still reaches the volume that actually carries it.</summary>
    [Fact]
    public void AMountWithoutADriveLetterIsSkipped()
    {
        using var t = new DiffViewTests.Fixture();
        t.Open();

        t.Press(KeyCode.F1, KeyModifiers.Alt);
        Only(t, Drive(@"\\server\share"), Drive(@"S:\"));
        t.Press(KeyCode.S);

        Assert.Equal(1, t.Field<int>("_driveCur"));
    }

    /// <summary>Replace the real volume list with a known one. The picker reads it on every
    /// key, so this has to land after Alt+F1 has opened it — that call is what fills it in.</summary>
    private static void Only(DiffViewTests.Fixture t, params DriveEntry[] drives)
    {
        t.SetField("_drives", new List<DriveEntry>(drives));
        t.SetField("_driveCur", 0);
    }

    private static DriveEntry Drive(string root) => new(root, "test", 0, 0, "NTFS", Ready: true);

    private static KeyCode LetterKeyOf(string path)
    {
        var root = Path.GetPathRoot(path)!;
        return (KeyCode)((int)KeyCode.A + (char.ToUpperInvariant(root[0]) - 'A'));
    }
}

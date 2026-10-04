using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The keymap on the framework's <see cref="ShortcutMap"/>: the keys Vesper keeps for itself are
/// in it, so the Keyboard page can no longer hand one to a command, and a binding saved by an
/// older Vesper still means what it did.
/// </summary>
public sealed class KeymapTests
{
    [Fact]
    public void TheKeyboardPage_RefusesAKeyVesperKeeps_AndSaysWhose()
    {
        // A command on Ctrl+C either never fired (the fixed check ran first) or silently
        // replaced copying files (the keymap ran first), depending on the key.
        using var t = new DiffViewTests.Fixture();
        t.Open();
        t.Invoke("OpenOptions");
        t.SetField("_rebindCmd", Cmd.Copy);

        t.Press(KeyCode.C, KeyModifiers.Ctrl);

        Assert.Equal(Cmd.Copy, t.Field<Cmd?>("_rebindCmd"));   // still capturing
        Assert.Contains("copy to the clipboard", t.Field<string?>("_rebindRefusal"));
        Assert.Empty(t.Field<Settings>("_draft").KeyBindings);

        t.Press(KeyCode.F2);
        Assert.Null(t.Field<Cmd?>("_rebindCmd"));
        Assert.Equal("F2", t.Field<Settings>("_draft").KeyBindings["Copy"]);
    }

    [Fact]
    public void TakingACommandsKey_LeavesThatCommandWithNone_UntilSaved()
    {
        using var t = new DiffViewTests.Fixture();
        t.Open();
        t.Invoke("OpenOptions");
        t.SetField("_rebindCmd", Cmd.Copy);

        t.Press(KeyCode.F6);   // Move's

        var draft = t.Field<Settings>("_draft").KeyBindings;
        Assert.Equal("F6", draft["Copy"]);
        Assert.Equal("", draft["Move"]);
        Assert.Equal("F5", t.Field<ShortcutMap>("_keys").Keys("Copy"));   // the live map waits for Save
    }

    [Fact]
    public void ABindingSavedInTheOldSpelling_StillWorks()
    {
        // Vesper wrote KeyCode names: Alt+Digit9, not Alt+9.
        using var t = new DiffViewTests.Fixture();
        t.Open();
        t.Field<Settings>("_settings").KeyBindings["Swap"] = "Alt+Digit9";
        t.Invoke("RebuildKeymap");

        var keys = t.Field<ShortcutMap>("_keys");
        Assert.Equal("Alt+9", keys.Keys("Swap"));
        Assert.Equal("Swap", keys.Find(new KeyDownEvent { Key = KeyCode.Digit9, Modifiers = KeyModifiers.Alt })?.Id);
    }
}

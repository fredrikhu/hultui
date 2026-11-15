using System.Linq;
using HultUI.Core.Builders;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The root hears every key passively and asks for a frame for it, except for a modifier key on
/// its own (Shift, Ctrl, Alt) and its auto-repeats, which change nothing on screen: each press
/// drew a frame of the same pixels, and a Shift or Ctrl held for a range about 30 a second
/// (plan 17 round 15). The chords they are held for still act and draw.
/// </summary>
public class ModifierKeyFrameTests
{
    private static DiffViewTests.Fixture FiveFiles()
    {
        var t = new DiffViewTests.Fixture();
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt", "e.txt" })
            t.WriteLeft(name, name);
        t.Open();
        for (int i = 0; i < 100 && t.Session.PumpFrame(); i++) { }
        Assert.False(t.Session.PumpFrame(), "the window did not settle");
        return t;
    }

    [Fact]
    public void ABareModifierDrawsNoFrame()
    {
        using var t = FiveFiles();
        t.PutCursorOn('L', "a.txt");
        for (int i = 0; i < 10 && t.Session.PumpFrame(); i++) { }

        foreach (var (key, mods) in new[] { (KeyCode.ShiftLeft, KeyModifiers.Shift), (KeyCode.CtrlLeft, KeyModifiers.Ctrl) })
        {
            t.Session.Window.KeyDown(key, mods);
            Assert.False(t.Session.PumpFrame(), $"{key} drew a frame");
            for (int i = 0; i < 3; i++)
            {
                t.Session.Window.KeyDown(key, mods, repeat: true);
                Assert.False(t.Session.PumpFrame(), $"{key}'s repeat drew a frame");
            }
            t.Session.Window.KeyUp(key);
            Assert.False(t.Session.PumpFrame());
        }
    }

    /// <summary>Shift held, then Shift+Down: the mark it makes is drawn.</summary>
    [Fact]
    public void AHeldShiftStillMarks()
    {
        using var t = FiveFiles();
        t.PutCursorOn('L', "a.txt");
        for (int i = 0; i < 10 && t.Session.PumpFrame(); i++) { }

        t.Session.Window.KeyDown(KeyCode.ShiftLeft, KeyModifiers.Shift);
        Assert.False(t.Session.PumpFrame());
        t.Session.Window.KeyDown(KeyCode.Down, KeyModifiers.Shift);
        Assert.True(t.Session.PumpFrame(), "Shift+Down drew no frame");
        t.Session.Window.KeyUp(KeyCode.Down, KeyModifiers.Shift);
        t.Session.Window.KeyUp(KeyCode.ShiftLeft);
        t.Session.PumpFrames(2);

        Assert.Equal(new[] { "a.txt", "b.txt" }, t.MarkNames('L').Order());
    }

    /// <summary>Ctrl held, then Ctrl+P: the palette opens and is drawn.</summary>
    [Fact]
    public void AHeldCtrlStillOpensThePalette()
    {
        using var t = FiveFiles();

        t.Session.Window.KeyDown(KeyCode.CtrlLeft, KeyModifiers.Ctrl);
        Assert.False(t.Session.PumpFrame());
        t.Session.Window.KeyDown(KeyCode.P, KeyModifiers.Ctrl);
        Assert.True(t.Session.PumpFrame(), "Ctrl+P drew no frame");
        t.Session.Window.KeyUp(KeyCode.P, KeyModifiers.Ctrl);
        t.Session.Window.KeyUp(KeyCode.CtrlLeft);
        t.Session.PumpFrames(2);

        Assert.True(t.Field<CommandPalette?>("_palette") is { IsOpen: true }, "Ctrl+P did not open the palette");
    }
}

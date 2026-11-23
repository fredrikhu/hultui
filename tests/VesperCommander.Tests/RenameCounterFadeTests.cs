using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Multi-rename's counter row (start, step and digits, three number fields) sits at 0.45 while no
/// mask holds a [C], which is how the dialog opens. It fades through a group of its own size: the
/// number fields could not bound their paint, so every frame of the dialog, each key typed into a
/// mask included, drew the row through a layer of the whole card, and on GL Skia kept that texture
/// for the window's life (+9 MB of video memory at 1860×1230; plan 17, round 18).
/// </summary>
public class RenameCounterFadeTests
{
    [Fact]
    public void TheDimmedCounterRowFadesInAGroupOfItsOwnSize()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("one.txt", "1");
        t.WriteLeft("two.txt", "2");
        t.Open(options: new HeadlessOptions { RecordDrawCalls = true });
        t.PutCursorOn('L', "one.txt");

        t.Press(KeyCode.M, KeyModifiers.Ctrl);
        t.Session.PumpFrame();
        Assert.True(t.Field<bool>("_renameOpen"), "Ctrl+M did not open Multi-rename");
        t.Session.Window.RenderLog.Clear();
        t.Session.Window.TypeText("x");
        t.Session.PumpFrame();

        var fades = t.Session.Window.RenderLog.Named("PushOpacity").Select(c => c.ToString()).ToList();
        var row = Assert.Single(fades, f => f.Contains("PushOpacity 0.45", System.StringComparison.Ordinal));
        Assert.Contains("bounds=", row);
    }
}

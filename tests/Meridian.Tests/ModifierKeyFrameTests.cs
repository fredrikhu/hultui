using System.Reflection;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// The root's key handler asks for no frame for a modifier key on its own (Shift, Ctrl) nor for
/// its auto-repeats: as an ordinary handler it drew a frame of the same pixels for each, a
/// shortcut two frames, and a held Shift about 30 a second (plan 17 round 15). Every other key
/// still gets its frame, and the chords still act.
/// </summary>
public class ModifierKeyFrameTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-modkeys-").FullName;

    public ModifierKeyFrameTests() =>
        File.WriteAllText(Path.Combine(_root, "get-ok.req"), "GET http://127.0.0.1:9/ok\n");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ABareModifierDrawsNoFrame_AndCtrlPStillOpensThePalette()
    {
        using var session = HeadlessSession.Create(1440, 900);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window);
            session.Application.SetRoot(app.Mount());
            app.OpenWorkspace(_root);
            for (int i = 0; i < 300 && session.PumpFrame(); i++) { }
            Assert.False(session.PumpFrame(), "the window did not settle");

            foreach (var (key, mods) in new[] { (KeyCode.ShiftLeft, KeyModifiers.Shift), (KeyCode.CtrlLeft, KeyModifiers.Ctrl) })
            {
                session.Window.KeyDown(key, mods);
                Assert.False(session.PumpFrame(), $"{key} drew a frame");
                for (int i = 0; i < 3; i++)
                {
                    session.Window.KeyDown(key, mods, repeat: true);
                    Assert.False(session.PumpFrame(), $"{key}'s repeat drew a frame");
                }
                session.Window.KeyUp(key);
                Assert.False(session.PumpFrame());
            }

            session.Window.KeyDown(KeyCode.CtrlLeft, KeyModifiers.Ctrl);
            Assert.False(session.PumpFrame());
            session.Window.KeyDown(KeyCode.P, KeyModifiers.Ctrl);
            Assert.True(session.PumpFrame(), "Ctrl+P drew no frame");
            session.Window.KeyUp(KeyCode.P, KeyModifiers.Ctrl);
            session.Window.KeyUp(KeyCode.CtrlLeft);
            session.PumpFrames(2);
            Assert.True(typeof(MeridianApp).GetField("_palette", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(app) is HultUI.Core.Builders.CommandPalette { IsOpen: true }, "Ctrl+P did not open the palette");
        }
        finally { app?.Dispose(); }
    }
}

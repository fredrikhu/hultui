using System.Reflection;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The root's key handler asks for no frame for a modifier key on its own (Shift, Ctrl) nor for
/// its auto-repeats: as an ordinary handler it drew a frame of the same pixels for each, a
/// shortcut two frames, and a Shift held for a range about 30 a second (plan 17 round 15). Every
/// other key still gets its frame, and the chords still act.
/// </summary>
public class ModifierKeyFrameTests
{
    [Fact]
    public void ABareModifierDrawsNoFrame_AndCtrlPStillOpensThePalette()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        using var session = HeadlessSession.Create(1440, 900);
        var app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
        {
            RepositoryPaths = [repo.Path],
            ActiveRepoPath = repo.Path,
            Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
            ActiveWorkspaceId = "w1",
        });
        try
        {
            session.Application.SetRoot(app.Mount());
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
            Assert.True(typeof(JunctionApp).GetField("_palette", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(app) is HultUI.Core.Builders.CommandPalette { IsOpen: true }, "Ctrl+P did not open the palette");
        }
        finally { app.Dispose(); }
    }
}

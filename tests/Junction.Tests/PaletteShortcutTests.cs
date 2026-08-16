using System.Reflection;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The key that opens the command palette. Junction was on Ctrl+K while Vesper was on Ctrl+P,
/// which made one shortcut mean two things across two apps built from the same framework.
/// Ctrl+P is the standard now; Ctrl+K keeps working, having been the advertised key for ten
/// releases.
/// </summary>
public class PaletteShortcutTests
{
    [Fact]
    public void CtrlPOpensThePalette()
    {
        Hosted((session, app) =>
        {
            Assert.False(PaletteOpen(app), "the palette was already open");

            session.Window.PressKey(KeyCode.P, KeyModifiers.Ctrl);
            session.PumpFrames(3);

            Assert.True(PaletteOpen(app), "Ctrl+P did not open the palette");
        });
    }

    [Fact]
    public void CtrlPClosesItAgain()
    {
        Hosted((session, app) =>
        {
            session.Window.PressKey(KeyCode.P, KeyModifiers.Ctrl);
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.P, KeyModifiers.Ctrl);
            session.PumpFrames(3);

            Assert.False(PaletteOpen(app), "Ctrl+P did not toggle the palette shut");
        });
    }

    /// <summary>The key Junction shipped with still works, so a decade of muscle memory is not
    /// answered with nothing happening.</summary>
    [Fact]
    public void CtrlKStillOpensIt()
    {
        Hosted((session, app) =>
        {
            session.Window.PressKey(KeyCode.K, KeyModifiers.Ctrl);
            session.PumpFrames(3);

            Assert.True(PaletteOpen(app), "Ctrl+K stopped opening the palette");
        });
    }

    /// <summary>
    /// Ctrl+P reaches the palette from the Branches view too — the view where the collision is.
    /// The per-view handlers switch on the bare key and ignore modifiers, and there <c>P</c> is
    /// Push, so Ctrl+P has to be taken before view dispatch or asking for the palette pushes a
    /// branch to its remote on the way.
    /// <para>The guard is the palette assertion: remove the interception and Ctrl+P falls through
    /// to the Branches handler, leaving the palette shut. Deliberately <em>not</em> asserted by
    /// checking that no push was announced — nothing is selected in this fixture, so bare P is a
    /// no-op here and "no push happened" would hold whether the key was intercepted or not.
    /// Selecting a branch to make the push real would shell out a git push.</para>
    /// </summary>
    [Fact]
    public void CtrlPReachesThePaletteFromTheViewWhereBarePIsPush()
    {
        Hosted((session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit4);       // Branches
            session.PumpFrames(3);

            session.Window.PressKey(KeyCode.P, KeyModifiers.Ctrl);
            session.PumpFrames(3);

            Assert.True(PaletteOpen(app), "Ctrl+P in the Branches view did not open the palette");
        });
    }

    // ── plumbing ─────────────────────────────────────────────────────────
    private static bool PaletteOpen(JunctionApp app) => Field<bool>(app, "_paletteOpen");

    private static T Field<T>(JunctionApp app, string name) =>
        (T)typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [repo.Path],
                ActiveRepoPath = repo.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
                ActiveWorkspaceId = "w1",
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}

using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The rows' right-click press handlers act only on the right button and mark what they change,
/// so they are passive: a left press on a row asks for no frame of its own. As ordinary handlers,
/// every left press drew the window again with the same pixels before the click's frame (plan 17
/// round 15: 10 History row clicks drew 20 frames in a real window, 10 of them repeats).
/// </summary>
public class RowPressFrameTests
{
    [Fact]
    public void ALeftPressOnAHistoryRow_DrawsNoFrame_AndTheClickSelectsIt()
    {
        using var repo = ThreeCommits();
        using var session = HeadlessSession.Create(1440, 900);
        var app = Open(session, repo);
        try
        {
            var row = RowOf(session, "second");
            var (x, y) = Center(row);
            session.Window.MoveMouse(x, y);
            Settle(session);

            session.Window.MouseDown(x, y);
            Assert.False(session.PumpFrame(), "the left press drew a frame");

            session.Window.MouseUp(x, y);
            Assert.True(session.PumpFrame(), "the click drew no frame");
            Assert.Equal(repo.Git("rev-parse", "HEAD~1").Stdout.Trim(), SelectedHash(app));
        }
        finally { app.Dispose(); }
    }

    /// <summary>A right press outside the selection still collapses it to the row and draws, and the
    /// click opens the row's menu.</summary>
    [Fact]
    public void ARightClickOnAHistoryRow_StillSelectsIt_AndOpensItsMenu()
    {
        using var repo = ThreeCommits();
        using var session = HeadlessSession.Create(1440, 900);
        var app = Open(session, repo);
        try
        {
            var (x, y) = Center(RowOf(session, "second"));
            session.Window.MoveMouse(x, y);
            Settle(session);

            session.Window.MouseDown(x, y, MouseButton.Right);
            Assert.Equal(repo.Git("rev-parse", "HEAD~1").Stdout.Trim(), SelectedHash(app));
            Assert.True(session.PumpFrame(), "the right press drew no frame for its selection");
            session.Window.MouseUp(x, y, MouseButton.Right);
            session.PumpFrames(2);
            Assert.NotEmpty(session.Window.OverlayContents);
        }
        finally { app.Dispose(); }
    }

    private static TempRepo ThreeCommits()
    {
        var repo = TempRepo.CreateWithInitialCommit();
        repo.Git("commit", "--allow-empty", "-m", "second");
        repo.Git("commit", "--allow-empty", "-m", "third");
        return repo;
    }

    private static JunctionApp Open(HeadlessSession session, TempRepo repo)
    {
        var app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
        {
            RepositoryPaths = [repo.Path],
            ActiveRepoPath = repo.Path,
            Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
            ActiveWorkspaceId = "w1",
        });
        session.Application.SetRoot(app.Mount());
        session.PumpUntil(() => LayoutProbe.Descendants<TextElement>(Root(session), t => t.Content == "second").Count > 0,
            TimeSpan.FromSeconds(15));
        Settle(session);
        return app;
    }

    /// <summary>Pumps until a frame draws nothing (the loads and their frames are done).</summary>
    private static void Settle(HeadlessSession session)
    {
        for (int i = 0; i < 200 && session.PumpFrame(); i++) { }
        Assert.False(session.PumpFrame(), "the window did not settle");
    }

    /// <summary>The subject label of the History row whose subject is <paramref name="subject"/>:
    /// a press on it bubbles to the row's handlers.</summary>
    private static Element RowOf(HeadlessSession session, string subject) =>
        LayoutProbe.Descendants<TextElement>(Root(session), t => t.Content == subject)[0];

    private static (float X, float Y) Center(Element e)
    {
        var box = e.WindowBorderBox;
        return (box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    private static string? SelectedHash(JunctionApp app) =>
        (string?)typeof(JunctionApp)
            .GetField("_selectedCommitHash", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(app);

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(session.Window.Window);
}

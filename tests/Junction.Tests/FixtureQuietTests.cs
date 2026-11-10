using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The app reading a fixture writes nothing into its <c>.git</c>, and its watcher finds nothing
/// to report.
///
/// <para>A fixture is a copy of a template repository, so every file in it is newer than the
/// stat data its index holds, and the first <c>git status</c> the app ran on one — the
/// sidebar's summary, the Changes view — wrote the index back: in 40 of 40 starts measured.
/// The app's watcher cannot tell that write from another program's, and now and then it
/// reloaded the repository 300 ms later, rebuilding the window in the middle of whatever a
/// test measured across frames (round 3: 2 starts in 40; DiffItemLabelTests failed once in
/// CI). TempRepo turns git's optional locks off for the test process, so a read-only call
/// writes nothing (0 of 40).</para>
/// </summary>
public class FixtureQuietTests
{
    [Fact]
    public void OpeningAFixture_LeavesItsIndexAsItWas()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("notes.txt", "one\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "notes");
        repo.Write("notes.txt", "two\n");   // a change for the Changes view to diff
        var index = Path.Combine(repo.Path, ".git", "index");
        var written = File.GetLastWriteTimeUtc(index);

        using var session = HeadlessSession.Create(1400, 900);
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
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Digit4);
            // The Changes view lists the file once its status has run; then the diffs land.
            session.PumpUntil(() => LayoutProbe.Descendants<HultUI.Core.Elements.TextElement>(Root(session),
                t => t.Content == "notes.txt").Count > 0, TimeSpan.FromSeconds(20));
            for (int i = 0; i < 30; i++) { session.PumpFrame(); Thread.Sleep(5); }
            Assert.True(written == File.GetLastWriteTimeUtc(index),
                $"the index was written {(File.GetLastWriteTimeUtc(index) - written).TotalMilliseconds:0} ms after the fixture's commit "
                + $"(GIT_OPTIONAL_LOCKS={Environment.GetEnvironmentVariable("GIT_OPTIONAL_LOCKS") ?? "unset"})");
        }
        finally { app.Dispose(); }
    }

    /// <summary>A fresh copy gives the app's watcher nothing to report. On Windows a copied
    /// folder reported a change the first time it was listed: the app's first
    /// <c>git for-each-ref</c> set off "refs\heads changed" in every copy, and the watcher
    /// reloaded the repository 300 ms later, rebuilding the window and the chrome in the middle of
    /// what ChromePartTests counted. TempRepo lists every folder of a copy before handing it
    /// out.</summary>
    [Fact]
    public void OpeningAFixtureCopy_GivesItsWatcherNothingToReport()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        using var session = HeadlessSession.Create(1400, 900);
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
            // The loads of the first frames list refs\heads; a report they set off would reach the
            // app 300 ms (the watcher's debounce) later, about half a second after the mount.
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(1);
            while (DateTime.UtcNow < until) { session.PumpFrame(); Thread.Sleep(5); }
            var reloads = ((Dictionary<string, int>)typeof(JunctionApp)
                .GetField("_dataGen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(app)!).GetValueOrDefault(repo.Path);
            Assert.True(reloads == 0, $"the watcher reloaded the fresh copy {reloads} time(s)");
        }
        finally { app.Dispose(); }
    }

    private static HultUI.Core.Elements.Element Root(HeadlessSession session) =>
        (HultUI.Core.Elements.Element)typeof(HultUI.Window)
            .GetField("_root", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;
}

using System.Collections;
using System.Reflection;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// What is already warm when you switch from History to the Commit tab.
///
/// <para>Selecting a commit in History prefetches its files and diffs, so the Commit tab
/// opens with content rather than "Loading…". The colours were a step behind: syntax
/// highlighting was only ever asked for by the diff pane itself, and the diff pane only
/// exists once the Commit view is the one building — so every switch was followed by a
/// few hundred milliseconds of uncoloured diff. Measured on this repository before the
/// fix: the detail was warm with 0 ms to wait, then ~350 ms of grey text after the
/// switch.</para>
///
/// <para>The assertions below are about <i>when</i> the work starts, not how fast it is,
/// which is the only part of that a test can hold: the tokenize has to be under way (or
/// finished) while History is still the visible view.</para>
/// </summary>
[Collection("JunctionDiff")]
public class CommitPrefetchTests
{
    private static object? Field(JunctionApp app, string name) =>
        typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app);

    private static IDictionary Highlights(JunctionApp app) =>
        (IDictionary)Field(app, "_highlights")!;

    private static int Pending(JunctionApp app) =>
        ((IEnumerable<string>)Field(app, "_highlightsPending")!).Count();

    /// <summary>How many files the prefetched commit detail holds.</summary>
    private static int DetailFiles(JunctionApp app)
    {
        var loader = Field(app, "_commitDetail")!;
        var value = loader.GetType().GetProperty("Value")!.GetValue(loader)!;
        return ((ICollection)value.GetType().GetProperty("Files")!.GetValue(value)!).Count;
    }

    private static JunctionView View(JunctionApp app) =>
        (JunctionView)typeof(JunctionApp).GetProperty("View")!.GetValue(app)!;

    /// <summary>
    /// The regression: the opening file is tokenized while History is still on screen.
    /// Without the prefetch this times out — nothing asks for a highlight until the
    /// Commit view builds.
    /// </summary>
    [Fact]
    public void SelectingACommitInHistoryStartsTokenizingTheFileTheDiffWillOpenOn()
    {
        Hosted((session, app) =>
        {
            OpenHistory(session, app);

            session.PumpUntil(() => Highlights(app).Count > 0 || Pending(app) > 0,
                TimeSpan.FromSeconds(20));

            Assert.Equal(JunctionView.History, View(app));
        });
    }

    /// <summary>And the detail itself, which is what stops the tab showing "Loading…".</summary>
    [Fact]
    public void TheCommitsFilesAndDiffsAreLoadedBeforeTheTabIsOpened()
    {
        Hosted((session, app) =>
        {
            OpenHistory(session, app);

            Assert.True(DetailFiles(app) > 0,
                "the selected commit's detail was not prefetched from History");
            Assert.Equal(JunctionView.History, View(app));
        });
    }

    /// <summary>The whole point, stated as the user would: the switch itself waits for
    /// nothing. Both caches answer on the first frame of the Commit view.</summary>
    [Fact]
    public void SwitchingToTheCommitTabWaitsForNeitherTheDiffNorItsColours()
    {
        Hosted((session, app) =>
        {
            OpenHistory(session, app);
            session.PumpUntil(() => Highlights(app).Count > 0, TimeSpan.FromSeconds(20));
            int warmed = Highlights(app).Count;

            session.Window.PressKey(KeyCode.Digit3);
            session.PumpFrame();

            Assert.Equal(JunctionView.Commit, View(app));
            Assert.True(DetailFiles(app) > 0);
            // Nothing was thrown away and re-asked for: the pane's own request for the
            // opening file's colours is a hit on what History warmed.
            Assert.True(Highlights(app).Count >= warmed);
        });
    }

    /// <summary>Only the file that will actually be shown. A commit of sixteen files must
    /// not tokenize sixteen of them to open one.</summary>
    [Fact]
    public void OnlyTheOpeningFileIsTokenized()
    {
        Hosted((session, app) =>
        {
            OpenHistory(session, app);
            session.PumpUntil(() => Highlights(app).Count > 0, TimeSpan.FromSeconds(20));

            Assert.Equal(1, Highlights(app).Count + Pending(app));
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    /// <summary>Opens History and waits for a commit to be selected and its detail to
    /// arrive — the state someone is in while reading the log.</summary>
    private static void OpenHistory(HeadlessSession session, JunctionApp app)
    {
        session.Window.PressKey(KeyCode.Digit2);
        session.PumpUntil(() => Field(app, "_selectedCommitHash") != null, TimeSpan.FromSeconds(30));
        session.PumpUntil(() => DetailFiles(app) > 0, TimeSpan.FromSeconds(30));
    }

    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        // A commit touching several files, in a language the highlighter knows — the
        // prefetch has a file to choose and something to colour.
        repo.Write("src/Program.cs", "class Program { static void Main() { } }\n");
        repo.Write("src/Helper.cs", "class Helper { }\n");
        repo.Write("README.md", "# demo\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add the sources");

        repo.Write("src/Program.cs", "class Program { static void Main() { System.Console.WriteLine(); } }\n");
        repo.Git("commit", "-am", "print something");

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

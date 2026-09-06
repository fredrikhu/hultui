using System.Collections;
using System.Reflection;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// What is already warm when you switch tabs after switching repositories.
///
/// <para>Every view used to ask for its data only while it was the one building, so the
/// tab you were <i>not</i> on was cold after each repository switch: land on Branches,
/// switch repos, click History, wait for the log. Now a switch queues a preload of the
/// new repository's views behind the visible one, the commit the log picks first has its
/// detail fetched from the selection itself rather than from the History build, the +/-
/// counts load separately from the list they decorate, and the Branches tab reads the
/// list the sidebar's summary already fetched.</para>
///
/// <para>As in <see cref="CommitPrefetchTests"/>, the assertions are about <i>when</i>
/// the work starts — under way or done while another tab is still showing — which is the
/// only part of this a test can hold.</para>
/// </summary>
[Collection("JunctionDiff")]
public class PreloadTests
{
    private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>Switching repositories while Branches is showing starts the new
    /// repository's log, and the log's first commit gets its detail, all before History
    /// is ever built.</summary>
    [Fact]
    public void SwitchingRepositoriesOnBranchesWarmsTheNewRepositorysHistoryAndItsFirstCommit()
    {
        Hosted((session, app, a, b) =>
        {
            session.Window.PressKey(KeyCode.Digit5);
            session.PumpFrame();
            Assert.Equal(JunctionView.Branches, app.View);

            SelectRepo(app, b.Path);
            session.PumpUntil(() => Commits(app, b.Path) > 0, TimeSpan.FromSeconds(30));
            Assert.Equal(JunctionView.Branches, app.View);

            // The selection lands with the preloaded log, and the detail follows it.
            session.PumpUntil(() => DetailFiles(app) > 0, TimeSpan.FromSeconds(30));
            Assert.NotNull(Field(app, "_selectedCommitHash"));
            Assert.Equal(JunctionView.Branches, app.View);
        });
    }

    /// <summary>The working tree, the tags and the +/- counts are in the same batch.</summary>
    [Fact]
    public void TheWorkingTreeTagsAndStatsAreWarmedWithTheLog()
    {
        Hosted((session, app, a, b) =>
        {
            session.Window.PressKey(KeyCode.Digit5);
            session.PumpFrame();

            SelectRepo(app, b.Path);
            session.PumpUntil(() =>
                Unstaged(app, b.Path) > 0 && Tags(app, b.Path) > 0 && Stats(app, b.Path).Count > 0,
                TimeSpan.FromSeconds(30));
            Assert.Equal(JunctionView.Branches, app.View);
        });
    }

    /// <summary>The counts are a separate load from the list: the log carries none, and the
    /// numstat pass fills them in keyed on the commit.</summary>
    [Fact]
    public void TheCommitListCarriesItsStatsInASeparateLoader()
    {
        Hosted((session, app, a, b) =>
        {
            session.Window.PressKey(KeyCode.Digit2);
            session.PumpUntil(() => Commits(app, a.Path) > 0, TimeSpan.FromSeconds(30));
            session.PumpUntil(() => Stats(app, a.Path).Contains(a.Head), TimeSpan.FromSeconds(30));

            var stat = Stats(app, a.Path)[a.Head]!;
            var added = (int)stat.GetType().GetField("Item1")!.GetValue(stat)!;
            Assert.True(added > 0, "the head commit's added-line count did not arrive");
        });
    }

    /// <summary>The Branches tab has no load of its own: once the sidebar's summary is in,
    /// opening the tab has rows on its first frame.</summary>
    [Fact]
    public void TheBranchesTabOpensOnTheListTheSidebarAlreadyFetched()
    {
        Hosted((session, app, a, b) =>
        {
            session.Window.PressKey(KeyCode.Digit2);
            session.PumpUntil(() => SummaryBranches(app, a.Path) > 0, TimeSpan.FromSeconds(30));

            session.Window.PressKey(KeyCode.Digit5);
            session.PumpFrame();

            Assert.Equal(JunctionView.Branches, app.View);
            var rows = (ICollection)typeof(JunctionApp).GetMethod("BranchRows", Priv)!.Invoke(app, null)!;
            Assert.True(rows.Count >= 2, $"expected main and topic, saw {rows.Count} rows");
        });
    }

    // ── Reflection into the loaders ──────────────────────────────────────

    private static object? Field(JunctionApp app, string name) =>
        typeof(JunctionApp).GetField(name, Priv)!.GetValue(app);

    /// <summary>The loaded value of the per-repo loader slot for <paramref name="path"/>.
    /// Asking for the slot creates it when missing, which is what the app does too.</summary>
    private static object LoaderValue(JunctionApp app, string loaders, string path)
    {
        var keyed = Field(app, loaders)!;
        var loader = keyed.GetType().GetMethod("For")!.Invoke(keyed, [path])!;
        return loader.GetType().GetProperty("Value")!.GetValue(loader)!;
    }

    private static int Count(object value, string member) =>
        ((ICollection)value.GetType().GetProperty(member)!.GetValue(value)!).Count;

    private static int Commits(JunctionApp app, string path) =>
        Count(LoaderValue(app, "_historyLoaders", path), "Commits");

    private static int Unstaged(JunctionApp app, string path) =>
        Count(LoaderValue(app, "_changesLoaders", path), "Unstaged");

    private static int Tags(JunctionApp app, string path) =>
        Count(LoaderValue(app, "_tagsLoaders", path), "All");

    private static IDictionary Stats(JunctionApp app, string path) =>
        (IDictionary)LoaderValue(app, "_historyStatLoaders", path);

    private static int SummaryBranches(JunctionApp app, string path) =>
        Count(LoaderValue(app, "_summaryLoaders", path), "Branches");

    private static int DetailFiles(JunctionApp app)
    {
        var loader = Field(app, "_commitDetail")!;
        var value = loader.GetType().GetProperty("Value")!.GetValue(loader)!;
        return Count(value, "Files");
    }

    private static void SelectRepo(JunctionApp app, string path) =>
        typeof(JunctionApp).GetMethod("SelectRepo", Priv)!.Invoke(app, [path]);

    // ── Harness ──────────────────────────────────────────────────────────

    /// <summary>Two repositories in one workspace, opened on the first. The second has a
    /// tag, a second branch and an untracked file, so every preloaded view has something
    /// to show for itself.</summary>
    private static void Hosted(Action<HeadlessSession, JunctionApp, TempRepo, TempRepo> body)
    {
        using var a = TempRepo.CreateWithInitialCommit();
        a.Write("src/Program.cs", "class Program { static void Main() { } }\n");
        a.Git("add", "-A");
        a.Git("commit", "-m", "add the source");
        a.Git("branch", "topic");

        using var b = TempRepo.CreateWithInitialCommit();
        b.Write("lib/Helper.cs", "class Helper { }\n");
        b.Git("add", "-A");
        b.Git("commit", "-m", "add the helper");
        b.Git("tag", "v1");
        b.Write("notes.txt", "not yet added\n");

        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [a.Path, b.Path],
                ActiveRepoPath = a.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [a.Path, b.Path] }],
                ActiveWorkspaceId = "w1",
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            body(session, app, a, b);
        }
        finally { app?.Dispose(); }
    }
}

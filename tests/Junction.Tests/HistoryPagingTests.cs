using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The History list loads the log a page at a time — the log used to stop at 500 commits,
/// and a repository's history does not.
///
/// <para>The page is shrunk to twenty here so a repository of forty-five commits pages
/// three times; everything else is what a person does — switching to History, pressing End,
/// typing a search — and what they read: the count in the include bar.</para>
/// </summary>
public class HistoryPagingTests
{
    private const int Commits = 45;
    private const int Page = 20;

    /// <summary>Scrolling to the end of what is loaded asks for the next page; the last page
    /// comes up short, and that is how the list learns the log is exhausted.</summary>
    [Fact]
    public void TheLogGrowsAPageAtATimeAsItsEndComesOnScreen()
    {
        Hosted((repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit2);
            Assert.Equal(JunctionView.History, app.View);
            PumpUntil(session, () => Loaded(app) == Page, () => "the first page did not load: " + Describe(app));
            session.PumpFrames(6);
            Assert.Equal(Page, Loaded(app));   // the end is off screen: nothing more asked for
            Assert.False(Exhausted(app));
            Assert.True(Shows(session, "newest 20 commits"), "the count did not say it was a window");

            session.Window.PressKey(KeyCode.End);   // the last loaded row comes on screen
            PumpUntil(session, () => Loaded(app) == 2 * Page, "the second page did not load");
            Assert.True(Shows(session, "newest 40 commits"));

            session.Window.PressKey(KeyCode.End);
            PumpUntil(session, () => Exhausted(app), "the log did not run out");
            Assert.Equal(Commits + 1, Loaded(app));   // plus the initial commit
            Assert.True(Shows(session, "46 commits"), "an exhausted log should count itself plainly");
        });
    }

    /// <summary>The selection made on a page survives the next page landing behind it.</summary>
    [Fact]
    public void TheSelectionStaysWhereItWasWhenAPageLands()
    {
        Hosted((repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit2);
            PumpUntil(session, () => Loaded(app) == Page, "the first page did not load");

            session.Window.PressKey(KeyCode.End);
            session.PumpFrames(2);
            var last = SelectedHash(app);
            Assert.Equal(CommitsLoaded(app)[Page - 1].Hash, last);

            PumpUntil(session, () => Loaded(app) == 2 * Page, "the second page did not load");
            Assert.Equal(last, SelectedHash(app));
        });
    }

    /// <summary>"Load all" keeps asking for the next page as each lands, until there is
    /// none — the streaming shape, so the count grows on screen rather than the list
    /// freezing on a single long load.</summary>
    [Fact]
    public void LoadAllKeepsLoadingUntilTheLogRunsOut()
    {
        Hosted((repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit2);
            PumpUntil(session, () => Loaded(app) == Page, "the first page did not load");

            Call(app, "RequestMoreHistory", true);
            session.PumpFrames(2);
            PumpUntil(session, () => Exhausted(app), "load-all did not reach the end");
            Assert.Equal(Commits + 1, Loaded(app));
        });
    }

    /// <summary>
    /// A search filters what is loaded, and does not by itself load the rest: a search
    /// narrows the list until its end is on screen from the start, and that would page in
    /// the whole history without anyone asking. The count says how much was searched, and
    /// "Search all history" is the ask.
    /// </summary>
    [Fact]
    public void ASearchDoesNotLoadOlderPagesUntilAsked()
    {
        Hosted((repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit2);
            PumpUntil(session, () => Loaded(app) == Page, "the first page did not load");

            session.Window.PressKey(KeyCode.Slash);
            session.PumpFrames(2);
            session.Window.TypeText("commit 3");   // commit 3, and commit 30..39
            session.PumpFrames(6);

            Assert.Equal(Page, Loaded(app));
            Assert.True(Shows(session, "10 of newest 20"), "the count did not say how much was searched");

            Call(app, "RequestMoreHistory", true);
            PumpUntil(session, () => Exhausted(app), "search-all did not reach the end");
            session.PumpFrames(2);
            Assert.True(Shows(session, "11 of 46 commits"), "the older match did not appear");
        });
    }

    /// <summary>The +/- counts follow every page, not just the first, and are kept by
    /// hash.</summary>
    [Fact]
    public void TheCountsFollowEveryPage()
    {
        Hosted((repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit2);
            PumpUntil(session, () => Loaded(app) == Page, "the first page did not load");
            Call(app, "RequestMoreHistory", true);
            PumpUntil(session, () => Exhausted(app), "load-all did not reach the end");

            var oldest = CommitsLoaded(app)[^2];   // the first "commit N": one line added
            PumpUntil(session, () => Stats(app).ContainsKey(oldest.Hash), "the last page's counts never came");
            Assert.Equal((1, 0), Stats(app)[oldest.Hash]);
            Assert.Equal((1, 0), Stats(app)[CommitsLoaded(app)[0].Hash]);
        });
    }

    /// <summary>A reveal of a commit beyond the pages loaded keeps loading until the log
    /// holds it, then selects it — rather than reporting that a commit which certainly
    /// exists is not in the history.</summary>
    [Fact]
    public void RevealingACommitBeyondTheLoadedPagesLoadsUntilItIsFound()
    {
        Hosted((repo, session, app) =>
        {
            var oldest = repo.Git("rev-list", "--max-parents=0", "HEAD").Stdout.Trim();
            session.Window.PressKey(KeyCode.Digit2);
            PumpUntil(session, () => Loaded(app) == Page, "the first page did not load");

            Call(app, "RevealCommitInHistory", oldest);
            PumpUntil(session, () => SelectedHash(app) == oldest, "the reveal never reached the commit");
            Assert.True(Loaded(app) > Page);
        });
    }

    // ── reaching the state ───────────────────────────────────────────────

    private static object History(JunctionApp app) =>
        typeof(JunctionApp).GetProperty("_history", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    private static object HistoryValue(JunctionApp app) =>
        History(app).GetType().GetProperty("Value")!.GetValue(History(app))!;

    private static IReadOnlyList<Git.GitCommit> CommitsLoaded(JunctionApp app) =>
        (IReadOnlyList<Git.GitCommit>)HistoryValue(app).GetType().GetProperty("Commits")!.GetValue(HistoryValue(app))!;

    private static int Loaded(JunctionApp app) => CommitsLoaded(app).Count;

    private static bool Exhausted(JunctionApp app) =>
        (bool)HistoryValue(app).GetType().GetProperty("Exhausted")!.GetValue(HistoryValue(app))!;

    private static Dictionary<string, (int Added, int Deleted)> Stats(JunctionApp app)
    {
        var loader = typeof(JunctionApp).GetProperty("_historyStats", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;
        return (Dictionary<string, (int Added, int Deleted)>)loader.GetType().GetProperty("Value")!.GetValue(loader)!;
    }

    private static string? SelectedHash(JunctionApp app) =>
        (string?)typeof(JunctionApp).GetField("_selectedCommitHash", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app);

    private static void Call(JunctionApp app, string method, params object[] args) =>
        typeof(JunctionApp).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, args);

    private static bool Shows(HeadlessSession session, string text) =>
        LayoutProbe.Descendants<TextElement>(Root(session),
            t => t.Content == text && t.Layout.AbsoluteBorderBox.Height > 0).Count > 0;

    private static void PumpUntil(HeadlessSession session, Func<bool> condition, string message) =>
        PumpUntil(session, condition, () => message);

    private static void PumpUntil(HeadlessSession session, Func<bool> condition, Func<string> message)
    {
        try
        {
            session.PumpUntil(condition, TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException($"{message()}.\n{e.Message}\n"
                + session.Window.DumpAccessibilityTree());
        }
    }

    private static string Describe(JunctionApp app)
    {
        var value = HistoryValue(app);
        var page = typeof(JunctionApp).GetField("_historyPageCommits", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app);
        return $"loaded={Loaded(app)} depth={value.GetType().GetProperty("Depth")!.GetValue(value)} "
             + $"exhausted={Exhausted(app)} page={page} key={value.GetType().GetProperty("Key")!.GetValue(value)}";
    }

    // ── harness ──────────────────────────────────────────────────────────

    /// <summary>A repository of forty-five commits ("commit 1" … "commit 45", each adding a
    /// line) on top of the initial one, hosted with the page shrunk to twenty.</summary>
    private static void Hosted(Action<TempRepo, HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        for (var i = 1; i <= Commits; i++)
        {
            repo.Write("log.txt", string.Concat(Enumerable.Range(1, i).Select(n => $"line {n}\n")));
            repo.Git("add", "-A");
            repo.Git("commit", "-m", $"commit {i}");
        }

        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            // The page goes in through the constructor: the first page loads there.
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [repo.Path],
                ActiveRepoPath = repo.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
                ActiveWorkspaceId = "w1",
            }, historyPageCommits: Page);
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            body(repo, session, app);
        }
        finally { app?.Dispose(); }
    }

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);
}

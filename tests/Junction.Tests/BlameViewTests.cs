using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Blame in the Files view.
///
/// <para>The parse has its own tests below against a repository where two commits each own
/// known lines. The view's half is about the two things that are easy to get wrong and
/// invisible until someone uses it: that switching mode actually re-renders (the loader is
/// only subscribed on a tracked rebuild, so a MarkDirty switch leaves the pane on
/// "Blaming…" forever), and that a row leads to the commit it names.</para>
/// </summary>
[Collection("JunctionDiff")]
public class BlameViewTests
{
    // ── The backend, against a real repository ───────────────────────────

    [Fact]
    public void BlameAttributesEachLineToTheCommitThatWroteIt()
    {
        using var repo = TwoAuthorRepo();

        var blame = repo.Repo.Blame("story.txt");

        Assert.Equal(4, blame.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, blame.Select(b => b.LineNumber).ToArray());
        Assert.Equal(new[] { "first line", "second line", "third line", "fourth line" },
            blame.Select(b => b.Content).ToArray());
        // The first two lines came in the root commit, the last two in the follow-up.
        Assert.Equal(blame[0].CommitHash, blame[1].CommitHash);
        Assert.Equal(blame[2].CommitHash, blame[3].CommitHash);
        Assert.NotEqual(blame[0].CommitHash, blame[2].CommitHash);
    }

    /// <summary>Porcelain output names the author once per commit and then refers back to the
    /// hash, so every line after the first of a run depends on the parser having remembered
    /// it. A parser that only read the headers leaves those lines blank.</summary>
    [Fact]
    public void EveryLineCarriesItsAuthorEvenWhenGitOnlyNamedItOnce()
    {
        using var repo = TwoAuthorRepo();

        var blame = repo.Repo.Blame("story.txt");

        Assert.All(blame, b => Assert.False(string.IsNullOrEmpty(b.AuthorName)));
        Assert.All(blame, b => Assert.NotEqual(DateTimeOffset.MinValue, b.AuthorDate));
        Assert.All(blame, b => Assert.Equal(7, b.ShortHash.Length));
        Assert.Equal("second pass", blame[3].Summary);
    }

    /// <summary>Blaming at a commit-ish answers for the file as it was then — the later lines
    /// simply are not there yet.</summary>
    [Fact]
    public void BlamingAnEarlierCommitAnswersForThatRevision()
    {
        using var repo = TwoAuthorRepo();
        var root = repo.Git("rev-list", "--max-parents=0", "HEAD").Stdout.Trim();

        var blame = repo.Repo.Blame("story.txt", root);

        Assert.Equal(2, blame.Count);
        Assert.Equal("second line", blame[1].Content);
    }

    [Fact]
    public void BlamingSomethingGitCannotAnswerForReturnsNothing()
    {
        using var repo = TwoAuthorRepo();
        Assert.Empty(repo.Repo.Blame("no-such-file.txt"));
    }

    // ── The view ─────────────────────────────────────────────────────────

    /// <summary>The toggle switches mode and the blame reaches the screen.</summary>
    [Fact]
    public void TogglingBlameLoadsItAndTheViewFollows()
    {
        Hosted((repo, session, app) =>
        {
            Assert.False(BlameOn(app));

            Toggle(app);
            Assert.True(BlameOn(app));

            session.PumpUntil(() => BlameLines(app, repo).Count > 0, TimeSpan.FromSeconds(30));
            session.PumpFrames(2);

            // Not "a frame happened": the rows themselves have to have reached the tree, and
            // a blame row's accessible name is the only form off-screen rows ever take.
            Assert.Contains("third line", Dump(session), StringComparison.Ordinal);
            Assert.Contains("line 3, ", Dump(session), StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Switching into blame goes through the tracked rebuild, not <c>MarkDirty</c>.
    ///
    /// <para>This is the app's standing trap: an untracked rebuild reads a newly activated
    /// per-file loader without subscribing to it, so the async result lands unobserved and
    /// the pane sits on "Blaming…" until some unrelated rebuild happens along. The assertion
    /// is on the revision signal rather than on rows appearing, because a test that pumps
    /// until the rows show up supplies exactly the rebuilds the bug removes — swapping this
    /// Rerender for a MarkDirty leaves every screen-level assertion in this file green.</para>
    /// </summary>
    [Fact]
    public void TheBlameToggleGoesThroughTheTrackedRebuild()
    {
        Hosted((repo, session, app) =>
        {
            int before = Revision(app);
            Toggle(app);
            Assert.Equal(before + 1, Revision(app));

            // And the context-menu route, which changes the open file at the same time.
            before = Revision(app);
            BlameTreeFile(app, "story.txt");
            Assert.Equal(before + 1, Revision(app));
        });
    }

    /// <summary>Blame replaces the source body rather than sitting beside it, and the toggle
    /// puts it back. Asserted through what reaches the accessibility tree, since a
    /// <c>Ref&lt;T&gt;</c> is never cleared on unmount and so cannot answer "still there?".</summary>
    [Fact]
    public void BlameReplacesTheSourceBodyAndGivesItBack()
    {
        Hosted((repo, session, app) =>
        {
            Assert.DoesNotContain("line 1, ", Dump(session), StringComparison.Ordinal);

            Toggle(app);
            session.PumpUntil(() => BlameLines(app, repo).Count > 0, TimeSpan.FromSeconds(30));
            session.PumpFrames(2);
            Assert.Contains("line 1, ", Dump(session), StringComparison.Ordinal);

            Toggle(app);
            session.PumpFrames(3);
            Assert.DoesNotContain("line 1, ", Dump(session), StringComparison.Ordinal);
        });
    }

    /// <summary>A blame row leads to its commit, in the Commit view — the destination a
    /// History double-click reaches.</summary>
    [Fact]
    public void FollowingABlameRowSelectsThatCommit()
    {
        Hosted((repo, session, app) =>
        {
            Toggle(app);
            session.PumpUntil(() => BlameLines(app, repo).Count > 0, TimeSpan.FromSeconds(30));

            var line = BlameLines(app, repo)[3];   // written by the second commit
            OpenBlameCommit(app, line.CommitHash);
            session.PumpFrames(2);

            Assert.Equal(JunctionView.Commit, app.View);
            Assert.Equal(line.CommitHash, SelectedHash(app));
        });
    }

    /// <summary>An uncommitted line has an all-zero hash and nothing to open. Following it
    /// would leave the Commit view resolving a hash git will not answer for.</summary>
    [Fact]
    public void AnUncommittedLineGoesNowhere()
    {
        Hosted((repo, session, app) =>
        {
            OpenBlameCommit(app, new string('0', 40));
            session.PumpFrames(2);

            Assert.Equal(JunctionView.Files, app.View);
        });
    }

    /// <summary>Ctrl+F searches the file's text, which blame does not show — so it comes back
    /// to the source rather than opening a bar with nothing behind it.</summary>
    [Fact]
    public void OpeningFindLeavesBlame()
    {
        Hosted((repo, session, app) =>
        {
            Toggle(app);
            session.PumpFrames(2);
            Assert.True(BlameOn(app));

            session.Window.PressKey(KeyCode.F, KeyModifiers.Ctrl);
            session.PumpFrames(2);

            Assert.False(BlameOn(app));
        });
    }

    // ── Reaching the app's state ─────────────────────────────────────────

    private static bool BlameOn(JunctionApp app) =>
        (bool)typeof(JunctionApp).GetField("_filesBlame", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    private static void Toggle(JunctionApp app) =>
        typeof(JunctionApp).GetMethod("ToggleFilesBlame", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, null);

    /// <summary>The tracked-rebuild counter <c>Rerender</c> bumps and <c>MarkDirty</c> does
    /// not — the one deterministic way to tell the two apart from outside.</summary>
    private static int Revision(JunctionApp app)
    {
        var signal = typeof(JunctionApp)
            .GetField("_revision", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        return (int)signal.GetType().GetProperty("Value")!.GetValue(signal)!;
    }

    private static void BlameTreeFile(JunctionApp app, string path) =>
        typeof(JunctionApp).GetMethod("BlameTreeFile", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, [path]);

    private static void OpenBlameCommit(JunctionApp app, string hash) =>
        typeof(JunctionApp).GetMethod("OpenBlameCommit", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, [hash]);

    private static string? SelectedHash(JunctionApp app) =>
        (string?)typeof(JunctionApp).GetField("_selectedCommitHash", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app);

    /// <summary>
    /// What the view's own loader holds, so a test can wait on the async blame landing
    /// without rebuilding the tree to make it land.
    ///
    /// <para>The key is rebuilt from the app's fields rather than assumed. The Files view
    /// browses <c>_selectedCommitHash ?? "HEAD"</c>, and History fills that hash in from a
    /// background load — so hard-coding "HEAD" makes the lookup a race with how quickly the
    /// commit list came back, which is exactly how it first behaved.</para>
    /// </summary>
    private static IReadOnlyList<GitBlameLine> BlameLines(JunctionApp app, TempRepo repo)
    {
        var reference = (string?)typeof(JunctionApp)
            .GetField("_selectedCommitHash", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app) ?? "HEAD";
        var loaders = typeof(JunctionApp)
            .GetField("_blameLoaders", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        var loader = loaders.GetType().GetMethod("For")!
            .Invoke(loaders, [$"{repo.Path}|{reference}|story.txt"])!;
        return (IReadOnlyList<GitBlameLine>)loader.GetType().GetProperty("Value")!.GetValue(loader)!;
    }

    private static TextAreaElement? BodyOrNull(JunctionApp app) =>
        ((HultUI.Core.Reactivity.Ref<TextAreaElement>)typeof(JunctionApp)
            .GetField("_fileBodyRef", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!).Current;

    private static string Dump(HeadlessSession session) => session.Window.DumpAccessibilityTree();

    /// <summary>Four lines over two commits by two authors, so a run boundary and an author
    /// change both fall in the middle of the file.</summary>
    private static TempRepo TwoAuthorRepo()
    {
        var repo = TempRepo.CreateEmpty();
        repo.Write("story.txt", "first line\nsecond line\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "first pass");

        repo.Git("config", "user.name", "Second Author");
        repo.Git("config", "user.email", "second@example.invalid");
        repo.Write("story.txt", "first line\nsecond line\nthird line\nfourth line\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "second pass");
        return repo;
    }

    private static void Hosted(Action<TempRepo, HeadlessSession, JunctionApp> body)
    {
        using var repo = TwoAuthorRepo();
        using var session = HeadlessSession.Create(1400, 900);
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
            session.PumpFrames(3);

            session.Window.PressKey(KeyCode.Digit7);   // Files
            session.PumpUntil(() => app!.View == JunctionView.Files, TimeSpan.FromSeconds(20));

            SelectFile(app, "story.txt");
            // The tree walk and the file read are both off-thread; wait for the body rather
            // than a frame count, or every assertion races an empty pane.
            session.PumpUntil(() => BodyOrNull(app) is { } b && b.Text.Length > 0, TimeSpan.FromSeconds(30));
            session.PumpFrames(2);

            body(repo, session, app!);
        }
        finally { app?.Dispose(); }
    }

    private static void SelectFile(JunctionApp app, string path)
    {
        typeof(JunctionApp).GetField("_filesSelPath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(app, path);
        typeof(JunctionApp).GetMethod("MarkDirty",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.Invoke(app, null);
    }
}

using System.Collections;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// What a change on disk does to the selection in each view, hosted headlessly.
///
/// <para>The watcher's callback is invoked directly rather than through a
/// <c>FileSystemWatcher</c>: the debounce is real time and the events are the OS's, and
/// neither is what is under test. Everything from the callback on — the generation bump,
/// the reload, the callback that picks a selection — is the production path.</para>
///
/// <para>A "refused" reload stands for the case that moved the selection in the field:
/// <c>git diff</c> dying with "cannot hash" on a file its writer still had open. Locking a
/// file only refuses git on Windows, so the tests refuse it portably with a broken
/// <c>.git/config</c> instead — every command fails the same way, and the fix does not care
/// which one did.</para>
/// </summary>
public class ExternalChangeTests
{
    private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    // ── History ──────────────────────────────────────────────────────────

    [Fact]
    public void AnExternalCommitKeepsTheSelectedCommit()
    {
        Hosted(repo =>
        {
            Commit(repo, "second");
            Commit(repo, "third");
        }, (repo, session, app) =>
        {
            var selected = OpenHistoryOnTheSecondCommit(repo, session, app);

            Commit(repo, "fourth");
            ExternalChange(app, repo);
            PumpUntil(session, () => Commits(app, repo.Path) == 4, "the external commit never showed");

            Assert.Equal(selected, SelectedCommit(app));
        });
    }

    [Fact]
    public void ARefusedReloadKeepsTheHistoryAndItsSelectionUntilGitAnswersAgain()
    {
        Hosted(repo =>
        {
            Commit(repo, "second");
            Commit(repo, "third");
        }, (repo, session, app) =>
        {
            var selected = OpenHistoryOnTheSecondCommit(repo, session, app);

            Commit(repo, "fourth");
            RefuseGit(repo);
            ExternalChange(app, repo);
            session.PumpFrames(6);

            // The log git refused is not an empty log: the three commits stay up and the
            // selection with them. (It used to land as "no commits", drop the selection,
            // and let the next load open on the newest commit.)
            Assert.Equal(3, Commits(app, repo.Path));
            Assert.Equal(selected, SelectedCommit(app));
            Assert.True(Shows(session, "third"), "the commit list blanked on a refused reload");

            // Once git answers again the refused load is retried on its own — the event
            // that raced the writer may have been the last one.
            RestoreGit(repo);
            PumpUntil(session, () => Commits(app, repo.Path) == 4, "the refused load was never retried");
            Assert.Equal(selected, SelectedCommit(app));
        });
    }

    // ── Changes ──────────────────────────────────────────────────────────

    [Fact]
    public void AWriteElsewhereKeepsTheSelectedChange()
    {
        Hosted(repo =>
        {
            repo.Write("a.txt", "a\n");
            repo.Write("b.txt", "b\n");
        }, (repo, session, app) =>
        {
            OpenChangesOnTheSecondFile(session, app);

            repo.Write("c.txt", "c\n");
            ExternalChange(app, repo);
            PumpUntil(session, () => Unstaged(app, repo.Path) == 3, "the new file never showed");

            Assert.Equal("b.txt", CaretPath(app));
        });
    }

    [Fact]
    public void ARefusedReloadKeepsTheSelectedChangeUntilGitAnswersAgain()
    {
        Hosted(repo =>
        {
            repo.Write("a.txt", "a\n");
            repo.Write("b.txt", "b\n");
        }, (repo, session, app) =>
        {
            OpenChangesOnTheSecondFile(session, app);

            repo.Write("c.txt", "c\n");
            RefuseGit(repo);
            ExternalChange(app, repo);
            session.PumpFrames(6);

            Assert.Equal(2, Unstaged(app, repo.Path));
            Assert.Equal("b.txt", CaretPath(app));

            RestoreGit(repo);
            PumpUntil(session, () => Unstaged(app, repo.Path) == 3, "the refused load was never retried");
            Assert.Equal("b.txt", CaretPath(app));
        });
    }

    /// <summary>Staging from a terminal or an editor writes only the index, and Changes stayed as
    /// it was until something else changed (plan 17 round 14: `git add`, `restore --staged` and a
    /// staged submodule bump). The real watcher, real git.</summary>
    [Fact]
    public void AStageMadeOutside_ReachesChanges_AndSoDoesAnUnstage()
    {
        Hosted(repo =>
        {
            repo.Write("README.md", "changed\n");
            repo.Settle();
        }, (repo, session, app) =>
        {
            var path = repo.Path;
            session.Window.PressKey(KeyCode.Digit4);
            PumpUntil(session, () => Unstaged(app, path) == 1, "the Changes view never loaded the edit");
            Quiet(session, app, path);

            repo.Git("add", "README.md");
            PumpUntil(session, () => Staged(app, path) == 1 && Unstaged(app, path) == 0,
                "the stage made outside never reached Changes");
            Quiet(session, app, path);

            repo.Git("restore", "--staged", "README.md");
            PumpUntil(session, () => Staged(app, path) == 0 && Unstaged(app, path) == 1,
                "the unstage made outside never reached Changes");
        });
    }

    // ── Files ────────────────────────────────────────────────────────────

    [Fact]
    public void AChangeOnDiskLeavesTheFilesTreeAsItWas()
    {
        Hosted(repo =>
        {
            repo.Write("kept.cs", "class Kept { }\n");
            repo.Write("sub/nested.cs", "class Nested { }\n");
            Commit(repo, "three paths");
        }, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit7);   // Files
            PumpUntil(session, () => FilesSelection(app) != null, "the Files view never loaded a tree");

            // Park the cursor on the folder (folders sort first) and open it, so both the
            // cursor and an expansion are there to lose.
            session.Window.PressKey(KeyCode.Home);
            session.PumpFrame();
            Assert.Equal("sub", FilesCursor(app));
            session.Window.PressKey(KeyCode.Right);
            session.PumpFrame();
            var rows = FileRows(app);
            Assert.Contains("sub/nested.cs", rows);
            var cursor = FilesCursor(app);
            var selected = FilesSelection(app);
            Assert.Equal("sub", cursor);

            repo.Write("kept.cs", "class Kept { int x; }\n");
            ExternalChange(app, repo);

            // Frame by frame through the reload: the tree it had at every one of them,
            // never an empty one while the new listing is on its way. A fresh loader slot
            // per generation used to blank it for a frame, and that frame reset the cursor
            // and closed the folder.
            for (var frame = 0; frame < 8; frame++)
            {
                session.PumpFrame();
                Assert.Equal(rows, FileRows(app));
                Assert.Equal(cursor, FilesCursor(app));
                Assert.Equal(selected, FilesSelection(app));
            }
        });
    }

    // ── What an event reads again ────────────────────────────────────────

    /// <summary>A file saved in the working tree, as the real watcher reports it, reloads what
    /// reads the working tree — the status, the dirty count — and reads no log, no tags and no
    /// branches again: their loaders keep the key they hold, and the summary keeps the branch
    /// list it had (plan 17 round 12: such an event ran 14 git commands on History, 2 now). A
    /// ref moving reloads all of them.</summary>
    [Fact]
    public void AWorkingTreeEvent_ReadsNoLogOrRefs_AndARefMovingDoes()
    {
        Hosted(repo =>
        {
            Commit(repo, "second");
            repo.Git("tag", "v1");
        }, (repo, session, app) =>
        {
            var path = repo.Path;
            session.Window.PressKey(KeyCode.Digit2);
            PumpUntil(session, () => Commits(app, path) == 2 && LoadedKey(app, "_tagsLoaders", path) != null
                && SummaryMember(app, path, "Branch") is "main", "the History view never loaded");
            Quiet(session, app, path);
            var log = LoadedKey(app, "_historyLoaders", path);
            var tags = LoadedKey(app, "_tagsLoaders", path);
            var branches = SummaryMember(app, path, "Branches");
            int gen = DataGeneration(app, path);

            repo.Write("edited.txt", "x\n");
            PumpUntil(session, () => DataGeneration(app, path) > gen && SummaryMember(app, path, "Dirty") is 1,
                "the saved file never reached the summary");
            Quiet(session, app, path);
            Assert.Equal(log, LoadedKey(app, "_historyLoaders", path));
            Assert.Equal(tags, LoadedKey(app, "_tagsLoaders", path));
            Assert.Same(branches, SummaryMember(app, path, "Branches"));

            repo.Git("branch", "zeta");
            PumpUntil(session, () => ((IEnumerable)SummaryMember(app, path, "Branches")!).Cast<object>()
                .Any(b => (string)b.GetType().GetProperty("Name")!.GetValue(b)! == "zeta"), "the new branch never showed");
            PumpUntil(session, () => LoadedKey(app, "_historyLoaders", path) != log && LoadedKey(app, "_tagsLoaders", path) != tags,
                "a ref moving did not read the log and the tags again");
        });
    }

    /// <summary>A reftable repository keeps HEAD's branch and every ref in .git/reftable, which
    /// the watcher did not watch: a switch made outside reached it only as the working-tree files
    /// it changed, a working-tree event, and the branch label, the branch list and the log kept
    /// what they had until a commit (plan 17 round 13). It is a ref moving now.</summary>
    [ReftableFact]
    public void OnAReftableRepository_AnOutsideSwitch_ShowsTheNewBranch()
    {
        Hosted(repo =>
        {
            Commit(repo, "second");
            repo.Git("branch", "other", "HEAD~1");
        }, (repo, session, app) =>
        {
            var path = repo.Path;
            session.Window.PressKey(KeyCode.Digit2);
            PumpUntil(session, () => Commits(app, path) == 2 && SummaryMember(app, path, "Branch") is "main",
                "the History view never loaded");
            Quiet(session, app, path);
            var log = LoadedKey(app, "_historyLoaders", path);

            repo.Git("switch", "-q", "other");   // second.txt leaves the working tree too
            PumpUntil(session, () => SummaryMember(app, path, "Branch") is "other",
                "the switch made outside never reached the branch label");
            PumpUntil(session, () => LoadedKey(app, "_historyLoaders", path) != log,
                "the switch made outside did not read the log again");
        }, reftable: true);
    }

    /// <summary>A fetch that brings nothing new still takes a ref's lock (the remote's HEAD, git
    /// 2.53 at least) and deletes it, which the watcher told as a ref moving: the log, the tags and
    /// the branches were read again 300 ms after the fetch, the background fetch's own fingerprint
    /// check having decided not to (plan 17 round 13). Such an event reads the status now; a fetch
    /// that brings a commit still reads them all.</summary>
    [Fact]
    public void ARefTransactionThatChangesNothing_ReadsNoLogOrRefs_AndAFetchThatBringsACommitDoes()
    {
        var remotes = Path.Combine(Path.GetTempPath(), "junction-git-remote-" + Guid.NewGuid().ToString("N"));
        string bare = Path.Combine(remotes, "origin.git"), peer = Path.Combine(remotes, "peer");
        try
        {
            Hosted(repo =>
            {
                Directory.CreateDirectory(bare);
                Assert.True(GitProcess.Run(bare, "init", "--bare", "-b", "main").Success);
                repo.Git("remote", "add", "origin", bare);
                repo.Git("push", "-q", "-u", "origin", "main");
                repo.Git("fetch", "-q", "origin");
                Assert.True(GitProcess.Run(remotes, "clone", "-q", bare, peer).Success);
            }, (repo, session, app) =>
            {
                var path = repo.Path;
                session.Window.PressKey(KeyCode.Digit2);
                PumpUntil(session, () => Commits(app, path) == 1 && LoadedKey(app, "_tagsLoaders", path) != null,
                    "the History view never loaded");
                Quiet(session, app, path);
                var log = LoadedKey(app, "_historyLoaders", path);
                var tags = LoadedKey(app, "_tagsLoaders", path);
                var branches = SummaryMember(app, path, "Branches");
                int gen = DataGeneration(app, path);

                repo.Git("fetch", "--all", "--prune");
                // What that fetch does with the remote's HEAD, by hand, so the event is there
                // whatever this git's version.
                var lockFile = Path.Combine(path, ".git", "refs", "remotes", "origin", "HEAD.lock");
                File.WriteAllText(lockFile, "ref: refs/remotes/origin/main\n");
                File.Delete(lockFile);
                PumpUntil(session, () => DataGeneration(app, path) > gen, "the lock was never heard");
                Quiet(session, app, path);
                Assert.Equal(log, LoadedKey(app, "_historyLoaders", path));
                Assert.Equal(tags, LoadedKey(app, "_tagsLoaders", path));
                Assert.Same(branches, SummaryMember(app, path, "Branches"));

                File.WriteAllText(Path.Combine(peer, "peer.txt"), "peer\n");
                Assert.True(GitProcess.Run(peer, "add", "-A").Success);
                Assert.True(GitProcess.Run(peer, "-c", "user.name=Peer", "-c", "user.email=peer@example.invalid",
                    "commit", "-q", "-m", "from the peer").Success);
                Assert.True(GitProcess.Run(peer, "push", "-q", "origin", "main").Success);
                repo.Git("fetch", "-q", "origin");
                PumpUntil(session, () => LoadedKey(app, "_historyLoaders", path) != log && LoadedKey(app, "_tagsLoaders", path) != tags
                    && !ReferenceEquals(SummaryMember(app, path, "Branches"), branches), "a fetch that brought a commit did not read the refs again");
            });
        }
        finally { TempRepo.ForceDelete(remotes); }
    }

    /// <summary>A move that writes only a reflog entry — <c>merge --abort</c>'s reset, a reset to
    /// where HEAD is — shows the watcher a ref's lock file and nothing else, a working-tree event,
    /// so the reflog keys on the data generation: an open reflog takes the new entry.</summary>
    [Fact]
    public void AMoveThatWritesOnlyTheReflog_ReachesTheOpenReflog()
    {
        Hosted(_ => { }, (repo, session, app) =>
        {
            var path = repo.Path;
            typeof(JunctionApp).GetMethod("OpenReflog", Priv)!.Invoke(app, []);
            PumpUntil(session, () => ReflogEntries(app, path) == 1, "the reflog never loaded");
            Quiet(session, app, path);

            repo.Git("reset", "-q", "--hard", "HEAD");
            PumpUntil(session, () => ReflogEntries(app, path) == 2, "the reset's reflog entry never showed");
        });
    }

    // ── The app's own actions ────────────────────────────────────────────

    /// <summary>An action of the app's own that moves a ref reloads once. Its RefreshAll reads the
    /// repository again after everything the watcher had heard while the command ran, so the event
    /// the watcher told 300 ms later read it all a second time (plan 17 round 13: an in-app
    /// checkout ran 28 git commands, 14 of them for that). An outside change made while the command
    /// runs is read by the action's own reload; one made after it is heard as ever.</summary>
    [Fact]
    public void AnInAppCheckout_ReloadsOnce_AndAnOutsideChangeMadeMeanwhileShows() => CheckoutReloadsOnce(reftable: false);

    [ReftableFact]
    public void OnAReftableRepository_AnInAppCheckout_ReloadsOnce_AndAnOutsideChangeMadeMeanwhileShows() => CheckoutReloadsOnce(reftable: true);

    private static void CheckoutReloadsOnce(bool reftable)
    {
        Hosted(repo =>
        {
            Commit(repo, "second");
            repo.Git("branch", "other", "HEAD~1");
        }, (repo, session, app) =>
        {
            var path = repo.Path;
            session.Window.PressKey(KeyCode.Digit2);
            PumpUntil(session, () => Commits(app, path) == 2 && SummaryMember(app, path, "Branch") is "main",
                "the History view never loaded");
            Quiet(session, app, path);
            // The app's watcher, told what it hears as the app's is, with a debounce long enough
            // that when it tells is no race with this test: the command below waits until it has
            // heard everything before it returns.
            var watchers = (IDictionary)Field(app, "_watchers")!;
            ((IDisposable)watchers[path]!).Dispose();
            var watcher = new GitWatcher(path, refs => typeof(JunctionApp)
                .GetMethod(refs ? "OnExternalChange" : "OnWorkingTreeChange", Priv)!.Invoke(app, [path]), debounceMs: 2000);
            watchers[path] = watcher;
            int gen = DataGeneration(app, path);

            // The palette's checkout, with a branch made outside while it runs.
            var git = (GitRepository)typeof(JunctionApp).GetMethod("RepoFor", Priv)!.Invoke(app, [path])!;
            Func<GitResult> command = () =>
            {
                var result = git.Checkout("other");
                GitProcess.Run(path, "branch", "made-meanwhile");
                HeardEverything(watcher);
                return result;
            };
            Action<GitResult> apply = _ => typeof(JunctionApp).GetMethod("RefreshAll", Priv)!.Invoke(app, null);
            typeof(JunctionApp).GetMethod("RunGitCommand", Priv)!.MakeGenericMethod(typeof(GitResult))
                .Invoke(app, [command, apply]);
            PumpUntil(session, () => SummaryMember(app, path, "Branch") is "other" && HasBranch(app, path, "made-meanwhile"),
                "the checkout's reload never showed the checkout and the branch made meanwhile");
            // Past the watcher's debounce: what it heard would have been told by now.
            for (var until = DateTime.UtcNow.AddSeconds(2.5); DateTime.UtcNow < until; Thread.Sleep(5)) session.PumpFrame();
            Quiet(session, app, path);
            Assert.Equal(gen + 1, DataGeneration(app, path));

            repo.Git("branch", "made-after");
            PumpUntil(session, () => HasBranch(app, path, "made-after"), "a branch made after the checkout never showed");
        }, reftable);
    }

    /// <summary>Waits, on the command's thread, until the watcher has heard something and then
    /// nothing more for 300 ms.</summary>
    private static void HeardEverything(GitWatcher watcher)
    {
        var field = typeof(GitWatcher).GetField("_heard", Priv)!;
        long last = (long)field.GetValue(watcher)!, since = Environment.TickCount64;
        var until = Environment.TickCount64 + 10_000;
        while (Environment.TickCount64 < until)
        {
            Thread.Sleep(10);
            long now = (long)field.GetValue(watcher)!;
            if (now != last) { last = now; since = Environment.TickCount64; }
            else if (now > 0 && Environment.TickCount64 - since >= 300) return;
        }
    }

    private static bool HasBranch(JunctionApp app, string path, string name) =>
        ((IEnumerable)SummaryMember(app, path, "Branches")!).Cast<object>()
            .Any(b => (string)b.GetType().GetProperty("Name")!.GetValue(b)! == name);

    private static int ReflogEntries(JunctionApp app, string path) =>
        ((ICollection)LoaderValue(app, "_reflogLoaders", path)).Count;

    /// <summary>Pumps until the repository's loads have landed and a quarter second has passed
    /// with no event, so what a test compares is not still moving.</summary>
    private static void Quiet(HeadlessSession session, JunctionApp app, string path)
    {
        int last = DataGeneration(app, path);
        var quietSince = DateTime.UtcNow;
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < until)
        {
            session.PumpFrame();
            Thread.Sleep(5);
            int now = DataGeneration(app, path);
            if (now != last) { last = now; quietSince = DateTime.UtcNow; }
            else if (DateTime.UtcNow - quietSince > TimeSpan.FromMilliseconds(600) && !Loading(app, path)) return;
        }
    }

    private static bool Loading(JunctionApp app, string path) =>
        new[] { "_summaryLoaders", "_historyLoaders", "_tagsLoaders", "_worktreeLoaders" }.Any(f =>
        {
            var keyed = Field(app, f)!;
            var loader = keyed.GetType().GetMethod("For")!.Invoke(keyed, [path])!;
            return (bool)loader.GetType().GetProperty("IsLoading")!.GetValue(loader)!;
        });

    private static int DataGeneration(JunctionApp app, string path) =>
        ((Dictionary<string, int>)Field(app, "_dataGen")!).GetValueOrDefault(path);

    /// <summary>The key a repository's loader in <paramref name="loaders"/> holds its value for.</summary>
    private static string? LoadedKey(JunctionApp app, string loaders, string path)
    {
        var keyed = Field(app, loaders)!;
        var loader = keyed.GetType().GetMethod("For")!.Invoke(keyed, [path])!;
        return loader.GetType().GetField("_loadedKey", Priv)!.GetValue(loader) as string;
    }

    private static object? SummaryMember(JunctionApp app, string path, string member)
    {
        var summary = LoaderValue(app, "_summaryLoaders", path);
        return summary.GetType().GetProperty(member)!.GetValue(summary);
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static string OpenHistoryOnTheSecondCommit(TempRepo repo, HeadlessSession session, JunctionApp app)
    {
        session.Window.PressKey(KeyCode.Digit2);
        Assert.Equal(JunctionView.History, app.View);
        PumpUntil(session, () => Commits(app, repo.Path) == 3 && Shows(session, "third"),
            "the History view never loaded the log");
        session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();
        var selected = SelectedCommit(app);
        Assert.NotNull(selected);
        Assert.Equal(repo.Git("rev-parse", "HEAD~1").Stdout.Trim(), selected);
        return selected!;
    }

    private static void OpenChangesOnTheSecondFile(HeadlessSession session, JunctionApp app)
    {
        session.Window.PressKey(KeyCode.Digit4);
        Assert.Equal(JunctionView.Changes, app.View);
        PumpUntil(session, () => CaretPath(app) != null, "the Changes view never loaded a file list");
        session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();
        Assert.Equal("b.txt", CaretPath(app));
    }

    private static void Commit(TempRepo repo, string subject)
    {
        repo.Write($"{subject}.txt", $"{subject}\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", subject);
    }

    /// <summary>What the watcher does when the repository changed on disk.</summary>
    private static void ExternalChange(JunctionApp app, TempRepo repo) =>
        typeof(JunctionApp).GetMethod("OnExternalChange", Priv)!.Invoke(app, [repo.Path]);

    private const string BrokenConfig = "\n[broken\n";

    private static string ConfigPath(TempRepo repo) => Path.Combine(repo.Path, ".git", "config");

    /// <summary>Makes git refuse every command in the repository ("bad config line").</summary>
    private static void RefuseGit(TempRepo repo) => File.AppendAllText(ConfigPath(repo), BrokenConfig);

    private static void RestoreGit(TempRepo repo) =>
        File.WriteAllText(ConfigPath(repo), File.ReadAllText(ConfigPath(repo)).Replace(BrokenConfig, ""));

    private static object? Field(JunctionApp app, string name) =>
        typeof(JunctionApp).GetField(name, Priv)!.GetValue(app);

    private static string? SelectedCommit(JunctionApp app) => (string?)Field(app, "_selectedCommitHash");
    private static string? CaretPath(JunctionApp app) => (string?)Field(app, "_changeSelPath");
    private static string? FilesSelection(JunctionApp app) => (string?)Field(app, "_filesSelPath");
    private static string? FilesCursor(JunctionApp app) => (string?)Field(app, "_filesCursorPath");

    /// <summary>The Files tree's visible rows, top to bottom.</summary>
    private static List<string> FileRows(JunctionApp app) =>
        ((IEnumerable)Field(app, "_filesFlat")!).Cast<object>()
            .Select(n => (string)n.GetType().GetProperty("Path")!.GetValue(n)!)
            .ToList();

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

    private static int Staged(JunctionApp app, string path) =>
        Count(LoaderValue(app, "_changesLoaders", path), "Staged");

    private static bool Shows(HeadlessSession session, string subject) =>
        LayoutProbe.Descendants<TextElement>(Root(session),
            t => t.Content == subject && t.Layout.AbsoluteBorderBox.Height > 0).Count > 0;

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window).GetField("_root", Priv)!.GetValue(session.Window.Window);

    private static void PumpUntil(HeadlessSession session, Func<bool> condition, string message)
    {
        try
        {
            session.PumpUntil(condition, TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException($"{message}.\n{e.Message}\n"
                + session.Window.DumpAccessibilityTree());
        }
    }

    private static void Hosted(Action<TempRepo> arrange, Action<TempRepo, HeadlessSession, JunctionApp> body, bool reftable = false)
    {
        using var repo = TempRepo.CreateWithInitialCommit(reftable);
        arrange(repo);

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
            body(repo, session, app);
        }
        finally { app?.Dispose(); }
    }
}

using System.Reflection;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The worktree layer, against real repositories. It shipped without tests, which is how
/// the two fragile parts of <c>GetWorktrees</c> could regress unnoticed: the Windows path
/// normalization (git prints forward slashes, the rest of the app stores OS-native paths,
/// and a mismatch makes the worktree you are standing in look like someone else's), and
/// the final flush for the last record, which has no trailing blank line to close it.
/// </summary>
public class WorktreeTests
{
    /// <summary>A sibling directory of the fixture, so the sweeper that cleans up
    /// <c>junction-git-*</c> reaches these too if a test dies mid-way.</summary>
    private static string LinkedPathFor(TempRepo repo, string suffix) =>
        Path.Combine(Path.GetDirectoryName(repo.Path)!, Path.GetFileName(repo.Path) + suffix);

    [Fact]
    public void AFreshRepositoryHasExactlyOneWorktreeAndItIsTheMainOne()
    {
        using var repo = TempRepo.CreateWithInitialCommit();

        var wt = Assert.Single(repo.Repo.GetWorktrees());

        Assert.True(wt.IsMain);
        Assert.Equal("main", wt.Branch);
        Assert.Equal("main", wt.RefLabel);
        Assert.False(wt.IsLocked);
        Assert.False(wt.Prunable);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(repo.Path)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(wt.Path)));
        // The normalization itself, which only bites where the two separators differ.
        if (Path.DirectorySeparatorChar != '/')
            Assert.DoesNotContain('/', wt.Path);
    }

    [Fact]
    public void AddingAWorktreeOnANewBranchListsBothAndOnlyTheFirstIsMain()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var linked = LinkedPathFor(repo, "-wt");

        try
        {
            var added = repo.Repo.AddWorktree(linked, newBranch: "feature/side");
            Assert.True(added.Success, added.Stderr);

            var all = repo.Repo.GetWorktrees();

            Assert.Equal(2, all.Count);
            Assert.True(all[0].IsMain);
            // The trailing record has no blank line after it; without the final flush
            // this is the entry that would silently go missing.
            Assert.False(all[1].IsMain);
            Assert.Equal("feature/side", all[1].Branch);
            Assert.Equal("feature/side", all[1].RefLabel);
            Assert.False(all[1].IsDetached);
        }
        finally
        {
            repo.Repo.RemoveWorktree(linked, force: true);
            if (Directory.Exists(linked)) Directory.Delete(linked, recursive: true);
        }
    }

    [Fact]
    public void AWorktreeCheckedOutAtACommitIsDetached()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var linked = LinkedPathFor(repo, "-det");

        try
        {
            Assert.True(repo.Repo.AddWorktree(linked, committish: repo.Head).Success);

            var detached = repo.Repo.GetWorktrees().Single(w => !w.IsMain);

            Assert.True(detached.IsDetached);
            Assert.Null(detached.Branch);
        }
        finally
        {
            repo.Repo.RemoveWorktree(linked, force: true);
            if (Directory.Exists(linked)) Directory.Delete(linked, recursive: true);
        }
    }

    [Fact]
    public void RemovingAWorktreeTakesItOffTheListAndOffDisk()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var linked = LinkedPathFor(repo, "-gone");

        Assert.True(repo.Repo.AddWorktree(linked, newBranch: "scratch").Success);
        Assert.Equal(2, repo.Repo.GetWorktrees().Count);

        var removed = repo.Repo.RemoveWorktree(linked);

        Assert.True(removed.Success, removed.Stderr);
        Assert.Single(repo.Repo.GetWorktrees());
        Assert.False(Directory.Exists(linked));
    }

    [Fact]
    public void AWorktreeWhoseDirectoryVanishedIsPrunableAndPruningClearsIt()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var linked = LinkedPathFor(repo, "-stale");

        Assert.True(repo.Repo.AddWorktree(linked, newBranch: "stale").Success);
        // Pulled out from under git — exactly what the "prunable" badge is there to report.
        Directory.Delete(linked, recursive: true);

        Assert.True(repo.Repo.GetWorktrees().Single(w => !w.IsMain).Prunable);

        Assert.True(repo.Repo.PruneWorktrees().Success);
        Assert.Single(repo.Repo.GetWorktrees());
    }

    /// <summary>A lock is what you put on a checkout that lives on a drive which is not
    /// always mounted, so git stops calling it prunable. It also blocks removal outright —
    /// <c>--force</c> included — which is why the panel shows the lock and its reason, and
    /// offers a way to lift it.</summary>
    [Fact]
    public void ALockedWorktreeCarriesItsReasonAndRefusesToBeRemovedUntilUnlocked()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var linked = LinkedPathFor(repo, "-locked");

        try
        {
            Assert.True(repo.Repo.AddWorktree(linked, newBranch: "locked-branch").Success);
            Assert.True(repo.Repo.LockWorktree(linked, "on the external drive").Success);

            var locked = repo.Repo.GetWorktrees().Single(w => !w.IsMain);
            Assert.True(locked.IsLocked);
            Assert.Equal("on the external drive", locked.LockReason);

            Assert.False(repo.Repo.RemoveWorktree(linked, force: true).Success);

            Assert.True(repo.Repo.UnlockWorktree(linked).Success);
            Assert.False(repo.Repo.GetWorktrees().Single(w => !w.IsMain).IsLocked);
            Assert.True(repo.Repo.RemoveWorktree(linked).Success);
            Assert.Single(repo.Repo.GetWorktrees());
        }
        finally
        {
            repo.Repo.UnlockWorktree(linked);
            repo.Repo.RemoveWorktree(linked, force: true);
            if (Directory.Exists(linked)) Directory.Delete(linked, recursive: true);
        }
    }

    // ── The panel's half of the bargain ──────────────────────────────────
    //
    // Opening a worktree registers nothing: the sidebar derives worktrees from
    // `git worktree list` and nests them under the repository. So what a removal or a prune
    // has to take away is the git-side entry, and what a *failed* one must leave alone is
    // the row that still reaches the worktree. Older settings files can hold a worktree as
    // a repository of its own, and the cleanup has to reach those too — the symptom when it
    // does not is a sidebar row pointing at a folder that is gone, which nothing else in
    // the app ever tidies up.

    /// <summary>Opening a worktree switches to it and leaves the settings file alone. It
    /// used to add the path as a repository of its own, and the sidebar then carried two
    /// entries sharing one name, one history and one set of remotes.</summary>
    [Fact]
    public void OpeningAWorktreeSwitchesToItWithoutRegisteringIt()
    {
        WithApp((repo, session, app, settings, linked) =>
        {
            Invoke(app, "OpenWorktree", linked);
            session.PumpFrames(2);

            Assert.True(Same(ActiveRepoPath(app), linked));
            Assert.DoesNotContain(settings.RepositoryPaths, p => Same(p, linked));
            Assert.DoesNotContain(settings.Workspaces[0].RepoPaths, p => Same(p, linked));
        });
    }

    /// <summary>An older settings file can still hold the worktree as a repository of its
    /// own. Removing the worktree has to take that stale entry with it, or the sidebar keeps
    /// a row pointing at a folder that is gone.</summary>
    [Fact]
    public void RemovingAWorktreeAlsoRemovesAStaleRepositoryRegistration()
    {
        WithApp((repo, session, app, settings, linked) =>
        {
            RegisterAsRepository(settings, linked);
            // Standing on the main worktree: git refuses to remove the tree you are in, and
            // the panel hides Remove on that row for the same reason.
            Invoke(app, "SelectRepo", repo.Path);
            session.PumpFrames(2);

            Invoke(app, "RemoveWorktreeAction", linked, false);
            session.PumpFrames(2);

            Assert.Single(repo.Repo.GetWorktrees());
            Assert.DoesNotContain(settings.RepositoryPaths, p => Same(p, linked));
            Assert.DoesNotContain(settings.Workspaces[0].RepoPaths, p => Same(p, linked));
        });
    }

    /// <summary>A failed removal must take nothing away: the worktree is still there, and
    /// the row that reaches it has to stay on screen.</summary>
    [Fact]
    public void AWorktreeThatRefusesToBeRemovedKeepsItsRow()
    {
        WithApp((repo, session, app, settings, linked) =>
        {
            PumpUntilNestedRow(session, app, linked);
            Assert.True(repo.Repo.LockWorktree(linked, "held").Success);

            Invoke(app, "RemoveWorktreeAction", linked, true);   // locked: git refuses
            session.PumpFrames(4);

            Assert.Equal(2, repo.Repo.GetWorktrees().Count);
            Assert.True(HasNestedRow(app, linked), "the row that reaches the worktree is gone");

            repo.Repo.UnlockWorktree(linked);
        });
    }

    [Fact]
    public void PruningForgetsStaleRegistrationsOfTheWorktreesItDropped()
    {
        WithApp((repo, session, app, settings, linked) =>
        {
            RegisterAsRepository(settings, linked);
            Invoke(app, "SelectRepo", repo.Path);
            session.PumpFrames(2);
            // Pulled out from under git — the state the Prune button exists for. Forced,
            // because the app is watching this directory while the test deletes it.
            Assert.True(TempRepo.ForceDelete(linked), "the fixture worktree would not delete");

            Invoke(app, "PruneWorktreesAction");
            session.PumpFrames(2);

            Assert.Single(repo.Repo.GetWorktrees());
            Assert.DoesNotContain(settings.RepositoryPaths, p => Same(p, linked));
            Assert.DoesNotContain(settings.Workspaces[0].RepoPaths, p => Same(p, linked));
        });
    }

    /// <summary>Pruning goes by what git actually dropped, not by what looked prunable: a
    /// locked worktree survives a prune even with its directory gone, so its row has to
    /// survive too.</summary>
    [Fact]
    public void PruningKeepsTheRowOfALockedWorktreeItCouldNotDrop()
    {
        WithApp((repo, session, app, settings, linked) =>
        {
            PumpUntilNestedRow(session, app, linked);
            Assert.True(repo.Repo.LockWorktree(linked, "unplugged").Success);
            Assert.True(TempRepo.ForceDelete(linked), "the fixture worktree would not delete");

            Invoke(app, "PruneWorktreesAction");
            session.PumpFrames(4);

            Assert.Equal(2, repo.Repo.GetWorktrees().Count);
            Assert.True(HasNestedRow(app, linked), "the row that reaches the worktree is gone");

            repo.Repo.UnlockWorktree(linked);
        });
    }

    /// <summary>Deregistering the repository you are standing in has to land somewhere:
    /// the shared undo reselects from the active workspace, which is what keeps the app
    /// from ending up pointed at a path it just forgot.</summary>
    [Fact]
    public void ForgettingTheActiveRepositoryFallsBackToAnotherOne()
    {
        WithApp((repo, session, app, settings, linked) =>
        {
            Invoke(app, "OpenWorktree", linked);
            session.PumpFrames(2);
            Assert.True(Same(ActiveRepoPath(app), linked));

            Invoke(app, "ForgetRepo", linked);

            Assert.True(Same(ActiveRepoPath(app), repo.Path),
                $"expected a fallback to the main worktree, got \"{ActiveRepoPath(app)}\"");
        });
    }

    /// <summary>The prompt accepts a relative folder, and git resolves it against the
    /// repository root because that is the working directory the command runs in. What gets
    /// registered has to be that same absolute path: a stored "../side" matches nothing the
    /// panel or the sidebar later compares it against.</summary>
    [Fact]
    public void ATypedRelativeFolderResolvesAgainstTheRepositoryRoot()
    {
        WithApp((repo, session, app, settings, linked) =>
        {
            var resolved = (string)typeof(JunctionApp)
                .GetMethod("ResolveWorktreePath", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(app, ["../" + Path.GetFileName(linked)])!;

            Assert.True(Path.IsPathFullyQualified(resolved), resolved);
            Assert.True(Same(resolved, linked), $"expected \"{linked}\", got \"{resolved}\"");
        });
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    /// <summary>Mounts the app over a repository that already has one linked worktree, and
    /// hands both plus the live settings object to <paramref name="body"/>. The settings
    /// instance is the one the app mutates, so assertions read it directly rather than
    /// re-loading the file (which <c>SettingsIsolation</c> has already redirected away
    /// from the developer's real one).</summary>
    private static void WithApp(Action<TempRepo, HeadlessSession, JunctionApp, JunctionSettingsData, string> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var linked = LinkedPathFor(repo, "-panel");
        var added = repo.Repo.AddWorktree(linked, newBranch: "side");
        Assert.True(added.Success, added.Stderr);

        var settings = new JunctionSettingsData
        {
            RepositoryPaths = [repo.Path],
            ActiveRepoPath = repo.Path,
            Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
            ActiveWorkspaceId = "w1",
        };

        using var session = HeadlessSession.Create(1200, 800);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, settings);
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();

            body(repo, session, app, settings, linked);
        }
        finally
        {
            app?.Dispose();
            repo.Repo.UnlockWorktree(linked);
            repo.Repo.RemoveWorktree(linked, force: true);
            TempRepo.ForceDelete(linked);
        }
    }

    /// <summary>What an older settings file looks like: the worktree stored as a repository
    /// of its own. Nothing writes this any more; the cleanup still has to reach it.</summary>
    private static void RegisterAsRepository(JunctionSettingsData settings, string path)
    {
        settings.RepositoryPaths.Add(path);
        settings.Workspaces[0].RepoPaths.Add(path);
    }

    /// <summary>Whether the sidebar is showing the worktree nested under its repository. The
    /// row keys itself by path plus a suffix of "#"-flags for whatever changes how it
    /// renders, hence the boundary match rather than a plain prefix.</summary>
    private static bool HasNestedRow(JunctionApp app, string path)
    {
        var key = "wtchild:" + path;
        return Descendants(app.Mount()).Any(e =>
            e.Key == key || (e.Key?.StartsWith(key + "#", StringComparison.Ordinal) ?? false));
    }

    /// <summary>Worktrees load off the UI thread, so the nested row is legitimately absent
    /// for the first frames.</summary>
    private static void PumpUntilNestedRow(HeadlessSession session, JunctionApp app, string path)
    {
        try
        {
            session.PumpUntil(() => HasNestedRow(app, path), TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException("no nested row appeared for " + path);
        }
    }

    private static IEnumerable<HultUI.Core.Elements.Element> Descendants(HultUI.Core.Elements.Element e)
    {
        yield return e;
        foreach (var c in e.Children)
            foreach (var d in Descendants(c))
                yield return d;
    }

    private static void Invoke(JunctionApp app, string method, params object?[] args) =>
        typeof(JunctionApp)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, args);

    private static string ActiveRepoPath(JunctionApp app) =>
        (string)typeof(JunctionApp)
            .GetField("_activeRepoPath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    /// <summary>The app's own path comparison: git prints forward slashes even on Windows
    /// and the registry stores OS-native paths, so a raw string compare is the trap this
    /// whole area is built around.</summary>
    private static bool Same(string a, string b) =>
        string.Equals(a, b,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

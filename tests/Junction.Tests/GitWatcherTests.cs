using System.Diagnostics;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// What the repository watcher reports, and as what: a change to the working tree alone, or a
/// ref moving — HEAD, a branch, the config, the packed refs — which is what the log, the
/// branches, the tags and the remotes are read from (JunctionApp.OnWorkingTreeChange reads none
/// of them again). Real file-system events, with a short debounce.
/// </summary>
public class GitWatcherTests
{
    [Fact]
    public void AWorkingTreeChange_IsToldFromARefMoving()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Git("branch", "other");
        using var session = HeadlessSession.Create(400, 300);
        var seen = new List<bool>();
        using var watcher = new GitWatcher(repo.Path, refs => seen.Add(refs), debounceMs: 100);

        List<bool> After(Action act, bool expectNone = false)
        {
            Pump(session, TimeSpan.FromMilliseconds(300));
            seen.Clear();
            act();
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(expectNone ? 1 : 5);
            while (DateTime.UtcNow < until && seen.Count == 0) { session.PumpFrame(); Thread.Sleep(10); }
            Pump(session, TimeSpan.FromMilliseconds(400));   // a straggler in the same change
            return seen.ToList();
        }

        Assert.Equal([false], After(() => repo.Write("saved.txt", "x\n")));
        Assert.Equal([false], After(() => File.Delete(Path.Combine(repo.Path, "saved.txt"))));
        Assert.Equal([true], After(() => repo.Git("branch", "delta")));
        // Git renames a lock file over HEAD and over the config: a checkout that changes no file
        // in the working tree, and a remote added, were never reported at all.
        Assert.Contains(true, After(() => repo.Git("checkout", "-q", "other")));
        Assert.Equal([true], After(() => repo.Git("remote", "add", "origin", "https://example.invalid/x.git")));
        // `git status` with optional locks on (as outside the tests) renames a new index over the
        // old one when the stat data it holds is stale, as a touched file makes it: the app's own
        // reads must not report themselves.
        Assert.Equal([false], After(() => File.SetLastWriteTimeUtc(Path.Combine(repo.Path, "README.md"), DateTime.UtcNow)));
        var index = File.GetLastWriteTimeUtc(Path.Combine(repo.Path, ".git", "index"));
        Assert.Empty(After(() => GitWithOptionalLocks(repo.Path, "status", "--porcelain=v1"), expectNone: true));
        Assert.True(File.GetLastWriteTimeUtc(Path.Combine(repo.Path, ".git", "index")) > index, "status did not write the index");
    }

    /// <summary>A reftable repository keeps every ref, HEAD's branch included, in .git/reftable:
    /// HEAD and refs/heads are stubs that never change. A branch made, a checkout and a reset
    /// reached the watcher only through the working-tree files they changed, if any, and were
    /// told as working-tree events, so the log and the branches kept what they had (plan 17
    /// round 13: 8 of 9 outside operations left Junction stale).</summary>
    [ReftableFact]
    public void OnAReftableRepository_ARefMovingIsToldAsOne()
    {
        using var repo = TempRepo.CreateWithInitialCommit(reftable: true);
        Assert.True(Directory.Exists(Path.Combine(repo.Path, ".git", "reftable")), "not a reftable repository");
        repo.Git("branch", "other");
        using var session = HeadlessSession.Create(400, 300);
        var seen = new List<bool>();
        using var watcher = new GitWatcher(repo.Path, refs => seen.Add(refs), debounceMs: 100);
        var after = Observer(session, seen);

        Assert.Equal([false], after(() => repo.Write("saved.txt", "x\n"), false));
        Assert.Equal([true], after(() => repo.Git("branch", "delta"), false));
        // The same commit: no file in the working tree changes, only the table HEAD's branch is in.
        Assert.Contains(true, after(() => repo.Git("checkout", "-q", "other"), false));
        Assert.Equal([true], after(() => repo.Git("branch", "-D", "delta"), false));
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "second");
        Pump(session, TimeSpan.FromMilliseconds(300));
        Assert.Contains(true, after(() => repo.Git("reset", "-q", "--hard", "HEAD~1"), false));
    }

    /// <summary>A ref transaction that changes nothing creates the ref's lock and deletes it, and
    /// the folder reports a Changed of its own: every <c>git fetch</c> (git 2.53 at least) does it for
    /// the remote's HEAD, so a fetch that brought nothing read the log, the branches and the tags
    /// again (plan 17 round 13). It is a working-tree event now, not dropped: a move that writes
    /// only a reflog entry shows the watcher nothing else. A ref's own rename still moves it.</summary>
    [Fact]
    public void ARefTransactionThatChangesNothing_IsAWorkingTreeEvent()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var remote = Path.Combine(Path.GetTempPath(), "junction-git-remote-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(remote);
            Assert.True(GitProcess.Run(remote, "init", "--bare", "-b", "main").Success);
            repo.Git("remote", "add", "origin", remote);
            repo.Git("push", "-q", "-u", "origin", "main");
            repo.Git("fetch", "-q", "origin");
            using var session = HeadlessSession.Create(400, 300);
            var seen = new List<bool>();
            using var watcher = new GitWatcher(repo.Path, refs => seen.Add(refs), debounceMs: 100);
            var after = Observer(session, seen);

            // As git leaves it when the ref already holds the value: the lock made and deleted.
            var lockFile = Path.Combine(repo.Path, ".git", "refs", "heads", "main.lock");
            Assert.Equal([false], after(() => { File.WriteAllText(lockFile, repo.Head + "\n"); File.Delete(lockFile); }, false));
            Assert.DoesNotContain(true, after(() => repo.Git("fetch", "--all", "--prune"), true));
            // The reflog gains "reset: moving to HEAD"; no ref moves.
            Assert.DoesNotContain(true, after(() => repo.Git("reset", "-q", "--hard", "HEAD"), true));

            repo.Write("next.txt", "next\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-q", "-m", "next");
            Assert.Equal([true], after(() => repo.Git("push", "-q", "origin", "main"), false));
        }
        finally { TempRepo.ForceDelete(remote); }
    }

    /// <summary>What was heard before <see cref="GitWatcher.Forget"/> is not told, whether its
    /// debounce was still running or its callback already on its way to the UI thread; what is
    /// heard after it is (the app forgets when its own action's reload starts, plan 17 round 13).</summary>
    [Fact]
    public void WhatWasHeardBeforeForget_IsNotTold_AndWhatComesAfterIs()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        using var session = HeadlessSession.Create(400, 300);
        var seen = new List<bool>();
        using var watcher = new GitWatcher(repo.Path, refs => seen.Add(refs), debounceMs: 100);
        var heard = typeof(GitWatcher).GetField("_heard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Pump(session, TimeSpan.FromMilliseconds(300));
        Assert.False(watcher.Forget());   // nothing heard yet

        // Heard, debounced and posted, not yet run: no frame is pumped until after the Forget.
        long before = (long)heard.GetValue(watcher)!;
        repo.Git("branch", "delta");
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < until && (long)heard.GetValue(watcher)! == before) Thread.Sleep(10);
        Thread.Sleep(400);
        Assert.True(watcher.Forget());
        Pump(session, TimeSpan.FromMilliseconds(600));
        Assert.Empty(seen);

        Assert.Equal([true], Observer(session, seen)(() => repo.Git("branch", "epsilon"), false));
    }

    /// <summary>Waits out what came before, runs the action, and returns what the watcher said
    /// about it (a straggler in the same change included).</summary>
    private static Func<Action, bool, List<bool>> Observer(HeadlessSession session, List<bool> seen) => (act, expectNone) =>
    {
        Pump(session, TimeSpan.FromMilliseconds(300));
        seen.Clear();
        act();
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(expectNone ? 1 : 5);
        while (DateTime.UtcNow < until && seen.Count == 0) { session.PumpFrame(); Thread.Sleep(10); }
        Pump(session, TimeSpan.FromMilliseconds(400));
        return seen.ToList();
    };

    private static void Pump(HeadlessSession session, TimeSpan time)
    {
        var until = DateTime.UtcNow + time;
        while (DateTime.UtcNow < until) { session.PumpFrame(); Thread.Sleep(10); }
    }

    private static void GitWithOptionalLocks(string path, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = path, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "1";
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }
}

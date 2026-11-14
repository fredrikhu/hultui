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

    /// <summary>Staging from a terminal or an editor writes only the index, by renaming a new one
    /// over it, which the watcher did not hear (plan 17 round 14: `git add`, `restore --staged`
    /// and a staged submodule bump left Changes stale). It hears the rename now, and tells it
    /// when the index's entries changed: a `git status` that rewrites the index to refresh the
    /// file times it holds (optional locks on, as outside the tests) is this app's own read and
    /// tells nothing.</summary>
    [Fact]
    public void AStageMadeOutside_IsHeard_AndAStatusThatRefreshesTheIndexIsNot()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("README.md", "changed\n");
        repo.Settle();
        using var session = HeadlessSession.Create(400, 300);
        var seen = new List<bool>();
        using var watcher = new GitWatcher(repo.Path, refs => seen.Add(refs), debounceMs: 100);
        var after = Observer(session, seen);

        Assert.Equal([false], after(() => repo.Git("add", "README.md"), false));
        Assert.Equal([false], after(() => repo.Git("restore", "--staged", "README.md"), false));
        Assert.Equal([false], after(() => repo.Git("add", "README.md"), false));

        // The same bytes written again: the file's times move, the status re-reads it and writes
        // the index back with the new times and the same entries.
        var readme = Path.Combine(repo.Path, "README.md");
        Assert.Equal([false], after(() => File.WriteAllBytes(readme, File.ReadAllBytes(readme)), false));
        var index = File.GetLastWriteTimeUtc(Path.Combine(repo.Path, ".git", "index"));
        Assert.Empty(after(() => GitWithOptionalLocks(repo.Path, "status", "--porcelain=v1"), true));
        Assert.True(File.GetLastWriteTimeUtc(Path.Combine(repo.Path, ".git", "index")) > index, "status did not write the index");
    }

    /// <summary>The first rewrite of the index in a session is compared too: the first callback
    /// reads the index before the reload it starts, so this app's own status refreshing the index
    /// after that is not heard back as one more reload, as it was for want of something to compare
    /// with (plan 17 round 14: a source file touched beside Changes, 11 git commands more).</summary>
    [Fact]
    public void TheSessionsFirstIndexRefresh_IsNotToldEither()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Settle();
        using var session = HeadlessSession.Create(400, 300);
        var seen = new List<bool>();
        using var watcher = new GitWatcher(repo.Path, refs => seen.Add(refs), debounceMs: 100);
        var after = Observer(session, seen);

        var readme = Path.Combine(repo.Path, "README.md");
        Assert.Equal([false], after(() => File.WriteAllBytes(readme, File.ReadAllBytes(readme)), false));
        var index = File.GetLastWriteTimeUtc(Path.Combine(repo.Path, ".git", "index"));
        Assert.Empty(after(() => GitWithOptionalLocks(repo.Path, "status", "--porcelain=v1"), true));
        Assert.True(File.GetLastWriteTimeUtc(Path.Combine(repo.Path, ".git", "index")) > index, "status did not write the index");
        Assert.Equal([false], after(() => repo.Write("README.md", "changed\n"), false));
        Assert.Equal([false], after(() => repo.Git("add", "README.md"), false));
    }

    /// <summary>What the watcher compares an index by: its entries' paths, modes, object ids and
    /// flags, in every index version git writes (2; 3 with an intent-to-add entry; 4 with its
    /// paths shortened) and with SHA-256 object ids, never the file times a status refreshes.</summary>
    [Theory]
    [InlineData("sha1", 2)]
    [InlineData("sha1", 3)]
    [InlineData("sha1", 4)]
    [InlineData("sha256", 2)]
    [InlineData("sha256", 4)]
    public void TheIndexPrint_FollowsTheEntries_NotTheFileTimes(string format, int version)
    {
        var path = Path.Combine(Path.GetTempPath(), "junction-git-print-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(path);
            void Git(params string[] args) => Assert.True(GitProcess.Run(path, args).Success, "git " + string.Join(' ', args));
            Git("init", "-q", "-b", "main", "--object-format=" + format);
            for (int i = 0; i < 40; i++)
            {
                var file = Path.Combine(path, $"dir{i % 3}", $"file-{i:D2}-with-a-longer-name.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, $"{i}\n");
            }
            Git("add", "-A");
            if (version == 3) { File.WriteAllText(Path.Combine(path, "later.txt"), "later\n"); Git("add", "-N", "later.txt"); }
            Git("update-index", "--index-version", version.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var indexPath = Path.Combine(path, ".git", "index");
            int oid = format == "sha256" ? 32 : 20;
            Assert.Equal(version, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(File.ReadAllBytes(indexPath).AsSpan(4)));

            var first = GitWatcher.IndexPrint(indexPath, oid);
            Assert.NotNull(first);

            // Every file's times move and a status with optional locks writes the index back.
            foreach (var f in Directory.EnumerateFiles(path, "*.txt", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(1));
            var before = File.GetLastWriteTimeUtc(indexPath);
            GitWithOptionalLocks(path, "status", "--porcelain=v1");
            Assert.True(File.GetLastWriteTimeUtc(indexPath) > before, "status did not write the index");
            Assert.Equal(first, GitWatcher.IndexPrint(indexPath, oid));

            File.WriteAllText(Path.Combine(path, "dir1", "file-01-with-a-longer-name.txt"), "staged\n");
            Git("add", "dir1/file-01-with-a-longer-name.txt");
            var staged = GitWatcher.IndexPrint(indexPath, oid);
            Assert.NotNull(staged);
            Assert.NotEqual(first, staged);
            Git("update-index", "--chmod=+x", "dir2/file-02-with-a-longer-name.txt");
            Assert.NotEqual(staged, GitWatcher.IndexPrint(indexPath, oid));
        }
        finally { TempRepo.ForceDelete(path); }
    }

    /// <summary>A build beside the app writes hundreds of files under bin\ and obj\, which git
    /// ignores and never shows, and each quiet gap in it reloaded the repository (plan 17 round
    /// 14: 2-4 reloads a build, 22-33 git commands over Changes). Git is asked about a folder the
    /// first time a window meets it, and nothing under a folder it ignores is told; a file git
    /// tracks there (force-added) is, and so is one force-added later, when the index's entries
    /// change and git is asked again.</summary>
    [Fact]
    public void WritesUnderAFolderGitIgnores_AreNotTold_AndATrackedFileThereIs()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write(".gitignore", "bin/\nobj/\n");
        repo.Write("proj/Program.cs", "x\n");
        repo.Write("proj/obj/keep/kept.txt", "kept\n");
        repo.Git("add", "-A");
        repo.Git("add", "-f", "proj/obj/keep/kept.txt");
        repo.Git("commit", "-q", "-m", "project");
        repo.Settle();
        using var session = HeadlessSession.Create(400, 300);
        var seen = new List<bool>();
        using var watcher = new GitWatcher(repo.Path, refs => seen.Add(refs), debounceMs: 100);
        var after = Observer(session, seen);

        void Build()
        {
            var obj = Path.Combine(repo.Path, "proj", "obj", "Debug", "net");
            var bin = Path.Combine(repo.Path, "proj", "bin", "Debug", "net");
            Directory.CreateDirectory(obj);
            Directory.CreateDirectory(bin);
            for (int i = 0; i < 20; i++)
            {
                File.WriteAllText(Path.Combine(obj, $"f{i}.cs"), "x");
                File.WriteAllText(Path.Combine(bin, $"f{i}.dll"), "x");
            }
            File.Delete(Path.Combine(obj, "f0.cs"));
        }

        // The first build makes the folders, and git is asked about them; a rebuild asks nothing.
        Assert.Empty(after(Build, true));
        int asked = watcher.IgnoreQuestions;
        Assert.InRange(asked, 1, 2);
        Assert.Empty(after(Build, true));
        Assert.Equal(asked, watcher.IgnoreQuestions);

        // A file git tracks under obj\ is heard, and so is a source file.
        Assert.Equal([false], after(() => repo.Write("proj/obj/keep/kept.txt", "edited\n"), false));
        Assert.Equal([false], after(() => repo.Write("proj/Program.cs", "y\n"), false));

        // Force-added later: the index's entries change, and what git ignores is asked again.
        Assert.Empty(after(() => repo.Write("proj/obj/Debug/net/later.txt", "later\n"), true));
        Assert.Equal([false], after(() => repo.Git("add", "-f", "proj/obj/Debug/net/later.txt"), false));
        Assert.Equal([false], after(() => repo.Write("proj/obj/Debug/net/later.txt", "edited\n"), false));
    }

    /// <summary>What git ignores is in .gitignore files and .git/info/exclude: a change to either
    /// is told (git status may show other files now) and what git said of folders is forgotten.</summary>
    [Fact]
    public void AChangedIgnoreRule_IsTold_AndTheFoldersAreAskedAgain()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write(".gitignore", "obj/\n");
        repo.Write("proj/a.txt", "a\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "project");
        repo.Settle();
        using var session = HeadlessSession.Create(400, 300);
        var seen = new List<bool>();
        using var watcher = new GitWatcher(repo.Path, refs => seen.Add(refs), debounceMs: 100);
        var after = Observer(session, seen);

        Assert.Empty(after(() => repo.Write("proj/obj/x.txt", "x\n"), true));
        Assert.Equal([false], after(() => repo.Write(".gitignore", "bin/\n"), false));
        Assert.Equal([false], after(() => repo.Write("proj/obj/y.txt", "y\n"), false));
        Assert.Equal([false], after(() => repo.Write("proj/.gitignore", "obj/\n"), false));
        Assert.Empty(after(() => repo.Write("proj/obj/z.txt", "z\n"), true));

        Assert.Equal([false], after(() => repo.Write("out/a.log", "1\n"), false));
        Assert.Equal([false], after(() => repo.Write(".git/info/exclude", "out/\n"), false));
        Assert.Empty(after(() => repo.Write("out/c.log", "3\n"), true));
    }

    /// <summary>A build writes hundreds of files in a burst. The burst is one callback, as
    /// before, and costs no exception: the debounce used to make a token, a task and a delay per
    /// event and cancel the one before, which threw (plan 17 round 14: a Junction build of this
    /// repository 267 events, an AOT publish 551, a caught exception each).</summary>
    [Fact]
    public void ABurstOfEvents_IsOneCallback_AndThrowsNothing()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        using var session = HeadlessSession.Create(400, 300);
        var seen = new List<bool>();
        using var watcher = new GitWatcher(repo.Path, refs => seen.Add(refs), debounceMs: 500);
        Directory.CreateDirectory(Path.Combine(repo.Path, "out"));
        Pump(session, TimeSpan.FromMilliseconds(800));
        seen.Clear();

        int thrown = 0;
        void OnThrown(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is OperationCanceledException && Environment.StackTrace.Contains(nameof(GitWatcher), StringComparison.Ordinal))
                Interlocked.Increment(ref thrown);
        }
        AppDomain.CurrentDomain.FirstChanceException += OnThrown;
        try
        {
            for (int i = 0; i < 100; i++)
            {
                File.WriteAllText(Path.Combine(repo.Path, "out", $"f{i}.bin"), "x");
                Thread.Sleep(1);   // a build's pace: each write its own event, all inside one debounce
            }
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < until && seen.Count == 0) { session.PumpFrame(); Thread.Sleep(10); }
            Pump(session, TimeSpan.FromMilliseconds(800));
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= OnThrown; }

        Assert.Equal([false], seen);
        Assert.Equal(0, thrown);
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

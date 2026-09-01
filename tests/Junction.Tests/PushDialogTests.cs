using System.Reflection;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Pressing Push asks first, and the card it opens is where force-with-lease lives.
///
/// <para>The lease is the point: after a rebase or an amend an ordinary push is refused, and
/// the only way through it used to be a terminal. <c>--force-with-lease</c> overwrites the
/// remote branch only while the remote is still where this repository last saw it, so a
/// colleague who pushed in between makes the command fail rather than lose their work —
/// which is what the two diverged-branch cases below actually exercise.</para>
/// </summary>
[Collection("JunctionDiff")]
public class PushDialogTests
{
    [Fact]
    public void PressingPushOpensTheCardInsteadOfPushing()
    {
        Hosted((session, app, repo, remote) =>
        {
            Diverge(repo);
            var before = RemoteHead(remote);

            Push(session, app);

            var dialog = Dialog(app);
            Assert.NotNull(dialog);
            Assert.Contains("Force push (with lease)", (string)Member(dialog!, "CheckboxLabel")!);
            Assert.False((bool)Field(app, "_dialogChecked")!, "the lease must be off until it is asked for");
            Assert.Equal("Push", (string)Member(dialog!, "ConfirmLabel")!);
            // Nothing has left yet: the card is a question, not a progress dialog.
            Assert.Equal(before, RemoteHead(remote));
        });
    }

    [Fact]
    public void ConfirmingWithTheLeaseForcePushesADivergedBranch()
    {
        Hosted((session, app, repo, remote) =>
        {
            Diverge(repo);
            var local = LocalHead(repo);
            Assert.NotEqual(local, RemoteHead(remote));

            Push(session, app);
            Confirm(session, app, force: true);
            session.PumpUntil(() => RemoteHead(remote) == local, TimeSpan.FromSeconds(30));

            Assert.Equal(local, RemoteHead(remote));
        });
    }

    [Fact]
    public void ConfirmingWithoutTheLeaseLeavesADivergedBranchWhereItWas()
    {
        Hosted((session, app, repo, remote) =>
        {
            Diverge(repo);
            var before = RemoteHead(remote);

            Push(session, app);
            Confirm(session, app, force: false);
            // The push is refused (non-fast-forward). Give it as long as the successful one
            // gets, then assert the remote never moved.
            session.PumpFrames(120);

            Assert.Equal(before, RemoteHead(remote));
            Assert.NotEqual(LocalHead(repo), RemoteHead(remote));
        });
    }

    // ── Fixture ──────────────────────────────────────────────────────────

    /// <summary>Rewrites the pushed commit, so the branch and its upstream disagree about
    /// history — the shape an ordinary push refuses and the lease is for.</summary>
    private static void Diverge(TempRepo repo)
    {
        repo.Write("README.md", "rewritten\n");
        repo.Git("add", "-A");
        repo.Git("commit", "--amend", "-m", "rewritten history");
    }

    private static string LocalHead(TempRepo repo) => repo.Git("rev-parse", "HEAD").Stdout.Trim();

    private static string RemoteHead(string bare) =>
        GitProcess.Run(bare, "rev-parse", "refs/heads/main").Stdout.Trim();

    private static void Push(HeadlessSession session, JunctionApp app)
    {
        Invoke(app, "DoPush");
        // PlanPush runs off the UI thread, so the card arrives a frame or two later.
        session.PumpUntil(() => Dialog(app) != null, TimeSpan.FromSeconds(30));
    }

    private static void Confirm(HeadlessSession session, JunctionApp app, bool force)
    {
        typeof(JunctionApp).GetField("_dialogChecked", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(app, force);
        Invoke(app, "ConfirmDialog");
        session.PumpFrames(3);
    }

    private static object? Dialog(JunctionApp app) => Field(app, "_dialog");

    private static object? Field(JunctionApp app, string name) =>
        typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app);

    private static object? Member(object owner, string name) =>
        owner.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(owner);

    private static void Invoke(JunctionApp app, string method) =>
        typeof(JunctionApp).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, null);

    /// <summary>A repository with a bare remote it has already published main to.</summary>
    private static void Hosted(Action<HeadlessSession, JunctionApp, TempRepo, string> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var bare = Path.Combine(Path.GetTempPath(), "junction-remote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bare);
        try
        {
            GitProcess.Run(bare, "init", "--bare", "-b", "main");
            repo.Git("remote", "add", "origin", bare);
            repo.Git("push", "-u", "origin", "main");

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

                body(session, app, repo, bare);
            }
            finally { app?.Dispose(); }
        }
        finally
        {
            try { Directory.Delete(bare, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }
}

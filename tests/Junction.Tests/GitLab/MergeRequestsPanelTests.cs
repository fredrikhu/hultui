using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Junction.Git;
using Junction.GitLab;
using Xunit;

namespace Junction.Tests.GitLab;

/// <summary>
/// Phase 2, hosted against recordings: the pipeline glyph in the toolbar and the branch row's
/// merge-request chip, the panel's filters and status words, the detail page, and a failed
/// refresh that keeps the rows and says how old they are. The git half of "Checkout locally"
/// runs against a real local origin in <see cref="MergeRequestCheckoutGitTests"/>.
/// </summary>
public class MergeRequestsPanelTests
{
    private const string Api = "/api/v4";
    private const string MrList = $"GET {Api}/projects/7781/merge_requests?state=opened&order_by=updated_at&sort=desc&with_merge_status_recheck=true&per_page=100&page=1";

    [Fact]
    public void TheToolbarShowsTheHeadPipelineAndTheBranchRowItsChip()
    {
        Hosted((repo, session, app) =>
        {
            // The head's pipeline lands in the toolbar once the hosted data arrives.
            session.PumpUntil(() => ById(session, "pipeline:running") is not null, TimeSpan.FromSeconds(15));
            var head = Root(session) is { } r
                ? LayoutProbe.Descendants<Element>(r, e => e.AccessibleName?.StartsWith("Pipeline #4021") == true).FirstOrDefault()
                : null;
            Assert.NotNull(head);

            session.Window.PressKey(KeyCode.Digit5);
            session.PumpUntil(() => ById(session, "mr-chip") is not null, TimeSpan.FromSeconds(15));
            Assert.Equal("!412 PC-1042: refund splitting", ById(session, "mr-chip")!.TooltipText);
        });
    }

    [Fact]
    public void ThePanelListsWhatIsAssignedToMeThenEverythingOpen()
    {
        Hosted((_, session, app) =>
        {
            app.OpenMergeRequests();
            session.PumpUntil(() => TreeHas(session, "!412"), TimeSpan.FromSeconds(15));
            Assert.True(app.MergeRequestsOpen);
            Assert.True(TreeHas(session, "acme/core-api on gitlab.com"));
            // Assigned to me is the default: !412 has dosei as assignee, !398 only as reviewer.
            Assert.True(TreeHas(session, "PC-1042: refund splitting"));
            Assert.False(TreeHas(session, "PC-1120 event sourcing POC"));
            Assert.True(TreeHas(session, "Pipeline running"));

            session.Window.Click(Named(session, "All open"));
            session.PumpFrames(2);
            Assert.True(TreeHas(session, "PC-1120 event sourcing POC"));
            Assert.True(TreeHas(session, "Draft"));
            Assert.True(TreeHas(session, "2 open"));

            session.Window.Click(Named(session, "Reviewing"));
            session.PumpFrames(2);
            Assert.False(TreeHas(session, "PC-1042: refund splitting"));
            Assert.True(TreeHas(session, "PC-1120 event sourcing POC"));
        });
    }

    [Fact]
    public void EnterOpensTheDetailAndEscWalksBackOut()
    {
        Hosted((_, session, app) =>
        {
            app.OpenMergeRequests(JunctionApp.MrFilter.AllOpen);
            session.PumpUntil(() => TreeHas(session, "!412"), TimeSpan.FromSeconds(15));

            session.Window.PressKey(KeyCode.Enter);
            session.PumpUntil(() => TreeHas(session, "Waiting for pipeline #4021 to finish."), TimeSpan.FromSeconds(15));
            // Approvals and discussions load behind the detail; wait for each to land.
            session.PumpUntil(() => TreeHas(session, "1 of 2"), TimeSpan.FromSeconds(15));
            session.PumpUntil(() => TreeHas(session, "Refunds should not count voided tenders."), TimeSpan.FromSeconds(15));
            Assert.True(TreeHas(session, "src/api/Orders.cs · lines 23–25"));
            Assert.NotNull(Label(session, "•"));                                          // description bullet
            Assert.NotNull(Label(session, "per-tender rows"));                            // …with its text beside it
            // The backticks became a code span on the words they held.
            var code = Label(session, "Refunds() counts them")!;
            Assert.Contains(code.Spans!, s => s.Start == 0 && s.Length == "Refunds()".Length && s.Background.A > 0);
            // #1042 links to the issue, as it would in a commit message.
            var closes = Label(session, "Closes #1042")!;
            Assert.Equal("https://gitlab.com/acme/core-api/-/issues/1042", Assert.Single(closes.Links!).Target);
            Assert.NotNull(Named(session, "Checkout locally"));

            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(2);
            Assert.True(app.MergeRequestsOpen);
            Assert.NotNull(Named(session, "All open"));

            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(2);
            Assert.False(app.MergeRequestsOpen);
        });
    }

    [Fact]
    public void AFailedRefreshKeepsTheRowsAndSaysHowOld()
    {
        int listCalls = 0;
        var handler = Routes(null).Map(MrList, _ => ++listCalls == 1
            ? FakeGitLabHandler.Json(FakeGitLabHandler.Recording("merge_requests_page1"))
            : FakeGitLabHandler.Json("{\"message\":\"502 Bad Gateway\"}", 502));

        Hosted((repo, session, app) =>
        {
            app.OpenMergeRequests(JunctionApp.MrFilter.AllOpen);
            session.PumpUntil(() => TreeHas(session, "!412"), TimeSpan.FromSeconds(15));
            Assert.Null(ById(session, "mr-failure"));

            app.RefreshHosted(repo.Path);
            session.PumpUntil(() => ById(session, "mr-failure") is not null, TimeSpan.FromSeconds(15));

            Assert.True(TreeHas(session, "Showing what was loaded"));
            Assert.True(TreeHas(session, "!412"));      // the rows stayed
            Assert.Equal(2, listCalls);
        }, handler);
    }

    // ── Hosting ──────────────────────────────────────────────────────────────────────

    private static FakeGitLabHandler Routes(string? headSha) =>
        new FakeGitLabHandler()
            .Map($"GET {Api}/projects/acme%2Fcore-api", "project")
            .Map($"GET {Api}/projects/7781/protected_branches?per_page=100&page=1", "protected_branches")
            .Map(MrList, "merge_requests_page1")
            .Map($"GET {Api}/projects/7781/merge_requests/412/approvals", "approvals")
            .Map($"GET {Api}/projects/7781/merge_requests/412/discussions?per_page=100&page=1", "discussions")
            .MapJson($"GET {Api}/projects/7781/merge_requests/412/diffs?per_page=100&page=1",
                "[{\"old_path\":\"src/api/Orders.cs\",\"new_path\":\"src/api/Orders.cs\",\"new_file\":false,\"renamed_file\":false,\"deleted_file\":false}]")
            .MapJson($"GET {Api}/projects/7781/pipelines?order_by=updated_at&sort=desc&per_page=100&page=1",
                headSha is null ? "[]" :
                $"[{{\"id\":4021,\"iid\":118,\"project_id\":7781,\"sha\":\"{headSha}\",\"ref\":\"main\",\"status\":\"running\",\"web_url\":\"https://gitlab.com/acme/core-api/-/pipelines/4021\",\"created_at\":\"2026-09-12T09:44:00.000Z\"}}]");

    private static void Hosted(Action<TempRepo, HeadlessSession, JunctionApp> body, FakeGitLabHandler? handler = null)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Git("remote", "add", "origin", "https://gitlab.com/acme/core-api.git");
        repo.Git("branch", "feature/2.4.0/payments/PC-1042-refund-splitting");
        // The pipelines recording names this repository's real head, so the toolbar glyph
        // and the branch row's glyph have a SHA to match.
        handler ??= Routes(repo.Head);

        var store = new InMemoryCredentialStore();
        store.Write(GitLabAccounts.Service("gitlab.com"), GitLabAccounts.TokenAccount, "glpat-test");
        var settings = new JunctionSettingsData
        {
            RepositoryPaths = [repo.Path],
            ActiveRepoPath = repo.Path,
            Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
            ActiveWorkspaceId = "w1",
        };
        settings.GitLabAccounts.Add(new GitLabAccountData
        {
            Host = "gitlab.com", WebBaseUrl = "https://gitlab.com", UserId = 42, Username = "dosei", Name = "Dana Osei",
            Kind = GitLabTokenKind.Pat, Scopes = ["api"], ExpiresAt = DateTimeOffset.UtcNow.AddDays(200),
        });

        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, settings,
                credentialStore: store, gitLabHandler: _ => handler);
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

    private static bool TreeHas(HeadlessSession session, string text) =>
        Root(session) is { } root &&
        LayoutProbe.Descendants<TextElement>(root, t => t.Content.Contains(text, StringComparison.Ordinal)).Count > 0;

    /// <summary>The label showing exactly <paramref name="text"/>.</summary>
    private static TextElement? Label(HeadlessSession session, string text) =>
        Root(session) is { } root
            ? LayoutProbe.Descendants<TextElement>(root, t => t.Content == text).FirstOrDefault()
            : null;

    private static Element? ById(HeadlessSession session, string id) =>
        Root(session) is { } root
            ? LayoutProbe.Descendants<Element>(root, e => e.AutomationId == id).FirstOrDefault()
            : null;

    private static Element Named(HeadlessSession session, string name)
    {
        var el = Root(session) is { } root
            ? LayoutProbe.Descendants<Element>(root, e => e.AccessibleName == name).FirstOrDefault()
            : null;
        return el ?? throw new Xunit.Sdk.XunitException($"nothing on screen is named \"{name}\"");
    }
}

/// <summary>
/// The git half of "Checkout locally", against a real local origin that carries a
/// <c>refs/merge-requests/N/head</c> the way GitLab does: the refspec fetch lands the head
/// under the remote's namespace, and a branch is created on it or fast-forwarded to it.
/// </summary>
public class MergeRequestCheckoutGitTests
{
    /// <summary>The ref a checkout or a review fetches the head into is addressed by name
    /// where it is used; among the remote branches it only read as clutter.</summary>
    [Fact]
    public void TheFetchedMergeRequestRefIsNotListedAmongTheBranches()
    {
        using var local = TempRepo.CreateWithInitialCommit();
        local.Git("update-ref", "refs/remotes/origin/merge-requests/412", local.Head);
        local.Git("update-ref", "refs/remotes/origin/main", local.Head);
        var names = local.Repo.GetBranches().Select(b => b.Name).ToList();
        Assert.Contains("origin/main", names);
        Assert.DoesNotContain("origin/merge-requests/412", names);
        // The ref itself is still there for the code that names it.
        Assert.Equal(local.Head, local.Git("rev-parse", "refs/remotes/origin/merge-requests/412").Stdout.Trim());
    }

    [Fact]
    public async Task TheMergeRequestHeadIsFetchedAndABranchCreatedOnIt()
    {
        using var origin = TempRepo.CreateWithInitialCommit();
        origin.Write("feature.txt", "from the merge request\n");
        origin.Git("add", "feature.txt");
        origin.Git("commit", "-m", "MR head");
        var head = origin.Head;
        origin.Git("update-ref", "refs/merge-requests/412/head", head);
        origin.Git("checkout", "-q", "--detach");   // so the branch can be fetched into freely

        using var local = TempRepo.CreateWithInitialCommit();
        local.Git("remote", "add", "origin", origin.Path);

        var repo = local.Repo;
        var fetch = await repo.FetchRefspecAsync("origin", "+refs/merge-requests/412/head:refs/remotes/origin/merge-requests/412");
        Assert.True(fetch.Success, fetch.Stderr);
        Assert.Equal(head, local.Git("rev-parse", "refs/remotes/origin/merge-requests/412").Stdout.Trim());

        Assert.False(repo.LocalBranchExists("feature/x"));
        Assert.True(repo.CreateBranch("feature/x", "refs/remotes/origin/merge-requests/412").Success);
        Assert.True(repo.Checkout("feature/x").Success);
        Assert.True(repo.LocalBranchExists("feature/x"));
        Assert.Equal("feature/x", local.Git("rev-parse", "--abbrev-ref", "HEAD").Stdout.Trim());
        Assert.Equal("from the merge request\n", local.Read("feature.txt"));
    }

    [Fact]
    public async Task AnExistingBranchIsFastForwardedNotRewritten()
    {
        using var origin = TempRepo.CreateWithInitialCommit();
        var first = origin.Head;
        origin.Write("more.txt", "second commit\n");
        origin.Git("add", "more.txt");
        origin.Git("commit", "-m", "second");
        var second = origin.Head;
        origin.Git("update-ref", "refs/merge-requests/7/head", second);

        using var local = TempRepo.CreateWithInitialCommit();
        local.Git("remote", "add", "origin", origin.Path);
        local.Git("fetch", "-q", "origin", $"{first}:refs/heads/feature/y");
        var repo = local.Repo;

        Assert.True((await repo.FetchRefspecAsync("origin", "+refs/merge-requests/7/head:refs/remotes/origin/merge-requests/7")).Success);
        Assert.True(repo.Checkout("feature/y").Success);
        Assert.True(repo.MergeFastForwardOnly("refs/remotes/origin/merge-requests/7").Success);
        Assert.Equal(second, local.Head);

        // Diverged: a local commit on the branch, and the merge request moved on without it.
        // No fast-forward is possible, and the refusal is the whole point — nothing is rewritten.
        local.Write("mine.txt", "local work\n");
        local.Git("add", "mine.txt");
        local.Git("commit", "-m", "mine");
        var mine = local.Head;
        origin.Write("theirs.txt", "more upstream\n");
        origin.Git("add", "theirs.txt");
        origin.Git("commit", "-m", "third");
        origin.Git("update-ref", "refs/merge-requests/7/head", origin.Head);
        Assert.True((await repo.FetchRefspecAsync("origin", "+refs/merge-requests/7/head:refs/remotes/origin/merge-requests/7")).Success);
        Assert.False(repo.MergeFastForwardOnly("refs/remotes/origin/merge-requests/7").Success);
        Assert.Equal(mine, local.Head);
    }
}

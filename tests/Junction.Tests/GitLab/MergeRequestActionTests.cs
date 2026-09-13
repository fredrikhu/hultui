using System.Net.Http;
using System.Reflection;
using System.Web;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Junction.Git;
using Junction.GitLab;
using Xunit;

namespace Junction.Tests.GitLab;

/// <summary>
/// Phase 5, hosted against recordings: the detail page's action row — approve and revoke,
/// the merge confirm with its two boxes and the form it sends, a refusal said inline under
/// the row, the draft toggle as a retitle — the todos bell and its list, and the Overview's
/// review strip that opens the panel in workspace scope.
/// </summary>
public class MergeRequestActionTests
{
    private const string Api = "/api/v4";
    private const string MrList = $"GET {Api}/projects/7781/merge_requests?state=opened&order_by=updated_at&sort=desc&with_merge_status_recheck=true&per_page=100&page=1";
    private const string Todos = $"GET {Api}/todos?state=pending&per_page=100&page=1";

    [Theory]
    [InlineData("Draft: PC-1042: refund splitting", "PC-1042: refund splitting")]
    [InlineData("[Draft] Fix", "Fix")]
    [InlineData("WIP: Fix", "Fix")]
    [InlineData("Plain", "Plain")]
    public void TheDraftPrefixIsStrippedInEveryFormGitLabAccepts(string title, string ready) =>
        Assert.Equal(ready, JunctionApp.StripDraftPrefix(title));

    [Fact]
    public void ApproveSendsTheShaAndTheButtonBecomesRevoke()
    {
        bool approved = false;
        var handler = Routes()
            .Map($"GET {Api}/projects/7781/merge_requests/412/approvals", _ => FakeGitLabHandler.Json(
                FakeGitLabHandler.Recording("approvals").Replace("\"user_has_approved\":false", approved ? "\"user_has_approved\":true" : "\"user_has_approved\":false")))
            .Map($"POST {Api}/projects/7781/merge_requests/412/approve", _ => { approved = true; return FakeGitLabHandler.Json(FakeGitLabHandler.Recording("approvals"), 201); })
            .Map($"POST {Api}/projects/7781/merge_requests/412/unapprove", _ => { approved = false; return new HttpResponseMessage(System.Net.HttpStatusCode.Created); });
        Hosted((_, session, app) =>
        {
            OpenDetail(session, app);
            session.PumpUntil(() => Named(session, "Approve") is not null, TimeSpan.FromSeconds(15));
            session.PumpFrames(2);
            session.Window.Click(Named(session, "Approve")!);
            session.PumpUntil(() => handler.Seen.Any(s => s.Request.RequestUri!.AbsolutePath.EndsWith("/approve")), TimeSpan.FromSeconds(10));
            var post = handler.Seen.Single(s => s.Request.RequestUri!.AbsolutePath.EndsWith("/approve"));
            Assert.Equal("c0ffee1234567890c0ffee1234567890c0ffee12", HttpUtility.ParseQueryString(post.Body!)["sha"]);

            // The reload says the user has approved; the button says so too.
            session.PumpUntil(() => Named(session, "Revoke approval") is not null, TimeSpan.FromSeconds(15));
            session.PumpFrames(2);
            session.Window.Click(Named(session, "Revoke approval")!);
            session.PumpUntil(() => handler.Seen.Any(s => s.Request.RequestUri!.AbsolutePath.EndsWith("/unapprove")), TimeSpan.FromSeconds(10));
            session.PumpUntil(() => Named(session, "Approve") is not null, TimeSpan.FromSeconds(15));
        }, handler);
    }

    [Fact]
    public void MergeAsksWithBothBoxesAndSendsTheForm()
    {
        var handler = Routes()
            .MapJson($"PUT {Api}/projects/7781/merge_requests/412/merge",
                "{\"id\":90412,\"iid\":412,\"project_id\":7781,\"title\":\"PC-1042: refund splitting\",\"state\":\"opened\",\"merge_when_pipeline_succeeds\":true}");
        Hosted((_, session, app) =>
        {
            OpenDetail(session, app);
            // !412's pipeline is running: merging now is off, only the deferred merge is on.
            session.PumpUntil(() => ById(session, "mr-merge") is not null, TimeSpan.FromSeconds(15));
            Assert.Null(ById(session, "mr-merge-reason"));

            app.ChooseMerge(whenPipelineSucceeds: true);
            session.PumpUntil(() => TreeHas(session, "Merge !412 into main"), TimeSpan.FromSeconds(10));
            session.PumpFrames(2);
            // Two boxes — squash, delete source — and the target's rule quoted before GitLab
            // is asked. A checkbox draws its own label, so it is counted, not read.
            Assert.Equal(2, LayoutProbe.Descendants<CheckboxElement>(Root(session)).Count);
            Assert.NotNull(ById(session, "dialog-check-2"));
            Assert.True(TreeHas(session, "main is protected"));

            session.Window.Click(Labelled(session, "Merge when pipeline succeeds"));
            session.PumpUntil(() => handler.Seen.Any(s => s.Request.Method == HttpMethod.Put), TimeSpan.FromSeconds(10));
            var put = handler.Seen.Single(s => s.Request.Method == HttpMethod.Put);
            Assert.Equal($"{Api}/projects/7781/merge_requests/412/merge", put.Request.RequestUri!.AbsolutePath);
            var form = HttpUtility.ParseQueryString(put.Body!);
            Assert.Equal("c0ffee1234567890c0ffee1234567890c0ffee12", form["sha"]);
            Assert.Equal("true", form["merge_when_pipeline_succeeds"]);
            Assert.Equal("true", form["should_remove_source_branch"]);   // the project's default
            Assert.NotNull(form["squash"]);
            session.PumpUntil(() => TreeHas(session, "merges when its pipeline succeeds"), TimeSpan.FromSeconds(10));
        }, handler);
    }

    [Fact]
    public void ARefusedMergeIsSaidUnderTheRow()
    {
        var handler = Routes()
            .MapJson($"PUT {Api}/projects/7781/merge_requests/412/merge", "{\"message\":\"405 Method Not Allowed\"}", 405);
        Hosted((_, session, app) =>
        {
            OpenDetail(session, app);
            session.PumpUntil(() => ById(session, "mr-merge") is not null, TimeSpan.FromSeconds(15));
            app.ChooseMerge(whenPipelineSucceeds: true);
            session.PumpUntil(() => TreeHas(session, "Merge !412 into main"), TimeSpan.FromSeconds(10));
            session.PumpFrames(2);
            session.Window.Click(Labelled(session, "Merge when pipeline succeeds"));
            session.PumpUntil(() => ById(session, "mr-action-failure") is not null, TimeSpan.FromSeconds(10));
            Assert.True(TreeHas(session, "GitLab refused: 405 Method Not Allowed"));
            // The row is usable again: nothing is left spinning.
            Assert.NotNull(ById(session, "mr-merge"));
        }, handler);
    }

    [Fact]
    public void MarkAsDraftRetitlesTheRequest()
    {
        var handler = Routes()
            .MapJson($"PUT {Api}/projects/7781/merge_requests/412",
                "{\"id\":90412,\"iid\":412,\"project_id\":7781,\"title\":\"Draft: PC-1042: refund splitting\",\"state\":\"opened\",\"draft\":true}");
        Hosted((_, session, app) =>
        {
            OpenDetail(session, app);
            session.PumpUntil(() => Named(session, "Mark as draft") is not null, TimeSpan.FromSeconds(15));
            session.PumpFrames(2);
            session.Window.Click(Named(session, "Mark as draft")!);
            session.PumpUntil(() => handler.Seen.Any(s => s.Request.Method == HttpMethod.Put), TimeSpan.FromSeconds(10));
            var put = handler.Seen.Single(s => s.Request.Method == HttpMethod.Put);
            Assert.Equal($"{Api}/projects/7781/merge_requests/412", put.Request.RequestUri!.AbsolutePath);
            Assert.Equal("Draft: PC-1042: refund splitting", HttpUtility.ParseQueryString(put.Body!)["title"]);
        }, handler);
    }

    [Fact]
    public void TheBellCountsTheTodosAndTheListMarksThemDone()
    {
        var handler = Routes()
            .Map($"POST {Api}/todos/61001/mark_as_done", _ => new HttpResponseMessage(System.Net.HttpStatusCode.Created))
            .Map($"POST {Api}/todos/mark_as_done", _ => new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        Hosted((_, session, app) =>
        {
            session.PumpUntil(() => ById(session, "todo-bell") is not null, TimeSpan.FromSeconds(15));
            Assert.Equal("To do: 2", ById(session, "todo-bell")!.AccessibleName);
            session.PumpFrames(2);
            session.Window.Click(ById(session, "todo-bell")!);
            session.PumpUntil(() => ById(session, "todos") is not null, TimeSpan.FromSeconds(10));
            session.PumpUntil(() => TreeHas(session, "REVIEW REQUESTED"), TimeSpan.FromSeconds(10));
            Assert.True(TreeHas(session, "!398"));
            Assert.True(TreeHas(session, "PIPELINE FAILED"));
            Assert.True(TreeHas(session, "acme/platform/infra"));

            session.PumpFrames(2);
            session.Window.Click(Named(session, "Done !398")!);
            session.PumpUntil(() => handler.Seen.Any(s => s.Request.RequestUri!.AbsolutePath.EndsWith("/todos/61001/mark_as_done")), TimeSpan.FromSeconds(10));

            session.PumpUntil(() => Named(session, "Mark all done") is not null, TimeSpan.FromSeconds(10));
            session.PumpFrames(2);
            session.Window.Click(Named(session, "Mark all done")!);
            session.PumpUntil(() => handler.Seen.Any(s => s.Request.RequestUri!.AbsolutePath.EndsWith("/todos/mark_as_done")), TimeSpan.FromSeconds(10));

            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(2);
            Assert.False(app.TodosOpen);
        }, handler);
    }

    [Fact]
    public void TheOverviewStripCountsReviewsAndOpensThePanelOnThem()
    {
        Hosted((_, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit1);
            // !398 names dosei as reviewer; !412 only as assignee.
            session.PumpUntil(() => TreeHas(session, "You have 1 merge request to review across 1 repository."), TimeSpan.FromSeconds(15));
            session.PumpFrames(2);
            session.Window.Click(Named(session, "Review them")!);
            session.PumpUntil(() => app.MergeRequestsOpen && TreeHas(session, "PC-1120 event sourcing POC"), TimeSpan.FromSeconds(15));
            Assert.False(TreeHas(session, "PC-1042: refund splitting"));   // Reviewing, not All open
        });
    }

    // ── Hosting ──────────────────────────────────────────────────────────────────────

    private static void OpenDetail(HeadlessSession session, JunctionApp app)
    {
        app.OpenMergeRequests(JunctionApp.MrFilter.AllOpen);
        session.PumpUntil(() => TreeHas(session, "!412"), TimeSpan.FromSeconds(15));
        session.Window.PressKey(KeyCode.Enter);
        session.PumpUntil(() => TreeHas(session, "Waiting for pipeline #4021 to finish."), TimeSpan.FromSeconds(15));
    }

    private static FakeGitLabHandler Routes() =>
        new FakeGitLabHandler()
            .Map($"GET {Api}/projects/acme%2Fcore-api", "project")
            .Map($"GET {Api}/projects/7781/protected_branches?per_page=100&page=1", "protected_branches")
            .Map(MrList, "merge_requests_page1")
            .Map($"GET {Api}/projects/7781/merge_requests/412/approvals", "approvals")
            .Map($"GET {Api}/projects/7781/merge_requests/412/discussions?per_page=100&page=1", "discussions")
            .MapJson($"GET {Api}/projects/7781/merge_requests/412/diffs?per_page=100&page=1", "[]")
            .MapJson($"GET {Api}/projects/7781/pipelines?order_by=updated_at&sort=desc&per_page=100&page=1", "[]")
            .Map(Todos, "todos");

    private static void Hosted(Action<TempRepo, HeadlessSession, JunctionApp> body, FakeGitLabHandler? handler = null)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Git("remote", "add", "origin", "https://gitlab.com/acme/core-api.git");
        handler ??= Routes();

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

    private static Element? ById(HeadlessSession session, string id) =>
        Root(session) is { } root
            ? LayoutProbe.Descendants<Element>(root, e => e.AutomationId == id).FirstOrDefault()
            : null;

    private static Element? Named(HeadlessSession session, string name) =>
        Root(session) is { } root
            ? LayoutProbe.Descendants<Element>(root, e => e.AccessibleName == name).FirstOrDefault()
            : null;

    /// <summary>A button by the text it shows — a click on the label reaches the button.</summary>
    private static Element Labelled(HeadlessSession session, string text) =>
        (Root(session) is { } root
            ? LayoutProbe.Descendants<TextElement>(root, t => t.Content == text).FirstOrDefault()
            : null) ?? throw new Xunit.Sdk.XunitException($"nothing on screen reads \"{text}\"");
}

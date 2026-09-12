using System.Net.Http;
using Junction.GitLab;
using Xunit;

namespace Junction.Tests.GitLab;

/// <summary>
/// The client against recorded answers. What is proved here is the contract the UI layers
/// build on: paths are encoded, lists follow <c>x-next-page</c> and stop, every failure comes
/// back classified with the server's own words, and the JSON GitLab actually sends — nulls,
/// strings-for-numbers, nested pipelines — lands in the models without a throw.
/// </summary>
public class GitLabClientTests
{
    private const string Api = "/api/v4";

    private static GitLabClient Client(FakeGitLabHandler handler, string? token = "glpat-test", GitLabTokenKind kind = GitLabTokenKind.Pat) =>
        new("https://gitlab.com", token, kind, handler);

    [Theory]
    [InlineData("https://gitlab.com", "https://gitlab.com/api/v4")]
    [InlineData("https://gitlab.com/", "https://gitlab.com/api/v4")]
    [InlineData("gitlab.com", "https://gitlab.com/api/v4")]
    [InlineData("GitLab.Com/api/v4", "https://gitlab.com/api/v4")]
    [InlineData("http://gitlab.lab:8080", "http://gitlab.lab:8080/api/v4")]
    [InlineData("http://gitlab.lab:8080/api/v4/", "http://gitlab.lab:8080/api/v4")]
    public void AnyWayOfWritingTheHostBecomesTheApiRoot(string given, string expected)
    {
        Assert.Equal(expected, GitLabClient.NormalizeApiBase(given));
    }

    [Fact]
    public async Task APatRidesInThePrivateTokenHeader()
    {
        var handler = new FakeGitLabHandler().Map($"GET {Api}/user", "user");
        using var client = Client(handler);

        var user = await client.GetCurrentUserAsync();

        Assert.True(user.Ok, user.Failure?.Message);
        Assert.Equal("dosei", user.Value.Username);
        Assert.Equal(42, user.Value.Id);
        var sent = handler.Seen.Single().Request;
        Assert.Equal("glpat-test", sent.Headers.GetValues("PRIVATE-TOKEN").Single());
        Assert.Null(sent.Headers.Authorization);
    }

    [Fact]
    public async Task AnOAuthTokenRidesAsBearer()
    {
        var handler = new FakeGitLabHandler().Map($"GET {Api}/user", "user");
        using var client = Client(handler, "gloas-abc", GitLabTokenKind.OAuth);

        await client.GetCurrentUserAsync();

        var sent = handler.Seen.Single().Request;
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.Equal("gloas-abc", sent.Headers.Authorization.Parameter);
        Assert.False(sent.Headers.Contains("PRIVATE-TOKEN"));
    }

    [Fact]
    public async Task AProjectIsAddressedByItsEncodedPath()
    {
        var handler = new FakeGitLabHandler().Map($"GET {Api}/projects/acme%2Fplatform%2Finfra", "project");
        using var client = Client(handler);

        var project = await client.GetProjectAsync("acme/platform/infra");

        Assert.True(project.Ok, project.Failure?.Message);
        Assert.Equal(7781, project.Value.Id);
        Assert.Equal("main", project.Value.DefaultBranch);
        Assert.True(project.Value.SquashByDefault);
        Assert.True(project.Value.RemoveSourceBranchAfterMerge);
        Assert.Equal(40, project.Value.Permissions!.ProjectAccess!.AccessLevel);
    }

    [Fact]
    public async Task ListsFollowXNextPageAndStopWhenItIsGone()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/projects/7781/merge_requests?state=opened&order_by=updated_at&sort=desc&with_merge_status_recheck=true&per_page=100&page=1",
                "merge_requests_page1", 200, ("x-next-page", "2"), ("x-page", "1"))
            .Map($"GET {Api}/projects/7781/merge_requests?state=opened&order_by=updated_at&sort=desc&with_merge_status_recheck=true&per_page=100&page=2",
                "merge_requests_page2", 200, ("x-next-page", ""), ("x-page", "2"));
        using var client = Client(handler);

        var mrs = await client.ListMergeRequestsAsync(7781);

        Assert.True(mrs.Ok, mrs.Failure?.Message);
        Assert.Equal([412L, 398L, 377L], mrs.Value.Select(m => m.Iid));
        Assert.Equal(2, handler.Seen.Count);
    }

    [Fact]
    public async Task APageCapEndsTheWalkEarly()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/projects/7781/merge_requests?state=opened&order_by=updated_at&sort=desc&with_merge_status_recheck=true&per_page=100&page=1",
                "merge_requests_page1", 200, ("x-next-page", "2"));
        using var client = Client(handler);

        var mrs = await client.ListMergeRequestsAsync(7781, maxPages: 1);

        Assert.True(mrs.Ok);
        Assert.Equal(2, mrs.Value.Count);
        Assert.Single(handler.Seen);
    }

    [Fact]
    public async Task TheQueryCarriesEveryFilterEscaped()
    {
        var handler = new FakeGitLabHandler();
        using var client = Client(handler);

        await client.ListMergeRequestsAsync(7781, new MergeRequestQuery(
            Scope: "reviews_for_me",
            SourceBranch: "feature/2.4.0/payments/PC-1042",
            Search: "refund splitting",
            UpdatedAfter: new DateTimeOffset(2026, 9, 1, 12, 30, 0, TimeSpan.FromHours(2))));

        var query = handler.Seen.Single().Request.RequestUri!.Query;
        Assert.Contains("scope=reviews_for_me", query);
        Assert.Contains("source_branch=feature%2F2.4.0%2Fpayments%2FPC-1042", query);
        Assert.Contains("search=refund%20splitting", query);
        Assert.Contains("updated_after=2026-09-01T10%3A30%3A00Z", query);   // UTC, not the +02:00 it was given in
    }

    [Fact]
    public async Task AMergeRequestArrivesWithItsStatusPipelineReviewersAndDiffRefs()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/projects/7781/merge_requests/412", _ =>
                FakeGitLabHandler.Json(FakeGitLabHandler.Recording("merge_requests_page1").TrimStart('[').Split(",\n{")[0]));
        using var client = Client(handler);

        var mr = await client.GetMergeRequestAsync(7781, 412);

        Assert.True(mr.Ok, mr.Failure?.Message);
        var m = mr.Value;
        Assert.Equal("ci_still_running", m.DetailedMergeStatus);
        Assert.Equal("running", m.HeadPipeline!.Status);
        Assert.Equal(4021, m.HeadPipeline.Id);
        Assert.Equal(["mkline", "tbrandt"], m.Reviewers.Select(r => r.Username));
        Assert.Equal("aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111", m.DiffRefs!.StartSha);
        Assert.Equal("c0ffee1234567890c0ffee1234567890c0ffee12", m.DiffRefs.HeadSha);
        Assert.Equal("5", m.ChangesCount);
        Assert.True(m.Squash);
        Assert.True(m.User!.CanMerge);
        Assert.Contains("Closes #1042", m.Description);
        Assert.True(m.IsOpen);
    }

    [Fact]
    public async Task ANullPipelineAndAMissingDescriptionDoNotBreakTheList()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/projects/7781/merge_requests?state=opened&order_by=updated_at&sort=desc&with_merge_status_recheck=true&per_page=100&page=1",
                "merge_requests_page2");
        using var client = Client(handler);

        var mrs = await client.ListMergeRequestsAsync(7781);

        Assert.True(mrs.Ok, mrs.Failure?.Message);
        var m = mrs.Value.Single();
        Assert.Null(m.HeadPipeline);
        Assert.Null(m.Description);
        Assert.True(m.HasConflicts);
        Assert.Equal("conflict", m.DetailedMergeStatus);
    }

    [Fact]
    public async Task ApprovalsCountWhoApproved()
    {
        var handler = new FakeGitLabHandler().Map($"GET {Api}/projects/7781/merge_requests/412/approvals", "approvals");
        using var client = Client(handler);

        var a = await client.GetMergeRequestApprovalsAsync(7781, 412);

        Assert.True(a.Ok);
        Assert.False(a.Value.Approved);
        Assert.Equal(2, a.Value.ApprovalsRequired);
        Assert.Equal(1, a.Value.ApprovedCount);
        Assert.Equal("mkline", a.Value.ApprovedBy[0].User!.Username);
    }

    [Fact]
    public async Task AMultiLineDiffThreadCarriesItsRangeAndASystemNoteIsMarked()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/projects/7781/merge_requests/412/discussions?per_page=100&page=1", "discussions");
        using var client = Client(handler);

        var res = await client.ListMergeRequestDiscussionsAsync(7781, 412);

        Assert.True(res.Ok, res.Failure?.Message);
        var threads = res.Value;
        Assert.Equal(3, threads.Count);

        var range = threads[0];
        Assert.False(range.IsResolved);
        Assert.Equal(2, range.Notes.Count);
        var pos = range.Position!;
        Assert.Equal("text", pos.PositionType);
        Assert.Equal(23, pos.NewLine);
        Assert.Equal("new", pos.LineRange!.Start!.Type);
        Assert.Equal(23, pos.LineRange.Start.NewLine);
        Assert.Equal(25, pos.LineRange.End!.NewLine);
        Assert.Equal("adc83b19e793491b1c6ea0fd8b46cd9f32e292fc_0_25", pos.LineRange.End.LineCode);

        var resolved = threads[1];
        Assert.True(resolved.IsResolved);
        Assert.Equal("dosei", resolved.First!.ResolvedBy!.Username);
        Assert.Equal(3, resolved.Position!.OldLine);
        Assert.Null(resolved.Position.NewLine);

        Assert.True(threads[2].IndividualNote);
        Assert.True(threads[2].First!.System);
    }

    [Fact]
    public async Task CommitStatusesAndTodosParse()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/projects/7781/repository/commits/c0ffee1234567890c0ffee1234567890c0ffee12/statuses?per_page=100&page=1", "commit_statuses")
            .Map($"GET {Api}/todos?state=pending&per_page=100&page=1", "todos")
            .Map($"GET {Api}/projects/7781/protected_branches?per_page=100&page=1", "protected_branches");
        using var client = Client(handler);

        var statuses = await client.GetCommitStatusesAsync(7781, "c0ffee1234567890c0ffee1234567890c0ffee12");
        var todos = await client.ListPendingTodosAsync();
        var protectedBranches = await client.GetProtectedBranchesAsync(7781);

        Assert.True(statuses.Ok, statuses.Failure?.Message);
        Assert.Equal(["running", "success"], statuses.Value.Select(s => s.Status));
        Assert.Equal(4021, statuses.Value[0].PipelineId);

        Assert.True(todos.Ok, todos.Failure?.Message);
        Assert.Equal("review_requested", todos.Value[0].ActionName);
        Assert.Equal(398, todos.Value[0].Target!.Iid);
        Assert.Equal("acme/platform/infra", todos.Value[0].Project!.PathWithNamespace);

        Assert.True(protectedBranches.Ok, protectedBranches.Failure?.Message);
        var main = protectedBranches.Value[0];
        Assert.Equal(40, main.PushLevel);
        Assert.False(main.AllowForcePush);
        var release = protectedBranches.Value[1];
        Assert.Equal(0, release.PushLevel);
        Assert.True(release.Matches("release/2.3.1"));
        Assert.True(release.Matches("release/2.4/hotfix"));
        Assert.False(release.Matches("releases/2.3.1"));
        Assert.True(main.Matches("main"));
        Assert.False(main.Matches("main-2"));
    }

    [Fact]
    public async Task A401IsUnauthorizedWithTheServersWords()
    {
        var handler = new FakeGitLabHandler().Map($"GET {Api}/user", "error_401", 401);
        using var client = Client(handler);

        var user = await client.GetCurrentUserAsync();

        Assert.False(user.Ok);
        Assert.Equal(GitLabFailureKind.Unauthorized, user.Failure!.Kind);
        Assert.Equal(401, user.Failure.Status);
        Assert.Equal("401 Unauthorized", user.Failure.Message);
        Assert.Equal("Sign in again", user.Failure.UserMessage("gitlab.com"));
        Assert.Throws<InvalidOperationException>(() => user.Value);
    }

    [Fact]
    public async Task A429CarriesRetryAfter()
    {
        var handler = new FakeGitLabHandler().MapJson($"GET {Api}/user", "{\"message\":\"Retry later\"}", 429, ("Retry-After", "42"));
        using var client = Client(handler);

        var user = await client.GetCurrentUserAsync();

        Assert.Equal(GitLabFailureKind.RateLimited, user.Failure!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(42), user.Failure.RetryAfter);
        Assert.Equal("gitlab.com asked us to slow down — retrying in 42 s", user.Failure.UserMessage("gitlab.com"));
    }

    [Fact]
    public async Task AValidationErrorKeepsTheFieldsGitLabNamed()
    {
        var handler = new FakeGitLabHandler().Map($"GET {Api}/user", "error_validation", 400);
        using var client = Client(handler);

        var user = await client.GetCurrentUserAsync();

        Assert.Equal(GitLabFailureKind.Unprocessable, user.Failure!.Kind);
        Assert.Contains("target_branch", user.Failure.Message);
    }

    [Fact]
    public async Task AHostThatIsNotGitLabIsInvalidNotACrash()
    {
        var handler = new FakeGitLabHandler().Map($"GET {Api}/user", _ => FakeGitLabHandler.Html("<html><body>Login</body></html>"));
        using var client = Client(handler);

        var user = await client.GetCurrentUserAsync();

        Assert.Equal(GitLabFailureKind.Invalid, user.Failure!.Kind);
        Assert.Contains("gitlab.com", user.Failure.Message);
    }

    [Fact]
    public async Task AnUnreachableHostIsANetworkFailure()
    {
        var handler = new FakeGitLabHandler().Throw($"GET {Api}/user",
            new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known."));
        using var client = Client(handler);

        var user = await client.GetCurrentUserAsync();

        Assert.Equal(GitLabFailureKind.Network, user.Failure!.Kind);
        Assert.Equal("Could not resolve gitlab.com", user.Failure.Message);
    }

    [Fact]
    public async Task AFailedLaterPageFailsTheWholeList()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/projects/7781/merge_requests?state=opened&order_by=updated_at&sort=desc&with_merge_status_recheck=true&per_page=100&page=1",
                "merge_requests_page1", 200, ("x-next-page", "2"))
            .MapJson($"GET {Api}/projects/7781/merge_requests?state=opened&order_by=updated_at&sort=desc&with_merge_status_recheck=true&per_page=100&page=2",
                "{\"message\":\"500 Internal Server Error\"}", 500);
        using var client = Client(handler);

        var mrs = await client.ListMergeRequestsAsync(7781);

        Assert.False(mrs.Ok);
        Assert.Equal(GitLabFailureKind.Server, mrs.Failure!.Kind);
    }

    [Fact]
    public async Task TheCallersOwnCancellationPropagates()
    {
        var handler = new FakeGitLabHandler().Map($"GET {Api}/user", "user");
        using var client = Client(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetCurrentUserAsync(cts.Token));
    }
}

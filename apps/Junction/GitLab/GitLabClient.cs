using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Junction.GitLab;

/// <summary>How a token is presented to the API.</summary>
public enum GitLabTokenKind
{
    /// <summary>Personal access token — <c>PRIVATE-TOKEN</c> header.</summary>
    Pat,
    /// <summary>OAuth access token — <c>Authorization: Bearer</c>.</summary>
    OAuth,
}

/// <summary>Filters for <see cref="GitLabClient.ListMergeRequestsAsync"/>.</summary>
/// <param name="Scope"><c>created_by_me · assigned_to_me · reviews_for_me · all</c>.</param>
public sealed record MergeRequestQuery(
    string State = "opened",
    string? Scope = null,
    string? SourceBranch = null,
    string? TargetBranch = null,
    string? Search = null,
    DateTimeOffset? UpdatedAfter = null,
    string OrderBy = "updated_at",
    string Sort = "desc");

/// <summary>What <see cref="GitLabClient.CreateMergeRequestAsync"/> sends. Null means "leave
/// it to the project's defaults" for the flags GitLab defaults itself.</summary>
public sealed record NewMergeRequest(
    string SourceBranch,
    string TargetBranch,
    string Title,
    string? Description = null,
    bool Draft = false,
    IReadOnlyList<long>? ReviewerIds = null,
    IReadOnlyList<long>? AssigneeIds = null,
    IReadOnlyList<string>? Labels = null,
    long? MilestoneId = null,
    bool? RemoveSourceBranch = null,
    bool? Squash = null);

/// <summary>Filters for <see cref="GitLabClient.ListIssuesAsync"/>.</summary>
public sealed record IssueQuery(
    string State = "opened",
    string? Search = null,
    string? Labels = null,
    string? AssigneeUsername = null,
    DateTimeOffset? UpdatedAfter = null,
    string OrderBy = "updated_at",
    string Sort = "desc");

/// <summary>
/// One GitLab host, one token. Every call returns a <see cref="GitLabResult{T}"/> — an HTTP
/// error status is a classified failure, not an exception; only the caller's own cancellation
/// propagates. Nothing here retries: a 429 comes back with its <c>Retry-After</c> so the UI
/// can say when, and a stale token comes back as <see cref="GitLabFailureKind.Unauthorized"/>
/// so the accounts layer can refresh or re-prompt.
/// </summary>
/// <remarks>
/// Lists page at the API's maximum of 100 and follow <c>x-next-page</c>; the totals headers
/// are not read because GitLab stops sending them past 10,000 rows. A page cap keeps a
/// pathological project from being walked forever — the default 10 pages is a thousand
/// rows, which is more than any list in Junction shows before it filters.
/// </remarks>
public sealed class GitLabClient : IDisposable
{
    public const int PerPage = 100;
    public const int DefaultMaxPages = 10;

    private readonly HttpClient _http;
    // Downloads: no automatic redirects and no token on the client, so the token is added
    // per hop and only to this host. See GetBytesAsync.
    private readonly HttpClient _raw;
    private readonly string? _token;
    private readonly GitLabTokenKind _kind;
    private readonly string _apiBase;

    /// <summary>Host name (no scheme, no port) — what accounts and the UI key on.</summary>
    public string Host { get; }

    /// <summary>The API root, no trailing slash: <c>https://gitlab.com/api/v4</c>.</summary>
    public string ApiBaseUrl => _apiBase;

    /// <summary>The instance's web root: <c>https://gitlab.com</c>.</summary>
    public string WebBaseUrl => _apiBase[..^"/api/v4".Length];

    public GitLabClient(string apiBaseUrl, string? token, GitLabTokenKind kind = GitLabTokenKind.Pat,
        HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _apiBase = NormalizeApiBase(apiBaseUrl);
        Host = new Uri(_apiBase).Host;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = timeout ?? TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Junction/1.0 (+https://github.com/fredrikhu/hultui)");
        if (!string.IsNullOrEmpty(token))
        {
            if (kind == GitLabTokenKind.Pat) _http.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token);
            else _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        _token = token;
        _kind = kind;
        _raw = handler is null
            ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            : new HttpClient(handler, disposeHandler: false);
        _raw.Timeout = _http.Timeout;
        _raw.DefaultRequestHeaders.UserAgent.ParseAdd("Junction/1.0 (+https://github.com/fredrikhu/hultui)");
    }

    /// <summary>
    /// <c>https://gitlab.com</c>, <c>gitlab.lab:8080/</c>, <c>https://host/api/v4</c> — all
    /// become <c>scheme://host[:port]/api/v4</c>. A bare host gets <c>https</c>.
    /// </summary>
    public static string NormalizeApiBase(string url)
    {
        var s = url.Trim().TrimEnd('/');
        if (!s.Contains("://", StringComparison.Ordinal)) s = "https://" + s;
        if (s.EndsWith("/api/v4", StringComparison.OrdinalIgnoreCase)) s = s[..^"/api/v4".Length];
        var uri = new Uri(s, UriKind.Absolute);
        var port = uri.IsDefaultPort ? "" : ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
        return $"{uri.Scheme}://{uri.Host.ToLowerInvariant()}{port}/api/v4";
    }

    // ── Identity ─────────────────────────────────────────────────────────────────────

    public Task<GitLabResult<GlUser>> GetCurrentUserAsync(CancellationToken ct = default) =>
        GetAsync("user", GitLabJson.Default.GlUser, ct);

    /// <summary>Scopes and expiry of the token in use. 404 for an OAuth token.</summary>
    public Task<GitLabResult<GlTokenInfo>> ProbeTokenAsync(CancellationToken ct = default) =>
        GetAsync("personal_access_tokens/self", GitLabJson.Default.GlTokenInfo, ct);

    /// <summary>A new token in place of the one in use, which is revoked in the same call.
    /// Needs the <c>api</c> or <c>self_rotate</c> scope; the answer's <see cref="GlTokenInfo.Token"/>
    /// is the only time the new value is shown.</summary>
    public async Task<GitLabResult<GlTokenInfo>> RotateTokenAsync(CancellationToken ct = default)
    {
        var res = await SendAsync(HttpMethod.Post, "personal_access_tokens/self/rotate", null, GitLabJson.Default.GlTokenInfo, ct).ConfigureAwait(false);
        return res.Ok ? GitLabResult<GlTokenInfo>.Success(res.Value.Body) : res.As<GlTokenInfo>();
    }

    // ── Projects ─────────────────────────────────────────────────────────────────────

    public Task<GitLabResult<GlProject>> GetProjectAsync(string projectPath, CancellationToken ct = default) =>
        GetAsync("projects/" + Uri.EscapeDataString(projectPath), GitLabJson.Default.GlProject, ct);

    public Task<GitLabResult<GlProject>> GetProjectAsync(long projectId, CancellationToken ct = default) =>
        GetAsync($"projects/{projectId}", GitLabJson.Default.GlProject, ct);

    public Task<GitLabResult<List<GlProtectedBranch>>> GetProtectedBranchesAsync(long projectId, CancellationToken ct = default) =>
        GetPagedAsync($"projects/{projectId}/protected_branches", "", GitLabJson.Default.ListGlProtectedBranch, DefaultMaxPages, ct);

    /// <summary>Projects the user is a member of, for the clone browser.
    /// <paramref name="minAccessLevel"/>: 10 guest · 20 reporter · 30 developer · 40 maintainer.</summary>
    public Task<GitLabResult<List<GlProject>>> ListMembershipProjectsAsync(int minAccessLevel = 30, string? search = null,
        int maxPages = DefaultMaxPages, CancellationToken ct = default)
    {
        var q = new Query()
            .Add("membership", "true")
            .Add("min_access_level", minAccessLevel)
            .Add("order_by", "last_activity_at")
            .Add("sort", "desc")
            .Add("simple", "true")
            .Add("search", search);
        return GetPagedAsync("projects", q.ToString(), GitLabJson.Default.ListGlProject, maxPages, ct);
    }

    // ── Merge requests ───────────────────────────────────────────────────────────────

    public Task<GitLabResult<List<GlMergeRequest>>> ListMergeRequestsAsync(long projectId, MergeRequestQuery? query = null,
        int maxPages = DefaultMaxPages, CancellationToken ct = default)
    {
        query ??= new MergeRequestQuery();
        var q = new Query()
            .Add("state", query.State)
            .Add("scope", query.Scope)
            .Add("source_branch", query.SourceBranch)
            .Add("target_branch", query.TargetBranch)
            .Add("search", query.Search)
            .Add("updated_after", query.UpdatedAfter)
            .Add("order_by", query.OrderBy)
            .Add("sort", query.Sort)
            .Add("with_merge_status_recheck", "true");
        return GetPagedAsync($"projects/{projectId}/merge_requests", q.ToString(), GitLabJson.Default.ListGlMergeRequest, maxPages, ct);
    }

    /// <summary><c>POST /projects/:id/merge_requests</c>. A title starting with <c>Draft:</c>
    /// is what GitLab reads as a draft; the <c>draft</c> flag is sent as well for newer
    /// instances. Reviewers, assignees and labels are sent only when given.</summary>
    public async Task<GitLabResult<GlMergeRequest>> CreateMergeRequestAsync(long projectId, NewMergeRequest mr, CancellationToken ct = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("source_branch", mr.SourceBranch),
            new("target_branch", mr.TargetBranch),
            new("title", mr.Draft && !mr.Title.StartsWith("Draft:", StringComparison.OrdinalIgnoreCase) ? "Draft: " + mr.Title : mr.Title),
        };
        if (mr.Description is { Length: > 0 } d) fields.Add(new("description", d));
        if (mr.Draft) fields.Add(new("draft", "true"));
        if (mr.ReviewerIds is { Count: > 0 } reviewers) foreach (var id in reviewers) fields.Add(new("reviewer_ids[]", id.ToString(CultureInfo.InvariantCulture)));
        if (mr.AssigneeIds is { Count: > 0 } assignees) foreach (var id in assignees) fields.Add(new("assignee_ids[]", id.ToString(CultureInfo.InvariantCulture)));
        if (mr.Labels is { Count: > 0 } labels) fields.Add(new("labels", string.Join(",", labels)));
        if (mr.MilestoneId is { } m) fields.Add(new("milestone_id", m.ToString(CultureInfo.InvariantCulture)));
        if (mr.RemoveSourceBranch is { } rsb) fields.Add(new("remove_source_branch", rsb ? "true" : "false"));
        if (mr.Squash is { } sq) fields.Add(new("squash", sq ? "true" : "false"));
        using var content = new FormUrlEncodedContent(fields);
        var res = await SendAsync(HttpMethod.Post, $"projects/{projectId}/merge_requests", content, GitLabJson.Default.GlMergeRequest, ct).ConfigureAwait(false);
        return res.Ok ? GitLabResult<GlMergeRequest>.Success(res.Value.Body) : res.As<GlMergeRequest>();
    }

    /// <summary>Everyone with access, inherited included — the reviewer picker's pool.</summary>
    public Task<GitLabResult<List<GlMember>>> ListMembersAsync(long projectId, string? search = null,
        int maxPages = DefaultMaxPages, CancellationToken ct = default) =>
        GetPagedAsync($"projects/{projectId}/members/all", new Query().Add("search", search).ToString(), GitLabJson.Default.ListGlMember, maxPages, ct);

    public Task<GitLabResult<List<GlLabel>>> ListLabelsAsync(long projectId, int maxPages = DefaultMaxPages, CancellationToken ct = default) =>
        GetPagedAsync($"projects/{projectId}/labels", new Query().Add("with_counts", "false").ToString(), GitLabJson.Default.ListGlLabel, maxPages, ct);

    public Task<GitLabResult<GlMergeRequest>> GetMergeRequestAsync(long projectId, long iid, CancellationToken ct = default) =>
        GetAsync($"projects/{projectId}/merge_requests/{iid}", GitLabJson.Default.GlMergeRequest, ct);

    public Task<GitLabResult<GlApprovals>> GetMergeRequestApprovalsAsync(long projectId, long iid, CancellationToken ct = default) =>
        GetAsync($"projects/{projectId}/merge_requests/{iid}/approvals", GitLabJson.Default.GlApprovals, ct);

    public Task<GitLabResult<List<GlDiscussion>>> ListMergeRequestDiscussionsAsync(long projectId, long iid,
        int maxPages = DefaultMaxPages, CancellationToken ct = default) =>
        GetPagedAsync($"projects/{projectId}/merge_requests/{iid}/discussions", "", GitLabJson.Default.ListGlDiscussion, maxPages, ct);

    /// <summary>The files a merge request touches. Without <paramref name="unidiff"/> the
    /// per-file <c>diff</c> text still comes along; the list view only reads the paths.</summary>
    public Task<GitLabResult<List<GlDiff>>> GetMergeRequestDiffsAsync(long projectId, long iid,
        int maxPages = DefaultMaxPages, CancellationToken ct = default) =>
        GetPagedAsync($"projects/{projectId}/merge_requests/{iid}/diffs", "", GitLabJson.Default.ListGlDiff, maxPages, ct);

    public Task<GitLabResult<List<GlDraftNote>>> ListDraftNotesAsync(long projectId, long iid, CancellationToken ct = default) =>
        GetPagedAsync($"projects/{projectId}/merge_requests/{iid}/draft_notes", "", GitLabJson.Default.ListGlDraftNote, DefaultMaxPages, ct);

    // ── Review: threads, replies, drafts ────────────────────────────────────────────

    /// <summary>Starts a thread — on a line or a range of the diff when <paramref name="position"/>
    /// is given (its three SHAs must be the request's <c>diff_refs</c>), on the request itself
    /// otherwise. Published at once; see <see cref="CreateDraftNoteAsync"/> for a review.</summary>
    public Task<GitLabResult<GlDiscussion>> CreateDiscussionAsync(long projectId, long iid, string body, GlPosition? position,
        CancellationToken ct = default)
    {
        var fields = new List<KeyValuePair<string, string>> { new("body", body) };
        AddPositionFields(fields, position);
        return PostFormAsync(HttpMethod.Post, $"projects/{projectId}/merge_requests/{iid}/discussions", fields, GitLabJson.Default.GlDiscussion, ct);
    }

    /// <summary>A reply in an existing thread.</summary>
    public Task<GitLabResult<GlNote>> ReplyToDiscussionAsync(long projectId, long iid, string discussionId, string body,
        CancellationToken ct = default) =>
        PostFormAsync(HttpMethod.Post, $"projects/{projectId}/merge_requests/{iid}/discussions/{discussionId}/notes",
            [new("body", body)], GitLabJson.Default.GlNote, ct);

    /// <summary>Resolves or reopens a thread.</summary>
    public Task<GitLabResult<GlDiscussion>> ResolveDiscussionAsync(long projectId, long iid, string discussionId, bool resolved,
        CancellationToken ct = default) =>
        PostFormAsync(HttpMethod.Put, $"projects/{projectId}/merge_requests/{iid}/discussions/{discussionId}",
            [new("resolved", resolved ? "true" : "false")], GitLabJson.Default.GlDiscussion, ct);

    /// <summary>A note nobody else sees until the review is submitted — a new thread when
    /// <paramref name="inReplyToDiscussionId"/> is null, a reply otherwise.</summary>
    public Task<GitLabResult<GlDraftNote>> CreateDraftNoteAsync(long projectId, long iid, string note, GlPosition? position,
        string? inReplyToDiscussionId = null, bool resolveDiscussion = false, CancellationToken ct = default)
    {
        var fields = new List<KeyValuePair<string, string>> { new("note", note) };
        if (inReplyToDiscussionId is { Length: > 0 } d) fields.Add(new("in_reply_to_discussion_id", d));
        if (resolveDiscussion) fields.Add(new("resolve_discussion", "true"));
        AddPositionFields(fields, position);
        return PostFormAsync(HttpMethod.Post, $"projects/{projectId}/merge_requests/{iid}/draft_notes", fields, GitLabJson.Default.GlDraftNote, ct);
    }

    public Task<GitLabResult<bool>> DeleteDraftNoteAsync(long projectId, long iid, long draftNoteId, CancellationToken ct = default) =>
        SendStatusAsync(HttpMethod.Delete, $"projects/{projectId}/merge_requests/{iid}/draft_notes/{draftNoteId}", null, ct);

    /// <summary>Publishes every pending draft note of the signed-in user as one review.
    /// <paramref name="reviewerState"/> is <c>requested_changes</c> or <c>reviewed</c>, or
    /// null to leave the reviewer state alone.</summary>
    public Task<GitLabResult<bool>> BulkPublishDraftNotesAsync(long projectId, long iid, string? reviewerState = null,
        CancellationToken ct = default)
    {
        var fields = new List<KeyValuePair<string, string>>();
        if (reviewerState is { Length: > 0 } s) fields.Add(new("reviewer_state", s));
        return SendStatusAsync(HttpMethod.Post, $"projects/{projectId}/merge_requests/{iid}/draft_notes/bulk_publish",
            new FormUrlEncodedContent(fields), ct);
    }

    /// <summary>Approves the request; <paramref name="sha"/> guards against approving a head
    /// that moved (409).</summary>
    public Task<GitLabResult<GlApprovals>> ApproveAsync(long projectId, long iid, string? sha = null, CancellationToken ct = default)
    {
        var fields = new List<KeyValuePair<string, string>>();
        if (sha is { Length: > 0 } s) fields.Add(new("sha", s));
        return PostFormAsync(HttpMethod.Post, $"projects/{projectId}/merge_requests/{iid}/approve", fields, GitLabJson.Default.GlApprovals, ct);
    }

    // ── Acting on a request: approvals, merge, rebase, draft ────────────────────────

    /// <summary>Takes the signed-in user's approval back.</summary>
    public Task<GitLabResult<bool>> UnapproveAsync(long projectId, long iid, CancellationToken ct = default) =>
        SendStatusAsync(HttpMethod.Post, $"projects/{projectId}/merge_requests/{iid}/unapprove", new FormUrlEncodedContent([]), ct);

    /// <summary>Merges — now, or when the pipeline succeeds. <paramref name="sha"/> is the
    /// head the reader saw; a head that moved since answers 409. 405 is "not mergeable in this
    /// state", 406 "already merged".</summary>
    public Task<GitLabResult<GlMergeRequest>> MergeAsync(long projectId, long iid, string? sha, bool squash, bool removeSourceBranch,
        bool whenPipelineSucceeds, CancellationToken ct = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("squash", squash ? "true" : "false"),
            new("should_remove_source_branch", removeSourceBranch ? "true" : "false"),
            new("merge_when_pipeline_succeeds", whenPipelineSucceeds ? "true" : "false"),
        };
        if (sha is { Length: > 0 } s) fields.Add(new("sha", s));
        return PostFormAsync(HttpMethod.Put, $"projects/{projectId}/merge_requests/{iid}/merge", fields, GitLabJson.Default.GlMergeRequest, ct);
    }

    /// <summary>Asks GitLab to rebase the source onto the target. Asynchronous on the server:
    /// poll <see cref="GetMergeRequestAsync"/> until <c>rebase_in_progress</c> clears, then
    /// read <c>merge_error</c>.</summary>
    public Task<GitLabResult<bool>> RebaseAsync(long projectId, long iid, CancellationToken ct = default) =>
        SendStatusAsync(HttpMethod.Put, $"projects/{projectId}/merge_requests/{iid}/rebase", new FormUrlEncodedContent([]), ct);

    /// <summary>Retitles the request — how draft is toggled: the <c>Draft:</c> prefix is the
    /// flag.</summary>
    public Task<GitLabResult<GlMergeRequest>> UpdateTitleAsync(long projectId, long iid, string title, CancellationToken ct = default) =>
        PostFormAsync(HttpMethod.Put, $"projects/{projectId}/merge_requests/{iid}", [new("title", title)], GitLabJson.Default.GlMergeRequest, ct);

    // ── Todos ───────────────────────────────────────────────────────────────────────

    public Task<GitLabResult<bool>> MarkTodoDoneAsync(long todoId, CancellationToken ct = default) =>
        SendStatusAsync(HttpMethod.Post, $"todos/{todoId}/mark_as_done", new FormUrlEncodedContent([]), ct);

    public Task<GitLabResult<bool>> MarkAllTodosDoneAsync(CancellationToken ct = default) =>
        SendStatusAsync(HttpMethod.Post, "todos/mark_as_done", new FormUrlEncodedContent([]), ct);

    /// <summary>The <c>position[...]</c> form fields of a diff note: the three SHAs, both
    /// paths, one line for a single-line note, and <c>line_range</c> for a range — each end
    /// carrying its <c>line_code</c> (<c>sha1(path)_old_new</c>) and side.</summary>
    internal static void AddPositionFields(List<KeyValuePair<string, string>> fields, GlPosition? position)
    {
        if (position is null) return;
        fields.Add(new("position[position_type]", position.PositionType ?? "text"));
        if (position.BaseSha is { } b) fields.Add(new("position[base_sha]", b));
        if (position.HeadSha is { } h) fields.Add(new("position[head_sha]", h));
        if (position.StartSha is { } s) fields.Add(new("position[start_sha]", s));
        if (position.NewPath is { } np) fields.Add(new("position[new_path]", np));
        if (position.OldPath is { } op) fields.Add(new("position[old_path]", op));
        if (position.NewLine is { } nl) fields.Add(new("position[new_line]", nl.ToString(CultureInfo.InvariantCulture)));
        if (position.OldLine is { } ol) fields.Add(new("position[old_line]", ol.ToString(CultureInfo.InvariantCulture)));
        if (position.LineRange is { Start: { } start, End: { } end })
        {
            AddLineFields("start", start);
            AddLineFields("end", end);
        }

        void AddLineFields(string which, GlLinePosition line)
        {
            if (line.LineCode is { } code) fields.Add(new($"position[line_range][{which}][line_code]", code));
            if (line.Type is { } type) fields.Add(new($"position[line_range][{which}][type]", type));
            if (line.OldLine is { } o) fields.Add(new($"position[line_range][{which}][old_line]", o.ToString(CultureInfo.InvariantCulture)));
            if (line.NewLine is { } n) fields.Add(new($"position[line_range][{which}][new_line]", n.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private async Task<GitLabResult<T>> PostFormAsync<T>(HttpMethod method, string path, List<KeyValuePair<string, string>> fields,
        JsonTypeInfo<T> type, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(fields);
        var res = await SendAsync(method, path, content, type, ct).ConfigureAwait(false);
        return res.Ok ? GitLabResult<T>.Success(res.Value.Body) : res.As<T>();
    }

    /// <summary>A call whose answer is its status — a delete, a bulk publish — where a 2xx
    /// with no body is the success case rather than "Empty response".</summary>
    private async Task<GitLabResult<bool>> SendStatusAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, _apiBase + "/" + path) { Content = content };
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return GitLabResult<bool>.Fail(Classify(ex));
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return GitLabResult<bool>.Fail(await FailureFromAsync(response, ct).ConfigureAwait(false));
            return GitLabResult<bool>.Success(true);
        }
    }

    // ── Uploads ──────────────────────────────────────────────────────────────────────

    /// <summary>The largest upload read into memory — a screenshot is a few hundred KB; this
    /// is for the one that is a 40 MB video someone pasted.</summary>
    public const long MaxUploadBytes = 16 * 1024 * 1024;

    /// <summary>
    /// A file pasted into a description or a note — what markdown links as
    /// <c>/uploads/&lt;secret&gt;/&lt;file&gt;</c>. GitLab 17.4 and later serve these to the API with
    /// any role from Guest up; the web URL wants a browser session instead, so for a private
    /// project this is the only way in with a token. An older instance answers 404.
    /// <paramref name="filename"/> is taken as it appears in the URL, already escaped.
    /// </summary>
    public Task<GitLabResult<byte[]>> GetUploadAsync(long projectId, string secret, string filename, CancellationToken ct = default) =>
        GetBytesAsync($"projects/{projectId}/uploads/{Uri.EscapeDataString(secret)}/{filename}", MaxUploadBytes, ct);

    /// <summary>
    /// A file's bytes. Redirects are followed here, not by the handler: an instance with
    /// object storage answers a download with a 302 to the bucket, and an automatic redirect
    /// carries every header but <c>Authorization</c> along — <c>PRIVATE-TOKEN</c> included — to
    /// a host that has no business seeing it. So the token rides only on a hop to this
    /// instance's own origin.
    /// </summary>
    private async Task<GitLabResult<byte[]>> GetBytesAsync(string path, long maxBytes, CancellationToken ct)
    {
        var origin = new Uri(_apiBase);
        var uri = new Uri(_apiBase + "/" + path);
        HttpResponseMessage? response = null;
        try
        {
            for (int hop = 0; ; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
                if (!string.IsNullOrEmpty(_token) && Uri.Compare(uri, origin, UriComponents.SchemeAndServer,
                        UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (_kind == GitLabTokenKind.Pat) request.Headers.Add("PRIVATE-TOKEN", _token);
                    else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                }
                response = await _raw.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is not { } location) break;
                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                response = null;
                if (hop >= 4 || next.Scheme is not ("https" or "http"))
                    return GitLabResult<byte[]>.Fail(new GitLabFailure(GitLabFailureKind.Invalid, "The download redirected too far"));
                uri = next;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            response?.Dispose();
            return GitLabResult<byte[]>.Fail(Classify(ex));
        }
        using (response)
        {
            if (!response!.IsSuccessStatusCode)
                return GitLabResult<byte[]>.Fail(await FailureFromAsync(response, ct).ConfigureAwait(false));
            if (response.Content.Headers.ContentLength > maxBytes)
                return GitLabResult<byte[]>.Fail(new GitLabFailure(GitLabFailureKind.Invalid, "The file is too large to show"));
            try
            {
                var bytes = await ReadCappedAsync(response.Content, maxBytes, ct).ConfigureAwait(false);
                return bytes is null
                    ? GitLabResult<byte[]>.Fail(new GitLabFailure(GitLabFailureKind.Invalid, "The file is too large to show"))
                    : GitLabResult<byte[]>.Success(bytes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return GitLabResult<byte[]>.Fail(Classify(ex));
            }
        }
    }

    /// <summary>The body, or null once it passes <paramref name="maxBytes"/> — a missing or
    /// wrong Content-Length does not get to decide how much is read.</summary>
    internal static async Task<byte[]?> ReadCappedAsync(HttpContent content, long maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    // ── Pipelines ────────────────────────────────────────────────────────────────────

    public Task<GitLabResult<List<GlPipeline>>> ListPipelinesAsync(long projectId, string? @ref = null, string? sha = null,
        int maxPages = 1, CancellationToken ct = default)
    {
        var q = new Query().Add("ref", @ref).Add("sha", sha).Add("order_by", "updated_at").Add("sort", "desc");
        return GetPagedAsync($"projects/{projectId}/pipelines", q.ToString(), GitLabJson.Default.ListGlPipeline, maxPages, ct);
    }

    public Task<GitLabResult<GlPipeline>> GetLatestPipelineAsync(long projectId, string? @ref = null, CancellationToken ct = default) =>
        GetAsync($"projects/{projectId}/pipelines/latest" + new Query().Add("ref", @ref), GitLabJson.Default.GlPipeline, ct);

    public Task<GitLabResult<List<GlCommitStatus>>> GetCommitStatusesAsync(long projectId, string sha, CancellationToken ct = default) =>
        GetPagedAsync($"projects/{projectId}/repository/commits/{Uri.EscapeDataString(sha)}/statuses", "", GitLabJson.Default.ListGlCommitStatus, 1, ct);

    // ── Issues ───────────────────────────────────────────────────────────────────────

    public Task<GitLabResult<List<GlIssue>>> ListIssuesAsync(long projectId, IssueQuery? query = null,
        int maxPages = DefaultMaxPages, CancellationToken ct = default)
    {
        query ??= new IssueQuery();
        var q = new Query()
            .Add("state", query.State)
            .Add("search", query.Search)
            .Add("labels", query.Labels)
            .Add("assignee_username", query.AssigneeUsername)
            .Add("updated_after", query.UpdatedAfter)
            .Add("order_by", query.OrderBy)
            .Add("sort", query.Sort);
        return GetPagedAsync($"projects/{projectId}/issues", q.ToString(), GitLabJson.Default.ListGlIssue, maxPages, ct);
    }

    // ── Todos ────────────────────────────────────────────────────────────────────────

    public Task<GitLabResult<List<GlTodo>>> ListPendingTodosAsync(int maxPages = 2, CancellationToken ct = default) =>
        GetPagedAsync("todos", new Query().Add("state", "pending").ToString(), GitLabJson.Default.ListGlTodo, maxPages, ct);

    // ── Transport ────────────────────────────────────────────────────────────────────

    private async Task<GitLabResult<T>> GetAsync<T>(string pathAndQuery, JsonTypeInfo<T> type, CancellationToken ct)
    {
        var page = await SendAsync(HttpMethod.Get, pathAndQuery, null, type, ct).ConfigureAwait(false);
        return page.Ok ? GitLabResult<T>.Success(page.Value.Body) : page.As<T>();
    }

    /// <summary>Walks pages until <c>x-next-page</c> is empty or the cap is hit. A failure on
    /// any page fails the whole list — a half list with no marker would read as complete.</summary>
    private async Task<GitLabResult<List<T>>> GetPagedAsync<T>(string path, string query, JsonTypeInfo<List<T>> type,
        int maxPages, CancellationToken ct)
    {
        var all = new List<T>();
        int page = 1;
        for (int n = 0; n < Math.Max(1, maxPages); n++)
        {
            var sep = query.Length == 0 ? "?" : (query.StartsWith('?') ? query + "&" : "?" + query + "&");
            var res = await SendAsync(HttpMethod.Get, $"{path}{sep}per_page={PerPage}&page={page}", null, type, ct).ConfigureAwait(false);
            if (!res.Ok) return res.As<List<T>>();
            all.AddRange(res.Value.Body);
            if (res.Value.NextPage is not { } next) break;
            page = next;
        }
        return GitLabResult<List<T>>.Success(all);
    }

    internal readonly record struct Response<T>(T Body, int? NextPage);

    internal async Task<GitLabResult<Response<T>>> SendAsync<T>(HttpMethod method, string pathAndQuery, HttpContent? content,
        JsonTypeInfo<T> type, CancellationToken ct)
    {
        var url = pathAndQuery.StartsWith('?') ? _apiBase + pathAndQuery : _apiBase + "/" + pathAndQuery;
        using var request = new HttpRequestMessage(method, url) { Content = content };
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return GitLabResult<Response<T>>.Fail(Classify(ex));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return GitLabResult<Response<T>>.Fail(await FailureFromAsync(response, ct).ConfigureAwait(false));

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var body = await JsonSerializer.DeserializeAsync(stream, type, ct).ConfigureAwait(false);
                if (body is null)
                    return GitLabResult<Response<T>>.Fail(GitLabFailureKind.Invalid, "Empty response", (int)response.StatusCode);
                return GitLabResult<Response<T>>.Success(new Response<T>(body, NextPageOf(response.Headers)));
            }
            catch (JsonException ex)
            {
                return GitLabResult<Response<T>>.Fail(GitLabFailureKind.Invalid,
                    $"{Host} did not answer with the JSON GitLab's API uses ({ex.Message})", (int)response.StatusCode);
            }
        }
    }

    private static int? NextPageOf(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("x-next-page", out var values)) return null;
        foreach (var v in values)
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0) return n;
        return null;
    }

    private GitLabFailure Classify(Exception ex)
    {
        if (ex is TaskCanceledException or OperationCanceledException)
            return new GitLabFailure(GitLabFailureKind.Timeout, $"{Host} did not answer within {_http.Timeout.TotalSeconds:0} s");
        if (ex is HttpRequestException hre)
        {
            var reason = hre.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => $"Could not resolve {Host}",
                HttpRequestError.ConnectionError => $"Could not connect to {Host}",
                HttpRequestError.SecureConnectionError => $"{Host}'s certificate was not trusted",
                _ => $"Could not reach {Host}: {Innermost(hre).Message}",
            };
            return new GitLabFailure(GitLabFailureKind.Network, reason);
        }
        return new GitLabFailure(GitLabFailureKind.Network, $"Could not reach {Host}: {Innermost(ex).Message}");
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException is { } inner) ex = inner;
        return ex;
    }

    private async Task<GitLabFailure> FailureFromAsync(HttpResponseMessage response, CancellationToken ct)
    {
        int status = (int)response.StatusCode;
        var kind = status switch
        {
            401 => GitLabFailureKind.Unauthorized,
            403 => GitLabFailureKind.Forbidden,
            404 => GitLabFailureKind.NotFound,
            405 => GitLabFailureKind.NotAllowed,
            409 => GitLabFailureKind.Conflict,
            429 => GitLabFailureKind.RateLimited,
            >= 500 => GitLabFailureKind.Server,
            _ => GitLabFailureKind.Unprocessable,
        };

        TimeSpan? retryAfter = null;
        if (response.Headers.RetryAfter is { } ra)
        {
            if (ra.Delta is { } delta) retryAfter = delta;
            else if (ra.Date is { } date) retryAfter = date - DateTimeOffset.UtcNow;
        }
        else if (response.Headers.TryGetValues("RateLimit-Reset", out var resets))
        {
            foreach (var r in resets)
                if (long.TryParse(r, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
                    retryAfter = DateTimeOffset.FromUnixTimeSeconds(epoch) - DateTimeOffset.UtcNow;
        }
        if (retryAfter is { } t && t < TimeSpan.Zero) retryAfter = TimeSpan.Zero;

        string message = response.ReasonPhrase is { Length: > 0 } rp ? $"{status} {rp}" : status.ToString(CultureInfo.InvariantCulture);
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (text.Length > 0 && text.Length < 64 * 1024)
            {
                var err = JsonSerializer.Deserialize(text, GitLabJson.Default.GlErrorBody);
                var said = err?.ErrorDescription ?? err?.Error ?? ErrorMessageText(err?.Message);
                if (!string.IsNullOrWhiteSpace(said)) message = said!;
            }
        }
        catch (JsonException)
        {
            // Not GitLab's JSON — a proxy or a login page. The status line is all we know.
        }
        return new GitLabFailure(kind, message, status, retryAfter);
    }

    private static string? ErrorMessageText(JsonElement? el) => el switch
    {
        null => null,
        { ValueKind: JsonValueKind.String } s => s.GetString(),
        { ValueKind: JsonValueKind.Undefined } => null,
        { } other => other.GetRawText(),
    };

    public void Dispose()
    {
        _http.Dispose();
        _raw.Dispose();
    }

    /// <summary>Builds <c>?a=1&amp;b=2</c>, skipping nulls, escaping values.</summary>
    internal sealed class Query
    {
        private readonly StringBuilder _sb = new();

        public Query Add(string key, string? value)
        {
            if (string.IsNullOrEmpty(value)) return this;
            _sb.Append(_sb.Length == 0 ? '?' : '&').Append(key).Append('=').Append(Uri.EscapeDataString(value));
            return this;
        }

        public Query Add(string key, int value) => Add(key, value.ToString(CultureInfo.InvariantCulture));

        public Query Add(string key, DateTimeOffset? value) =>
            value is { } v ? Add(key, v.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)) : this;

        public override string ToString() => _sb.ToString();
    }
}

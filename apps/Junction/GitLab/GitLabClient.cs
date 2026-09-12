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
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Junction/1.0 (+https://github.com/fredrikhu/hului-revamped)");
        if (!string.IsNullOrEmpty(token))
        {
            if (kind == GitLabTokenKind.Pat) _http.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token);
            else _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
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

    public Task<GitLabResult<GlMergeRequest>> GetMergeRequestAsync(long projectId, long iid, CancellationToken ct = default) =>
        GetAsync($"projects/{projectId}/merge_requests/{iid}", GitLabJson.Default.GlMergeRequest, ct);

    public Task<GitLabResult<GlApprovals>> GetMergeRequestApprovalsAsync(long projectId, long iid, CancellationToken ct = default) =>
        GetAsync($"projects/{projectId}/merge_requests/{iid}/approvals", GitLabJson.Default.GlApprovals, ct);

    public Task<GitLabResult<List<GlDiscussion>>> ListMergeRequestDiscussionsAsync(long projectId, long iid,
        int maxPages = DefaultMaxPages, CancellationToken ct = default) =>
        GetPagedAsync($"projects/{projectId}/merge_requests/{iid}/discussions", "", GitLabJson.Default.ListGlDiscussion, maxPages, ct);

    public Task<GitLabResult<List<GlDraftNote>>> ListDraftNotesAsync(long projectId, long iid, CancellationToken ct = default) =>
        GetPagedAsync($"projects/{projectId}/merge_requests/{iid}/draft_notes", "", GitLabJson.Default.ListGlDraftNote, DefaultMaxPages, ct);

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

    public void Dispose() => _http.Dispose();

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

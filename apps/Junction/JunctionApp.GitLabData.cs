using System.Collections.Concurrent;
using HultUI;
using Junction.GitLab;

namespace Junction;

/// <summary>
/// What the app knows from GitLab about each repository, cached per repository like every
/// other view's data: the project, its protected branches, the open merge requests, the
/// latest pipelines by SHA, and per merge request the approvals and discussions. Every value
/// carries when it was loaded — the "as of" every hosted surface shows — and a failure keeps
/// the previous value on screen with a stale mark rather than replacing it with a spinner.
/// </summary>
/// <remarks>
/// The loaders key on a per-repository <i>hosted generation</i>, bumped by Fetch, by opening
/// the merge-request panel, and by a repository switch when the data is older than two
/// minutes — so a quiet instance is not polled by every rebuild, and a refresh reloads the
/// whole set at once. Everything produced off the UI thread goes through <see cref="Sync{T}"/>:
/// the loaders run their factories on a pool thread, where blocking on the client's
/// awaitable is the simplest correct thing.
/// </remarks>
public partial class JunctionApp
{
    /// <summary>A hosted value and its provenance.</summary>
    private sealed record Hosted<T>(T Value, DateTimeOffset AsOf, GitLabFailure? Failure, bool Loaded)
    {
        public static Hosted<T> Empty(T value) => new(value, default, null, false);
        public bool Stale => Loaded && DateTimeOffset.UtcNow - AsOf > StaleAfter;
        public TimeSpan Age => DateTimeOffset.UtcNow - AsOf;
    }

    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RefreshOnSwitchAfter = TimeSpan.FromMinutes(2);

    /// <summary>The project and what it enforces, fetched together: one is never wanted
    /// without the other.</summary>
    private sealed record GitLabProjectInfo(GlProject Project, IReadOnlyList<GlProtectedBranch> Protected);

    private readonly Dictionary<string, int> _hostedGen = new(PathComparer);
    private readonly Dictionary<string, DateTimeOffset> _hostedLastRefresh = new(PathComparer);

    private readonly KeyedAsyncLoader<string, Hosted<GitLabProjectInfo?>> _projectLoaders =
        new(Hosted<GitLabProjectInfo?>.Empty(null), capacity: 16, comparer: PathComparer);
    private readonly KeyedAsyncLoader<string, Hosted<IReadOnlyList<GlMergeRequest>>> _mrLoaders =
        new(Hosted<IReadOnlyList<GlMergeRequest>>.Empty([]), capacity: 16, comparer: PathComparer);
    private readonly KeyedAsyncLoader<string, Hosted<IReadOnlyDictionary<string, GlPipeline>>> _pipelineLoaders =
        new(Hosted<IReadOnlyDictionary<string, GlPipeline>>.Empty(new Dictionary<string, GlPipeline>()), capacity: 16, comparer: PathComparer);
    private readonly KeyedAsyncLoader<(string Path, long Iid), Hosted<GlApprovals?>> _approvalLoaders =
        new(Hosted<GlApprovals?>.Empty(null), capacity: 64);
    private readonly KeyedAsyncLoader<(string Path, long Iid), Hosted<IReadOnlyList<GlDiscussion>>> _discussionLoaders =
        new(Hosted<IReadOnlyList<GlDiscussion>>.Empty([]), capacity: 16);

    // Project ids by "host|path", so every later call addresses the project by number.
    private readonly ConcurrentDictionary<string, long> _projectIds = new(StringComparer.OrdinalIgnoreCase);

    private int HostedGen(string path) => _hostedGen.GetValueOrDefault(path);

    /// <summary>Reloads everything hosted for the repository on the next build.</summary>
    internal void RefreshHosted(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        _hostedGen[path] = HostedGen(path) + 1;
        _hostedLastRefresh[path] = DateTimeOffset.UtcNow;
        RefreshTodos();   // the bell rides along: an action on a request usually changes them
        Rerender();
    }

    /// <summary>A refresh only when the last one is old enough — what a repository switch
    /// and a panel open ask for, so flipping between repositories is not a poll.</summary>
    private void RefreshHostedIfStale(string path, TimeSpan after)
    {
        if (!GitLabActive(path)) return;
        // A load already on its way, or one that just landed, is the refresh — bumping the
        // generation under it would supersede a result about to arrive with a second call.
        var mrs = _mrLoaders.For(path);
        if (mrs.IsLoading) return;
        if (mrs.Value.Loaded && mrs.Value.Age <= after) return;
        if (!_hostedLastRefresh.TryGetValue(path, out var last) || DateTimeOffset.UtcNow - last > after)
            RefreshHosted(path);
    }

    private static T Sync<T>(Task<T> task) => task.GetAwaiter().GetResult();

    // ── Loaders ──────────────────────────────────────────────────────────────────────

    private Hosted<GitLabProjectInfo?> ProjectFor(string path)
    {
        if (GitLabRemoteFor(path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client)
            return Hosted<GitLabProjectInfo?>.Empty(null);
        var loader = _projectLoaders.For(path);
        var prev = loader.Value;
        loader.Load($"{remote.Host}|{remote.ProjectPath}#{HostedGen(path)}", () =>
        {
            var project = Sync(client.GetProjectAsync(remote.ProjectPath));
            if (!project.Ok) return Keep(prev, project.Failure!);
            _projectIds[remote.Host + "|" + remote.ProjectPath] = project.Value.Id;
            var protectedBranches = Sync(client.GetProtectedBranchesAsync(project.Value.Id));
            return new Hosted<GitLabProjectInfo?>(
                new GitLabProjectInfo(project.Value, protectedBranches.Ok ? protectedBranches.Value : prev.Value?.Protected ?? []),
                DateTimeOffset.UtcNow, protectedBranches.Ok ? null : protectedBranches.Failure, true);
        }, NoteAuthFailure<GitLabProjectInfo?>(remote.Host));
        return loader.Value;
    }

    /// <summary>The project's numeric id, or null until the project has loaded once.</summary>
    private long? ProjectIdFor(string path) =>
        GitLabRemoteFor(path) is { } r && _projectIds.TryGetValue(r.Host + "|" + r.ProjectPath, out var id) ? id
        : ProjectFor(path).Value?.Project.Id;

    private Hosted<IReadOnlyList<GlMergeRequest>> MergeRequestsFor(string path)
    {
        if (GitLabRemoteFor(path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client)
            return Hosted<IReadOnlyList<GlMergeRequest>>.Empty([]);
        var loader = _mrLoaders.For(path);
        var prev = loader.Value;
        loader.Load($"{path}#{HostedGen(path)}", () =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return Keep(prev, new GitLabFailure(GitLabFailureKind.NotFound, "Project not found"));
            var mrs = Sync(client.ListMergeRequestsAsync(id.Value, new MergeRequestQuery(State: "opened")));
            return mrs.Ok
                ? new Hosted<IReadOnlyList<GlMergeRequest>>(mrs.Value, DateTimeOffset.UtcNow, null, true)
                : Keep(prev, mrs.Failure!);
        }, NoteAuthFailure<IReadOnlyList<GlMergeRequest>>(remote.Host));
        return loader.Value;
    }

    /// <summary>The latest pipeline per SHA among the project's hundred most recent — one
    /// call that covers every branch tip and every recent History row.</summary>
    private Hosted<IReadOnlyDictionary<string, GlPipeline>> PipelinesFor(string path)
    {
        if (GitLabRemoteFor(path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client)
            return Hosted<IReadOnlyDictionary<string, GlPipeline>>.Empty(new Dictionary<string, GlPipeline>());
        var loader = _pipelineLoaders.For(path);
        var prev = loader.Value;
        loader.Load($"{path}#{HostedGen(path)}", () =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return Keep(prev, new GitLabFailure(GitLabFailureKind.NotFound, "Project not found"));
            var pipelines = Sync(client.ListPipelinesAsync(id.Value, maxPages: 1));
            if (!pipelines.Ok) return Keep(prev, pipelines.Failure!);
            var bySha = new Dictionary<string, GlPipeline>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in pipelines.Value)
                if (p.Sha is { Length: > 0 } sha && !bySha.ContainsKey(sha)) bySha[sha] = p;   // newest first
            return new Hosted<IReadOnlyDictionary<string, GlPipeline>>(bySha, DateTimeOffset.UtcNow, null, true);
        }, NoteAuthFailure<IReadOnlyDictionary<string, GlPipeline>>(remote.Host));
        return loader.Value;
    }

    private Hosted<GlApprovals?> ApprovalsFor(string path, long iid)
    {
        if (GitLabRemoteFor(path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client)
            return Hosted<GlApprovals?>.Empty(null);
        var loader = _approvalLoaders.For((path, iid));
        var prev = loader.Value;
        loader.Load($"{path}#{iid}#{HostedGen(path)}", () =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return prev;
            var a = Sync(client.GetMergeRequestApprovalsAsync(id.Value, iid));
            return a.Ok ? new Hosted<GlApprovals?>(a.Value, DateTimeOffset.UtcNow, null, true) : Keep(prev, a.Failure!);
        });
        return loader.Value;
    }

    private Hosted<IReadOnlyList<GlDiscussion>> DiscussionsFor(string path, long iid)
    {
        if (GitLabRemoteFor(path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client)
            return Hosted<IReadOnlyList<GlDiscussion>>.Empty([]);
        var loader = _discussionLoaders.For((path, iid));
        var prev = loader.Value;
        // The note generation reloads this request's threads alone — after a post, a reply,
        // a resolve — without asking for the rest of the hosted data again.
        loader.Load($"{path}#{iid}#{HostedGen(path)}#{NoteGen(path, iid)}", () =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return prev;
            var d = Sync(client.ListMergeRequestDiscussionsAsync(id.Value, iid));
            return d.Ok ? new Hosted<IReadOnlyList<GlDiscussion>>(d.Value, DateTimeOffset.UtcNow, null, true) : Keep(prev, d.Failure!);
        });
        return loader.Value;
    }

    /// <summary>Whether a merge-request load is in flight with nothing shown yet — the one
    /// moment the skeleton rows are for.</summary>
    private bool MergeRequestsFirstLoading(string path) =>
        _mrLoaders.For(path) is { IsLoading: true, Value.Loaded: false };

    // ── Derived lookups ──────────────────────────────────────────────────────────────

    /// <summary>The latest pipeline for a commit, or null: nothing is drawn for none.</summary>
    private GlPipeline? PipelineForSha(string path, string? sha) =>
        sha is { Length: > 0 } && PipelinesFor(path).Value.TryGetValue(sha, out var p) ? p : null;

    /// <summary>The open merge request whose source is <paramref name="branch"/>, if any.</summary>
    private GlMergeRequest? MergeRequestForBranch(string path, string? branch)
    {
        if (string.IsNullOrEmpty(branch)) return null;
        foreach (var mr in MergeRequestsFor(path).Value)
            if (mr.IsOpen && mr.SourceBranch == branch) return mr;
        return null;
    }

    /// <summary>The open merge request whose head is <paramref name="sha"/>, if any.</summary>
    private GlMergeRequest? MergeRequestForSha(string path, string? sha)
    {
        if (string.IsNullOrEmpty(sha)) return null;
        foreach (var mr in MergeRequestsFor(path).Value)
            if (mr.IsOpen && string.Equals(mr.Sha, sha, StringComparison.OrdinalIgnoreCase)) return mr;
        return null;
    }

    private GlProtectedBranch? ProtectionFor(string path, string branch)
    {
        var info = ProjectFor(path).Value;
        if (info is null) return null;
        foreach (var rule in info.Protected)
            if (rule.Matches(branch)) return rule;
        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────

    private long? ResolveProjectId(GitLabClient client, GitLabRemote remote)
    {
        var key = remote.Host + "|" + remote.ProjectPath;
        if (_projectIds.TryGetValue(key, out var id)) return id;
        var project = Sync(client.GetProjectAsync(remote.ProjectPath));
        if (!project.Ok) return null;
        _projectIds[key] = project.Value.Id;
        return project.Value.Id;
    }

    /// <summary>A failed load keeps what was on screen and says why.</summary>
    private static Hosted<T> Keep<T>(Hosted<T> prev, GitLabFailure failure) => prev with { Failure = failure };

    /// <summary>Runs on the UI thread when a load lands: a 401 flips the account row.</summary>
    private Action<Hosted<T>> NoteAuthFailure<T>(string host) => result =>
    {
        if (result.Failure?.Kind == GitLabFailureKind.Unauthorized) MarkGitLabAuthFailed(host);
    };

    /// <summary>"as of 2 min ago" — the provenance line under every hosted surface.</summary>
    private static string AsOfText(DateTimeOffset asOf)
    {
        var age = DateTimeOffset.UtcNow - asOf;
        if (age < TimeSpan.FromMinutes(1)) return "as of just now";
        if (age < TimeSpan.FromHours(1)) return $"as of {(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromDays(1)) return $"as of {(int)age.TotalHours} h ago";
        return $"as of {asOf.ToLocalTime():g}";
    }

    private static string RelativeTime(DateTimeOffset? when)
    {
        if (when is not { } t) return "";
        var age = DateTimeOffset.UtcNow - t;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} h ago";
        if (age < TimeSpan.FromDays(30)) return $"{(int)age.TotalDays} d ago";
        return t.ToLocalTime().ToString("d");
    }
}

using Aperture.Core.Requests;

namespace Aperture.Core.Testing;

/// <summary>
/// A request server with nothing behind it: the fixture the headless tests and the
/// screenshot driver discover and request against. In memory and synchronous, like
/// <see cref="FakeMediaLibrary"/>, with the same two test hooks — <see cref="Calls"/>
/// records every request in order, and <see cref="FailWith"/> scripts the next failure.
///
/// <para>It behaves the way the real server does in the two places a view depends on it:
/// a request moves its title to <see cref="Availability.Pending"/> (or straight to
/// <see cref="Availability.Processing"/> for a user who auto-approves), and approving one
/// moves it on.</para>
/// </summary>
public sealed class FakeRequestServer : IRequestServer
{
    private readonly Dictionary<string, DiscoverDetail> _titles = new(StringComparer.Ordinal);
    private readonly Dictionary<DiscoverShelf, List<string>> _shelves = [];
    private readonly List<MediaRequest> _requests = [];
    private int _nextRequestId = 1;

    public FakeRequestServer(RequestServerInfo? server = null, RequestUser? user = null)
    {
        Server = server ?? new RequestServerInfo("http://jellyseerr.test:5055", "Jellyseerr", "2.7.3",
            Movie4kEnabled: false, Series4kEnabled: false, PartialRequestsEnabled: true);
        User = user ?? new RequestUser(1, "tester", RequestPermission.Request);
    }

    public RequestServerInfo Server { get; }
    public RequestUser User { get; set; }

    public List<string> Calls { get; } = [];
    public ApertureFailure? FailWith { get; set; }
    public bool Disposed { get; private set; }

    /// <summary>Other people's requests are made in this name.</summary>
    public const string OtherUser = "anna";

    // ── Fixture building ─────────────────────────────────────────────────

    /// <summary>A title, on the shelves given. Seasons default to none for a film and two
    /// for a series.</summary>
    public FakeRequestServer AddTitle(DiscoverItem item, IReadOnlyList<DiscoverSeason>? seasons = null,
        params DiscoverShelf[] shelves)
    {
        seasons ??= item.Kind == DiscoverKind.Tv
            ? [new DiscoverSeason(1, "Season 1", 10, 2020, Availability.Unknown, Availability.Unknown),
               new DiscoverSeason(2, "Season 2", 10, 2021, Availability.Unknown, Availability.Unknown)]
            : [];
        _titles[item.Key] = new DiscoverDetail
        {
            Item = item,
            Tagline = item.Kind == DiscoverKind.Movie ? "A fixture with a tagline." : null,
            Runtime = item.Kind == DiscoverKind.Movie ? TimeSpan.FromMinutes(118) : null,
            Genres = ["Drama"],
            Status = "Released",
            TvdbId = item.Kind == DiscoverKind.Tv ? 70000 + item.TmdbId : null,
            Seasons = seasons,
            Cast = [new DiscoverPerson(9000 + item.TmdbId, "Fixture Actor", "The Lead", null)],
        };
        foreach (var shelf in shelves)
        {
            if (!_shelves.TryGetValue(shelf, out var list)) _shelves[shelf] = list = [];
            list.Add(item.Key);
        }
        return this;
    }

    public FakeRequestServer AddMovie(int tmdbId, string title, int year, Availability availability = Availability.Unknown,
        string? jellyfinItemId = null, params DiscoverShelf[] shelves) =>
        AddTitle(new DiscoverItem
        {
            TmdbId = tmdbId, Kind = DiscoverKind.Movie, Title = title, Year = year,
            Overview = $"{title} is a fixture film.", PosterPath = $"/poster-{tmdbId}.jpg",
            BackdropPath = $"/backdrop-{tmdbId}.jpg", VoteAverage = 7.4f,
            Availability = availability, JellyfinItemId = jellyfinItemId,
        }, null, shelves);

    public FakeRequestServer AddSeries(int tmdbId, string title, int year, Availability availability = Availability.Unknown,
        string? jellyfinItemId = null, IReadOnlyList<DiscoverSeason>? seasons = null, params DiscoverShelf[] shelves) =>
        AddTitle(new DiscoverItem
        {
            TmdbId = tmdbId, Kind = DiscoverKind.Tv, Title = title, Year = year,
            Overview = $"{title} is a fixture series.", PosterPath = $"/poster-tv-{tmdbId}.jpg",
            BackdropPath = $"/backdrop-tv-{tmdbId}.jpg", VoteAverage = 8.1f,
            Availability = availability, JellyfinItemId = jellyfinItemId,
        }, seasons, shelves);

    /// <summary>A request someone else made, waiting for approval.</summary>
    public MediaRequest AddPendingRequest(string key, IReadOnlyList<int>? seasons = null)
    {
        var title = _titles[key];
        var request = NewRecord(title, RequestStatus.Pending, seasons ?? [], is4k: false, byId: 2, by: OtherUser);
        _requests.Insert(0, request);
        SetAvailability(key, Availability.Pending, seasons);
        return request;
    }

    public DiscoverDetail? Title(string key) => _titles.GetValueOrDefault(key);
    public IReadOnlyList<MediaRequest> Requests => _requests;

    // ── IRequestServer ───────────────────────────────────────────────────

    public Task<ApertureResult<DiscoverPage>> DiscoverAsync(DiscoverShelf shelf, int page = 1, CancellationToken ct = default)
    {
        Calls.Add($"Discover({shelf},{page})");
        if (Fail<DiscoverPage>() is { } failed) return failed;
        var items = _shelves.GetValueOrDefault(shelf, []).Select(k => _titles[k].Item).ToList();
        return Done(new DiscoverPage(items, 1, items.Count > 0 ? 1 : 0, items.Count));
    }

    public Task<ApertureResult<DiscoverPage>> SearchAsync(string query, int page = 1, CancellationToken ct = default)
    {
        Calls.Add($"Search({query})");
        if (Fail<DiscoverPage>() is { } failed) return failed;
        var items = _titles.Values.Select(t => t.Item)
            .Where(i => i.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Title, StringComparer.Ordinal).ToList();
        return Done(new DiscoverPage(items, 1, items.Count > 0 ? 1 : 0, items.Count));
    }

    public Task<ApertureResult<DiscoverDetail>> GetDetailAsync(DiscoverKind kind, int tmdbId, CancellationToken ct = default)
    {
        var key = new DiscoverItem { TmdbId = tmdbId, Kind = kind, Title = "" }.Key;
        Calls.Add($"GetDetail({key})");
        if (Fail<DiscoverDetail>() is { } failed) return failed;
        if (!_titles.TryGetValue(key, out var detail))
            return Task.FromResult(ApertureResult<DiscoverDetail>.Fail(ApertureFailureKind.NotFound, "no such title", 404));
        return Done(detail with { Requests = _requests.Where(r => r.MediaKey == key).ToList() });
    }

    public Task<ApertureResult<DiscoverPage>> GetRecommendationsAsync(DiscoverKind kind, int tmdbId, int page = 1,
        CancellationToken ct = default)
    {
        Calls.Add($"GetRecommendations({kind},{tmdbId})");
        if (Fail<DiscoverPage>() is { } failed) return failed;
        var items = _titles.Values.Select(t => t.Item)
            .Where(i => i.Kind == kind && i.TmdbId != tmdbId).Take(8).ToList();
        return Done(new DiscoverPage(items, 1, 1, items.Count));
    }

    public Task<ApertureResult<RequestPage>> GetRequestsAsync(RequestFilter filter, bool mine, int skip = 0, int take = 20,
        CancellationToken ct = default)
    {
        Calls.Add($"GetRequests({filter},{(mine ? "mine" : "all")},{skip})");
        if (Fail<RequestPage>() is { } failed) return failed;
        IEnumerable<MediaRequest> list = _requests;
        if (mine || !User.CanManageRequests) list = list.Where(r => r.RequestedById == User.Id);
        list = filter switch
        {
            RequestFilter.Pending => list.Where(r => r.Status == RequestStatus.Pending),
            RequestFilter.Approved => list.Where(r => r.Status == RequestStatus.Approved),
            RequestFilter.Completed => list.Where(r => r.Status == RequestStatus.Completed),
            RequestFilter.Failed => list.Where(r => r.Status == RequestStatus.Failed),
            RequestFilter.Available => list.Where(r => r.Availability == Availability.Available),
            RequestFilter.Processing => list.Where(r => r.Status == RequestStatus.Approved
                                                        && r.Availability != Availability.Available),
            RequestFilter.Unavailable => list.Where(r => r.Availability != Availability.Available),
            _ => list,
        };
        var all = list.ToList();
        return Done(new RequestPage(all.Skip(skip).Take(take).ToList(), all.Count, skip));
    }

    public Task<ApertureResult<int>> GetPendingCountAsync(CancellationToken ct = default)
    {
        Calls.Add("GetPendingCount");
        if (Fail<int>() is { } failed) return failed;
        return Done(_requests.Count(r => r.Status == RequestStatus.Pending));
    }

    public Task<ApertureResult<RequestQuotas>> GetQuotaAsync(CancellationToken ct = default)
    {
        Calls.Add("GetQuota");
        if (Fail<RequestQuotas>() is { } failed) return failed;
        int movies = _requests.Count(r => r.RequestedById == User.Id && r.Kind == DiscoverKind.Movie);
        int tv = _requests.Count(r => r.RequestedById == User.Id && r.Kind == DiscoverKind.Tv);
        return Done(new RequestQuotas(
            new RequestQuota(null, 7, movies, null, false),
            new RequestQuota(null, 7, tv, null, false)));
    }

    public Task<ApertureResult<MediaRequest>> RequestAsync(NewRequest request, CancellationToken ct = default)
    {
        var key = new DiscoverItem { TmdbId = request.TmdbId, Kind = request.Kind, Title = "" }.Key;
        Calls.Add($"Request({key}{(request.Seasons.Count > 0 ? " s" + string.Join(",", request.Seasons) : "")}"
                  + $"{(request.Is4k ? " 4k" : "")})");
        if (Fail<MediaRequest>() is { } failed) return failed;
        if (!_titles.TryGetValue(key, out var title))
            return Task.FromResult(ApertureResult<MediaRequest>.Fail(ApertureFailureKind.NotFound, "no such title", 404));
        if (!User.CanRequest(request.Kind))
            return Task.FromResult(ApertureResult<MediaRequest>.Fail(ApertureFailureKind.Forbidden,
                "You do not have permission to request this", 403));

        bool auto = User.AutoApproves(request.Kind);
        var record = NewRecord(title, auto ? RequestStatus.Approved : RequestStatus.Pending, request.Seasons,
            request.Is4k, User.Id, User.DisplayName);
        _requests.Insert(0, record);
        SetAvailability(key, auto ? Availability.Processing : Availability.Pending, request.Seasons);
        return Done(record with { Availability = _titles[key].Item.Availability });
    }

    public Task<ApertureResult<MediaRequest>> DecideAsync(int requestId, bool approve, CancellationToken ct = default)
    {
        Calls.Add($"{(approve ? "Approve" : "Decline")}({requestId})");
        if (Fail<MediaRequest>() is { } failed) return failed;
        int at = _requests.FindIndex(r => r.Id == requestId);
        if (at < 0) return Task.FromResult(ApertureResult<MediaRequest>.Fail(ApertureFailureKind.NotFound, "no such request", 404));
        if (!User.CanManageRequests)
            return Task.FromResult(ApertureResult<MediaRequest>.Fail(ApertureFailureKind.Forbidden, "not allowed", 403));

        var updated = _requests[at] with { Status = approve ? RequestStatus.Approved : RequestStatus.Declined };
        SetAvailability(updated.MediaKey, approve ? Availability.Processing : Availability.Unknown, updated.Seasons);
        updated = updated with { Availability = _titles[updated.MediaKey].Item.Availability };
        _requests[at] = updated;
        return Done(updated);
    }

    public Task<ApertureResult<bool>> DeleteRequestAsync(int requestId, CancellationToken ct = default)
    {
        Calls.Add($"DeleteRequest({requestId})");
        if (Fail<bool>() is { } failed) return failed;
        int at = _requests.FindIndex(r => r.Id == requestId);
        if (at < 0) return Task.FromResult(ApertureResult<bool>.Fail(ApertureFailureKind.NotFound, "no such request", 404));
        var gone = _requests[at];
        _requests.RemoveAt(at);
        if (!_requests.Any(r => r.MediaKey == gone.MediaKey))
            SetAvailability(gone.MediaKey, Availability.Unknown, gone.Seasons);
        return Done(true);
    }

    public void Dispose() => Disposed = true;

    // ── Helpers ──────────────────────────────────────────────────────────

    private MediaRequest NewRecord(DiscoverDetail title, RequestStatus status, IReadOnlyList<int> seasons, bool is4k,
        int byId, string by) => new()
    {
        Id = _nextRequestId++,
        Status = status,
        Kind = title.Item.Kind,
        TmdbId = title.Item.TmdbId,
        Title = title.Item.Title,
        Year = title.Item.Year,
        PosterPath = title.Item.PosterPath,
        Availability = title.Item.Availability,
        Is4k = is4k,
        Seasons = seasons,
        RequestedById = byId,
        RequestedBy = by,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Moves a title (and the named seasons of a series) to a new state, the way
    /// the server's own bookkeeping does when a request is made or decided.</summary>
    private void SetAvailability(string key, Availability availability, IReadOnlyList<int>? seasons)
    {
        var title = _titles[key];
        var newSeasons = title.Seasons.Select(s =>
            seasons is { Count: > 0 } && !seasons.Contains(s.Number) ? s : s with { Availability = availability }).ToList();
        _titles[key] = title with { Item = title.Item with { Availability = availability }, Seasons = newSeasons };
    }

    private Task<ApertureResult<T>>? Fail<T>()
    {
        if (FailWith is not { } failure) return null;
        FailWith = null;
        return Task.FromResult(ApertureResult<T>.Fail(failure));
    }

    private static Task<ApertureResult<T>> Done<T>(T value) => Task.FromResult(ApertureResult<T>.Success(value));
}

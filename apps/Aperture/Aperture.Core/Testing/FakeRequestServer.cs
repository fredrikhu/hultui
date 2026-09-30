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
    public MediaRequest AddPendingRequest(string key, IReadOnlyList<int>? seasons = null) =>
        AddRequest(key, RequestStatus.Pending, seasons);

    /// <summary>A request someone else made, in whatever state — approved moves its title
    /// on its way, as the server's bookkeeping does.</summary>
    public MediaRequest AddRequest(string key, RequestStatus status, IReadOnlyList<int>? seasons = null)
    {
        var title = _titles[key];
        var request = NewRecord(title, status, seasons ?? [], is4k: false, byId: 2, by: OtherUser);
        _requests.Insert(0, request);
        SetAvailability(key, status switch
        {
            RequestStatus.Pending => Availability.Pending,
            RequestStatus.Approved => Availability.Processing,
            RequestStatus.Completed => Availability.Available,
            _ => title.Item.Availability,
        }, seasons);
        return request with { Availability = _titles[key].Item.Availability };
    }

    public DiscoverDetail? Title(string key) => _titles.GetValueOrDefault(key);

    /// <summary>What Radarr/Sonarr are downloading for a title, as the server would relay it.</summary>
    public void SetDownloads(string key, IReadOnlyList<DownloadProgress> downloads) =>
        _titles[key] = _titles[key] with { Downloads = downloads };

    /// <summary>The allowance <see cref="GetQuotaAsync"/> reports; null reports an unlimited one.</summary>
    public RequestQuotas? Quotas { get; set; }
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
        return Done(detail with
        {
            Requests = _requests.Where(r => r.MediaKey == key).Select(r => r with { Downloads = detail.Downloads }).ToList(),
            // A made-up id for the server's own record, so an issue has something to be filed on.
            MediaId = 100_000 + tmdbId + (kind == DiscoverKind.Tv ? 50_000 : 0),
            OnWatchlist = _watchlist.Contains(key),
        });
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
        // Where each title stands now — a request made last week reads its title's state today.
        IEnumerable<MediaRequest> list = _requests.Select(r => r with
        {
            Availability = _titles[r.MediaKey].Item.Availability,
            JellyfinItemId = _titles[r.MediaKey].Item.JellyfinItemId,
        });
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
        // Each request carries what is downloading for its title, as the server's does.
        var page = all.Skip(skip).Take(take)
            .Select(r => r with { Downloads = _titles.TryGetValue(r.MediaKey, out var t) ? t.Downloads : [] }).ToList();
        return Done(new RequestPage(page, all.Count, skip));
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
        if (Quotas is { } set) return Done(set);
        int movies = _requests.Count(r => r.RequestedById == User.Id && r.Kind == DiscoverKind.Movie);
        int tv = _requests.Count(r => r.RequestedById == User.Id && r.Kind == DiscoverKind.Tv);
        return Done(new RequestQuotas(
            new RequestQuota(null, 7, movies, null, false),
            new RequestQuota(null, 7, tv, null, false)));
    }

    public Task<ApertureResult<MediaRequest>> RequestAsync(NewRequest request, CancellationToken ct = default)
    {
        var key = new DiscoverItem { TmdbId = request.TmdbId, Kind = request.Kind, Title = "" }.Key;
        SentOptions.Add(request.Options);
        Calls.Add($"Request({key}{(request.Seasons.Count > 0 ? " s" + string.Join(",", request.Seasons) : "")}"
                  + $"{(request.Is4k ? " 4k" : "")})");
        if (Fail<MediaRequest>() is { } failed) return failed;
        if (!_titles.TryGetValue(key, out var title))
            return Task.FromResult(ApertureResult<MediaRequest>.Fail(ApertureFailureKind.NotFound, "no such title", 404));
        if (!User.CanRequest(request.Kind))
            return Task.FromResult(ApertureResult<MediaRequest>.Fail(ApertureFailureKind.Forbidden,
                "You do not have permission to request this", 403));

        if (Quotas is { } quotas && (request.Kind == DiscoverKind.Movie ? quotas.Movie : quotas.Tv).Restricted)
            return Task.FromResult(ApertureResult<MediaRequest>.Fail(ApertureFailureKind.Forbidden,
                "Quota exceeded", 403));

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

    // ── Slice 7b ─────────────────────────────────────────────────────────

    private readonly HashSet<string> _watchlist = new(StringComparer.Ordinal);
    private readonly Dictionary<(DiscoverKind, int), List<string>> _genreTitles = [];
    private readonly List<DiscoverGenre> _genres = [];
    private readonly Dictionary<int, List<string>> _customRows = [];
    private readonly Dictionary<(DiscoverKind, int), List<string>> _companyTitles = [];

    /// <summary>The administrator's discover layout. Null serves the server's own default —
    /// the twelve built-in rows, in the order a fresh install has them.</summary>
    public List<DiscoverSlider>? Layout { get; set; }

    /// <summary>How many titles a page of a row holds — twenty, as the server's; smaller so a
    /// test can have a second page without twenty titles.</summary>
    public int SliderPageSize { get; set; } = 20;

    /// <summary>Problem reports made through <see cref="ReportIssueAsync"/>, in order.</summary>
    public List<NewIssue> Reports { get; } = [];

    /// <summary>Requests' advanced choices, as sent — so a test can see what went.</summary>
    public List<RequestOptions?> SentOptions { get; } = [];

    public static readonly RequestService Radarr = new(0, "Radarr", false, true, 4, "/media/movies");
    public static readonly RequestService Sonarr = new(0, "Sonarr", false, true, 6, "/media/shows");

    public FakeRequestServer AddGenre(DiscoverKind kind, int id, string name, params string[] keys)
    {
        _genres.Add(new DiscoverGenre(id, name, kind, $"/genre-{id}.jpg"));
        _genreTitles[(kind, id)] = [.. keys];
        return this;
    }

    /// <summary>A custom row of the layout, filled with these titles.</summary>
    public FakeRequestServer AddCustomRow(DiscoverSlider slider, params string[] keys)
    {
        (Layout ??= [.. DefaultLayout]).Add(slider);
        _customRows[slider.Id] = [.. keys];
        return this;
    }

    public void AddToWatchlist(string key) => _watchlist.Add(key);

    /// <summary>A studio's films (<see cref="DiscoverKind.Movie"/>) or a network's series.</summary>
    public FakeRequestServer AddCompanyTitles(DiscoverKind kind, int companyId, params string[] keys)
    {
        _companyTitles[(kind, companyId)] = [.. keys];
        return this;
    }

    /// <summary>A requested title lands in the library — what the server's own scan records
    /// when Jellyfin picks the download up.</summary>
    public void Arrive(string key, string jellyfinItemId)
    {
        var title = _titles[key];
        _titles[key] = title with
        {
            Item = title.Item with { Availability = Availability.Available, JellyfinItemId = jellyfinItemId },
            Downloads = [],
        };
    }

    private static readonly DiscoverSlider[] DefaultLayout =
    [
        .. new[]
        {
            SliderKind.RecentlyAdded, SliderKind.RecentRequests, SliderKind.Watchlist, SliderKind.Trending,
            SliderKind.PopularMovies, SliderKind.MovieGenres, SliderKind.UpcomingMovies, SliderKind.Studios,
            SliderKind.PopularTv, SliderKind.TvGenres, SliderKind.UpcomingTv, SliderKind.Networks,
        }.Select((kind, i) => new DiscoverSlider(i + 1, kind, null, null, true, i)),
    ];

    public Task<ApertureResult<IReadOnlyList<DiscoverSlider>>> GetDiscoverLayoutAsync(CancellationToken ct = default)
    {
        Calls.Add("GetDiscoverLayout");
        if (Fail<IReadOnlyList<DiscoverSlider>>() is { } failed) return failed;
        return Done((IReadOnlyList<DiscoverSlider>)(Layout ?? [.. DefaultLayout]).OrderBy(s => s.Order).ToList());
    }

    public Task<ApertureResult<DiscoverPage>> GetSliderAsync(DiscoverSlider slider, int page = 1, CancellationToken ct = default)
    {
        Calls.Add(page > 1 ? $"GetSlider({slider.Kind},{slider.Id},p{page})" : $"GetSlider({slider.Kind},{slider.Id})");
        if (Fail<DiscoverPage>() is { } failed) return failed;
        DiscoverShelf? shelf = slider.Kind switch
        {
            SliderKind.Trending => DiscoverShelf.Trending,
            SliderKind.PopularMovies => DiscoverShelf.PopularMovies,
            SliderKind.UpcomingMovies => DiscoverShelf.UpcomingMovies,
            SliderKind.PopularTv => DiscoverShelf.PopularSeries,
            SliderKind.UpcomingTv => DiscoverShelf.UpcomingSeries,
            _ => null,
        };
        var keys = shelf is { } s ? _shelves.GetValueOrDefault(s, []) : _customRows.GetValueOrDefault(slider.Id, []);
        // Pages of SliderPageSize, as the server's are of twenty.
        var items = keys.Skip((Math.Max(1, page) - 1) * SliderPageSize).Take(SliderPageSize).Select(k => _titles[k].Item).ToList();
        return Done(new DiscoverPage(items, Math.Max(1, page), (keys.Count + SliderPageSize - 1) / SliderPageSize, keys.Count));
    }

    public Task<ApertureResult<IReadOnlyList<DiscoverGenre>>> GetGenresAsync(DiscoverKind kind, CancellationToken ct = default)
    {
        Calls.Add($"GetGenres({kind})");
        if (Fail<IReadOnlyList<DiscoverGenre>>() is { } failed) return failed;
        return Done((IReadOnlyList<DiscoverGenre>)_genres.Where(g => g.Kind == kind).ToList());
    }

    public Task<ApertureResult<DiscoverPage>> DiscoverGenreAsync(DiscoverKind kind, int genreId, int page = 1,
        CancellationToken ct = default)
    {
        Calls.Add($"DiscoverGenre({kind},{genreId},{page})");
        if (Fail<DiscoverPage>() is { } failed) return failed;
        var items = _genreTitles.GetValueOrDefault((kind, genreId), []).Select(k => _titles[k].Item).ToList();
        return Done(new DiscoverPage(items, 1, items.Count > 0 ? 1 : 0, items.Count));
    }

    public Task<ApertureResult<DiscoverPage>> DiscoverCompanyAsync(DiscoverKind kind, int companyId, int page = 1,
        CancellationToken ct = default)
    {
        Calls.Add($"DiscoverCompany({kind},{companyId},{page})");
        if (Fail<DiscoverPage>() is { } failed) return failed;
        var items = _companyTitles.GetValueOrDefault((kind, companyId), []).Select(k => _titles[k].Item).ToList();
        return Done(new DiscoverPage(items, 1, items.Count > 0 ? 1 : 0, items.Count));
    }

    public Task<ApertureResult<PersonDetail>> GetPersonAsync(int tmdbId, CancellationToken ct = default)
    {
        Calls.Add($"GetPerson({tmdbId})");
        if (Fail<PersonDetail>() is { } failed) return failed;
        var credited = _titles.Values.Where(t => t.Cast.Any(c => c.TmdbId == tmdbId)).ToList();
        if (credited.Count == 0)
            return Task.FromResult(ApertureResult<PersonDetail>.Fail(ApertureFailureKind.NotFound, "no such person", 404));
        var person = credited[0].Cast.First(c => c.TmdbId == tmdbId);
        return Done(new PersonDetail
        {
            TmdbId = tmdbId,
            Name = person.Name,
            Biography = $"{person.Name} is a fixture actor.",
            KnownFor = "Acting",
            Credits = credited.Select(t => t.Item).OrderByDescending(i => i.Year).ToList(),
        });
    }

    public Task<ApertureResult<DiscoverPage>> GetWatchlistAsync(int page = 1, CancellationToken ct = default)
    {
        Calls.Add("GetWatchlist");
        if (Fail<DiscoverPage>() is { } failed) return failed;
        var items = _watchlist.Select(k => _titles[k].Item).ToList();
        return Done(new DiscoverPage(items, 1, items.Count > 0 ? 1 : 0, items.Count));
    }

    public Task<ApertureResult<bool>> SetWatchlistAsync(DiscoverItem item, bool on, CancellationToken ct = default)
    {
        Calls.Add($"{(on ? "Watchlist" : "Unwatchlist")}({item.Key})");
        if (Fail<bool>() is { } failed) return failed;
        if (on) _watchlist.Add(item.Key); else _watchlist.Remove(item.Key);
        return Done(true);
    }

    public Task<ApertureResult<int>> ReportIssueAsync(NewIssue issue, CancellationToken ct = default)
    {
        Calls.Add($"ReportIssue({issue.MediaId},{issue.Type},s{issue.Season}e{issue.Episode})");
        if (Fail<int>() is { } failed) return failed;
        if (!User.Has(RequestPermission.CreateIssues) && !User.Has(RequestPermission.ManageIssues))
            return Task.FromResult(ApertureResult<int>.Fail(ApertureFailureKind.Forbidden, "not allowed", 403));
        Reports.Add(issue);
        var title = _titles.Values.FirstOrDefault(t => t.MediaId == issue.MediaId);
        if (title is null)
            return Task.FromResult(ApertureResult<int>.Fail(ApertureFailureKind.NotFound, "Media does not exist.", 404));
        var record = NewIssueRecord(title, issue.Type, issue.Message, issue.Season, issue.Episode, User.Id, User.DisplayName);
        _issues.Add(record);
        return Done(record.Id);
    }

    // ── Issues ───────────────────────────────────────────────────────────

    private readonly List<MediaIssue> _issues = [];
    private int _nextIssueId = 1;
    private int _nextCommentId = 1;

    /// <summary>Someone's report — <see cref="OtherUser"/>'s unless another name is given.</summary>
    public MediaIssue AddIssue(string key, IssueType type, string message, int? season = null, int? episode = null,
        IssueStatus status = IssueStatus.Open, int byId = 2, string by = OtherUser, DateTimeOffset? at = null)
    {
        var record = NewIssueRecord(_titles[key], type, message, season, episode, byId, by, at) with { Status = status };
        _issues.Add(record);
        return record;
    }

    /// <summary>Someone else answers in a report's conversation.</summary>
    public void AddComment(int issueId, string message, int byId = 2, string by = OtherUser)
    {
        int at = _issues.FindIndex(i => i.Id == issueId);
        var issue = _issues[at];
        _issues[at] = issue with
        {
            Comments = [.. issue.Comments, new IssueComment(_nextCommentId++, message, byId, by, DateTimeOffset.UtcNow)],
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public MediaIssue? Issue(int id) => _issues.FirstOrDefault(i => i.Id == id);

    public Task<ApertureResult<IssuePage>> GetIssuesAsync(IssueFilter filter, int skip = 0, int take = 20,
        CancellationToken ct = default)
    {
        Calls.Add($"GetIssues({filter},{skip})");
        if (Fail<IssuePage>() is { } failed) return failed;
        IEnumerable<MediaIssue> list = _issues.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id);
        if (!CanSeeAllIssues) list = list.Where(i => i.ReportedById == User.Id);
        list = filter switch
        {
            IssueFilter.Open => list.Where(i => i.Status == IssueStatus.Open),
            IssueFilter.Resolved => list.Where(i => i.Status == IssueStatus.Resolved),
            _ => list,
        };
        var all = list.ToList();
        return Done(new IssuePage(all.Skip(skip).Take(take).Select(Fresh).ToList(), all.Count, skip));
    }

    public Task<ApertureResult<MediaIssue>> GetIssueAsync(int issueId, CancellationToken ct = default)
    {
        Calls.Add($"GetIssue({issueId})");
        if (Fail<MediaIssue>() is { } failed) return failed;
        if (Issue(issueId) is not { } issue)
            return Task.FromResult(ApertureResult<MediaIssue>.Fail(ApertureFailureKind.Server, "Issue not found.", 500));
        if (issue.ReportedById != User.Id && !CanSeeAllIssues)
            return Task.FromResult(ApertureResult<MediaIssue>.Fail(ApertureFailureKind.Forbidden, "not allowed", 403));
        return Done(Fresh(issue));
    }

    public Task<ApertureResult<MediaIssue>> CommentOnIssueAsync(int issueId, string message, CancellationToken ct = default)
    {
        Calls.Add($"CommentOnIssue({issueId},{message})");
        if (Fail<MediaIssue>() is { } failed) return failed;
        if (Issue(issueId) is not { } issue)
            return Task.FromResult(ApertureResult<MediaIssue>.Fail(ApertureFailureKind.Server, "Issue not found.", 500));
        if (issue.ReportedById != User.Id && !User.Has(RequestPermission.ManageIssues))
            return Task.FromResult(ApertureResult<MediaIssue>.Fail(ApertureFailureKind.Forbidden, "not allowed", 403));
        AddComment(issueId, message, User.Id, User.DisplayName);
        return Done(Fresh(Issue(issueId)!));
    }

    public Task<ApertureResult<MediaIssue>> SetIssueStatusAsync(int issueId, IssueStatus status, CancellationToken ct = default)
    {
        Calls.Add($"SetIssueStatus({issueId},{status})");
        if (Fail<MediaIssue>() is { } failed) return failed;
        int at = _issues.FindIndex(i => i.Id == issueId);
        if (at < 0) return Task.FromResult(ApertureResult<MediaIssue>.Fail(ApertureFailureKind.Server, "Issue not found.", 500));
        if (_issues[at].ReportedById != User.Id && !User.Has(RequestPermission.ManageIssues))
            return Task.FromResult(ApertureResult<MediaIssue>.Fail(ApertureFailureKind.Forbidden, "not allowed", 403));
        _issues[at] = _issues[at] with { Status = status, UpdatedAt = DateTimeOffset.UtcNow };
        return Done(Fresh(_issues[at]));
    }

    public Task<ApertureResult<IssueCounts>> GetIssueCountsAsync(CancellationToken ct = default)
    {
        Calls.Add("GetIssueCounts");
        if (Fail<IssueCounts>() is { } failed) return failed;
        // Like the server's: everyone's, whoever asks.
        return Done(new IssueCounts(_issues.Count(i => i.Status == IssueStatus.Open),
            _issues.Count(i => i.Status == IssueStatus.Resolved)));
    }

    private bool CanSeeAllIssues => User.CanSeeIssues;

    /// <summary>The title as it stands now — a report about a film that has since arrived
    /// opens the library's page.</summary>
    private MediaIssue Fresh(MediaIssue issue) =>
        _titles.TryGetValue(issue.MediaKey, out var t)
            ? issue with { JellyfinItemId = t.Item.JellyfinItemId, Title = t.Item.Title, PosterPath = t.Item.PosterPath }
            : issue;

    private MediaIssue NewIssueRecord(DiscoverDetail title, IssueType type, string message, int? season, int? episode,
        int byId, string by, DateTimeOffset? at = null)
    {
        var when = at ?? DateTimeOffset.UtcNow;
        return new MediaIssue
        {
            Id = _nextIssueId++,
            Type = type,
            Status = IssueStatus.Open,
            Kind = title.Item.Kind,
            TmdbId = title.Item.TmdbId,
            Title = title.Item.Title,
            Year = title.Item.Year,
            PosterPath = title.Item.PosterPath,
            JellyfinItemId = title.Item.JellyfinItemId,
            Season = season is > 0 ? season : null,
            Episode = season is > 0 && episode is > 0 ? episode : null,
            ReportedById = byId,
            ReportedBy = by,
            CreatedAt = when,
            UpdatedAt = when,
            Comments = [new IssueComment(_nextCommentId++, message, byId, by, when)],
        };
    }

    public Task<ApertureResult<IReadOnlyList<RequestService>>> GetServicesAsync(DiscoverKind kind, CancellationToken ct = default)
    {
        Calls.Add($"GetServices({kind})");
        if (Fail<IReadOnlyList<RequestService>>() is { } failed) return failed;
        return Done((IReadOnlyList<RequestService>)[kind == DiscoverKind.Movie ? Radarr : Sonarr]);
    }

    public Task<ApertureResult<RequestServiceDetails>> GetServiceDetailsAsync(DiscoverKind kind, int serviceId,
        CancellationToken ct = default)
    {
        Calls.Add($"GetServiceDetails({kind},{serviceId})");
        if (Fail<RequestServiceDetails>() is { } failed) return failed;
        return kind == DiscoverKind.Movie
            ? Done(new RequestServiceDetails(Radarr,
                [new QualityProfile(4, "HD-1080p"), new QualityProfile(5, "Ultra-HD")],
                [new RootFolder(1, "/media/movies", 2_000_000_000_000), new RootFolder(2, "/media/kids", 500_000_000_000)]))
            : Done(new RequestServiceDetails(Sonarr,
                [new QualityProfile(6, "HD-720p/1080p"), new QualityProfile(7, "Any")],
                [new RootFolder(1, "/media/shows", 2_000_000_000_000)]));
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

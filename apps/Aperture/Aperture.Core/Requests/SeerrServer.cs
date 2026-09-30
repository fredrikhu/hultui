using System.Collections.Concurrent;
using System.Globalization;

namespace Aperture.Core.Requests;

/// <summary>
/// A signed-in request server — Jellyseerr 2.x or Seerr 3.x — over its REST API. Built
/// by <see cref="SeerrConnection"/>, which is where addresses, sign-in and resuming live;
/// this class is the session and the vocabulary mapping.
///
/// <para>Hand-written rather than generated: the API is a few dozen endpoints of which
/// Aperture reads a dozen, the published spec disagrees with the servers in places, and a
/// source-generated JSON context is the one serializer path that is known to survive
/// Native AOT without warnings. Same reasoning, and same shape, as Junction's GitLab
/// client.</para>
/// </summary>
public sealed class SeerrServer : IRequestServer
{
    private readonly SeerrHttp _http;

    /// <summary>Titles already fetched this session, for the requests list — the list call
    /// carries only ids, and the same dozen titles recur on every page of it.</summary>
    private readonly ConcurrentDictionary<string, DiscoverDetail> _titles = new(StringComparer.Ordinal);
    private const int TitleCacheCap = 400;

    /// <summary>Title look-ups in flight at once while filling in a page of requests.</summary>
    private const int TitleFetchParallelism = 6;

    internal SeerrServer(RequestServerInfo server, RequestUser user, SeerrHttp http, RequestServerAccountData account,
        string secret)
    {
        Server = server;
        User = user;
        _http = http;
        Account = account;
        Secret = secret;
    }

    public RequestServerInfo Server { get; }
    public RequestUser User { get; }

    /// <summary>Who and where, for the settings file. No secret in it.</summary>
    public RequestServerAccountData Account { get; }

    /// <summary>The session cookie or API key — for the credential store, and nowhere else.</summary>
    internal string Secret { get; }

    // ── Discovery ────────────────────────────────────────────────────────

    public async Task<ApertureResult<DiscoverPage>> DiscoverAsync(DiscoverShelf shelf, int page = 1, CancellationToken ct = default)
    {
        var path = shelf switch
        {
            DiscoverShelf.Trending => "discover/trending",
            DiscoverShelf.PopularMovies => "discover/movies",
            DiscoverShelf.UpcomingMovies => "discover/movies/upcoming",
            DiscoverShelf.PopularSeries => "discover/tv",
            DiscoverShelf.UpcomingSeries => "discover/tv/upcoming",
            _ => "discover/trending",
        };
        // The shelves list one kind each except Trending, which mixes them — so each hit
        // is told its kind by the shelf when the payload does not say.
        DiscoverKind? kind = shelf switch
        {
            DiscoverShelf.PopularMovies or DiscoverShelf.UpcomingMovies => DiscoverKind.Movie,
            DiscoverShelf.PopularSeries or DiscoverShelf.UpcomingSeries => DiscoverKind.Tv,
            _ => null,
        };
        var result = await _http.GetAsync($"{path}?page={Math.Max(1, page)}", SeerrJson.Default.SeerrResultPageDto, ct)
            .ConfigureAwait(false);
        return result.Ok ? ApertureResult<DiscoverPage>.Success(MapPage(result.Value, kind)) : result.As<DiscoverPage>();
    }

    public async Task<ApertureResult<DiscoverPage>> SearchAsync(string query, int page = 1, CancellationToken ct = default)
    {
        var term = (query ?? "").Trim();
        if (term.Length == 0) return ApertureResult<DiscoverPage>.Success(DiscoverPage.Empty);
        // EscapeDataString encodes the RFC 3986 reserved set as well — the server's request
        // validator rejects a query with a bare ' or ! in it.
        var result = await _http.GetAsync($"search?query={Uri.EscapeDataString(term)}&page={Math.Max(1, page)}",
            SeerrJson.Default.SeerrResultPageDto, ct).ConfigureAwait(false);
        return result.Ok ? ApertureResult<DiscoverPage>.Success(MapPage(result.Value, null)) : result.As<DiscoverPage>();
    }

    public async Task<ApertureResult<DiscoverDetail>> GetDetailAsync(DiscoverKind kind, int tmdbId, CancellationToken ct = default)
    {
        ApertureResult<DiscoverDetail> result;
        if (kind == DiscoverKind.Movie)
        {
            var movie = await _http.GetAsync($"movie/{tmdbId}", SeerrJson.Default.SeerrMovieDetailsDto, ct).ConfigureAwait(false);
            result = movie.Ok ? ApertureResult<DiscoverDetail>.Success(MapMovie(movie.Value)) : movie.As<DiscoverDetail>();
        }
        else
        {
            var tv = await _http.GetAsync($"tv/{tmdbId}", SeerrJson.Default.SeerrTvDetailsDto, ct).ConfigureAwait(false);
            result = tv.Ok ? ApertureResult<DiscoverDetail>.Success(MapTv(tv.Value)) : tv.As<DiscoverDetail>();
        }
        if (result.Ok) Remember(result.Value);
        return result;
    }

    public async Task<ApertureResult<DiscoverPage>> GetRecommendationsAsync(DiscoverKind kind, int tmdbId, int page = 1,
        CancellationToken ct = default)
    {
        var path = kind == DiscoverKind.Movie ? $"movie/{tmdbId}/recommendations" : $"tv/{tmdbId}/recommendations";
        var result = await _http.GetAsync($"{path}?page={Math.Max(1, page)}", SeerrJson.Default.SeerrResultPageDto, ct)
            .ConfigureAwait(false);
        return result.Ok ? ApertureResult<DiscoverPage>.Success(MapPage(result.Value, kind)) : result.As<DiscoverPage>();
    }

    // ── Requests ─────────────────────────────────────────────────────────

    public async Task<ApertureResult<RequestPage>> GetRequestsAsync(RequestFilter filter, bool mine, int skip = 0,
        int take = 20, CancellationToken ct = default)
    {
        var query = $"request?take={Math.Clamp(take, 1, 100)}&skip={Math.Max(0, skip)}&sort=added&filter={FilterName(filter)}";
        if (mine) query += $"&requestedBy={User.Id}";
        var result = await _http.GetAsync(query, SeerrJson.Default.SeerrRequestPageDto, ct).ConfigureAwait(false);
        if (!result.Ok) return result.As<RequestPage>();

        var requests = (result.Value.Results ?? []).Select(r => MapRequest(r)).ToList();
        // The list carries ids, not titles: fill them in from the detail calls, a few at a
        // time and from the session's cache first. A title that will not load leaves its
        // row saying "TMDB 603" rather than failing the whole page.
        using var gate = new SemaphoreSlim(TitleFetchParallelism);
        var named = await Task.WhenAll(requests.Select(async request =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var title = await TitleAsync(request.Kind, request.TmdbId, ct).ConfigureAwait(false);
                return title is null ? request : request with
                {
                    Title = title.Item.Title,
                    Year = title.Item.Year,
                    PosterPath = title.Item.PosterPath,
                };
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        int total = result.Value.PageInfo?.Results ?? named.Length;
        return ApertureResult<RequestPage>.Success(new RequestPage(named, total, skip));
    }

    /// <summary>The server's filter names — exactly its list, since its validator refuses
    /// anything else with a 400.</summary>
    internal static string FilterName(RequestFilter filter) => filter switch
    {
        RequestFilter.Pending => "pending",
        RequestFilter.Approved => "approved",
        RequestFilter.Processing => "processing",
        RequestFilter.Available => "available",
        RequestFilter.Unavailable => "unavailable",
        RequestFilter.Failed => "failed",
        RequestFilter.Completed => "completed",
        _ => "all",
    };

    public async Task<ApertureResult<int>> GetPendingCountAsync(CancellationToken ct = default)
    {
        var result = await _http.GetAsync("request/count", SeerrJson.Default.SeerrRequestCountDto, ct).ConfigureAwait(false);
        return result.Ok ? ApertureResult<int>.Success(result.Value.Pending) : result.As<int>();
    }

    public async Task<ApertureResult<RequestQuotas>> GetQuotaAsync(CancellationToken ct = default)
    {
        var result = await _http.GetAsync($"user/{User.Id}/quota", SeerrJson.Default.SeerrQuotaDto, ct).ConfigureAwait(false);
        if (!result.Ok) return result.As<RequestQuotas>();
        static RequestQuota Part(SeerrQuotaPartDto? p) => new(
            p?.Limit is > 0 ? p.Limit : null, p?.Days ?? 0, p?.Used ?? 0,
            p?.Limit is > 0 ? p.Remaining : null, p?.Restricted ?? false);
        return ApertureResult<RequestQuotas>.Success(new RequestQuotas(Part(result.Value.Movie), Part(result.Value.Tv)));
    }

    public async Task<ApertureResult<MediaRequest>> RequestAsync(NewRequest request, CancellationToken ct = default)
    {
        var body = new SeerrNewRequestDto
        {
            MediaType = request.Kind == DiscoverKind.Movie ? "movie" : "tv",
            MediaId = request.TmdbId,
            TvdbId = request.Kind == DiscoverKind.Tv ? request.TvdbId : null,
            // Always the explicit list, never "all": "all" is resolved by the server against
            // its own idea of the seasons at the moment it reads it, and the list is what the
            // person saw and ticked.
            Seasons = request.Kind == DiscoverKind.Tv ? request.Seasons.ToList() : null,
            Is4k = request.Is4k,
            // Advanced choices go only where they were made; the server fills in its own
            // defaults for anything left out.
            ServerId = request.Options?.ServerId,
            ProfileId = request.Options?.ProfileId,
            RootFolder = request.Options?.RootFolder,
        };
        var result = await _http.PostAsync("request", body, SeerrJson.Default.SeerrNewRequestDto,
            SeerrJson.Default.SeerrRequestDto, ct).ConfigureAwait(false);
        Forget(request.Kind, request.TmdbId);
        if (!result.Ok) return result.As<MediaRequest>();
        // A 202 carries a sentence instead of a request: nothing was left to ask for.
        if (result.Value.Id <= 0)
            return ApertureResult<MediaRequest>.Fail(ApertureFailureKind.Invalid,
                result.Value.Message is { Length: > 0 } said ? said : "There was nothing left to request", 202);
        return ApertureResult<MediaRequest>.Success(MapRequest(result.Value, request.Kind, request.TmdbId));
    }

    public async Task<ApertureResult<MediaRequest>> DecideAsync(int requestId, bool approve, CancellationToken ct = default)
    {
        var result = await _http.PostAsync($"request/{requestId}/{(approve ? "approve" : "decline")}",
            SeerrJson.Default.SeerrRequestDto, ct).ConfigureAwait(false);
        if (!result.Ok) return result.As<MediaRequest>();
        var mapped = MapRequest(result.Value);
        Forget(mapped.Kind, mapped.TmdbId);
        return ApertureResult<MediaRequest>.Success(mapped);
    }

    public Task<ApertureResult<bool>> DeleteRequestAsync(int requestId, CancellationToken ct = default) =>
        _http.SendStatusAsync(HttpMethod.Delete, $"request/{requestId}", ct);

    // ── Slice 7b ─────────────────────────────────────────────────────────

    public async Task<ApertureResult<IReadOnlyList<DiscoverSlider>>> GetDiscoverLayoutAsync(CancellationToken ct = default)
    {
        var result = await _http.GetAsync("settings/discover", SeerrJson.Default.ListSeerrSliderDto, ct).ConfigureAwait(false);
        if (!result.Ok) return result.As<IReadOnlyList<DiscoverSlider>>();
        return ApertureResult<IReadOnlyList<DiscoverSlider>>.Success(result.Value
            .Where(s => Enum.IsDefined((SliderKind)s.Type))
            .OrderBy(s => s.Order)
            .Select(s => new DiscoverSlider(s.Id, (SliderKind)s.Type,
                NullIfEmpty(s.Title), NullIfEmpty(s.Data), s.Enabled, s.Order))
            .ToList());
    }

    public Task<ApertureResult<DiscoverPage>> GetSliderAsync(DiscoverSlider slider, int page = 1, CancellationToken ct = default)
    {
        int p = Math.Max(1, page);
        string Data() => Uri.EscapeDataString(slider.Data ?? "");
        (string Path, DiscoverKind? Kind)? route = slider.Kind switch
        {
            SliderKind.Trending => ($"discover/trending?page={p}", null),
            SliderKind.PopularMovies => ($"discover/movies?page={p}", DiscoverKind.Movie),
            SliderKind.UpcomingMovies => ($"discover/movies/upcoming?page={p}", DiscoverKind.Movie),
            SliderKind.PopularTv => ($"discover/tv?page={p}", DiscoverKind.Tv),
            SliderKind.UpcomingTv => ($"discover/tv/upcoming?page={p}", DiscoverKind.Tv),
            SliderKind.MovieKeyword when slider.Data is not null => ($"discover/movies?keywords={Data()}&page={p}", DiscoverKind.Movie),
            SliderKind.MovieGenre when slider.Data is not null => ($"discover/movies?genre={Data()}&page={p}", DiscoverKind.Movie),
            SliderKind.TvKeyword when slider.Data is not null => ($"discover/tv?keywords={Data()}&page={p}", DiscoverKind.Tv),
            SliderKind.TvGenre when slider.Data is not null => ($"discover/tv?genre={Data()}&page={p}", DiscoverKind.Tv),
            SliderKind.Search when slider.Data is not null => ($"search?query={Data()}&page={p}", null),
            SliderKind.Studio when slider.Data is not null => ($"discover/movies?studio={Data()}&page={p}", DiscoverKind.Movie),
            SliderKind.Network when slider.Data is not null => ($"discover/tv?network={Data()}&page={p}", DiscoverKind.Tv),
            SliderKind.MovieStreaming when StreamingQuery(slider.Data) is { } q => ($"discover/movies?{q}&page={p}", DiscoverKind.Movie),
            SliderKind.TvStreaming when StreamingQuery(slider.Data) is { } q => ($"discover/tv?{q}&page={p}", DiscoverKind.Tv),
            _ => null,
        };
        return route is { } r
            ? PageAsync(r.Path, r.Kind, ct)
            : Task.FromResult(ApertureResult<DiscoverPage>.Success(DiscoverPage.Empty));
    }

    /// <summary>
    /// A streaming-service row's data as the discover routes want it. The server stores it
    /// as its web page's selector writes it — <c>SE,8|337</c>: a region, then the services'
    /// TMDB ids, any of which will do — and its own page splits it at the comma. Anything
    /// else is refused (null) rather than sent: without the filter the route answers with
    /// every film there is, under a heading that says "Netflix".
    /// </summary>
    internal static string? StreamingQuery(string? data)
    {
        if (data?.Split(',') is not [var region, var providers]) return null;
        region = region.Trim();
        providers = providers.Trim();
        if (region.Length != 2 || !region.All(char.IsAsciiLetter)) return null;
        if (providers.Split('|') is not { Length: > 0 } ids
            || !ids.All(id => id.Length > 0 && id.All(char.IsAsciiDigit)))
            return null;
        return $"watchRegion={Uri.EscapeDataString(region.ToUpperInvariant())}&watchProviders={Uri.EscapeDataString(providers)}";
    }

    public async Task<ApertureResult<IReadOnlyList<DiscoverGenre>>> GetGenresAsync(DiscoverKind kind, CancellationToken ct = default)
    {
        var result = await _http.GetAsync($"discover/genreslider/{(kind == DiscoverKind.Movie ? "movie" : "tv")}",
            SeerrJson.Default.ListSeerrGenreSliderDto, ct).ConfigureAwait(false);
        if (!result.Ok) return result.As<IReadOnlyList<DiscoverGenre>>();
        return ApertureResult<IReadOnlyList<DiscoverGenre>>.Success(result.Value
            .Where(g => g.Id > 0 && !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => new DiscoverGenre(g.Id, g.Name!, kind, g.Backdrops?.FirstOrDefault(b => !string.IsNullOrEmpty(b))))
            .ToList());
    }

    public Task<ApertureResult<DiscoverPage>> DiscoverGenreAsync(DiscoverKind kind, int genreId, int page = 1,
        CancellationToken ct = default) =>
        PageAsync($"discover/{(kind == DiscoverKind.Movie ? "movies" : "tv")}?genre={genreId}&page={Math.Max(1, page)}",
            kind, ct);

    public Task<ApertureResult<DiscoverPage>> DiscoverCompanyAsync(DiscoverKind kind, int companyId, int page = 1,
        CancellationToken ct = default) =>
        PageAsync(kind == DiscoverKind.Movie
            ? $"discover/movies/studio/{companyId}?page={Math.Max(1, page)}"
            : $"discover/tv/network/{companyId}?page={Math.Max(1, page)}", kind, ct);

    private async Task<ApertureResult<DiscoverPage>> PageAsync(string pathAndQuery, DiscoverKind? kind, CancellationToken ct)
    {
        var result = await _http.GetAsync(pathAndQuery, SeerrJson.Default.SeerrResultPageDto, ct).ConfigureAwait(false);
        return result.Ok ? ApertureResult<DiscoverPage>.Success(MapPage(result.Value, kind)) : result.As<DiscoverPage>();
    }

    public async Task<ApertureResult<PersonDetail>> GetPersonAsync(int tmdbId, CancellationToken ct = default)
    {
        var person = _http.GetAsync($"person/{tmdbId}", SeerrJson.Default.SeerrPersonDto, ct);
        var credits = _http.GetAsync($"person/{tmdbId}/combined_credits", SeerrJson.Default.SeerrCombinedCreditsDto, ct);
        await Task.WhenAll(person, credits).ConfigureAwait(false);
        if (!person.Result.Ok) return person.Result.As<PersonDetail>();
        // Credits that fail to load leave a page with the person and nothing under them,
        // which is still a page — not a failure of the whole thing.
        var list = credits.Result.Ok ? MapCredits(credits.Result.Value) : [];
        return ApertureResult<PersonDetail>.Success(MapPerson(person.Result.Value, list));
    }

    public async Task<ApertureResult<DiscoverPage>> GetWatchlistAsync(int page = 1, CancellationToken ct = default)
    {
        var result = await _http.GetAsync($"discover/watchlist?page={Math.Max(1, page)}",
            SeerrJson.Default.SeerrWatchlistPageDto, ct).ConfigureAwait(false);
        if (!result.Ok) return result.As<DiscoverPage>();

        // The list carries a TMDB id and a title and nothing to draw: artwork and state
        // come from the title's own record, a few at a time and from the cache first.
        using var gate = new SemaphoreSlim(TitleFetchParallelism);
        var entries = (result.Value.Results ?? [])
            .Where(e => e.TmdbId > 0 && e.MediaType is "movie" or "tv")
            .ToList();
        var items = await Task.WhenAll(entries.Select(async e =>
        {
            var kind = e.MediaType == "tv" ? DiscoverKind.Tv : DiscoverKind.Movie;
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var title = await TitleAsync(kind, e.TmdbId, ct).ConfigureAwait(false);
                return title?.Item ?? new DiscoverItem { TmdbId = e.TmdbId, Kind = kind, Title = e.Title ?? $"TMDB {e.TmdbId}" };
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        return ApertureResult<DiscoverPage>.Success(new DiscoverPage(items, result.Value.Page ?? 1,
            result.Value.TotalPages ?? 1, result.Value.TotalResults ?? items.Length));
    }

    public async Task<ApertureResult<bool>> SetWatchlistAsync(DiscoverItem item, bool on, CancellationToken ct = default)
    {
        var mediaType = item.Kind == DiscoverKind.Movie ? "movie" : "tv";
        Forget(item.Kind, item.TmdbId);
        if (on)
        {
            var body = new SeerrWatchlistAddDto { TmdbId = item.TmdbId, MediaType = mediaType, Title = item.Title };
            var added = await _http.SendStatusAsync(HttpMethod.Post, "watchlist", ct,
                SeerrHttp.JsonContent(body, SeerrJson.Default.SeerrWatchlistAddDto)).ConfigureAwait(false);
            // Already on it is where the user wanted it: a 409 is the outcome, not a failure.
            return added.Ok || added.Failure!.Status == 409 ? ApertureResult<bool>.Success(true) : added;
        }

        // The media type became a required parameter in 3.2 — and the older validator
        // refuses a parameter it does not know, so it is sent only where it is expected.
        var path = $"watchlist/{item.TmdbId}";
        if (Server.ParsedVersion >= new Version(3, 2)) path += $"?mediaType={mediaType}";
        var removed = await _http.SendStatusAsync(HttpMethod.Delete, path, ct).ConfigureAwait(false);
        return removed.Ok || removed.Failure!.Kind == ApertureFailureKind.NotFound ? ApertureResult<bool>.Success(true) : removed;
    }

    public async Task<ApertureResult<int>> ReportIssueAsync(NewIssue issue, CancellationToken ct = default)
    {
        var body = new SeerrNewIssueDto
        {
            IssueType = (int)issue.Type,
            Message = issue.Message,
            MediaId = issue.MediaId,
            ProblemSeason = issue.Season ?? 0,
            ProblemEpisode = issue.Season is null ? 0 : issue.Episode ?? 0,
        };
        var result = await _http.PostAsync("issue", body, SeerrJson.Default.SeerrNewIssueDto,
            SeerrJson.Default.SeerrIdDto, ct).ConfigureAwait(false);
        return result.Ok ? ApertureResult<int>.Success(result.Value.Id) : result.As<int>();
    }

    public async Task<ApertureResult<IssuePage>> GetIssuesAsync(IssueFilter filter, int skip = 0, int take = 20,
        CancellationToken ct = default)
    {
        var name = filter switch { IssueFilter.Open => "open", IssueFilter.Resolved => "resolved", _ => "all" };
        var query = $"issue?take={Math.Clamp(take, 1, 100)}&skip={Math.Max(0, skip)}&sort=added&filter={name}";
        var result = await _http.GetAsync(query, SeerrJson.Default.SeerrIssuePageDto, ct).ConfigureAwait(false);
        if (!result.Ok) return result.As<IssuePage>();

        // Like the request list: ids only, titles filled in from the cache or a detail call.
        using var gate = new SemaphoreSlim(TitleFetchParallelism);
        var named = await Task.WhenAll((result.Value.Results ?? []).Select(MapIssue).Select(async issue =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await NamedAsync(issue, ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        int total = result.Value.PageInfo?.Results ?? named.Length;
        return ApertureResult<IssuePage>.Success(new IssuePage(named, total, skip));
    }

    public async Task<ApertureResult<MediaIssue>> GetIssueAsync(int issueId, CancellationToken ct = default)
    {
        var result = await _http.GetAsync($"issue/{issueId}", SeerrJson.Default.SeerrIssueDto, ct).ConfigureAwait(false);
        return result.Ok ? ApertureResult<MediaIssue>.Success(await NamedAsync(MapIssue(result.Value), ct).ConfigureAwait(false))
            : result.As<MediaIssue>();
    }

    public async Task<ApertureResult<MediaIssue>> CommentOnIssueAsync(int issueId, string message, CancellationToken ct = default)
    {
        var posted = await _http.PostAsync($"issue/{issueId}/comment", new SeerrCommentBodyDto { Message = message },
            SeerrJson.Default.SeerrCommentBodyDto, SeerrJson.Default.SeerrIssueDto, ct).ConfigureAwait(false);
        // The answer's comments carry no authors; the single issue does.
        return posted.Ok ? await GetIssueAsync(issueId, ct).ConfigureAwait(false) : posted.As<MediaIssue>();
    }

    public async Task<ApertureResult<MediaIssue>> SetIssueStatusAsync(int issueId, IssueStatus status, CancellationToken ct = default)
    {
        var posted = await _http.PostAsync($"issue/{issueId}/{(status == IssueStatus.Resolved ? "resolved" : "open")}",
            SeerrJson.Default.SeerrIssueDto, ct).ConfigureAwait(false);
        if (posted.Ok) return await GetIssueAsync(issueId, ct).ConfigureAwait(false);
        // The server answers "not yours to change" with a 401, which everywhere else means
        // the session is gone — and would sign the person out of the request server.
        return posted.Failure!.Kind == ApertureFailureKind.Unauthorized
            ? ApertureResult<MediaIssue>.Fail(ApertureFailureKind.Forbidden, posted.Failure.Message, 403)
            : posted.As<MediaIssue>();
    }

    public async Task<ApertureResult<IssueCounts>> GetIssueCountsAsync(CancellationToken ct = default)
    {
        var result = await _http.GetAsync("issue/count", SeerrJson.Default.SeerrIssueCountDto, ct).ConfigureAwait(false);
        return result.Ok ? ApertureResult<IssueCounts>.Success(new IssueCounts(result.Value.Open, result.Value.Closed))
            : result.As<IssueCounts>();
    }

    private async Task<MediaIssue> NamedAsync(MediaIssue issue, CancellationToken ct)
    {
        var title = await TitleAsync(issue.Kind, issue.TmdbId, ct).ConfigureAwait(false);
        return title is null ? issue : issue with
        {
            Title = title.Item.Title,
            Year = title.Item.Year,
            PosterPath = title.Item.PosterPath,
            JellyfinItemId = issue.JellyfinItemId ?? title.Item.JellyfinItemId,
        };
    }

    public async Task<ApertureResult<IReadOnlyList<RequestService>>> GetServicesAsync(DiscoverKind kind, CancellationToken ct = default)
    {
        var result = await _http.GetAsync(ServicePath(kind), SeerrJson.Default.ListSeerrServiceDto, ct).ConfigureAwait(false);
        if (!result.Ok) return result.As<IReadOnlyList<RequestService>>();
        return ApertureResult<IReadOnlyList<RequestService>>.Success(result.Value.Select(MapService).ToList());
    }

    public async Task<ApertureResult<RequestServiceDetails>> GetServiceDetailsAsync(DiscoverKind kind, int serviceId,
        CancellationToken ct = default)
    {
        var result = await _http.GetAsync($"{ServicePath(kind)}/{serviceId}", SeerrJson.Default.SeerrServiceDetailsDto, ct)
            .ConfigureAwait(false);
        if (!result.Ok) return result.As<RequestServiceDetails>();
        var d = result.Value;
        return ApertureResult<RequestServiceDetails>.Success(new RequestServiceDetails(
            MapService(d.Server ?? new SeerrServiceDto { Id = serviceId }),
            (d.Profiles ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => new QualityProfile(p.Id, p.Name!)).ToList(),
            (d.RootFolders ?? []).Where(f => !string.IsNullOrWhiteSpace(f.Path))
                .Select(f => new RootFolder(f.Id, f.Path!, f.FreeSpace is { } free ? (long)free : null)).ToList()));
    }

    private static string ServicePath(DiscoverKind kind) => kind == DiscoverKind.Movie ? "service/radarr" : "service/sonarr";

    private static RequestService MapService(SeerrServiceDto s) =>
        new(s.Id, NullIfEmpty(s.Name) ?? $"Server {s.Id}", s.Is4k, s.IsDefault, s.ActiveProfileId, NullIfEmpty(s.ActiveDirectory));

    public void Dispose() => _http.Dispose();

    // ── The title cache ──────────────────────────────────────────────────

    private async Task<DiscoverDetail?> TitleAsync(DiscoverKind kind, int tmdbId, CancellationToken ct)
    {
        if (tmdbId <= 0) return null;
        if (_titles.TryGetValue(Key(kind, tmdbId), out var cached)) return cached;
        var fetched = await GetDetailAsync(kind, tmdbId, ct).ConfigureAwait(false);
        return fetched.Ok ? fetched.Value : null;
    }

    private void Remember(DiscoverDetail detail)
    {
        if (_titles.Count >= TitleCacheCap) _titles.Clear();
        _titles[detail.Item.Key] = detail;
    }

    /// <summary>A title whose state just changed is dropped, so its next look is fresh.</summary>
    private void Forget(DiscoverKind kind, int tmdbId) => _titles.TryRemove(Key(kind, tmdbId), out _);

    private static string Key(DiscoverKind kind, int tmdbId) => $"{(kind == DiscoverKind.Movie ? "movie" : "tv")}:{tmdbId}";

    // ── Mapping ──────────────────────────────────────────────────────────

    internal static DiscoverPage MapPage(SeerrResultPageDto page, DiscoverKind? kind)
    {
        var items = new List<DiscoverItem>();
        foreach (var r in page.Results ?? [])
            if (MapResult(r, kind) is { } item) items.Add(item);
        return new DiscoverPage(items, page.Page ?? 1, page.TotalPages ?? 1, page.TotalResults ?? items.Count);
    }

    /// <summary>A hit, or null for what Aperture cannot request — people and collections.</summary>
    internal static DiscoverItem? MapResult(SeerrResultDto r, DiscoverKind? shelfKind)
    {
        DiscoverKind? kind = r.MediaType switch
        {
            "movie" => DiscoverKind.Movie,
            "tv" => DiscoverKind.Tv,
            null => shelfKind,
            _ => null,
        };
        if (kind is not { } k || r.Id <= 0) return null;
        var title = k == DiscoverKind.Movie ? r.Title ?? r.Name : r.Name ?? r.Title;
        if (string.IsNullOrWhiteSpace(title)) return null;
        return new DiscoverItem
        {
            TmdbId = r.Id,
            Kind = k,
            Title = title,
            Year = YearOf(k == DiscoverKind.Movie ? r.ReleaseDate : r.FirstAirDate),
            Overview = NullIfEmpty(r.Overview),
            PosterPath = NullIfEmpty(r.PosterPath),
            BackdropPath = NullIfEmpty(r.BackdropPath),
            VoteAverage = r.VoteAverage is > 0 && r.VoteCount is not 0 ? (float)r.VoteAverage.Value : null,
            Availability = MapAvailability(r.MediaInfo?.Status),
            Availability4k = MapAvailability(r.MediaInfo?.Status4k),
            JellyfinItemId = JellyfinId(r.MediaInfo?.JellyfinMediaId),
        };
    }

    /// <summary>A person's films and series: cast and crew together, each title once (a
    /// director who also acted is not listed twice), newest first — undated ones last.</summary>
    internal static IReadOnlyList<DiscoverItem> MapCredits(SeerrCombinedCreditsDto credits)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<(DiscoverItem Item, double Popularity)>();
        foreach (var c in (credits.Cast ?? []).Concat(credits.Crew ?? []))
        {
            var hit = MapResult(new SeerrResultDto
            {
                Id = c.Id, MediaType = c.MediaType, Title = c.Title, Name = c.Name,
                ReleaseDate = c.ReleaseDate, FirstAirDate = c.FirstAirDate,
                PosterPath = c.PosterPath, BackdropPath = c.BackdropPath,
                VoteAverage = c.VoteAverage, VoteCount = c.VoteCount, MediaInfo = c.MediaInfo,
            }, null);
            if (hit is not null && seen.Add(hit.Key)) items.Add((hit, c.Popularity ?? 0));
        }
        return items
            .OrderByDescending(i => i.Item.Year ?? int.MinValue)
            .ThenByDescending(i => i.Popularity)
            .Select(i => i.Item)
            .ToList();
    }

    internal static PersonDetail MapPerson(SeerrPersonDto p, IReadOnlyList<DiscoverItem> credits) => new()
    {
        TmdbId = p.Id,
        Name = NullIfEmpty(p.Name) ?? $"TMDB {p.Id}",
        Biography = NullIfEmpty(p.Biography),
        ProfilePath = NullIfEmpty(p.ProfilePath),
        KnownFor = NullIfEmpty(p.KnownForDepartment),
        Birthday = DateOf(p.Birthday),
        Deathday = DateOf(p.Deathday),
        PlaceOfBirth = NullIfEmpty(p.PlaceOfBirth),
        Credits = credits,
    };

    internal static DiscoverDetail MapMovie(SeerrMovieDetailsDto m)
    {
        var item = new DiscoverItem
        {
            TmdbId = m.Id,
            Kind = DiscoverKind.Movie,
            Title = m.Title ?? $"TMDB {m.Id}",
            Year = YearOf(m.ReleaseDate),
            Overview = NullIfEmpty(m.Overview),
            PosterPath = NullIfEmpty(m.PosterPath),
            BackdropPath = NullIfEmpty(m.BackdropPath),
            VoteAverage = m.VoteAverage is > 0 ? (float)m.VoteAverage.Value : null,
            Availability = MapAvailability(m.MediaInfo?.Status),
            Availability4k = MapAvailability(m.MediaInfo?.Status4k),
            JellyfinItemId = JellyfinId(m.MediaInfo?.JellyfinMediaId),
        };
        return new DiscoverDetail
        {
            Item = item,
            Tagline = NullIfEmpty(m.Tagline),
            Runtime = m.Runtime is > 0 ? TimeSpan.FromMinutes(m.Runtime.Value) : null,
            Genres = (m.Genres ?? []).Select(g => g.Name).OfType<string>().ToList(),
            Status = NullIfEmpty(m.Status),
            ReleaseDate = DateOf(m.ReleaseDate),
            Cast = MapCast(m.Credits),
            Requests = (m.MediaInfo?.Requests ?? []).Select(r => MapRequest(r, DiscoverKind.Movie, m.Id, m.MediaInfo)).ToList(),
            Downloads = MapDownloads(m.MediaInfo?.DownloadStatus),
            Downloads4k = MapDownloads(m.MediaInfo?.DownloadStatus4k),
            MediaId = m.MediaInfo?.Id is > 0 ? m.MediaInfo.Id : null,
            OnWatchlist = m.OnUserWatchlist ?? false,
        };
    }

    internal static DiscoverDetail MapTv(SeerrTvDetailsDto t)
    {
        var item = new DiscoverItem
        {
            TmdbId = t.Id,
            Kind = DiscoverKind.Tv,
            Title = t.Name ?? $"TMDB {t.Id}",
            Year = YearOf(t.FirstAirDate),
            Overview = NullIfEmpty(t.Overview),
            PosterPath = NullIfEmpty(t.PosterPath),
            BackdropPath = NullIfEmpty(t.BackdropPath),
            VoteAverage = t.VoteAverage is > 0 ? (float)t.VoteAverage.Value : null,
            Availability = MapAvailability(t.MediaInfo?.Status),
            Availability4k = MapAvailability(t.MediaInfo?.Status4k),
            JellyfinItemId = JellyfinId(t.MediaInfo?.JellyfinMediaId),
        };

        var statuses = (t.MediaInfo?.Seasons ?? []).GroupBy(s => s.SeasonNumber).ToDictionary(g => g.Key, g => g.Last());
        var seasons = (t.Seasons ?? [])
            // Specials are left out, as the server's own request form leaves them out.
            .Where(s => s.SeasonNumber > 0)
            .OrderBy(s => s.SeasonNumber)
            .Select(s =>
            {
                statuses.TryGetValue(s.SeasonNumber, out var st);
                return new DiscoverSeason(s.SeasonNumber, NullIfEmpty(s.Name) ?? $"Season {s.SeasonNumber}",
                    s.EpisodeCount ?? 0, YearOf(s.AirDate), MapAvailability(st?.Status), MapAvailability(st?.Status4k));
            })
            .ToList();

        return new DiscoverDetail
        {
            Item = item,
            Tagline = NullIfEmpty(t.Tagline),
            Runtime = t.EpisodeRunTime is { Count: > 0 } runs && runs[0] > 0 ? TimeSpan.FromMinutes(runs[0]) : null,
            Genres = (t.Genres ?? []).Select(g => g.Name).OfType<string>().ToList(),
            Status = NullIfEmpty(t.Status),
            ReleaseDate = DateOf(t.FirstAirDate),
            TvdbId = t.ExternalIds?.TvdbId is > 0 ? t.ExternalIds.TvdbId : t.MediaInfo?.TvdbId,
            Seasons = seasons,
            Cast = MapCast(t.Credits),
            Requests = (t.MediaInfo?.Requests ?? []).Select(r => MapRequest(r, DiscoverKind.Tv, t.Id, t.MediaInfo)).ToList(),
            Downloads = MapDownloads(t.MediaInfo?.DownloadStatus),
            Downloads4k = MapDownloads(t.MediaInfo?.DownloadStatus4k),
            MediaId = t.MediaInfo?.Id is > 0 ? t.MediaInfo.Id : null,
            OnWatchlist = t.OnUserWatchlist ?? false,
        };
    }

    /// <summary>
    /// A request. The request list nests the title's record (<c>media</c>) inside each
    /// request; a detail page's requests sit inside that record instead and carry no
    /// <c>media</c> of their own — so the kind, id and state can be handed down.
    /// </summary>
    internal static MediaRequest MapRequest(SeerrRequestDto r, DiscoverKind? kind = null, int? tmdbId = null,
        SeerrMediaInfoDto? parent = null)
    {
        var media = r.Media ?? parent;
        var k = r.Type switch
        {
            "movie" => DiscoverKind.Movie,
            "tv" => DiscoverKind.Tv,
            _ => media?.MediaType switch
            {
                "tv" => DiscoverKind.Tv,
                "movie" => DiscoverKind.Movie,
                _ => kind ?? DiscoverKind.Movie,
            },
        };
        return new MediaRequest
        {
            Id = r.Id,
            Status = Enum.IsDefined((RequestStatus)r.Status) ? (RequestStatus)r.Status : RequestStatus.Pending,
            Kind = k,
            TmdbId = media?.TmdbId is > 0 ? media.TmdbId : tmdbId ?? 0,
            Availability = MapAvailability(r.Is4k ? media?.Status4k : media?.Status),
            Is4k = r.Is4k,
            Seasons = (r.Seasons ?? []).Select(s => s.SeasonNumber).Where(n => n > 0).Order().ToList(),
            RequestedById = r.RequestedBy?.Id ?? 0,
            RequestedBy = r.RequestedBy?.Name ?? "someone",
            CreatedAt = DateTimeOffset.TryParse(r.CreatedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var at) ? at : DateTimeOffset.MinValue,
            JellyfinItemId = JellyfinId(r.Is4k ? media?.JellyfinMediaId4k : media?.JellyfinMediaId),
            Downloads = MapDownloads(r.Is4k ? media?.DownloadStatus4k : media?.DownloadStatus),
        };
    }

    internal static MediaIssue MapIssue(SeerrIssueDto i)
    {
        var media = i.Media;
        return new MediaIssue
        {
            Id = i.Id,
            Type = Enum.IsDefined((IssueType)i.IssueType) ? (IssueType)i.IssueType : IssueType.Other,
            Status = i.Status == (int)IssueStatus.Resolved ? IssueStatus.Resolved : IssueStatus.Open,
            Kind = media?.MediaType == "tv" ? DiscoverKind.Tv : DiscoverKind.Movie,
            TmdbId = media?.TmdbId ?? 0,
            JellyfinItemId = JellyfinId(media?.JellyfinMediaId),
            // The server stores 0 for "not about one season/episode".
            Season = i.ProblemSeason is > 0 ? i.ProblemSeason : null,
            Episode = i.ProblemSeason is > 0 && i.ProblemEpisode is > 0 ? i.ProblemEpisode : null,
            ReportedById = i.CreatedBy?.Id ?? 0,
            ReportedBy = i.CreatedBy?.Name ?? "someone",
            CreatedAt = TimeOf(i.CreatedAt),
            UpdatedAt = TimeOf(i.UpdatedAt ?? i.CreatedAt),
            // Oldest first, as a conversation reads. The list call leaves the authors out;
            // the first message is always the reporter's own.
            Comments = (i.Comments ?? []).OrderBy(c => TimeOf(c.CreatedAt)).ThenBy(c => c.Id)
                .Where(c => !string.IsNullOrWhiteSpace(c.Message))
                .Select((c, n) => new IssueComment(c.Id, c.Message!.Trim(),
                    c.User?.Id ?? (n == 0 ? i.CreatedBy?.Id ?? 0 : 0),
                    c.User?.Name ?? (n == 0 ? i.CreatedBy?.Name ?? "someone" : "someone"),
                    TimeOf(c.CreatedAt)))
                .ToList(),
        };
    }

    private static DateTimeOffset TimeOf(string? at) =>
        DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t : DateTimeOffset.MinValue;

    internal static RequestUser MapUser(SeerrUserDto u) =>
        new(u.Id, u.Name, (RequestPermission)(int)Math.Clamp(u.Permissions, 0, int.MaxValue));

    internal static IReadOnlyList<DownloadProgress> MapDownloads(List<SeerrDownloadDto>? downloads) =>
        (downloads ?? [])
            .Select(d => new DownloadProgress(
                d.Title ?? "",
                (long)Math.Max(0, d.Size ?? 0),
                (long)Math.Max(0, d.SizeLeft ?? 0),
                d.Status ?? "",
                // Radarr and Sonarr write their queue's time left as a .NET TimeSpan.
                TimeSpan.TryParse(d.TimeLeft, CultureInfo.InvariantCulture, out var left) && left >= TimeSpan.Zero
                    ? left : null,
                DateTimeOffset.TryParse(d.EstimatedCompletionTime, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var eta) ? eta : null,
                d.Episode?.SeasonNumber,
                d.Episode?.EpisodeNumber))
            .ToList();

    private static IReadOnlyList<DiscoverPerson> MapCast(SeerrCreditsDto? credits) =>
        (credits?.Cast ?? [])
            .Where(c => c.Id > 0 && !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => new DiscoverPerson(c.Id, c.Name!, NullIfEmpty(c.Character), NullIfEmpty(c.ProfilePath)))
            .ToList();

    /// <summary>The server's status number, or <see cref="Availability.Unknown"/> for a
    /// missing one or one newer than this client.</summary>
    internal static Availability MapAvailability(int? status) =>
        status is { } s && Enum.IsDefined((Availability)s) ? (Availability)s : Availability.Unknown;

    /// <summary>Jellyfin ids as Aperture uses them everywhere else: 32 hex digits, no
    /// dashes (Jellyfin's own <c>N</c> format). The request server stores whatever the
    /// scan gave it.</summary>
    internal static string? JellyfinId(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : id.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

    internal static int? YearOf(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            && y > 1800 ? y : null;

    private static DateOnly? DateOf(string? date) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

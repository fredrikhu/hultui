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
        };
    }

    internal static RequestUser MapUser(SeerrUserDto u) =>
        new(u.Id, u.Name, (RequestPermission)(int)Math.Clamp(u.Permissions, 0, int.MaxValue));

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

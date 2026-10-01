using Aperture.Core;
using Aperture.Core.Requests;
using HultUI;
using HultUI.Core.Elements;

namespace Aperture;

/// <summary>
/// The request server's side of the app: the session, what the discovery views read, and
/// the verbs — request, approve, decline, withdraw. The same rule as the library's data
/// file holds here: a build may <i>ask</i> for data but never wait for it, and every
/// loader is keyed on a generation that one bump invalidates.
///
/// <para>A request server is optional and secondary. It is only ever opened alongside a
/// signed-in Jellyfin server (it is paired with that server in the settings file), and
/// everything here is a no-op while <see cref="_requestServer"/> is null — the rail simply
/// has no Discover button.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>The connected request server, or null.</summary>
    private IRequestServer? _requestServer;

    /// <summary>Bumped when anything a request changes may be stale — after a request, a
    /// decision, a withdrawal, and by <see cref="Refresh"/>. Kept apart from the library's
    /// generation so that approving a request does not re-read every shelf of the library.</summary>
    private int _requestGeneration;

    internal IRequestServer? RequestServer => _requestServer;

    /// <summary>What the UI calls the request server: its own title, "Jellyseerr" unless
    /// an administrator renamed it.</summary>
    internal string RequestServerTitle => _requestServer?.Server.Title ?? "Jellyseerr";

    /// <summary>Where discovery artwork comes from — the request server's image cache when
    /// it keeps one, TMDB otherwise.</summary>
    private string? RequestImageBase => _requestServer?.Server.ImageBase;

    // ── The session ──────────────────────────────────────────────────────

    private readonly AsyncLoader<RequestConnectOutcome?> _requestResume = new(null);

    /// <summary>A connect attempt's result: a session, or why not.</summary>
    internal sealed record RequestConnectOutcome(IRequestServer? Server, ApertureFailure? Failure, bool NeedsPassword = false);

    /// <summary>
    /// Re-opens the request server paired with the signed-in Jellyfin server, from the
    /// secret in the credential store. Quietly: a request server that is off is not a
    /// reason to interrupt someone who opened the app to watch something, so a failure
    /// only shows on the Settings card.
    /// </summary>
    private void EnsureRequestServer()
    {
        if (_requestServer is not null || _library is not { } lib) return;
        if (_settings.RequestServerFor(lib.Server.Id) is not { } account) return;
        if (_settings.RequestSecretFor(lib.Server.Id) is not { } secret) return;

        // The data generation is in the key because a sign-in bumps it: signing out and back
        // in to the same server within one run must resume again, not hit the old outcome.
        _requestResume.Load(("request-resume", lib.Server.Id, _requestConnectAttempt, _dataGeneration), () =>
        {
            var result = SeerrConnection.ResumeAsync(account, secret).GetAwaiter().GetResult();
            return (RequestConnectOutcome?)new RequestConnectOutcome(result.Ok ? result.Value : null, result.Failure);
        }, outcome =>
        {
            if (outcome?.Server is { } server && _library?.Server.Id == account.JellyfinServerId)
                AdoptRequestServer(server);
            else
            {
                outcome?.Server?.Dispose();
                _requestConnectFailure = outcome?.Failure;
            }
        });
    }

    /// <summary>Takes a connected request server into use.</summary>
    private void AdoptRequestServer(IRequestServer server)
    {
        _requestServer?.Dispose();
        _requestServer = server;
        StartArrivalWatch();
        _requestConnectFailure = null;
        _requestGeneration++;
        Rerender();
    }

    /// <summary>Disconnects the request server and forgets it for this Jellyfin server.</summary>
    internal void DisconnectRequestServer()
    {
        var title = RequestServerTitle;   // before the session that knows it is gone
        if (_library is { } lib) _settings.ForgetRequestServer(lib.Server.Id);
        CloseRequestServer();
        Flash($"Disconnected from {title}");
        Rerender();
    }

    /// <summary>Drops the session without forgetting it — what signing out of Jellyfin does,
    /// because the request server is paired with the server being left, and what a session
    /// the server has ended does.</summary>
    private void CloseRequestServer()
    {
        StopProgressTimer();
        StopArrivalWatch();
        _requestServer?.Dispose();
        _requestServer = null;
        _requestGeneration++;
        _requestListItems.Clear();
        _requestListKey = null;
        _issueListItems.Clear();
        _issueListKey = null;
        // Off any view that needs it — their rail buttons have just gone, and a page with
        // no way back to it is a dead end.
        if (_stack.RemoveAll(r => r.View is ApertureView.Discover or ApertureView.DiscoverTitle or ApertureView.DiscoverGenre
                or ApertureView.DiscoverPerson or ApertureView.DiscoverCompany or ApertureView.DiscoverAll
                or ApertureView.DiscoverGenres or ApertureView.DiscoverCompanies or ApertureView.Requests
                or ApertureView.Issues or ApertureView.Issue) > 0
            && _stack.Count == 0)
            _stack.Add(new Route(ApertureView.Home));
    }

    // ── Discover's rows: the administrator's layout ──────────────────────

    /// <summary>One row of Discover, filled. One of the lists is used, by the row's kind:
    /// titles (requests become titles too), genres to browse, or studios and networks.
    /// A row of titles keeps its <see cref="Slider"/>, and whether the server has more of
    /// it than the first page — which is what "See all" opens. Genre, studio and network
    /// rows are whole already.</summary>
    internal sealed record DiscoverRow(string Key, string Title, SliderKind Kind,
        IReadOnlyList<DiscoverItem> Items, IReadOnlyList<DiscoverGenre> Genres, IReadOnlyList<DiscoverCompany> Companies,
        DiscoverSlider? Slider = null, bool HasMore = false)
    {
        public bool IsEmpty => Items.Count == 0 && Genres.Count == 0 && Companies.Count == 0;

        /// <summary>A row with more than its first page: the heading offers the rest.</summary>
        public bool CanSeeAll => Slider is not null && HasMore;
    }

    private readonly AsyncLoader<IReadOnlyList<DiscoverRow>> _discoverRows = new([]);

    /// <summary>
    /// The server's own discover layout, in its order, filled in one load so the page
    /// settles in one step rather than row by row. A server whose layout cannot be read
    /// gets the built-in default, which is what a fresh install has anyway.
    ///
    /// <para><b>Recently added</b> is what the server has seen arrive in Jellyfin, across
    /// libraries — drawn for whom the server's own page draws it (managers, and "view recently
    /// added"), and never thinned by the hide-available switch, which would empty it: every
    /// title in it is available. <b>Studios</b> and
    /// <b>networks</b> are the lists the server's web page compiles in
    /// (<see cref="DiscoverCompanies"/>) — the API does not serve them, so they cost no call.
    /// A <b>streaming-service</b> row is a custom row like any other, its region and services
    /// read from its data (<see cref="SeerrServer.StreamingQuery"/>). Recent requests are
    /// their own loader.</para>
    /// </summary>
    private void EnsureDiscoverRows()
    {
        if (_requestServer is not { } server) return;
        _discoverRows.Load(("rows", _requestGeneration, _dataGeneration), () =>
        {
            var layout = server.GetDiscoverLayoutAsync().GetAwaiter().GetResult();
            if (layout.Failure is { Kind: ApertureFailureKind.Unauthorized } refused)
            {
                // Not a layout problem: the session is gone, and that has to be said (and
                // the doors to Discover closed) rather than papered over with defaults.
                Application.InvokeOnMainThread(() => FlashRequestFailure(refused));
                return (IReadOnlyList<DiscoverRow>)[];
            }
            var sliders = layout.Ok && layout.Value.Count > 0 ? layout.Value : DefaultSliders;

            var rows = new List<DiscoverRow>();
            foreach (var slider in sliders.Where(s => s.Enabled))
            {
                var title = SliderTitle(slider);
                if (title is null) continue;
                switch (slider.Kind)
                {
                    case SliderKind.RecentRequests:
                        // Its "See all" is the Requests list, whatever this says of the first page.
                        rows.Add(new DiscoverRow("requests", title, slider.Kind, [], [], [], slider));
                        break;
                    case SliderKind.Watchlist:
                        var watchlist = FetchRequest(() => server.GetWatchlistAsync(), DiscoverPage.Empty, "Your watchlist");
                        rows.Add(new DiscoverRow("watchlist", title, slider.Kind, watchlist.Items, [], [],
                            slider, watchlist.HasMore));
                        break;
                    case SliderKind.MovieGenres or SliderKind.TvGenres:
                        var kind = slider.Kind == SliderKind.MovieGenres ? DiscoverKind.Movie : DiscoverKind.Tv;
                        rows.Add(new DiscoverRow("genres-" + slider.Id, title, slider.Kind, [],
                            FetchRequest(() => server.GetGenresAsync(kind), (IReadOnlyList<DiscoverGenre>)[], title), []));
                        break;
                    case SliderKind.Studios:
                        rows.Add(new DiscoverRow("studios-" + slider.Id, title, slider.Kind, [], [], DiscoverCompanies.Studios));
                        break;
                    case SliderKind.Networks:
                        rows.Add(new DiscoverRow("networks-" + slider.Id, title, slider.Kind, [], [], DiscoverCompanies.Networks));
                        break;
                    default:
                        var first = FetchRequest(() => server.GetSliderAsync(slider), DiscoverPage.Empty, title);
                        rows.Add(new DiscoverRow("slider-" + slider.Id, title, slider.Kind,
                            slider.Kind == SliderKind.RecentlyAdded ? first.Items : Shown(server, first.Items),
                            [], [], slider, first.HasMore));
                        break;
                }
            }
            return (IReadOnlyList<DiscoverRow>)rows;
        });
    }

    internal IReadOnlyList<DiscoverRow> DiscoverRows => _discoverRows.Value;

    private readonly AsyncLoader<IReadOnlyList<DiscoverGenre>> _genreList = new([]);

    /// <summary>Every genre of one kind, for the genres page — its own load, so the page
    /// stands on its own when the Discover rows have not been read (or have been re-keyed).</summary>
    private void EnsureGenreList(DiscoverKind kind)
    {
        if (_requestServer is not { } server) return;
        _genreList.Load(("genre-list", _requestGeneration, _dataGeneration, kind), () =>
            FetchRequest(() => server.GetGenresAsync(kind), (IReadOnlyList<DiscoverGenre>)[], "The genres"));
    }

    internal IReadOnlyList<DiscoverGenre> GenreList => _genreList.Value;
    internal bool GenreListLoading => _genreList.IsLoading;
    internal bool DiscoverRowsLoading => _discoverRows.IsLoading;

    /// <summary>The layout a fresh install has — what Discover shows when the server's
    /// own cannot be read.</summary>
    private static readonly IReadOnlyList<DiscoverSlider> DefaultSliders =
    [
        .. new[]
        {
            SliderKind.RecentlyAdded, SliderKind.RecentRequests, SliderKind.Watchlist, SliderKind.Trending,
            SliderKind.PopularMovies,
            SliderKind.MovieGenres, SliderKind.UpcomingMovies, SliderKind.Studios, SliderKind.PopularTv,
            SliderKind.TvGenres, SliderKind.UpcomingTv, SliderKind.Networks,
        }.Select((kind, i) => new DiscoverSlider(-(i + 1), kind, null, null, true, i)),
    ];

    /// <summary>A row's heading, or null for a row Aperture does not draw (see
    /// <see cref="EnsureDiscoverRows"/>). A custom row is called what its administrator
    /// called it.</summary>
    internal string? SliderTitle(DiscoverSlider slider) => slider.Kind switch
    {
        SliderKind.RecentlyAdded => _requestServer?.User is { } user
                                    && (user.CanManageRequests || user.Has(RequestPermission.RecentView))
            ? "Recently added" : null,
        SliderKind.RecentRequests => _requestServer?.User.CanManageRequests == true ? "Recent requests" : "Your requests",
        SliderKind.Watchlist => "Your watchlist",
        SliderKind.Trending => "Trending",
        SliderKind.PopularMovies => "Popular films",
        SliderKind.MovieGenres => "Films by genre",
        SliderKind.UpcomingMovies => "Coming to cinemas",
        SliderKind.PopularTv => "Popular series",
        SliderKind.TvGenres => "Series by genre",
        SliderKind.UpcomingTv => "Coming to television",
        SliderKind.Studios => "Studios",
        SliderKind.Networks => "Networks",
        _ when slider.Data is null => null,   // a custom row with nothing to ask for
        _ => slider.Title ?? slider.Kind switch
        {
            SliderKind.Search => $"“{slider.Data}”",
            SliderKind.MovieGenre or SliderKind.MovieKeyword or SliderKind.Studio or SliderKind.MovieStreaming => "Films",
            _ => "Series",
        },
    };

    // ── Recent requests, as a shelf ──────────────────────────────────────

    private readonly AsyncLoader<IReadOnlyList<MediaRequest>> _myRecentRequests = new([]);

    /// <summary>The newest requests: everyone's for someone who decides them — that row is
    /// their inbox — and one's own otherwise.</summary>
    private void EnsureMyRecentRequests()
    {
        if (_requestServer is not { } server) return;
        bool everyone = server.User.CanManageRequests;
        _myRecentRequests.Load(("mine", _requestGeneration, _dataGeneration), () =>
            FetchRequest(() => server.GetRequestsAsync(RequestFilter.All, mine: !everyone, 0, 12),
                RequestPage.Empty, "Recent requests").Items);
    }

    internal IReadOnlyList<MediaRequest> MyRecentRequests => _myRecentRequests.Value;

    // ── The watchlist, for Home ──────────────────────────────────────────

    private readonly AsyncLoader<DiscoverPage> _homeWatchlist = new(DiscoverPage.Empty);

    /// <summary>
    /// The watchlist as Home shows it: what can be played now first — a title on the list
    /// that has since arrived is the most useful thing the row can say — then the rest, each
    /// group in the server's own order. Re-read whenever anything a request changes (adding
    /// to the list bumps the same generation), and by F5.
    /// </summary>
    private void EnsureHomeWatchlist()
    {
        if (_requestServer is not { } server) return;
        _homeWatchlist.Load(("home-watchlist", _requestGeneration, _dataGeneration), () =>
        {
            var page = FetchRequest(() => server.GetWatchlistAsync(), DiscoverPage.Empty, "Your watchlist");
            return page with
            {
                Items = [.. page.Items.Where(i => i.InLibrary), .. page.Items.Where(i => !i.InLibrary)],
            };
        });
    }

    internal IReadOnlyList<DiscoverItem> HomeWatchlist => _requestServer is null ? [] : _homeWatchlist.Value.Items;

    /// <summary>More on the watchlist than its first page — the Home row's "See all".</summary>
    internal bool HomeWatchlistHasMore => _requestServer is not null && _homeWatchlist.Value.HasMore;

    // ── A genre, a studio or network, a person ───────────────────────────

    private readonly AsyncLoader<DiscoverPage> _wallPage = new(DiscoverPage.Empty);
    private readonly List<DiscoverItem> _wallItems = [];
    private string? _wallKey;

    private void EnsureGenrePage(DiscoverKind kind, int genreId, bool more = false) =>
        EnsureWallPage($"genre|{kind}|{genreId}",
            (server, page) => server.DiscoverGenreAsync(kind, genreId, page), "That genre", more);

    /// <summary>A row's titles in full. Keyed by what the row asks for, not only its id, so
    /// an administrator changing the row mid-visit starts it again. The watchlist is a
    /// record, as on its row, so the server's hide switches do not thin it.</summary>
    private void EnsureSliderPage(DiscoverSlider slider, bool more = false)
    {
        var source = $"slider|{slider.Id}|{slider.Kind}|{slider.Data}";
        if (slider.Kind == SliderKind.Watchlist)
            EnsureWallPage(source, (server, page) => server.GetWatchlistAsync(page), "Your watchlist", more, hide: false);
        else
            EnsureWallPage(source, (server, page) => server.GetSliderAsync(slider, page), "That row", more,
                // Everything recently added is available: the hide switch would empty it.
                hide: slider.Kind != SliderKind.RecentlyAdded);
    }

    private void EnsureCompanyPage(DiscoverKind kind, int companyId, bool more = false) =>
        EnsureWallPage($"company|{kind}|{companyId}",
            (server, page) => server.DiscoverCompanyAsync(kind, companyId, page),
            kind == DiscoverKind.Movie ? "That studio" : "That network", more);

    /// <summary>A genre's or a company's titles, a page at a time: asks for the next page
    /// when <paramref name="more"/> (the "Show more" button) and none is loading. One page
    /// on screen at a time, so one loader serves both.</summary>
    private void EnsureWallPage(string source, Func<IRequestServer, int, Task<ApertureResult<DiscoverPage>>> fetch,
        string what, bool more, bool hide = true)
    {
        if (_requestServer is not { } server) return;
        var key = $"{_requestGeneration}|{_dataGeneration}|{source}";
        if (_wallKey != key)
        {
            _wallKey = key;
            _wallItems.Clear();
        }
        else if (!more || _wallPage.IsLoading || !_wallPage.Value.HasMore)
        {
            return;
        }
        int page = _wallItems.Count == 0 ? 1 : _wallPage.Value.Page + 1;
        _wallPage.Load((key, page), () => FetchRequest(() => fetch(server, page), DiscoverPage.Empty, what), loaded =>
        {
            if (_wallKey != key) return;
            var seen = _wallItems.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
            _wallItems.AddRange((hide ? Shown(server, loaded.Items) : loaded.Items).Where(i => seen.Add(i.Key)));
        });
    }

    internal IReadOnlyList<DiscoverItem> WallItems => _wallItems;
    internal bool WallLoading => _wallPage.IsLoading;
    internal bool WallHasMore => _wallPage.Value.HasMore;

    private readonly AsyncLoader<PersonDetail?> _person = new(null);

    private void EnsurePerson(int tmdbId)
    {
        if (_requestServer is not { } server) return;
        _person.Load(("person", _requestGeneration, _dataGeneration, tmdbId), () =>
            FetchRequest<PersonDetail?>(async () =>
            {
                var result = await server.GetPersonAsync(tmdbId).ConfigureAwait(false);
                return result.Ok ? ApertureResult<PersonDetail?>.Success(result.Value) : result.As<PersonDetail?>();
            }, null, "That person"));
    }

    internal PersonDetail? Person => _person.Value;
    internal bool PersonLoading => _person.IsLoading;

    // ── One title ────────────────────────────────────────────────────────

    private readonly AsyncLoader<DiscoverDetail?> _discoverDetail = new(null);
    private readonly AsyncLoader<IReadOnlyList<DiscoverItem>> _recommendations = new([]);

    private void EnsureDiscoverDetail(DiscoverKind kind, int tmdbId, bool withRecommendations = true)
    {
        if (_requestServer is not { } server) return;
        _discoverDetail.Load(("title", _requestGeneration, _dataGeneration, _progressTick, kind, tmdbId), () =>
            FetchRequest<DiscoverDetail?>(async () =>
            {
                var result = await server.GetDetailAsync(kind, tmdbId).ConfigureAwait(false);
                return result.Ok ? ApertureResult<DiscoverDetail?>.Success(result.Value) : result.As<DiscoverDetail?>();
            }, null, "That title"));
        if (!withRecommendations) return;
        _recommendations.Load(("recs", _dataGeneration, kind, tmdbId), () =>
            Shown(server, FetchRequest(() => server.GetRecommendationsAsync(kind, tmdbId), DiscoverPage.Empty,
                "Recommendations").Items));
    }

    internal DiscoverDetail? DiscoverDetail => _discoverDetail.Value;
    internal bool DiscoverDetailLoading => _discoverDetail.IsLoading;
    internal IReadOnlyList<DiscoverItem> Recommendations => _recommendations.Value;

    // ── Search, beside the library's ─────────────────────────────────────

    private readonly AsyncLoader<IReadOnlyList<DiscoverItem>> _requestSearch = new([]);

    /// <summary>The request server's answer to the same search the library is answering —
    /// minus what the library already has, which the library's own results show.</summary>
    private void EnsureRequestSearch(string term)
    {
        if (_requestServer is not { } server || term.Length < 2) return;
        _requestSearch.Load(("request-search", _requestGeneration, term), () =>
            FetchRequest(() => server.SearchAsync(term), DiscoverPage.Empty, "The search").Items
                .Where(i => !i.InLibrary && Visible(server, i)).ToList());
    }

    internal IReadOnlyList<DiscoverItem> RequestSearchResults => _requestSearch.Value;
    internal bool RequestSearchLoading => _requestSearch.IsLoading;

    // ── The requests list, paged ─────────────────────────────────────────

    private readonly AsyncLoader<RequestPage> _requestPage = new(RequestPage.Empty);
    private readonly List<MediaRequest> _requestListItems = [];
    private string? _requestListKey;
    private int _requestListTotal;

    internal RequestFilter RequestListFilter { get; private set; } = RequestFilter.All;

    /// <summary>Only this user's requests. Always true for someone who cannot manage
    /// requests — the server would show them nothing else anyway.</summary>
    internal bool RequestListMine { get; private set; }

    internal const int RequestPageSize = 20;

    internal IReadOnlyList<MediaRequest> RequestList => _requestListItems;
    internal int RequestListTotal => _requestListTotal;
    internal bool RequestListLoading => _requestPage.IsLoading;
    internal bool RequestListHasMore => _requestListItems.Count < _requestListTotal;

    /// <summary>Asks for the first page of the current list, or the next one when
    /// <paramref name="more"/> — the "show more" button at the foot of the list.</summary>
    private void EnsureRequestList(bool more = false)
    {
        if (_requestServer is not { } server) return;
        bool mine = RequestListMine || !server.User.CanManageRequests;
        var filter = RequestListFilter;
        var key = $"{_requestGeneration}|{_dataGeneration}|{filter}|{mine}";
        if (_requestListKey != key)
        {
            _requestListKey = key;
            _requestListItems.Clear();
            _requestListTotal = 0;
        }
        else if (!more || !RequestListHasMore)
        {
            if (_requestListItems.Count > 0 || _requestPage.IsLoading) return;
        }

        int skip = _requestListItems.Count;
        _requestPage.Load((key, skip), () => FetchRequest(
            () => server.GetRequestsAsync(filter, mine, skip, RequestPageSize), RequestPage.Empty, "The requests"),
            page =>
            {
                if (_requestListKey != key || page.Skip != _requestListItems.Count) return;
                _requestListItems.AddRange(page.Items);
                _requestListTotal = page.Total;
            });
    }

    internal void ShowMoreRequests()
    {
        EnsureRequestList(more: true);
        Rerender();
    }

    internal void SetRequestListFilter(RequestFilter filter)
    {
        if (RequestListFilter == filter) return;
        RequestListFilter = filter;
        Rerender();
    }

    internal void SetRequestListMine(bool mine)
    {
        if (RequestListMine == mine) return;
        RequestListMine = mine;
        Rerender();
    }

    // ── What the administrator asked to hide ─────────────────────────────

    /// <summary>
    /// The server's own hide switches, applied the way its web client applies them — the
    /// server itself sends everything. Available (and partly available) titles go when
    /// <see cref="RequestServerInfo.HideAvailable"/> is on; blocklisted ones only for
    /// someone who manages the blocklist, because nobody else is shown them at all.
    /// "Your requests" is never filtered: it is a record, not a shelf.
    /// </summary>
    internal static bool Visible(IRequestServer server, DiscoverItem item)
    {
        if (server.Server.HideAvailable
            && item.Availability is Availability.Available or Availability.PartiallyAvailable)
            return false;
        if (server.Server.HideBlocklisted && item.Availability == Availability.Blocklisted
            && server.User.Has(RequestPermission.ManageBlocklist))
            return false;
        return true;
    }

    private static IReadOnlyList<DiscoverItem> Shown(IRequestServer server, IReadOnlyList<DiscoverItem> items) =>
        items.Where(i => Visible(server, i)).ToList();

    // ── The allowance ────────────────────────────────────────────────────

    private readonly AsyncLoader<RequestQuotas?> _quota = new(null);

    /// <summary>What this user may still request — read for the title page, where the
    /// Request button is, and re-read after every request, which spends it.</summary>
    private void EnsureQuota()
    {
        if (_requestServer is not { } server) return;
        _quota.Load(("quota", _requestGeneration, _dataGeneration), () =>
            FetchRequest<RequestQuotas?>(async () =>
            {
                var result = await server.GetQuotaAsync().ConfigureAwait(false);
                return result.Ok ? ApertureResult<RequestQuotas?>.Success(result.Value) : result.As<RequestQuotas?>();
            }, null, "Your request allowance"));
    }

    /// <summary>The allowance for one kind, or null while unknown or when unlimited.</summary>
    internal RequestQuota? QuotaFor(DiscoverKind kind) =>
        _quota.Value is { } q && (kind == DiscoverKind.Movie ? q.Movie : q.Tv) is { IsLimited: true } limited
            ? limited
            : null;

    // ── Downloads, while they run ────────────────────────────────────────

    /// <summary>Bumped by the progress timer; part of the title page's key, so a tick re-reads
    /// the title and with it how far its downloads have got.</summary>
    private int _progressTick;
    private System.Threading.Timer? _progressTimer;

    /// <summary>Whether the page on screen is being kept up to date with its downloads.</summary>
    internal bool RefreshingProgress => _progressTimer is not null;

    /// <summary>How often a title page with something downloading re-reads it. The server
    /// relays the downloader's queue, which Radarr and Sonarr refresh about this often.</summary>
    internal static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(15);

    /// <summary>Keeps the progress timer running while the page on screen has downloads, and
    /// only then. Called from the title page's build; the tick checks again, so leaving the
    /// page stops it within one interval.</summary>
    private void KeepProgressFresh(bool downloading)
    {
        if (downloading && _progressTimer is null)
            _progressTimer = new System.Threading.Timer(_ => Application.InvokeOnMainThread(ProgressTick), null,
                ProgressInterval, ProgressInterval);
        else if (!downloading)
            StopProgressTimer();
    }

    private void ProgressTick()
    {
        if (_requestServer is null || Current.View != ApertureView.DiscoverTitle || _playingItem is not null)
        {
            // Nothing to watch — or a film playing, which is not the moment for the title
            // page underneath to be re-reading itself.
            if (_requestServer is null || Current.View != ApertureView.DiscoverTitle) StopProgressTimer();
            return;
        }
        _progressTick++;
        Rerender();
    }

    private void StopProgressTimer()
    {
        _progressTimer?.Dispose();
        _progressTimer = null;
    }

    // ── The badge ────────────────────────────────────────────────────────

    private readonly AsyncLoader<int> _pendingCount = new(0);

    /// <summary>Requests waiting for approval — only asked for someone who can approve.</summary>
    private void EnsurePendingCount()
    {
        if (_requestServer is not { } server || !server.User.CanManageRequests) return;
        _pendingCount.Load(("pending", _requestGeneration, _dataGeneration), () =>
            FetchRequest(() => server.GetPendingCountAsync(), 0, "The pending requests"));
    }

    internal int PendingRequestCount => _requestServer is null ? 0 : _pendingCount.Value;

    // ── The verbs ────────────────────────────────────────────────────────

    /// <summary>Makes a request, says what became of it, and re-reads everything a request
    /// changes — the title's state, the shelves' badges, the lists.</summary>
    internal void MakeRequest(NewRequest request, string title)
    {
        if (_requestServer is not { } server) return;
        _ = ApplyRequestAsync(() => server.RequestAsync(request), made =>
            made.Status == RequestStatus.Approved
                ? $"Requested {title} — approved, on its way"
                : $"Requested {title} — waiting for approval");
    }

    internal void DecideRequest(MediaRequest request, bool approve)
    {
        if (_requestServer is not { } server) return;
        var name = request.Title ?? "the request";
        _ = ApplyRequestAsync(() => server.DecideAsync(request.Id, approve),
            _ => approve ? $"Approved {name}" : $"Declined {name}");
    }

    internal void WithdrawRequest(MediaRequest request)
    {
        if (_requestServer is not { } server) return;
        var name = request.Title ?? "the request";
        _ = ApplyRequestAsync(() => server.DeleteRequestAsync(request.Id), _ => $"Withdrew {name}");
    }

    internal void SetWatchlist(DiscoverItem item, bool on)
    {
        if (_requestServer is not { } server) return;
        _ = ApplyRequestAsync(() => server.SetWatchlistAsync(item, on),
            _ => on ? $"Added {item.Title} to your watchlist" : $"Took {item.Title} off your watchlist");
    }

    private async Task ApplyRequestAsync<T>(Func<Task<ApertureResult<T>>> call, Func<T, string> done)
    {
        var result = await call().ConfigureAwait(false);
        Application.InvokeOnMainThread(() =>
        {
            if (!result.Ok)
            {
                FlashRequestFailure(result.Failure!);
                return;
            }
            Flash(done(result.Value), ToastSeverity.Success);
            _requestGeneration++;
            _seasonPickerFor = null;
            _optionsFor = null;
            Rerender();
        });
    }

    // ── Running a call ───────────────────────────────────────────────────

    /// <summary>
    /// <see cref="Fetch{T}"/> for the request server. The difference is what a failure
    /// does: a request server that stops answering is not the media server going away, so
    /// it never signs anyone out of Jellyfin — a rejected session just says to reconnect.
    /// </summary>
    private T FetchRequest<T>(Func<Task<ApertureResult<T>>> call, T fallback, string what)
    {
        var result = call().GetAwaiter().GetResult();
        if (result.Ok) return result.Value;
        var failure = result.Failure!;
        Application.InvokeOnMainThread(() => FlashRequestFailure(failure, what));
        return fallback;
    }

    private void FlashRequestFailure(ApertureFailure failure, string what = "That")
    {
        var host = _requestServer?.Server.Host ?? "the request server";
        var message = failure.Kind switch
        {
            ApertureFailureKind.Unauthorized => $"{RequestServerTitle} wants you to sign in again — see Settings",
            ApertureFailureKind.Forbidden when failure.Message is { Length: > 0 } said => said,
            ApertureFailureKind.NotFound => $"{what} is not on {host}",
            ApertureFailureKind.NotJellyfin => $"{host} answered, but not like {RequestServerTitle}",
            _ => failure.UserMessage(host, what),
        };
        Flash(message, ToastSeverity.Error);
        if (failure.Kind == ApertureFailureKind.Unauthorized)
        {
            // The session is gone; keep the account so Settings can offer the sign-in again.
            CloseRequestServer();
            _requestConnectFailure = failure;
            Rerender();
        }
    }
}

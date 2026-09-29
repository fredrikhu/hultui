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
        _requestServer?.Dispose();
        _requestServer = null;
        _requestGeneration++;
        _requestListItems.Clear();
        _requestListKey = null;
        // Off any view that needs it — their rail buttons have just gone, and a page with
        // no way back to it is a dead end.
        if (_stack.RemoveAll(r => r.View is ApertureView.Discover or ApertureView.DiscoverTitle or ApertureView.Requests) > 0
            && _stack.Count == 0)
            _stack.Add(new Route(ApertureView.Home));
    }

    // ── Discovery shelves ────────────────────────────────────────────────

    private readonly AsyncLoader<IReadOnlyDictionary<DiscoverShelf, IReadOnlyList<DiscoverItem>>> _shelves =
        new(new Dictionary<DiscoverShelf, IReadOnlyList<DiscoverItem>>());

    /// <summary>The shelves in the order Discover draws them.</summary>
    internal static readonly DiscoverShelf[] ShelfOrder =
    [
        DiscoverShelf.Trending,
        DiscoverShelf.PopularMovies,
        DiscoverShelf.UpcomingMovies,
        DiscoverShelf.PopularSeries,
        DiscoverShelf.UpcomingSeries,
    ];

    internal static string ShelfTitle(DiscoverShelf shelf) => shelf switch
    {
        DiscoverShelf.Trending => "Trending",
        DiscoverShelf.PopularMovies => "Popular films",
        DiscoverShelf.UpcomingMovies => "Coming to cinemas",
        DiscoverShelf.PopularSeries => "Popular series",
        DiscoverShelf.UpcomingSeries => "Coming to television",
        _ => shelf.ToString(),
    };

    /// <summary>One load for every shelf, so Discover settles in one step rather than
    /// flickering in row by row — the same arrangement as home's latest rows.</summary>
    private void EnsureShelves()
    {
        if (_requestServer is not { } server) return;
        _shelves.Load(("shelves", _requestGeneration, _dataGeneration), () =>
        {
            var map = new Dictionary<DiscoverShelf, IReadOnlyList<DiscoverItem>>();
            foreach (var shelf in ShelfOrder)
                map[shelf] = FetchRequest(() => server.DiscoverAsync(shelf), DiscoverPage.Empty, ShelfTitle(shelf)).Items;
            return (IReadOnlyDictionary<DiscoverShelf, IReadOnlyList<DiscoverItem>>)map;
        });
    }

    internal IReadOnlyList<DiscoverItem> Shelf(DiscoverShelf shelf) =>
        _shelves.Value.TryGetValue(shelf, out var items) ? items : [];

    internal bool ShelvesLoading => _shelves.IsLoading;

    // ── Your own requests, as a shelf ────────────────────────────────────

    private readonly AsyncLoader<IReadOnlyList<MediaRequest>> _myRecentRequests = new([]);

    private void EnsureMyRecentRequests()
    {
        if (_requestServer is not { } server) return;
        _myRecentRequests.Load(("mine", _requestGeneration, _dataGeneration), () =>
            FetchRequest(() => server.GetRequestsAsync(RequestFilter.All, mine: true, 0, 12),
                RequestPage.Empty, "Your requests").Items);
    }

    internal IReadOnlyList<MediaRequest> MyRecentRequests => _myRecentRequests.Value;

    // ── One title ────────────────────────────────────────────────────────

    private readonly AsyncLoader<DiscoverDetail?> _discoverDetail = new(null);
    private readonly AsyncLoader<IReadOnlyList<DiscoverItem>> _recommendations = new([]);

    private void EnsureDiscoverDetail(DiscoverKind kind, int tmdbId)
    {
        if (_requestServer is not { } server) return;
        _discoverDetail.Load(("title", _requestGeneration, _dataGeneration, kind, tmdbId), () =>
            FetchRequest<DiscoverDetail?>(async () =>
            {
                var result = await server.GetDetailAsync(kind, tmdbId).ConfigureAwait(false);
                return result.Ok ? ApertureResult<DiscoverDetail?>.Success(result.Value) : result.As<DiscoverDetail?>();
            }, null, "That title"));
        _recommendations.Load(("recs", _dataGeneration, kind, tmdbId), () =>
            FetchRequest(() => server.GetRecommendationsAsync(kind, tmdbId), DiscoverPage.Empty,
                "Recommendations").Items);
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
                .Where(i => !i.InLibrary).ToList());
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

namespace Aperture.Core.Requests;

/// <summary>
/// Everything Aperture asks of a request server — Jellyseerr 2.x or Seerr 3.x (its successor), which
/// share one API — in the app's own vocabulary. One implementation talks HTTP
/// (<see cref="SeerrServer"/>); another is the fixture the headless tests drive
/// (<c>Aperture.Core.Testing.FakeRequestServer</c>).
///
/// <para>The same contract as <see cref="IMediaLibrary"/>: every call returns an
/// <see cref="ApertureResult{T}"/> rather than throwing, may be made from any thread, and
/// touches nothing in the UI.</para>
/// </summary>
public interface IRequestServer : IDisposable
{
    RequestServerInfo Server { get; }

    /// <summary>Who requests are made as, and what they are allowed.</summary>
    RequestUser User { get; }

    /// <summary>One page of a discovery shelf.</summary>
    Task<ApertureResult<DiscoverPage>> DiscoverAsync(DiscoverShelf shelf, int page = 1, CancellationToken ct = default);

    /// <summary>Films and series matching a search, people left out.</summary>
    Task<ApertureResult<DiscoverPage>> SearchAsync(string query, int page = 1, CancellationToken ct = default);

    /// <summary>One title in full, with its seasons, cast and the requests already made.</summary>
    Task<ApertureResult<DiscoverDetail>> GetDetailAsync(DiscoverKind kind, int tmdbId, CancellationToken ct = default);

    /// <summary>What TMDB recommends after this one.</summary>
    Task<ApertureResult<DiscoverPage>> GetRecommendationsAsync(DiscoverKind kind, int tmdbId, int page = 1,
        CancellationToken ct = default);

    /// <summary>
    /// A page of requests, newest first. <paramref name="mine"/> limits it to this user's
    /// own — which is all a user without <see cref="RequestPermission.ManageRequests"/> is
    /// shown anyway, whatever is asked.
    /// </summary>
    Task<ApertureResult<RequestPage>> GetRequestsAsync(RequestFilter filter, bool mine, int skip = 0, int take = 20,
        CancellationToken ct = default);

    /// <summary>How many requests are waiting for approval — the badge on the rail. Only
    /// meaningful for someone who can approve them.</summary>
    Task<ApertureResult<int>> GetPendingCountAsync(CancellationToken ct = default);

    /// <summary>What this user may still request.</summary>
    Task<ApertureResult<RequestQuotas>> GetQuotaAsync(CancellationToken ct = default);

    /// <summary>Makes a request. Comes back with the request as the server recorded it —
    /// already <see cref="RequestStatus.Approved"/> for a user who auto-approves.</summary>
    Task<ApertureResult<MediaRequest>> RequestAsync(NewRequest request, CancellationToken ct = default);

    /// <summary>Approves or declines someone's request.</summary>
    Task<ApertureResult<MediaRequest>> DecideAsync(int requestId, bool approve, CancellationToken ct = default);

    /// <summary>Withdraws a request — your own while it is pending, anyone's for a manager.</summary>
    Task<ApertureResult<bool>> DeleteRequestAsync(int requestId, CancellationToken ct = default);
}

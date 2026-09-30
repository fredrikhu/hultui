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

    // ── Slice 7b ─────────────────────────────────────────────────────────

    /// <summary>The administrator's discover layout, in its own order, disabled rows
    /// included (the caller decides what to draw).</summary>
    Task<ApertureResult<IReadOnlyList<DiscoverSlider>>> GetDiscoverLayoutAsync(CancellationToken ct = default);

    /// <summary>One page of a custom row of the layout — a genre, keywords, a search, a
    /// studio or a network. Built-in rows go through <see cref="DiscoverAsync"/>.</summary>
    Task<ApertureResult<DiscoverPage>> GetSliderAsync(DiscoverSlider slider, int page = 1, CancellationToken ct = default);

    /// <summary>The genres to browse films or series by, each with a backdrop.</summary>
    Task<ApertureResult<IReadOnlyList<DiscoverGenre>>> GetGenresAsync(DiscoverKind kind, CancellationToken ct = default);

    /// <summary>One page of a genre, most popular first.</summary>
    Task<ApertureResult<DiscoverPage>> DiscoverGenreAsync(DiscoverKind kind, int genreId, int page = 1,
        CancellationToken ct = default);

    /// <summary>One page of a studio's films (<see cref="DiscoverKind.Movie"/>) or a
    /// network's series (<see cref="DiscoverKind.Tv"/>), most popular first.</summary>
    Task<ApertureResult<DiscoverPage>> DiscoverCompanyAsync(DiscoverKind kind, int companyId, int page = 1,
        CancellationToken ct = default);

    /// <summary>A person and their films and series.</summary>
    Task<ApertureResult<PersonDetail>> GetPersonAsync(int tmdbId, CancellationToken ct = default);

    /// <summary>This user's watchlist, one page, with titles and artwork filled in.</summary>
    Task<ApertureResult<DiscoverPage>> GetWatchlistAsync(int page = 1, CancellationToken ct = default);

    /// <summary>Puts a title on this user's watchlist, or takes it off.</summary>
    Task<ApertureResult<bool>> SetWatchlistAsync(DiscoverItem item, bool on, CancellationToken ct = default);

    /// <summary>Reports a problem with a title. Returns the new issue's id.</summary>
    Task<ApertureResult<int>> ReportIssueAsync(NewIssue issue, CancellationToken ct = default);

    /// <summary>A page of reports, newest first, titles filled in — everyone's for someone
    /// who may see them (<see cref="RequestPermission.ManageIssues"/> or
    /// <see cref="RequestPermission.ViewIssues"/>), one's own otherwise.</summary>
    Task<ApertureResult<IssuePage>> GetIssuesAsync(IssueFilter filter, int skip = 0, int take = 20,
        CancellationToken ct = default);

    /// <summary>One report with its whole conversation.</summary>
    Task<ApertureResult<MediaIssue>> GetIssueAsync(int issueId, CancellationToken ct = default);

    /// <summary>Adds to a report's conversation.</summary>
    Task<ApertureResult<MediaIssue>> CommentOnIssueAsync(int issueId, string message, CancellationToken ct = default);

    /// <summary>Marks a report resolved, or opens it again.</summary>
    Task<ApertureResult<MediaIssue>> SetIssueStatusAsync(int issueId, IssueStatus status, CancellationToken ct = default);

    /// <summary>How many reports are open and resolved — the rail's badge.</summary>
    Task<ApertureResult<IssueCounts>> GetIssueCountsAsync(CancellationToken ct = default);

    /// <summary>The Radarr (films) or Sonarr (series) servers requests can go to.</summary>
    Task<ApertureResult<IReadOnlyList<RequestService>>> GetServicesAsync(DiscoverKind kind, CancellationToken ct = default);

    /// <summary>One server's quality profiles and root folders — asked of the downloader
    /// live, so it can be slow when that downloader is.</summary>
    Task<ApertureResult<RequestServiceDetails>> GetServiceDetailsAsync(DiscoverKind kind, int serviceId,
        CancellationToken ct = default);
}

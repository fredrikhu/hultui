namespace Aperture.Core.Requests;

/// <summary>What a request server said about itself before anyone signed in.</summary>
/// <param name="Address">The base address that answered, normalized — scheme, host, port,
/// no trailing slash, no <c>/api/v1</c>.</param>
/// <param name="Title">The name the server gives itself — "Jellyseerr" or "Seerr" unless an
/// administrator renamed it — which is what the UI calls it, so the button says what the
/// household calls the thing.</param>
/// <param name="JellyfinBacked">The server's media server is Jellyfin (not Plex or Emby),
/// which is what makes a Jellyfin sign-in possible at all.</param>
/// <param name="CacheImages">The server proxies and caches TMDB artwork itself. Its own web
/// page then loads posters through it, and so does Aperture — the administrator switched it
/// on so that browsers on this network do not talk to TMDB.</param>
public sealed record RequestServerInfo(
    string Address,
    string Title,
    string Version,
    bool Movie4kEnabled = false,
    bool Series4kEnabled = false,
    bool PartialRequestsEnabled = true,
    bool JellyfinBacked = true,
    bool CacheImages = false)
{
    public string Host => Uri.TryCreate(Address, UriKind.Absolute, out var u) ? u.Authority : Address;

    /// <summary>The server's version as a number, or 0.0 when it is not one.</summary>
    public Version ParsedVersion =>
        System.Version.TryParse(Version.TrimStart('v', 'V').Split('-', '+')[0], out var v) ? v : new Version(0, 0);

    /// <summary>
    /// Seerr 3.4 signs a Jellyfin user in through Jellyfin's Quick Connect, which Aperture —
    /// already holding a Jellyfin session — can approve by itself. That is the door that
    /// needs no password and no administrator's key.
    /// </summary>
    public bool SupportsQuickConnect => JellyfinBacked && ParsedVersion >= new Version(3, 4);

    /// <summary>Where TMDB artwork comes from: the server's own proxy when it caches
    /// images, TMDB otherwise.</summary>
    public string ImageBase => CacheImages ? Address + "/imageproxy/tmdb/t/p/" : RequestImages.TmdbImageBase;
}

/// <summary>Who requests are made as.</summary>
public sealed record RequestUser(int Id, string DisplayName, RequestPermission Permissions)
{
    public bool Has(RequestPermission permission) => Permissions.Allows(permission);

    /// <summary>May approve, decline and delete anyone's request.</summary>
    public bool CanManageRequests => Has(RequestPermission.ManageRequests);

    public bool CanRequest(DiscoverKind kind) => kind == DiscoverKind.Movie
        ? Has(RequestPermission.Request) || Has(RequestPermission.RequestMovie)
        : Has(RequestPermission.Request) || Has(RequestPermission.RequestTv);

    public bool CanRequest4k(DiscoverKind kind) => kind == DiscoverKind.Movie
        ? Has(RequestPermission.Request4k) || Has(RequestPermission.Request4kMovie)
        : Has(RequestPermission.Request4k) || Has(RequestPermission.Request4kTv);

    /// <summary>A request from this user is approved the moment it is made.</summary>
    public bool AutoApproves(DiscoverKind kind) => kind == DiscoverKind.Movie
        ? Has(RequestPermission.AutoApprove) || Has(RequestPermission.AutoApproveMovie)
        : Has(RequestPermission.AutoApprove) || Has(RequestPermission.AutoApproveTv);
}

/// <summary>
/// The request server's permission bits, as its own <c>permissions.ts</c> numbers them.
/// <see cref="Admin"/> implies every other bit — the server checks it that way, so
/// <see cref="PermissionExtensions.Allows"/> does too.
/// </summary>
[Flags]
public enum RequestPermission
{
    None = 0,
    Admin = 2,
    ManageSettings = 4,
    ManageUsers = 8,
    ManageRequests = 16,
    Request = 32,
    Vote = 64,
    AutoApprove = 128,
    AutoApproveMovie = 256,
    AutoApproveTv = 512,
    Request4k = 1024,
    Request4kMovie = 2048,
    Request4kTv = 4096,
    RequestAdvanced = 8192,
    RequestView = 16384,
    AutoApprove4k = 32768,
    AutoApprove4kMovie = 65536,
    AutoApprove4kTv = 131072,
    RequestMovie = 262144,
    RequestTv = 524288,
    ManageIssues = 1048576,
    ViewIssues = 2097152,
    CreateIssues = 4194304,
    AutoRequest = 8388608,
    AutoRequestMovie = 16777216,
    AutoRequestTv = 33554432,
    RecentView = 67108864,
    WatchlistView = 134217728,
    /// <summary>"Blacklist" before Seerr 3.0; same bit.</summary>
    ManageBlocklist = 268435456,
    ViewBlocklist = 1073741824,
}

public static class PermissionExtensions
{
    public static bool Allows(this RequestPermission held, RequestPermission wanted) =>
        (held & RequestPermission.Admin) != 0 || (held & wanted) == wanted;
}

/// <summary>Films and series — the two things a request server can fetch.</summary>
public enum DiscoverKind { Movie, Tv }

/// <summary>
/// Where a title stands with the request server, in the order it moves through them. The
/// numbers are Jellyseerr's and Seerr's own <c>MediaStatus</c>, so a value read off the
/// wire casts straight across; anything newer than this list reads as <see cref="Unknown"/>.
/// (Overseerr numbered deleted as 6 — the published spec still says so, and is wrong for
/// every server Aperture talks to.)
/// </summary>
public enum Availability
{
    /// <summary>Nobody has asked for it and the library does not have it. Also what a
    /// title the server has no record of at all reads as.</summary>
    Unknown = 1,
    /// <summary>Asked for, waiting for someone to approve it.</summary>
    Pending = 2,
    /// <summary>Approved and handed to Radarr/Sonarr: on its way.</summary>
    Processing = 3,
    /// <summary>Some seasons are in the library, some are not.</summary>
    PartiallyAvailable = 4,
    /// <summary>In the library.</summary>
    Available = 5,
    /// <summary>An administrator has ruled it out; it cannot be requested. ("Blacklisted"
    /// before Seerr 3.0; same number.)</summary>
    Blocklisted = 6,
    /// <summary>Was in the library, has been removed from it.</summary>
    Deleted = 7,
}

/// <summary>Where one request stands. The server's <c>MediaRequestStatus</c> numbers.</summary>
public enum RequestStatus
{
    Pending = 1,
    Approved = 2,
    Declined = 3,
    Failed = 4,
    Completed = 5,
}

/// <summary>
/// One title as the request server knows it — a search hit, a card on a discovery shelf.
/// Keyed by TMDB id and kind: a film and a series can share a TMDB number.
/// </summary>
public sealed record DiscoverItem
{
    public required int TmdbId { get; init; }
    public required DiscoverKind Kind { get; init; }
    public required string Title { get; init; }
    public int? Year { get; init; }
    public string? Overview { get; init; }
    /// <summary>TMDB image paths (<c>/abc.jpg</c>); <see cref="RequestImages"/> turns them into URLs.</summary>
    public string? PosterPath { get; init; }
    public string? BackdropPath { get; init; }
    /// <summary>TMDB's 0–10 score, or null when too few people have voted for it to mean anything.</summary>
    public float? VoteAverage { get; init; }
    public Availability Availability { get; init; } = Availability.Unknown;
    public Availability Availability4k { get; init; } = Availability.Unknown;
    /// <summary>The Jellyfin item id once the title is in the library — which is how a
    /// discovery card opens the library's own page rather than a request form.</summary>
    public string? JellyfinItemId { get; init; }

    /// <summary>A stable key for view state: <c>movie:603</c>, <c>tv:1399</c>.</summary>
    public string Key => $"{(Kind == DiscoverKind.Movie ? "movie" : "tv")}:{TmdbId}";

    public bool InLibrary => (Availability is Availability.Available or Availability.PartiallyAvailable)
                             && JellyfinItemId is { Length: > 0 };

    /// <summary>Parses <see cref="Key"/> back. False for anything else.</summary>
    public static bool TryParseKey(string? key, out DiscoverKind kind, out int tmdbId)
    {
        kind = DiscoverKind.Movie;
        tmdbId = 0;
        if (key is null) return false;
        int colon = key.IndexOf(':');
        if (colon <= 0) return false;
        var prefix = key[..colon];
        if (prefix == "movie") kind = DiscoverKind.Movie;
        else if (prefix == "tv") kind = DiscoverKind.Tv;
        else return false;
        return int.TryParse(key.AsSpan(colon + 1), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out tmdbId) && tmdbId > 0;
    }
}

/// <summary>A page of discovery results, numbered the way TMDB numbers them (from 1).</summary>
public sealed record DiscoverPage(IReadOnlyList<DiscoverItem> Items, int Page, int TotalPages, int TotalResults)
{
    public static readonly DiscoverPage Empty = new([], 1, 0, 0);
    public bool HasMore => Page < TotalPages;
}

/// <summary>The discovery shelves the server offers.</summary>
public enum DiscoverShelf
{
    Trending,
    PopularMovies,
    UpcomingMovies,
    PopularSeries,
    UpcomingSeries,
}

/// <summary>One season of a series, and where it stands.</summary>
public sealed record DiscoverSeason(int Number, string Name, int EpisodeCount, int? Year,
    Availability Availability, Availability Availability4k);

/// <summary>Someone in a title's credits, with TMDB's picture of them.</summary>
public sealed record DiscoverPerson(int TmdbId, string Name, string? Role, string? ProfilePath);

/// <summary>
/// One title in full: the discovery detail page. Carries the requests already made for it,
/// which is how the page knows to say "requested by Anna" rather than offering a second
/// request for the same thing.
/// </summary>
public sealed record DiscoverDetail
{
    public required DiscoverItem Item { get; init; }
    public string? Tagline { get; init; }
    public TimeSpan? Runtime { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    /// <summary>"Released", "Returning Series", "In Production"…</summary>
    public string? Status { get; init; }
    public DateOnly? ReleaseDate { get; init; }
    /// <summary>The TVDB id Sonarr needs; only a series has one.</summary>
    public int? TvdbId { get; init; }
    /// <summary>Seasons in order; season 0 (specials) is left out, as the server's own
    /// request form leaves it out.</summary>
    public IReadOnlyList<DiscoverSeason> Seasons { get; init; } = [];
    public IReadOnlyList<DiscoverPerson> Cast { get; init; } = [];
    public IReadOnlyList<MediaRequest> Requests { get; init; } = [];
}

/// <summary>One request, as the requests list and a detail page show it.</summary>
public sealed record MediaRequest
{
    public required int Id { get; init; }
    public required RequestStatus Status { get; init; }
    public required DiscoverKind Kind { get; init; }
    public required int TmdbId { get; init; }
    /// <summary>The title, when the list call carried it. The request list only carries
    /// ids — the server's own page fetches each title separately — so this is filled in
    /// by <see cref="IRequestServer.GetRequestsAsync"/> from the detail calls.</summary>
    public string? Title { get; init; }
    public int? Year { get; init; }
    public string? PosterPath { get; init; }
    /// <summary>Where the title stands now — a request approved last week whose film has
    /// since arrived reads as available, which is what the person asking cares about.</summary>
    public Availability Availability { get; init; } = Availability.Unknown;
    public bool Is4k { get; init; }
    /// <summary>The seasons asked for; empty for a film.</summary>
    public IReadOnlyList<int> Seasons { get; init; } = [];
    public int RequestedById { get; init; }
    public string RequestedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public string? JellyfinItemId { get; init; }

    public string MediaKey => $"{(Kind == DiscoverKind.Movie ? "movie" : "tv")}:{TmdbId}";
}

/// <summary>A page of requests, newest first.</summary>
public sealed record RequestPage(IReadOnlyList<MediaRequest> Items, int Total, int Skip)
{
    public static readonly RequestPage Empty = new([], 0, 0);
    public bool HasMore => Skip + Items.Count < Total;
}

/// <summary>Which requests to list — the server's own filters, and only those: its request
/// validator answers any other value with a 400. (There is no "declined" filter.)</summary>
public enum RequestFilter { All, Pending, Approved, Processing, Available, Unavailable, Failed, Completed }

/// <summary>What a request asks for.</summary>
/// <param name="Seasons">The seasons, for a series; empty for a film.</param>
public sealed record NewRequest(DiscoverKind Kind, int TmdbId, IReadOnlyList<int> Seasons, bool Is4k = false,
    int? TvdbId = null);

/// <summary>How many of one kind this user may still request, when the server limits it.</summary>
/// <param name="Limit">Requests allowed per <paramref name="Days"/>; null when unlimited.</param>
public sealed record RequestQuota(int? Limit, int Days, int Used, int? Remaining, bool Restricted);

/// <summary>A user's allowance for both kinds.</summary>
public sealed record RequestQuotas(RequestQuota Movie, RequestQuota Tv);

/// <summary>
/// Where TMDB artwork is fetched from: TMDB's image host, or the request server's own proxy
/// when it caches images (<see cref="RequestServerInfo.ImageBase"/>) — the same choice its
/// web page makes. No key either way.
/// </summary>
public static class RequestImages
{
    public const string TmdbImageBase = "https://image.tmdb.org/t/p/";

    /// <summary>A poster at a width TMDB actually serves (it offers fixed sizes only).</summary>
    public static Uri? Poster(string? path, int width, string? imageBase = null) => Image(path, width switch
    {
        <= 92 => "w92",
        <= 154 => "w154",
        <= 185 => "w185",
        <= 342 => "w342",
        <= 500 => "w500",
        _ => "w780",
    }, imageBase);

    public static Uri? Backdrop(string? path, int width, string? imageBase = null) => Image(path, width switch
    {
        <= 300 => "w300",
        <= 780 => "w780",
        // Never "original": a TMDB original backdrop is a 4K JPEG, 33 MB decoded, for a
        // hero that is at most a window wide.
        _ => "w1280",
    }, imageBase);

    public static Uri? Profile(string? path, int width, string? imageBase = null) => Image(path, width switch
    {
        <= 45 => "w45",
        <= 185 => "w185",
        _ => "h632",
    }, imageBase);

    /// <summary>Only a TMDB-shaped path (<c>/abc.jpg</c>) becomes a URL — a path is joined
    /// onto a base, so anything with a scheme, a <c>..</c> or a query in it is refused.</summary>
    private static Uri? Image(string? path, string size, string? imageBase)
    {
        if (path is not { Length: > 1 } || path[0] != '/' || path.Contains("..", StringComparison.Ordinal)
            || path.IndexOfAny(['?', '#', '\\', ':']) >= 0)
            return null;
        return Uri.TryCreate((imageBase ?? TmdbImageBase) + size + path, UriKind.Absolute, out var uri) ? uri : null;
    }
}

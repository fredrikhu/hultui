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
/// <param name="HideAvailable">The administrator asked for titles the library has to be left
/// off the discovery shelves. The server does not filter them itself — its own web client
/// does — so a native client has to as well.</param>
/// <param name="HideBlocklisted">The same for blocklisted titles; the web client applies it
/// only for someone who manages the blocklist (anyone else is never shown them).</param>
public sealed record RequestServerInfo(
    string Address,
    string Title,
    string Version,
    bool Movie4kEnabled = false,
    bool Series4kEnabled = false,
    bool PartialRequestsEnabled = true,
    bool JellyfinBacked = true,
    bool CacheImages = false,
    bool HideAvailable = false,
    bool HideBlocklisted = false)
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

/// <summary>
/// One download Radarr or Sonarr has queued for a title — a film, or an episode of a series
/// (<see cref="Season"/>/<see cref="Episode"/> set). What the server relays from the
/// downloader's queue, as of when the title was read.
/// </summary>
public sealed record DownloadProgress(
    string Title,
    long Size,
    long SizeLeft,
    string Status,
    TimeSpan? TimeLeft,
    DateTimeOffset? EstimatedCompletion,
    int? Season = null,
    int? Episode = null)
{
    /// <summary>0–1, how much has arrived. Zero for a download whose size is not known yet.</summary>
    public double Fraction => Size > 0 ? Math.Clamp(1 - (double)SizeLeft / Size, 0, 1) : 0;
}

/// <summary>Several downloads read as one: how much of all of it has arrived, and how long
/// until the last of it does.</summary>
public readonly record struct DownloadSummary(int Count, double Fraction, TimeSpan? TimeLeft)
{
    public static DownloadSummary? Of(IReadOnlyList<DownloadProgress> downloads)
    {
        if (downloads.Count == 0) return null;
        long size = 0, left = 0;
        TimeSpan? longest = null;
        foreach (var d in downloads)
        {
            size += Math.Max(0, d.Size);
            left += Math.Clamp(d.SizeLeft, 0, Math.Max(0, d.Size));
            if (d.TimeLeft is { } t && (longest is null || t > longest)) longest = t;
        }
        double fraction = size > 0 ? Math.Clamp(1 - (double)left / size, 0, 1) : 0;
        return new DownloadSummary(downloads.Count, fraction, longest);
    }

    /// <summary>"42%", with "· about 12 min left" when the downloader knows.</summary>
    public string Phrase
    {
        get
        {
            var text = $"{Math.Round(Fraction * 100):0}%";
            if (Count > 1) text += $" of {Count} files";
            if (TimeLeft is { } t && t > TimeSpan.Zero)
                text += t.TotalHours >= 1
                    ? $" · about {(int)t.TotalHours} h {t.Minutes} min left"
                    : $" · about {Math.Max(1, (int)Math.Ceiling(t.TotalMinutes))} min left";
            return text;
        }
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

    /// <summary>What Radarr/Sonarr are downloading for it right now; empty when nothing is.</summary>
    public IReadOnlyList<DownloadProgress> Downloads { get; init; } = [];
    public IReadOnlyList<DownloadProgress> Downloads4k { get; init; } = [];

    /// <summary>The request server's own id for its record of the title — what an issue is
    /// filed against (not the TMDB id). Null for a title it has no record of yet.</summary>
    public int? MediaId { get; init; }

    /// <summary>On this user's watchlist.</summary>
    public bool OnWatchlist { get; init; }
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

    /// <summary>What is downloading for it — the 4K downloads for a 4K request.</summary>
    public IReadOnlyList<DownloadProgress> Downloads { get; init; } = [];

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
    int? TvdbId = null, RequestOptions? Options = null);

/// <summary>How many of one kind this user may still request, when the server limits it.
/// For series the server counts <i>seasons</i>, not series.</summary>
/// <param name="Limit">Requests allowed per <paramref name="Days"/>; null when unlimited.</param>
/// <param name="Restricted">Nothing left: the server will refuse the next request.</param>
public sealed record RequestQuota(int? Limit, int Days, int Used, int? Remaining, bool Restricted)
{
    public bool IsLimited => Limit is > 0;

    /// <summary>"today", "this week", "in any 14 days" — the window the limit runs over. The
    /// server's window rolls rather than resetting on a calendar day, which "this week"
    /// glosses over; it is the phrase people use for a seven-day allowance anyway.</summary>
    public string Period => Days switch
    {
        1 => "today",
        7 => "this week",
        30 => "this month",
        _ => $"in any {Days} days",
    };
}

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

// ── Slice 7b: advanced requests, issues, the layout, genres, people ─────────

/// <summary>
/// Where an advanced request goes: which Radarr/Sonarr, at which quality, into which
/// folder. Only users with <see cref="RequestPermission.RequestAdvanced"/> (or who manage
/// requests) may send these; anyone else's request takes the server's defaults.
/// </summary>
public sealed record RequestOptions(int? ServerId, int? ProfileId, string? RootFolder);

/// <summary>One Radarr or Sonarr the request server sends requests to.</summary>
/// <param name="ActiveProfileId">The quality profile it uses when nobody picks one.</param>
/// <param name="ActiveDirectory">The root folder it uses when nobody picks one.</param>
public sealed record RequestService(int Id, string Name, bool Is4k, bool IsDefault, int? ActiveProfileId,
    string? ActiveDirectory);

/// <summary>A service's choices: its quality profiles and root folders.</summary>
public sealed record RequestServiceDetails(RequestService Server, IReadOnlyList<QualityProfile> Profiles,
    IReadOnlyList<RootFolder> RootFolders);

public sealed record QualityProfile(int Id, string Name);

/// <summary>A folder the downloader files things into, and how much room is left in it.</summary>
public sealed record RootFolder(int Id, string Path, long? FreeSpace);

/// <summary>What kind of problem a report is about — the server's own numbers.</summary>
public enum IssueType { Video = 1, Audio = 2, Subtitles = 3, Other = 4 }

/// <summary>A problem report. <paramref name="MediaId"/> is the request server's id for the
/// title (<see cref="DiscoverDetail.MediaId"/>), not TMDB's. Season and episode narrow it
/// down within a series; null (sent as 0) means the whole thing.</summary>
public sealed record NewIssue(int MediaId, IssueType Type, string Message, int? Season = null, int? Episode = null);

/// <summary>
/// The kinds of row an administrator can put on the request server's discover page —
/// its own <c>DiscoverSliderType</c> numbers. Aperture draws the ones the API can fill;
/// see the app's Discover view for which, and why the others are left out.
/// </summary>
public enum SliderKind
{
    RecentlyAdded = 1,
    RecentRequests = 2,
    Watchlist = 3,
    Trending = 4,
    PopularMovies = 5,
    MovieGenres = 6,
    UpcomingMovies = 7,
    Studios = 8,
    PopularTv = 9,
    TvGenres = 10,
    UpcomingTv = 11,
    Networks = 12,
    MovieKeyword = 13,
    MovieGenre = 14,
    TvKeyword = 15,
    TvGenre = 16,
    Search = 17,
    Studio = 18,
    Network = 19,
    MovieStreaming = 20,
    TvStreaming = 21,
}

/// <summary>One row of the administrator's discover layout.</summary>
/// <param name="Title">Set for a custom row; a built-in row is named by its kind.</param>
/// <param name="Data">What a custom row is about — a genre id, keyword ids, a search.</param>
public sealed record DiscoverSlider(int Id, SliderKind Kind, string? Title, string? Data, bool Enabled, int Order);

/// <summary>A genre to browse by, with a backdrop to draw its card with.</summary>
public sealed record DiscoverGenre(int Id, string Name, DiscoverKind Kind, string? BackdropPath);

/// <summary>Someone in TMDB's credits, and everything they are credited with.</summary>
public sealed record PersonDetail
{
    public required int TmdbId { get; init; }
    public required string Name { get; init; }
    public string? Biography { get; init; }
    public string? ProfilePath { get; init; }
    public string? KnownFor { get; init; }
    public DateOnly? Birthday { get; init; }
    public DateOnly? Deathday { get; init; }
    public string? PlaceOfBirth { get; init; }
    /// <summary>Films and series, newest first, each once however many roles it held.</summary>
    public IReadOnlyList<DiscoverItem> Credits { get; init; } = [];
}

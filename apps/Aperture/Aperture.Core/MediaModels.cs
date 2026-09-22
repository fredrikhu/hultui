namespace Aperture.Core;

/// <summary>What a server said about itself before anyone signed in.</summary>
/// <param name="Address">The base address that answered, normalized — scheme, host, port,
/// no trailing slash.</param>
public sealed record ServerInfo(string Address, string Name, string Version, string Id)
{
    /// <summary>Host (and port when it isn't the default) — what the failure copy names.</summary>
    public string Host => Uri.TryCreate(Address, UriKind.Absolute, out var u) ? u.Authority : Address;
}

/// <summary>Who is signed in, and with what. The token is the secret: it lives in the OS
/// credential store, never in the settings file.</summary>
public sealed record SignedInUser(Guid Id, string Name, string AccessToken, string? PrimaryImageTag);

/// <summary>The kinds of item Aperture renders differently. Anything else the server sends
/// lands in <see cref="Other"/> and is shown as a plain card.</summary>
public enum MediaKind { Movie, Series, Season, Episode, Video, BoxSet, Folder, Person, Other }

/// <summary>What a library is for — decides the poster shape and the default sort.</summary>
public enum LibraryKind { Movies, TvShows, Music, Photos, Books, MusicVideos, HomeVideos, Mixed, Other }

/// <summary>One library on the server's home screen.</summary>
public sealed record LibraryView(string Id, string Name, LibraryKind Kind, string? PrimaryImageTag);

/// <summary>
/// One item — a film, a series, a season, an episode — flattened to what the views draw.
/// Deliberately not a <c>BaseItemDto</c>: the UI never sees an SDK type, so the client can
/// be replaced without touching a view, and a test can build one in a line.
/// </summary>
public sealed record MediaItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required MediaKind Kind { get; init; }

    /// <summary>Image tags — the poster and the backdrop. Null means the server has no
    /// artwork for it and the view draws its placeholder.</summary>
    public string? PrimaryImageTag { get; init; }
    public string? BackdropImageTag { get; init; }
    /// <summary>The item whose backdrop stands in for this one — an episode borrows its
    /// series'. Null when the item's own <see cref="BackdropImageTag"/> is the one to use.</summary>
    public string? BackdropItemId { get; init; }
    /// <summary>Width ÷ height of the primary image, when the server measured it. Poster
    /// grids assume 2:3 without it.</summary>
    public double? PrimaryAspectRatio { get; init; }

    public int? ProductionYear { get; init; }
    public string? Overview { get; init; }
    public TimeSpan? RunTime { get; init; }
    public string? OfficialRating { get; init; }
    public float? CommunityRating { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];

    // Episode / season placement.
    public string? SeriesId { get; init; }
    public string? SeriesName { get; init; }
    public string? SeasonId { get; init; }
    public string? SeasonName { get; init; }
    /// <summary>Episode number within its season; season number for a season.</summary>
    public int? IndexNumber { get; init; }
    public int? ParentIndexNumber { get; init; }

    /// <summary>Episodes in a season, seasons in a series, items in a folder.</summary>
    public int? ChildCount { get; init; }

    // Per-user state.
    public bool Played { get; init; }
    public TimeSpan? ResumePosition { get; init; }
    /// <summary>0–100 when the server tracks partial playback; null when it doesn't.</summary>
    public double? PlayedPercentage { get; init; }
    public bool IsFavorite { get; init; }
    /// <summary>Unwatched episodes under a series or season.</summary>
    public int? UnplayedItemCount { get; init; }

    /// <summary>Whether this item is something the player can open at all.</summary>
    public bool IsPlayable => Kind is MediaKind.Movie or MediaKind.Episode or MediaKind.Video;

    /// <summary>"S2 · E4" for an episode, the year for a film — the line under a title.</summary>
    public string SubLine => Kind switch
    {
        MediaKind.Episode when ParentIndexNumber is { } s && IndexNumber is { } e => $"S{s} · E{e}",
        MediaKind.Season when IndexNumber is { } s => $"Season {s}",
        _ => ProductionYear?.ToString() ?? "",
    };
}

/// <summary>A page of items, and how many there are in total behind it.</summary>
public sealed record ItemPage(IReadOnlyList<MediaItem> Items, int TotalCount, int StartIndex)
{
    public static readonly ItemPage Empty = new([], 0, 0);
}

/// <summary>How a library view is sorted. The server's own names, kept out of the views.</summary>
public enum ItemSort { Name, DateAdded, PremiereDate, CommunityRating, Runtime, Random }

/// <summary>What to fetch from a library. A record so a view can change one field of the
/// query it is holding and re-run it.</summary>
public sealed record ItemQuery
{
    /// <summary>The library (or season, or series) to list. Null lists everything the user
    /// can see, which is what search does.</summary>
    public string? ParentId { get; init; }
    public int StartIndex { get; init; }
    public int Limit { get; init; } = 100;
    public ItemSort Sort { get; init; } = ItemSort.Name;
    public bool Descending { get; init; }
    public string? SearchTerm { get; init; }
    /// <summary>Only these kinds. Empty means whatever the parent holds.</summary>
    public IReadOnlyList<MediaKind> Kinds { get; init; } = [];
    /// <summary>Descend into sub-folders rather than listing only direct children.</summary>
    public bool Recursive { get; init; } = true;
    /// <summary>Null lists everything; true only watched, false only unwatched.</summary>
    public bool? Played { get; init; }
    public bool? Favorite { get; init; }
}

/// <summary>Which image of an item to fetch.</summary>
public enum ImageKind { Primary, Backdrop, Thumb, Logo }

/// <summary>
/// A stream the player can open, and the session bookkeeping that goes with it. The server
/// hands out one <see cref="PlaySessionId"/> per playback; every progress report carries it
/// back so "now playing" in the dashboard, and the resume point, follow the right stream.
/// </summary>
/// <param name="StreamUrl">What the media backend opens. Either the original file
/// (<c>/Videos/{id}/stream?static=true</c>) or the transcode the server prepared.</param>
/// <param name="Transcoding">True when the server refused direct play. The UI says so —
/// it's the difference between a film that plays instantly and one the server is re-encoding.</param>
public sealed record PlaybackTarget(
    Uri StreamUrl,
    string ItemId,
    string PlaySessionId,
    string? MediaSourceId,
    bool Transcoding,
    TimeSpan StartPosition,
    TimeSpan? Duration);

namespace Aperture.Core.Testing;

/// <summary>
/// A library with no server behind it: a fixture the headless tests and the screenshot
/// driver browse. Everything is in memory and synchronous — <c>await</c> on any call
/// completes before it yields — so a test never pumps frames waiting for a network.
///
/// <para><see cref="Calls"/> records every request in order
/// (<c>GetItems(parent=movies)</c>, <c>ReportProgress(00:01:30)</c>…), which is how the
/// playback-reporting tests assert on what the server would have been told.
/// <see cref="FailWith"/> scripts a failure for the next call, so the failure copy can be
/// tested without unplugging anything.</para>
/// </summary>
public sealed class FakeMediaLibrary : IMediaLibrary
{
    private readonly List<LibraryView> _libraries = [];
    private readonly Dictionary<string, MediaItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _children = new(StringComparer.OrdinalIgnoreCase);

    public FakeMediaLibrary(ServerInfo? server = null, SignedInUser? user = null)
    {
        Server = server ?? new ServerInfo("http://jellyfin.test:8096", "Test server", "12.1.0", "test-server-id");
        User = user ?? new SignedInUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), "tester", "test-token", null);
    }

    public ServerInfo Server { get; }
    public SignedInUser User { get; }

    /// <summary>Every call made, in order, for a test to assert on.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>When set, the next call fails with this and clears it.</summary>
    public ApertureFailure? FailWith { get; set; }

    /// <summary>Items the user is part-way through, in the order the home screen shows them.</summary>
    public List<string> ResumeIds { get; } = [];

    /// <summary>Next-up episodes, same idea.</summary>
    public List<string> NextUpIds { get; } = [];

    public bool Disposed { get; private set; }

    // ── Fixture building ─────────────────────────────────────────────────

    public FakeMediaLibrary AddLibrary(string id, string name, LibraryKind kind = LibraryKind.Movies)
    {
        _libraries.Add(new LibraryView(id, name, kind, null));
        _children.TryAdd(id, []);
        return this;
    }

    /// <summary>Renames a library, as an administrator does on the server: the next
    /// <see cref="GetLibrariesAsync"/> answers with the new name.</summary>
    public void RenameLibrary(string id, string name)
    {
        int at = _libraries.FindIndex(l => l.Id == id);
        if (at >= 0) _libraries[at] = _libraries[at] with { Name = name };
    }

    public FakeMediaLibrary AddItem(MediaItem item, string? parentId = null)
    {
        _items[item.Id] = item;
        if (parentId is not null)
        {
            if (!_children.TryGetValue(parentId, out var kids)) _children[parentId] = kids = [];
            kids.Add(item.Id);
        }
        return this;
    }

    /// <summary>A film, named and numbered — the one-liner most fixtures need.</summary>
    public FakeMediaLibrary AddMovie(string id, string name, string parentId, int year = 2020,
        int runtimeMinutes = 96, TimeSpan? resume = null, bool played = false) =>
        AddItem(new MediaItem
        {
            Id = id,
            Name = name,
            Kind = MediaKind.Movie,
            ProductionYear = year,
            RunTime = TimeSpan.FromMinutes(runtimeMinutes),
            PrimaryImageTag = "tag-" + id,
            Overview = $"{name} is a fixture.",
            ResumePosition = resume,
            Played = played,
            PlayedPercentage = resume is { } r ? r.TotalMinutes / runtimeMinutes * 100 : null,
        }, parentId);

    /// <summary>
    /// A season of a series that is already in the fixture, with <paramref name="episodes"/>
    /// episodes under it, the first <paramref name="watched"/> of them watched. Ids are
    /// <c>{series}-s{n}</c> and <c>{series}-s{n}e{m}</c>. The season and its episodes were added at
    /// <paramref name="added"/> (a week before the fixture was made, when not given).
    /// </summary>
    public FakeMediaLibrary AddSeason(string seriesId, int number, int episodes, int watched = 0,
        DateTimeOffset? added = null)
    {
        var series = _items[seriesId];
        var when = added ?? Made.AddDays(-7);
        string seasonId = $"{seriesId}-s{number}";
        AddItem(new MediaItem
        {
            Id = seasonId, Name = number == 0 ? "Specials" : $"Season {number}", Kind = MediaKind.Season,
            SeriesId = seriesId, SeriesName = series.Name, IndexNumber = number, ChildCount = episodes,
            PrimaryImageTag = "tag-" + seasonId, DateAdded = when,
        }, seriesId);
        for (int e = 1; e <= episodes; e++)
            AddItem(new MediaItem
            {
                Id = $"{seasonId}e{e}", Name = $"Episode {e}", Kind = MediaKind.Episode,
                SeriesId = seriesId, SeriesName = series.Name, SeasonId = seasonId, SeasonName = $"Season {number}",
                ParentIndexNumber = number, IndexNumber = e, RunTime = TimeSpan.FromMinutes(45),
                PrimaryImageTag = $"tag-{seasonId}e{e}", Played = e <= watched, DateAdded = when,
            }, seasonId);
        return this;
    }

    /// <summary>Takes an item and everything under it out of the fixture, as a server does when the
    /// files go.</summary>
    public void Remove(string id)
    {
        foreach (var child in (_children.GetValueOrDefault(id) ?? []).ToList()) Remove(child);
        _items.Remove(id);
        _children.Remove(id);
        foreach (var kids in _children.Values) kids.Remove(id);
    }

    /// <summary>When the fixture was made: what <see cref="AddSeason"/> dates a season from.</summary>
    public DateTimeOffset Made { get; } = DateTimeOffset.UtcNow;

    public MediaItem? Item(string id) => _items.GetValueOrDefault(id);

    /// <summary>Replaces an item — what a test uses to check a view re-reads state after a
    /// watched-toggle.</summary>
    public void Update(MediaItem item) => _items[item.Id] = item;

    // ── IMediaLibrary ────────────────────────────────────────────────────

    public Task<ApertureResult<IReadOnlyList<LibraryView>>> GetLibrariesAsync(CancellationToken ct = default)
    {
        Calls.Add("GetLibraries");
        return Result<IReadOnlyList<LibraryView>>(_libraries.ToList());
    }

    public Task<ApertureResult<ItemPage>> GetItemsAsync(ItemQuery query, CancellationToken ct = default)
    {
        Calls.Add($"GetItems(parent={query.ParentId ?? "-"}, start={query.StartIndex}, limit={query.Limit}, " +
                  $"sort={query.Sort}{(query.Descending ? " desc" : "")}" +
                  $"{(query.SearchTerm is { Length: > 0 } s ? $", search={s}" : "")}" +
                  $"{(query.PersonId is { Length: > 0 } p ? $", person={p}" : "")}" +
                  $"{(query.Favorite is { } f2 ? $", favourite={f2}" : "")})");

        var all = Query(query);
        var page = all.Skip(query.StartIndex).Take(query.Limit).ToList();
        return Result(new ItemPage(page, all.Count, query.StartIndex));
    }

    /// <summary>Everything a query matches, filtered and sorted, before paging.</summary>
    private List<MediaItem> Query(ItemQuery query)
    {
        IEnumerable<MediaItem> source = query.ParentId is { } parent
            ? (_children.GetValueOrDefault(parent) ?? []).Select(id => _items[id])
            : _items.Values;

        if (query.SearchTerm is { Length: > 0 } term)
            source = source.Where(i => i.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (query.Kinds.Count > 0) source = source.Where(i => query.Kinds.Contains(i.Kind));
        if (query.PersonId is { Length: > 0 } person)
            source = source.Where(i => i.People.Any(p => p.Id == person));
        if (query.GenreId is { Length: > 0 } genre)
            source = source.Where(i => i.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase));
        if (query.Played is { } played) source = source.Where(i => i.Played == played);
        if (query.Favorite is { } fav) source = source.Where(i => i.IsFavorite == fav);

        source = query.Sort switch
        {
            ItemSort.DateAdded or ItemSort.PremiereDate => source.OrderBy(i => i.ProductionYear ?? 0),
            ItemSort.CommunityRating => source.OrderBy(i => i.CommunityRating ?? 0),
            ItemSort.Runtime => source.OrderBy(i => i.RunTime ?? TimeSpan.Zero),
            ItemSort.Random => source,
            _ => source.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
        };
        if (query.Descending) source = source.Reverse();
        return source.ToList();
    }

    public Task<ApertureResult<ItemPage>> GetSeriesByEpisodeAddedAsync(int startIndex, int limit,
        CancellationToken ct = default)
    {
        Calls.Add($"GetSeriesByEpisodeAdded(start={startIndex}, limit={limit})");
        var all = _items.Values.Where(i => i.Kind == MediaKind.Series)
            .Select(s => s with { DateLastMediaAdded = s.DateLastMediaAdded ?? LastAdded(s.Id) })
            .OrderByDescending(s => s.DateLastMediaAdded ?? DateTimeOffset.MinValue)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Result(new ItemPage(all.Skip(startIndex).Take(limit).ToList(), all.Count, startIndex));
    }

    public Task<ApertureResult<ItemPage>> GetSeasonsAsync(string? seriesId, int startIndex, int limit,
        CancellationToken ct = default)
    {
        Calls.Add($"GetSeasons({seriesId ?? "all"}, start={startIndex}, limit={limit})");
        IEnumerable<MediaItem> seasons = seriesId is null
            ? _items.Values.Where(i => i.Kind == MediaKind.Season)
            : (_children.GetValueOrDefault(seriesId) ?? []).Select(id => _items[id]).Where(i => i.Kind == MediaKind.Season);
        var all = seasons
            .OrderBy(s => _items.GetValueOrDefault(s.SeriesId ?? "")?.Name ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.IndexNumber ?? int.MaxValue)
            .Select(WithWatchedState)
            .ToList();
        return Result(new ItemPage(all.Skip(startIndex).Take(limit).ToList(), all.Count, startIndex));
    }

    /// <summary>A season as the server reports it with user data: counted from the episodes under
    /// it, when it has any in the fixture; otherwise as the fixture wrote it.</summary>
    private MediaItem WithWatchedState(MediaItem season)
    {
        var episodes = (_children.GetValueOrDefault(season.Id) ?? [])
            .Select(id => _items[id]).Where(i => i.Kind == MediaKind.Episode).ToList();
        if (episodes.Count == 0) return season with { FileCount = season.FileCount ?? 0 };
        int unwatched = episodes.Count(e => !e.Played);
        return season with
        {
            FileCount = episodes.Count,
            UnplayedItemCount = unwatched,
            Played = unwatched == 0,
        };
    }

    /// <summary>When anything under a series last arrived: its seasons' and episodes' dates added.</summary>
    private DateTimeOffset? LastAdded(string id)
    {
        DateTimeOffset? newest = null;
        foreach (var child in _children.GetValueOrDefault(id) ?? [])
        {
            var item = _items[child];
            if (item.DateAdded is { } at && (newest is null || at > newest)) newest = at;
            if (LastAdded(child) is { } below && (newest is null || below > newest)) newest = below;
        }
        return newest;
    }

    public Task<ApertureResult<IReadOnlyList<MediaItem>>> GetResumeAsync(int limit = 12, CancellationToken ct = default)
    {
        Calls.Add($"GetResume(limit={limit})");
        return Result<IReadOnlyList<MediaItem>>(
            ResumeIds.Select(id => _items[id]).Take(limit).ToList());
    }

    public Task<ApertureResult<IReadOnlyList<MediaItem>>> GetNextUpAsync(int limit = 12, string? seriesId = null,
        CancellationToken ct = default)
    {
        Calls.Add($"GetNextUp(limit={limit}{(seriesId is null ? "" : ", series=" + seriesId)})");
        var items = NextUpIds.Select(id => _items[id]);
        if (seriesId is not null) items = items.Where(i => i.SeriesId == seriesId);
        return Result<IReadOnlyList<MediaItem>>(items.Take(limit).ToList());
    }

    public Task<ApertureResult<MediaItem?>> GetNextEpisodeAsync(MediaItem episode, CancellationToken ct = default)
    {
        Calls.Add($"GetNextEpisode({episode.Id})");
        if (episode.Kind != MediaKind.Episode || episode.SeriesId is not { } seriesId)
            return Result<MediaItem?>(null);

        // The fixture's running order is the order episodes were added under the series.
        var siblings = (_children.GetValueOrDefault(seriesId) ?? [])
            .Select(id => _items[id])
            .Where(i => i.Kind == MediaKind.Episode)
            .ToList();
        int index = siblings.FindIndex(i => i.Id == episode.Id);
        return Result(index >= 0 && index + 1 < siblings.Count ? siblings[index + 1] : null);
    }

    public Task<ApertureResult<IReadOnlyList<MediaItem>>> GetLatestAsync(string libraryId, int limit = 16,
        CancellationToken ct = default)
    {
        Calls.Add($"GetLatest({libraryId}, limit={limit})");
        // What the real one asks: the library itself, newest first — so a row's "See all"
        // (the grid sorted Newest) carries on in the same order here as against a server.
        var sorted = Query(new ItemQuery
        {
            ParentId = libraryId,
            Limit = limit,
            Sort = ItemSort.DateAdded,
            Descending = true,
            Recursive = false,
        }).Take(limit).ToList();
        return Result<IReadOnlyList<MediaItem>>(sorted);
    }

    /// <summary>The genres of everything under a library, which is how a real server
    /// answers it too.</summary>
    public Task<ApertureResult<IReadOnlyList<MediaGenre>>> GetGenresAsync(string libraryId,
        CancellationToken ct = default)
    {
        Calls.Add($"GetGenres({libraryId})");
        var genres = (_children.GetValueOrDefault(libraryId) ?? [])
            .Select(id => _items[id])
            .SelectMany(i => i.Genres)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .Select(g => new MediaGenre(g, g))
            .ToList();
        return Result<IReadOnlyList<MediaGenre>>(genres);
    }

    public Task<ApertureResult<MediaItem>> GetItemAsync(string itemId, CancellationToken ct = default)
    {
        Calls.Add($"GetItem({itemId})");
        if (FailWith is { } f) { FailWith = null; return Task.FromResult(ApertureResult<MediaItem>.Fail(f)); }
        return Task.FromResult(_items.TryGetValue(itemId, out var item)
            ? ApertureResult<MediaItem>.Success(item)
            : ApertureResult<MediaItem>.Fail(ApertureFailureKind.NotFound, "No such item"));
    }

    // The size goes into the URL as the real server's does, so a test can see what size the
    // app asked for, and the same picture at two sizes is two cache entries here too.
    public Uri? ImageUrl(string itemId, ImageKind kind, string? tag, int? maxWidth = null, int? maxHeight = null) =>
        tag is null ? null
            : new Uri($"{Server.Address}/Items/{itemId}/Images/{kind}?tag={tag}{Size(maxWidth, maxHeight)}");

    public Uri? TrickplayTileUrl(string itemId, int width, int tileIndex) =>
        width <= 0 || tileIndex < 0 ? null
            : new Uri($"{Server.Address}/Videos/{itemId}/Trickplay/{width}/{tileIndex}.jpg");

    public Uri? ChapterImageUrl(string itemId, int chapterIndex, string? tag, int? maxWidth = null, int? maxHeight = null) =>
        tag is null ? null
            : new Uri($"{Server.Address}/Items/{itemId}/Images/Chapter/{chapterIndex}?tag={tag}{Size(maxWidth, maxHeight)}");

    private static string Size(int? width, int? height) =>
        (width is { } w ? $"&fillWidth={w}" : "") + (height is { } h ? $"&fillHeight={h}" : "");

    public Task<ApertureResult<PlaybackTarget>> StartPlaybackAsync(string itemId, TimeSpan startPosition,
        int? maxBitrateBps = null, CancellationToken ct = default)
    {
        Calls.Add($"StartPlayback({itemId}, at={startPosition:g}"
            + (maxBitrateBps is { } bps ? $", max={bps}" : "") + ")");
        if (FailWith is { } f) { FailWith = null; return Task.FromResult(ApertureResult<PlaybackTarget>.Fail(f)); }
        if (!_items.TryGetValue(itemId, out var item))
            return Task.FromResult(ApertureResult<PlaybackTarget>.Fail(ApertureFailureKind.NotFound, "No such item"));
        // A ceiling is what makes a server transcode, so a capped start reports one, at the HLS
        // playlist a Jellyfin server hands out for it.
        bool transcoding = maxBitrateBps is > 0;
        return Task.FromResult(ApertureResult<PlaybackTarget>.Success(new PlaybackTarget(
            new Uri(transcoding
                ? $"{Server.Address}/videos/{itemId}/master.m3u8?playSessionId=play-session-{itemId}"
                : $"{Server.Address}/Videos/{itemId}/stream?static=true"),
            itemId, "play-session-" + itemId, "source-" + itemId,
            transcoding, startPosition, item.RunTime)));
    }

    public PlaybackTarget OriginalFileOf(PlaybackTarget transcode)
    {
        Calls.Add($"OriginalFileOf({transcode.ItemId})");
        return transcode with
        {
            StreamUrl = new Uri($"{Server.Address}/Videos/{transcode.ItemId}/stream?static=true"),
            Transcoding = false,
        };
    }

    public Task<ApertureResult<bool>> ReportStartedAsync(PlaybackTarget target, TimeSpan position, bool paused,
        CancellationToken ct = default)
    {
        Calls.Add($"ReportStarted({target.ItemId}, at={position:g}{(paused ? ", paused" : "")})");
        return Result(true);
    }

    public Task<ApertureResult<bool>> ReportProgressAsync(PlaybackTarget target, TimeSpan position, bool paused,
        CancellationToken ct = default)
    {
        Calls.Add($"ReportProgress({target.ItemId}, at={position:g}{(paused ? ", paused" : "")})");
        return Result(true);
    }

    public Task<ApertureResult<bool>> ReportStoppedAsync(PlaybackTarget target, TimeSpan position,
        CancellationToken ct = default)
    {
        Calls.Add($"ReportStopped({target.ItemId}, at={position:g})");
        return Result(true);
    }

    public Task<ApertureResult<bool>> SetPlayedAsync(string itemId, bool played, CancellationToken ct = default)
    {
        Calls.Add($"SetPlayed({itemId}, {played})");
        if (_items.TryGetValue(itemId, out var item))
            _items[itemId] = item with { Played = played, ResumePosition = played ? null : item.ResumePosition };
        return Result(true);
    }

    public Task<ApertureResult<bool>> SetFavoriteAsync(string itemId, bool favorite, CancellationToken ct = default)
    {
        Calls.Add($"SetFavorite({itemId}, {favorite})");
        if (_items.TryGetValue(itemId, out var item)) _items[itemId] = item with { IsFavorite = favorite };
        return Result(true);
    }

    /// <summary>Quick Connect codes this user has approved, in order.</summary>
    public List<string> ApprovedCodes { get; } = [];

    public Task<ApertureResult<bool>> AuthorizeQuickConnectAsync(string code, CancellationToken ct = default)
    {
        Calls.Add($"AuthorizeQuickConnect({code})");
        ApprovedCodes.Add(code);
        return Result(true);
    }

    public void Dispose() => Disposed = true;

    private Task<ApertureResult<T>> Result<T>(T value)
    {
        if (FailWith is { } f)
        {
            FailWith = null;
            return Task.FromResult(ApertureResult<T>.Fail(f));
        }
        return Task.FromResult(ApertureResult<T>.Success(value));
    }
}

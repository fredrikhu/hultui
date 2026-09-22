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

        var all = source.ToList();
        var page = all.Skip(query.StartIndex).Take(query.Limit).ToList();
        return Result(new ItemPage(page, all.Count, query.StartIndex));
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
        var kids = (_children.GetValueOrDefault(libraryId) ?? []).Select(id => _items[id]).Take(limit).ToList();
        return Result<IReadOnlyList<MediaItem>>(kids);
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

    public Uri? ImageUrl(string itemId, ImageKind kind, string? tag, int? maxWidth = null, int? maxHeight = null) =>
        tag is null ? null : new Uri($"{Server.Address}/Items/{itemId}/Images/{kind}?tag={tag}");

    public Task<ApertureResult<PlaybackTarget>> StartPlaybackAsync(string itemId, TimeSpan startPosition,
        int? maxBitrateBps = null, CancellationToken ct = default)
    {
        Calls.Add($"StartPlayback({itemId}, at={startPosition:g}"
            + (maxBitrateBps is { } bps ? $", max={bps}" : "") + ")");
        if (FailWith is { } f) { FailWith = null; return Task.FromResult(ApertureResult<PlaybackTarget>.Fail(f)); }
        if (!_items.TryGetValue(itemId, out var item))
            return Task.FromResult(ApertureResult<PlaybackTarget>.Fail(ApertureFailureKind.NotFound, "No such item"));
        return Task.FromResult(ApertureResult<PlaybackTarget>.Success(new PlaybackTarget(
            new Uri($"{Server.Address}/Videos/{itemId}/stream?static=true"),
            itemId, "play-session-" + itemId, "source-" + itemId,
            // A ceiling is what makes a server transcode, so a capped start reports one.
            Transcoding: maxBitrateBps is > 0, startPosition, item.RunTime)));
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

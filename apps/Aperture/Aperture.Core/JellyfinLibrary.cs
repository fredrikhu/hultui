using Jellyfin.Sdk;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Aperture.Core;

/// <summary>
/// <see cref="IMediaLibrary"/> over the official Jellyfin SDK. Owns the HTTP client, the
/// Kiota adapter and the generated client for one signed-in session; disposing it closes
/// the connection pool.
///
/// <para>Everything the SDK returns is mapped to Aperture's own records here and nowhere
/// else — no <c>BaseItemDto</c> escapes this file. Two payload shapes get special care:
/// image tags arrive as an untyped bag (Kiota models a free-form object as
/// <c>AdditionalData</c>), and ticks are 100-nanosecond units, which is exactly a
/// <see cref="TimeSpan"/> tick, so the conversion is a constructor rather than arithmetic.</para>
/// </summary>
public sealed class JellyfinLibrary : IMediaLibrary
{
    private readonly JellyfinApiClient _client;
    private readonly HttpClientRequestAdapter _adapter;
    private readonly HttpClient _http;
    private readonly DeviceIdentity _device;
    private bool _disposed;

    /// <summary>What every list asks the server to fill in. Kept in one place because a
    /// missing field shows up as a blank line in the UI rather than an error.</summary>
    private static readonly ItemFields[] ListFields =
        [ItemFields.PrimaryImageAspectRatio, ItemFields.Overview];

    private static readonly ItemFields[] DetailFields =
        [ItemFields.PrimaryImageAspectRatio, ItemFields.Overview, ItemFields.Genres, ItemFields.Studios,
         ItemFields.People, ItemFields.MediaSources, ItemFields.Taglines];

    private static readonly ImageType[] ListImages = [ImageType.Primary, ImageType.Backdrop, ImageType.Thumb];

    internal JellyfinLibrary(ServerInfo server, SignedInUser user, DeviceIdentity device,
        JellyfinApiClient client, HttpClientRequestAdapter adapter, HttpClient http)
    {
        Server = server;
        User = user;
        _device = device;
        _client = client;
        _adapter = adapter;
        _http = http;
    }

    public ServerInfo Server { get; }
    public SignedInUser User { get; }

    /// <summary>The device this session identifies as — the settings file keeps it so the
    /// same id comes back next run.</summary>
    public DeviceIdentity Device => _device;

    public async Task<ApertureResult<IReadOnlyList<LibraryView>>> GetLibrariesAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _client.UserViews.GetAsync(q => q.QueryParameters.UserId = User.Id, ct)
                .ConfigureAwait(false);
            var views = (result?.Items ?? [])
                .Where(v => v.Id is not null)
                .Select(v => new LibraryView(
                    v.Id!.Value.ToString("N"),
                    v.Name ?? "Library",
                    MapLibraryKind(v.CollectionType),
                    PrimaryTag(v)))
                .ToList();
            return ApertureResult<IReadOnlyList<LibraryView>>.Success(views);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ApertureResult<IReadOnlyList<LibraryView>>.Fail(JellyfinConnection.Classify(ex, Server.Host));
        }
    }

    public async Task<ApertureResult<ItemPage>> GetItemsAsync(ItemQuery query, CancellationToken ct = default)
    {
        try
        {
            var result = await _client.Items.GetAsync(q =>
            {
                var p = q.QueryParameters;
                p.UserId = User.Id;
                if (ParseId(query.ParentId) is { } parent) p.ParentId = parent;
                p.StartIndex = query.StartIndex;
                p.Limit = query.Limit;
                p.Recursive = query.Recursive;
                p.SortBy = [MapSort(query.Sort)];
                p.SortOrder = [query.Descending ? SortOrder.Descending : SortOrder.Ascending];
                p.Fields = ListFields;
                p.EnableImageTypes = ListImages;
                p.ImageTypeLimit = 1;
                p.EnableUserData = true;
                p.EnableTotalRecordCount = true;
                if (!string.IsNullOrWhiteSpace(query.SearchTerm)) p.SearchTerm = query.SearchTerm;
                if (query.Kinds.Count > 0) p.IncludeItemTypes = [.. query.Kinds.Select(MapKind)];
                if (query.Played is { } played) p.IsPlayed = played;
                if (query.Favorite is { } fav) p.IsFavorite = fav;
                if (ParseId(query.PersonId) is { } person) p.PersonIds = [person];
                if (ParseId(query.GenreId) is { } genre) p.GenreIds = [genre];
            }, ct).ConfigureAwait(false);

            var items = (result?.Items ?? []).Select(Map).ToList();
            return ApertureResult<ItemPage>.Success(
                new ItemPage(items, result?.TotalRecordCount ?? items.Count, result?.StartIndex ?? query.StartIndex));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ApertureResult<ItemPage>.Fail(JellyfinConnection.Classify(ex, Server.Host));
        }
    }

    public async Task<ApertureResult<IReadOnlyList<MediaItem>>> GetResumeAsync(int limit = 12, CancellationToken ct = default)
    {
        try
        {
            var result = await _client.UserItems.Resume.GetAsync(q =>
            {
                var p = q.QueryParameters;
                p.UserId = User.Id;
                p.Limit = limit;
                p.Fields = ListFields;
                p.EnableImageTypes = ListImages;
                p.ImageTypeLimit = 1;
                p.EnableUserData = true;
                p.MediaTypes = [MediaType.Video];
            }, ct).ConfigureAwait(false);
            return ApertureResult<IReadOnlyList<MediaItem>>.Success((result?.Items ?? []).Select(Map).ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ApertureResult<IReadOnlyList<MediaItem>>.Fail(JellyfinConnection.Classify(ex, Server.Host));
        }
    }

    public async Task<ApertureResult<IReadOnlyList<MediaItem>>> GetNextUpAsync(int limit = 12, string? seriesId = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _client.Shows.NextUp.GetAsync(q =>
            {
                var p = q.QueryParameters;
                p.UserId = User.Id;
                p.Limit = limit;
                if (ParseId(seriesId) is { } series) p.SeriesId = series;
                p.Fields = ListFields;
                p.EnableImageTypes = ListImages;
                p.ImageTypeLimit = 1;
                p.EnableUserData = true;
                // An episode already half-watched belongs in Continue watching, not here.
                p.EnableResumable = false;
            }, ct).ConfigureAwait(false);
            return ApertureResult<IReadOnlyList<MediaItem>>.Success((result?.Items ?? []).Select(Map).ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ApertureResult<IReadOnlyList<MediaItem>>.Fail(JellyfinConnection.Classify(ex, Server.Host));
        }
    }

    public async Task<ApertureResult<MediaItem?>> GetNextEpisodeAsync(MediaItem episode, CancellationToken ct = default)
    {
        if (episode.Kind != MediaKind.Episode || ParseId(episode.SeriesId) is not { } series)
            return ApertureResult<MediaItem?>.Success(null);
        try
        {
            // StartItemId makes the list begin at this episode, so the next one is the
            // second entry — and because the list is the whole series, the last episode of
            // a season is followed by the first of the next.
            var result = await _client.Shows[series].Episodes.GetAsync(q =>
            {
                var p = q.QueryParameters;
                p.UserId = User.Id;
                p.StartItemId = ParseId(episode.Id);
                p.Limit = 2;
                p.Fields = ListFields;
                p.EnableImageTypes = ListImages;
                p.ImageTypeLimit = 1;
                p.EnableUserData = true;
            }, ct).ConfigureAwait(false);

            var items = result?.Items ?? [];
            // The first entry is the episode we just watched; anything after it is next.
            var next = items.Count > 1 ? items[1] : null;
            return ApertureResult<MediaItem?>.Success(next is null ? null : Map(next));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ApertureResult<MediaItem?>.Fail(JellyfinConnection.Classify(ex, Server.Host));
        }
    }

    /// <summary>
    /// What arrived in a library most recently — the home screen's shelf per library.
    ///
    /// <para>Deliberately <i>not</i> the server's <c>/Items/Latest</c>, which hides
    /// anything the user has played: on a library that has been watched through it returns
    /// nothing at all, and a home screen whose rows disappear as you watch them is a worse
    /// answer than showing what is there. This is the ordinary item query, newest first.</para>
    /// </summary>
    public async Task<ApertureResult<IReadOnlyList<MediaItem>>> GetLatestAsync(string libraryId, int limit = 16,
        CancellationToken ct = default)
    {
        var page = await GetItemsAsync(new ItemQuery
        {
            ParentId = libraryId,
            Limit = limit,
            Sort = ItemSort.DateAdded,
            Descending = true,
            Recursive = false,
        }, ct).ConfigureAwait(false);
        return page.Ok
            ? ApertureResult<IReadOnlyList<MediaItem>>.Success(page.Value.Items)
            : page.As<IReadOnlyList<MediaItem>>();
    }

    public async Task<ApertureResult<MediaItem>> GetItemAsync(string itemId, CancellationToken ct = default)
    {
        if (ParseId(itemId) is not { } id)
            return ApertureResult<MediaItem>.Fail(ApertureFailureKind.NotFound, $"'{itemId}' is not an item id");
        try
        {
            var dto = await _client.Items[id].GetAsync(q =>
            {
                q.QueryParameters.UserId = User.Id;
            }, ct).ConfigureAwait(false);
            // The single-item endpoint sends the heavy fields (people, chapters, media
            // sources) without being asked; a list query never does, which is why
            // MediaItem.People is empty everywhere except here.
            if (dto is null)
                return ApertureResult<MediaItem>.Fail(ApertureFailureKind.NotFound, "No such item");
            return ApertureResult<MediaItem>.Success(Map(dto));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ApertureResult<MediaItem>.Fail(JellyfinConnection.Classify(ex, Server.Host));
        }
    }

    /// <summary>
    /// Image URLs carry the token as a query parameter rather than a header: they are
    /// fetched by a plain <c>HttpClient</c> in the poster cache, and by the media backend
    /// for a poster overlay, neither of which runs through Kiota.
    /// </summary>
    public Uri? ImageUrl(string itemId, ImageKind kind, string? tag, int? maxWidth = null, int? maxHeight = null)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        var name = kind switch
        {
            ImageKind.Backdrop => "Backdrop",
            ImageKind.Thumb => "Thumb",
            ImageKind.Logo => "Logo",
            _ => "Primary",
        };
        var url = $"{Server.Address}/Items/{Uri.EscapeDataString(itemId)}/Images/{name}?quality=90";
        if (tag is { Length: > 0 }) url += $"&tag={Uri.EscapeDataString(tag)}";
        if (maxWidth is { } w) url += $"&fillWidth={w}";
        if (maxHeight is { } h) url += $"&fillHeight={h}";
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
    }

    public async Task<ApertureResult<PlaybackTarget>> StartPlaybackAsync(string itemId, TimeSpan startPosition,
        CancellationToken ct = default)
    {
        if (ParseId(itemId) is not { } id)
            return ApertureResult<PlaybackTarget>.Fail(ApertureFailureKind.NotFound, $"'{itemId}' is not an item id");
        try
        {
            var info = await _client.Items[id].PlaybackInfo.PostAsync(new PlaybackInfoDto
            {
                UserId = User.Id,
                StartTimeTicks = startPosition.Ticks,
                // mpv plays essentially any container the server can hand it, so ask for
                // the file itself and let the server transcode only when it insists.
                EnableDirectPlay = true,
                EnableDirectStream = true,
                EnableTranscoding = true,
                AllowVideoStreamCopy = true,
                AllowAudioStreamCopy = true,
                AutoOpenLiveStream = true,
            }, cancellationToken: ct).ConfigureAwait(false);

            if (info?.MediaSources is not { Count: > 0 } sources)
                return ApertureResult<PlaybackTarget>.Fail(ApertureFailureKind.NotFound,
                    info?.ErrorCode?.ToString() ?? "The server offered no way to play this");

            var source = sources[0];
            var playSessionId = info.PlaySessionId ?? Guid.NewGuid().ToString("N");
            bool transcoding = source.SupportsDirectStream != true && source.TranscodingUrl is { Length: > 0 };

            Uri? url = transcoding
                ? Uri.TryCreate(Server.Address + source.TranscodingUrl, UriKind.Absolute, out var t) ? t : null
                : DirectStreamUrl(itemId, source, playSessionId);

            if (url is null)
                return ApertureResult<PlaybackTarget>.Fail(ApertureFailureKind.NotJellyfin,
                    "The server's stream URL could not be read");

            var duration = source.RunTimeTicks is { } ticks ? new TimeSpan(ticks) : (TimeSpan?)null;
            return ApertureResult<PlaybackTarget>.Success(new PlaybackTarget(
                url, itemId, playSessionId, source.Id, transcoding, startPosition, duration));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ApertureResult<PlaybackTarget>.Fail(JellyfinConnection.Classify(ex, Server.Host));
        }
    }

    /// <summary>The original file, byte for byte: <c>static=true</c> is what makes the
    /// server stop deciding and start sending.</summary>
    private Uri? DirectStreamUrl(string itemId, MediaSourceInfo source, string playSessionId)
    {
        var url = $"{Server.Address}/Videos/{Uri.EscapeDataString(itemId)}/stream" +
                  $"?static=true&playSessionId={Uri.EscapeDataString(playSessionId)}" +
                  $"&api_key={Uri.EscapeDataString(User.AccessToken)}";
        if (source.Id is { Length: > 0 } sourceId) url += $"&mediaSourceId={Uri.EscapeDataString(sourceId)}";
        if (source.Container is { Length: > 0 } container) url += $"&container={Uri.EscapeDataString(container)}";
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
    }

    public Task<ApertureResult<bool>> ReportStartedAsync(PlaybackTarget target, TimeSpan position, bool paused,
        CancellationToken ct = default) =>
        Post(() => _client.Sessions.Playing.PostAsync(new PlaybackStartInfo
        {
            ItemId = ParseId(target.ItemId),
            MediaSourceId = target.MediaSourceId,
            PlaySessionId = target.PlaySessionId,
            PositionTicks = position.Ticks,
            IsPaused = paused,
            CanSeek = true,
            PlayMethod = target.Transcoding
                ? PlaybackStartInfo_PlayMethod.Transcode
                : PlaybackStartInfo_PlayMethod.DirectStream,
        }, cancellationToken: ct), ct);

    public Task<ApertureResult<bool>> ReportProgressAsync(PlaybackTarget target, TimeSpan position, bool paused,
        CancellationToken ct = default) =>
        Post(() => _client.Sessions.Playing.Progress.PostAsync(new PlaybackProgressInfo
        {
            ItemId = ParseId(target.ItemId),
            MediaSourceId = target.MediaSourceId,
            PlaySessionId = target.PlaySessionId,
            PositionTicks = position.Ticks,
            IsPaused = paused,
            CanSeek = true,
            PlayMethod = target.Transcoding
                ? PlaybackProgressInfo_PlayMethod.Transcode
                : PlaybackProgressInfo_PlayMethod.DirectStream,
        }, cancellationToken: ct), ct);

    public Task<ApertureResult<bool>> ReportStoppedAsync(PlaybackTarget target, TimeSpan position,
        CancellationToken ct = default) =>
        Post(() => _client.Sessions.Playing.Stopped.PostAsync(new PlaybackStopInfo
        {
            ItemId = ParseId(target.ItemId),
            MediaSourceId = target.MediaSourceId,
            PlaySessionId = target.PlaySessionId,
            PositionTicks = position.Ticks,
        }, cancellationToken: ct), ct);

    public Task<ApertureResult<bool>> SetPlayedAsync(string itemId, bool played, CancellationToken ct = default)
    {
        if (ParseId(itemId) is not { } id)
            return Task.FromResult(ApertureResult<bool>.Fail(ApertureFailureKind.NotFound, "Not an item id"));
        return Post(() => played
            ? _client.UserPlayedItems[id].PostAsync(q => q.QueryParameters.UserId = User.Id, ct)
            : _client.UserPlayedItems[id].DeleteAsync(q => q.QueryParameters.UserId = User.Id, ct), ct);
    }

    public Task<ApertureResult<bool>> SetFavoriteAsync(string itemId, bool favorite, CancellationToken ct = default)
    {
        if (ParseId(itemId) is not { } id)
            return Task.FromResult(ApertureResult<bool>.Fail(ApertureFailureKind.NotFound, "Not an item id"));
        return Post(() => favorite
            ? _client.UserFavoriteItems[id].PostAsync(q => q.QueryParameters.UserId = User.Id, ct)
            : _client.UserFavoriteItems[id].DeleteAsync(q => q.QueryParameters.UserId = User.Id, ct), ct);
    }

    /// <summary>A call whose only interesting outcome is whether it worked. Progress
    /// reports run on a timer, so a failure is classified and handed back rather than
    /// thrown at a caller that has nowhere to put it.</summary>
    private async Task<ApertureResult<bool>> Post(Func<Task> call, CancellationToken ct)
    {
        try
        {
            await call().ConfigureAwait(false);
            return ApertureResult<bool>.Success(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ApertureResult<bool>.Fail(JellyfinConnection.Classify(ex, Server.Host));
        }
    }

    private async Task<ApertureResult<bool>> Post<T>(Func<Task<T>> call, CancellationToken ct) =>
        await Post(async () => { await call().ConfigureAwait(false); }, ct).ConfigureAwait(false);

    // ── Mapping ──────────────────────────────────────────────────────────

    internal static MediaItem Map(BaseItemDto dto) => new()
    {
        Id = dto.Id?.ToString("N") ?? "",
        Name = dto.Name ?? "Untitled",
        Kind = MapKind(dto.Type),
        PrimaryImageTag = PrimaryTag(dto),
        BackdropImageTag = dto.BackdropImageTags is { Count: > 0 } b ? b[0] : null,
        BackdropItemId = dto.ParentBackdropItemId?.ToString("N"),
        PrimaryAspectRatio = dto.PrimaryImageAspectRatio,
        ProductionYear = dto.ProductionYear,
        Overview = dto.Overview,
        RunTime = dto.RunTimeTicks is { } ticks and > 0 ? new TimeSpan(ticks) : null,
        OfficialRating = dto.OfficialRating,
        CommunityRating = dto.CommunityRating,
        Genres = dto.Genres ?? [],
        SeriesId = dto.SeriesId?.ToString("N"),
        SeriesName = dto.SeriesName,
        SeasonId = dto.SeasonId?.ToString("N"),
        SeasonName = dto.SeasonName,
        IndexNumber = dto.IndexNumber,
        ParentIndexNumber = dto.ParentIndexNumber,
        ChildCount = dto.ChildCount ?? dto.RecursiveItemCount,
        People = MapPeople(dto.People),
        Chapters = MapChapters(dto.Chapters),
        Played = dto.UserData?.Played ?? false,
        ResumePosition = dto.UserData?.PlaybackPositionTicks is { } pos and > 0 ? new TimeSpan(pos) : null,
        PlayedPercentage = dto.UserData?.PlayedPercentage,
        IsFavorite = dto.UserData?.IsFavorite ?? false,
        UnplayedItemCount = dto.UserData?.UnplayedItemCount,
    };

    private static IReadOnlyList<MediaPerson> MapPeople(List<BaseItemPerson>? people) =>
        people is null ? [] : people
            .Where(p => p.Id is not null && !string.IsNullOrEmpty(p.Name))
            .Select(p => new MediaPerson(
                p.Id!.Value.ToString("N"),
                p.Name!,
                MapPersonKind(p.Type),
                string.IsNullOrWhiteSpace(p.Role) ? null : p.Role,
                p.PrimaryImageTag))
            .ToList();

    internal static PersonKind MapPersonKind(BaseItemPerson_Type? type) => type switch
    {
        BaseItemPerson_Type.Actor => PersonKind.Actor,
        BaseItemPerson_Type.GuestStar => PersonKind.GuestStar,
        BaseItemPerson_Type.Director => PersonKind.Director,
        BaseItemPerson_Type.Writer => PersonKind.Writer,
        BaseItemPerson_Type.Producer => PersonKind.Producer,
        BaseItemPerson_Type.Composer => PersonKind.Composer,
        _ => PersonKind.Other,
    };

    /// <summary>
    /// Chapter marks, with the two kinds of nonsense a scan can leave behind filtered out:
    /// a mark without a position, and the unnamed ones some encoders write every ten
    /// minutes. A chapter list of "Chapter 1 … Chapter 24" is worse than none.
    /// </summary>
    private static IReadOnlyList<MediaChapter> MapChapters(List<ChapterInfo>? chapters) =>
        chapters is null ? [] : chapters
            .Where(c => c.StartPositionTicks is >= 0)
            .Select((c, i) => new MediaChapter(
                new TimeSpan(c.StartPositionTicks!.Value),
                string.IsNullOrWhiteSpace(c.Name) ? $"Chapter {i + 1}" : c.Name!,
                c.ImageTag))
            .ToList();

    /// <summary>
    /// Image tags arrive as a free-form object — <c>{"Primary": "a1b2…"}</c> — which Kiota
    /// models as an untyped <c>AdditionalData</c> bag. The value is a JSON string, but the
    /// serializer hands it over boxed, so it is read defensively rather than cast.
    /// </summary>
    internal static string? PrimaryTag(BaseItemDto dto) => Tag(dto.ImageTags?.AdditionalData, "Primary");

    private static string? Tag(IDictionary<string, object>? tags, string key)
    {
        if (tags is null || !tags.TryGetValue(key, out var value) || value is null) return null;
        return value switch
        {
            string s => s.Length > 0 ? s : null,
            System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.String => je.GetString(),
            _ => value.ToString(),
        };
    }

    internal static MediaKind MapKind(BaseItemDto_Type? type) => type switch
    {
        BaseItemDto_Type.Movie => MediaKind.Movie,
        BaseItemDto_Type.Series => MediaKind.Series,
        BaseItemDto_Type.Season => MediaKind.Season,
        BaseItemDto_Type.Episode => MediaKind.Episode,
        BaseItemDto_Type.Video or BaseItemDto_Type.MusicVideo or BaseItemDto_Type.Trailer => MediaKind.Video,
        BaseItemDto_Type.BoxSet => MediaKind.BoxSet,
        BaseItemDto_Type.Folder or BaseItemDto_Type.CollectionFolder or BaseItemDto_Type.UserView => MediaKind.Folder,
        BaseItemDto_Type.Person => MediaKind.Person,
        _ => MediaKind.Other,
    };

    internal static BaseItemKind MapKind(MediaKind kind) => kind switch
    {
        MediaKind.Movie => BaseItemKind.Movie,
        MediaKind.Series => BaseItemKind.Series,
        MediaKind.Season => BaseItemKind.Season,
        MediaKind.Episode => BaseItemKind.Episode,
        MediaKind.Video => BaseItemKind.Video,
        MediaKind.BoxSet => BaseItemKind.BoxSet,
        MediaKind.Person => BaseItemKind.Person,
        _ => BaseItemKind.Folder,
    };

    internal static LibraryKind MapLibraryKind(BaseItemDto_CollectionType? type) => type switch
    {
        BaseItemDto_CollectionType.Movies => LibraryKind.Movies,
        BaseItemDto_CollectionType.Tvshows => LibraryKind.TvShows,
        BaseItemDto_CollectionType.Music => LibraryKind.Music,
        BaseItemDto_CollectionType.Musicvideos => LibraryKind.MusicVideos,
        BaseItemDto_CollectionType.Homevideos => LibraryKind.HomeVideos,
        BaseItemDto_CollectionType.Photos => LibraryKind.Photos,
        BaseItemDto_CollectionType.Books => LibraryKind.Books,
        null or BaseItemDto_CollectionType.Unknown => LibraryKind.Mixed,
        _ => LibraryKind.Other,
    };

    internal static ItemSortBy MapSort(ItemSort sort) => sort switch
    {
        ItemSort.DateAdded => ItemSortBy.DateCreated,
        ItemSort.PremiereDate => ItemSortBy.PremiereDate,
        ItemSort.CommunityRating => ItemSortBy.CommunityRating,
        ItemSort.Runtime => ItemSortBy.Runtime,
        ItemSort.Random => ItemSortBy.Random,
        _ => ItemSortBy.SortName,
    };

    /// <summary>Ids travel through the app as 32-character hex, which is how they appear in
    /// the server's own URLs; the SDK wants GUIDs.</summary>
    internal static Guid? ParseId(string? id) =>
        Guid.TryParse(id, out var guid) ? guid : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _adapter.Dispose();
        _http.Dispose();
    }
}

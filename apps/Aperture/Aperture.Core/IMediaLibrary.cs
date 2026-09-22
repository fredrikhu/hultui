namespace Aperture.Core;

/// <summary>
/// Everything Aperture's views ask of a server, in the app's own vocabulary. One
/// implementation talks to Jellyfin through the official SDK
/// (<see cref="JellyfinLibrary"/>); another is a fixture the headless tests drive
/// (<c>Aperture.Core.Testing.FakeMediaLibrary</c>).
///
/// <para>Every call returns an <see cref="ApertureResult{T}"/> rather than throwing: a
/// server that is off, a token that expired and a film that was deleted are all things a
/// view renders, not exceptions it catches. Only the caller's own cancellation propagates.</para>
///
/// <para>Calls may be made from any thread; nothing here touches the UI.</para>
/// </summary>
public interface IMediaLibrary : IDisposable
{
    ServerInfo Server { get; }
    SignedInUser User { get; }

    /// <summary>The libraries on the home screen, in the user's own order.</summary>
    Task<ApertureResult<IReadOnlyList<LibraryView>>> GetLibrariesAsync(CancellationToken ct = default);

    /// <summary>A page of items. The workhorse: library grids, search, a season's episodes.</summary>
    Task<ApertureResult<ItemPage>> GetItemsAsync(ItemQuery query, CancellationToken ct = default);

    /// <summary>What the user stopped halfway through, most recent first.</summary>
    Task<ApertureResult<IReadOnlyList<MediaItem>>> GetResumeAsync(int limit = 12, CancellationToken ct = default);

    /// <summary>
    /// The next unwatched episode of each series in progress — or, with
    /// <paramref name="seriesId"/>, of that one series, which is what a series page's Play
    /// button plays.
    /// </summary>
    Task<ApertureResult<IReadOnlyList<MediaItem>>> GetNextUpAsync(int limit = 12, string? seriesId = null,
        CancellationToken ct = default);

    /// <summary>
    /// The episode that follows this one, across the season boundary — what auto-play
    /// plays next. Null when this was the last one, or when the item is not an episode.
    ///
    /// <para>Asked of the series rather than worked out from a season's children: only the
    /// server knows the running order once specials, missing episodes and a season that
    /// ends mid-numbering are in play.</para>
    /// </summary>
    Task<ApertureResult<MediaItem?>> GetNextEpisodeAsync(MediaItem episode, CancellationToken ct = default);

    /// <summary>Recently added items in a library, newest first.</summary>
    Task<ApertureResult<IReadOnlyList<MediaItem>>> GetLatestAsync(string libraryId, int limit = 16, CancellationToken ct = default);

    /// <summary>
    /// The genres in one library, in the server's own order. Asked per library because
    /// "Action &amp; Adventure" belongs to the shows and "Film-Noir" to the films, and a
    /// combined list offers filters that match nothing.
    /// </summary>
    Task<ApertureResult<IReadOnlyList<MediaGenre>>> GetGenresAsync(string libraryId, CancellationToken ct = default);

    /// <summary>One item in full — the detail page's overview, cast, ratings.</summary>
    Task<ApertureResult<MediaItem>> GetItemAsync(string itemId, CancellationToken ct = default);

    /// <summary>Where an image lives, for the poster cache to fetch. Never null-checked by
    /// the caller: an item with no tag simply gets no URL.</summary>
    Uri? ImageUrl(string itemId, ImageKind kind, string? tag, int? maxWidth = null, int? maxHeight = null);

    /// <summary>Opens a playback session: asks the server how this item can be played, and
    /// comes back with the URL to hand the media backend.</summary>
    Task<ApertureResult<PlaybackTarget>> StartPlaybackAsync(string itemId, TimeSpan startPosition, CancellationToken ct = default);

    /// <summary>Tells the server playback began — this is what puts the app in "now playing".</summary>
    Task<ApertureResult<bool>> ReportStartedAsync(PlaybackTarget target, TimeSpan position, bool paused, CancellationToken ct = default);

    /// <summary>The periodic heartbeat that moves the resume point. Jellyfin's own clients
    /// send this every ten seconds and on every pause, seek and rate change.</summary>
    Task<ApertureResult<bool>> ReportProgressAsync(PlaybackTarget target, TimeSpan position, bool paused, CancellationToken ct = default);

    /// <summary>Ends the session and fixes the resume point where the user left it.</summary>
    Task<ApertureResult<bool>> ReportStoppedAsync(PlaybackTarget target, TimeSpan position, CancellationToken ct = default);

    /// <summary>Marks an item watched or unwatched.</summary>
    Task<ApertureResult<bool>> SetPlayedAsync(string itemId, bool played, CancellationToken ct = default);

    /// <summary>Adds or removes the heart.</summary>
    Task<ApertureResult<bool>> SetFavoriteAsync(string itemId, bool favorite, CancellationToken ct = default);
}

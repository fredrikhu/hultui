using Aperture.Core;
using HultUI;
using HultUI.Core.Primitives;
using HultUI.Media;

namespace Aperture;

/// <summary>
/// The still under the pointer on the scrubber, and the pictures beside the chapters.
///
/// <para>Both come from work the server did in advance. Trickplay stills are packed as
/// sprite sheets — one JPEG holding a grid of small frames, one every ten seconds — so
/// scrubbing a two-hour film costs a handful of downloads rather than seven hundred. The
/// arithmetic that turns a position into a sheet and a rectangle inside it belongs to
/// <see cref="TrickplayInfo"/>; this is the plumbing around it.</para>
///
/// <para>Sheets go through the same cache as the posters. It is keyed by URL and bounded,
/// which is exactly right here: a sheet is fetched once and then answers for every
/// position it covers.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>
    /// The item the player is showing, in full. What Play() was handed comes from
    /// whichever list it was clicked in, and a list query asks for neither chapters nor
    /// trickplay — so opening a film from a shelf would have neither, while opening the
    /// same film from its detail page would have both.
    /// </summary>
    private void EnsurePlayingDetail(IMediaLibrary lib, MediaItem item)
    {
        if (item.Chapters.Count > 0 && item.Trickplay.Count > 0) return;

        _ = Task.Run(async () =>
        {
            var full = await lib.GetItemAsync(item.Id).ConfigureAwait(false);
            if (!full.Ok) return;
            Application.InvokeOnMainThread(() =>
            {
                // It may have been stopped, or something else started, while we asked.
                if (_playingItem?.Id != item.Id) return;
                _playingItem = full.Value;
                Rerender();
            });
        });
    }

    /// <summary>
    /// Which set of stills to use. The sheets come in whatever widths the server was
    /// configured to make; the smallest one at least as wide as the preview is drawn is
    /// the cheapest that still looks right, and the widest is the fallback when they are
    /// all smaller than that.
    /// </summary>
    internal static TrickplayInfo? BestTrickplay(IReadOnlyList<TrickplayInfo> options, float drawWidth)
    {
        if (options.Count == 0) return null;
        TrickplayInfo? best = null;
        foreach (var option in options)
        {
            if (option.Width < drawWidth) continue;
            if (best is null || option.Width < best.Width) best = option;
        }
        return best ?? options.OrderByDescending(o => o.Width).First();
    }

    /// <summary>
    /// The frame for a position, or null when there is none to show: no stills generated,
    /// a position past the last one, or a sheet that has not arrived yet. Called during
    /// the player's build, so the miss starts a download and says nothing — the picture
    /// appears a frame or two later, which is how the poster grid behaves too.
    /// </summary>
    internal VideoPreviewFrame? TrickplayFrame(TimeSpan position)
    {
        if (_library is not { } lib || _playingItem is not { } item) return null;
        if (BestTrickplay(item.Trickplay, PreviewWidth * _window.DisplayScale) is not { } info) return null;
        if (info.Locate(position) is not { } spot) return null;
        if (lib.TrickplayTileUrl(item.Id, info.Width, spot.Tile) is not { } url) return null;

        var (image, status) = _posters.Get(url);
        if (image is null || status != PosterCache.Status.Ready) return null;

        return new VideoPreviewFrame(
            image,
            new Rect(spot.X, spot.Y, info.Width, info.Height),
            info.Snap(position));
    }

    /// <summary>How wide the preview still is drawn, in layout units. Kept here because
    /// the width also decides which set of sheets is worth fetching.</summary>
    internal const float PreviewWidth = 176f;
}

using Aperture.Core;
using Aperture.Core.Requests;
using Aperture.Theme;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;

namespace Aperture;

/// <summary>
/// The library's own page, told what the request server knows about the same title. A
/// series you are watching is where you notice the next season is missing, so that is where
/// asking for it belongs — not three clicks away on a discovery page you would have to find
/// it on first.
///
/// <para>The join is the TMDB id Jellyfin scraped for the item (<see cref="MediaItem.TmdbId"/>).
/// An item without one — a home video, a series Jellyfin matched only on TVDB — simply gets
/// none of this.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>Asks the request server about a series on its library page. Films are left
    /// out: a film on its library page is a film you already have.</summary>
    private void EnsureLibraryRequestState(MediaItem item)
    {
        if (_requestServer is null || item.Kind != MediaKind.Series || item.TmdbId is not { } tmdb) return;
        // The same loader the discovery page uses, so opening the picker from here is a
        // cache hit there. Recommendations are not asked for: this page has no shelf for them.
        EnsureDiscoverDetail(DiscoverKind.Tv, tmdb, withRecommendations: false);
    }

    /// <summary>The request server's record of this library item, once it has arrived.</summary>
    private DiscoverDetail? LibraryRequestState(MediaItem item) =>
        item.Kind == MediaKind.Series && item.TmdbId is { } tmdb
        && DiscoverDetail is { Item: { Kind: DiscoverKind.Tv } title } detail && title.TmdbId == tmdb
            ? detail
            : null;

    /// <summary>
    /// "Request Seasons 4–5", beside Play on a series page — when the request server knows
    /// seasons the library does not have and nobody has asked for yet, and this user may ask.
    /// It opens the season picker on the title's discovery page, with those seasons ticked.
    /// </summary>
    private Element? MissingSeasonsButton(MediaItem item)
    {
        if (_requestServer is not { } server || !server.User.CanRequest(DiscoverKind.Tv)) return null;
        if (LibraryRequestState(item) is not { } detail) return null;
        var missing = RequestableSeasons(detail);
        if (missing.Count == 0) return null;

        var label = "Request " + SeasonsPhrase(missing.Select(s => s.Number).ToList());
        var button = Ui.Button(ButtonVariant.Secondary, label, () =>
        {
            OpenSeasonPicker(detail);
            Navigate(new Route(ApertureView.DiscoverTitle, detail.Item.Key, item.Name));
        }, "detail-request-seasons", Icons.Request);
        button.Tooltip($"Not in your library — ask {RequestServerTitle} for {(missing.Count == 1 ? "it" : "them")}");
        return button;
    }

    /// <summary>
    /// Seasons someone has already asked for, above the season list: waiting for approval,
    /// or on their way and how far. The library cannot show what it does not have yet, so
    /// without this a requested season is simply absent, which reads as "nobody asked".
    /// </summary>
    private Element? SeasonsUnderWay(MediaItem item)
    {
        if (LibraryRequestState(item) is not { } detail) return null;
        var waiting = detail.Seasons.Where(s => s.Availability == Availability.Pending).Select(s => s.Number).ToList();
        var coming = detail.Seasons.Where(s => s.Availability == Availability.Processing).Select(s => s.Number).ToList();
        if (waiting.Count == 0 && coming.Count == 0) return null;

        var column = Flex.Column(gap: Halide.S2).AutomationId("detail-seasons-under-way");
        if (coming.Count > 0)
        {
            var text = $"{SeasonsPhrase(coming)} on {(coming.Count == 1 ? "its" : "their")} way";
            if (DownloadSummary.Of(detail.Downloads) is { } downloading) text += " · " + downloading.Phrase;
            column.AddChild(UnderWayLine(Icons.Processing, Halide.Accent300, text));
        }
        if (waiting.Count > 0)
            column.AddChild(UnderWayLine(Icons.Pending, Halide.Accent,
                $"{SeasonsPhrase(waiting)} requested, waiting for approval"));
        return column;
    }

    private static Element UnderWayLine(string glyph, Color colour, string text)
    {
        var line = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        line.AddChild(Ui.Icon(glyph, 14f, colour));
        line.AddChild(Ui.Line(text, 12.5f, Halide.TextDim));
        return line;
    }
}

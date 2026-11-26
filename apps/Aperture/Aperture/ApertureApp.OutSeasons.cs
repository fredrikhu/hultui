using Aperture.Core;
using Aperture.Core.Requests;
using Aperture.Theme;
using HultUI;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Aperture;

/// <summary>
/// "Season 4 is out — not in your library": the request server's half of the New seasons row
/// (slice 8c). For a series the library is caught up on and still running, the request server's
/// record says which seasons have aired that nobody has asked for (<see cref="SeasonWatch.OutSeasons"/>);
/// each such series gets a card after the library's own new seasons, whose menu asks for them
/// through the usual season picker.
///
/// <para>Only with a request server connected, and only for someone allowed to ask for series:
/// otherwise the card would be news with nothing to do about it. It costs a call per caught-up,
/// running series, so it looks a minute and a half after the request server opens, then every
/// twelve hours, and on F5 — and says nothing as a toast: a season that is out is not one that
/// has arrived. What it found is kept in memory only, and found again at the next start.</para>
/// </summary>
public sealed partial class ApertureApp
{
    internal static readonly TimeSpan FirstOutLook = TimeSpan.FromSeconds(90);
    internal static readonly TimeSpan OutLookInterval = TimeSpan.FromHours(12);

    /// <summary>How far apart the request server is asked about each series — gently, as the
    /// server asks TMDB in turn. Tests set it to nothing.</summary>
    internal TimeSpan OutLookPace { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>One series' out seasons on the row: the library's series (with its artwork), the
    /// request server's record of it, and the seasons.</summary>
    internal sealed record OutSeasonCard(MediaItem Series, DiscoverDetail Detail, IReadOnlyList<DiscoverSeason> Seasons);

    private System.Threading.Timer? _outTimer;
    private bool _outLooking;
    private List<OutSeasonCard> _outCards = [];

    /// <summary>Request-server looks applied, for tests.</summary>
    internal int OutLooks { get; private set; }

    /// <summary>The seasons ticked in the season picker, for tests.</summary>
    internal IReadOnlyCollection<int> SeasonPick => _seasonPick;

    private void StartOutWatch()
    {
        StopOutWatch();
        _outTimer = new System.Threading.Timer(_ => Application.InvokeOnMainThread(() => _ = LookForOutSeasonsAsync()),
            null, FirstOutLook, OutLookInterval);
    }

    private void StopOutWatch()
    {
        _outTimer?.Dispose();
        _outTimer = null;
        _outCards = [];
        _outLooking = false;
    }

    /// <summary>One look at the request server, off the UI thread; applied on it. Quiet when it
    /// fails, as the arrival watch is.</summary>
    internal async Task LookForOutSeasonsAsync()
    {
        if (_requestServer is not { } server || _library is not { } lib || _outLooking) return;
        if (!server.User.CanRequest(DiscoverKind.Tv) || SeasonWatchState() is not { } watch) return;
        _outLooking = true;
        var skip = watch.Notices.Select(n => n.SeriesId).ToHashSet(StringComparer.Ordinal);
        IReadOnlyCollection<string> dismissed = [.. watch.Dismissed];
        IReadOnlyList<MutedSeries> muted = [.. watch.Muted];
        var pace = OutLookPace;
        var today = DateOnly.FromDateTime(DateTime.Now);

        ApertureResult<List<OutSeasonCard>> result;
        try
        {
            result = await Task.Run(async () =>
            {
                var look = await SeasonWatch.OutLookAsync(lib, server, skip, dismissed, muted, today, pace).ConfigureAwait(false);
                if (!look.Ok) return ApertureResult<List<OutSeasonCard>>.Fail(look.Failure!);
                // The library's own record of each series, for its artwork: the read above asks for none.
                var cards = new List<OutSeasonCard>();
                foreach (var finding in look.Value.Findings)
                {
                    var item = await lib.GetItemAsync(finding.Series.Id).ConfigureAwait(false);
                    cards.Add(new OutSeasonCard(item.Ok ? item.Value : finding.Series, finding.Detail, finding.Seasons));
                }
                return ApertureResult<List<OutSeasonCard>>.Success(cards);
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Disconnected while it was asking.
            Application.InvokeOnMainThread(() => _outLooking = false);
            return;
        }

        Application.InvokeOnMainThread(() =>
        {
            _outLooking = false;
            if (!result.Ok || _requestServer != server || _library != lib) return;
            OutLooks++;
            ApplyOutLook(result.Value);
        });
    }

    /// <summary>A look's cards, minus what was dismissed or muted while it was looking.</summary>
    private void ApplyOutLook(List<OutSeasonCard> cards)
    {
        if (SeasonWatchState() is not { } watch) return;
        var dismissed = watch.Dismissed.ToHashSet(StringComparer.Ordinal);
        _outCards = cards
            .Where(c => !watch.Muted.Any(m => m.Matches(c.Series.Id, c.Detail.Item.TmdbId, c.Series.TvdbId ?? c.Detail.TvdbId)))
            .Select(c => c with
            {
                Seasons = c.Seasons.Where(s => !dismissed.Contains(SeasonWatch.OutDismissKey(c.Series.Id, s.Number))).ToList(),
            })
            .Where(c => c.Seasons.Count > 0)
            // The most recently aired first.
            .OrderByDescending(c => c.Seasons.Max(s => s.AirDate ?? DateOnly.MinValue))
            .ToList();
        Rerender();
    }

    /// <summary>The cards on the row now: none for a series with a season in the library on it
    /// already (that is the thing to watch), nor once the request server has gone.</summary>
    internal IReadOnlyList<OutSeasonCard> OutSeasonCards
    {
        get
        {
            if (_outCards.Count == 0 || _requestServer is null) return [];
            var notices = NewSeasonNotices;
            if (notices.Count == 0) return _outCards;
            return _outCards.Where(c => !notices.Any(n => n.SeriesId == c.Series.Id)).ToList();
        }
    }

    /// <summary>The out-season card whose series <paramref name="item"/> is, or null.</summary>
    internal OutSeasonCard? OutCardFor(MediaItem item)
    {
        if (item.Kind != MediaKind.Series || _outCards.Count == 0) return null;
        foreach (var card in OutSeasonCards)
            if (card.Series.Id == item.Id) return card;
        return null;
    }

    /// <summary>"Season 4 · 10 episodes" — under the series' name on its card.</summary>
    internal static string OutSeasonLine(OutSeasonCard card)
    {
        int episodes = card.Seasons.Sum(s => s.EpisodeCount);
        return $"{SeasonsPhrase(card.Seasons.Select(s => s.Number).ToList())} · {episodes} episode{(episodes == 1 ? "" : "s")}";
    }

    /// <summary>"out 5 days ago" — when the newest of them first aired.</summary>
    internal static string OutSeasonAired(OutSeasonCard card, DateTimeOffset? now = null)
    {
        var aired = card.Seasons.Max(s => s.AirDate ?? DateOnly.MinValue);
        return "out " + Ui.Ago(new DateTimeOffset(aired.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), now);
    }

    /// <summary>
    /// An out season's card: the series' poster marked "Not in library", its name and the two lines
    /// under it. Enter (and Space, as for any series) opens the series' page, where the Request button
    /// already is; its menu asks for the seasons directly.
    /// </summary>
    private BoxElement OutSeasonCardElement(MediaItem series, OutSeasonCard card, int cursorIndex, CardArt picture)
    {
        float width = Halide.PosterWidth;
        float height = MathF.Round(width * 1.5f);
        var line = OutSeasonLine(card);
        var aired = OutSeasonAired(card);
        var element = Flex.Column(gap: Halide.S2)
            .Width(width).FlexShrink(0)
            .CornerRadius(Halide.Radius2)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName($"{series.Name}: {line.Replace(" · ", ", ")}, not in your library, {aired}")
            .AutomationId("out-season-" + series.Id)
            .Tooltip($"{series.Name} — not in your library yet");
        MarkCursor(element, cursorIndex, series);

        var art = Artwork(series, width, height, Halide.Radius2, picture);
        art.AddChild(NewSeasonMark("Not in library", quiet: true));
        element.AddChild(art);

        if (_settings.Data.ShowCardTitles)
        {
            var caption = Flex.Column(gap: 1).Width(width);
            caption.AddChild(Ui.Line(series.Name, 12.5f, Halide.Text));
            caption.AddChild(Ui.Line(line, 11.5f, Halide.TextMuted));
            caption.AddChild(Ui.Line(aired, 11.5f, Halide.TextFaint));
            element.AddChild(caption);
        }
        element.OnClick(_ => Open(series));
        return element;
    }

    /// <summary>What an out season's card adds to its menu: ask for it, dismiss it, or stop watching.</summary>
    private void AddOutSeasonMenu(List<ContextMenuItem> items, OutSeasonCard card)
    {
        items.Add(ContextMenuItem.Separator());
        if (_requestServer?.User.CanRequest(DiscoverKind.Tv) == true)
            items.Add(new ContextMenuItem
            {
                Label = $"Request {SeasonsPhrase(card.Seasons.Select(s => s.Number).ToList())}…",
                Action = () => RequestOutSeasons(card),
            });
        items.Add(new ContextMenuItem { Label = "Dismiss", Action = () => DismissOutSeasons([card]) });
        items.Add(new ContextMenuItem
        {
            Label = $"Stop watching {card.Series.Name} for new seasons",
            Action = () => MuteSeries(new SeasonNotice
            {
                SeriesId = card.Series.Id,
                SeriesName = card.Series.Name,
                TmdbId = card.Detail.Item.TmdbId,
                TvdbId = card.Series.TvdbId ?? card.Detail.TvdbId,
            }),
        });
    }

    /// <summary>The season picker on the title's page, with just these seasons ticked (as many as
    /// the allowance covers) — not every gap the server could fill as well.</summary>
    private void RequestOutSeasons(OutSeasonCard card)
    {
        OpenSeasonPicker(card.Detail);
        var allowance = QuotaFor(DiscoverKind.Tv) is { } quota ? (quota.Restricted ? 0 : quota.Remaining) : null;
        _seasonPick.Clear();
        foreach (var season in card.Seasons.Take(allowance ?? int.MaxValue)) _seasonPick.Add(season.Number);
        Navigate(new Route(ApertureView.DiscoverTitle, card.Detail.Item.Key, card.Series.Name));
    }

    /// <summary>Takes out-season cards off the row; their seasons are not said again. The same season
    /// arriving in the library later is news all the same.</summary>
    private void DismissOutSeasons(IReadOnlyList<OutSeasonCard> cards)
    {
        if (SeasonWatchState() is not { } watch || cards.Count == 0) return;
        KeepCursorPlaceIfOnOut(cards);
        foreach (var card in cards)
        {
            foreach (var season in card.Seasons)
            {
                var key = SeasonWatch.OutDismissKey(card.Series.Id, season.Number);
                if (!watch.Dismissed.Contains(key, StringComparer.Ordinal)) watch.Dismissed.Add(key);
            }
            _outCards.RemoveAll(c => c.Series.Id == card.Series.Id);
        }
        if (watch.Dismissed.Count > DismissedKept) watch.Dismissed.RemoveRange(0, watch.Dismissed.Count - DismissedKept);
        _settings.Save();
        Rerender();
    }

    /// <summary>A request made for a series takes its out card off the row: it has been acted on.</summary>
    private void ForgetOutSeasons(int tmdbId)
    {
        if (_outCards.RemoveAll(c => c.Detail.Item.TmdbId == tmdbId) > 0) Rerender();
    }

    private void KeepCursorPlaceIfOnOut(IReadOnlyList<OutSeasonCard> cards)
    {
        if (Current.View != ApertureView.Home || CursorItem is not { } item) return;
        if (OutCardFor(item) is { } on && cards.Any(c => c.Series.Id == on.Series.Id))
            _homeCursorAfterRemoval = _cursorIndex;
    }
}

using Aperture.Core;
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
/// "New seasons": series whose every other season had been watched when a new one arrived. The
/// watch (<see cref="SeasonWatch"/>) looks on a slow timer — the server has no push for this either
/// — and on F5 or after a film; what it finds is a row on Home, whose card is the new season's
/// first episode, so Space plays it as on Next up.
///
/// <para>The notices, the dismissed seasons and the muted series are kept in the settings file per
/// server and user (<see cref="SeasonWatchAccount"/>); what each series had at the last look is in a
/// cache file beside it (<see cref="SeasonBaselineStore"/>). A card leaves the row by itself once an
/// episode of its season is watched, or by hand: Dismiss on the card, Clear all on the row.
/// "Stop watching" mutes the series for good, by its provider ids as well as its Jellyfin id, so a
/// library added again does not unmute it; Settings lists the muted ones with a way back.</para>
/// </summary>
public sealed partial class ApertureApp
{
    internal const string NewSeasonsRowKey = "newseasons";

    /// <summary>How long after the library opens the first look is taken — after Home has
    /// settled, and after the arrival watch's own first look.</summary>
    internal static readonly TimeSpan FirstSeasonLook = TimeSpan.FromSeconds(30);

    /// <summary>How often after that. Seasons arrive a few times a week at most.</summary>
    internal static readonly TimeSpan SeasonLookInterval = TimeSpan.FromMinutes(30);

    /// <summary>Dismissed seasons kept: a season is only ever new once, so the newest few hundred
    /// are plenty.</summary>
    private const int DismissedKept = 200;

    private System.Threading.Timer? _seasonTimer;

    /// <summary>Set by the timer's first tick and by F5: until then no look is taken, so opening
    /// the app asks the server for Home first.</summary>
    private bool _seasonLookArmed;
    private int _seasonLookTick;

    /// <summary>The look last asked for — library, data generation, tick — compared field by field:
    /// every browsing build asks, and a key made to compare would be made each time.</summary>
    private IMediaLibrary? _seasonLookAskedOf;
    private int _seasonLookAskedGeneration;
    private int _seasonLookAskedTick;
    private readonly AsyncLoader<SeasonLook?> _seasonLook = new(null);

    /// <summary>The watch state of the library it was looked up for: Home reads it each build.</summary>
    private SeasonWatchAccount? _seasonWatch;
    private IMediaLibrary? _seasonWatchOf;

    /// <summary>Looks applied, for tests.</summary>
    internal int SeasonLooks { get; private set; }

    /// <summary>The new seasons' first episodes, by the season's id — the row's cards.</summary>
    private readonly AsyncLoader<IReadOnlyDictionary<string, MediaItem>> _newSeasonCards =
        new(new Dictionary<string, MediaItem>(StringComparer.Ordinal));
    private int _newSeasonCardsGeneration = -1;

    /// <summary>Where the cursor was when its card was dismissed: Home puts it back there, on the
    /// card that took its place, rather than nowhere.</summary>
    private int? _homeCursorAfterRemoval;

    private void StartSeasonWatch()
    {
        StopSeasonWatch();
        _seasonTimer = new System.Threading.Timer(_ => Application.InvokeOnMainThread(LookForNewSeasons),
            null, FirstSeasonLook, SeasonLookInterval);
    }

    private void StopSeasonWatch()
    {
        _seasonTimer?.Dispose();
        _seasonTimer = null;
        _seasonLookArmed = false;
        _seasonLookAskedOf = null;
    }

    /// <summary>The watch's tick, and F5's: look now.</summary>
    internal void LookForNewSeasons()
    {
        _seasonLookArmed = true;
        _seasonLookTick++;
        Rerender();
    }

    /// <summary>This server's and user's watch state, or null while signed out.</summary>
    private SeasonWatchAccount? SeasonWatchState()
    {
        if (_library is not { } lib) return null;
        if (!ReferenceEquals(_seasonWatchOf, lib))
        {
            _seasonWatch = _settings.SeasonWatchFor(lib.Server.Id, lib.User.Id.ToString("N"));
            _seasonWatchOf = lib;
        }
        return _seasonWatch;
    }

    /// <summary>The notices on the row now.</summary>
    internal IReadOnlyList<SeasonNotice> NewSeasonNotices => SeasonWatchState()?.Notices ?? [];

    /// <summary>The series muted for new seasons, for Settings.</summary>
    internal IReadOnlyList<MutedSeries> MutedForNewSeasons => SeasonWatchState()?.Muted ?? [];

    /// <summary>Takes a look when one is due: called from every browsing build, a cache hit unless
    /// the tick or the data generation (F5, a film stopping) moved. Quiet when it fails, as the
    /// arrival watch is: the next look tries again.</summary>
    private void EnsureSeasonWatch()
    {
        if (!_seasonLookArmed || _library is not { } lib || SeasonWatchState() is not { } watch) return;
        if (ReferenceEquals(_seasonLookAskedOf, lib) && _seasonLookAskedGeneration == _dataGeneration
            && _seasonLookAskedTick == _seasonLookTick) return;
        _seasonLookAskedOf = lib;
        _seasonLookAskedGeneration = _dataGeneration;
        _seasonLookAskedTick = _seasonLookTick;
        var key = ("season-look", lib.Server.Id, lib.User.Id, _dataGeneration, _seasonLookTick);

        // What the look compares against, as it is now; what the user does while it looks is
        // applied over the result (ApplySeasonLook).
        IReadOnlyList<SeasonNotice> notices = [.. watch.Notices];
        IReadOnlyCollection<string> dismissed = [.. watch.Dismissed];
        IReadOnlyList<MutedSeries> muted = [.. watch.Muted];
        var store = new SeasonBaselineStore(_settings.Folder, lib.Server.Id, lib.User.Id.ToString("N"));
        _seasonLook.Load(key, () =>
        {
            var result = SeasonWatch.LookAsync(lib, store.Load(), notices, dismissed, muted, DateTimeOffset.UtcNow)
                .GetAwaiter().GetResult();
            if (!result.Ok) return null;
            // What the library holds, not what the user chose: kept whether or not the result is.
            store.Save(result.Value.Baseline);
            return result.Value;
        }, look =>
        {
            if (look is not null && _library == lib) ApplySeasonLook(look);
        });
    }

    /// <summary>A look's notices, minus anything dismissed or muted while it was looking.</summary>
    private void ApplySeasonLook(SeasonLook look)
    {
        if (SeasonWatchState() is not { } watch) return;
        SeasonLooks++;
        var dismissed = watch.Dismissed.ToHashSet(StringComparer.Ordinal);
        var kept = look.Notices
            .Where(n => !watch.Muted.Any(m => m.Matches(n.SeriesId, n.TmdbId, n.TvdbId)))
            .Where(n => n.Seasons.Any(s => !dismissed.Contains(SeasonWatch.DismissKey(n.SeriesId, s))))
            .ToList();
        if (SameNotices(watch.Notices, kept)) return;
        watch.Notices = kept;
        _settings.Save();
        Rerender();
    }

    private static bool SameNotices(IReadOnlyList<SeasonNotice> a, IReadOnlyList<SeasonNotice> b) =>
        a.Count == b.Count && a.Zip(b).All(p =>
            p.First.SeriesId == p.Second.SeriesId && p.First.FirstSeasonId == p.Second.FirstSeasonId
            && p.First.Episodes == p.Second.Episodes && p.First.Added == p.Second.Added
            && p.First.SeriesName == p.Second.SeriesName && p.First.Seasons.SequenceEqual(p.Second.Seasons));

    // ── The row ──────────────────────────────────────────────────────────

    /// <summary>Asks for each notice's card — the first episode of its first new season — when one
    /// is missing or the data generation moved (F5, a film stopping: the card's watched state).</summary>
    private void EnsureNewSeasonCards()
    {
        if (_library is not { } lib) return;
        var notices = NewSeasonNotices;
        if (notices.Count == 0) return;
        var wanted = notices.Select(n => n.FirstSeasonId).Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var have = _newSeasonCards.Value;
        if (_newSeasonCardsGeneration == _dataGeneration && wanted.All(have.ContainsKey)) return;

        int generation = _dataGeneration;
        _newSeasonCards.Load(("new-season-cards", generation, string.Join(",", wanted)), () =>
        {
            var map = new Dictionary<string, MediaItem>(StringComparer.Ordinal);
            foreach (var seasonId in wanted)
            {
                // A season's episodes in their own order, as the season's page lists them.
                var page = lib.GetItemsAsync(new ItemQuery
                {
                    ParentId = seasonId,
                    Kinds = [MediaKind.Episode],
                    Recursive = false,
                    Sort = ItemSort.Name,
                    Limit = 1,
                }).GetAwaiter().GetResult();
                if (page.Ok && page.Value.Items.Count > 0) map[seasonId] = page.Value.Items[0];
            }
            return (IReadOnlyDictionary<string, MediaItem>)map;
        }, _ => _newSeasonCardsGeneration = generation);
    }

    /// <summary>The row's cards, in the notices' order (newest arrival first); a notice whose
    /// episode has not loaded yet is not on it yet.</summary>
    internal IReadOnlyList<MediaItem> NewSeasonItems
    {
        get
        {
            var notices = NewSeasonNotices;
            if (notices.Count == 0) return [];
            var cards = _newSeasonCards.Value;
            var items = new List<MediaItem>(notices.Count);
            foreach (var notice in notices)
                if (cards.TryGetValue(notice.FirstSeasonId, out var episode)) items.Add(episode);
            return items;
        }
    }

    /// <summary>The notice whose card <paramref name="item"/> is, or null.</summary>
    internal SeasonNotice? NoticeFor(MediaItem item)
    {
        if (item.Kind != MediaKind.Episode) return null;
        var cards = _newSeasonCards.Value;
        foreach (var notice in NewSeasonNotices)
            if (cards.TryGetValue(notice.FirstSeasonId, out var episode) && episode.Id == item.Id) return notice;
        return null;
    }

    /// <summary>"Season 3 · 8 episodes" — under the series' name on its card.</summary>
    internal static string NewSeasonLine(SeasonNotice notice) =>
        $"{SeasonsPhrase(notice.Seasons)} · {notice.Episodes} episode{(notice.Episodes == 1 ? "" : "s")}";

    /// <summary>"added 2 days ago" — the card's last line, on its own: the three together do not fit
    /// a poster's width.</summary>
    internal static string NewSeasonAdded(SeasonNotice notice, DateTimeOffset? now = null) =>
        "added " + Ui.Ago(notice.Added, now);

    /// <summary>
    /// A new season's card: the poster-sized artwork of its first episode with a "New season" mark,
    /// the series' name and the line under it. Enter opens the episode and Space plays it, as on a
    /// Next up card; its menu adds Dismiss and Stop watching.
    /// </summary>
    private BoxElement NewSeasonCard(MediaItem item, SeasonNotice notice, int cursorIndex,
        HultUI.Core.Rendering.IImageSource? picture)
    {
        const float width = Halide.PosterWidth;
        float height = MathF.Round(width * 1.5f);
        var line = NewSeasonLine(notice);
        var added = NewSeasonAdded(notice);
        var series = notice.SeriesName.Length > 0 ? notice.SeriesName : item.SeriesName ?? item.Name;
        var card = Flex.Column(gap: Halide.S2)
            .Width(width).FlexShrink(0)
            .CornerRadius(Halide.Radius2)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName($"{series}: {line.Replace(" · ", ", ")}, new, {added}")
            .AutomationId("new-season-" + notice.SeriesId)
            .Tooltip($"{series} — {SeasonsPhrase(notice.Seasons)} is new");
        MarkCursor(card, cursorIndex, item);

        var art = Artwork(item, width, height, Halide.Radius2, picture);
        art.AddChild(NewSeasonMark(notice.Seasons.Count));
        card.AddChild(art);

        if (_settings.Data.ShowCardTitles)
        {
            var caption = Flex.Column(gap: 1).Width(width);
            caption.AddChild(Ui.Line(series, 12.5f, Halide.Text));
            caption.AddChild(Ui.Line(line, 11.5f, Halide.TextMuted));
            caption.AddChild(Ui.Line(added, 11.5f, Halide.TextFaint));
            card.AddChild(caption);
        }
        card.OnClick(_ => Open(item));
        return card;
    }

    /// <summary>The accent mark in the artwork's corner: a mark, not a surface, as the watched tick is.</summary>
    private static Element NewSeasonMark(int seasons)
    {
        var mark = Box.New()
            .Padding(new Thickness(2, 7, 2, 7))
            .CornerRadius(Halide.Radius1)
            .Background(Halide.Accent);
        mark.AddChild(Ui.Line(seasons == 1 ? "New season" : "New seasons", 10.5f, new Color(35, 23, 4)));
        mark.Position.Type = PositionType.Absolute;
        mark.Position.Left = Dimension.Px(Halide.S2);
        mark.Position.Top = Dimension.Px(Halide.S2);
        return mark;
    }

    /// <summary>What a new season's card adds to its menu.</summary>
    private void AddNewSeasonMenu(List<ContextMenuItem> items, SeasonNotice notice)
    {
        var series = notice.SeriesName.Length > 0 ? notice.SeriesName : "this series";
        items.Add(ContextMenuItem.Separator());
        items.Add(new ContextMenuItem { Label = "Dismiss", Action = () => DismissNewSeason(notice) });
        items.Add(new ContextMenuItem
        {
            Label = $"Stop watching {series} for new seasons",
            Action = () => MuteSeries(notice),
        });
    }

    // ── Dismissing and muting ────────────────────────────────────────────

    /// <summary>Takes a notice off the row; its seasons are never said again. A later season of the
    /// same series still is.</summary>
    internal void DismissNewSeason(SeasonNotice notice) => Dismiss([notice]);

    /// <summary>The row's "Clear all".</summary>
    internal void DismissAllNewSeasons() => Dismiss([.. NewSeasonNotices]);

    private void Dismiss(IReadOnlyList<SeasonNotice> notices)
    {
        if (SeasonWatchState() is not { } watch || notices.Count == 0) return;
        KeepCursorPlaceIfOn(notices);
        foreach (var notice in notices)
        {
            foreach (var season in notice.Seasons)
            {
                var key = SeasonWatch.DismissKey(notice.SeriesId, season);
                if (!watch.Dismissed.Contains(key, StringComparer.Ordinal)) watch.Dismissed.Add(key);
            }
            watch.Notices.RemoveAll(n => n.SeriesId == notice.SeriesId);
        }
        if (watch.Dismissed.Count > DismissedKept) watch.Dismissed.RemoveRange(0, watch.Dismissed.Count - DismissedKept);
        _settings.Save();
        Rerender();
    }

    /// <summary>Never tell about this series again, whatever arrives — matched on its provider ids
    /// too, so a library added again (new Jellyfin ids) does not bring it back.</summary>
    internal void MuteSeries(SeasonNotice notice)
    {
        if (SeasonWatchState() is not { } watch) return;
        KeepCursorPlaceIfOn([notice]);
        if (!watch.Muted.Any(m => m.Matches(notice.SeriesId, notice.TmdbId, notice.TvdbId)))
            watch.Muted.Add(new MutedSeries
            {
                SeriesId = notice.SeriesId,
                Name = notice.SeriesName,
                TmdbId = notice.TmdbId,
                TvdbId = notice.TvdbId,
            });
        var muted = watch.Muted;
        watch.Notices.RemoveAll(n => muted.Any(m => m.Matches(n.SeriesId, n.TmdbId, n.TvdbId)));
        _settings.Save();
        Flash($"No more new-season news about {(notice.SeriesName.Length > 0 ? notice.SeriesName : "that series")} — Settings can undo it");
        Rerender();
    }

    /// <summary>Settings' "Watch again": seasons that arrive from now on are news again. Those that
    /// arrived while it was muted are not — they are already in what the watch last saw.</summary>
    internal void UnmuteSeries(MutedSeries series)
    {
        if (SeasonWatchState() is not { } watch || !watch.Muted.Remove(series)) return;
        _settings.Save();
        Rerender();
    }

    /// <summary>When the cursor is on one of these notices' cards, Home's next build puts it back at
    /// the same place — on the card that moves up into it.</summary>
    private void KeepCursorPlaceIfOn(IReadOnlyList<SeasonNotice> notices)
    {
        if (Current.View != ApertureView.Home || CursorItem is not { } item) return;
        if (NoticeFor(item) is { } on && notices.Any(n => n.SeriesId == on.SeriesId))
            _homeCursorAfterRemoval = _cursorIndex;
    }

    // ── Settings ─────────────────────────────────────────────────────────

    /// <summary>The series muted for new seasons, each with a way back — a mute that left no trace
    /// would read as a broken feature the day the next season arrives in silence.</summary>
    private Element NewSeasonsSettingsCard()
    {
        var card = Card("New seasons");
        card.AutomationId("settings-new-seasons");
        var muted = MutedForNewSeasons;
        if (muted.Count == 0)
        {
            card.AddChild(Text.New("Every series is watched for new seasons. A card's menu on Home's "
                    + "New seasons row stops watching one.")
                .FontSize(11.5f).TextColor(Halide.TextFaint));
            return card;
        }

        card.AddChild(Text.New("Not watched for new seasons. Watching one again tells you about seasons "
                + "that arrive from now on.")
            .FontSize(11.5f).TextColor(Halide.TextFaint));
        foreach (var series in muted)
        {
            var row = Flex.Row(gap: Halide.S4).AlignItems(AlignItems.Center);
            row.AddChild(Ui.Line(series.Name.Length > 0 ? series.Name : series.SeriesId, 12.5f, Halide.TextDim)
                .FlexGrow(1).FlexShrink(1));
            var one = series;
            row.AddChild(Ui.Button(ButtonVariant.Secondary, "Watch again", () => UnmuteSeries(one),
                "settings-unmute-" + series.SeriesId));
            card.AddChild(row);
        }
        return card;
    }
}

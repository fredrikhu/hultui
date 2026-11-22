using HultUI.Core.Storage;

namespace Aperture.Core;

/// <summary>
/// One season as the watch sees it: its number, how many episodes have a file, how many of those
/// are unwatched, and when it came into the library.
/// </summary>
public readonly record struct SeasonState(string Id, int? Number, int Files, int Unwatched, DateTimeOffset? Added)
{
    /// <summary>A numbered season (not the specials) with at least one episode file — the only kind
    /// that counts either way. An empty season folder is not a season yet.</summary>
    public bool Regular => Number is > 0 && Files > 0;

    /// <summary>Every episode with a file watched.</summary>
    public bool Watched => Files > 0 && Unwatched == 0;

    /// <summary>Not one episode of it watched.</summary>
    public bool Untouched => Files > 0 && Unwatched >= Files;

    /// <summary>A season item as the server reported it with the watch's fields, or null for
    /// anything else.</summary>
    public static SeasonState? From(MediaItem item)
    {
        if (item.Kind != MediaKind.Season) return null;
        int files = item.FileCount ?? item.ChildCount ?? 0;
        int unwatched = item.UnplayedItemCount ?? (item.Played ? 0 : files);
        return new SeasonState(item.Id, item.IndexNumber, files, unwatched, item.DateAdded);
    }
}

/// <summary>
/// What every series had at the last look: its numbered seasons with a file, and the newest
/// "episode added" date the look saw — the next look asks only about series newer than that. Kept
/// in a cache file (<see cref="SeasonBaselineStore"/>); losing it makes the next look a first look.
/// </summary>
public sealed class SeasonBaseline
{
    public DateTimeOffset? Watermark { get; set; }
    public Dictionary<string, List<int>> Seasons { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>The baseline for one server and user, in a file beside the settings.</summary>
public sealed class SeasonBaselineStore
{
    private readonly JsonFile<SeasonBaseline> _file;

    public SeasonBaselineStore(string folder, string serverId, string userId) =>
        _file = new JsonFile<SeasonBaseline>(System.IO.Path.Combine(folder, $"season-watch-{Safe(serverId)}-{Safe(userId)}.json"),
            ApertureJsonContext.Default.SeasonBaseline);

    public string Path => _file.Path;

    public SeasonBaseline? Load()
    {
        var loaded = _file.Load();
        if (loaded is not null) loaded.Seasons ??= new(StringComparer.Ordinal);
        return loaded;
    }

    public bool Save(SeasonBaseline baseline) => _file.Save(baseline);

    private static string Safe(string id) =>
        string.Concat(id.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
}

/// <summary>What one look found: the baseline to keep, the notices standing now (newest first),
/// those among them that are news since the last look, whether it read every season, and how many
/// calls it made.</summary>
public sealed record SeasonLook(SeasonBaseline Baseline, IReadOnlyList<SeasonNotice> Notices,
    IReadOnlyList<SeasonNotice> Arrived, bool Swept, int Calls);

/// <summary>
/// The new-season watch: a series whose every other season had been watched when one or more new
/// seasons arrived, none of them watched yet.
///
/// <para><b>New</b> means a numbered season with a file that was not there at the previous look.
/// Season numbers are compared, not dates: a re-scan or a re-added library can rewrite the server's
/// "date added", season numbers do not move. Dates are used once, at the first look, when there is
/// nothing to compare with: a season added in the last <see cref="FirstLookWindow"/> counts.</para>
///
/// <para><b>Caught up</b> is judged per season from the server's own watched state, for whoever is
/// signed in: every other numbered season fully watched. The specials count neither way.</para>
///
/// <para>A look costs one small call — the series by "date episode added", newest first — and one
/// more per series that changed since the last look or has a notice standing. The first look, a
/// lost cache, a server that gives no dates, and a change too wide for that one call (a re-scan)
/// read every series and every season instead, a page of <see cref="SweepPage"/> at a time.</para>
/// </summary>
public static class SeasonWatch
{
    /// <summary>At the first look, how recently a season must have been added to count as new.</summary>
    public static readonly TimeSpan FirstLookWindow = TimeSpan.FromDays(14);

    /// <summary>How many series each look asks about. More than this changed at once is a re-scan,
    /// and one sweep is cheaper than a call per series.</summary>
    public const int ChangedProbe = 25;

    /// <summary>Items a page of a sweep asks for.</summary>
    public const int SweepPage = 1000;

    /// <summary>Seasons asked for when one series is looked at — far more than any series has.</summary>
    internal const int SeriesSeasonLimit = 500;

    /// <summary>
    /// The new seasons of one series, in order, or null when there is nothing to tell.
    /// </summary>
    /// <param name="seasons">The series' seasons now.</param>
    /// <param name="before">The numbered seasons it had at the last look, or null when the last
    /// look did not see the series (or there was none).</param>
    /// <param name="firstLook">No look has been made before: dates decide what is new.</param>
    /// <param name="allowed">Seasons that may stay unwatched without spoiling "caught up": those
    /// already on a notice, and those the user dismissed. They join the result while untouched.</param>
    public static IReadOnlyList<SeasonState>? NewSeasons(IReadOnlyList<SeasonState> seasons,
        IReadOnlyCollection<int>? before, bool firstLook, DateTimeOffset now, IReadOnlyCollection<int> allowed)
    {
        // Episodes in no numbered season mean the series is numbered some other way (straight
        // through, or "Season Unknown"): which season is new means nothing there.
        if (seasons.Any(s => s.Number is null && s.Files > 0)) return null;

        var regular = seasons.Where(s => s.Regular).ToList();
        var added = before is not null ? regular.Where(s => !before.Contains(s.Number!.Value)).ToList()
            : firstLook ? regular.Where(s => s.Added is { } at && now - at <= FirstLookWindow).ToList()
            : [];
        // Nothing new, or something new already started: there is nothing to tell.
        if (added.Count == 0 || added.Any(s => !s.Untouched)) return null;

        var fresh = regular.Where(s => added.Contains(s) || (allowed.Contains(s.Number!.Value) && s.Untouched)).ToList();
        var rest = regular.Where(s => !fresh.Contains(s)).ToList();
        // Caught up: there was something before, and all of it is watched.
        if (rest.Count == 0 || rest.Any(s => !s.Watched)) return null;
        // A season numbered below one already watched is a gap filled in (seasons 1–3 downloaded
        // after season 5 was watched), not more of the series: never news, and while it stays
        // unwatched the series is not caught up either.
        int lastWatched = rest.Max(s => s.Number!.Value);
        if (fresh.Any(s => s.Number!.Value < lastWatched)) return null;
        return fresh;
    }

    /// <summary>Whether a standing notice still stands: every season on it still there, and not one
    /// episode of them watched.</summary>
    public static bool StillNew(SeasonNotice notice, IReadOnlyList<SeasonState> seasons) =>
        notice.Seasons.Count > 0
        && notice.Seasons.All(n => seasons.Any(s => s.Number == n && s.Regular && s.Untouched));

    /// <summary>The key a dismissed season is kept under.</summary>
    public static string DismissKey(string seriesId, int season) =>
        seriesId + ":" + season.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// One look. Reads the library and nothing else: it neither saves the baseline nor touches the
    /// settings — the caller applies the result on its own thread, against what the user did while
    /// it was looking.
    /// </summary>
    /// <param name="baseline">What the last look saw, or null for a first look.</param>
    /// <param name="notices">Notices standing now.</param>
    /// <param name="dismissed">Dismissed seasons, as <see cref="DismissKey"/>s.</param>
    /// <param name="muted">Series never to be told about.</param>
    public static async Task<ApertureResult<SeasonLook>> LookAsync(IMediaLibrary library, SeasonBaseline? baseline,
        IReadOnlyList<SeasonNotice> notices, IReadOnlyCollection<string> dismissed, IReadOnlyList<MutedSeries> muted,
        DateTimeOffset now, CancellationToken ct = default)
    {
        int calls = 0;
        bool firstLook = baseline is null;

        var probe = await library.GetSeriesByEpisodeAddedAsync(0, ChangedProbe, ct).ConfigureAwait(false);
        calls++;
        if (!probe.Ok) return ApertureResult<SeasonLook>.Fail(probe.Failure!);

        var series = new Dictionary<string, MediaItem>(StringComparer.Ordinal);
        foreach (var s in probe.Value.Items) series[s.Id] = s;
        var watermark = baseline?.Watermark;
        var probed = probe.Value.Items;
        var changed = probed.Where(s => s.DateLastMediaAdded is { } at && (watermark is null || at > watermark)).ToList();
        // A server that gives no dates cannot say what changed: every look reads everything.
        bool datesKnown = probed.Count == 0 || probed.Any(s => s.DateLastMediaAdded is not null);
        bool sweep = firstLook || !datesKnown || (probed.Count >= ChangedProbe && changed.Count == probed.Count);

        var seasonsOf = new Dictionary<string, List<MediaItem>>(StringComparer.Ordinal);
        if (sweep)
        {
            for (int start = 0; ; start += SweepPage)
            {
                var page = await library.GetSeriesByEpisodeAddedAsync(start, SweepPage, ct).ConfigureAwait(false);
                calls++;
                if (!page.Ok) return ApertureResult<SeasonLook>.Fail(page.Failure!);
                foreach (var s in page.Value.Items)
                {
                    series[s.Id] = s;
                    seasonsOf.TryAdd(s.Id, []);
                }
                if (page.Value.Items.Count == 0 || start + page.Value.Items.Count >= page.Value.TotalCount) break;
            }
            for (int start = 0; ; start += SweepPage)
            {
                var page = await library.GetSeasonsAsync(null, start, SweepPage, ct).ConfigureAwait(false);
                calls++;
                if (!page.Ok) return ApertureResult<SeasonLook>.Fail(page.Failure!);
                foreach (var season in page.Value.Items)
                {
                    if (season.SeriesId is not { Length: > 0 } id) continue;
                    if (!seasonsOf.TryGetValue(id, out var list)) seasonsOf[id] = list = [];
                    list.Add(season);
                }
                if (page.Value.Items.Count == 0 || start + page.Value.Items.Count >= page.Value.TotalCount) break;
            }
        }
        else
        {
            var ids = changed.Select(s => s.Id).Concat(notices.Select(n => n.SeriesId)).Distinct(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var page = await library.GetSeasonsAsync(id, 0, SeriesSeasonLimit, ct).ConfigureAwait(false);
                calls++;
                if (!page.Ok) return ApertureResult<SeasonLook>.Fail(page.Failure!);
                seasonsOf[id] = [.. page.Value.Items];
            }
        }

        var next = new SeasonBaseline
        {
            Seasons = sweep || baseline is null
                ? new(StringComparer.Ordinal)
                : new(baseline.Seasons, StringComparer.Ordinal),
            Watermark = Newest(watermark, series.Values.Select(s => s.DateLastMediaAdded)),
        };

        var standing = new Dictionary<string, SeasonNotice>(StringComparer.Ordinal);
        foreach (var n in notices) standing[n.SeriesId] = n;
        var gone = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var key in dismissed)
        {
            int colon = key.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(key.AsSpan(colon + 1), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var number)) continue;
            var id = key[..colon];
            if (!gone.TryGetValue(id, out var set)) gone[id] = set = [];
            set.Add(number);
        }

        var result = new List<SeasonNotice>();
        var arrived = new List<SeasonNotice>();
        foreach (var (id, items) in seasonsOf)
        {
            var states = items.Select(SeasonState.From).OfType<SeasonState>().ToList();
            next.Seasons[id] = states.Where(s => s.Regular).Select(s => s.Number!.Value).Distinct().Order().ToList();

            var info = series.GetValueOrDefault(id);
            var old = standing.GetValueOrDefault(id);
            int? tmdb = info?.TmdbId ?? old?.TmdbId;
            int? tvdb = info?.TvdbId ?? old?.TvdbId;
            if (muted.Any(m => m.Matches(id, tmdb, tvdb))) continue;

            var dismissedHere = gone.GetValueOrDefault(id) ?? [];
            var allowed = new HashSet<int>(dismissedHere);
            if (old is not null) allowed.UnionWith(old.Seasons);
            IReadOnlyCollection<int>? before = baseline is not null && baseline.Seasons.TryGetValue(id, out var was) ? was : null;

            var fresh = NewSeasons(states, before, firstLook, now, allowed)?
                .Where(s => !dismissedHere.Contains(s.Number!.Value)).ToList();
            if (fresh is { Count: > 0 })
            {
                var notice = new SeasonNotice
                {
                    SeriesId = id,
                    SeriesName = info?.Name ?? old?.SeriesName ?? items.FirstOrDefault()?.SeriesName ?? "",
                    Seasons = fresh.Select(s => s.Number!.Value).ToList(),
                    FirstSeasonId = fresh[0].Id,
                    Episodes = fresh.Sum(s => s.Files),
                    Added = Newest(null, fresh.Select(s => s.Added)) ?? old?.Added ?? now,
                    TmdbId = tmdb,
                    TvdbId = tvdb,
                };
                result.Add(notice);
                if (old is null || fresh.Any(s => !old.Seasons.Contains(s.Number!.Value))) arrived.Add(notice);
            }
            else if (old is not null && StillNew(old, states))
            {
                result.Add(old);
            }
        }

        result.Sort((a, b) => b.Added.CompareTo(a.Added));
        return ApertureResult<SeasonLook>.Success(new SeasonLook(next, result, arrived, sweep, calls));
    }

    private static DateTimeOffset? Newest(DateTimeOffset? seed, IEnumerable<DateTimeOffset?> dates)
    {
        var newest = seed;
        foreach (var d in dates)
            if (d is { } at && (newest is null || at > newest)) newest = at;
        return newest;
    }
}

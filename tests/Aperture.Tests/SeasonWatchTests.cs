using Aperture.Core;
using Aperture.Core.Testing;
using HultUI.Core.Platform;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// The new-season watch, without a window: which series count as caught up, which seasons count as
/// new, and what one look costs against the library.
/// </summary>
public class SeasonWatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private static SeasonState Season(int? number, int files, int watched, int daysAgo = 300) =>
        new($"season-{number}", number, files, files - watched, Now.AddDays(-daysAgo));

    private static int[]? Numbers(IReadOnlyList<SeasonState>? seasons) =>
        seasons?.Select(s => s.Number!.Value).ToArray();

    // ── What counts as new ──────────────────────────────────────────────

    [Fact]
    public void ASeasonThatArrivedAfterEveryOtherWasWatchedIsNew()
    {
        var seasons = new[] { Season(1, 8, 8), Season(2, 10, 10), Season(3, 8, 0, daysAgo: 2) };
        Assert.Equal(new[] { 3 }, Numbers(SeasonWatch.NewSeasons(seasons, [1, 2], firstLook: false, Now, [])));
    }

    [Fact]
    public void HalfWayThroughTheLastSeasonIsNotCaughtUp()
    {
        var seasons = new[] { Season(1, 8, 8), Season(2, 10, 6), Season(3, 8, 0) };
        Assert.Null(SeasonWatch.NewSeasons(seasons, [1, 2], false, Now, []));
    }

    [Fact]
    public void ASeasonAlreadyStartedIsNotNews()
    {
        var seasons = new[] { Season(1, 8, 8), Season(2, 8, 1) };
        Assert.Null(SeasonWatch.NewSeasons(seasons, [1], false, Now, []));
    }

    [Fact]
    public void TheSpecialsCountNeitherWay()
    {
        // An unwatched special does not spoil "caught up"…
        var withSpecial = new[] { Season(0, 2, 0), Season(1, 8, 8), Season(2, 8, 0) };
        Assert.Equal(new[] { 2 }, Numbers(SeasonWatch.NewSeasons(withSpecial, [1], false, Now, [])));
        // …and a new special is not a new season.
        var newSpecial = new[] { Season(0, 1, 0), Season(1, 8, 8) };
        Assert.Null(SeasonWatch.NewSeasons(newSpecial, [1], false, Now, []));
    }

    [Fact]
    public void TwoSeasonsThatArriveTogetherAreOneNotice()
    {
        var seasons = new[] { Season(1, 8, 8), Season(2, 8, 0), Season(3, 8, 0) };
        Assert.Equal(new[] { 2, 3 }, Numbers(SeasonWatch.NewSeasons(seasons, [1], false, Now, [])));
    }

    [Fact]
    public void AnEmptySeasonFolderIsNotASeasonUntilItsFirstEpisodeArrives()
    {
        var empty = new[] { Season(1, 8, 8), Season(2, 0, 0) };
        Assert.Null(SeasonWatch.NewSeasons(empty, [1], false, Now, []));
        // The baseline records only seasons with a file, so season 2 is still new once one lands.
        var landed = new[] { Season(1, 8, 8), Season(2, 1, 0) };
        Assert.Equal(new[] { 2 }, Numbers(SeasonWatch.NewSeasons(landed, [1], false, Now, [])));
    }

    [Fact]
    public void ASeriesTheLastLookNeverSawIsTheBaselineNotNews()
    {
        var seasons = new[] { Season(1, 8, 8), Season(2, 8, 0, daysAgo: 1) };
        Assert.Null(SeasonWatch.NewSeasons(seasons, before: null, firstLook: false, Now, []));
    }

    [Fact]
    public void TheFirstLookCountsOnlySeasonsAddedInTheLastTwoWeeks()
    {
        var recent = new[] { Season(1, 8, 8, daysAgo: 400), Season(2, 8, 0, daysAgo: 3) };
        Assert.Equal(new[] { 2 }, Numbers(SeasonWatch.NewSeasons(recent, null, firstLook: true, Now, [])));
        var old = new[] { Season(1, 8, 8, daysAgo: 400), Season(2, 8, 0, daysAgo: 30) };
        Assert.Null(SeasonWatch.NewSeasons(old, null, firstLook: true, Now, []));
        // A whole series added this week, part of it watched since: nothing new, it is all one arrival.
        var allNew = new[] { Season(1, 8, 8, daysAgo: 3), Season(2, 8, 0, daysAgo: 3) };
        Assert.Null(SeasonWatch.NewSeasons(allNew, null, firstLook: true, Now, []));
    }

    [Fact]
    public void EarlierSeasonsFilledInAfterALaterOneWasWatchedAreNotNew()
    {
        // As on the lab server: season 5 watched in January, seasons 1–3 downloaded last week.
        var backfill = new[] { Season(1, 26, 0, daysAgo: 7), Season(2, 7, 0, daysAgo: 7), Season(3, 11, 0, daysAgo: 7),
                               Season(5, 9, 9, daysAgo: 310) };
        Assert.Null(SeasonWatch.NewSeasons(backfill, null, firstLook: true, Now, []));
        Assert.Null(SeasonWatch.NewSeasons(backfill, [5], false, Now, []));
        // And while they stay unwatched, a season after them is not news either: the series is
        // not caught up.
        var withNext = backfill.Append(Season(6, 8, 0, daysAgo: 1)).ToArray();
        Assert.Null(SeasonWatch.NewSeasons(withNext, [1, 2, 3, 5], false, Now, []));
        // A gap in the numbering is no backfill: season 5 after a watched season 3 is new.
        Assert.Equal(new[] { 5 }, Numbers(SeasonWatch.NewSeasons([Season(3, 8, 8), Season(5, 8, 0)], [3], false, Now, [])));
    }

    [Fact]
    public void NothingWatchedBeforeIsNotCaughtUp()
    {
        Assert.Null(SeasonWatch.NewSeasons([Season(1, 8, 0)], [], false, Now, []));
        Assert.Null(SeasonWatch.NewSeasons([Season(1, 8, 0), Season(2, 8, 0)], [1], false, Now, []));
    }

    [Fact]
    public void EpisodesInNoNumberedSeasonMeanTheSeriesNeverQualifies()
    {
        var seasons = new[] { Season(null, 24, 0), Season(1, 8, 8), Season(2, 8, 0) };
        Assert.Null(SeasonWatch.NewSeasons(seasons, [1], false, Now, []));
    }

    [Fact]
    public void ADismissedOrAnnouncedSeasonMayStayUnwatched()
    {
        // Season 3 was dismissed (or is on the row already) and is still untouched; season 4 arrives.
        var seasons = new[] { Season(1, 8, 8), Season(2, 8, 8), Season(3, 8, 0), Season(4, 8, 0) };
        Assert.Equal(new[] { 3, 4 }, Numbers(SeasonWatch.NewSeasons(seasons, [1, 2, 3], false, Now, [3])));
        // Without that, season 3 unwatched means not caught up.
        Assert.Null(SeasonWatch.NewSeasons(seasons, [1, 2, 3], false, Now, []));
    }

    [Fact]
    public void ANoticeStandsUntilAnEpisodeOfItIsWatchedOrItIsGone()
    {
        var notice = new SeasonNotice { SeriesId = "a", Seasons = [3] };
        Assert.True(SeasonWatch.StillNew(notice, [Season(1, 8, 8), Season(3, 8, 0)]));
        Assert.False(SeasonWatch.StillNew(notice, [Season(1, 8, 8), Season(3, 8, 1)]));
        Assert.False(SeasonWatch.StillNew(notice, [Season(1, 8, 8)]));
    }

    // ── One look against a library ──────────────────────────────────────

    /// <summary>
    /// Three series: Severance caught up on two seasons with a third added two days ago; Stalker Hill
    /// with season 2 unwatched for two months; Done Show watched through.
    /// </summary>
    private static FakeMediaLibrary Shows()
    {
        var fake = new FakeMediaLibrary().AddLibrary("shows", "Series", LibraryKind.TvShows);
        fake.AddItem(new MediaItem { Id = "sev", Name = "Severance", Kind = MediaKind.Series, TmdbId = 95396, TvdbId = 371980 }, "shows");
        fake.AddSeason("sev", 1, 9, watched: 9, added: fake.Made.AddDays(-700));
        fake.AddSeason("sev", 2, 10, watched: 10, added: fake.Made.AddDays(-300));
        fake.AddSeason("sev", 3, 8, watched: 0, added: fake.Made.AddDays(-2));
        fake.AddItem(new MediaItem { Id = "hill", Name = "Stalker Hill", Kind = MediaKind.Series }, "shows");
        fake.AddSeason("hill", 1, 6, watched: 6, added: fake.Made.AddDays(-400));
        fake.AddSeason("hill", 2, 6, watched: 0, added: fake.Made.AddDays(-60));
        fake.AddItem(new MediaItem { Id = "done", Name = "Done Show", Kind = MediaKind.Series, TmdbId = 1 }, "shows");
        fake.AddSeason("done", 1, 4, watched: 4, added: fake.Made.AddDays(-500));
        fake.AddSeason("done", 2, 4, watched: 4, added: fake.Made.AddDays(-30));
        return fake;
    }

    private static SeasonLook Look(FakeMediaLibrary fake, SeasonBaseline? baseline, IReadOnlyList<SeasonNotice>? notices = null,
        IReadOnlyCollection<string>? dismissed = null, IReadOnlyList<MutedSeries>? muted = null)
    {
        var result = SeasonWatch.LookAsync(fake, baseline, notices ?? [], dismissed ?? [], muted ?? [], fake.Made)
            .GetAwaiter().GetResult();
        Assert.True(result.Ok, result.Failure?.Message);
        return result.Value;
    }

    [Fact]
    public void TheFirstLookReadsEverythingOnceAndFindsOnlyWhatArrivedRecently()
    {
        var fake = Shows();
        var look = Look(fake, baseline: null);

        Assert.True(look.Swept);
        var notice = Assert.Single(look.Notices);
        Assert.Equal(("sev", "Severance"), (notice.SeriesId, notice.SeriesName));
        Assert.Equal(new[] { 3 }, notice.Seasons);
        Assert.Equal(("sev-s3", 8, (int?)95396, (int?)371980), (notice.FirstSeasonId, notice.Episodes, notice.TmdbId, notice.TvdbId));
        Assert.Equal(fake.Made.AddDays(-2), notice.Added);
        Assert.Same(notice, Assert.Single(look.Arrived));
        // The probe, then a page of every series and a page of every season.
        Assert.Equal(3, look.Calls);
        Assert.Equal(new[] { 1, 2, 3 }, look.Baseline.Seasons["sev"]);
        Assert.Equal(new[] { 1, 2 }, look.Baseline.Seasons["hill"]);
    }

    [Fact]
    public void ALaterLookAsksOnlyAboutWhatChangedAndWhatIsOnTheRow()
    {
        var fake = Shows();
        var first = Look(fake, null);
        fake.Calls.Clear();

        // Nothing has arrived: one call, about the series with a notice standing.
        var quiet = Look(fake, first.Baseline, first.Notices);
        Assert.False(quiet.Swept);
        Assert.Equal(new[] { "GetSeriesByEpisodeAdded(start=0, limit=25)", "GetSeasons(sev, start=0, limit=500)" }, fake.Calls);
        Assert.Single(quiet.Notices);
        Assert.Empty(quiet.Arrived);

        // Done Show gets a third season.
        fake.Calls.Clear();
        fake.AddSeason("done", 3, 6, added: fake.Made.AddMinutes(-5));
        var next = Look(fake, quiet.Baseline, quiet.Notices);
        Assert.False(next.Swept);
        Assert.Equal(3, next.Calls);
        Assert.Equal(new[] { "done", "sev" }, next.Notices.Select(n => n.SeriesId));
        Assert.Equal("done", Assert.Single(next.Arrived).SeriesId);
    }

    [Fact]
    public void NoNoticesAndNothingNewCostsOneCall()
    {
        var fake = Shows();
        var first = Look(fake, null);
        var look = Look(fake, first.Baseline, notices: [], dismissed: ["sev:3"]);
        Assert.Equal(1, look.Calls);
        Assert.Empty(look.Notices);
    }

    [Fact]
    public void ANoticeLeavesOnceAnEpisodeOfItIsWatched()
    {
        var fake = Shows();
        var first = Look(fake, null);
        fake.SetPlayedAsync("sev-s3e1", true).GetAwaiter().GetResult();
        Assert.Empty(Look(fake, first.Baseline, first.Notices).Notices);
    }

    [Fact]
    public void ANoticeLeavesWithItsSeries()
    {
        var fake = Shows();
        var first = Look(fake, null);
        fake.Remove("sev");
        var look = Look(fake, first.Baseline, first.Notices);
        Assert.Empty(look.Notices);
        Assert.Empty(look.Baseline.Seasons["sev"]);
    }

    [Fact]
    public void ASeasonArrivingBesideOneOnTheRowJoinsIt()
    {
        var fake = Shows();
        var first = Look(fake, null);
        fake.AddSeason("sev", 4, 8, added: fake.Made.AddMinutes(-1));
        var look = Look(fake, first.Baseline, first.Notices);
        var notice = Assert.Single(look.Notices);
        Assert.Equal(new[] { 3, 4 }, notice.Seasons);
        Assert.Equal(16, notice.Episodes);
        Assert.Same(notice, Assert.Single(look.Arrived));
    }

    [Fact]
    public void ADismissedSeasonIsNotSaidAgain()
    {
        var fake = Shows();
        Assert.Empty(Look(fake, null, dismissed: ["sev:3"]).Notices);
    }

    [Fact]
    public void AMutedSeriesIsMatchedOnItsProviderIdsAfterALibraryIsAddedAgain()
    {
        var fake = Shows();
        // Muted under the id it had before the library was added again; the TMDB id is the same.
        var muted = new MutedSeries { SeriesId = "old-jellyfin-id", Name = "Severance", TmdbId = 95396 };
        Assert.Empty(Look(fake, null, muted: [muted]).Notices);
        var byTvdb = new MutedSeries { SeriesId = "old-jellyfin-id", Name = "Severance", TvdbId = 371980 };
        Assert.Empty(Look(fake, null, muted: [byTvdb]).Notices);
        var other = new MutedSeries { SeriesId = "old-jellyfin-id", Name = "Something else", TmdbId = 7 };
        Assert.Single(Look(fake, null, muted: [other]).Notices);
    }

    [Fact]
    public void ARescanThatRewritesEveryDateReadsEverythingOnceAndSaysNothing()
    {
        var fake = new FakeMediaLibrary().AddLibrary("shows", "Series", LibraryKind.TvShows);
        for (int i = 0; i < 30; i++)
        {
            fake.AddItem(new MediaItem { Id = "show" + i, Name = "Show " + i, Kind = MediaKind.Series }, "shows");
            fake.AddSeason("show" + i, 1, 4, watched: 4, added: fake.Made.AddDays(-200));
            fake.AddSeason("show" + i, 2, 4, watched: 0, added: fake.Made.AddDays(-100));
        }
        var first = Look(fake, null);
        Assert.Empty(first.Notices);

        // Every series says something arrived just now; no season number moved.
        for (int i = 0; i < 30; i++)
            fake.Update(fake.Item("show" + i)! with { DateLastMediaAdded = fake.Made.AddMinutes(-1) });
        var look = Look(fake, first.Baseline);
        Assert.True(look.Swept);
        Assert.Equal(3, look.Calls);   // the probe, every series, every season — not thirty calls
        Assert.Empty(look.Notices);
    }

    [Fact]
    public void AServerThatGivesNoDatesIsReadInFullEachLook()
    {
        var fake = Shows();
        var first = Look(fake, null);
        // The fake fills a missing date from the seasons; this one answers as a server that does
        // not send the field would.
        var noDates = new NoDatesLibrary(fake);
        var look = SeasonWatch.LookAsync(noDates, first.Baseline, first.Notices, [], [], fake.Made).GetAwaiter().GetResult();
        Assert.True(look.Ok);
        Assert.True(look.Value.Swept);
        Assert.Single(look.Value.Notices);
    }

    [Fact]
    public void AFailedCallFailsTheLookAndKeepsNothing()
    {
        var fake = Shows();
        fake.FailWith = new ApertureFailure(ApertureFailureKind.Network, "off");
        var result = SeasonWatch.LookAsync(fake, null, [], [], [], fake.Made).GetAwaiter().GetResult();
        Assert.False(result.Ok);
    }

    // ── What is kept ────────────────────────────────────────────────────

    [Fact]
    public void TheBaselineIsKeptInAFileBesideTheSettings()
    {
        var folder = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SeasonBaselineStore(folder, "server-1", "user-1");
            Assert.Null(store.Load());
            var baseline = new SeasonBaseline { Watermark = Now, Seasons = { ["sev"] = [1, 2, 3] } };
            Assert.True(store.Save(baseline));
            var loaded = new SeasonBaselineStore(folder, "server-1", "user-1").Load();
            Assert.Equal(Now, loaded!.Watermark);
            Assert.Equal(new[] { 1, 2, 3 }, loaded.Seasons["sev"]);
            // Another person on the same server has a baseline of their own.
            Assert.Null(new SeasonBaselineStore(folder, "server-1", "user-2").Load());
            Assert.Equal(folder, Path.GetDirectoryName(store.Path));
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void TheWatchStateIsKeptPerServerAndUserInTheSettingsFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "settings.json");
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), path);
            Assert.Equal(folder, settings.Folder);
            var mine = settings.SeasonWatchFor("server-1", "user-1");
            mine.Dismissed.Add("sev:3");
            mine.Muted.Add(new MutedSeries { SeriesId = "hill", Name = "Stalker Hill", TvdbId = 9 });
            mine.Notices.Add(new SeasonNotice { SeriesId = "done", SeriesName = "Done Show", Seasons = [3], Added = Now });
            settings.Save();

            var again = new ApertureSettings(new InMemoryCredentialStore(), path);
            var kept = again.SeasonWatchFor("server-1", "user-1");
            Assert.Equal(new[] { "sev:3" }, kept.Dismissed);
            Assert.Equal("Stalker Hill", Assert.Single(kept.Muted).Name);
            Assert.Equal(new[] { 3 }, Assert.Single(kept.Notices).Seasons);
            Assert.Empty(again.SeasonWatchFor("server-1", "user-2").Notices);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    /// <summary>A library whose series carry no "episode added" date, as a server that does not send
    /// the field would answer.</summary>
    private sealed class NoDatesLibrary(FakeMediaLibrary inner) : IMediaLibrary
    {
        public ServerInfo Server => inner.Server;
        public SignedInUser User => inner.User;

        public async Task<ApertureResult<ItemPage>> GetSeriesByEpisodeAddedAsync(int startIndex, int limit, CancellationToken ct = default)
        {
            var page = await inner.GetSeriesByEpisodeAddedAsync(startIndex, limit, ct);
            return ApertureResult<ItemPage>.Success(page.Value with
            {
                Items = page.Value.Items.Select(i => i with { DateLastMediaAdded = null }).ToList(),
            });
        }

        public Task<ApertureResult<ItemPage>> GetSeasonsAsync(string? seriesId, int startIndex, int limit, CancellationToken ct = default) =>
            inner.GetSeasonsAsync(seriesId, startIndex, limit, ct);

        public Task<ApertureResult<IReadOnlyList<LibraryView>>> GetLibrariesAsync(CancellationToken ct = default) => inner.GetLibrariesAsync(ct);
        public Task<ApertureResult<ItemPage>> GetItemsAsync(ItemQuery query, CancellationToken ct = default) => inner.GetItemsAsync(query, ct);
        public Task<ApertureResult<IReadOnlyList<MediaItem>>> GetResumeAsync(int limit = 12, CancellationToken ct = default) => inner.GetResumeAsync(limit, ct);
        public Task<ApertureResult<IReadOnlyList<MediaItem>>> GetNextUpAsync(int limit = 12, string? seriesId = null, CancellationToken ct = default) => inner.GetNextUpAsync(limit, seriesId, ct);
        public Task<ApertureResult<MediaItem?>> GetNextEpisodeAsync(MediaItem episode, CancellationToken ct = default) => inner.GetNextEpisodeAsync(episode, ct);
        public Task<ApertureResult<IReadOnlyList<MediaItem>>> GetLatestAsync(string libraryId, int limit = 16, CancellationToken ct = default) => inner.GetLatestAsync(libraryId, limit, ct);
        public Task<ApertureResult<IReadOnlyList<MediaGenre>>> GetGenresAsync(string libraryId, CancellationToken ct = default) => inner.GetGenresAsync(libraryId, ct);
        public Task<ApertureResult<MediaItem>> GetItemAsync(string itemId, CancellationToken ct = default) => inner.GetItemAsync(itemId, ct);
        public Uri? ImageUrl(string itemId, ImageKind kind, string? tag, int? maxWidth = null, int? maxHeight = null) => inner.ImageUrl(itemId, kind, tag, maxWidth, maxHeight);
        public Task<ApertureResult<PlaybackTarget>> StartPlaybackAsync(string itemId, TimeSpan startPosition, int? maxBitrateBps = null, CancellationToken ct = default) => inner.StartPlaybackAsync(itemId, startPosition, maxBitrateBps, ct);
        public Task<ApertureResult<bool>> ReportStartedAsync(PlaybackTarget target, TimeSpan position, bool paused, CancellationToken ct = default) => inner.ReportStartedAsync(target, position, paused, ct);
        public Task<ApertureResult<bool>> ReportProgressAsync(PlaybackTarget target, TimeSpan position, bool paused, CancellationToken ct = default) => inner.ReportProgressAsync(target, position, paused, ct);
        public Task<ApertureResult<bool>> ReportStoppedAsync(PlaybackTarget target, TimeSpan position, CancellationToken ct = default) => inner.ReportStoppedAsync(target, position, ct);
        public Task<ApertureResult<bool>> SetPlayedAsync(string itemId, bool played, CancellationToken ct = default) => inner.SetPlayedAsync(itemId, played, ct);
        public Task<ApertureResult<bool>> SetFavoriteAsync(string itemId, bool favorite, CancellationToken ct = default) => inner.SetFavoriteAsync(itemId, favorite, ct);
        public void Dispose() { }
    }
}

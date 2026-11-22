using Aperture;
using Aperture.Core;
using Aperture.Core.Testing;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using HultUI.Media.Testing;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// Home's New seasons row, through the whole app: when the watch looks, where the row sits, what its
/// card does from the keyboard, and Dismiss, Clear all, Stop watching and Settings' way back.
/// </summary>
public partial class ApertureAppHostTests
{
    /// <summary>The usual fixture, plus Severance — two seasons watched, a third added two days ago —
    /// and, with <paramref name="second"/>, Northern Light the same way.</summary>
    private static FakeMediaLibrary WithNewSeason(bool second = false)
    {
        var library = Library();
        library.AddItem(new MediaItem
        {
            Id = "sev", Name = "Severance", Kind = MediaKind.Series, TmdbId = 95396, TvdbId = 371980,
            PrimaryImageTag = "tag-sev",
        }, "shows");
        library.AddSeason("sev", 1, 9, watched: 9, added: library.Made.AddDays(-700));
        library.AddSeason("sev", 2, 10, watched: 10, added: library.Made.AddDays(-300));
        library.AddSeason("sev", 3, 8, added: library.Made.AddDays(-2));
        if (second)
        {
            library.AddItem(new MediaItem { Id = "north", Name = "Northern Light", Kind = MediaKind.Series }, "shows");
            library.AddSeason("north", 1, 6, watched: 6, added: library.Made.AddDays(-400));
            library.AddSeason("north", 2, 6, added: library.Made.AddDays(-1));
        }
        return library;
    }

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window).GetField("_root", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;

    private static IEnumerable<Element> AllOf(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in AllOf(child))
                yield return d;
    }

    /// <summary>Home's rows, by their keys, in the order they are drawn.</summary>
    private static List<string> HomeRowKeys(HeadlessSession session) =>
        AllOf(Root(session)).Select(e => e.AutomationId)
            .Where(id => id is { } s && s.StartsWith("row-", StringComparison.Ordinal)
                         && !s.EndsWith("-menu", StringComparison.Ordinal) && !s.EndsWith("-see-all", StringComparison.Ordinal)
                         && !s.Contains("-page", StringComparison.Ordinal) && !s.EndsWith("-prev", StringComparison.Ordinal)
                         && !s.EndsWith("-next", StringComparison.Ordinal))
            .Select(id => id![4..]).Distinct().ToList();

    private static void LookNow(HeadlessSession session)
    {
        Press(session, KeyCode.F5);
        for (int i = 0; i < 6; i++) session.PumpFrame();
    }

    [Fact]
    public void NothingIsAskedBeforeTheWatchIsDue_AndF5LooksAtOnce()
    {
        var library = WithNewSeason();
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Stalker");
            Assert.DoesNotContain(library.Calls, c => c.StartsWith("GetSeriesByEpisodeAdded", StringComparison.Ordinal));
            Assert.Null(session.Window.FindByAutomationId("new-season-sev"));

            LookNow(session);

            // The first look reads everything once: the probe, every series, every season.
            Assert.Equal(2, library.Calls.Count(c => c.StartsWith("GetSeriesByEpisodeAdded", StringComparison.Ordinal)));
            Assert.Equal(1, library.Calls.Count(c => c.StartsWith("GetSeasons(all", StringComparison.Ordinal)));
            Assert.NotNull(session.Window.FindByAutomationId("new-season-sev"));
            Assert.True(Shows(session, "Severance"));
            Assert.True(Shows(session, "Season 3 · 8 episodes"));
            Assert.True(Shows(session, "added 2 days ago"));
            Assert.True(Shows(session, "New season"));
            // What the look saw is kept beside the settings, for the next one to compare with.
            Assert.True(File.Exists(Path.Combine(app.Settings.Folder,
                $"season-watch-{library.Server.Id}-{library.User.Id:N}.json")));
            Assert.Equal("sev", Assert.Single(app.NewSeasonNotices).SeriesId);
        });
    }

    [Fact]
    public void TheRowSitsAfterWhatYouAreWatching()
    {
        Host(WithNewSeason(), (session, app) =>
        {
            PumpUntilShown(session, "Stalker");
            LookNow(session);
            var rows = HomeRowKeys(session);
            Assert.Equal("resume", rows[0]);
            Assert.Equal("newseasons", rows[1]);
        });
    }

    [Fact]
    public void TheCardIsTheNewSeasonsFirstEpisode_SpacePlaysIt_EnterOpensIt()
    {
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(45) };
        Host(WithNewSeason(), (session, app) =>
        {
            PumpUntilShown(session, "Stalker");
            LookNow(session);

            // Continue watching holds two cards; the walk goes on into the next row.
            for (int i = 0; i < 3; i++) Press(session, KeyCode.Right);
            Assert.Equal("sev-s3e1", app.CursorItem?.Id);
            Assert.Equal("new-season-sev", Focused(session));

            Press(session, KeyCode.Enter);
            Assert.Equal((ApertureView.Detail, "sev-s3e1"), (app.Current.View, app.Current.Id));
            Press(session, KeyCode.Escape);
            Assert.Equal(ApertureView.Home, app.Current.View);
            Assert.Equal("sev-s3e1", app.CursorItem?.Id);

            Press(session, KeyCode.Space);
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.Equal("sev-s3e1", app.PlayingItem?.Id);
        }, () => backend);
    }

    [Fact]
    public void DismissTakesTheCardOff_KeepsTheCursorInItsPlace_AndIsNotUndoneByTheNextLook()
    {
        Host(WithNewSeason(), (session, app) =>
        {
            PumpUntilShown(session, "Stalker");
            LookNow(session);
            for (int i = 0; i < 3; i++) Press(session, KeyCode.Right);
            int place = app.CursorIndex;

            var menu = app.CardMenu(app.CursorItem!);
            menu.Single(m => m.Label == "Dismiss").Action!();
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.Null(session.Window.FindByAutomationId("new-season-sev"));
            Assert.DoesNotContain("newseasons", HomeRowKeys(session));
            Assert.Equal(place, app.CursorIndex);
            Assert.NotNull(app.CursorItem);
            Assert.Contains("sev:3", app.Settings.SeasonWatchFor(app.Library!.Server.Id, app.Library.User.Id.ToString("N")).Dismissed);

            LookNow(session);
            Assert.Empty(app.NewSeasonNotices);
        });
    }

    [Fact]
    public void ClearAllInTheRowsMenuDismissesEveryCard()
    {
        Host(WithNewSeason(second: true), (session, app) =>
        {
            PumpUntilShown(session, "Stalker");
            LookNow(session);
            Assert.Equal(2, app.NewSeasonNotices.Count);
            Assert.NotNull(session.Window.FindByAutomationId("new-season-north"));

            AllOf(Root(session)).Single(e => e.AutomationId == "row-newseasons-menu")
                .ContextMenuItems!().Single(i => i.Label == "Clear all").Action!();
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.Empty(app.NewSeasonNotices);
            Assert.DoesNotContain("newseasons", HomeRowKeys(session));
        });
    }

    [Fact]
    public void StopWatchingMutesTheSeriesForGood_AndSettingsBringsItBack()
    {
        Host(WithNewSeason(), (session, app) =>
        {
            PumpUntilShown(session, "Stalker");
            LookNow(session);
            var card = app.NewSeasonItems.Single();

            app.CardMenu(card).Single(m => m.Label == "Stop watching Severance for new seasons").Action!();
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.Empty(app.NewSeasonNotices);
            var muted = Assert.Single(app.MutedForNewSeasons);
            // The provider ids go with it, so a library added again does not unmute it.
            Assert.Equal(("sev", "Severance", (int?)95396, (int?)371980), (muted.SeriesId, muted.Name, muted.TmdbId, muted.TvdbId));
            LookNow(session);
            Assert.Empty(app.NewSeasonNotices);

            // Settings lists it, with a way back.
            session.Window.FindByAutomationId("rail-settings")!.Click();
            PumpUntilShown(session, "Not watched for new seasons. Watching one again tells you about seasons that arrive from now on.");
            Assert.True(Shows(session, "Severance"));
            for (int i = 0; i < 2; i++) session.PumpFrame();
            session.Window.FindByAutomationId("settings-unmute-sev")!.Click();
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Empty(app.MutedForNewSeasons);
            Assert.True(Shows(session, "Every series is watched for new seasons. A card's menu on Home's New seasons row stops watching one."));
        }, height: 1600);
    }

    [Fact]
    public void MarkingTheFirstEpisodeWatchedTakesTheCardOff()
    {
        var library = WithNewSeason();
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Stalker");
            LookNow(session);
            var card = app.NewSeasonItems.Single();

            app.CardMenu(card).Single(m => m.Label == "Mark watched").Action!();
            for (int i = 0; i < 8; i++) session.PumpFrame();

            Assert.Contains("SetPlayed(sev-s3e1, True)", library.Calls);
            Assert.Empty(app.NewSeasonNotices);
            Assert.Null(session.Window.FindByAutomationId("new-season-sev"));
        });
    }

    // ── Saying it (slice 8b) ─────────────────────────────────────────────

    [Fact]
    public void ANewSeasonIsToldOnce_AndThePlayInItsToastPlaysTheFirstEpisode()
    {
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(45) };
        Host(WithNewSeason(), (session, app) =>
        {
            PumpUntilShown(session, "Stalker");
            LookNow(session);
            Assert.Equal("Severance: Season 3 is in your library", app.Toasts.Message);
            Assert.Equal(1, app.NewSeasonToasts);

            var (label, run) = app.Toasts.Action!.Value;
            Assert.Equal("Play", label);
            run();
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.Equal("sev-s3e1", app.PlayingItem?.Id);
            app.StopPlayback(report: false);

            // The next look finds it still on the row, and says nothing: it is not news twice.
            LookNow(session);
            Assert.Single(app.NewSeasonNotices);
            Assert.Equal(1, app.NewSeasonToasts);
        }, () => backend);
    }

    [Fact]
    public void SeveralAtOnceAreOneToast_WhoseShowPutsTheCursorOnTheRow()
    {
        Host(WithNewSeason(second: true), (session, app) =>
        {
            PumpUntilShown(session, "Stalker");
            LookNow(session);
            Assert.Equal("2 series you'd finished have new seasons", app.Toasts.Message);

            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Solaris");
            app.Toasts.Action!.Value.Run();
            for (int i = 0; i < 6; i++) session.PumpFrame();

            Assert.Equal(ApertureView.Home, app.Current.View);
            // Newest arrival first: Northern Light's season came yesterday.
            Assert.Equal("north-s2e1", app.CursorItem?.Id);
            Assert.Equal("new-season-north", Focused(session));
        });
    }

    [Fact]
    public void WhatArrivesDuringAFilmIsToldWhenThePlayerCloses()
    {
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(167) };
        Host(WithNewSeason(), (session, app) =>
        {
            PumpUntilShown(session, "Stalker");
            session.Window.FindByAutomationId("resume-m2")!.Click();
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.Equal("m2", app.PlayingItem?.Id);

            app.LookForNewSeasons();
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.Single(app.NewSeasonNotices);
            Assert.Equal(0, app.NewSeasonToasts);

            app.StopPlayback(report: false);
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Equal(1, app.NewSeasonToasts);
            Assert.Equal("Severance: Season 3 is in your library", app.Toasts.Message);
        }, () => backend);
    }

    [Fact]
    public void NextUpLeavesOutWhatTheRowShows_UnlessTheRowIsHidden_WhichAlsoKeepsItQuiet()
    {
        var library = WithNewSeason();
        library.NextUpIds.Add("sev-s3e1");
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Stalker");
            app.SetHomeRowHidden(ApertureApp.NewSeasonsRowKey, true);
            LookNow(session);

            // Hidden: found all the same, but not said, and Next up keeps the episode.
            Assert.Single(app.NewSeasonNotices);
            Assert.Equal(0, app.NewSeasonToasts);
            Assert.NotNull(session.Window.FindByAutomationId("card-sev-s3e1"));
            Assert.Null(session.Window.FindByAutomationId("new-season-sev"));

            app.SetHomeRowHidden(ApertureApp.NewSeasonsRowKey, false);
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.NotNull(session.Window.FindByAutomationId("new-season-sev"));
            Assert.Null(session.Window.FindByAutomationId("card-sev-s3e1"));
        });
    }

    [Fact]
    public void TheSeriesPageSaysWhichSeasonIsNew_AndMarksItsCard()
    {
        var library = WithNewSeason();
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Stalker");
            LookNow(session);
            app.Open(library.Item("sev")!);
            PumpUntilShown(session, "Season 3 is new · added 2 days ago");
            for (int i = 0; i < 4; i++) session.PumpFrame();   // the seasons arrive after the item

            var s3 = session.Window.FindByAutomationId("card-sev-s3");
            var s2 = session.Window.FindByAutomationId("card-sev-s2");
            Assert.True(s3 is not null && s2 is not null, "the season cards are not on the page");
            Assert.EndsWith(", new", s3!.Name);
            Assert.DoesNotContain(", new", s2!.Name);
        }, height: 1400);
    }

    [Fact]
    public void ARowWithNothingInItIsNotDrawn()
    {
        var library = Library();
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Stalker");
            LookNow(session);
            Assert.Empty(app.NewSeasonNotices);
            Assert.DoesNotContain("newseasons", HomeRowKeys(session));
        });
    }
}

using Aperture.Core;
using Aperture.Core.Testing;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// Aperture on a TV: a remote's D-pad, OK and Back, and no Tab (<c>Window.ArrowKeysMoveFocus</c>,
/// docs/design/DPAD-FOCUS.md §5). On a Shield TV the cursor walked the posters, but the rail and the
/// bar could not be reached, the arrows walked the posters wherever the focus was, and a fully watched
/// series' page opened with the focus on Watched, so OK toggled it.
/// </summary>
public partial class ApertureAppHostTests
{
    private static string? FocusedId(HeadlessSession session) => session.Window.Window.FocusedElement?.AutomationId;

    /// <summary>A series with nothing left to watch: its page has no Play.</summary>
    private static FakeMediaLibrary WithAWatchedSeries()
    {
        var library = Library();
        library.AddItem(new MediaItem { Id = "done", Name = "Finished Show", Kind = MediaKind.Series }, "shows");
        library.AddSeason("done", 1, 4, watched: 4).AddSeason("done", 2, 4, watched: 4);
        return library;
    }

    [Fact]
    public void OnADPad_HomeStartsOnACard_AndLeftAtARowsStart_ReachesTheRail()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            session.PumpFrames(3);
            Assert.True(app.CursorIndex >= 0, $"the cursor is nowhere; the focus is on {FocusedId(session)}");
            int start = app.CursorIndex;

            // Along the row first, which is still the cursor's.
            session.Window.PressKey(KeyCode.Right);
            session.PumpFrames(2);
            Assert.Equal(start + 1, app.CursorIndex);
            session.Window.PressKey(KeyCode.Left);
            session.PumpFrames(2);
            Assert.Equal(start, app.CursorIndex);

            // At the row's first card the cursor lets go, and the focus goes to the rail.
            var card = session.Window.Window.FocusedElement;
            session.Window.PressKey(KeyCode.Left);
            session.PumpFrames(2);
            Assert.StartsWith("rail-", FocusedId(session) ?? "(nothing)");
            Assert.Equal(start, app.CursorIndex);   // the cursor stays where it was

            // On the rail, Down goes down the rail, not into the posters.
            string? on = FocusedId(session);
            var onBox = session.Window.Window.FocusedElement!.WindowBorderBox;
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(2);
            Assert.True(FocusedId(session)?.StartsWith("rail-", StringComparison.Ordinal) == true,
                $"Down from {on} at {onBox} went to {FocusedId(session)} at {session.Window.Window.FocusedElement?.WindowBorderBox}");
            Assert.NotEqual(on, FocusedId(session));
            Assert.Equal(start, app.CursorIndex);

            // And Right comes back to the card it left, from whichever door: not to what sits level with
            // the door (a shelf's arrows, from the top doors).
            session.Window.PressKey(KeyCode.Right);
            session.PumpFrames(2);
            Assert.Same(card, session.Window.Window.FocusedElement);
            Assert.Equal(start, app.CursorIndex);
        }, dpad: true);
    }

    /// <summary>On the Shield, OK on the door of the page already open left the keyboard on the door;
    /// a door to another page hands it to that page.</summary>
    [Fact]
    public void OnADPad_TheOpenPagesDoor_HandsTheKeyboardToItsContent()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            session.PumpFrames(3);
            var card = session.Window.Window.FocusedElement;
            Assert.NotNull(card);

            session.Window.FindByAutomationId("rail-home")!.Focus();
            session.PumpFrame();
            session.Window.PressKey(KeyCode.Enter);
            session.PumpFrames(2);
            Assert.Same(card, session.Window.Window.FocusedElement);
        }, dpad: true);
    }

    /// <summary>On the Shield, nothing below the Search field could be reached: the results' cards were
    /// made before the search registered them, so the first was no stop.</summary>
    [Fact]
    public void OnADPad_DownFromTheSearchField_ReachesTheResults()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.Search));
            session.PumpFrames(2);
            Assert.Equal("search-input", FocusedId(session));
            session.Window.TypeText("So");
            PumpUntilShown(session, "Solaris");
            session.PumpFrames(2);

            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(2);
            Assert.StartsWith("card-", FocusedId(session) ?? $"(nothing)");
            Assert.True(app.CursorIndex >= 0, "the cursor did not follow the focus onto the result");

            session.Window.PressKey(KeyCode.Up);  // the top row's edge: back to the field
            session.PumpFrames(2);
            Assert.Equal("search-input", FocusedId(session));
        }, dpad: true);
    }

    [Fact]
    public void OnADPad_UpOnTheTopShelf_LeavesTheCards()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            session.PumpFrames(3);
            var card = session.Window.Window.FocusedElement;
            Assert.NotNull(card);

            session.Window.PressKey(KeyCode.Up);
            session.PumpFrames(2);
            Assert.NotSame(card, session.Window.Window.FocusedElement);
            Assert.NotNull(session.Window.Window.FocusedElement);
        }, dpad: true);
    }

    [Fact]
    public void AWatchedSeriesPage_OpensOnItsFirstSeason_NotOnWatched()
    {
        // Not only on a TV: Enter on a page with no Play toggled Watched on the desktop as well.
        foreach (bool dpad in new[] { true, false })
            Host(WithAWatchedSeries(), (session, app) =>
            {
                PumpUntilShown(session, "Films");
                app.Navigate(new Route(ApertureView.Detail, "done", "Finished Show"));
                PumpUntilShown(session, "Season 2");
                session.PumpFrames(3);
                Assert.NotEqual("detail-watched", FocusedId(session));
                var focused = session.Window.Window.FocusedElement;
                Assert.Contains("Season 1", focused?.AccessibleName ?? $"(the focus is on {FocusedId(session)})");

                // OK opens the season the focus is on; it toggles nothing.
                session.Window.PressKey(KeyCode.Enter);
                session.PumpFrames(3);
                Assert.Equal(ApertureView.Detail, app.Current.View);
                Assert.NotEqual("done", app.Current.Id);
            }, dpad: dpad);
    }

    /// <summary>The series opened from its card with OK, a press and a release: the card leaves with the
    /// page, the window puts the focus near where it was while the series loads (on the Shield, the
    /// rail's Search), and the first season takes it from there when it comes. Counted as a user's move,
    /// the release kept it on Search.</summary>
    [Fact]
    public void AWatchedSeriesOpenedWithOK_EndsOnItsFirstSeason()
    {
        // Nothing else on Home: the cursor starts on the series' card.
        var library = new FakeMediaLibrary().AddLibrary("shows", "Series", LibraryKind.TvShows);
        library.AddItem(new MediaItem { Id = "done", Name = "Finished Show", Kind = MediaKind.Series }, "shows");
        library.AddSeason("done", 1, 4, watched: 4).AddSeason("done", 2, 4, watched: 4);
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Finished Show");
            session.PumpFrames(3);
            Assert.Equal("done", app.CursorItem?.Id);

            session.Window.PressKey(KeyCode.Enter);
            PumpUntilShown(session, "Season 2");
            session.PumpFrames(3);
            var focused = session.Window.Window.FocusedElement;
            Assert.Contains("Season 1", focused?.AccessibleName ?? $"(the focus is on {FocusedId(session)})");
        }, dpad: true);
    }

    /// <summary>On the desktop too: Tab from the Search field went past the results, whose first card
    /// was made before the search registered them and so was no stop.</summary>
    [Fact]
    public void TabFromTheSearchField_ReachesTheFirstResult()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.Search));
            session.PumpFrames(2);
            session.Window.TypeText("So");
            PumpUntilShown(session, "Solaris");
            session.PumpFrames(2);
            Assert.Equal("search-input", FocusedId(session));

            session.Window.PressKey(KeyCode.Tab);
            Assert.StartsWith("card-", FocusedId(session) ?? "(nothing)");
        });
    }

    /// <summary>The sign-in form on a TV's keyboard: the server is a web address and the user name a user
    /// name (the Leanback keyboard capitalised "Jellyfin.home"), and Enter (the keyboard's Done) in the
    /// server field goes on to the user name while it is empty, where it signed in and said "Enter your
    /// username".</summary>
    [Fact]
    public void TheSignInFields_AskForTheirKeyboards_AndEnterGoesOnToTheMissingField()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.SignOut(keepAccount: false);
            for (int i = 0; i < 6; i++) session.PumpFrame();

            var address = (TextInputElement)session.Window.FindByAutomationId("connect-address")!.Element;
            var username = (TextInputElement)session.Window.FindByAutomationId("connect-username")!.Element;
            Assert.Equal(TextInputKind.Url, address.InputKind);
            Assert.Equal(TextInputKind.Username, username.InputKind);

            session.Window.FindByAutomationId("connect-address")!.Click();
            session.Window.TypeText("jellyfin.example");
            session.PumpFrame();
            session.Window.PressKey(KeyCode.Enter);
            session.PumpFrames(2);
            Assert.Equal("connect-username", FocusedId(session));
            Assert.False(Shows(session, "Enter your username"), session.Window.DumpAccessibilityTree());
        });
    }

    [Fact]
    public void AFilmPage_StillOpensOnPlay_OnADPad()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.Detail, "m1", "Stalker"));
            PumpUntilShown(session, "Alexander Kaidanovsky");
            session.PumpFrames(3);
            Assert.Equal("detail-play", FocusedId(session));
        }, dpad: true);
    }
}

using Aperture;
using Aperture.Core;
using Aperture.Core.Testing;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// The history, as a browser keeps it — Alt+← and Alt+→, the mouse's side buttons, Escape — and the
/// app driven from the keyboard alone: Tab reaches every control and Enter or Space presses it, a
/// list of cards is one Tab stop that the arrows walk, and Back lands on the card that was opened.
/// </summary>
public partial class ApertureAppHostTests
{
    private static void Press(HeadlessSession session, KeyCode key, KeyModifiers modifiers = KeyModifiers.None)
    {
        session.Window.PressKey(key, modifiers);
        for (int i = 0; i < 4; i++) session.PumpFrame();
    }

    private static Element? FocusedElement(HeadlessSession session) => session.Window.Window.FocusedElement;

    private static string? Focused(HeadlessSession session) => FocusedElement(session)?.AutomationId;

    /// <summary>Tabs until <paramref name="until"/> holds of the focused element, at most
    /// <paramref name="max"/> times; how many it took.</summary>
    private static int TabUntil(HeadlessSession session, Func<Element?, bool> until, int max = 60,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        for (int i = 1; i <= max; i++)
        {
            Press(session, KeyCode.Tab, modifiers);
            if (until(FocusedElement(session))) return i;
        }
        Assert.Fail($"Tab never got there; the focus is on {Focused(session) ?? "nothing"}");
        return -1;
    }

    private static void OpenFilms(HeadlessSession session)
    {
        PumpUntilShown(session, "Films");
        session.Window.FindByAutomationId("rail-films")!.Click();
        PumpUntilShown(session, "Stalker");
    }

    private static void SideButton(HeadlessSession session, MouseButton button)
    {
        // Over the page, wherever a thumb happens to be: on a poster here.
        session.Window.MouseDown(400, 300, button);
        session.Window.MouseUp(400, 300, button);
        for (int i = 0; i < 4; i++) session.PumpFrame();
    }

    [Fact]
    public void AltArrowsWalkTheHistory_AndOpeningAPageDropsWhatWasAhead()
    {
        Host(Library(), (session, app) =>
        {
            OpenFilms(session);
            session.Window.FindByAutomationId("card-m1")!.Click();
            PumpUntilShown(session, "Stalker is a fixture.");

            Press(session, KeyCode.Left, KeyModifiers.Alt);
            Assert.Equal(ApertureView.Library, app.Current.View);
            Press(session, KeyCode.Left, KeyModifiers.Alt);
            Assert.Equal(ApertureView.Home, app.Current.View);
            Press(session, KeyCode.Left, KeyModifiers.Alt);   // nowhere further back
            Assert.Equal(ApertureView.Home, app.Current.View);

            Press(session, KeyCode.Right, KeyModifiers.Alt);
            Assert.Equal(ApertureView.Library, app.Current.View);
            Press(session, KeyCode.Right, KeyModifiers.Alt);
            Assert.Equal((ApertureView.Detail, "m1"), (app.Current.View, app.Current.Id));
            Press(session, KeyCode.Right, KeyModifiers.Alt);  // nothing ahead
            Assert.Equal("m1", app.Current.Id);

            // Back, then somewhere new: what was ahead is gone, as in a browser.
            Press(session, KeyCode.Left, KeyModifiers.Alt);
            PumpUntilShown(session, "Solaris");
            session.Window.FindByAutomationId("card-m2")!.Click();
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Equal("m2", app.Current.Id);
            Assert.Empty(app.Ahead);
            Assert.Equal(new[] { ApertureView.Home, ApertureView.Library, ApertureView.Detail },
                app.Stack.Select(r => r.View));
        });
    }

    [Fact]
    public void TheMouseSideButtonsGoBackAndForward_AndOpenNothingUnderThePointer()
    {
        Host(Library(), (session, app) =>
        {
            OpenFilms(session);
            session.Window.FindByAutomationId("card-m1")!.Click();
            PumpUntilShown(session, "Stalker is a fixture.");

            SideButton(session, MouseButton.Back);
            Assert.Equal(ApertureView.Library, app.Current.View);
            SideButton(session, MouseButton.Back);
            Assert.Equal(ApertureView.Home, app.Current.View);
            SideButton(session, MouseButton.Forward);
            Assert.Equal(ApertureView.Library, app.Current.View);
            SideButton(session, MouseButton.Forward);
            Assert.Equal((ApertureView.Detail, "m1"), (app.Current.View, app.Current.Id));
            Assert.Equal(3, app.Stack.Count);
            Assert.Empty(app.Ahead);
        });
    }

    /// <summary>Home is a page like any other: going there from the rail or with 1 is a step Back
    /// undoes. It used to empty the history.</summary>
    [Fact]
    public void BackFromHome_ReturnsToWhereYouWere()
    {
        Host(Library(), (session, app) =>
        {
            OpenFilms(session);
            Press(session, KeyCode.Digit1);
            Assert.Equal(ApertureView.Home, app.Current.View);

            Press(session, KeyCode.Escape);
            Assert.Equal((ApertureView.Library, "movies"), (app.Current.View, app.Current.Id));
        });
    }

    /// <summary>Back puts the cursor on the card that was opened, with the keyboard, as a browser
    /// puts you back where you were; Forward and Back again do the same.</summary>
    [Fact]
    public void BackLandsOnTheCardThatWasOpened()
    {
        Host(Library(), (session, app) =>
        {
            OpenFilms(session);
            Press(session, KeyCode.Right);
            Press(session, KeyCode.Right);
            var opened = app.CursorItem!;
            Assert.Equal(1, app.CursorIndex);

            Press(session, KeyCode.Enter);
            Assert.Equal((ApertureView.Detail, opened.Id), (app.Current.View, app.Current.Id));
            // The item's page walks nothing: the arrows used to walk the grid out of sight.
            Press(session, KeyCode.Right);
            Assert.Equal(-1, app.CursorIndex);

            Press(session, KeyCode.Escape);
            Assert.Equal(ApertureView.Library, app.Current.View);
            Assert.Equal(1, app.CursorIndex);
            Assert.Equal("card-" + opened.Id, Focused(session));

            Press(session, KeyCode.Right, KeyModifiers.Alt);
            Press(session, KeyCode.Left, KeyModifiers.Alt);
            Assert.Equal(1, app.CursorIndex);
            Assert.Equal("card-" + opened.Id, Focused(session));
        });
    }

    /// <summary>A page opened again from the rail starts where its last visit left the cursor,
    /// across other pages with cursors of their own; one never visited starts with none.</summary>
    [Fact]
    public void APageOpenedAgain_StartsWhereItsLastVisitLeftTheCursor()
    {
        Host(Library(), (session, app) =>
        {
            OpenFilms(session);
            Press(session, KeyCode.Right);
            Press(session, KeyCode.Right);
            var item = app.CursorItem!;
            Press(session, KeyCode.Digit3);   // the series: a page with a cursor of its own
            Assert.Equal((ApertureView.Library, "shows"), (app.Current.View, app.Current.Id));
            Assert.Equal(-1, app.CursorIndex);
            Press(session, KeyCode.Right);

            session.Window.FindByAutomationId("rail-films")!.Click();
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Equal(ApertureView.Library, app.Current.View);
            Assert.Equal(item.Id, app.CursorItem?.Id);
            Assert.Equal(3, app.Stack.Count(r => r.View == ApertureView.Library));
        });
    }

    /// <summary>A library of films enough that ten Downs take its grid off the first screen.</summary>
    private static FakeMediaLibrary ManyFilms()
    {
        var library = new FakeMediaLibrary()
            .AddLibrary("movies", "Films", LibraryKind.Movies)
            .AddLibrary("shows", "Series", LibraryKind.TvShows);
        for (int i = 0; i < 300; i++)
            library.AddMovie($"m{i:000}", $"Film number {i:000}", "movies", 1950 + i % 70, 80 + i % 60);
        return library;
    }

    private static ScrollViewElement? LibraryGrid(HeadlessSession session) =>
        AllOf(Root(session)).OfType<ScrollViewElement>().FirstOrDefault(e => e.AutomationId == "library-grid");

    private static void OpenManyFilms(HeadlessSession session)
    {
        PumpUntilShown(session, "Films");
        session.Window.FindByAutomationId("rail-films")!.Click();
        PumpUntilShown(session, "Film number 000");
    }

    /// <summary>
    /// Back to a library scrolled off its first screen lands where it was scrolled, on the card that
    /// was opened, ringed and with the keyboard, all made in Back's own frame, so the keys after it
    /// build nothing; Forward to it and a reopen from the rail the same. It landed at the top: another
    /// view had let go of the grid, so the cursor's row had no list to be scrolled to, its card was
    /// not made and the keyboard went nowhere, and the first two keys after made the grid again
    /// (plan 17 round 18: 279 KB and 289 elements, then 76 KB).
    /// </summary>
    [Fact]
    public void BackToAScrolledGrid_LandsWhereItWas_OnTheCardThatWasOpened()
    {
        Host(ManyFilms(), (session, app) =>
        {
            OpenManyFilms(session);
            Press(session, KeyCode.Right);
            for (int i = 0; i < 10; i++) Press(session, KeyCode.Down);
            var opened = app.CursorItem!;
            float scrolled = LibraryGrid(session)!.ScrollOffsetY;
            Assert.True(scrolled > 0, "ten Downs left the grid at the top");

            Press(session, KeyCode.Enter);
            Assert.Equal((ApertureView.Detail, opened.Id), (app.Current.View, app.Current.Id));
            Press(session, KeyCode.Left, KeyModifiers.Alt);
            LandedOnTheOpenedCard("Back");

            int builds = app.Builds;
            Press(session, KeyCode.Right);
            Press(session, KeyCode.Left);
            Assert.Equal(builds, app.Builds);
            Assert.Equal("card-" + opened.Id, Focused(session));

            // Forward to the film's page and Escape back; Back to Home and Forward to the library.
            Press(session, KeyCode.Right, KeyModifiers.Alt);
            Assert.Equal(ApertureView.Detail, app.Current.View);
            Press(session, KeyCode.Escape);
            LandedOnTheOpenedCard("Escape");
            Press(session, KeyCode.Left, KeyModifiers.Alt);
            Assert.Equal(ApertureView.Home, app.Current.View);
            Press(session, KeyCode.Right, KeyModifiers.Alt);
            LandedOnTheOpenedCard("Forward");

            // Opened again from the rail, after Home: where its last visit left it.
            Press(session, KeyCode.Digit1);
            Assert.Equal(ApertureView.Home, app.Current.View);
            session.Window.FindByAutomationId("rail-films")!.Click();
            for (int i = 0; i < 4; i++) session.PumpFrame();
            LandedOnTheOpenedCard("the rail");

            void LandedOnTheOpenedCard(string how)
            {
                Assert.Equal(ApertureView.Library, app.Current.View);
                Assert.True(scrolled == LibraryGrid(session)!.ScrollOffsetY,
                    $"{how} landed at {LibraryGrid(session)!.ScrollOffsetY}, not at {scrolled}");
                Assert.Equal(opened.Id, app.CursorItem?.Id);
                Assert.Equal("card-" + opened.Id, Focused(session));
                Assert.True(((BoxElement)FocusedElement(session)!).Outline.Width > 0, $"after {how} the card has no ring");
            }
        });
    }

    /// <summary>The mouse the same: a grid wheeled down, a card clicked, and the mouse's back button
    /// lands where the wheel left it, with the card that was clicked on screen. It landed at the
    /// top.</summary>
    [Fact]
    public void TheMouseBackButton_ReturnsAWheeledGridToWhereItWas()
    {
        Host(ManyFilms(), (session, app) =>
        {
            OpenManyFilms(session);
            var box = LibraryGrid(session)!.Layout.AbsoluteContentBox;
            for (int i = 0; i < 15; i++)
            {
                session.Window.Wheel(box.X + box.Width / 2, box.Y + box.Height / 2, 0, -4);
                session.PumpFrame();
            }
            for (int i = 0; i < 4; i++) session.PumpFrame();
            var grid = LibraryGrid(session)!;
            float wheeled = grid.ScrollOffsetY;
            Assert.True(wheeled > 1000, $"the wheel took the grid to {wheeled}");
            var clicked = AllOf(grid).OfType<BoxElement>()
                .First(e => e.AutomationId?.StartsWith("card-", StringComparison.Ordinal) == true
                    && e.WindowBorderBox.Y > grid.WindowBorderBox.Y + 200);
            string id = clicked.AutomationId!;
            session.Window.Click(clicked);
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Equal(ApertureView.Detail, app.Current.View);

            SideButton(session, MouseButton.Back);
            Assert.Equal(ApertureView.Library, app.Current.View);
            Assert.Equal(wheeled, LibraryGrid(session)!.ScrollOffsetY);
            Assert.NotNull(session.Window.FindByAutomationId(id));
        });
    }

    /// <summary>An item's page hands its first verb the keyboard, so Enter on a card and Enter
    /// again plays it.</summary>
    [Fact]
    public void AnItemsPage_HandsPlayTheKeyboard_AndEnterPlays()
    {
        var backend = new HultUI.Media.Testing.FakeMediaBackend();
        Host(Library(), (session, app) =>
        {
            OpenFilms(session);
            Press(session, KeyCode.Right);
            var item = app.CursorItem!;
            Press(session, KeyCode.Enter);
            PumpUntilShown(session, $"{item.Name} is a fixture.");
            Assert.Equal("detail-play", Focused(session));

            Press(session, KeyCode.Enter);
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.Equal(item.Id, app.PlayingItem?.Id);
        }, () => backend);
    }

    /// <summary>Tab reaches the rail, and Enter and Space press a rail button.</summary>
    [Fact]
    public void TheRailIsReachedWithTab_AndPressedWithEnter()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            TabUntil(session, e => e?.AutomationId == "rail-films");
            Press(session, KeyCode.Enter);
            Assert.Equal((ApertureView.Library, "movies"), (app.Current.View, app.Current.Id));

            TabUntil(session, e => e?.AutomationId == "rail-favourites");
            Press(session, KeyCode.Space);
            Assert.Equal(ApertureView.Favourites, app.Current.View);
        });
    }

    /// <summary>A list of cards is one Tab stop: Tab goes into the grid at its first card, which
    /// takes the cursor, and Enter opens it; one more Tab leaves the grid.</summary>
    [Fact]
    public void AGridIsOneTabStop_WhoseCardTakesTheCursor()
    {
        Host(Library(), (session, app) =>
        {
            OpenFilms(session);
            Assert.Equal(-1, app.CursorIndex);
            TabUntil(session, e => e?.AutomationId?.StartsWith("card-", StringComparison.Ordinal) == true);
            Assert.Equal(0, app.CursorIndex);
            Assert.Equal("card-" + app.CursorItem!.Id, Focused(session));

            Press(session, KeyCode.Right);
            Assert.Equal(1, app.CursorIndex);
            Assert.Equal("card-" + app.CursorItem!.Id, Focused(session));

            // Tab leaves the cards; Shift+Tab comes back to the cursor's, not the first.
            Press(session, KeyCode.Tab);
            Assert.False(Focused(session)?.StartsWith("card-", StringComparison.Ordinal) == true,
                $"Tab went on to {Focused(session)}");
            Press(session, KeyCode.Tab, KeyModifiers.Shift);
            Assert.Equal("card-" + app.CursorItem!.Id, Focused(session));

            var opened = app.CursorItem!.Id;
            Press(session, KeyCode.Enter);
            Assert.Equal((ApertureView.Detail, opened), (app.Current.View, app.Current.Id));
        });
    }

    /// <summary>A filter chip is a Tab stop that Space toggles; Space on it no longer reaches
    /// the shelf's cursor.</summary>
    [Fact]
    public void AFilterChipIsPressedFromTheKeyboard()
    {
        Host(Library(), (session, app) =>
        {
            OpenFilms(session);
            Press(session, KeyCode.Right);   // a cursor whose card Space would play
            TabUntil(session, e => e?.AutomationId == "filter-unwatched");
            Press(session, KeyCode.Space);
            for (int i = 0; i < 6; i++) session.PumpFrame();

            Assert.False(Shows(session, "The Mirror"), "the watched film survived the filter");
            Assert.Null(app.PlayingItem);
        });
    }

    /// <summary>Enter on a chip whose click opens a menu opens it under the chip, from the
    /// keyboard, where the arrows and Enter pick from it.</summary>
    [Fact]
    public void AMenuChipOpensItsMenuFromTheKeyboard()
    {
        Host(Library(), (session, app) =>
        {
            OpenFilms(session);
            TabUntil(session, e => e?.AutomationId == "library-view");
            Press(session, KeyCode.Enter);
            Assert.True(Shows(session, "Titles under posters"), session.Window.DumpAccessibilityTree());
        });
    }

    /// <summary>Alt with an arrow is Back or Forward, never a step of the cursor.</summary>
    [Fact]
    public void AltLeft_GoesBack_RatherThanMovingTheCursor()
    {
        Host(Library(), (session, app) =>
        {
            OpenFilms(session);
            Press(session, KeyCode.Right);
            Press(session, KeyCode.Right);
            Press(session, KeyCode.Left, KeyModifiers.Alt);
            Assert.Equal(ApertureView.Home, app.Current.View);
        });
    }

    /// <summary>A season card on a series' page, which no cursor walks, is a button of its own.</summary>
    [Fact]
    public void ACardNoListWalks_IsPressedWithEnter()
    {
        Host(Library(), (session, app) =>
        {
            app.Navigate(new Route(ApertureView.Detail, "s1", "Bleach"));
            PumpUntilShown(session, "Season 1");
            TabUntil(session, e => e?.AutomationId == "card-se1");
            Press(session, KeyCode.Enter);
            Assert.Equal((ApertureView.Detail, "se1"), (app.Current.View, app.Current.Id));
        });
    }
}

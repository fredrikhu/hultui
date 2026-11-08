using System.Diagnostics;
using System.Reflection;
using Aperture;
using Aperture.Core;
using Aperture.Core.Testing;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// The library grid keeps a row of posters until its item changes — its place and the column
/// the cursor is in there — so a build after an arrow makes the row or two the cursor left and
/// reached instead of every row on screen. (The arrow itself moves the ring on the live cards and
/// builds nothing: GridCursorTests.) What else a row shows is in a shape that makes the rows on
/// screen again: one test per input, each failing with that input left out.
/// </summary>
// Serial: the display-scale test changes process-wide state (the glyph caches) that other
// sessions draw with.
[Collection("ApertureProcessWide")]
public class GridRowKeepTests
{
    [Fact]
    public void ABuildAfterARightArrow_MakesTheRowTheCursorMovedIn_AndKeepsTheRest()
    {
        Host((session, app, _, _) =>
        {
            Keys(session, KeyCode.Right);   // the first press lands on the first card
            Keys(session, KeyCode.Right);
            int made = app.GridRowsMade;

            Rebuild(session, app);

            Assert.Equal(1, app.GridRowsMade - made);
            Assert.True(Card(session, "m001").Outline.Width > 0, "the cursor's card has no ring");
            Assert.False(Card(session, "m000").Outline.Width > 0, "the card the cursor left keeps its ring");
        });
    }

    [Fact]
    public void ABuildAfterADownArrow_MakesTheTwoRowsTheCursorMovedBetween()
    {
        Host((session, app, _, _) =>
        {
            Keys(session, KeyCode.Right);
            Rebuild(session, app);
            Keys(session, KeyCode.Down);
            int made = app.GridRowsMade;

            Rebuild(session, app);

            Assert.Equal(2, app.GridRowsMade - made);
        });
    }

    /// <summary>A view-only rebuild, as a picture landing asks for, and the frames that make its rows.</summary>
    private static void Rebuild(HeadlessSession session, ApertureApp app)
    {
        typeof(ApertureApp).GetMethod("RerenderView", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);
        session.PumpFrames(3);
    }

    /// <summary>A narrower window fits fewer posters across: every row on screen holds other
    /// cards now, though its place and the cursor did not move.</summary>
    [Fact]
    public void ANarrowerWindow_RefillsTheKeptRows()
    {
        Host((session, app, _, _) =>
        {
            int across = Cards(Rows(session)[0]).Count;

            session.Window.SetClientSize(1000, 900);
            Settle(session);

            int now = Cards(Rows(session)[0]).Count;
            Assert.True(now < across, $"{now} cards a row, {across} before");
            Assert.All(Rows(session).Where(r => Cards(r).Count > 0), r => Assert.True(Cards(r).Count <= now));
        });
    }

    /// <summary>Marking a card watched changes that copy in place: its row, which neither moved
    /// nor holds the cursor, shows the tick.</summary>
    [Fact]
    public void MarkingACardWatched_TicksItsKeptRow()
    {
        Host((session, app, library, _) =>
        {
            int across = Cards(Rows(session)[0]).Count;
            var item = app.GridItems[across + 1];   // second row, second card
            Assert.False(item.Played);
            Assert.DoesNotContain("watched", Card(session, item.Id).AccessibleName ?? "");

            app.CardMenu(item).First(m => m.Label == "Mark watched").Action!();
            Settle(session);

            Assert.Contains("watched", Card(session, item.Id).AccessibleName ?? "");
        });
    }

    /// <summary>
    /// A page that lands fills the last row the page before left part full, a row whose place
    /// and cursor did not change. The artwork never lands here, so no picture makes the rows
    /// again. The landing rebuilds the view by itself (it waited for the next build until plan
    /// 17's step 17; see LibraryPagingTests), so no key is pressed.
    /// </summary>
    [Fact]
    public void APageThatLands_FillsTheKeptLastRow()
    {
        Host((session, app, _, _) =>
        {
            int across = Cards(Rows(session)[0]).Count;
            int partial = 100 / across;   // the row the first page left part full
            Assert.Equal(150, app.GridItems.Count);   // the second page has landed
            Assert.Equal(across, Cards(Rows(session)[partial]).Count);
        }, films: 150, width: 2400, height: 2400, artwork: new GatedArtwork());
    }

    /// <summary>Pictures land after the cards are made: the kept rows take them.</summary>
    [Fact]
    public void PicturesThatLandLater_ShowOnTheKeptRows()
    {
        var artwork = new GatedArtwork();
        Host((session, app, _, _) =>
        {
            Assert.DoesNotContain(Rows(session).SelectMany(Cards), HasPicture);

            artwork.Open();
            try
            {
                session.PumpUntil(() => Rows(session).SelectMany(Cards).Any(HasPicture), TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                throw new Xunit.Sdk.XunitException("no card on screen shows its picture");
            }
        }, artwork: artwork);
    }

    /// <summary>A move to a screen of another scale: the cards' glyphs and pictures are cut for
    /// it, so the rows on screen are made again.</summary>
    [Fact]
    public void ADisplayScaleChange_MakesTheRowsAgain()
    {
        Host((session, app, _, _) =>
        {
            try
            {
                int onScreen = Rows(session).Count;
                int made = app.GridRowsMade;

                session.Window.SetScale(1.5f);
                session.PumpFrames(3);

                Assert.True(app.GridRowsMade - made >= onScreen, $"{app.GridRowsMade - made} rows made of {onScreen}");
            }
            finally
            {
                session.Window.SetScale(1f);
                session.PumpFrames(2);
            }
        });
    }

    // ── Kept cards ───────────────────────────────────────────────────────

    /// <summary>A patch copies what the build gave the card, so a mark left on a card no build
    /// gave it is still there on the cards nothing patched.</summary>
    private const string Untouched = "untouched";

    private static void Stamp(IEnumerable<BoxElement> cards)
    {
        foreach (var card in cards) card.AccessibleDescription = Untouched;
    }

    /// <summary>A build after a move along a row makes the row again, and the row hands back every
    /// card but the two the cursor moved between. The row is patched in place, the cards are the same
    /// cards.</summary>
    [Fact]
    public void ABuildAfterARightArrow_MakesTheTwoCardsItMovedBetween_AndLeavesTheRestUntouched()
    {
        Host((session, app, _, _) =>
        {
            Keys(session, KeyCode.Right);
            Rebuild(session, app);
            var row = Rows(session)[0];
            var cards = Cards(row);
            Stamp(cards);

            Keys(session, KeyCode.Right);
            Rebuild(session, app);

            Assert.Same(row, Rows(session)[0]);
            Assert.Equal(cards, Cards(row));
            Assert.Equal([null, null, .. Enumerable.Repeat(Untouched, cards.Count - 2)],
                Cards(row).Select(c => c.AccessibleDescription));
            Assert.True(Card(session, "m001").Outline.Width > 0, "the cursor's card has no ring");
            Assert.False(Card(session, "m000").Outline.Width > 0, "the card the cursor left keeps its ring");
        });
    }

    [Fact]
    public void ABuildAfterADownArrow_MakesTheTwoCardsItMovedBetween_InBothRows()
    {
        Host((session, app, _, _) =>
        {
            Keys(session, KeyCode.Right);
            Rebuild(session, app);
            var rows = Rows(session);
            var cards = rows.SelectMany(Cards).ToList();
            Stamp(cards);
            int across = Cards(rows[0]).Count;

            Keys(session, KeyCode.Down);
            Rebuild(session, app);

            Assert.Equal(rows, Rows(session));
            Assert.Equal(cards, Rows(session).SelectMany(Cards));
            Assert.Equal(["card-m000", $"card-m{across:000}"],
                cards.Where(c => c.AccessibleDescription is null).Select(c => c.AutomationId));
            Assert.True(Card(session, $"m{across:000}").Outline.Width > 0);
        });
    }

    /// <summary>A card with the keyboard is a kept element: its row made again (a toggle on another
    /// card in it) leaves it focused.</summary>
    [Fact]
    public void ACardWithTheKeyboard_KeepsItWhenItsRowIsMadeAgain()
    {
        Host((session, app, _, _) =>
        {
            Keys(session, KeyCode.Right);
            Keys(session, KeyCode.Right);
            var focused = session.Window.Window.FocusedElement;
            Assert.Equal("card-m001", focused?.AutomationId);
            int made = app.GridRowsMade;

            app.CardMenu(app.GridItems[3]).First(m => m.Label == "Mark watched").Action!();
            Settle(session);

            Assert.True(app.GridRowsMade > made, "no row was made again");
            Assert.Same(focused, session.Window.Window.FocusedElement);
        });
    }

    /// <summary>
    /// A narrower window fits a card fewer across: every place but the first row's moves to another
    /// row, and its card goes with it, untouched. Only the first row's last card is made: the first
    /// row is the one row whose key both widths share, so the list patches it and lets go of the card
    /// that no longer fits in it before the second row asks for it. Each card was made again before,
    /// in every row whose key is new.
    /// </summary>
    [Fact]
    public void AChangeOfColumns_HandsTheCardsBack()
    {
        Host((session, app, _, _) =>
        {
            var cards = Rows(session).SelectMany(Cards).ToList();
            int across = Cards(Rows(session)[0]).Count;
            Stamp(cards);

            ulong before = ElementId.New().Value;
            session.Window.SetClientSize(1300, 900);
            session.PumpFrames(3);
            ulong elements = ElementId.New().Value - before - 1;

            Assert.Equal(across - 1, Cards(Rows(session)[0]).Count);
            var now = Rows(session).SelectMany(Cards).ToList();
            Assert.Equal([$"card-m{across - 1:000}"], now.Where(c => c.AccessibleDescription is null).Select(c => c.AutomationId));
            Assert.All(now.Where(c => c.AccessibleDescription is not null), c => Assert.Contains(c, cards));
            // The view's build, the rows, and the one card: 249 elements before.
            Assert.True(elements < 80, $"{elements} elements made");
        });
    }

    /// <summary>The cursor's card, made with its ring by the build before, keeps the ring and the
    /// keyboard as it moves to another row with a change of columns.</summary>
    [Fact]
    public void AChangeOfColumns_KeepsTheRingAndTheKeyboardOnTheCursorsCard()
    {
        Host((session, app, _, _) =>
        {
            Keys(session, KeyCode.Right);
            Keys(session, KeyCode.Down);
            Rebuild(session, app);
            var card = Card(session, "m007");
            var row = card.Parent;
            Assert.Same(card, session.Window.Window.FocusedElement);

            session.Window.SetClientSize(1300, 900);
            Settle(session);

            Assert.Same(card, Card(session, "m007"));
            Assert.NotSame(row, card.Parent);   // the second row starts at m006 now: another row
            Assert.Same(card, session.Window.Window.FocusedElement);
            Assert.Equal(["card-m007"], Rows(session).SelectMany(Cards).Where(c => c.Outline.Width > 0).Select(c => c.AutomationId));
        });
    }

    /// <summary>A card under the pointer stays where a full build leaves it: moved, it would take its
    /// hover to where the pointer is not.</summary>
    [Fact]
    public void ACardUnderThePointer_IsNotMovedByAChangeOfColumns()
    {
        Host((session, app, _, _) =>
        {
            var hovered = Card(session, "m009");
            var box = hovered.Layout.AbsoluteBorderBox;
            session.Window.MoveMouse(box.X + box.Width / 2, box.Y + 40);
            session.PumpFrames(2);
            Assert.True(hovered.IsHovered);
            var other = Card(session, "m010");

            session.Window.SetClientSize(1300, 900);
            Settle(session);

            Assert.NotSame(hovered, Card(session, "m009"));
            Assert.Same(other, Card(session, "m010"));
            Assert.DoesNotContain(Rows(session).SelectMany(Cards), c => c.IsHovered);
        });
    }

    /// <summary>The oracle across changes of columns: the grid, its rings and what a reader is told
    /// are what a grid made from nothing at the same width shows.</summary>
    [Fact]
    public void ChangesOfColumns_EndWhereAGridMadeFromNothingWould()
    {
        Host((session, app, _, _) =>
        {
            session.Window.InstallAccessibilityBridge();
            session.PumpFrames(3);
            (KeyCode? Key, int Width)[] steps =
            [
                (KeyCode.Right, 0), (null, 1300), (KeyCode.Down, 0), (null, 1440), (KeyCode.Right, 0), (null, 1140),
                (KeyCode.PageDown, 0), (null, 1440), (KeyCode.Up, 0), (null, 1300),
            ];
            int step = 0;
            foreach (var (key, width) in steps)
            {
                if (key is { } k) Keys(session, k);
                else session.Window.SetClientSize(width, 900);
                for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));
                step++;
                string shown = RowsSig(session) + Rings(session), told = Told(session.Window.AccessibilityTree);

                ForgetCards(app);
                ((VirtualListElement<ApertureApp.GridRowItem>)Grid(session)!).InvalidateRows();
                session.PumpFrames(2);
                for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));

                string fresh = RowsSig(session) + Rings(session);
                Assert.True(fresh == shown, $"after step {step}: the grid is not what one made from nothing shows\n{First(shown, fresh)}");
                string freshTold = Told(session.Window.AccessibilityTree);
                Assert.True(freshTold == told, $"after step {step}: a reader is told otherwise\n{First(told, freshTold)}");
            }
        }, films: 200);
    }

    private static string Rings(HeadlessSession session) =>
        "rings: " + string.Join(",", Rows(session).SelectMany(Cards).Where(c => c.Outline.Width > 0).Select(c => c.AutomationId));

    /// <summary>A kept card keeps its image element, and a card made again draws its picture in
    /// the first frame after the key: nothing shows a placeholder on the way.</summary>
    [Fact]
    public void Pictures_StayUpThroughACursorMove()
    {
        Host((session, app, _, _) =>
        {
            session.PumpUntil(() => Rows(session).SelectMany(Cards).All(HasPicture), TimeSpan.FromSeconds(5));
            Keys(session, KeyCode.Right);
            var row = Rows(session)[0];
            var images = Cards(row).ToDictionary(c => c, c => Descendants(c).OfType<ImageElement>().Single(i => i.Fit == ImageFit.Cover));

            for (int i = 0; i < 4; i++)
            {
                session.Window.PressKey(KeyCode.Right);
                session.PumpFrame();
                Assert.All(Rows(session).SelectMany(Cards), c => Assert.True(HasPicture(c), $"{c.AutomationId} shows no picture"));
                session.PumpFrames(2);
            }

            Assert.Equal(images.Keys, Cards(row));
            foreach (var (card, image) in images)
            {
                var now = Descendants(card).OfType<ImageElement>().Single(e => e.Fit == ImageFit.Cover);
                Assert.Same(image, now);
                Assert.Same(image.Source, now.Source);
            }
        });
    }

    /// <summary>One picture landing makes the rows on screen again, and only its card.</summary>
    [Fact]
    public void APictureThatLands_MakesItsCardAlone()
    {
        var artwork = new GatedArtwork(open: true) { HeldBack = "m003" };
        Host((session, app, _, _) =>
        {
            session.PumpUntil(() => Rows(session).SelectMany(Cards).Count(HasPicture) == Rows(session).SelectMany(Cards).Count() - 1,
                TimeSpan.FromSeconds(5));
            Rest(session);
            var cards = Rows(session).SelectMany(Cards).ToList();
            Stamp(cards);

            artwork.ReleaseHeld();
            session.PumpUntil(() => HasPicture(Card(session, "m003")), TimeSpan.FromSeconds(5));

            Assert.Equal(cards, Rows(session).SelectMany(Cards));
            Assert.Equal(["card-m003"], cards.Where(c => c.AccessibleDescription is null).Select(c => c.AutomationId));
        }, artwork: artwork);
    }

    /// <summary>Pictures that land together build the view once: the cache calls back once per
    /// picture, and the app posts one build at a time, which shows every picture landed before
    /// it ran. A build posted per picture made a screenful of posters a screenful of builds in
    /// one frame (46 builds for the grid's 42 posters, up to 13 in a frame).</summary>
    [Fact]
    public void PicturesThatLandTogether_BuildTheViewOnce()
    {
        var artwork = new GatedArtwork(open: false);
        Host((session, app, _, _) =>
        {
            var cards = Rows(session).SelectMany(Cards).ToList();
            Assert.True(cards.Count > 20, $"only {cards.Count} cards on screen");
            Assert.DoesNotContain(cards, HasPicture);

            // Every picture lands while the UI thread is not pumping, so their callbacks wait
            // for the same drain. The pause after the last one leaves its callback time to post.
            artwork.Open();
            var sw = Stopwatch.StartNew();
            while (app.Posters.PendingCount > 0 && sw.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(5);
            Assert.Equal(0, app.Posters.PendingCount);
            Thread.Sleep(200);

            int builds = app.Builds;
            session.PumpFrame();

            Assert.Equal(1, app.Builds - builds);
            Assert.All(Rows(session).SelectMany(Cards), card => Assert.True(HasPicture(card), $"{card.AutomationId} has no picture"));
        }, artwork: artwork);
    }

    /// <summary>A toggle changes the copy of one film: only its card is made.</summary>
    [Fact]
    public void MarkingACardWatched_MakesItsCardAlone()
    {
        Host((session, app, _, _) =>
        {
            var item = app.GridItems[3];
            var cards = Rows(session).SelectMany(Cards).ToList();
            Stamp(cards);

            app.CardMenu(item).First(m => m.Label == "Mark watched").Action!();
            Settle(session);

            Assert.Equal([$"card-{item.Id}"], cards.Where(c => c.AccessibleDescription is null).Select(c => c.AutomationId));
        });
    }

    /// <summary>What every card shows is in the shape: nothing is handed back across it.</summary>
    [Fact]
    public void TheTitlesSetting_MakesEveryCardAgain()
    {
        Host((session, app, _, _) =>
        {
            Assert.Contains(Descendants(Rows(session)[0]), e => e is TextElement { Content: "Film number 000" });

            var menu = (IReadOnlyList<ContextMenuItem>)typeof(ApertureApp)
                .GetMethod("ViewMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null)!;
            menu.First(m => m.Label == "Titles under posters").Action!();
            Settle(session);

            Assert.DoesNotContain(Descendants(Rows(session)[0]), e => e is TextElement { Content: "Film number 000" });
        });
    }

    /// <summary>With no picture to tell them apart: the glyphs are cut for the scale.</summary>
    [Fact]
    public void ADisplayScaleChange_MakesEveryCardAgain()
    {
        Host((session, app, _, _) =>
        {
            try
            {
                var cards = Rows(session).SelectMany(Cards).ToList();
                Stamp(cards);

                session.Window.SetScale(1.5f);
                session.PumpFrames(3);

                Assert.DoesNotContain(Rows(session).SelectMany(Cards), c => c.AccessibleDescription == Untouched);
            }
            finally
            {
                session.Window.SetScale(1f);
                session.PumpFrames(2);
            }
        }, artwork: new GatedArtwork());
    }

    /// <summary>Scrolled just out of the realized window and back, with the cards still remembered:
    /// a card in a row the list let go of is out of the tree, and is not put back in one.</summary>
    [Fact]
    public void ARowScrolledAwayAndBack_IsMadeOfNewCards()
    {
        Host((session, app, _, _) =>
        {
            Keys(session, KeyCode.Right);
            var top = Rows(session)[0];
            var cards = Cards(top);
            for (int i = 0; i < 4; i++) Keys(session, KeyCode.Down);
            Assert.DoesNotContain(top, Rows(session));
            int kept = app.GridCardsKept;
            for (int i = 0; i < 4; i++) Keys(session, KeyCode.Up);
            Settle(session);

            Assert.True(kept >= Rows(session).SelectMany(Cards).Count(), $"{kept} cards remembered");
            Assert.DoesNotContain(Cards(Rows(session)[0]), cards.Contains);
        }, films: 200);
    }

    [Fact]
    public void TheCardsRemembered_StayFewAsTheGridIsWalked()
    {
        Host((session, app, _, _) =>
        {
            Keys(session, KeyCode.Right);
            int across = Cards(Rows(session)[0]).Count;
            for (int i = 0; i < 40; i++) Keys(session, KeyCode.Down);
            Assert.True(app.GridCardsKept <= across * 12, $"{app.GridCardsKept} cards remembered, {across} a row");
        }, films: 400);
    }

    /// <summary>The oracle: wherever a walk ends, the grid and what a reader is told of it are what
    /// a grid made from nothing shows there.</summary>
    [Fact]
    public void AWalk_EndsWhereAGridMadeFromNothingWould()
    {
        var library = new FakeMediaLibrary().AddLibrary("movies", "Films", LibraryKind.Movies);
        for (int i = 0; i < 300; i++)
            library.AddMovie($"m{i:000}", $"Film number {i:000}", "movies", 1950 + i % 70, 80 + i % 60,
                resume: i % 9 == 4 ? TimeSpan.FromMinutes(20) : null, played: i % 7 == 2);
        KeyCode[] keys =
        [
            KeyCode.Right, KeyCode.Right, KeyCode.Down, KeyCode.Left, KeyCode.Down, KeyCode.Down, KeyCode.Down, KeyCode.Right,
            KeyCode.Up, KeyCode.PageDown, KeyCode.Left, KeyCode.PageUp, KeyCode.Home, KeyCode.Right,
        ];
        Host((session, app, _, _) =>
        {
            session.Window.InstallAccessibilityBridge();
            session.PumpFrames(3);
            int step = 0;
            foreach (var key in keys)
            {
                Keys(session, key);
                for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));
                step++;
                string shown = RowsSig(session), told = Told(session.Window.AccessibilityTree);

                ForgetCards(app);
                ((VirtualListElement<ApertureApp.GridRowItem>)Grid(session)!).InvalidateRows();
                session.PumpFrames(2);
                for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));

                string fresh = RowsSig(session);
                Assert.True(fresh == shown, $"after step {step}, {key}: the grid is not what one made from nothing shows\n{First(shown, fresh)}");
                string freshTold = Told(session.Window.AccessibilityTree);
                Assert.True(freshTold == told, $"after step {step}, {key}: a reader is told otherwise\n{First(told, freshTold)}");
            }
        }, library: library);
    }

    private static string RowsSig(HeadlessSession session) => string.Concat(Rows(session).Select(WindowFitTests.Sig));

    private static string First(string kept, string fresh)
    {
        var xl = kept.Split('\n');
        var yl = fresh.Split('\n');
        for (int l = 0; l < Math.Max(xl.Length, yl.Length); l++)
            if (l >= xl.Length || l >= yl.Length || xl[l] != yl[l])
                return $"line {l}:\n  kept:  {(l < xl.Length ? xl[l] : "<none>")}\n  fresh: {(l < yl.Length ? yl[l] : "<none>")}";
        return "";
    }

    private static void ForgetCards(ApertureApp app)
    {
        var cards = typeof(ApertureApp).GetField("_gridCards", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        cards.GetType().GetMethod("Clear")!.Invoke(cards, null);
    }

    private static string Told(AccessibilityNode root)
    {
        var sb = new System.Text.StringBuilder();
        void Walk(AccessibilityNode n, int depth)
        {
            sb.Append(' ', depth).Append(n.Role).Append(' ').Append(n.Name).Append(' ').Append(n.AutomationId)
                .Append(n.IsFocused ? " focused" : "").Append(n.IsOffscreen ? " offscreen" : "")
                .Append(' ').Append(n.BoundingRect).Append('\n');
            foreach (var c in n.Children) Walk(c, depth + 1);
        }
        Walk(root, 0);
        return sb.ToString();
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    private static void Host(Action<HeadlessSession, ApertureApp, FakeMediaLibrary, HttpMessageHandler> body,
        int films = 60, int width = 1440, int height = 900, HttpMessageHandler? artwork = null,
        FakeMediaLibrary? library = null)
    {
        if (library is null)
        {
            library = new FakeMediaLibrary().AddLibrary("movies", "Films", LibraryKind.Movies);
            for (int i = 0; i < films; i++)
                library.AddMovie($"m{i:000}", $"Film number {i:000}", "movies", 1950 + i % 70, 80 + i % 60);
        }
        artwork ??= new GatedArtwork(open: true);

        using var session = HeadlessSession.Create(width, height);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                posterHttp: new HttpClient(artwork));
            session.Application.SetRoot(app.Mount());
            session.PumpUntil(() => session.Window.FindByAutomationId("rail-films") is not null, TimeSpan.FromSeconds(10));
            session.Window.FindByAutomationId("rail-films")!.Click();
            session.PumpUntil(() => Grid(session) is { } g && g.Children.Count > 1, TimeSpan.FromSeconds(10));
            Settle(session);
            body(session, app, library, artwork);
        }
        finally
        {
            app?.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(settingsPath)!, true); } catch (IOException) { }
        }
    }

    /// <summary>Pumps for a while in real time: artwork comes back on the thread pool.</summary>
    private static void Settle(HeadlessSession session)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < 300) { session.PumpFrame(); Thread.Sleep(5); }
        session.PumpFrames(2);
    }

    private static void Rest(HeadlessSession session) => Settle(session);

    private static void Keys(HeadlessSession session, KeyCode key)
    {
        session.Window.PressKey(key);
        session.PumpFrames(3);
    }

    /// <summary>A small poster for every artwork request, once the gate is open; until then
    /// every request waits.</summary>
    private sealed class GatedArtwork(bool open = false) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate = NewGate(open);

        private static readonly byte[] Png = Make();

        private static TaskCompletionSource NewGate(bool open)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (open) gate.SetResult();
            return gate;
        }

        public void Open() => _gate.TrySetResult();

        /// <summary>A film whose picture waits for <see cref="ReleaseHeld"/>, by its id in the URL.</summary>
        public string? HeldBack { get; init; }

        private readonly TaskCompletionSource _held = NewGate(false);

        public void ReleaseHeld() => _held.TrySetResult();

        private static byte[] Make()
        {
            using var bitmap = new SkiaSharp.SKBitmap(40, 60);
            using var canvas = new SkiaSharp.SKCanvas(bitmap);
            canvas.Clear(new SkiaSharp.SKColor(40, 60, 90));
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            return data.ToArray();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await _gate.Task.ConfigureAwait(false);
            if (HeldBack is { } held && request.RequestUri!.AbsolutePath.Contains(held, StringComparison.Ordinal))
                await _held.Task.ConfigureAwait(false);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return response;
        }
    }

    // ── Reading the screen ───────────────────────────────────────────────

    private static ScrollViewElement? Grid(HeadlessSession session) =>
        Descendants(Root(session)).OfType<ScrollViewElement>().FirstOrDefault(e => e.AutomationId == "library-grid");

    /// <summary>The realized rows, top to bottom (the list's first child is its spacer).</summary>
    private static List<Element> Rows(HeadlessSession session) => Grid(session)!.Children.Skip(1).ToList();

    private static List<BoxElement> Cards(Element row) =>
        Descendants(row).OfType<BoxElement>().Where(e => e.AutomationId?.StartsWith("card-", StringComparison.Ordinal) == true).ToList();

    private static BoxElement Card(HeadlessSession session, string id) =>
        Rows(session).SelectMany(Cards).Single(c => c.AutomationId == "card-" + id);

    /// <summary>A card drawing its picture (a cover fit), not the placeholder glyph.</summary>
    private static bool HasPicture(BoxElement card) =>
        Descendants(card).OfType<ImageElement>().Any(i => i.Fit == ImageFit.Cover);

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window).GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;
}

/// <summary>Runs its classes alone: they change state every session in the process draws with.</summary>
[CollectionDefinition("ApertureProcessWide", DisableParallelization = true)]
public class ApertureProcessWideCollection { }

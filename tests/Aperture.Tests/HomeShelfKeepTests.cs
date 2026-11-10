using System.Reflection;
using Aperture;
using Aperture.Core;
using Aperture.Core.Testing;
using HultUI.Core.Elements;
using HultUI.Core.Platform;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// Home's shelves handed back whole (plan 17 round 8 step 6). A Home build runs mostly when pictures
/// land; with every card kept it made each shelf's heading and row around them, 39 of the build's 46
/// elements. A shelf whose heading inputs and every card on its page are what it was made with is now
/// the live one, untouched; one whose count, title or place among the rows moved is made again, with a
/// heading and a menu that say so. Five shelves of seven films and a "Continue watching" shelf, as
/// HomeCursorTests has them.
/// </summary>
[Collection("ApertureProcessWide")]
public class HomeShelfKeepTests
{
    private const string Untouched = "untouched";

    [Fact]
    public void ABuildWithNothingChanged_HandsBackEveryShelf()
    {
        Host((session, app, library) =>
        {
            var shelves = Shelves(session);
            Assert.Equal(6, shelves.Count);
            Stamp(shelves);
            int made = app.HomeShelvesMade;

            ulong before = ElementId.New().Value;
            Rebuild(app);
            session.PumpFrames(2);
            ulong elements = ElementId.New().Value - before - 1;

            Assert.Equal(made, app.HomeShelvesMade);
            Assert.Equal(shelves, Shelves(session));
            Assert.All(shelves.SelectMany(Descendants), e => Assert.Equal(Untouched, e.AccessibleDescription));
            // What is left of the build: the view's scroll view and its page, and the app's body around
            // them. Every shelf made its heading and row again before: 46 elements a build.
            Assert.Equal(4ul, elements);
        });
    }

    /// <summary>At 1000 px a page holds four films. Two older films on "Shelf 1" go on its last page:
    /// the cards on its first are what they were, and its heading says three pages now.</summary>
    [Fact]
    public void AShelfWhoseCardsAreKept_FollowsItsCount()
    {
        Host((session, app, library) =>
        {
            Assert.Equal("1/2", PageOf(session, "row-latest-lib1"));
            var cards = CardsOf(session, "row-latest-lib1");
            Stamp(cards);

            library.AddMovie("x1", "Old film 1", "lib1", 1900, 90);
            library.AddMovie("x2", "Old film 2", "lib1", 1901, 90);
            app.Refresh();
            Settle(session);

            Assert.Equal("1/3", PageOf(session, "row-latest-lib1"));
            Assert.Equal(cards, CardsOf(session, "row-latest-lib1"));
            Assert.All(cards, c => Assert.Equal(Untouched, c.AccessibleDescription));
        }, width: 1000);
    }

    /// <summary>A library renamed on the server: the shelf's cards are what they were, and its heading,
    /// its menu's name and its "See all" say the new name.</summary>
    [Fact]
    public void AShelfWhoseCardsAreKept_FollowsItsTitle()
    {
        Host((session, app, library) =>
        {
            var cards = CardsOf(session, "row-latest-lib1");
            Stamp(cards);

            library.RenameLibrary("lib1", "Renamed");
            app.Refresh();
            Settle(session);

            var shelf = Shelf(session, "row-latest-lib1");
            Assert.Contains(Descendants(shelf).OfType<TextElement>(), t => t.Content == "Latest in Renamed");
            Assert.DoesNotContain(Descendants(shelf).OfType<TextElement>(), t => t.Content == "Latest in Shelf 1");
            Assert.Equal("Customise “Latest in Renamed”", Find(shelf, "row-latest-lib1-menu").AccessibleName);
            Assert.All(cards, c => Assert.Equal(Untouched, c.AccessibleDescription));
        }, width: 1000);
    }

    /// <summary>"Continue watching" moved down past a hidden row draws where it did, around the same
    /// card, but is no longer the first of the rows: its menu offers "Move up". The menu's entries are
    /// worked out from the rows of the build that made the shelf, so a shelf handed back from before
    /// the move offered it disabled.</summary>
    [Fact]
    public void AShelfWhoseCardsAreKept_FollowsItsPlaceInItsMenu()
    {
        Host((session, app, library) =>
        {
            app.SetHomeRowHidden(ApertureApp.LatestRowKey("lib0"), true);
            Settle(session);
            Assert.True(MenuItem(session, "row-resume-menu", "Move up").IsDisabled);
            var cards = CardsOf(session, "row-resume");
            Stamp(cards);
            int made = app.HomeShelvesMade;

            app.MoveHomeRow(ApertureApp.ResumeRowKey, 1);   // below the hidden row: Home draws as it did
            Settle(session);

            Assert.False(MenuItem(session, "row-resume-menu", "Move up").IsDisabled);
            Assert.All(cards, c => Assert.Equal(Untouched, c.AccessibleDescription));
            Assert.Equal(made + 1, app.HomeShelvesMade);   // that shelf alone
        });
    }

    /// <summary>Another view lets Home go (plan 17 round 10). The shelves and cards Home keeps are held
    /// weakly (KeptElements), and the scratch list of a shelf's cards is emptied once the shelves are
    /// made: left full, it held the last shelf's cards, and through them the whole Home its build made,
    /// behind Settings. Back on Home every shelf is there.</summary>
    [Fact]
    public void AnotherView_LetsHomeGo()
    {
        Host((session, app, _) =>
        {
            var home = WeakShelves(session);
            var page = PageOf(session, "row-latest-lib1");

            app.Navigate(new Route(ApertureView.Settings));
            Settle(session);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.All(home, shelf => Assert.False(shelf.IsAlive, "a Home shelf outlived the view"));
            app.Navigate(new Route(ApertureView.Home));
            Settle(session);
            Assert.Equal(6, Shelves(session).Count);
            Assert.Equal(page, PageOf(session, "row-latest-lib1"));
        });
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference[] WeakShelves(HeadlessSession session) =>
        Shelves(session).Select(s => new WeakReference(s)).ToArray();

    // ── Hosting ──────────────────────────────────────────────────────────

    private static void Host(Action<HeadlessSession, ApertureApp, FakeMediaLibrary> body, int width = 1440)
    {
        var library = new FakeMediaLibrary();
        for (int l = 0; l < 5; l++)
        {
            library.AddLibrary($"lib{l}", $"Shelf {l}", LibraryKind.Movies);
            for (int i = 0; i < 7; i++)
            {
                int n = l * 7 + i;
                library.AddMovie($"m{n:000}", $"Film {n:000}", $"lib{l}", 1950 + n, 80 + n,
                    resume: n == 3 ? TimeSpan.FromMinutes(20) : null);
            }
        }
        library.ResumeIds.Add("m003");

        using var session = HeadlessSession.Create(width, 900);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                posterHttp: new HttpClient(new NoArtwork()));
            session.Application.SetRoot(app.Mount());
            session.PumpUntil(() => Shelves(session).Count == 6, TimeSpan.FromSeconds(10));
            Settle(session);
            body(session, app, library);
        }
        finally
        {
            app?.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(settingsPath)!, true); } catch (IOException) { }
        }
    }

    private static void Settle(HeadlessSession session)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < 300) { session.PumpFrame(); Thread.Sleep(5); }
        session.PumpFrames(2);
    }

    /// <summary>A view-only rebuild, as a picture landing asks for.</summary>
    private static void Rebuild(ApertureApp app) =>
        typeof(ApertureApp).GetMethod("RerenderView", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);

    /// <summary>A patch copies what the build gave an element, so a mark no build gave it stays only on
    /// the elements nothing patched or made again.</summary>
    private static void Stamp(IEnumerable<Element> elements)
    {
        foreach (var e in elements.SelectMany(Descendants)) e.AccessibleDescription = Untouched;
    }

    /// <summary>The shelves of library cards, top to bottom: the page's children with a row id.</summary>
    private static List<Element> Shelves(HeadlessSession session) =>
        Descendants(Root(session))
            .Where(e => e.AutomationId is { } id && id.StartsWith("row-", StringComparison.Ordinal)
                && Descendants(e).Any(d => d.AutomationId == id + "-menu"))
            .ToList();

    private static Element Shelf(HeadlessSession session, string rowId) =>
        Find(Root(session), rowId);

    private static List<BoxElement> CardsOf(HeadlessSession session, string rowId) =>
        Descendants(Shelf(session, rowId)).OfType<BoxElement>()
            .Where(e => e.AutomationId is { } id && (id.StartsWith("card-", StringComparison.Ordinal) || id.StartsWith("resume-", StringComparison.Ordinal)))
            .ToList();

    /// <summary>The page a shelf's heading says it shows, "1/2"; null for a shelf on one page.</summary>
    private static string? PageOf(HeadlessSession session, string rowId) =>
        Descendants(Shelf(session, rowId)).OfType<TextElement>()
            .Select(t => t.Content).FirstOrDefault(c => c.Length >= 3 && c.Contains('/') && char.IsDigit(c[0]));

    private static ContextMenuItem MenuItem(HeadlessSession session, string menuId, string label) =>
        Find(Root(session), menuId).ContextMenuItems!().Single(i => i.Label == label);

    private static Element Find(Element root, string automationId) =>
        Descendants(root).Single(e => e.AutomationId == automationId);

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

    private sealed class NoArtwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}

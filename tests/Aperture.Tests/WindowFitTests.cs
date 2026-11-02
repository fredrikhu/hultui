using System.Reflection;
using System.Text;
using Aperture;
using Aperture.Core;
using Aperture.Core.Requests;
using Aperture.Core.Testing;
using Aperture.Theme;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// A resize builds only when a build would read something else off the window's size
/// (ApertureApp.WindowFit.cs). The risk is a reader of the size the check does not know: a pager
/// or a shelf that stays at the last build's count until something else builds. The oracle is a
/// full build: each view is walked through the sizes, and what is on screen is compared with what
/// a forced rebuild makes, with builds far enough apart for a missed one to show.
/// </summary>
[Collection("ApertureProcessWide")]
public class WindowFitTests
{
    [Fact]
    public void AResizeThatKeepsTheColumns_BuildsNothing()
    {
        Host((session, app) =>
        {
            OpenGrid(session);
            Assert.Equal(7, app.VisibleCards(Halide.PosterWidth));
            int builds = app.Builds, rows = app.GridRowsMade;

            for (int width = 1444; width < 1560; width += 4)
            {
                session.Window.SetClientSize(width, 900);
                session.PumpFrames(2);
            }

            Assert.Equal(0, app.Builds - builds);
            Assert.Equal(0, app.GridRowsMade - rows);
        });
    }

    /// <summary>The first width with a column fewer builds, and the arrows and the rows have it.</summary>
    [Fact]
    public void AResizeAcrossAColumnCount_Builds_AndTheArrowsAndRowsFollow()
    {
        Host((session, app) =>
        {
            OpenGrid(session);
            session.Window.SetClientSize(1384, 900);
            session.PumpFrames(2);
            Assert.Equal(7, app.VisibleCards(Halide.PosterWidth));
            int builds = app.Builds;

            session.Window.SetClientSize(1383, 900);
            session.PumpFrames(2);

            Assert.Equal(1, app.Builds - builds);
            Assert.Equal(6, app.VisibleCards(Halide.PosterWidth));
            Assert.Equal(6, Cards(session, "library-grid").Count(c => c.Layout.AbsoluteBorderBox.Y == FirstRowY(session)));
            session.Window.PressKey(KeyCode.Right);
            session.PumpFrames(2);
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(2);
            Assert.Equal(6, app.CursorIndex);
        });
    }

    /// <summary>A taller window needs no build for its rows: they are made as they come into view,
    /// and each asks for the next page while a page is still to go.</summary>
    [Fact]
    public void ATallerWindow_ShowsItsRows_AndKeepsAPageAhead()
    {
        Host((session, app) =>
        {
            OpenGrid(session);
            Assert.Equal(200, app.GridItems.Count);

            for (int height = 1000; height <= 7000; height += 400)
            {
                session.Window.SetClientSize(1440, height);
                session.PumpFrames(2);
            }
            session.PumpUntil(() => app.GridItems.Count > 200, TimeSpan.FromSeconds(5));
            Rest(session);

            int last = Cards(session, "library-grid").Max(c => int.Parse(c.AutomationId!["card-m".Length..]));
            Assert.True(last > 120, $"the last card on screen is number {last}");
            Assert.True(app.GridItems.Count >= last + 100, $"{app.GridItems.Count} loaded for {last + 1} shown");
            AssertAFullBuildChangesNothing(session, app, "a tall window");
        });
    }

    /// <summary>A window that shrinks under the cursor brings it back on screen, a step behind the
    /// window as a build's reveal was, with no build.</summary>
    [Fact]
    public void AShorterWindow_KeepsTheCursorOnScreen()
    {
        Host((session, app) =>
        {
            OpenGrid(session);
            for (int i = 0; i < 3; i++)
            {
                session.Window.PressKey(KeyCode.Down);
                session.PumpFrames(2);
            }
            var grid = Grid(session);
            float top = grid.ScrollOffsetY;

            for (int height = 880; height >= 600; height -= 20)
            {
                session.Window.SetClientSize(1440, height);
                session.PumpFrames(2);
            }
            session.Window.SetClientSize(1440, 599);
            session.PumpFrames(2);

            float stride = Halide.PosterHeight + Halide.PosterCaption + Halide.PosterGap;
            Assert.True(grid.ScrollOffsetY > top, "the list did not follow the cursor");
            Assert.True(3 * stride <= grid.ScrollOffsetY + grid.Layout.AbsoluteContentBox.Height + 1f,
                $"the cursor's row ends at {3 * stride}, the view at {grid.ScrollOffsetY + grid.Layout.AbsoluteContentBox.Height}");
        });
    }

    /// <summary>Full screen taken from outside the app comes as a resize: the title bar goes.</summary>
    [Fact]
    public void FullScreenThatArrivesAsAResize_TakesTheTitleBarAway()
    {
        Host((session, app) =>
        {
            OpenGrid(session);
            Assert.NotNull(session.Window.FindByAutomationId("titlebar-user"));

            session.Window.Window.SetFullscreen(true);
            session.Window.SetClientSize(1444, 900);
            session.PumpFrames(2);
            Assert.Null(session.Window.FindByAutomationId("titlebar-user"));

            session.Window.Window.SetFullscreen(false);
            session.Window.SetClientSize(1440, 900);
            session.PumpFrames(2);
            Assert.NotNull(session.Window.FindByAutomationId("titlebar-user"));
        });
    }

    /// <summary>A hero's box follows the window's height, by a build, and only that.</summary>
    [Fact]
    public void AHeroFollowsTheHeight_AndAWidthInsideAnArtworkStepBuildsNothing()
    {
        Host((session, app) =>
        {
            app.Navigate(new Route(ApertureView.Detail, "m001", "Film number 001"));
            session.PumpFrames(3);
            session.PumpUntil(() => session.Window.FindByAutomationId("detail-play") is not null || app.Builds > 3, TimeSpan.FromSeconds(5));
            Rest(session);
            float hero = app.HeroHeight;

            int builds = app.Builds;
            session.Window.SetClientSize(1444, 900);
            session.PumpFrames(2);
            Assert.Equal(0, app.Builds - builds);

            session.Window.SetClientSize(1444, 800);
            session.PumpFrames(2);
            Assert.True(app.Builds > builds, "the hero's box did not take the new height");
            Assert.True(app.HeroHeight < hero);
            AssertAFullBuildChangesNothing(session, app, "a new height");
        });
    }

    /// <summary>A width that keeps every column count and passes an artwork step asks for the
    /// bigger picture, which only a build does.</summary>
    [Fact]
    public void AWidthPastAnArtworkStep_Builds()
    {
        Host((session, app) =>
        {
            app.Navigate(new Route(ApertureView.Detail, "m001", "Film number 001"));
            session.PumpFrames(3);
            Rest(session);
            session.Window.SetClientSize(1472, 900);   // 1,408 across the hero, a step exactly
            session.PumpFrames(2);
            int builds = app.Builds;

            session.Window.SetClientSize(1473, 900);
            session.PumpFrames(2);

            Assert.True(app.Builds > builds, "the bigger picture was not asked for");
        });
    }

    public static TheoryData<string> Views => new()
    {
        "home", "library", "library list", "library no titles", "detail", "favourites", "search", "continue",
        "discover", "discover title", "discover all", "genres", "companies", "person", "requests", "settings",
    };

    [Theory]
    [MemberData(nameof(Views))]
    public void EveryView_IsWhatAFullBuildMakes_AfterAResize(string view)
    {
        Host((session, app) =>
        {
            Show(session, app, view);
            AssertAFullBuildChangesNothing(session, app, "the first width");
            int builds = app.Builds, steps = 0;

            // Shrinking, then growing past where it started, a build only every ninth step, so a
            // build the window needed and did not get shows at the widths between. 13 px steps:
            // every column count of every card width is crossed between two of them.
            for (int width = 1440; width >= 860; width -= 13)
            {
                session.Window.SetClientSize(width, 900 - (1440 - width) / 4);
                session.PumpFrames(2);
                if (++steps % 9 == 0) AssertAFullBuildChangesNothing(session, app, $"{width} wide");
            }
            for (int width = 860; width <= 1760; width += 13)
            {
                session.Window.SetClientSize(width, 760 + (width - 860) / 2);
                session.PumpFrames(2);
                if (++steps % 9 == 0) AssertAFullBuildChangesNothing(session, app, $"{width} wide");
            }
            AssertAFullBuildChangesNothing(session, app, "the last width");
        }, requests: view is "discover" or "discover title" or "discover all" or "genres" or "companies" or "person" or "requests");
    }

    // ── Views ────────────────────────────────────────────────────────────

    private static void Show(HeadlessSession session, ApertureApp app, string view)
    {
        void Go(Route route)
        {
            app.Navigate(route);
            session.PumpFrames(3);
            Rest(session);
        }
        switch (view)
        {
            case "home": Rest(session); break;
            case "library": OpenGrid(session); break;
            case "library list":
                OpenGrid(session);
                app.SetLayout(LibraryLayout.List);
                Rest(session);
                break;
            case "library no titles":
                app.Settings.Data.ShowCardTitles = false;
                OpenGrid(session);
                break;
            case "detail": Go(new Route(ApertureView.Detail, "m001", "Film number 001")); break;
            case "favourites": Go(new Route(ApertureView.Favourites)); break;
            case "continue": Go(new Route(ApertureView.ContinueWatching)); break;
            case "search":
                Go(new Route(ApertureView.Search));
                session.Window.TypeText("Film number 01");
                Rest(session);
                break;
            case "settings": Go(new Route(ApertureView.Settings)); break;
            case "discover": Go(new Route(ApertureView.Discover)); break;
            case "discover title": Go(new Route(ApertureView.DiscoverTitle, "movie:603", "Discovered film 3")); break;
            case "discover all": Go(new Route(ApertureView.DiscoverAll, "1", "Popular")); break;
            case "genres": Go(new Route(ApertureView.DiscoverGenres, "movie")); break;
            case "companies": Go(new Route(ApertureView.DiscoverCompanies, "movie")); break;
            case "person": Go(new Route(ApertureView.DiscoverPerson, "287", "A person")); break;
            case "requests": Go(new Route(ApertureView.Requests)); break;
        }
    }

    private static void OpenGrid(HeadlessSession session)
    {
        session.PumpUntil(() => session.Window.FindByAutomationId("rail-films") is not null, TimeSpan.FromSeconds(10));
        session.Window.FindByAutomationId("rail-films")!.Click();
        session.PumpUntil(() => session.Window.FindByAutomationId("card-m000") is not null, TimeSpan.FromSeconds(10));
        Rest(session);
    }

    private static void Rest(HeadlessSession session)
    {
        session.PumpFrames(3);
        Thread.Sleep(30);
        session.PumpFrames(3);
    }

    private static VirtualListElement<ApertureApp.GridRowItem> Grid(HeadlessSession session) =>
        Descendants(Root(session)).OfType<VirtualListElement<ApertureApp.GridRowItem>>().Single();

    private static float FirstRowY(HeadlessSession session) =>
        Cards(session, "library-grid").Min(c => c.Layout.AbsoluteBorderBox.Y);

    private static List<BoxElement> Cards(HeadlessSession session, string _) =>
        Descendants(Grid(session)).OfType<BoxElement>()
            .Where(e => e.AutomationId?.StartsWith("card-", StringComparison.Ordinal) == true).ToList();

    // ── The oracle ───────────────────────────────────────────────────────

    private static void AssertAFullBuildChangesNothing(HeadlessSession session, ApertureApp app, string when)
    {
        session.PumpFrames(2);
        string kept = Sig(Root(session));
        typeof(Component).GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);
        session.PumpFrames(2);
        string full = Sig(Root(session));
        if (kept == full) return;
        var xl = kept.Split('\n');
        var yl = full.Split('\n');
        for (int l = 0; l < Math.Max(xl.Length, yl.Length); l++)
            if (l >= xl.Length || l >= yl.Length || xl[l] != yl[l])
                throw new Xunit.Sdk.XunitException($"the window is not what a full build makes ({when}), line {l}:\n"
                    + $"  kept: {(l < xl.Length ? xl[l] : "<none>")}\n  full: {(l < yl.Length ? yl[l] : "<none>")}");
    }

    /// <summary>What a subtree shows: element kinds, text, names and where each element lies.</summary>
    internal static string Sig(Element e)
    {
        var sb = new StringBuilder();
        void Walk(Element el, int depth)
        {
            sb.Append(' ', depth).Append(el.GetType().Name);
            if (el is TextElement t) sb.Append(" text=").Append(t.Content);
            if (el.AccessibleName is { } n) sb.Append(" name=").Append(n);
            if (el.AutomationId is { } id) sb.Append(" id=").Append(id);
            var box = el.Layout.AbsoluteBorderBox;
            sb.Append(FormattableString.Invariant($" @{box.X:0.##},{box.Y:0.##} {box.Width:0.##}x{box.Height:0.##}"));
            sb.Append('\n');
            foreach (var child in el.Children) Walk(child, depth + 1);
        }
        Walk(e, 0);
        return sb.ToString();
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    private sealed class NoArtwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    private static FakeRequestServer RequestServer()
    {
        var server = new FakeRequestServer(null, new RequestUser(1, "tester", RequestPermission.Admin));
        var keys = new List<string>();
        for (int i = 0; i < 30; i++)
        {
            server.AddMovie(600 + i, $"Discovered film {i}", 1990 + i % 30,
                shelves: [DiscoverShelf.Trending, DiscoverShelf.PopularMovies]);
            keys.Add($"movie:{600 + i}");
        }
        for (int i = 0; i < 14; i++) server.AddGenre(DiscoverKind.Movie, 100 + i, $"Genre {i}", [.. keys.Take(3)]);
        return server;
    }

    private static void Host(Action<HeadlessSession, ApertureApp> body, bool requests = false)
    {
        var library = new FakeMediaLibrary().AddLibrary("movies", "Films", LibraryKind.Movies);
        for (int i = 0; i < 600; i++)
            library.AddMovie($"m{i:000}", $"Film number {i:000}", "movies", 1950 + i % 70, 80 + i % 60,
                resume: i % 9 == 4 ? TimeSpan.FromMinutes(20) : null);
        library.ResumeIds.AddRange(["m004", "m013", "m022", "m031", "m040", "m049", "m058"]);

        using var session = HeadlessSession.Create(1440, 900);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                posterHttp: new HttpClient(new NoArtwork()), requests: requests ? RequestServer() : null);
            session.Application.SetRoot(app.Mount());
            session.PumpFrames(3);
            body(session, app);
        }
        finally
        {
            app?.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(settingsPath)!, true); } catch (IOException) { }
        }
    }

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

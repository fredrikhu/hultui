using System.Reflection;
using Aperture;
using Aperture.Core;
using Aperture.Core.Testing;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// A library loads a page of 100 at a time. The grid asked for pages by the window's height
/// alone, so at 1440×900 a library of 250 films loaded 100 and End, the arrows and the wheel all
/// stopped at the 100th (since the grid first paged, f7b3f0a); and once the total was known, a
/// page that landed did not rebuild the view. The next page is now asked for when the rows
/// scrolled to or the cursor come within a page of what has loaded, End asks for the rest, and
/// a landed page shows by itself.
/// </summary>
public class LibraryPagingTests
{
    [Theory]
    [InlineData(250)]
    [InlineData(1000)]
    public void End_ReachesTheLastFilm(int films)
    {
        Host(films, (session, app, library) =>
        {
            Keys(session, KeyCode.Right);   // the first press lands on the first card
            Keys(session, KeyCode.End);

            Pump(session, () => app.GridItems.Count == films && app.CursorIndex == films - 1,
                () => $"End: {app.GridItems.Count} of {films} loaded, the cursor on {app.CursorIndex}");
            session.PumpFrames(3);

            var last = Card(session, $"m{films - 1:0000}");
            Assert.NotNull(last);
            Assert.True(last!.Outline.Width > 0, "the last film is on screen but has no cursor ring");
            Assert.Equal($"{films} of {films}", BarCount(session));
            // End in a large library asks for the rest in pages of up to a thousand, not page by page.
            Assert.True(library.Calls.Count(c => c.StartsWith("GetItems(parent=movies", StringComparison.Ordinal)) <= 3,
                string.Join("\n", library.Calls.Where(c => c.StartsWith("GetItems", StringComparison.Ordinal))));
        });
    }

    [Theory]
    [InlineData(250)]
    [InlineData(1000)]
    public void TheWheel_ReachesTheEnd(int films)
    {
        Host(films, (session, app, _) =>
        {
            var grid = Grid(session)!;
            var box = grid.Layout.AbsoluteContentBox;
            float last = -1f;
            int still = 0;
            for (int turn = 0; turn < 1000 && still < 5; turn++)
            {
                session.Window.Wheel(box.X + box.Width / 2, box.Y + box.Height / 2, 0, -10);
                session.PumpFrames(3);
                grid = Grid(session)!;
                still = grid.ScrollOffsetY == last ? still + 1 : 0;
                last = grid.ScrollOffsetY;
            }

            Assert.True(films == app.GridItems.Count,
                $"the wheel stopped with {app.GridItems.Count} of {films} loaded, at {grid.ScrollOffsetY} of {grid.ContentHeight}");
            Assert.NotNull(Card(session, $"m{films - 1:0000}"));
            Assert.True(grid.ScrollOffsetY > 0 && grid.ScrollOffsetY + box.Height >= grid.ContentHeight - 1,
                $"the wheel stopped at {grid.ScrollOffsetY} of {grid.ContentHeight}");
        });
    }

    [Theory]
    [InlineData(250)]
    [InlineData(1000)]
    public void TheArrows_ReachTheEnd(int films)
    {
        Host(films, (session, app, _) =>
        {
            Keys(session, KeyCode.Right);
            int at = -1;
            for (int press = 0; press < 400 && app.CursorIndex != at; press++)
            {
                at = app.CursorIndex;
                Keys(session, KeyCode.Down);
            }

            Assert.Equal(films, app.GridItems.Count);
            Assert.Equal(films - 1, app.CursorIndex);
            Assert.True(Card(session, $"m{films - 1:0000}")!.Outline.Width > 0, "the last film has no cursor ring");
        });
    }

    /// <summary>A page that lands shows by itself: the second page fills the row the first left
    /// part full, and the count says so, with no key, no wheel and no other rebuild.</summary>
    [Fact]
    public void APageThatLands_ShowsWithoutAnotherInput()
    {
        Host(150, (session, app, _) =>
        {
            Assert.Equal(150, app.GridItems.Count);   // the second page has landed
            int across = Cards(Rows(session)[0]).Count;
            int partial = 100 / across;               // the row the first page left part full
            Assert.Equal(across, Cards(Rows(session)[partial]).Count);
            Assert.Equal("150 of 150", BarCount(session));
        }, width: 2400, height: 2400);
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    private static void Host(int films, Action<HeadlessSession, ApertureApp, FakeMediaLibrary> body,
        int width = 1440, int height = 900)
    {
        var library = new FakeMediaLibrary().AddLibrary("movies", "Films", LibraryKind.Movies);
        for (int i = 0; i < films; i++)
            library.AddMovie($"m{i:0000}", $"Film number {i:0000}", "movies", 1950 + i % 70, 80 + i % 60);

        using var session = HeadlessSession.Create(width, height);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            // Artwork never answers: the cards keep their placeholders, which is all paging needs.
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                posterHttp: new HttpClient(new NoArtwork()));
            session.Application.SetRoot(app.Mount());
            session.PumpUntil(() => session.Window.FindByAutomationId("rail-films") is not null, TimeSpan.FromSeconds(10));
            session.Window.FindByAutomationId("rail-films")!.Click();
            session.PumpUntil(() => Grid(session) is { } g && g.Children.Count > 1, TimeSpan.FromSeconds(10));
            session.PumpFrames(6);
            body(session, app, library);
        }
        finally
        {
            app?.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(settingsPath)!, true); } catch (IOException) { }
        }
    }

    private static void Keys(HeadlessSession session, KeyCode key)
    {
        session.Window.PressKey(key);
        session.PumpFrames(3);
    }

    /// <summary>Pumps frames until <paramref name="done"/>, at most 600 of them.</summary>
    private static void Pump(HeadlessSession session, Func<bool> done, Func<string> what)
    {
        for (int i = 0; i < 600 && !done(); i++) session.PumpFrame();
        Assert.True(done(), what());
    }

    /// <summary>Artwork that never comes: a picture that landed (or failed) would rebuild the
    /// grid, and the tests here must see only what paging does.</summary>
    private sealed class NoArtwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    // ── Reading the screen ───────────────────────────────────────────────

    private static ScrollViewElement? Grid(HeadlessSession session) =>
        Descendants(Root(session)).OfType<ScrollViewElement>().FirstOrDefault(e => e.AutomationId == "library-grid");

    /// <summary>The realized rows, top to bottom (the list's first child is its spacer).</summary>
    private static List<Element> Rows(HeadlessSession session) => Grid(session)!.Children.Skip(1).ToList();

    private static List<BoxElement> Cards(Element row) =>
        Descendants(row).OfType<BoxElement>().Where(e => e.AutomationId?.StartsWith("card-", StringComparison.Ordinal) == true).ToList();

    private static BoxElement? Card(HeadlessSession session, string id) =>
        Rows(session).SelectMany(Cards).SingleOrDefault(c => c.AutomationId == "card-" + id);

    /// <summary>The bar's count, "loaded of total".</summary>
    private static string? BarCount(HeadlessSession session) =>
        Descendants(Root(session)).OfType<TextElement>().Select(t => t.Content)
            .FirstOrDefault(t => t.Contains(" of ", StringComparison.Ordinal) && char.IsDigit(t[0]));

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

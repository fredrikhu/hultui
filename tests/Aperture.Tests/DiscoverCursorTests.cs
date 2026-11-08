using System.Diagnostics;
using System.Reflection;
using Aperture;
using Aperture.Core;
using Aperture.Core.Requests;
using Aperture.Core.Testing;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// Discover's cursor (plan 17 round 8 step 5), as Home's (HomeCursorTests): a cursor key moves the
/// ring and the keyboard on the live cards and builds nothing, and a build hands back every card
/// whose title, place, cursor and picture are what it was made with. A key used to build all of
/// Discover, every shelf's cards with it. Five shelves of seven titles, so Down reaches the next
/// shelf.
/// </summary>
[Collection("ApertureProcessWide")]
public class DiscoverCursorTests
{
    private const string Untouched = "untouched";

    [Fact]
    public void ACursorKey_BuildsNothing_AndMovesTheRingAndTheKeyboard()
    {
        Host((session, app) =>
        {
            Keys(session, KeyCode.Right);   // the first press lands on the first card
            int builds = app.Builds;
            var cards = Cards(session);
            Stamp(cards);

            // Along the first shelf, on into the second, down to the third and back up.
            foreach (var key in new[] { KeyCode.Right, KeyCode.Right, KeyCode.Right, KeyCode.Right, KeyCode.Right,
                         KeyCode.Right, KeyCode.Right, KeyCode.Down, KeyCode.Up, KeyCode.Left })
                Keys(session, key);

            Assert.Equal(builds, app.Builds);
            Assert.Equal(cards, Cards(session));
            Assert.All(cards, c => Assert.Equal(Untouched, c.AccessibleDescription));
            AssertRingAndKeyboardOnTheCursor(session, app);
        });
    }

    /// <summary>A build after a walk (a picture landing asks for one) makes the card the ring moved
    /// onto, whose cursor moved since it was made, and hands back the rest untouched.</summary>
    [Fact]
    public void ABuildAfterAWalk_MakesOnlyTheCardTheRingMovedOnto()
    {
        Host((session, app) =>
        {
            Keys(session, KeyCode.Right);
            Keys(session, KeyCode.Right);
            Keys(session, KeyCode.Right);
            var cards = Cards(session);
            Stamp(cards);
            int builds = app.Builds;

            Rebuild(app);
            session.PumpFrames(2);

            Assert.Equal(builds + 1, app.Builds);
            Assert.Equal(cards, Cards(session));
            Assert.Equal([cards[2].AutomationId], cards.Where(c => c.AccessibleDescription is null).Select(c => c.AutomationId));
            AssertRingAndKeyboardOnTheCursor(session, app);

            // The ring moves on the live cards again after the build.
            Keys(session, KeyCode.Right);
            Assert.Equal(builds + 1, app.Builds);
            Assert.Same(cards[3], session.Window.Window.FocusedElement);
            AssertRingAndKeyboardOnTheCursor(session, app);
        });
    }

    private static void AssertRingAndKeyboardOnTheCursor(HeadlessSession session, ApertureApp app)
    {
        var cards = Cards(session);
        var ringed = Assert.Single(cards, c => c.Outline.Width > 0);
        Assert.Equal(app.CursorIndex, cards.IndexOf(ringed));
        Assert.Same(ringed, session.Window.Window.FocusedElement);
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    internal static void Host(Action<HeadlessSession, ApertureApp> body, int titles = 7, int width = 1440)
    {
        var library = new FakeMediaLibrary().AddLibrary("movies", "Films", LibraryKind.Movies);
        for (int i = 0; i < 10; i++)
            library.AddMovie($"m{i:000}", $"Film {i:000}", "movies", 1950 + i, 80 + i);
        var server = new FakeRequestServer(null, new RequestUser(1, "tester", RequestPermission.Admin));
        var shelves = Enum.GetValues<DiscoverShelf>();
        for (int s = 0; s < shelves.Length; s++)
            for (int i = 0; i < titles; i++)
                server.AddMovie(1000 + s * 100 + i, $"Discovered {s}-{i:00}", 1990 + i, shelves: [shelves[s]]);

        using var session = HeadlessSession.Create(width, 900);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                posterHttp: new HttpClient(new NoArtwork()), requests: server);
            session.Application.SetRoot(app.Mount());
            session.PumpFrames(3);
            Settle(session);
            app.Navigate(new Route(ApertureView.Discover));
            session.PumpUntil(() => Cards(session).Count >= 5 * Math.Min(titles, 4), TimeSpan.FromSeconds(10));
            Settle(session);
            body(session, app);
        }
        finally
        {
            app?.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(settingsPath)!, true); } catch (IOException) { }
        }
    }

    internal static void Settle(HeadlessSession session)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < 300) { session.PumpFrame(); Thread.Sleep(5); }
        session.PumpFrames(2);
    }

    internal static void Keys(HeadlessSession session, KeyCode key)
    {
        session.Window.PressKey(key);
        session.PumpFrames(2);
    }

    /// <summary>A view-only rebuild, as a picture landing asks for.</summary>
    internal static void Rebuild(ApertureApp app) =>
        typeof(ApertureApp).GetMethod("RerenderView", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);

    /// <summary>A patch copies what the build gave a card, so a mark no build gave it stays only on the
    /// cards nothing patched.</summary>
    private static void Stamp(IEnumerable<BoxElement> cards)
    {
        foreach (var card in cards) card.AccessibleDescription = Untouched;
    }

    /// <summary>The cards in the walk's order: the shelves top to bottom, each left to right.</summary>
    internal static List<BoxElement> Cards(HeadlessSession session) =>
        Descendants(Root(session)).OfType<BoxElement>()
            .Where(e => e.AutomationId is { } id && (id.StartsWith("discover-card-", StringComparison.Ordinal)
                || id.StartsWith("genre-", StringComparison.Ordinal) || id.StartsWith("company-", StringComparison.Ordinal)))
            .ToList();

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

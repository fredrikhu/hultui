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
/// Home's cursor (plan 17 round 7 step 11). A cursor key moves the ring and the keyboard on the live
/// cards and builds nothing; a build hands back every card whose item, place, cursor and picture are
/// what it was made with, so one after a walk makes only the cards the walk moved the ring between.
/// Five shelves of seven films, and a "Continue watching" shelf that shares a film with one of them.
/// </summary>
[Collection("ApertureProcessWide")]
public class HomeCursorTests
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
            var ringed = Assert.Single(Cards(session), c => c.Outline.Width > 0);
            Assert.Same(ringed, session.Window.Window.FocusedElement);
            Assert.Equal(app.CursorIndex, Walk(session).IndexOf(ringed));
        });
    }

    /// <summary>A build after a walk (a picture landing asks for one) makes the card the ring moved
    /// onto, whose cursor moved since it was made, and hands back the rest untouched, the same film on
    /// another shelf among them.</summary>
    [Fact]
    public void ABuildAfterAWalk_MakesOnlyTheCardsWhoseCursorMoved()
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
            var walk = Walk(session);
            Assert.Equal([walk[2].AutomationId], cards.Where(c => c.AccessibleDescription is null).Select(c => c.AutomationId));
            Assert.True(walk[2].Outline.Width > 0);
            Assert.Same(walk[2], session.Window.Window.FocusedElement);
            Assert.Contains(cards, c => c.AutomationId == "resume-m003");
            Assert.Contains(cards, c => c.AutomationId == "card-m003");

            // The ring moves on the live cards again after the build.
            Keys(session, KeyCode.Right);
            Assert.Equal(builds + 1, app.Builds);
            Assert.Same(walk[3], session.Window.Window.FocusedElement);
            Assert.Single(Cards(session), c => c.Outline.Width > 0);
        });
    }

    /// <summary>Into a film and back: Home is made again, the cursor where it was, and the next key
    /// moves the ring with no build.</summary>
    [Fact]
    public void BackFromAFilm_TheCursorWalksOnWithoutABuild()
    {
        Host((session, app) =>
        {
            Keys(session, KeyCode.Right);
            Keys(session, KeyCode.Down);
            Keys(session, KeyCode.Enter);
            Settle(session);
            Keys(session, KeyCode.Escape);
            Settle(session);
            int builds = app.Builds;
            int at = app.CursorIndex;

            Keys(session, KeyCode.Right);

            Assert.Equal(builds, app.Builds);
            var walk = Walk(session);
            Assert.True(walk[at + 1].Outline.Width > 0);
            Assert.False(walk[at].Outline.Width > 0);
            Assert.Same(walk[at + 1], session.Window.Window.FocusedElement);
        });
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    private static void Host(Action<HeadlessSession, ApertureApp> body)
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

        using var session = HeadlessSession.Create(1440, 900);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                posterHttp: new HttpClient(new NoArtwork()));
            session.Application.SetRoot(app.Mount());
            session.PumpUntil(() => Cards(session).Count == 36, TimeSpan.FromSeconds(10));
            Settle(session);
            body(session, app);
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

    private static void Keys(HeadlessSession session, KeyCode key)
    {
        session.Window.PressKey(key);
        session.PumpFrames(2);
    }

    /// <summary>A view-only rebuild, as a picture landing asks for.</summary>
    private static void Rebuild(ApertureApp app) =>
        typeof(ApertureApp).GetMethod("RerenderView", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);

    /// <summary>A patch copies what the build gave a card, so a mark no build gave it stays only on the
    /// cards nothing patched.</summary>
    private static void Stamp(IEnumerable<BoxElement> cards)
    {
        foreach (var card in cards) card.AccessibleDescription = Untouched;
    }

    private static List<BoxElement> Cards(HeadlessSession session) =>
        Descendants(Root(session)).OfType<BoxElement>()
            .Where(e => e.AutomationId is { } id && (id.StartsWith("card-", StringComparison.Ordinal) || id.StartsWith("resume-", StringComparison.Ordinal)))
            .ToList();

    /// <summary>The cards in the walk's order: the shelves top to bottom, each left to right.</summary>
    private static List<BoxElement> Walk(HeadlessSession session) => Cards(session);

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

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
/// The cursor on a shelf that pages (plan 17 round 8 step 5): a card on another page of its shelf
/// turns the shelf to that page, with one build, and takes the ring and the keyboard. It used to
/// walk on with no card ringed and the keyboard left behind. And a move that builds takes the ring
/// off the live card it leaves first, as a build hands that card back as it was made.
/// </summary>
[Collection("ApertureProcessWide")]
public class ShelfPageTests
{
    /// <summary>Home at 1000 px, four films a page: along the first library shelf onto its second
    /// page, on into the next shelf and back. (The cards a turned page shows ask for their pictures,
    /// and the answers build Home again: the counts are taken once those are in.)</summary>
    [Fact]
    public void OnHome_ACardOnTheNextPage_TurnsTheShelf()
    {
        HomeHost((session, app) =>
        {
            Keys(session, KeyCode.Right);   // the resume shelf's one card
            for (int i = 0; i < 4; i++) Keys(session, KeyCode.Right);   // the first page of "Shelf 0"
            AssertRingAndKeyboardOnTheCursor(session, app);
            var onFirstPage = app.CursorItem?.Id;

            int builds = app.Builds;
            Keys(session, KeyCode.Right);
            Assert.True(app.Builds > builds, "the move onto the next page built nothing");
            AssertRingAndKeyboardOnTheCursor(session, app);
            DiscoverCursorTests.Settle(session);

            builds = app.Builds;
            foreach (var key in new[] { KeyCode.Right, KeyCode.Right, KeyCode.Right, KeyCode.Left, KeyCode.Left, KeyCode.Left })
            {
                Keys(session, key);
                AssertRingAndKeyboardOnTheCursor(session, app);
            }
            Assert.Equal(builds, app.Builds);   // the page stays turned; the next shelf starts on its first

            Keys(session, KeyCode.Left);
            Assert.True(app.Builds > builds, "the move back onto the first page built nothing");
            Assert.Equal(onFirstPage, app.CursorItem?.Id);
            AssertRingAndKeyboardOnTheCursor(session, app);
        }, width: 1000);
    }

    /// <summary>A watchlist title card is made by every build, so the move onto one builds: the card
    /// the ring leaves, which the ring reached with no build, loses it.</summary>
    [Fact]
    public void OnHome_AMoveOntoATitleCard_LeavesOneRing()
    {
        HomeHost((session, app) =>
        {
            Keys(session, KeyCode.Right);   // the resume shelf's card, with no build
            AssertRingAndKeyboardOnTheCursor(session, app);
            int builds = app.Builds;

            Keys(session, KeyCode.Right);   // the watchlist's first title
            Assert.True(app.Builds > builds, "the move onto a title card built nothing");
            Assert.IsType<DiscoverItem>(app.CursorEntry);
            AssertRingAndKeyboardOnTheCursor(session, app);

            Keys(session, KeyCode.Left);
            AssertRingAndKeyboardOnTheCursor(session, app);
        }, watchlist: 3);
    }

    /// <summary>Discover's shelves of twenty, seven a page: Right onto the second page, Down onto the
    /// third, Up and Left back, each turning the page with a build.</summary>
    [Fact]
    public void OnDiscover_ACardOnAnotherPage_TurnsTheShelf()
    {
        DiscoverCursorTests.Host((session, app) =>
        {
            Keys(session, KeyCode.Right);
            for (int i = 0; i < 6; i++) Keys(session, KeyCode.Right);
            int builds = app.Builds;
            AssertRingAndKeyboardOnTheCursor(session, app);

            foreach (var key in new[] { KeyCode.Right, KeyCode.Down, KeyCode.Up, KeyCode.Left })
            {
                DiscoverCursorTests.Settle(session);   // the pictures a turned page asked for are in
                builds = app.Builds;
                Keys(session, key);
                Assert.True(app.Builds > builds, $"{key} onto another page built nothing");
                AssertRingAndKeyboardOnTheCursor(session, app);
            }
            Assert.Equal(6, app.CursorIndex);
        }, titles: 20);
    }

    private static void AssertRingAndKeyboardOnTheCursor(HeadlessSession session, ApertureApp app)
    {
        var ringed = Assert.Single(Cards(session), c => c.Outline.Width > 0);
        string id = app.CursorEntry switch
        {
            MediaItem item => item.Id,
            DiscoverItem title => title.Key,
            _ => throw new Xunit.Sdk.XunitException($"the cursor is on {app.CursorEntry}"),
        };
        Assert.EndsWith("-" + id, ringed.AutomationId);
        Assert.Same(ringed, session.Window.Window.FocusedElement);
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    /// <summary>Home as HomeCursorTests has it: five shelves of seven films and a "Continue watching"
    /// shelf, and a watchlist of <paramref name="watchlist"/> titles when there is one.</summary>
    private static void HomeHost(Action<HeadlessSession, ApertureApp> body, int width = 1440, int watchlist = 0)
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
        FakeRequestServer? server = null;
        if (watchlist > 0)
        {
            server = new FakeRequestServer(null, new RequestUser(1, "tester", RequestPermission.Admin));
            for (int i = 0; i < watchlist; i++)
            {
                server.AddMovie(2000 + i, $"Wanted {i}", 2000 + i);
                server.AddToWatchlist($"movie:{2000 + i}");
            }
        }

        using var session = HeadlessSession.Create(width, 900);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                posterHttp: new HttpClient(new NoArtwork()), requests: server);
            session.Application.SetRoot(app.Mount());
            session.PumpUntil(() => Cards(session).Count(c => c.AutomationId!.StartsWith("card-", StringComparison.Ordinal)) >= 20
                && (watchlist == 0 || Cards(session).Any(c => c.AutomationId!.StartsWith("discover-card-", StringComparison.Ordinal))),
                TimeSpan.FromSeconds(10));
            DiscoverCursorTests.Settle(session);
            body(session, app);
        }
        finally
        {
            app?.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(settingsPath)!, true); } catch (IOException) { }
        }
    }

    private static void Keys(HeadlessSession session, KeyCode key) => DiscoverCursorTests.Keys(session, key);

    private static List<BoxElement> Cards(HeadlessSession session) =>
        Descendants(Root(session)).OfType<BoxElement>()
            .Where(e => e.AutomationId is { } id && (id.StartsWith("card-", StringComparison.Ordinal)
                || id.StartsWith("resume-", StringComparison.Ordinal) || id.StartsWith("discover-card-", StringComparison.Ordinal)))
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

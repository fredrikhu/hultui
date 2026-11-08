using System.Diagnostics;
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
/// The library grid's cursor (plan 17 round 8 step 5), as Home's (HomeCursorTests). A cursor key
/// whose card is in a realized row moves the ring and the keyboard on the live cards and builds
/// nothing; one that reaches a row not made yet, or comes within a page of the loaded end, builds,
/// and the card it reaches takes the keyboard once its row is made. A key used to build the view,
/// and the card it reached never took the keyboard.
/// </summary>
[Collection("ApertureProcessWide")]
public class GridCursorTests
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

            foreach (var key in new[] { KeyCode.Right, KeyCode.Right, KeyCode.Down, KeyCode.Down, KeyCode.Down, KeyCode.Left, KeyCode.Up })
                Keys(session, key);

            Assert.Equal(builds, app.Builds);
            // The rows still on screen are the rows they were, their cards untouched: the ring moved
            // on the live ones. The rows the walk scrolled in were made in the frame.
            Assert.All(cards.Where(c => c.IsHosted), c => Assert.Equal(Untouched, c.AccessibleDescription));
            AssertRingAndKeyboardOnTheCursor(session, app);
        });
    }

    /// <summary>A build after a walk (a picture landing asks for one) makes the card the ring moved
    /// onto since the build before, whose cursor moved since it was made, and hands back the rest;
    /// the ring and the keyboard stay on the cursor's card, and the next key builds nothing.</summary>
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

            Rebuild(session, app);

            Assert.Equal(builds + 1, app.Builds);
            Assert.Equal(cards, Cards(session));
            Assert.Equal(["card-m002"], cards.Where(c => c.AccessibleDescription is null).Select(c => c.AutomationId));
            AssertRingAndKeyboardOnTheCursor(session, app);

            Keys(session, KeyCode.Right);
            Assert.Equal(builds + 1, app.Builds);
            Assert.Equal("card-m003", session.Window.Window.FocusedElement?.AutomationId);
            AssertRingAndKeyboardOnTheCursor(session, app);
        });
    }

    /// <summary>
    /// End reaches a row not made yet: the key builds, and the last card takes the ring and, once its
    /// row is made, the keyboard. The ring the walk moved without a build is gone from the card it
    /// was on: the list keeps a row while its item is equal, and a row the ring reached with no build
    /// has the same item as before. Home goes back the same way.
    /// </summary>
    [Fact]
    public void AMoveToARowNotMade_BuildsAndHandsTheKeyboardOver()
    {
        Host((session, app) =>
        {
            Keys(session, KeyCode.Right);
            Keys(session, KeyCode.Right);
            int builds = app.Builds;

            Keys(session, KeyCode.End);

            Assert.True(app.Builds > builds, "End built nothing");
            Assert.Equal(app.GridItems.Count - 1, app.CursorIndex);
            AssertRingAndKeyboardOnTheCursor(session, app);

            builds = app.Builds;
            Keys(session, KeyCode.Home);
            Keys(session, KeyCode.Right);

            Assert.True(app.Builds > builds, "Home built nothing");
            Assert.Equal(1, app.CursorIndex);
            AssertRingAndKeyboardOnTheCursor(session, app);
        });
    }

    /// <summary>
    /// A move that builds after one that did not: the list keeps a row while its item is equal, and
    /// its items are its last build's, so the row the ring reached without a build has the same item
    /// in the build after it and is not made again. The ring leaves that card on the live cards, as
    /// the move to it came.
    /// </summary>
    [Fact]
    public void AMoveThatBuilds_AfterOneThatDidNot_LeavesOneRing()
    {
        Host((session, app) =>
        {
            Keys(session, KeyCode.Right);
            int builds = app.Builds;
            Keys(session, KeyCode.PageDown);   // three rows down: made, below the screen
            Assert.Equal(builds, app.Builds);
            var left = Assert.Single(Cards(session), c => c.Outline.Width > 0);

            Keys(session, KeyCode.PageDown);   // three more: not made yet
            Assert.True(app.Builds > builds, "the move to a row not made built nothing");
            Assert.True(left.IsHosted, "the row the ring left scrolled away; the test needs it on screen");

            AssertRingAndKeyboardOnTheCursor(session, app);
            Keys(session, KeyCode.Left);
            AssertRingAndKeyboardOnTheCursor(session, app);
        });
    }

    /// <summary>Within a page of the loaded end a key builds, as it did, and the build asks for the
    /// next page: here the first page of a thousand films is in and the server does not answer for
    /// the rest, so every place is within a page of the end. (A headless session runs the loads in
    /// its pump, so a page cannot be held back without holding the pump.)</summary>
    [Fact]
    public void NearTheLoadedEnd_AKeyBuilds()
    {
        Host((session, app) =>
        {
            Assert.Equal(ApertureApp.GridPageSize, app.GridItems.Count);
            Assert.True(app.GridHasMore);
            Keys(session, KeyCode.Right);
            for (int i = 0; i < 3; i++)
            {
                int builds = app.Builds;
                Keys(session, KeyCode.Right);
                Assert.True(app.Builds > builds, $"a key to {app.CursorIndex} of {app.GridItems.Count} loaded built nothing");
                AssertRingAndKeyboardOnTheCursor(session, app);
            }
        }, library: new FirstPageOnlyLibrary(1000));
    }

    private static void AssertRingAndKeyboardOnTheCursor(HeadlessSession session, ApertureApp app)
    {
        var ringed = Assert.Single(Cards(session), c => c.Outline.Width > 0);
        Assert.Equal("card-" + app.GridItems[app.CursorIndex].Id, ringed.AutomationId);
        Assert.Same(ringed, session.Window.Window.FocusedElement);
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    private static FakeMediaLibrary Films(int films)
    {
        var library = new FakeMediaLibrary().AddLibrary("movies", "Films", LibraryKind.Movies);
        for (int i = 0; i < films; i++)
            library.AddMovie($"m{i:000}", $"Film number {i:000}", "movies", 1950 + i % 70, 80 + i % 60);
        return library;
    }

    private static void Host(Action<HeadlessSession, ApertureApp> body, IMediaLibrary? library = null)
    {
        library ??= Films(60);

        using var session = HeadlessSession.Create(1440, 900);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                posterHttp: new HttpClient(new NoArtwork()));
            session.Application.SetRoot(app.Mount());
            session.PumpUntil(() => session.Window.FindByAutomationId("rail-films") is not null, TimeSpan.FromSeconds(10));
            session.Window.FindByAutomationId("rail-films")!.Click();
            session.PumpUntil(() => Cards(session).Count > 20, TimeSpan.FromSeconds(10));
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
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < 300) { session.PumpFrame(); Thread.Sleep(5); }
        session.PumpFrames(2);
    }

    private static void Keys(HeadlessSession session, KeyCode key)
    {
        session.Window.PressKey(key);
        session.PumpFrames(3);
    }

    /// <summary>A view-only rebuild, as a picture landing asks for, and the frames that make its rows.</summary>
    private static void Rebuild(HeadlessSession session, ApertureApp app)
    {
        typeof(ApertureApp).GetMethod("RerenderView", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);
        session.PumpFrames(3);
    }

    /// <summary>A patch copies what the build gave a card, so a mark no build gave it stays only on the
    /// cards nothing patched.</summary>
    private static void Stamp(IEnumerable<BoxElement> cards)
    {
        foreach (var card in cards) card.AccessibleDescription = Untouched;
    }

    private static List<BoxElement> Cards(HeadlessSession session) =>
        Descendants(Root(session)).OfType<BoxElement>()
            .Where(e => e.AutomationId?.StartsWith("card-", StringComparison.Ordinal) == true)
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

    /// <summary>A library of films that answers for the first page of a list and times out on the rest.</summary>
    private sealed class FirstPageOnlyLibrary(int films) : IMediaLibrary
    {
        private readonly FakeMediaLibrary _inner = Films(films);

        public ServerInfo Server => _inner.Server;
        public SignedInUser User => _inner.User;

        public Task<ApertureResult<ItemPage>> GetItemsAsync(ItemQuery query, CancellationToken ct = default) =>
            query.StartIndex > 0
                ? Task.FromResult(ApertureResult<ItemPage>.Fail(ApertureFailureKind.Timeout, "no answer"))
                : _inner.GetItemsAsync(query, ct);

        public Task<ApertureResult<IReadOnlyList<LibraryView>>> GetLibrariesAsync(CancellationToken ct = default) => _inner.GetLibrariesAsync(ct);
        public Task<ApertureResult<IReadOnlyList<MediaItem>>> GetResumeAsync(int limit = 12, CancellationToken ct = default) => _inner.GetResumeAsync(limit, ct);
        public Task<ApertureResult<IReadOnlyList<MediaItem>>> GetNextUpAsync(int limit = 12, string? seriesId = null, CancellationToken ct = default) =>
            _inner.GetNextUpAsync(limit, seriesId, ct);
        public Task<ApertureResult<MediaItem?>> GetNextEpisodeAsync(MediaItem episode, CancellationToken ct = default) => _inner.GetNextEpisodeAsync(episode, ct);
        public Task<ApertureResult<IReadOnlyList<MediaItem>>> GetLatestAsync(string libraryId, int limit = 16, CancellationToken ct = default) =>
            _inner.GetLatestAsync(libraryId, limit, ct);
        public Task<ApertureResult<IReadOnlyList<MediaGenre>>> GetGenresAsync(string libraryId, CancellationToken ct = default) => _inner.GetGenresAsync(libraryId, ct);
        public Task<ApertureResult<MediaItem>> GetItemAsync(string itemId, CancellationToken ct = default) => _inner.GetItemAsync(itemId, ct);
        public Uri? ImageUrl(string itemId, ImageKind kind, string? tag, int? maxWidth = null, int? maxHeight = null) =>
            _inner.ImageUrl(itemId, kind, tag, maxWidth, maxHeight);
        public Task<ApertureResult<PlaybackTarget>> StartPlaybackAsync(string itemId, TimeSpan startPosition, int? maxBitrateBps = null, CancellationToken ct = default) =>
            _inner.StartPlaybackAsync(itemId, startPosition, maxBitrateBps, ct);
        public Task<ApertureResult<bool>> ReportStartedAsync(PlaybackTarget target, TimeSpan position, bool paused, CancellationToken ct = default) =>
            _inner.ReportStartedAsync(target, position, paused, ct);
        public Task<ApertureResult<bool>> ReportProgressAsync(PlaybackTarget target, TimeSpan position, bool paused, CancellationToken ct = default) =>
            _inner.ReportProgressAsync(target, position, paused, ct);
        public Task<ApertureResult<bool>> ReportStoppedAsync(PlaybackTarget target, TimeSpan position, CancellationToken ct = default) =>
            _inner.ReportStoppedAsync(target, position, ct);
        public Task<ApertureResult<bool>> SetPlayedAsync(string itemId, bool played, CancellationToken ct = default) => _inner.SetPlayedAsync(itemId, played, ct);
        public Task<ApertureResult<bool>> SetFavoriteAsync(string itemId, bool favorite, CancellationToken ct = default) => _inner.SetFavoriteAsync(itemId, favorite, ct);

        public void Dispose() => _inner.Dispose();
    }
}

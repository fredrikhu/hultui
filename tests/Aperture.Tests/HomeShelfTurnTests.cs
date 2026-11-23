using System.Reflection;
using System.Text;
using Aperture;
using Aperture.Core;
using Aperture.Core.Testing;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// Each of Home's shelves of library cards is a part of its own (plan 17 round 18 step 9; the perf suite's
/// proposal (j), here because the suite hosts no app): a turn of its page builds that shelf alone. A Right
/// past a page's last card, a Down or an Up onto a card on another page of its shelf, and the shelf's Next
/// and Previous used to build all of Home, every shelf's cards asked for again around the seven the turn
/// makes. Home here is three shelves of thirty films at 1440 px, seven a page.
/// </summary>
[Collection("ApertureProcessWide")]
public class HomeShelfTurnTests
{
    private const string Untouched = "untouched";

    /// <summary>Right past the first page, then Down and Up, then the next shelf's Next: each turn builds
    /// its shelf and nothing else, and the other shelves are the elements they were.</summary>
    [Fact]
    public void AShelfTurn_BuildsThatShelfAlone()
    {
        Host((session, app, _) =>
        {
            for (int i = 0; i < 7; i++) Keys(session, KeyCode.Right);   // onto the first page's last card
            Assert.Equal(6, app.CursorIndex);
            Assert.Equal("1/5", PageOf(session, 0));

            foreach (var (key, cursor, page) in new[] { (KeyCode.Right, 7, "2/5"), (KeyCode.Down, 14, "3/5"), (KeyCode.Up, 7, "2/5") })
            {
                Stamp(Shelf(session, 1), Shelf(session, 2));
                int builds = app.Builds, shelves = app.HomeShelvesMade;
                Keys(session, key);
                Assert.Equal(cursor, app.CursorIndex);
                Assert.Equal(page, PageOf(session, 0));
                Assert.Equal(builds, app.Builds);
                Assert.Equal(shelves + 1, app.HomeShelvesMade);
                AssertUntouched(Shelf(session, 1), Shelf(session, 2));
            }

            // The pointer's Next on the next shelf.
            Stamp(Shelf(session, 0), Shelf(session, 2));
            int was = app.Builds, made = app.HomeShelvesMade;
            session.Window.FindByAutomationId("row-latest-lib1-next")!.Click();
            session.PumpFrames(2);
            Assert.Equal("2/5", PageOf(session, 1));
            Assert.Equal(was, app.Builds);
            Assert.Equal(made + 1, app.HomeShelvesMade);
            AssertUntouched(Shelf(session, 0), Shelf(session, 2));
        });
    }

    /// <summary>The card a turn brings in takes the ring and the keyboard, as the build of Home that turned
    /// the shelf gave them, and a reader hears the card it is on; the card the cursor left has neither.</summary>
    [Fact]
    public void AShelfTurn_HandsTheCursorsCardTheKeyboard()
    {
        Host((session, app, _) =>
        {
            for (int i = 0; i < 7; i++) Keys(session, KeyCode.Right);
            // 6 → 7, 14, 21, 14 and 13: each onto another page of the first shelf.
            foreach (var key in new[] { KeyCode.Right, KeyCode.Down, KeyCode.Down, KeyCode.Up, KeyCode.Left })
            {
                int shelves = app.HomeShelvesMade;
                Keys(session, key);
                Assert.True(app.HomeShelvesMade > shelves, $"{key} turned nothing");
                AssertRingAndKeyboardOnTheCursor(session, app);
            }
            Assert.Equal(13, app.CursorIndex);
            Assert.Equal("2/5", PageOf(session, 0));
        });
    }

    /// <summary>A shelf a turn built alone still shows what the data says when it changes: a film added to
    /// its library and a film on its page renamed, as a refresh brings them, are on the page it shows.</summary>
    [Fact]
    public void AShelfWhoseItemsChange_ShowsThem()
    {
        Host((session, app, library) =>
        {
            for (int i = 0; i < 8; i++) Keys(session, KeyCode.Right);   // shelf 0 turned to its second page
            Assert.Equal("2/5", PageOf(session, 0));
            Assert.Equal(Ids(22, 16), CardIds(session, 0));

            library.AddMovie("x0new", "Newest film", "lib0", 2000, 90);
            library.AddItem(library.Item("x020")! with { Name = "Renamed film" });
            app.Refresh();
            DiscoverCursorTests.Settle(session);

            // Every card moved one place on: the second page starts a card earlier in the list.
            Assert.Equal(Ids(23, 17), CardIds(session, 0));
            Assert.Contains(Descendants(Shelf(session, 0)).OfType<TextElement>(), t => t.Content == "Renamed film");
            Assert.Equal("2/5", PageOf(session, 0));
        });
    }

    /// <summary>The forced build (the hidden <c>MarkDirty</c>, PartTests' oracle) builds every shelf, and
    /// what it makes is what the turns left on screen: the same cards, ring and Tab stop.</summary>
    [Fact]
    public void AFullBuild_BuildsEveryShelf_AsTheTurnsLeftThem()
    {
        Host((session, app, _) =>
        {
            for (int i = 0; i < 8; i++) Keys(session, KeyCode.Right);
            Keys(session, KeyCode.Down);
            session.Window.FindByAutomationId("row-latest-lib2-next")!.Click();
            session.PumpFrames(2);
            var kept = Enumerable.Range(0, 3).Select(i => Sig(Shelf(session, i))).ToList();
            int shelves = app.HomeShelvesMade;

            typeof(ApertureApp).GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                Type.EmptyTypes)!.Invoke(app, null);
            session.PumpFrames(2);

            Assert.Equal(shelves + 3, app.HomeShelvesMade);
            for (int i = 0; i < 3; i++) Assert.Equal(kept[i], Sig(Shelf(session, i)));
        });
    }

    // ── Checks ───────────────────────────────────────────────────────────

    private static void AssertRingAndKeyboardOnTheCursor(HeadlessSession session, ApertureApp app)
    {
        var ringed = Assert.Single(Descendants(Root(session)).OfType<BoxElement>(),
            c => c.Outline.Width > 0 && c.AutomationId?.StartsWith("card-", StringComparison.Ordinal) == true);
        Assert.Equal("card-" + app.CursorItem!.Id, ringed.AutomationId);
        Assert.Same(ringed, session.Window.Window.FocusedElement);
        Assert.Equal(0, ringed.TabIndex);
    }

    private static void Stamp(params Element[] shelves)
    {
        foreach (var e in shelves.SelectMany(Descendants)) e.AccessibleDescription = Untouched;
    }

    /// <summary>A patch copies what the build gave an element, so a mark no build gave it stays only on the
    /// elements nothing patched or made again.</summary>
    private static void AssertUntouched(params Element[] shelves)
    {
        foreach (var shelf in shelves)
        {
            Assert.True(shelf.IsHosted);
            Assert.All(Descendants(shelf), e => Assert.Equal(Untouched, e.AccessibleDescription));
        }
    }

    /// <summary>What a shelf shows: kinds, text, ids, places, and each card's ring and Tab stop.</summary>
    private static string Sig(Element shelf)
    {
        var sb = new StringBuilder(WindowFitTests.Sig(shelf));
        foreach (var card in Descendants(shelf).OfType<BoxElement>())
            if (card.AutomationId?.StartsWith("card-", StringComparison.Ordinal) == true)
                sb.Append(card.AutomationId).Append(" ring=").Append(card.Outline.Width > 0).Append(" tab=").Append(card.TabIndex).Append('\n');
        return sb.ToString();
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    /// <summary>The ids of shelf 0's films from <paramref name="from"/> down to <paramref name="to"/>, as
    /// "Latest in" lists them, newest first.</summary>
    private static List<string> Ids(int from, int to) =>
        Enumerable.Range(0, from - to + 1).Select(i => $"x0{from - i:00}").ToList();

    /// <summary>Shelf <paramref name="index"/>, top to bottom: the element with its row's id.</summary>
    private static Element Shelf(HeadlessSession session, int index) =>
        Descendants(Root(session)).Single(e => e.AutomationId == $"row-latest-lib{index}");

    private static List<string> CardIds(HeadlessSession session, int index) =>
        Descendants(Shelf(session, index)).OfType<BoxElement>()
            .Select(e => e.AutomationId)
            .Where(id => id?.StartsWith("card-", StringComparison.Ordinal) == true)
            .Select(id => id!["card-".Length..])
            .ToList();

    /// <summary>The page a shelf's heading says it shows, "2/5".</summary>
    private static string? PageOf(HeadlessSession session, int index) =>
        Descendants(Shelf(session, index)).OfType<TextElement>()
            .Select(t => t.Content).FirstOrDefault(c => c.Length >= 3 && c.Contains('/') && char.IsDigit(c[0]));

    private static void Keys(HeadlessSession session, KeyCode key) => DiscoverCursorTests.Keys(session, key);

    private static void Host(Action<HeadlessSession, ApertureApp, FakeMediaLibrary> body)
    {
        var library = new FakeMediaLibrary();
        for (int l = 0; l < 3; l++)
        {
            library.AddLibrary($"lib{l}", $"Shelf {l}", LibraryKind.Movies);
            for (int i = 0; i < 30; i++)
                library.AddMovie($"x{l}{i:00}", $"Shelf film {l}-{i:00}", $"lib{l}", 1960 + i, 90 + i);
        }

        using var session = HeadlessSession.Create(1440, 900);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, new ThirtyLatest(library),
                posterHttp: new HttpClient(new NoArtwork()));
            session.Application.SetRoot(app.Mount());
            session.PumpUntil(() => Descendants(Root(session)).Count(e => e.AutomationId?.StartsWith("card-", StringComparison.Ordinal) == true) >= 21,
                TimeSpan.FromSeconds(10));
            DiscoverCursorTests.Settle(session);
            Assert.Equal(3, app.HomeShelvesMade);
            body(session, app, library);
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

    private sealed class NoArtwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    /// <summary>The fake library with "Latest in" shelves of thirty: the app asks for twelve, and a server
    /// that answers more fills the shelf with them.</summary>
    private sealed class ThirtyLatest(FakeMediaLibrary inner) : IMediaLibrary
    {
        public ServerInfo Server => inner.Server;
        public SignedInUser User => inner.User;
        public Task<ApertureResult<IReadOnlyList<LibraryView>>> GetLibrariesAsync(CancellationToken ct = default) => inner.GetLibrariesAsync(ct);
        public Task<ApertureResult<ItemPage>> GetItemsAsync(ItemQuery query, CancellationToken ct = default) => inner.GetItemsAsync(query, ct);
        public Task<ApertureResult<IReadOnlyList<MediaItem>>> GetResumeAsync(int limit = 12, CancellationToken ct = default) => inner.GetResumeAsync(limit, ct);
        public Task<ApertureResult<IReadOnlyList<MediaItem>>> GetNextUpAsync(int limit = 12, string? seriesId = null, CancellationToken ct = default) =>
            inner.GetNextUpAsync(limit, seriesId, ct);
        public Task<ApertureResult<MediaItem?>> GetNextEpisodeAsync(MediaItem episode, CancellationToken ct = default) => inner.GetNextEpisodeAsync(episode, ct);
        public Task<ApertureResult<IReadOnlyList<MediaItem>>> GetLatestAsync(string libraryId, int limit = 16, CancellationToken ct = default) =>
            inner.GetLatestAsync(libraryId, 30, ct);
        public Task<ApertureResult<IReadOnlyList<MediaGenre>>> GetGenresAsync(string libraryId, CancellationToken ct = default) => inner.GetGenresAsync(libraryId, ct);
        public Task<ApertureResult<MediaItem>> GetItemAsync(string itemId, CancellationToken ct = default) => inner.GetItemAsync(itemId, ct);
        public Uri? ImageUrl(string itemId, ImageKind kind, string? tag, int? maxWidth = null, int? maxHeight = null) =>
            inner.ImageUrl(itemId, kind, tag, maxWidth, maxHeight);
        public Task<ApertureResult<PlaybackTarget>> StartPlaybackAsync(string itemId, TimeSpan startPosition, int? maxBitrateBps = null, CancellationToken ct = default) =>
            inner.StartPlaybackAsync(itemId, startPosition, maxBitrateBps, ct);
        public Task<ApertureResult<bool>> ReportStartedAsync(PlaybackTarget target, TimeSpan position, bool paused, CancellationToken ct = default) =>
            inner.ReportStartedAsync(target, position, paused, ct);
        public Task<ApertureResult<bool>> ReportProgressAsync(PlaybackTarget target, TimeSpan position, bool paused, CancellationToken ct = default) =>
            inner.ReportProgressAsync(target, position, paused, ct);
        public Task<ApertureResult<bool>> ReportStoppedAsync(PlaybackTarget target, TimeSpan position, CancellationToken ct = default) =>
            inner.ReportStoppedAsync(target, position, ct);
        public Task<ApertureResult<bool>> SetPlayedAsync(string itemId, bool played, CancellationToken ct = default) => inner.SetPlayedAsync(itemId, played, ct);
        public Task<ApertureResult<bool>> SetFavoriteAsync(string itemId, bool favorite, CancellationToken ct = default) => inner.SetFavoriteAsync(itemId, favorite, ct);
        public void Dispose() => inner.Dispose();
    }
}

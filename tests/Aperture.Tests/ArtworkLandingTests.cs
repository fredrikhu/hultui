using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Aperture;
using Aperture.Core;
using Aperture.Core.Testing;
using Aperture.Theme;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// A picture that lands goes into the elements on screen that wait for it, in place, with no build
/// (ApertureApp.OnArtworkLanded). Until plan 17 round 18 every landing built the whole view to make
/// the cards that show it again: a film's page took a 84 KB build for each batch of pictures, a
/// library's first open 9 builds of its 13. These pin what in place must keep: an item page's
/// pictures landing one a frame build nothing and end where a full build would; an element made
/// for one picture never shows another's; a card made after its picture landed shows it at once; a
/// picture that fails builds nothing; and a picture the cache lets go of is never drawn after.
/// </summary>
[Collection("ApertureProcessWide")]
public class ArtworkLandingTests
{
    /// <summary>The suite's proposal (k), as a test (the perf suite hosts no app): a film's page with
    /// its hero, its poster and six faces held back, then let land one a frame. No landing builds,
    /// each shows in the frame after it lands, and the page is then what a full build makes.</summary>
    [Fact]
    public void AnItemPage_TakesItsPicturesAsTheyLand_OneAFrame_WithNoBuild()
    {
        var art = new Artwork();
        string[] held = ["/m005/", "/p0/", "/p1/", "/p2/", "/p3/", "/p4/", "/p5/"];
        foreach (var part in held) art.Hold(part);
        Host((session, app) =>
        {
            Open(session, app, "m005");
            Assert.Equal(0, Pictures(session));
            int builds = app.Builds, partBuilds = app.PartBuilds, shown = 0;

            foreach (var part in held)
            {
                art.Release(part);
                // The hero and the poster are one item's: they land together, or one a frame.
                int expected = part == "/m005/" ? 2 : 1;
                PumpUntil(session, () => Pictures(session) >= shown + expected, $"the picture for {part}");
                shown += expected;
                Assert.Equal(0, app.Builds - builds);
            }
            Rest(session);

            Assert.Equal(8, Pictures(session));
            Assert.Equal(0, app.Builds - builds);
            Assert.Equal(0, app.PartBuilds - partBuilds);
            AssertAFullBuildChangesNothing(session, app);
        }, art);
    }

    /// <summary>A search result whose picture is still on the way when the search changes: the new
    /// result's card is made where the old one was, and the reconcile patches the old card with the
    /// new one by its place. Keyed by its picture while it waits, an element is patched only
    /// from one that waits for the same picture, so the old picture lands on no card on screen.</summary>
    [Fact]
    public void ACardMadeAgainForAnotherItem_NeverShowsThePictureItWaitedFor()
    {
        var art = new Artwork { Marked = "/m001/" };
        art.Hold("/m001/");
        Host((session, app) =>
        {
            Key(session, KeyCode.Slash);
            app.SetSearchTerm("Film number 001");
            PumpUntil(session, () => Card(session, "m001") is not null, "the first result");
            Rest(session);
            Assert.Equal(0, Pictures(session));

            app.SetSearchTerm("Film number 011");
            PumpUntil(session, () => Card(session, "m011") is not null && Pictures(session) == 1, "the second result's picture");
            art.Release("/m001/");
            PumpUntil(session, () => app.Posters.PendingCount == 0, "the first result's picture");
            Rest(session);

            Assert.False(MarkedShown(session), "a result shows the picture of one the search no longer has");
            Assert.Equal(1, Pictures(session));
            AssertNothingDrawsADisposedPicture(session);
            AssertAFullBuildChangesNothing(session, app);
        }, art);
    }

    /// <summary>A page's pictures still on the way when another item's page opens: the elements that
    /// waited for them leave with their page, and the new page never takes them.</summary>
    [Fact]
    public void AnotherItemsPage_NeverShowsThePictureTheFirstWaitedFor()
    {
        var art = new Artwork { Marked = "/m005/" };
        art.Hold("/m005/");
        Host((session, app) =>
        {
            Open(session, app, "m005");
            PumpUntil(session, () => Pictures(session) == 6, "the faces, its hero and poster held");

            Open(session, app, "m006");
            PumpUntil(session, () => Pictures(session) == 2, "the second film's hero and poster");
            art.Release("/m005/");
            PumpUntil(session, () => app.Posters.PendingCount == 0, "the first film's pictures");
            Rest(session);

            Assert.False(MarkedShown(session), "the second film's page shows the first film's picture");
            Assert.Equal(2, Pictures(session));
            AssertNothingDrawsADisposedPicture(session);
            AssertAFullBuildChangesNothing(session, app);
        }, art);
    }

    /// <summary>A card scrolled out of the grid before its picture lands: the landing finds no card
    /// waiting on screen, and the card made for it when it comes back shows the picture in the frame
    /// that makes it.</summary>
    [Fact]
    public void ACardMadeAfterItsPictureLanded_ShowsIt_AtOnce()
    {
        var art = new Artwork { Marked = "/m003/" };
        art.Hold("/m003/");
        Host((session, app) =>
        {
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntil(session, () => session.Window.FindByAutomationId("card-m003") is not null, "the grid");
            Rest(session);
            Assert.False(HasPicture(Card(session, "m003")!));

            for (int i = 0; i < 12; i++) Key(session, KeyCode.Down);
            Rest(session);
            Assert.Null(Card(session, "m003"));

            art.Release("/m003/");
            PumpUntil(session, () => app.Posters.PendingCount == 0, "the held picture");
            Rest(session);
            Assert.False(MarkedShown(session), "a card on screen shows m003's picture");

            for (int i = 0; i < 12; i++)
            {
                session.Window.PressKey(KeyCode.Up);
                session.PumpFrame();
                if (Card(session, "m003") is { } card)
                {
                    Assert.True(HasPicture(card), "card-m003 was made without the picture that had landed");
                    Assert.True(MarkedShown(session));
                    return;
                }
            }
            Assert.Fail("card-m003 did not come back");
        }, art);
    }

    /// <summary>A picture that fails (a 404) leaves the placeholder and builds nothing; the page is
    /// still what a full build makes.</summary>
    [Fact]
    public void APictureThatFails_BuildsNothing_AndLeavesThePlaceholder()
    {
        var art = new Artwork { Failed = ["/m007/"] };
        art.Hold("/m007/");
        Host((session, app) =>
        {
            Open(session, app, "m007");
            int builds = app.Builds, partBuilds = app.PartBuilds;

            art.Release("/m007/");
            PumpUntil(session, () => app.Posters.PendingCount == 0, "the failures");
            Rest(session);

            Assert.Equal(0, app.Builds - builds);
            Assert.Equal(0, app.PartBuilds - partBuilds);
            Assert.Equal(0, Pictures(session));
            // The poster's box still holds its glyph.
            var poster = Descendants(Root(session)).OfType<ImageElement>().Single(i => i.Parent is { } box
                && MathF.Abs(box.Layout.AbsoluteBorderBox.Width - 150f) < 0.5f && MathF.Abs(box.Layout.AbsoluteBorderBox.Height - 225f) < 0.5f);
            Assert.Equal(ImageFit.Contain, poster.Fit);
            AssertAFullBuildChangesNothing(session, app);
        }, art);
    }

    /// <summary>A hero asked for at a new size after a resize draws the old size until the new one is
    /// in. When the new one fails, the old one is let go (disposed) once every download has landed:
    /// the page is built again so nothing draws it, as a full build would leave it, with no picture.
    /// Put in place, a stand-in that stayed would be drawn disposed.</summary>
    [Fact]
    public void AHeroWhoseNewSizeFails_DrawsNothingTheCacheLetGo()
    {
        var art = new Artwork();
        Host((session, app) =>
        {
            Open(session, app, "m005");
            PumpUntil(session, () => Pictures(session) == 8, "the page's pictures");
            Rest(session);
            int now = ApertureApp.HeroPixels(1440 - Halide.RailWidth, 1f);
            int wider = ApertureApp.HeroPixels(1700 - Halide.RailWidth, 1f);
            Assert.NotEqual(now, wider);
            art.Failed = [$"fillWidth={wider}"];

            session.Window.SetClientSize(1700, 900);
            session.PumpFrames(2);
            PumpUntil(session, () => app.Posters.PendingCount == 0, "the wider hero");
            Rest(session);

            AssertNothingDrawsADisposedPicture(session);
            Assert.Equal(7, Pictures(session));   // the poster and the faces; the hero has none
            AssertAFullBuildChangesNothing(session, app);
        }, art);
    }

    /// <summary>A hero let go of while it is on the way (four pages on, PosterCache.Drop) lands with no
    /// page waiting for it, and is never drawn; Back to its page asks for it again, and it goes into
    /// that page in place.</summary>
    [Fact]
    public void AHeroLetGoBeforeItLands_IsNeverDrawn_AndComesBackWithItsPage()
    {
        var art = new Artwork { Marked = "/m010/" };
        art.Hold("/m010/");
        Host((session, app) =>
        {
            Open(session, app, "m010");
            foreach (var id in new[] { "m011", "m012", "m013", "m014" })
            {
                Open(session, app, id);
                PumpUntil(session, () => Pictures(session) == 2, $"{id}'s hero and poster");
            }
            art.Release("/m010/");
            PumpUntil(session, () => app.Posters.PendingCount == 0, "m010's pictures");
            Rest(session);
            Assert.False(MarkedShown(session), "m014's page shows m010's picture");
            AssertNothingDrawsADisposedPicture(session);

            for (int i = 0; i < 4; i++)
            {
                session.Window.PressKey(KeyCode.Left, KeyModifiers.Alt);
                Rest(session);
                AssertNothingDrawsADisposedPicture(session);
            }
            PumpUntil(session, () => Pictures(session) == 2, "m010's hero and poster again");
            Assert.True(MarkedShown(session));
            AssertAFullBuildChangesNothing(session, app);
        }, art);
    }

    /// <summary>Pages left while their pictures stall (a body gone quiet lands nothing until the
    /// download's deadline). Each wait holds its element, and through it the page it is on: until plan
    /// 17 round 19 every page visited after a stall was kept whole, about 0.33 MB a page. After a frame
    /// the waits whose element left the window go, and so does the older of two for one element (a
    /// card made again around the cursor while it waited): one wait is left for each element on screen
    /// that waits, and the pictures still go into them when they land.</summary>
    [Fact]
    public void PagesLeftWhilePicturesStall_KeepOneWaitForEachElementOnScreen()
    {
        var art = new Artwork();
        HoldEveryFilm(art);
        Host((session, app) =>
        {
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntil(session, () => session.Window.FindByAutomationId("card-m003") is not null, "the grid");
            Rest(session);
            AssertOneWaitForEachElement(app);

            for (int i = 0; i < 6; i++)
            {
                Key(session, KeyCode.Right);
                AssertOneWaitForEachElement(app);
                Key(session, KeyCode.Enter);
                PumpUntil(session, () => session.Window.FindByAutomationId("detail-play") is not null, "an item's page");
                Rest(session);
                AssertOneWaitForEachElement(app);
                // A build of the page while its pictures wait (its data again): the hero, the poster
                // and the faces are made again and patched into the live ones, refs and all.
                FullBuild(app);
                Rest(session);
                AssertOneWaitForEachElement(app);
                session.Window.PressKey(KeyCode.Left, KeyModifiers.Alt);
                PumpUntil(session, () => Card(session, "m003") is not null, "the grid again");
                Rest(session);
                AssertOneWaitForEachElement(app);
            }

            // What is left still takes its pictures (the first eight asked for hold the slots: let all go).
            Assert.False(HasPicture(Card(session, "m003")!));
            ReleaseEveryFilm(art);
            PumpUntil(session, () => app.Posters.PendingCount == 0, "the pictures held back");
            Rest(session);
            Assert.True(HasPicture(Card(session, "m003")!), "a card on screen did not take its picture");
            Assert.Equal((0, 0), app.ArtWaitCounts);
        }, art);
    }

    /// <summary>Home's shelves are parts the app's build hands back, and while it builds they and the
    /// cards they hand back are out of the tree; a card just made is out of it until its build is
    /// patched in. Waits are dropped only after a frame, so a full build of Home, and a Back to Home
    /// from an item's page, still leave each waiting card its wait: the picture goes into it when it
    /// lands. Dropped in the build, the cards kept the glyph.</summary>
    [Fact]
    public void HomesCards_TakeTheirPictures_AfterAFullBuild_AndAfterABack()
    {
        var art = new Artwork();
        HoldEveryFilm(art);
        Host((session, app) =>
        {
            var cards = HomeCards(session);
            Assert.True(cards.Count >= 3, $"Home shows {cards.Count} cards");
            Assert.DoesNotContain(cards, id => HasPicture(Card(session, id)!));

            FullBuild(app);
            Rest(session);
            AssertOneWaitForEachElement(app);
            art.Release($"/{cards[0]}/");
            PumpUntil(session, () => HasPicture(Card(session, cards[0])!), $"{cards[0]}'s picture after a full build");

            Open(session, app, "m150");
            session.Window.PressKey(KeyCode.Left, KeyModifiers.Alt);
            PumpUntil(session, () => Card(session, cards[1]) is not null, "Home again");
            Rest(session);
            AssertOneWaitForEachElement(app);
            Assert.False(HasPicture(Card(session, cards[1])!));
            art.Release($"/{cards[1]}/");
            PumpUntil(session, () => HasPicture(Card(session, cards[1])!), $"{cards[1]}'s picture after a Back");
            Assert.True(HasPicture(Card(session, cards[0])!));
            Assert.False(HasPicture(Card(session, cards[2])!));
            ReleaseEveryFilm(art);
        }, art);
    }

    private static void FullBuild(ApertureApp app) =>
        typeof(Component).GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);

    private static void HoldEveryFilm(Artwork art)
    {
        for (int i = 0; i < 200; i++) art.Hold($"/m{i:000}/");
    }

    private static void ReleaseEveryFilm(Artwork art)
    {
        for (int i = 0; i < 200; i++) art.Release($"/m{i:000}/");
    }

    /// <summary>Every wait held is for an element in the window, and no two for one element.</summary>
    private static void AssertOneWaitForEachElement(ApertureApp app)
    {
        var (waits, elements) = app.ArtWaitCounts;
        Assert.True(elements > 0, "nothing on screen waits for a picture");
        Assert.Equal(elements, waits);
    }

    /// <summary>The items of Home's cards on screen, in the window's order.</summary>
    private static List<string> HomeCards(HeadlessSession session) =>
        Descendants(Root(session)).OfType<BoxElement>()
            .Select(e => e.AutomationId).OfType<string>()
            .Where(id => id.StartsWith("card-", StringComparison.Ordinal))
            .Select(id => id["card-".Length..]).Distinct().ToList();

    // ── Reading the screen ───────────────────────────────────────────────

    /// <summary>Pictures drawn: image elements covering their box with a picture that is in.</summary>
    private static int Pictures(HeadlessSession session) =>
        Descendants(Root(session)).OfType<ImageElement>().Count(i => i.Fit == ImageFit.Cover && i.Source is { IsLoaded: true });

    /// <summary>Whether anything on screen draws the marked item's picture (41 by 61; the rest are 40 by 60).</summary>
    private static bool MarkedShown(HeadlessSession session) =>
        Descendants(Root(session)).OfType<ImageElement>().Any(i => i.Source is { IsLoaded: true } s && s.IntrinsicSize.Width == 41);

    private static void AssertNothingDrawsADisposedPicture(HeadlessSession session)
    {
        foreach (var image in Descendants(Root(session)).OfType<ImageElement>())
            Assert.True(image.Source is null or { IsLoaded: true }, $"an image at {image.WindowBorderBox} draws a picture that was let go");
    }

    private static BoxElement? Card(HeadlessSession session, string id) =>
        Descendants(Root(session)).OfType<BoxElement>().FirstOrDefault(e => e.AutomationId == "card-" + id);

    private static bool HasPicture(BoxElement card) =>
        Descendants(card).OfType<ImageElement>().Any(i => i.Fit == ImageFit.Cover && i.Source is { IsLoaded: true });

    private static void AssertAFullBuildChangesNothing(HeadlessSession session, ApertureApp app)
    {
        session.PumpFrames(2);
        string kept = WindowFitTests.Sig(Root(session));
        typeof(Component).GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);
        session.PumpFrames(2);
        Assert.Equal(kept, WindowFitTests.Sig(Root(session)));
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

    // ── Driving ──────────────────────────────────────────────────────────

    private static void Open(HeadlessSession session, ApertureApp app, string id)
    {
        app.Navigate(new Route(ApertureView.Detail, id, id));
        PumpUntil(session, () => session.Window.FindByAutomationId("detail-play") is not null
            && app.Current.Id == id, $"{id}'s page");
        Rest(session);
    }

    private static void Key(HeadlessSession session, KeyCode key)
    {
        session.Window.PressKey(key);
        session.PumpFrames(2);
    }

    private static void PumpUntil(HeadlessSession session, Func<bool> done, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!done())
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(10))
                throw new Xunit.Sdk.XunitException($"timed out waiting for {what} ({Pictures(session)} pictures, cards: "
                    + string.Join(" ", Descendants(Root(session)).Select(e => e.AutomationId).Where(i => i?.StartsWith("card-") == true)) + ")");
            session.PumpFrame();
            Thread.Sleep(2);
        }
    }

    /// <summary>Pumps for a while in real time: artwork comes back on the thread pool.</summary>
    private static void Rest(HeadlessSession session)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < 150) { session.PumpFrame(); Thread.Sleep(5); }
        session.PumpFrames(2);
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    /// <summary>A picture for every artwork request, 40 by 60, or 41 by 61 for the URLs containing
    /// <see cref="Marked"/>; a 404 for those containing one of <see cref="Failed"/>; a request whose URL
    /// contains a part held waits until it is released.</summary>
    private sealed class Artwork : HttpMessageHandler
    {
        public string? Marked { get; init; }
        public volatile string[] Failed = [];
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _held = new();

        public void Hold(string part) => _held[part] = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(string part)
        {
            if (_held.TryRemove(part, out var gate)) gate.TrySetResult();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            foreach (var (part, gate) in _held)
                if (url.Contains(part, StringComparison.Ordinal)) await gate.Task.ConfigureAwait(false);
            foreach (var part in Failed)
                if (url.Contains(part, StringComparison.Ordinal))
                    return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            bool marked = Marked is { } m && url.Contains(m, StringComparison.Ordinal);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(marked ? MarkedPng : Png) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return response;
        }

        private static readonly byte[] Png = Make(40, 60), MarkedPng = Make(41, 61);

        private static byte[] Make(int width, int height)
        {
            using var bitmap = new SkiaSharp.SKBitmap(width, height);
            using var canvas = new SkiaSharp.SKCanvas(bitmap);
            canvas.Clear(new SkiaSharp.SKColor(40, 60, (byte)(width * 2)));
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            return data.ToArray();
        }
    }

    private static void Host(Action<HeadlessSession, ApertureApp> body, Artwork artwork)
    {
        var library = new FakeMediaLibrary().AddLibrary("movies", "Films", LibraryKind.Movies);
        for (int i = 0; i < 200; i++)
            library.AddMovie($"m{i:000}", $"Film number {i:000}", "movies", 1950 + i % 70, 80 + i % 60);
        library.AddItem(library.Item("m005")! with
        {
            People = Enumerable.Range(0, 6).Select(p => new MediaPerson($"p{p}", $"Person {p}", PersonKind.Actor,
                $"Role {p}", $"tag-p{p}")).ToList(),
        });

        using var session = HeadlessSession.Create(1440, 900);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                posterHttp: new HttpClient(artwork));
            session.Application.SetRoot(app.Mount());
            PumpUntil(session, () => session.Window.FindByAutomationId("rail-films") is not null, "the rail");
            Rest(session);
            body(session, app);
        }
        finally
        {
            app?.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(settingsPath)!, true); } catch (IOException) { }
        }
    }
}

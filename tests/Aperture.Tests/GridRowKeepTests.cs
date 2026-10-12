using System.Diagnostics;
using System.Reflection;
using Aperture;
using Aperture.Core;
using Aperture.Core.Testing;
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
/// the cursor is in there — so an arrow makes the row or two the cursor left and reached
/// instead of every row on screen. What else a row shows is in a shape that makes the rows on
/// screen again: one test per input, each failing with that input left out.
/// </summary>
// Serial: the display-scale test changes process-wide state (the glyph caches) that other
// sessions draw with.
[Collection("ApertureProcessWide")]
public class GridRowKeepTests
{
    [Fact]
    public void ARightArrow_MakesTheRowTheCursorMovesIn_AndKeepsTheRest()
    {
        Host((session, app, _, _) =>
        {
            Keys(session, KeyCode.Right);   // the first press lands on the first card
            var before = Rows(session);
            int made = app.GridRowsMade;

            Keys(session, KeyCode.Right);

            Assert.Equal(1, app.GridRowsMade - made);
            var after = Rows(session);
            Assert.False(ReferenceEquals(before[0], after[0]), "the cursor's row was kept");
            Assert.Equal(before.Count - 1, before.Zip(after).Count(p => ReferenceEquals(p.First, p.Second)));
            Assert.True(Card(session, "m001").Outline.Width > 0, "the cursor's card has no ring");
            Assert.False(Card(session, "m000").Outline.Width > 0, "the card the cursor left keeps its ring");
        });
    }

    [Fact]
    public void ADownArrow_MakesTheTwoRowsItMovedBetween()
    {
        Host((session, app, _, _) =>
        {
            Keys(session, KeyCode.Right);
            var before = Rows(session);
            int made = app.GridRowsMade;

            Keys(session, KeyCode.Down);

            Assert.Equal(2, app.GridRowsMade - made);
            var after = Rows(session);
            Assert.Equal(before.Count - 2, before.Zip(after).Count(p => ReferenceEquals(p.First, p.Second)));
        });
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

    // ── Hosting ──────────────────────────────────────────────────────────

    private static void Host(Action<HeadlessSession, ApertureApp, FakeMediaLibrary, HttpMessageHandler> body,
        int films = 60, int width = 1440, int height = 900, HttpMessageHandler? artwork = null)
    {
        var library = new FakeMediaLibrary().AddLibrary("movies", "Films", LibraryKind.Movies);
        for (int i = 0; i < films; i++)
            library.AddMovie($"m{i:000}", $"Film number {i:000}", "movies", 1950 + i % 70, 80 + i % 60);
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

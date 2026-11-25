using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Aperture;
using Aperture.Core;
using Aperture.Core.Testing;
using Aperture.Theme;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// The page Back returns to, kept while away (plan 17 round 19, question 79: ApertureApp.KeptPage.cs). Back
/// to Home or a library's grid made the whole page again, 285 to 297 elements and about 290 KB, where opening
/// the film's page was 13 to 135 KB. Now the page left is kept out of the window, one page at a time, and
/// shown again on Back: its build hands back everything that did not change, and draws what a full build
/// draws. It is let go of on a sign-out, a scale or theme change and a refresh.
/// </summary>
[Collection("ApertureProcessWide")]
public class KeptPageTests
{
    /// <summary>Back to Home builds no shelf: the page is the one left, the cursor's card the one opened,
    /// ringed and with the keyboard, and the frame is the frame Back drew when it made Home again. (A card
    /// the ring moved onto or off with no build is made again by the next build of its page, on screen or
    /// on the way back: a build first, here, as a picture or a load landing gives one.)</summary>
    [Fact]
    public void BackToHome_ShowsThePageItLeft_AsAFullBuildDrawsIt()
    {
        Host((session, app, _) =>
        {
            Keys(session, KeyCode.Right);
            Keys(session, KeyCode.Down);
            Keys(session, KeyCode.Right);
            Rerender(app);
            Rest(session);
            var home = View(session, ApertureView.Home);
            var opened = Focused(session);
            Assert.StartsWith("card-", opened.AutomationId);
            int shelves = app.HomeShelvesMade, shown = app.PagesShownAgain;

            Keys(session, KeyCode.Enter);
            Assert.Equal(ApertureView.Detail, app.Current.View);
            Assert.Equal(ApertureView.Home, app.KeptRoute?.View);
            Keys(session, KeyCode.Escape);

            Assert.Equal(ApertureView.Home, app.Current.View);
            Assert.Equal(shown + 1, app.PagesShownAgain);
            Assert.Same(home, View(session, ApertureView.Home));
            Assert.Equal(shelves, app.HomeShelvesMade);
            Assert.Null(app.KeptRoute);
            Assert.Same(opened, Focused(session));
            Assert.True(opened.Outline.Width > 0, "the card Back landed on has no ring");
            var kept = Pixels(session);

            // The same Back with Home made again, as before round 19.
            Keys(session, KeyCode.Enter);
            DropKeptPage(app);
            Keys(session, KeyCode.Escape);
            Assert.NotSame(home, View(session, ApertureView.Home));
            Assert.True(app.HomeShelvesMade > shelves, "Back made no shelf with nothing kept");
            Assert.Equal(opened.AutomationId, Focused(session).AutomationId);
            AssertSameFrame(kept, Pixels(session), "Back to the kept Home");
        });
    }

    /// <summary>Back to a library scrolled off its first screen makes no row: the list is the one left,
    /// where it was scrolled, the opened card ringed and with the keyboard, and the frame is the one a
    /// Back that made the grid again drew. Escape, the mouse's back button and the rail opening the same
    /// library show it again too.</summary>
    [Fact]
    public void BackToAGrid_ShowsTheListItLeft_AsAFullBuildDrawsIt()
    {
        Host((session, app, _) =>
        {
            OpenFilms(session);
            Keys(session, KeyCode.Right);
            for (int i = 0; i < 10; i++) Keys(session, KeyCode.Down);
            Rerender(app);
            Rest(session);
            var grid = Grid(session);
            float scrolled = grid.ScrollOffsetY;
            Assert.True(scrolled > 0, "ten Downs left the grid at the top");
            var opened = Focused(session);
            int rows = app.GridRowsMade, shown = app.PagesShownAgain;

            Keys(session, KeyCode.Enter);
            Assert.Equal(ApertureView.Library, app.KeptRoute?.View);
            Keys(session, KeyCode.Left, KeyModifiers.Alt);
            LandedOnTheOpenedCard("Back", 1);
            var kept = Pixels(session);

            Keys(session, KeyCode.Enter);
            Keys(session, KeyCode.Escape);
            LandedOnTheOpenedCard("Escape", 2);

            Keys(session, KeyCode.Enter);
            session.Window.MouseDown(400, 300, MouseButton.Back);
            session.Window.MouseUp(400, 300, MouseButton.Back);
            Rest(session);
            LandedOnTheOpenedCard("the mouse's back button", 3);

            app.Navigate(new Route(ApertureView.Settings));
            Rest(session);
            session.Window.FindByAutomationId("rail-films")!.Click();
            Rest(session);
            LandedOnTheOpenedCard("the rail", 4);

            // The same Back with the grid made again, as before round 19.
            Keys(session, KeyCode.Enter);
            DropKeptPage(app);
            Keys(session, KeyCode.Left, KeyModifiers.Alt);
            Assert.NotSame(grid, Grid(session));
            Assert.Equal(scrolled, Grid(session).ScrollOffsetY);
            Assert.True(app.GridRowsMade > rows, "Back made no row with nothing kept");
            AssertSameFrame(kept, Pixels(session), "Back to the kept grid");

            void LandedOnTheOpenedCard(string how, int times)
            {
                Assert.Equal(ApertureView.Library, app.Current.View);
                Assert.Equal(shown + times, app.PagesShownAgain);
                Assert.Same(grid, Grid(session));
                Assert.Equal(rows, app.GridRowsMade);
                Assert.True(scrolled == grid.ScrollOffsetY, $"{how} landed at {grid.ScrollOffsetY}, not at {scrolled}");
                Assert.Same(opened, Focused(session));
                Assert.True(opened.Outline.Width > 0, $"after {how} the card has no ring");
            }
        });
    }

    /// <summary>A picture that lands while its page is away goes into its card there, and Back shows it:
    /// the waits of the kept page are kept with it, and the card is handed back, picture and all.</summary>
    [Fact]
    public void APictureThatLandsWhileAway_IsOnItsCardOnBack()
    {
        var art = new Artwork(open: false);
        Host((session, app, _) =>
        {
            Keys(session, KeyCode.Right);
            Rerender(app);
            Rest(session);
            var card = Focused(session);
            var home = View(session, ApertureView.Home);
            Assert.False(HasPicture(card), "the picture came before its gate opened");
            var waiting = HomeCards(home).Where(c => !HasPicture(c)).ToList();
            Assert.True(waiting.Count > 3, $"{waiting.Count} cards wait for a picture");

            Keys(session, KeyCode.Enter);
            Assert.Equal(ApertureView.Home, app.KeptRoute?.View);
            art.Open();
            session.PumpUntil(() => app.Posters.PendingCount == 0, TimeSpan.FromSeconds(10));
            Rest(session);
            Assert.All(waiting, c => Assert.True(HasPicture(c), $"{c.AutomationId}'s picture did not go into it while away"));

            int shelves = app.HomeShelvesMade;
            Keys(session, KeyCode.Escape);
            Assert.Same(home, View(session, ApertureView.Home));
            Assert.Equal(shelves, app.HomeShelvesMade);
            Assert.Same(card, Focused(session));
            Assert.All(waiting, c => Assert.True(c.IsHosted && HasPicture(c), $"{c.AutomationId} is not back with its picture"));
            Assert.Equal((0, 0), app.ArtWaitCounts);
        }, art);
    }

    /// <summary>A sign-out, a move to a screen of another scale and a change of theme let the kept page go:
    /// nothing holds it after a full collection, and Back (or the next session) makes the page again.</summary>
    [Theory]
    [InlineData("sign-out")]
    [InlineData("scale")]
    [InlineData("theme")]
    public void TheKeptPage_IsLetGoOf(string what)
    {
        Host((session, app, _) =>
        {
            try
            {
                app.Navigate(new Route(ApertureView.Settings));
                Rest(session);
                Assert.Equal(ApertureView.Home, app.KeptRoute?.View);
                var kept = Weak(app);
                int shown = app.PagesShownAgain;

                switch (what)
                {
                    case "sign-out":
                        app.SignOut(keepAccount: true);
                        Rest(session);
                        Assert.Equal(ApertureStage.Connect, app.Stage);
                        break;
                    case "scale":
                        session.Window.SetScale(1.5f);
                        Rest(session);
                        Assert.Null(app.KeptRoute);
                        Keys(session, KeyCode.Escape);
                        break;
                    case "theme":
                        session.Application.Theme = Halide.ToHultTheme();
                        Keys(session, KeyCode.Escape);
                        break;
                }
                Assert.Null(app.KeptRoute);
                Assert.Equal(shown, app.PagesShownAgain);
                Collect();
                Assert.False(kept.IsAlive, $"the kept page outlived a {what}");
            }
            finally
            {
                session.Window.SetScale(1f);
                session.PumpFrames(2);
            }
        });
    }

    /// <summary>What changed while the page was away is on it when it comes back. A refresh, and a film
    /// marked watched on its own page (which refreshes), let the kept page go, and Back makes Home again
    /// with the new data; a setting every card shows (titles under posters) is patched into the kept page
    /// by its build, which draws what a full build draws.</summary>
    [Fact]
    public void APageWhoseDataChangedWhileAway_IsBuiltAgain_OrPatched()
    {
        Host((session, app, library) =>
        {
            Keys(session, KeyCode.Right);
            Assert.Equal("resume-m003", Focused(session).AutomationId);   // Continue watching's film
            var before = View(session, ApertureView.Home);
            int shown = app.PagesShownAgain;

            Keys(session, KeyCode.Enter);
            Assert.Equal(("m003", ApertureView.Home), (app.Current.Id, app.KeptRoute?.View));
            session.Window.FindByAutomationId("detail-watched")!.Click();
            session.PumpUntil(() => library.Item("m003")!.Played, TimeSpan.FromSeconds(5));
            Rest(session);
            Assert.Null(app.KeptRoute);
            Keys(session, KeyCode.Escape);
            Assert.Equal(shown, app.PagesShownAgain);
            Assert.NotSame(before, View(session, ApertureView.Home));
            // "Marked watched" stays up until its hold and its fade end on the session's clock, which a Rest
            // moves by as many frames as its 250 ms of real time pump: 18 here, where 154 of the toast's 156
            // frames had passed at the full build's frame below, and a frame or two more on CI's windows-2025
            // runner, where it faded between the two frames. Its time runs out before them.
            session.PumpUntil(() => !app.Toasts.IsShowing, TimeSpan.FromSeconds(10));

            // A setting every card shows, changed while Home is away: the kept page comes back with it.
            Keys(session, KeyCode.Right);
            app.Navigate(new Route(ApertureView.Settings));
            Rest(session);
            var home = app.KeptRoot;
            Assert.NotNull(home);
            app.Settings.Data.ShowCardTitles = false;
            Rerender(app);
            Rest(session);
            Keys(session, KeyCode.Escape);
            Assert.Equal(shown + 1, app.PagesShownAgain);
            Assert.Same(home, View(session, ApertureView.Home));
            Assert.DoesNotContain(Descendants(home!).OfType<TextElement>(), t => t.Content.StartsWith("Shelf film", StringComparison.Ordinal));
            var patched = Pixels(session);

            app.Navigate(new Route(ApertureView.Settings));
            Rest(session);
            DropKeptPage(app);
            Keys(session, KeyCode.Escape);
            Assert.NotSame(home, View(session, ApertureView.Home));
            AssertSameFrame(patched, Pixels(session), "the patched Home");
        });
    }

    /// <summary>Open and Back across many pages keeps one page: Home, the same elements every time, and no
    /// item's page after it is left.</summary>
    [Fact]
    public void OpenAndBack_AcrossManyPages_KeepsOnePage()
    {
        Host((session, app, _) =>
        {
            var home = View(session, ApertureView.Home);
            int shown = app.PagesShownAgain;
            var pages = new List<WeakReference>();
            for (int i = 0; i < 24; i++)
            {
                app.Navigate(new Route(ApertureView.Detail, $"x0{i % 7:00}", "Shelf film"));
                session.PumpUntil(() => session.Window.FindByAutomationId("detail-play") is not null, TimeSpan.FromSeconds(5));
                session.PumpFrames(2);
                pages.Add(WeakView(session, ApertureView.Detail));
                Assert.Equal(ApertureView.Home, app.KeptRoute?.View);
                Keys(session, KeyCode.Escape);
                Assert.Same(home, View(session, ApertureView.Home));
            }
            Assert.Equal(shown + 24, app.PagesShownAgain);
            Rest(session);
            Collect();
            // The last one is held by the ref its first verb takes the keyboard through, until another
            // item's page is built (as before round 19).
            Assert.All(pages[..^1], p => Assert.False(p.IsAlive, "an item's page outlived its Back"));
        });
    }

    // ── Reading the screen ───────────────────────────────────────────────

    private static Element View(HeadlessSession session, ApertureView view) =>
        Descendants(Root(session)).Single(e => e.Key == "view-" + view);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WeakView(HeadlessSession session, ApertureView view) => new(View(session, view));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Weak(ApertureApp app) => new(app.KeptRoot);

    private static ScrollViewElement Grid(HeadlessSession session) =>
        Descendants(Root(session)).OfType<ScrollViewElement>().Single(e => e.AutomationId == "library-grid");

    private static BoxElement Focused(HeadlessSession session) =>
        Assert.IsAssignableFrom<BoxElement>(session.Window.Window.FocusedElement);

    private static List<BoxElement> HomeCards(Element home) =>
        Descendants(home).OfType<BoxElement>()
            .Where(e => e.AutomationId?.StartsWith("card-", StringComparison.Ordinal) == true).ToList();

    /// <summary>A card drawing its picture (a cover fit), not the placeholder glyph.</summary>
    private static bool HasPicture(BoxElement card) =>
        Descendants(card).OfType<ImageElement>().Any(i => i.Fit == ImageFit.Cover && i.Source is { IsLoaded: true });

    /// <summary>The two frames are the same bytes; if not, how many pixels differ and where.</summary>
    private static void AssertSameFrame(byte[] expected, byte[] actual, string what)
    {
        if (expected.AsSpan().SequenceEqual(actual)) return;
        const int width = 1440;
        int differ = 0, left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (int i = 0; i + 3 < Math.Min(expected.Length, actual.Length); i += 4)
        {
            if (expected.AsSpan(i, 4).SequenceEqual(actual.AsSpan(i, 4))) continue;
            differ++;
            int x = i / 4 % width, y = i / 4 / width;
            (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x), Math.Max(bottom, y));
        }
        Assert.Fail($"{what} draws another frame than a full build: {differ} pixels differ in x {left}–{right}, y {top}–{bottom}");
    }

    /// <summary>The window's frame, with the pointer out of it (a hover look or a tooltip is the pointer's,
    /// not the page's) and no toast up: a toast's fades run on the session's clock, which a Rest moves by as
    /// many frames as the machine pumps in its 250 ms, so two frames with one up differ on one machine and
    /// not on another.</summary>
    private static byte[] Pixels(HeadlessSession session)
    {
        session.Window.MouseLeaveWindow();
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Assert.False(Descendants(Root(session)).OfType<ToastElement>().Any(), "a toast is up in the frame");
        using var image = session.Window.CapturePixels();
        using var bitmap = SkiaSharp.SKBitmap.FromImage(image);
        return bitmap.Bytes;
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

    private static void Keys(HeadlessSession session, KeyCode key, KeyModifiers modifiers = KeyModifiers.None)
    {
        session.Window.PressKey(key, modifiers);
        Rest(session);
    }

    private static void OpenFilms(HeadlessSession session)
    {
        session.Window.FindByAutomationId("rail-films")!.Click();
        session.PumpUntil(() => Descendants(Root(session)).Any(e => e.AutomationId == "card-m001"), TimeSpan.FromSeconds(10));
        Rest(session);
    }

    /// <summary>Lets the page go as if nothing were kept, for the full build the kept page is held to.</summary>
    private static void DropKeptPage(ApertureApp app) =>
        typeof(ApertureApp).GetMethod("DropKeptPage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);

    private static void Rerender(ApertureApp app) =>
        typeof(ApertureApp).GetMethod("Rerender", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>Pumps for a while in real time: loads and artwork come back on the thread pool.</summary>
    private static void Rest(HeadlessSession session)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < 250) { session.PumpFrame(); Thread.Sleep(5); }
        session.PumpFrames(2);
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    /// <summary>Home of four shelves (Continue watching, the latest in Films and in two libraries of seven)
    /// and a library of 300 films, at 1440×900.</summary>
    private static void Host(Action<HeadlessSession, ApertureApp, FakeMediaLibrary> body, Artwork? artwork = null)
    {
        var library = new FakeMediaLibrary()
            .AddLibrary("movies", "Films", LibraryKind.Movies);
        for (int i = 0; i < 300; i++)
            library.AddMovie($"m{i:000}", $"Film number {i:000}", "movies", 1950 + i % 70, 80 + i % 60,
                resume: i == 3 ? TimeSpan.FromMinutes(20) : null);
        for (int l = 0; l < 2; l++)
        {
            library.AddLibrary($"lib{l}", $"Shelf {l}", LibraryKind.Movies);
            for (int i = 0; i < 7; i++)
                library.AddMovie($"x{l}{i:00}", $"Shelf film {l}-{i:00}", $"lib{l}", 1960 + i, 90 + i);
        }
        library.ResumeIds.Add("m003");

        using var session = HeadlessSession.Create(1440, 900);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                posterHttp: new HttpClient(artwork ?? new Artwork(open: true)));
            session.Application.SetRoot(app.Mount());
            session.PumpUntil(() => Descendants(Root(session)).Any(e => e.AutomationId == "card-x100"), TimeSpan.FromSeconds(10));
            Rest(session);
            body(session, app, library);
        }
        finally
        {
            app?.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(settingsPath)!, true); } catch (IOException) { }
        }
    }

    /// <summary>A small picture for every artwork request, a colour per item, once the gate is open; until
    /// then every request waits.</summary>
    private sealed class Artwork(bool open) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate = Gate(open);

        private static TaskCompletionSource Gate(bool open)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (open) gate.SetResult();
            return gate;
        }

        public void Open() => _gate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await _gate.Task.WaitAsync(ct).ConfigureAwait(false);
            var path = request.RequestUri!.AbsolutePath;
            uint hash = 2166136261;
            foreach (char c in path) hash = (hash ^ c) * 16777619;
            using var bitmap = new SkiaSharp.SKBitmap(40, 60);
            using var canvas = new SkiaSharp.SKCanvas(bitmap);
            canvas.Clear(new SkiaSharp.SKColor((byte)(40 + hash % 180), (byte)(40 + hash / 180 % 180), 90));
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(data.ToArray()) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return response;
        }
    }
}

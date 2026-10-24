using Aperture;
using Aperture.Core;
using Aperture.Core.Testing;
using HultUI.Core.Accessibility;
using HultUI.Core.Platform;
using HultUI.Headless;
using HultUI.Media;
using HultUI.Media.Testing;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// The whole app, hosted headless over a <see cref="FakeMediaLibrary"/> and a
/// <see cref="FakeMediaBackend"/> — no server, no decoder, no window. What these cover is
/// the wiring the unit tests cannot see: that a click on a card opens the right page, that
/// pressing Play tells the server what Jellyfin's own clients tell it, and that stopping
/// puts the resume point where the viewer left off.
/// </summary>
public class ApertureAppHostTests
{
    private static FakeMediaLibrary Library()
    {
        var library = new FakeMediaLibrary()
            .AddLibrary("movies", "Films", LibraryKind.Movies)
            .AddLibrary("shows", "Series", LibraryKind.TvShows);

        library.AddMovie("m1", "Stalker", "movies", 1979, 162);
        library.AddMovie("m2", "Solaris", "movies", 1972, 167, resume: TimeSpan.FromMinutes(58));
        library.AddMovie("m3", "The Mirror", "movies", 1975, 107, played: true);
        // Genres, so the filter has something to filter on.
        library.Update(library.Item("m1")! with { Genres = ["Science Fiction"] });
        library.Update(library.Item("m2")! with { Genres = ["Science Fiction"] });
        library.Update(library.Item("m3")! with { Genres = ["Drama"] });
        library.AddItem(new MediaItem
        {
            Id = "s1", Name = "Bleach", Kind = MediaKind.Series, PrimaryImageTag = "tag-s1",
            ChildCount = 2, UnplayedItemCount = 3,
        }, "shows");
        library.AddItem(new MediaItem
        {
            Id = "e1", Name = "I Went to Market", Kind = MediaKind.Episode,
            SeriesId = "s1", SeriesName = "Bleach", ParentIndexNumber = 1, IndexNumber = 4,
            RunTime = TimeSpan.FromMinutes(24), ResumePosition = TimeSpan.FromMinutes(20),
            PrimaryImageTag = "tag-e1",
        }, "s1");

        // Credits on one film, so the cast row and the person page have something real.
        library.Update(library.Item("m1")! with
        {
            People =
            [
                new MediaPerson("p1", "Alexander Kaidanovsky", PersonKind.Actor, "Stalker", null),
                new MediaPerson("p2", "Andrei Tarkovsky", PersonKind.Director, null, null),
            ],
        });
        library.Update(library.Item("m3")! with { IsFavorite = true });
        library.Update(library.Item("m2")! with
        {
            Chapters =
            [
                new MediaChapter(TimeSpan.Zero, "Opening", null),
                new MediaChapter(TimeSpan.FromMinutes(30), "The Ocean", null),
                new MediaChapter(TimeSpan.FromMinutes(90), "Return", null),
            ],
        });

        // A season, so Play all has a list that is not the whole series.
        library.AddItem(new MediaItem
        {
            Id = "se1", Name = "Season 1", Kind = MediaKind.Season, SeriesId = "s1",
            SeriesName = "Bleach", IndexNumber = 1, ChildCount = 3,
        }, "s1");
        for (int n = 1; n <= 3; n++)
            library.AddItem(new MediaItem
            {
                Id = "s1e" + n, Name = "Season one, episode " + n, Kind = MediaKind.Episode,
                SeriesId = "s1", SeriesName = "Bleach", ParentIndexNumber = 1, IndexNumber = n,
                RunTime = TimeSpan.FromMinutes(24), PrimaryImageTag = "tag-s1e" + n,
            }, "se1");

        // Enough episodes after e1 that auto-play can run past the pass-out threshold
        // and still have one left to offer.
        for (int n = 5; n <= 9; n++)
            library.AddItem(new MediaItem
            {
                Id = "e" + n, Name = "Episode " + n, Kind = MediaKind.Episode,
                SeriesId = "s1", SeriesName = "Bleach", ParentIndexNumber = 1, IndexNumber = n,
                RunTime = TimeSpan.FromMinutes(24), PrimaryImageTag = "tag-e" + n,
            }, "s1");

        library.ResumeIds.Add("m2");
        library.ResumeIds.Add("e1");
        return library;
    }

    /// <summary>Hosts the app over a fixture. The poster cache is given an HttpClient that
    /// answers nothing, so no test ever reaches the network for artwork.</summary>
    private static void Host(FakeMediaLibrary library, Action<HeadlessSession, ApertureApp> body,
        Func<IMediaBackend>? backends = null, HttpMessageHandler? artwork = null,
        int width = 1440, int height = 900)
    {
        using var session = HeadlessSession.Create(width, height);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                backends, posterHttp: new HttpClient(artwork ?? new NoArtworkHandler()));
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            body(session, app);
        }
        finally
        {
            app?.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(settingsPath)!, true); } catch (IOException) { }
        }
    }

    /// <summary>Every artwork request fails, at once and without a socket.</summary>
    private sealed class NoArtworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    /// <summary>
    /// Answers every artwork request with a real PNG of the asked-for shape, so the poster
    /// cache has something to decode. Trickplay sheets have to be genuine pictures: the
    /// preview crops a rectangle out of one, and a cache that failed to decode would make
    /// the whole feature look absent rather than broken.
    /// </summary>
    private sealed class PngArtworkHandler(int width = 1600, int height = 360) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        /// <summary>When set, trickplay sheets wait for it: a test can rest the pointer on the
        /// scrubber before the sheet under it arrives.</summary>
        public TaskCompletionSource? HoldSheets { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            lock (Requested) Requested.Add(path);
            if (HoldSheets is { } hold && path.Contains("/Trickplay/", StringComparison.Ordinal))
                await hold.Task.WaitAsync(ct).ConfigureAwait(false);
            using var bitmap = new SkiaSharp.SKBitmap(width, height);
            using var canvas = new SkiaSharp.SKCanvas(bitmap);
            canvas.Clear(new SkiaSharp.SKColor(40, 60, 90));
            using var data = SkiaSharp.SKImage.FromBitmap(bitmap).Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(data.ToArray()),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return response;
        }
    }

    private static bool Shows(HeadlessSession session, string text) =>
        Walk(session.Window.AccessibilityTree).Any(n =>
            n.Name?.Contains(text, StringComparison.Ordinal) == true ||
            n.Value?.Contains(text, StringComparison.Ordinal) == true);

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    /// <summary>Pumps until the condition holds, or fails with the tree so the assertion
    /// says what was on screen instead of just "false".</summary>
    private static void PumpUntilShown(HeadlessSession session, string text, int maxFrames = 12)
    {
        for (int i = 0; i < maxFrames && !Shows(session, text); i++) session.PumpFrame();
        Assert.True(Shows(session, text),
            $"\"{text}\" never appeared.\n{session.Window.DumpAccessibilityTree()}");
    }

    // ── The shell ────────────────────────────────────────────────────────

    [Fact]
    public void ASignedInAppOpensOnHomeWithALibraryPerRailButton()
    {
        Host(Library(), (session, app) =>
        {
            Assert.Equal(ApertureStage.Browsing, app.Stage);
            Assert.Equal(ApertureView.Home, app.Current.View);

            PumpUntilShown(session, "Films");
            Assert.True(Shows(session, "Series"), "the second library is not on the rail");
            Assert.NotNull(session.Window.FindByAutomationId("rail-home"));
        });
    }

    [Fact]
    public void ContinueWatchingShowsWhatWasStartedAndSaysHowMuchIsLeft()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            // 167 minutes, 58 watched.
            Assert.True(Shows(session, "1 h 49 m left"), session.Window.DumpAccessibilityTree());
            Assert.Contains("GetResume(limit=12)", ((FakeMediaLibrary)app.Library!).Calls);
        });
    }

    [Fact]
    public void ARailButtonOpensThatLibrarysGrid()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            session.PumpFrame();

            Assert.Equal(ApertureView.Library, app.Current.View);
            Assert.Equal("movies", app.Current.Id);
            PumpUntilShown(session, "Stalker");
        });
    }

    [Fact]
    public void TheGridFiltersToUnwatchedWithoutReopeningTheLibrary()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "The Mirror");

            session.Window.FindByAutomationId("filter-unwatched")!.Click();
            for (int i = 0; i < 6; i++) session.PumpFrame();

            Assert.True(Shows(session, "Stalker"), "an unwatched film disappeared");
            Assert.False(Shows(session, "The Mirror"), "a watched film survived the filter");
        });
    }

    [Fact]
    public void OpeningACardShowsTheItemAndEscapeGoesBack()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");

            session.Window.FindByAutomationId("card-m1")!.Click();
            for (int i = 0; i < 6; i++) session.PumpFrame();

            Assert.Equal(ApertureView.Detail, app.Current.View);
            Assert.Equal("m1", app.Current.Id);
            PumpUntilShown(session, "Stalker is a fixture.");

            session.Window.PressKey(HultUI.Core.Events.KeyCode.Escape);
            session.PumpFrame();
            Assert.Equal(ApertureView.Library, app.Current.View);
        });
    }

    [Fact]
    public void ASeriesOpensItsSeasonsRatherThanTryingToPlayIt()
    {
        var library = Library();
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Series");
            session.Window.FindByAutomationId("rail-series")!.Click();
            PumpUntilShown(session, "Bleach");

            session.Window.FindByAutomationId("card-s1")!.Click();
            for (int i = 0; i < 8; i++) session.PumpFrame();

            Assert.Equal(ApertureView.Detail, app.Current.View);
            // A series shows its seasons; the episodes are one level down.
            PumpUntilShown(session, "Season 1");
            Assert.Null(app.Playback);

            session.Window.FindByAutomationId("card-se1")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();
            PumpUntilShown(session, "Season one, episode 1");
        });
    }

    // ── Keyboard ─────────────────────────────────────────────────────────

    [Fact]
    public void ArrowsWalkTheGridAndEnterOpensWhatTheyLandOn()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");

            // Nothing is under the cursor until an arrow says so; the first press lands
            // on the first card rather than moving from nowhere.
            Assert.Equal(-1, app.CursorIndex);
            session.Window.PressKey(HultUI.Core.Events.KeyCode.Right);
            for (int i = 0; i < 3; i++) session.PumpFrame();
            Assert.Equal(0, app.CursorIndex);

            session.Window.PressKey(HultUI.Core.Events.KeyCode.Right);
            for (int i = 0; i < 3; i++) session.PumpFrame();
            Assert.Equal(1, app.CursorIndex);
            var landed = app.CursorItem;
            Assert.NotNull(landed);

            session.Window.PressKey(HultUI.Core.Events.KeyCode.Enter);
            for (int i = 0; i < 6; i++) session.PumpFrame();

            Assert.Equal(ApertureView.Detail, app.Current.View);
            Assert.Equal(landed!.Id, app.Current.Id);
        });
    }

    [Fact]
    public void SpacePlaysWhatTheCursorIsOn()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(167) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");

            session.Window.PressKey(HultUI.Core.Events.KeyCode.Right);
            for (int i = 0; i < 3; i++) session.PumpFrame();
            var target = app.CursorItem!;

            session.Window.PressKey(HultUI.Core.Events.KeyCode.Space);
            for (int i = 0; i < 10; i++) session.PumpFrame();

            Assert.Equal(target.Id, app.PlayingItem?.Id);
        }, () => backend);
    }

    [Fact]
    public void TheCursorStartsOverWhenTheListDoes()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            session.Window.PressKey(HultUI.Core.Events.KeyCode.Right);
            session.Window.PressKey(HultUI.Core.Events.KeyCode.Right);
            for (int i = 0; i < 3; i++) session.PumpFrame();
            Assert.Equal(1, app.CursorIndex);

            // A different library is a different list: "the second poster" means nothing
            // in it, so the cursor is put away rather than carried over.
            session.Window.FindByAutomationId("rail-series")!.Click();
            for (int i = 0; i < 8; i++) session.PumpFrame();
            Assert.Equal(-1, app.CursorIndex);
        });
    }

    [Fact]
    public void TypingInSearchIsNotTheCursorsBusiness()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.PressKey(HultUI.Core.Events.KeyCode.Slash);
            for (int i = 0; i < 6; i++) session.PumpFrame();
            session.Window.TypeText("sta");
            for (int i = 0; i < 8; i++) session.PumpFrame();
            PumpUntilShown(session, "Stalker");

            // The caret keys belong to the field while it has the keyboard.
            session.Window.PressKey(HultUI.Core.Events.KeyCode.Left);
            for (int i = 0; i < 3; i++) session.PumpFrame();
            Assert.Equal(-1, app.CursorIndex);
        });
    }

    // ── Cast, people and favourites ──────────────────────────────────────

    [Fact]
    public void TheDetailPageCreditsTheCastAndTheCrew()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            session.Window.FindByAutomationId("card-m1")!.Click();
            PumpUntilShown(session, "Stalker is a fixture.");

            // An actor is named with the character; the crew is a line of names.
            Assert.True(Shows(session, "Alexander Kaidanovsky"), session.Window.DumpAccessibilityTree());
            Assert.True(Shows(session, "Andrei Tarkovsky"), "the director is not credited");
            Assert.NotNull(session.Window.FindByAutomationId("person-p1"));
        });
    }

    [Fact]
    public void ANameInTheCastOpensEverythingTheyAreIn()
    {
        var library = Library();
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            session.Window.FindByAutomationId("card-m1")!.Click();
            PumpUntilShown(session, "Alexander Kaidanovsky");

            session.Window.FindByAutomationId("person-p1")!.Click();
            for (int i = 0; i < 8; i++) session.PumpFrame();

            Assert.Equal(ApertureView.Person, app.Current.View);
            Assert.Equal("p1", app.Current.Id);
            Assert.Contains(library.Calls, c => c.Contains("person=p1", StringComparison.Ordinal));
            PumpUntilShown(session, "Stalker");
        });
    }

    [Fact]
    public void TheFavouritesViewListsWhatHasBeenHearted()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.PressKey(HultUI.Core.Events.KeyCode.Digit0);
            for (int i = 0; i < 8; i++) session.PumpFrame();

            Assert.Equal(ApertureView.Favourites, app.Current.View);
            PumpUntilShown(session, "The Mirror");
            // Only the hearted one.
            Assert.False(Shows(session, "Stalker"), "an unhearted film is in favourites");
        });
    }

    // ── Playback and what the server is told ─────────────────────────────

    [Fact]
    public void PlayingAFilmOpensTheStreamAndTellsTheServerItStarted()
    {
        var library = Library();
        var backend = new FakeMediaBackend();
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            session.Window.FindByAutomationId("card-m1")!.Click();
            PumpUntilShown(session, "Stalker is a fixture.");

            session.Window.FindByAutomationId("detail-play")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            Assert.True(app.Playback is not null, "calls: " + string.Join(" | ", library.Calls));
            Assert.Equal("m1", app.PlayingItem!.Id);
            Assert.Contains("StartPlayback(m1, at=0:00:00)", library.Calls);
            Assert.Contains(library.Calls, c => c.StartsWith("ReportStarted(m1", StringComparison.Ordinal));
            Assert.Contains(backend.Calls, c => c.StartsWith("Open(", StringComparison.Ordinal));
        }, () => backend);
    }

    [Fact]
    public void ResumingSeeksToWhereItWasLeftAndStoppingReportsThePosition()
    {
        var library = Library();
        // As long as the film the fixture resumes: the backend clamps a seek to the clip
        // it is playing, and the default fake clip is ten seconds long.
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(167) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");

            // The Continue watching card plays straight away, from where it was left.
            session.Window.FindByAutomationId("resume-m2")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            Assert.Contains("StartPlayback(m2, at=0:58:00)", library.Calls);
            // The seek waits for the backend to load — which for the fake is immediate —
            // and only then moves the position.
            Assert.Contains(backend.Calls, c => c.StartsWith("Seek(00:58:00", StringComparison.Ordinal));

            session.Window.PressKey(HultUI.Core.Events.KeyCode.Escape);
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.Null(app.Playback);
            Assert.Contains(library.Calls, c => c.StartsWith("ReportStopped(m2", StringComparison.Ordinal));
        }, () => backend);
    }

    /// <summary>
    /// Plays under the application's own loop (<see cref="HeadlessSession.RunLoop"/>) for
    /// <paramref name="seconds"/> of virtual time with nothing touched: the backend posts its
    /// position every 100 ms, as mpv does, with the frames that fall due. <paramref name="eachPass"/>
    /// runs once per pass of the loop.
    /// </summary>
    private static void PlayQuietly(HeadlessSession session, FakeMediaBackend backend, double seconds,
        Action? eachPass = null)
    {
        long end = session.Time.GetTimestamp() + TimeSpan.FromSeconds(seconds).Ticks;
        session.RunLoop(() =>
            {
                eachPass?.Invoke();
                return session.Time.GetTimestamp() >= end;
            },
            eventEvery: TimeSpan.FromMilliseconds(100),
            onEvent: () => HultUI.Application.InvokeOnMainThread(() => backend.Advance(TimeSpan.FromMilliseconds(100))),
            limit: TimeSpan.FromSeconds(seconds + 5));
    }

    private static int Count(FakeMediaLibrary library, string prefix) =>
        library.Calls.Count(c => c.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// A film that plays with nobody touching anything still tells the server where it is,
    /// every ten seconds — the dashboard's "now playing", and the resume point another device
    /// picks up. The heartbeat ran from the app's build, said to run on every frame of a film;
    /// the transport bar rebuilds on its own, so the app built once, when the bar hid, and from
    /// Aperture's first version until 2026-10-11 a quiet film sent no progress at all.
    /// </summary>
    [Fact]
    public void AFilmPlayingQuietlyIsReportedEveryTenSeconds_WithoutRebuildingTheApp()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(162) };
        Host(library, (session, app) =>
        {
            app.Play(library.Item("m1")!, TimeSpan.Zero);
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Equal(1, Count(library, "ReportStarted(m1"));
            PlayQuietly(session, backend, 4);
            // The bar has gone, and the title strip with it: the one app build quiet playback makes.
            Assert.Null(session.Window.FindByAutomationId("player-back"));
            int builds = app.Builds;

            var reports = new List<double>();
            int seen = Count(library, "ReportProgress(");
            long start = session.Time.GetTimestamp();
            PlayQuietly(session, backend, 61, () =>
            {
                int now = Count(library, "ReportProgress(");
                if (now == seen) return;
                seen = now;
                reports.Add(session.Time.GetElapsedTime(start).TotalSeconds);
            });

            // 4 s in when the minute began: reports at 10, 20 … 60 s of playing.
            Assert.Equal(6, reports.Count);
            for (int i = 1; i < reports.Count; i++)
                Assert.InRange(reports[i] - reports[i - 1], 10.0, 10.2);
            Assert.All(library.Calls.Where(c => c.StartsWith("ReportProgress(", StringComparison.Ordinal)),
                c => Assert.DoesNotContain("paused", c, StringComparison.Ordinal));
            Assert.Equal(builds, app.Builds);
        }, () => backend, width: 960, height: 600);
    }

    /// <summary>A pause is reported at once, and so is playing again: the server's "now
    /// playing" shows the pause, and the resume point is where it happened.</summary>
    [Fact]
    public void APauseIsReportedAtOnce_AndSoIsPlayingAgain()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(162) };
        Host(library, (session, app) =>
        {
            app.Play(library.Item("m1")!, TimeSpan.Zero);
            for (int i = 0; i < 4; i++) session.PumpFrame();
            PlayQuietly(session, backend, 3);

            app.Player!.Pause();
            session.PumpFrame();
            var at = app.Player.Position;
            Assert.True(at > TimeSpan.FromSeconds(2));
            Assert.Equal($"ReportProgress(m1, at={at:g}, paused)", library.Calls[^1]);

            app.Player.Play();
            session.PumpFrame();
            Assert.Equal($"ReportProgress(m1, at={at:g})", library.Calls[^1]);
        }, () => backend, width: 960, height: 600);
    }

    /// <summary>
    /// mpv loads a stream after it opens. Until it has, nothing is reported — a report at zero
    /// would move the resume point to the start of a film someone is half-way through — and
    /// the resume seek waits for the length. Once loaded, both happen with nothing else going on:
    /// the seek first, so the started report carries the resume point.
    /// </summary>
    [Fact]
    public void NothingIsReportedOrSoughtUntilTheStreamHasLoaded()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(167), HoldOpening = true };
        Host(library, (session, app) =>
        {
            app.Play(library.Item("m2")!, TimeSpan.FromMinutes(58));
            for (int i = 0; i < 10; i++) session.PumpFrame();

            Assert.NotNull(app.Playback);
            Assert.Equal(PlaybackState.Opening, app.Player!.State);
            Assert.Equal(0, Count(library, "ReportStarted("));
            Assert.DoesNotContain(backend.Calls, c => c.StartsWith("Seek(", StringComparison.Ordinal));

            backend.FinishOpening();
            session.PumpFrame();

            Assert.Equal(TimeSpan.FromMinutes(58), app.Player.Position);
            Assert.Equal(["ReportStarted(m2, at=0:58:00)"], library.Calls.Where(c => c.StartsWith("Report", StringComparison.Ordinal)));
        }, () => backend, width: 960, height: 600);
    }

    /// <summary>
    /// An episode that runs out is reported where it ended, the next one is offered, and when
    /// it starts the finished one is reported stopped at its end — the report that marks it
    /// watched — before the new stream's own started report.
    /// </summary>
    [Fact]
    public void AnEpisodeThatRunsOutIsReportedAtItsEnd_AndStoppedWhenTheNextStarts()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        Host(library, (session, app) =>
        {
            app.Play(library.Item("e1")!, TimeSpan.FromMinutes(20));
            for (int i = 0; i < 4; i++) session.PumpFrame();
            PlayQuietly(session, backend, 2);

            backend.Advance(TimeSpan.FromMinutes(5));      // past the end
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.Equal("ReportProgress(e1, at=0:24:00, paused)",
                library.Calls.Last(c => c.StartsWith("Report", StringComparison.Ordinal)));
            Assert.Equal("e5", app.UpNextItem?.Id);

            app.PlayUpNext(deliberate: true);
            for (int i = 0; i < 4; i++) session.PumpFrame();

            var reports = library.Calls.Where(c => c.StartsWith("Report", StringComparison.Ordinal)).ToList();
            int stopped = reports.IndexOf("ReportStopped(e1, at=0:24:00)");
            Assert.True(stopped >= 0, string.Join(" | ", reports));
            Assert.Equal("ReportStarted(e5, at=0:00:00)", reports[stopped + 1]);
        }, () => backend, width: 960, height: 600);
    }

    [Fact]
    public void WithoutADecoderPlayingSaysSoRatherThanOpeningABlankSheet()
    {
        var library = Library();
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            session.Window.FindByAutomationId("resume-m2")!.Click();
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.Null(app.Playback);
            Assert.DoesNotContain(library.Calls, c => c.StartsWith("StartPlayback", StringComparison.Ordinal));
            Assert.True(Shows(session, "libmpv"), session.Window.DumpAccessibilityTree());
        });   // no backends: this machine has no libmpv
    }

    [Fact]
    public void FullScreenCoversTheWindowRatherThanMaximizingIt()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(167) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            session.Window.FindByAutomationId("resume-m2")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            Assert.True(Shows(session, "APERTURE"), "the title bar should be up in a window");

            session.Window.PressKey(HultUI.Core.Events.KeyCode.F);
            for (int i = 0; i < 3; i++) session.PumpFrame();

            Assert.True(session.Window.Window.IsFullscreen, "F did not take the window fullscreen");
            // Maximizing is the thing this must not do: it leaves the task bar and the
            // title bar over the picture.
            Assert.False(session.Window.Window.IsMaximized, "full screen must not be a maximize");
            Assert.False(Shows(session, "APERTURE"), "the app's title bar survived into full screen");

            session.Window.PressKey(HultUI.Core.Events.KeyCode.F);
            for (int i = 0; i < 3; i++) session.PumpFrame();
            Assert.False(session.Window.Window.IsFullscreen);
            Assert.True(Shows(session, "APERTURE"));
        }, () => backend);
    }

    [Fact]
    public void StoppingAFilmLeavesFullScreenBehind()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(167) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            session.Window.FindByAutomationId("resume-m2")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();
            session.Window.PressKey(HultUI.Core.Events.KeyCode.F);
            for (int i = 0; i < 3; i++) session.PumpFrame();
            Assert.True(session.Window.Window.IsFullscreen);

            session.Window.PressKey(HultUI.Core.Events.KeyCode.Escape);
            for (int i = 0; i < 4; i++) session.PumpFrame();

            // A library filling the display with no task bar and no way out is a trap.
            Assert.False(session.Window.Window.IsFullscreen);
            Assert.Null(app.Playback);
        }, () => backend);
    }

    // ── Auto-play ────────────────────────────────────────────────────────

    /// <summary>Plays an episode and runs it to the end, the way the backend would.</summary>
    private static void PlayToTheEnd(HeadlessSession session, ApertureApp app, FakeMediaBackend backend,
        string automationId)
    {
        session.Window.FindByAutomationId(automationId)!.Click();
        for (int i = 0; i < 10; i++) session.PumpFrame();
        backend.Raise(new BackendEvent.EndReached());
        for (int i = 0; i < 10; i++) session.PumpFrame();
    }

    [Fact]
    public void AFinishedEpisodeOffersTheNextOneOnACountdown()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            PlayToTheEnd(session, app, backend, "resume-e1");

            Assert.NotNull(app.UpNextItem);
            Assert.Equal("e5", app.UpNextItem!.Id);
            Assert.False(app.UpNextIsAsking, "the first episode in a row should not ask");
            Assert.InRange(app.UpNextSecondsLeft, 1, (int)ApertureApp.UpNextCountdown.TotalSeconds);
            Assert.True(Shows(session, "Up next"), session.Window.DumpAccessibilityTree());
        }, () => backend);
    }

    [Fact]
    public void PlayNowStartsTheNextEpisodeAndTellsTheServer()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            PlayToTheEnd(session, app, backend, "resume-e1");

            session.Window.FindByAutomationId("up-next-play")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            Assert.Equal("e5", app.PlayingItem?.Id);
            Assert.Null(app.UpNextItem);
            Assert.Contains(library.Calls, c => c.StartsWith("StartPlayback(e5", StringComparison.Ordinal));
        }, () => backend);
    }

    [Fact]
    public void NotNowLeavesThePictureWhereItEnded()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            PlayToTheEnd(session, app, backend, "resume-e1");

            session.Window.FindByAutomationId("up-next-dismiss")!.Click();
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.Null(app.UpNextItem);
            Assert.Equal("e1", app.PlayingItem?.Id);   // still the finished episode, not the library
        }, () => backend);
    }

    [Fact]
    public void AfterThreeEpisodesInARowItAsksInsteadOfCountingDown()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            PlayToTheEnd(session, app, backend, "resume-e1");

            // Three that started themselves: e5, e6, e7.
            for (int i = 0; i < ApertureApp.AutoPlaysBeforeAsking; i++)
            {
                Assert.False(app.UpNextIsAsking, $"asked too early, after {i} auto-plays");
                app.PlayUpNext();                       // as the countdown would
                for (int f = 0; f < 10; f++) session.PumpFrame();
                backend.Raise(new BackendEvent.EndReached());
                for (int f = 0; f < 10; f++) session.PumpFrame();
            }

            Assert.True(app.UpNextIsAsking, "a fourth episode started without asking");
            Assert.Equal(0, app.UpNextSecondsLeft);
            Assert.True(Shows(session, "Still watching?"), session.Window.DumpAccessibilityTree());
            Assert.NotNull(session.Window.FindByAutomationId("up-next-continue"));
        }, () => backend);
    }

    [Fact]
    public void KeepWatchingRestartsTheCountdownAndTheRun()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            PlayToTheEnd(session, app, backend, "resume-e1");
            for (int i = 0; i < ApertureApp.AutoPlaysBeforeAsking; i++)
            {
                app.PlayUpNext();
                for (int f = 0; f < 10; f++) session.PumpFrame();
                backend.Raise(new BackendEvent.EndReached());
                for (int f = 0; f < 10; f++) session.PumpFrame();
            }
            Assert.True(app.UpNextIsAsking);

            session.Window.FindByAutomationId("up-next-continue")!.Click();
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.False(app.UpNextIsAsking, "the question stayed up after being answered");
            Assert.InRange(app.UpNextSecondsLeft, 1, (int)ApertureApp.UpNextCountdown.TotalSeconds);
        }, () => backend);
    }

    [Fact]
    public void AFilmEndsWithoutOfferingAnything()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(167) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            PlayToTheEnd(session, app, backend, "resume-m2");

            Assert.Null(app.UpNextItem);
        }, () => backend);
    }

    // ── Layout ───────────────────────────────────────────────────────────

    [Fact]
    public void TheLibraryCanBeAListInsteadOfAShelf()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");

            app.SetLayout(LibraryLayout.List);
            for (int i = 0; i < 8; i++) session.PumpFrame();

            // Same items, same cards by id — a different shape.
            Assert.True(Shows(session, "Stalker"), session.Window.DumpAccessibilityTree());
            Assert.NotNull(session.Window.FindByAutomationId("card-m1"));
            Assert.Equal(LibraryLayout.List, app.Settings.Data.Layout);

            // And the arrows still walk it, one row at a time.
            session.Window.PressKey(HultUI.Core.Events.KeyCode.Down);
            session.Window.PressKey(HultUI.Core.Events.KeyCode.Down);
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Equal(1, app.CursorIndex);
        });
    }

    [Fact]
    public void TitlesUnderPostersCanBeTurnedOffWithoutLosingThem()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");

            app.Settings.Data.ShowCardTitles = false;
            app.Refresh();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            // The caption is gone; the card still says what it is to a screen reader.
            var card = session.Window.FindByAutomationId("card-m1");
            Assert.NotNull(card);
            Assert.Contains("Stalker", card!.Name);
        });
    }

    // ── Home layout ──────────────────────────────────────────────────────

    /// <summary>
    /// Where a row sits in the drawn order. Read from each row's menu button: a shelf is a
    /// plain box, which the accessibility tree does not publish, and its heading menu is
    /// the one node that is both per-row and in document order.
    /// </summary>
    private static int RowPosition(HeadlessSession session, string automationId)
    {
        var rows = Walk(session.Window.AccessibilityTree)
            .Select(n => n.AutomationId)
            .Where(id => id?.StartsWith("row-", StringComparison.Ordinal) == true &&
                         id.EndsWith("-menu", StringComparison.Ordinal))
            .Select(id => id![..^"-menu".Length])
            .ToList();
        return rows.IndexOf(automationId);
    }

    [Fact]
    public void AFullContinueWatchingRowsSeeAllIsEverythingPartWatched()
    {
        var library = Library();   // two part-watched already: Solaris and an episode
        for (int i = 4; i <= 14; i++)
        {
            library.AddMovie("m" + i, "Film " + i, "movies", 2000 + i, 90, resume: TimeSpan.FromMinutes(i));
            library.ResumeIds.Add("m" + i);
        }
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "See all of Continue watching");
            session.Window.FindByAutomationId("row-resume-see-all")!.Click();
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.Equal(ApertureView.ContinueWatching, app.Current.View);
            // All thirteen, not the row's twelve: the last one added is past the row's end.
            PumpUntilShown(session, "Film 14");
            Assert.Equal(13, app.ResumeAll.Count);
            Assert.Contains("GetResume(limit=200)", library.Calls);
        });
    }

    [Fact]
    public void AFullNextUpRowsSeeAllIsEverySeriesNextEpisode()
    {
        var library = Library();
        for (int n = 1; n <= 13; n++)
        {
            library.AddItem(new MediaItem
            {
                Id = "next" + n, Name = "Pilot " + n, Kind = MediaKind.Episode,
                SeriesId = "show" + n, SeriesName = "Show " + n, ParentIndexNumber = 1, IndexNumber = 2,
                RunTime = TimeSpan.FromMinutes(30),
            }, "shows");
            library.NextUpIds.Add("next" + n);
        }
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "See all of Next up");
            session.Window.FindByAutomationId("row-nextup-see-all")!.Click();
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.Equal(ApertureView.NextUp, app.Current.View);
            Assert.Equal(13, app.NextUpAll.Count);
            Assert.Contains("GetNextUp(limit=200)", library.Calls);
            // The thirteenth, which the row's twelve left out.
            PumpUntilShown(session, "Show 13");
        });
    }

    [Fact]
    public void ShortContinueWatchingAndNextUpRowsHaveNoSeeAll()
    {
        var library = Library();
        library.NextUpIds.Add("e5");
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            PumpUntilShown(session, "Next up");
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Null(session.Window.FindByAutomationId("row-resume-see-all"));
            Assert.NotNull(session.Window.FindByAutomationId("row-resume-menu"));
            Assert.Null(session.Window.FindByAutomationId("row-nextup-see-all"));
            Assert.NotNull(session.Window.FindByAutomationId("row-nextup-menu"));
        });
    }

    [Fact]
    public void FavouritesHaveARowOnHomeAfterWhatIsBeingWatched()
    {
        Host(Library(), (session, app) =>   // The Mirror is hearted
        {
            PumpUntilShown(session, "Your favourites");
            for (int i = 0; i < 4; i++) session.PumpFrame();
            int favourites = RowPosition(session, "row-favourites");
            Assert.True(RowPosition(session, "row-resume") < favourites, session.Window.DumpAccessibilityTree());
            Assert.True(favourites < RowPosition(session, "row-latest-movies"));
            // One favourite is all of them: nothing more to see.
            Assert.Null(session.Window.FindByAutomationId("row-favourites-see-all"));
            Assert.NotNull(session.Window.FindByAutomationId("row-favourites-menu"));
        });
    }

    [Fact]
    public void MoreFavouritesThanTheRowHoldsHaveSeeAllOpeningTheFavouritesPage()
    {
        var library = Library();
        for (int i = 4; i <= 16; i++)
        {
            library.AddMovie("m" + i, "Film " + i, "movies", 2000 + i, 90);
            library.Update(library.Item("m" + i)! with { IsFavorite = true });
        }
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "See all of Your favourites");
            session.Window.FindByAutomationId("row-favourites-see-all")!.Click();
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.Equal(ApertureView.Favourites, app.Current.View);
            Assert.Equal(14, app.Favourites.Count);   // The Mirror and thirteen more
        });
    }

    [Fact]
    public void AFullLatestRowsSeeAllIsTheLibraryNewestFirstUnfiltered()
    {
        var library = Library();
        for (int i = 4; i <= 13; i++) library.AddMovie("m" + i, "Film " + i, "movies", 2000 + i, 90);
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "See all of Latest in Films");
            // A row holding its whole library has nothing more to see.
            Assert.True(Shows(session, "Latest in Series"));
            Assert.Null(session.Window.FindByAutomationId("row-latest-shows-see-all"));
            Assert.NotNull(session.Window.FindByAutomationId("row-latest-movies-menu"));

            // Left sorted and filtered some other way since the last visit.
            app.SetGridSort(ItemSort.Name, descending: false);
            app.SetGridPlayedFilter(false);
            session.Window.FindByAutomationId("row-latest-movies-see-all")!.Click();
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.Equal(ApertureView.Library, app.Current.View);
            Assert.Equal("movies", app.Current.Id);
            // The query the row is the first twelve of, so the grid carries on from it.
            Assert.Equal(ItemSort.DateAdded, app.GridSort);
            Assert.True(app.GridDescending);
            Assert.False(app.GridHasFilters);
            PumpUntilShown(session, "Newest (on)");
        });
    }

    [Fact]
    public void AHomeRowCanBeMovedAndStaysWhereItWasPut()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            PumpUntilShown(session, "Latest in Films");
            Assert.True(RowPosition(session, "row-resume") < RowPosition(session, "row-latest-movies"),
                session.Window.DumpAccessibilityTree());

            // Up twice: past "Your favourites", then past "Continue watching".
            app.MoveHomeRow("latest-movies", -1);
            app.MoveHomeRow("latest-movies", -1);
            for (int i = 0; i < 6; i++) session.PumpFrame();

            Assert.Equal(0, RowPosition(session, "row-latest-movies"));
            // And it is written down, so the next launch opens the same way.
            Assert.Equal("latest-movies", app.Settings.Data.HomeRowOrder.First());

            app.ResetHomeLayout();
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.Equal(0, RowPosition(session, "row-resume"));
            Assert.Empty(app.Settings.Data.HomeRowOrder);
        });
    }

    [Fact]
    public void HidingAHomeRowLeavesAWayToBringItBack()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");

            app.SetHomeRowHidden(ApertureApp.ResumeRowKey, true);
            for (int i = 0; i < 6; i++) session.PumpFrame();

            Assert.Equal(-1, RowPosition(session, "row-resume"));
            var back = session.Window.FindByAutomationId("home-show-resume");
            Assert.NotNull(back);

            // The footer is below the fold once Home has a few rows; a click is at a point,
            // so bring it up first, as a person would.
            for (int i = 0; i < 20 && back!.Element.Layout.AbsoluteBorderBox.Bottom > 880; i++)
            {
                session.Window.Wheel(700, 400, 0, -3);
                session.PumpFrame();
                back = session.Window.FindByAutomationId("home-show-resume");
            }
            back!.Click();
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.True(RowPosition(session, "row-resume") >= 0, session.Window.DumpAccessibilityTree());
            Assert.Empty(app.Settings.Data.HiddenHomeRows);
        });
    }

    [Fact]
    public void TheTwoWhatNowRowsCanBeDrawnAsOne()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");

            app.SetCombineUpNext(true);
            for (int i = 0; i < 8; i++) session.PumpFrame();

            Assert.True(Shows(session, "Up next"), session.Window.DumpAccessibilityTree());
            Assert.False(Shows(session, "Continue watching"), "the folded row is still drawn");
            Assert.Equal(-1, RowPosition(session, "row-nextup"));
            // What was part-way through is still on the shelf, under the new heading.
            Assert.NotNull(session.Window.FindByAutomationId("card-m2"));
        });
    }

    [Fact]
    public void CombiningNeverListsTheSameEpisodeTwice()
    {
        var resume = new List<MediaItem> { new() { Id = "e1", Name = "Episode one", Kind = MediaKind.Episode } };
        var next = new List<MediaItem>
        {
            new() { Id = "e1", Name = "Episode one", Kind = MediaKind.Episode },
            new() { Id = "e2", Name = "Episode two", Kind = MediaKind.Episode },
        };

        var merged = ApertureApp.MergeUpNext(resume, next);

        Assert.Equal(["e1", "e2"], merged.Select(i => i.Id));
    }

    // ── The card menu ────────────────────────────────────────────────────

    [Fact]
    public void MarkingWatchedFromACardKeepsTheShelfWhereItWas()
    {
        var library = Library();
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");

            // Put the cursor somewhere, then act on that card the way the menu does.
            session.Window.PressKey(HultUI.Core.Events.KeyCode.Right);
            session.Window.PressKey(HultUI.Core.Events.KeyCode.Right);
            for (int i = 0; i < 4; i++) session.PumpFrame();
            int cursor = app.CursorIndex;
            var item = app.CursorItem!;
            Assert.False(item.Played);

            var menu = app.CardMenu(item);
            menu.First(m => m.Label == "Mark watched").Action!();
            for (int i = 0; i < 8; i++) session.PumpFrame();

            Assert.Contains(library.Calls, c => c == $"SetPlayed({item.Id}, True)");
            // The grid was not thrown away and re-read: the cursor is where it was, and
            // the card now shows a tick.
            Assert.Equal(cursor, app.CursorIndex);
            Assert.True(app.CursorItem!.Played);
        });
    }

    [Fact]
    public void TheMenuKeyOpensTheCardMenuOverTheCursor()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");

            session.Window.PressKey(HultUI.Core.Events.KeyCode.Right);
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Empty(session.Window.OverlayContents);

            session.Window.PressKey(HultUI.Core.Events.KeyCode.Menu);
            for (int i = 0; i < 4; i++) session.PumpFrame();

            // The menu is an overlay over the grid — and it opened without a pointer,
            // which is the whole point of the key.
            Assert.Single(session.Window.OverlayContents);
        });
    }

    [Fact]
    public void TheCardMenuOffersWhatTheItemCanDo()
    {
        var library = Library();
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");

            // A part-watched film offers both ways in.
            var labels = app.CardMenu(library.Item("m2")!).Select(m => m.Label).ToList();
            Assert.Contains(labels, l => l!.StartsWith("Resume", StringComparison.Ordinal));
            Assert.Contains("Play from the start", labels);
            Assert.Contains("Mark watched", labels);
            Assert.Contains("Add to favourites", labels);

            // A hearted, watched film says the opposite of both.
            var watched = app.CardMenu(library.Item("m3")!).Select(m => m.Label).ToList();
            Assert.Contains("Mark unwatched", watched);
            Assert.Contains("Remove from favourites", watched);

            // An episode can jump to its series; a film has nowhere to jump.
            var episode = app.CardMenu(library.Item("e1")!).Select(m => m.Label).ToList();
            Assert.Contains("Go to Bleach", episode);
            Assert.DoesNotContain(labels, l => l!.StartsWith("Go to", StringComparison.Ordinal));
        });
    }

    // ── Filters ──────────────────────────────────────────────────────────

    [Fact]
    public void AGenreFilterNarrowsTheGridAndTheCountSaysSo()
    {
        var library = Library();
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "The Mirror");

            app.SetGridGenre(app.Genres.First(g => g.Name == "Science Fiction"));
            for (int i = 0; i < 8; i++) session.PumpFrame();

            Assert.True(Shows(session, "Stalker"));
            Assert.False(Shows(session, "The Mirror"), "a drama survived the science-fiction filter");
            Assert.True(Shows(session, "matching"), session.Window.DumpAccessibilityTree());
        });
    }

    [Fact]
    public void FavouritesOnlyLeavesTheHeartedOnes()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");

            session.Window.FindByAutomationId("filter-favourites")!.Click();
            for (int i = 0; i < 8; i++) session.PumpFrame();

            Assert.True(Shows(session, "The Mirror"), session.Window.DumpAccessibilityTree());
            Assert.False(Shows(session, "Stalker"), "an unhearted film survived the favourites filter");
        });
    }

    [Fact]
    public void ClearingFiltersBringsTheWholeLibraryBack()
    {
        var library = Library();
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");

            session.Window.FindByAutomationId("filter-unwatched")!.Click();
            for (int i = 0; i < 6; i++) session.PumpFrame();
            app.SetGridGenre(app.Genres.First(g => g.Name == "Science Fiction"));
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.True(app.GridHasFilters);

            session.Window.FindByAutomationId("filter-clear")!.Click();
            for (int i = 0; i < 8; i++) session.PumpFrame();

            Assert.False(app.GridHasFilters);
            Assert.True(Shows(session, "The Mirror"), "the watched drama did not come back");
        });
    }

    [Fact]
    public void TheUnwatchedChipTurnsItselfOffAgain()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");

            session.Window.FindByAutomationId("filter-unwatched")!.Click();
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.False(Shows(session, "The Mirror"));

            session.Window.FindByAutomationId("filter-unwatched")!.Click();
            for (int i = 0; i < 8; i++) session.PumpFrame();
            Assert.True(Shows(session, "The Mirror"), "the chip did not toggle back off");
        });
    }

    // ── The queue ────────────────────────────────────────────────────────

    /// <summary>Opens the fixture's season page, where Play all lives.</summary>
    private static void OpenSeason(HeadlessSession session, ApertureApp app)
    {
        PumpUntilShown(session, "Series");
        session.Window.FindByAutomationId("rail-series")!.Click();
        PumpUntilShown(session, "Bleach");
        session.Window.FindByAutomationId("card-s1")!.Click();
        PumpUntilShown(session, "Season 1");
        session.Window.FindByAutomationId("card-se1")!.Click();
        for (int i = 0; i < 10; i++) session.PumpFrame();
    }

    [Fact]
    public void PlayAllQueuesTheSeasonAndPlaysTheFirstEpisode()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        Host(library, (session, app) =>
        {
            OpenSeason(session, app);
            session.Window.FindByAutomationId("detail-play-all")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            Assert.True(app.QueueIsRunning);
            Assert.Equal(3, app.Queue.Count);
            Assert.Equal(0, app.QueueIndex);
            Assert.Equal("s1e1", app.PlayingItem?.Id);
            Assert.Equal("s1e2", app.QueueNext?.Id);
        }, () => backend);
    }

    [Fact]
    public void TheQueueDecidesWhatFollowsRatherThanTheServer()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        Host(library, (session, app) =>
        {
            OpenSeason(session, app);
            session.Window.FindByAutomationId("detail-play-all")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            library.Calls.Clear();
            backend.Raise(new BackendEvent.EndReached());
            for (int i = 0; i < 10; i++) session.PumpFrame();

            Assert.Equal("s1e2", app.UpNextItem?.Id);
            Assert.DoesNotContain(library.Calls, c => c.StartsWith("GetNextEpisode", StringComparison.Ordinal));

            session.Window.FindByAutomationId("up-next-play")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            Assert.Equal(1, app.QueueIndex);
            Assert.Equal("s1e2", app.PlayingItem?.Id);
        }, () => backend);
    }

    [Fact]
    public void StartingSomethingElseEndsTheQueue()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(167) };
        Host(library, (session, app) =>
        {
            OpenSeason(session, app);
            session.Window.FindByAutomationId("detail-play-all")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();
            Assert.True(app.QueueIsRunning);

            // Away to a film, by hand.
            session.Window.PressKey(HultUI.Core.Events.KeyCode.Escape);
            for (int i = 0; i < 6; i++) session.PumpFrame();
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            session.Window.FindByAutomationId("card-m1")!.Click();
            PumpUntilShown(session, "Stalker is a fixture.");
            session.Window.FindByAutomationId("detail-play")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            Assert.False(app.QueueIsRunning, "the queue followed the viewer to another film");
        }, () => backend);
    }

    [Fact]
    public void QOpensTheQueueAndARowJumpsToIt()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        Host(library, (session, app) =>
        {
            OpenSeason(session, app);
            session.Window.FindByAutomationId("detail-play-all")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            session.Window.PressKey(HultUI.Core.Events.KeyCode.Q);
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.True(app.QueueOpen);
            Assert.True(Shows(session, "Playing next"), session.Window.DumpAccessibilityTree());

            session.Window.FindByAutomationId("queue-2")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            Assert.False(app.QueueOpen);
            Assert.Equal(2, app.QueueIndex);
            Assert.Equal("s1e3", app.PlayingItem?.Id);
        }, () => backend);
    }

    [Fact]
    public void ShuffleKeepsTheOneYouStartedWithFirst()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        Host(library, (session, app) =>
        {
            OpenSeason(session, app);
            session.Window.FindByAutomationId("detail-shuffle")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            Assert.True(app.QueueIsRunning);
            Assert.Equal(3, app.Queue.Count);
            Assert.Equal("s1e1", app.Queue[0].Id);
            Assert.Equal("s1e1", app.PlayingItem?.Id);
            // Every episode is still in there, once.
            Assert.Equal(3, app.Queue.Select(i => i.Id).Distinct().Count());
        }, () => backend);
    }

    [Fact]
    public void AnEpisodeRowPlaysTheRestOfTheSeasonAfterIt()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        Host(library, (session, app) =>
        {
            OpenSeason(session, app);
            session.Window.FindByAutomationId("episode-s1e2")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            Assert.Equal("s1e2", app.PlayingItem?.Id);
            Assert.Equal(1, app.QueueIndex);
            Assert.Equal("s1e3", app.QueueNext?.Id);
        }, () => backend);
    }

    // ── Chapters ─────────────────────────────────────────────────────────

    [Fact]
    public void CListsTheChaptersAndClickingOneSeeksThere()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(167) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            session.Window.FindByAutomationId("resume-m2")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            session.Window.PressKey(HultUI.Core.Events.KeyCode.C);
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.True(app.ChaptersOpen);
            Assert.True(Shows(session, "The Ocean"), session.Window.DumpAccessibilityTree());

            session.Window.FindByAutomationId("chapter-2")!.Click();
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.False(app.ChaptersOpen, "picking a chapter left the list up");
            Assert.Contains(backend.Calls, c => c.StartsWith("Seek(01:30:00", StringComparison.Ordinal));
        }, () => backend);
    }

    /// <summary>
    /// The panel marks the chapter playing now and follows the playhead, rebuilding the app
    /// only when the playhead crosses into another chapter. It read the position in the app's
    /// build, so while it was open every position report (ten a second) rebuilt the whole app.
    /// </summary>
    [Fact]
    public void TheChapterPanelFollowsThePlayhead_RebuildingOnlyWhenTheChapterChanges()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(167) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            // Resumes at 58 minutes, inside "The Ocean" (30-90).
            session.Window.FindByAutomationId("resume-m2")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();
            session.Window.PressKey(HultUI.Core.Events.KeyCode.C);
            for (int i = 0; i < 4; i++) session.PumpFrame();
            for (int m = 58; m < 60; m++)
            {
                backend.Raise(new BackendEvent.PositionChanged(TimeSpan.FromMinutes(m)));
                session.PumpFrame();
            }
            Assert.True(Shows(session, "The Ocean, playing now"), session.Window.DumpAccessibilityTree());
            int builds = app.Builds;

            // Thirty reports inside the chapter.
            for (int m = 60; m < 90; m++)
            {
                backend.Raise(new BackendEvent.PositionChanged(TimeSpan.FromMinutes(m)));
                session.PumpFrame();
            }
            Assert.Equal(builds, app.Builds);

            backend.Raise(new BackendEvent.PositionChanged(TimeSpan.FromMinutes(91)));
            session.PumpFrame();
            Assert.Equal(builds + 1, app.Builds);
            Assert.True(Shows(session, "Return, playing now"));
            Assert.False(Shows(session, "The Ocean, playing now"));
        }, () => backend);
    }

    [Fact]
    public void PageDownStepsToTheNextChapterAndPageUpRestartsThisOne()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(167) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            // Resumes at 58 minutes, which is inside "The Ocean" (30–90).
            session.Window.FindByAutomationId("resume-m2")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            backend.Calls.Clear();
            session.Window.PressKey(HultUI.Core.Events.KeyCode.PageDown);
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Contains(backend.Calls, c => c.StartsWith("Seek(01:30:00", StringComparison.Ordinal));

            // Well inside a chapter, back restarts that chapter rather than leaving it.
            app.Player!.Seek(TimeSpan.FromMinutes(95));
            for (int i = 0; i < 4; i++) session.PumpFrame();
            backend.Calls.Clear();
            session.Window.PressKey(HultUI.Core.Events.KeyCode.PageUp);
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Contains(backend.Calls, c => c.StartsWith("Seek(01:30:00", StringComparison.Ordinal));

            // At a chapter's own start, back means the one before it.
            backend.Calls.Clear();
            session.Window.PressKey(HultUI.Core.Events.KeyCode.PageUp);
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Contains(backend.Calls, c => c.StartsWith("Seek(00:30:00", StringComparison.Ordinal));
        }, () => backend);
    }

    [Fact]
    public void AFileWithNoChaptersHasNoPanelAndNoButton()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            session.Window.FindByAutomationId("resume-e1")!.Click();   // no chapters on e1
            for (int i = 0; i < 10; i++) session.PumpFrame();

            session.Window.PressKey(HultUI.Core.Events.KeyCode.C);
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.False(app.ChaptersOpen);
            Assert.Null(session.Window.FindByAutomationId("player-chapters"));
        }, () => backend);
    }

    // ── Track memory ─────────────────────────────────────────────────────

    [Fact]
    public void TheAudioTrackYouPickIsTheOneTheNextEpisodeOpensWith()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        backend.AudioTracks.Add(new MediaTrack(1, "Japanese", "jpn"));
        backend.AudioTracks.Add(new MediaTrack(2, "English", "eng"));

        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            session.Window.FindByAutomationId("resume-e1")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            // The file opens on its own default; the viewer switches to English, the way
            // the overlay's audio menu does.
            Assert.Equal(1, app.Player!.SelectedAudioTrack);
            app.Player!.SelectAudioTrack(2);
            for (int i = 0; i < 6; i++) session.PumpFrame();

            // The next episode opens on English without being asked.
            backend.Raise(new BackendEvent.EndReached());
            for (int i = 0; i < 10; i++) session.PumpFrame();
            session.Window.FindByAutomationId("up-next-play")!.Click();
            for (int i = 0; i < 12; i++) session.PumpFrame();

            Assert.Equal("e5", app.PlayingItem?.Id);
            Assert.Equal(2, app.Player!.SelectedAudioTrack);
            Assert.Equal("eng", app.Settings.Data.PreferredAudioLanguage);
        }, () => backend);
    }

    [Fact]
    public void SubtitlesSwitchedOffStayOff()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        backend.AudioTracks.Add(new MediaTrack(1, "English", "eng"));
        backend.SubtitleTracks.Add(new MediaTrack(5, "English", "eng"));

        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            session.Window.FindByAutomationId("resume-e1")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            app.Player!.SelectSubtitleTrack(5);       // on…
            for (int i = 0; i < 4; i++) session.PumpFrame();
            app.Player!.SelectSubtitleTrack(null);    // …and off again
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.False(app.Settings.Data.SubtitlesOn);

            backend.Raise(new BackendEvent.EndReached());
            for (int i = 0; i < 10; i++) session.PumpFrame();
            session.Window.FindByAutomationId("up-next-play")!.Click();
            for (int i = 0; i < 12; i++) session.PumpFrame();

            // A file whose subtitle track matches the remembered language must not turn
            // them back on for someone who switched them off.
            Assert.Null(app.Player!.SelectedSubtitleTrack);
        }, () => backend);
    }

    // ── Failures ─────────────────────────────────────────────────────────

    [Fact]
    public void AFailedLoadSaysWhatWentWrongInsteadOfShowingAnEmptyShelf()
    {
        var library = Library();
        library.FailWith = new ApertureFailure(ApertureFailureKind.Network, "connection refused");
        Host(library, (session, app) =>
        {
            for (int i = 0; i < 8; i++) session.PumpFrame();
            Assert.True(Shows(session, "Can't reach"), session.Window.DumpAccessibilityTree());
        });
    }

    [Fact]
    public void AnExpiredTokenMidSessionSendsTheUserBackToSignIn()
    {
        var library = Library();
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Films");

            library.FailWith = new ApertureFailure(ApertureFailureKind.Unauthorized, "token revoked", 401);
            app.Refresh();
            for (int i = 0; i < 8; i++) session.PumpFrame();

            Assert.Equal(ApertureStage.Connect, app.Stage);
            Assert.True(Shows(session, "Sign in to your Jellyfin server."),
                session.Window.DumpAccessibilityTree());
        });
    }

    // ── Scrubber stills and chapter pictures ─────────────────────────────

    /// <summary>A film with chapter images and one set of trickplay sheets.</summary>
    private static FakeMediaLibrary WithStills()
    {
        var library = Library();
        library.Update(library.Item("m1")! with
        {
            RunTime = TimeSpan.FromMinutes(162),
            Chapters =
            [
                new MediaChapter(TimeSpan.Zero, "The Zone", "chap-0"),
                new MediaChapter(TimeSpan.FromMinutes(40), "The Room", "chap-1"),
            ],
            // 320x180 stills, ten to a sheet, one every ten seconds.
            Trickplay = [new TrickplayInfo(320, 180, 5, 2, 972, TimeSpan.FromSeconds(10))],
        });
        return library;
    }

    [Fact]
    public void TheStillForAPositionIsCutFromTheSheetThatHoldsIt()
    {
        var library = WithStills();
        var artwork = new PngArtworkHandler();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(162) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            session.Window.FindByAutomationId("card-m1")!.Click();
            PumpUntilShown(session, "Stalker is a fixture.");
            session.Window.FindByAutomationId("detail-play")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            // The first ask starts the download and says nothing; the sheet lands a frame
            // or two later, exactly as a poster does.
            session.PumpUntil(() => app.TrickplayFrame(TimeSpan.FromMinutes(2)) is not null,
                TimeSpan.FromSeconds(5));

            var frame = app.TrickplayFrame(TimeSpan.FromMinutes(2))!.Value;
            // Two minutes in is the 13th still: sheet 1, third across, first row.
            Assert.Equal(new HultUI.Core.Primitives.Rect(2 * 320, 0, 320, 180), frame.Source);
            Assert.Equal(TimeSpan.FromMinutes(2), frame.Time);
            Assert.Contains(artwork.Requested, r => r.Contains("/Trickplay/320/1.jpg", StringComparison.Ordinal));

            // Past the end of the stills there is nothing to show rather than a wrong frame.
            Assert.Null(app.TrickplayFrame(TimeSpan.FromHours(4)));
        }, () => backend, artwork);
    }

    [Fact]
    public void ASheetLandingUnderAPointerRestingOnTheScrubber_ShowsItsStillAtOnce()
    {
        // The pointer stops on the scrubber before the sheet under it has downloaded. The card
        // is the player's, which the app's build does not rebuild, so it used to wait for the
        // pointer's next move; the landing now asks the player to look again.
        var artwork = new PngArtworkHandler { HoldSheets = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(162) };
        Host(WithStills(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            session.Window.FindByAutomationId("card-m1")!.Click();
            PumpUntilShown(session, "Stalker is a fixture.");
            session.Window.FindByAutomationId("detail-play")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();
            app.Player!.Pause();                  // the bar stays up while paused
            session.PumpFrames(2);

            var track = session.Window.FindByAutomationId("video-seek")!.Element.Layout.AbsoluteContentBox;
            session.Window.MoveMouse(track.X + track.Width / 2f, track.Y + track.Height / 2f);
            session.PumpFrames(2);
            session.PumpUntil(() => { lock (artwork.Requested) return artwork.Requested.Any(r => r.Contains("/Trickplay/", StringComparison.Ordinal)); },
                TimeSpan.FromSeconds(5));
            Assert.Null(session.Window.FindByAutomationId("video-preview-card"));

            artwork.HoldSheets!.SetResult();
            session.PumpUntil(() => session.Window.FindByAutomationId("video-preview-card") is not null,
                TimeSpan.FromSeconds(5));
        }, () => backend, artwork);
    }

    [Fact]
    public void AChapterShowsThePictureTheServerExtractedForIt()
    {
        var artwork = new PngArtworkHandler(320, 180);
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(162) };
        Host(WithStills(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            session.Window.FindByAutomationId("card-m1")!.Click();
            PumpUntilShown(session, "Stalker is a fixture.");
            session.Window.FindByAutomationId("detail-play")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            app.ToggleChapters();
            session.PumpUntil(() => session.Window.FindByAutomationId("chapter-0-still") is not null,
                TimeSpan.FromSeconds(5));

            Assert.NotNull(session.Window.FindByAutomationId("chapter-1-still"));
            Assert.Contains(artwork.Requested, r => r.Contains("/Images/Chapter/1", StringComparison.Ordinal));
            // The picture is named for what it is, so a reader is not told "image".
            Assert.True(Shows(session, "The Room, at"), session.Window.DumpAccessibilityTree());
        }, () => backend, artwork);
    }

    [Fact]
    public void AFilmWithNoStillsShowsChaptersAsTheyAlwaysWere()
    {
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(167) };
        // m2 has chapters but no image tags, and no trickplay at all.
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Solaris");
            session.Window.FindByAutomationId("card-m2")!.Click();
            PumpUntilShown(session, "Solaris is a fixture.");
            session.Window.FindByAutomationId("detail-play")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();

            app.ToggleChapters();
            for (int i = 0; i < 6; i++) session.PumpFrame();

            Assert.True(Shows(session, "The Ocean"), session.Window.DumpAccessibilityTree());
            Assert.Null(session.Window.FindByAutomationId("chapter-0-still"));
            Assert.Null(app.TrickplayFrame(TimeSpan.FromMinutes(2)));
        }, () => backend);
    }

    // ── Artwork sizes ────────────────────────────────────────────────────

    /// <summary>
    /// Answers each artwork request with a PNG of the size it asked for (its fillWidth or
    /// fillHeight; a backdrop 16:9, anything else 2:3), so a test can tell which size a card
    /// is drawing. Requests whose URL contains <see cref="Hold"/> wait for
    /// <see cref="Release"/>, so a test can look at a card while its new picture is still on
    /// the way; setting <see cref="Hold"/> again holds the next ones.
    /// </summary>
    private sealed class SizedArtworkHandler : HttpMessageHandler
    {
        private TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _requested = [];
        private string? _hold;

        public string? Hold
        {
            get => _hold;
            set
            {
                _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _hold = value;
            }
        }

        public void Release() => _released.TrySetResult();

        public bool Asked(string text)
        {
            lock (_requested) return _requested.Any(r => r.Contains(text, StringComparison.Ordinal));
        }

        public int Count(string text)
        {
            lock (_requested) return _requested.Count(r => r.Contains(text, StringComparison.Ordinal));
        }

        public string Log()
        {
            lock (_requested) return string.Join("\n", _requested);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.PathAndQuery;
            lock (_requested) _requested.Add(url);
            var released = _released;
            if (Hold is { } hold && url.Contains(hold, StringComparison.Ordinal))
                await released.Task.WaitAsync(ct);

            static int? Asked(string url, string name) =>
                System.Text.RegularExpressions.Regex.Match(url, name + @"=(\d+)") is { Success: true } m
                    ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
            // Width over height, as a fraction: a backdrop 16:9, anything else a 2:3 poster.
            var (across, down) = url.Contains("/Images/Backdrop", StringComparison.Ordinal) ? (16, 9) : (2, 3);
            int width, height;
            if (Asked(url, "fillHeight") is { } h && Asked(url, "fillWidth") is null)
                (width, height) = ((h * across + down - 1) / down, h);
            else
            {
                width = Asked(url, "fillWidth") ?? 100;
                height = (width * down + across - 1) / across;
            }
            using var bitmap = new SkiaSharp.SKBitmap(width, height);
            using var canvas = new SkiaSharp.SKCanvas(bitmap);
            canvas.Clear(new SkiaSharp.SKColor(90, 60, 40));
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(data.ToArray()),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return response;
        }
    }

    /// <summary>The picture a card is drawing, or null while it shows its placeholder. Artwork
    /// is the image that covers its box; icons (the placeholder, the watched tick) fit theirs.</summary>
    private static HultUI.Core.Rendering.IImageSource? CardPicture(HeadlessSession session, string automationId) =>
        session.Window.FindByAutomationId(automationId) is { } card
            ? Descendants(card.Element).OfType<HultUI.Core.Elements.ImageElement>()
                .FirstOrDefault(i => i.Fit == HultUI.Core.Layout.ImageFit.Cover)?.Source
            : null;

    private static IEnumerable<HultUI.Core.Elements.Element> Descendants(HultUI.Core.Elements.Element element)
    {
        foreach (var child in element.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    [Fact]
    public void ArtworkIsAskedForAtTheWindowsScaleAndAgainWhenTheScaleChanges()
    {
        var artwork = new SizedArtworkHandler();
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            session.PumpUntil(() => CardPicture(session, "card-m1") is { IsLoaded: true }, TimeSpan.FromSeconds(5));

            // At 100 % a poster card 168 wide asks for 168 pixels: one per screen pixel, not
            // the four that a fixed "twice the width" held.
            Assert.True(artwork.Asked("/Items/m1/Images/Primary?tag=tag-m1&fillWidth=168"), artwork.Log());
            Assert.False(artwork.Asked("fillWidth=336"), artwork.Log());
            var old = CardPicture(session, "card-m1")!;
            Assert.Equal(168f, old.IntrinsicSize.Width);

            // The window moves to a 150 % monitor: the card asks again at 252. Until that
            // lands it keeps drawing the old picture rather than going blank.
            artwork.Hold = "fillWidth=252";
            session.Window.SetScale(1.5f);
            session.PumpUntil(() => artwork.Asked("/Items/m1/Images/Primary?tag=tag-m1&fillWidth=252"),
                TimeSpan.FromSeconds(5));
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Equal(1.5f, app.ArtworkScale);
            Assert.Same(old, CardPicture(session, "card-m1"));

            // Once it lands the card draws it, and the 100 % picture is gone from memory
            // rather than kept beside its replacement.
            artwork.Release();
            session.PumpUntil(() => CardPicture(session, "card-m1") is { IntrinsicSize.Width: 252f },
                TimeSpan.FromSeconds(5));
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.False(old.IsLoaded, "the 100 % picture was kept after its replacement landed");
            Assert.False(app.Posters.HasRetired, "pictures fetched for 100 % are still held");
        }, artwork: artwork);
    }

    [Fact]
    public void APictureWiderThanItsCardIsAskedForByHeight()
    {
        var library = Library();
        library.NextUpIds.Add("e5");
        var artwork = new SizedArtworkHandler();
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Next up");
            session.PumpUntil(() => app.Posters.PendingCount == 0 && artwork.Asked("/Items/e5/"),
                TimeSpan.FromSeconds(5));

            // An episode's picture is a 16:9 still. On a 2:3 poster card its height decides
            // the cover fit, so the card asks for the card's height in pixels: by width it
            // would arrive 168 x 95 and be drawn 2.7 times too large.
            Assert.True(artwork.Asked("/Items/e5/Images/Primary?tag=tag-e5&fillHeight=252"), artwork.Log());
            // A still card is the still's own shape: by width, as a poster card is.
            Assert.True(artwork.Asked("/Items/e1/Images/Primary?tag=tag-e1&fillWidth=300"), artwork.Log());
            Assert.False(artwork.Asked("&fillWidth=168&fillHeight"), artwork.Log());
        }, artwork: artwork);
    }

    /// <summary>The picture across the top of an item page — the widest cover-fit image on
    /// it — and the box it is laid out in.</summary>
    private static HultUI.Core.Elements.ImageElement? Hero(HeadlessSession session) =>
        session.Window.FindByAutomationId("detail") is { } page
            ? Descendants(page.Element).OfType<HultUI.Core.Elements.ImageElement>()
                .Where(i => i.Fit == HultUI.Core.Layout.ImageFit.Cover)
                .MaxBy(i => i.Layout.AbsoluteBorderBox.Width)
            : null;

    [Fact]
    public void AHeroIsAskedForAtTheDevicePixelsItCoversAndAgainWhenTheyOutgrowIt()
    {
        var library = Library();
        library.Update(library.Item("m1")! with { BackdropImageTag = "bd-m1" });
        var artwork = new SizedArtworkHandler();
        Host(library, (session, app) =>
        {
            const string Backdrop = "/Items/m1/Images/Backdrop?tag=bd-m1&";
            app.Navigate(new Route(ApertureView.Detail, "m1", "Stalker"));
            session.PumpUntil(() => Hero(session)?.Source is { IsLoaded: true, IntrinsicSize.Width: > 400f },
                TimeSpan.FromSeconds(5));

            // At 100 % the hero is the view beside the rail, 1,376 wide in a 1,440 window: it
            // asks for its width in device pixels, rounded up to a 128-pixel step.
            var box = LayoutProbe.Painted(Hero(session)!);
            Assert.Equal(1376f, box.Width);
            Assert.Equal(1408, ApertureApp.HeroPixels(box.Width, 1f));
            Assert.True(artwork.Asked(Backdrop + "fillWidth=1408"), artwork.Log());
            var old = Hero(session)!.Source!;
            Assert.Equal(1408f, old.IntrinsicSize.Width);

            // At 150 % it covers 2,064 device pixels: it asks again at 2,176 — not the layout
            // width it used to ask for at any scale, 0.7 source pixels per screen pixel — and
            // keeps drawing the old picture until the new one lands.
            artwork.Hold = "fillWidth=2176";
            session.Window.SetScale(1.5f);
            session.PumpUntil(() => artwork.Asked(Backdrop + "fillWidth=2176"), TimeSpan.FromSeconds(5));
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Same(old, Hero(session)!.Source);
            artwork.Release();
            session.PumpUntil(() => Hero(session)!.Source is { IntrinsicSize.Width: 2176f }, TimeSpan.FromSeconds(5));
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.False(old.IsLoaded, "the 100 % hero was kept after its replacement landed");
            Assert.True(Hero(session)!.Source!.IntrinsicSize.Width >= LayoutProbe.Painted(Hero(session)!).Width * 1.5f);

            // A window widened by a few pixels still has a source pixel for every screen pixel:
            // nothing is asked for.
            int asked = artwork.Count("/Images/Backdrop");
            session.Window.SetClientSize(1460, 900);
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.Equal(1396f, LayoutProbe.Painted(Hero(session)!).Width);
            Assert.Equal(asked, artwork.Count("/Images/Backdrop"));

            // Widened past the step (1,576 wide: 2,364 device pixels), the 2,176 picture would
            // be drawn larger than it is: it asks again, and the old picture stays up until the
            // new one lands, then goes.
            var narrower = Hero(session)!.Source!;
            artwork.Hold = "fillWidth=2432";
            session.Window.SetClientSize(1640, 900);
            session.PumpUntil(() => artwork.Asked(Backdrop + "fillWidth=2432"), TimeSpan.FromSeconds(5));
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Same(narrower, Hero(session)!.Source);
            artwork.Release();
            session.PumpUntil(() => Hero(session)!.Source is { IntrinsicSize.Width: 2432f }, TimeSpan.FromSeconds(5));
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.False(narrower.IsLoaded, "the narrower hero was kept after its replacement landed");
            Assert.False(app.Posters.HasRetired, "a hero size the window has left is still held");
        }, artwork: artwork);
    }

    [Fact]
    public void OnlyTheLastFewHeroesAreKept()
    {
        var library = Library();
        string[] pages = ["m1", "m2", "m3", "s1", "se1"];
        foreach (var id in pages)
            library.Update(library.Item(id)! with { BackdropImageTag = "bd-" + id });
        var artwork = new SizedArtworkHandler();
        Host(library, (session, app) =>
        {
            var heroes = new List<HultUI.Core.Rendering.IImageSource>();
            foreach (var id in pages)
            {
                app.Navigate(new Route(ApertureView.Detail, id, id));
                session.PumpUntil(() => Hero(session)?.Source is { IsLoaded: true, IntrinsicSize.Width: > 400f } s
                    && !heroes.Contains(s), TimeSpan.FromSeconds(5));
                heroes.Add(Hero(session)!.Source!);
            }
            Assert.Equal(5, ApertureApp.HeroesKept + 1);

            // Five pages in, the first page's hero is no longer held; the four after it are.
            session.PumpUntil(() => !heroes[0].IsLoaded, TimeSpan.FromSeconds(5));
            Assert.False(heroes[0].IsLoaded, "the hero of a page five back is still held");
            Assert.All(heroes.Skip(1), h => Assert.True(h.IsLoaded, "a hero of the last four pages was dropped"));

            // Back on the second page, its hero is still there; on the first it is asked
            // for again.
            int asked = artwork.Count("/Images/Backdrop");
            for (int i = 0; i < 3; i++) app.GoBack();
            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.Same(heroes[1], Hero(session)!.Source);
            Assert.Equal(asked, artwork.Count("/Images/Backdrop"));
            app.GoBack();
            session.PumpUntil(() => Hero(session)?.Source is { IsLoaded: true, IntrinsicSize.Width: > 400f },
                TimeSpan.FromSeconds(5));
            Assert.Equal(asked + 1, artwork.Count("/Images/Backdrop"));
        }, artwork: artwork);
    }

    [Fact]
    public void AHeroInANarrowWindowIsAskedForByHeight()
    {
        var library = Library();
        library.Update(library.Item("m1")! with { BackdropImageTag = "bd-m1" });
        var artwork = new SizedArtworkHandler();
        Host(library, (session, app) =>
        {
            // 636 x 396 is narrower than 16:9, so the backdrop's height decides the cover fit:
            // by width it would arrive 640 x 360 and be drawn larger than it is.
            session.Window.SetClientSize(700, 900);
            app.Navigate(new Route(ApertureView.Detail, "m1", "Stalker"));
            session.PumpUntil(() => Hero(session)?.Source is { IsLoaded: true, IntrinsicSize.Width: > 400f },
                TimeSpan.FromSeconds(5));
            Assert.Equal(396f, LayoutProbe.Painted(Hero(session)!).Height);
            Assert.True(artwork.Asked("/Items/m1/Images/Backdrop?tag=bd-m1&fillHeight=512"), artwork.Log());
            Assert.False(artwork.Asked("/Items/m1/Images/Backdrop?tag=bd-m1&fillWidth"), artwork.Log());
        }, artwork: artwork);
    }

    /// <summary>
    /// The scrim in from the hero's left covers 70 % of it and fades out on the way: it ended at
    /// 15 % of the ground's colour, so where it stopped the picture brightened at once, a seam
    /// down the hero (luma 144 → 169 on a real film's backdrop). It ends at nothing.
    /// </summary>
    [Fact]
    public void TheHerosLeftScrimFadesOutWithoutASeam()
    {
        var library = Library();
        library.Update(library.Item("m1")! with { BackdropImageTag = "bd-m1" });
        var artwork = new SizedArtworkHandler();
        Host(library, (session, app) =>
        {
            app.Navigate(new Route(ApertureView.Detail, "m1", "Stalker"));
            session.PumpUntil(() => Hero(session)?.Source is { IsLoaded: true, IntrinsicSize.Width: > 400f },
                TimeSpan.FromSeconds(5));
            for (int i = 0; i < 3; i++) session.PumpFrame();

            // A row across the scrim's right edge, high in the hero, where only the picture (one
            // flat colour here) and the two scrims are.
            var box = LayoutProbe.Painted(Hero(session)!);
            float edge = box.X + box.Width * 0.7f;
            int y = (int)(box.Y + box.Height * 0.2f);
            using var image = session.Window.CapturePixels();
            using var bitmap = SkiaSharp.SKBitmap.FromImage(image);
            int largest = 0, at = 0;
            for (int x = (int)edge - 12; x < (int)edge + 12; x++)
            {
                SkiaSharp.SKColor a = bitmap.GetPixel(x, y), b = bitmap.GetPixel(x + 1, y);
                int step = Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));
                if (step > largest) (largest, at) = (step, x);
            }
            // Past the edge the hero is as at its far right: the scrim really ends here.
            SkiaSharp.SKColor past = bitmap.GetPixel((int)edge + 20, y), far = bitmap.GetPixel((int)(box.X + box.Width) - 10, y);
            Assert.True(Math.Abs(past.Red - far.Red) <= 1 && Math.Abs(past.Green - far.Green) <= 1,
                $"past the scrim's edge the hero is {past}, at its far right {far}");
            Assert.True(largest <= 2, $"the hero brightens by {largest} levels from {bitmap.GetPixel(at, y)} at x {at} to {bitmap.GetPixel(at + 1, y)} (the scrim ends at {edge:0.#})");
        }, artwork: artwork);
    }

    // ── Streaming quality ────────────────────────────────────────────────

    [Fact]
    public void AQualityCeilingIsAskedForAndTakesEffectOnWhatIsPlaying()
    {
        var library = Library();
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(167) };
        Host(library, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            session.Window.FindByAutomationId("card-m1")!.Click();
            PumpUntilShown(session, "Stalker is a fixture.");

            session.Window.FindByAutomationId("detail-play")!.Click();
            for (int i = 0; i < 10; i++) session.PumpFrame();
            // No ceiling by default: the file as it is.
            Assert.Contains("StartPlayback(m1, at=0:00:00)", library.Calls);
            Assert.False(app.Playback!.Transcoding);

            app.SetMaxStreamingKbps(2_000);
            for (int i = 0; i < 10; i++) session.PumpFrame();

            // Re-opened under the new ceiling rather than waiting for the next film.
            Assert.Contains(library.Calls, c => c.Contains("max=2000000", StringComparison.Ordinal));
            Assert.Equal("m1", app.PlayingItem!.Id);
            Assert.True(app.Playback!.Transcoding);
            Assert.Equal(2_000, app.Settings.Data.MaxStreamingKbps);
        }, () => backend);
    }

    [Fact]
    public void TheQualityChoiceIsWhereTheOtherPlaybackFactsAre()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-settings")!.Click();
            for (int i = 0; i < 6; i++) session.PumpFrame();

            var chip = session.Window.FindByAutomationId("settings-quality");
            Assert.NotNull(chip);
            Assert.Contains("Original", chip!.Name);

            app.SetMaxStreamingKbps(4_000);
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.Contains("4 Mbps", session.Window.FindByAutomationId("settings-quality")!.Name);
        });
    }

    // ── Quick Connect ────────────────────────────────────────────────────

    [Fact]
    public void TheFrontDoorOffersACodeAndWantsAnAddressFirst()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.SignOut(keepAccount: false);
            for (int i = 0; i < 6; i++) session.PumpFrame();

            var code = session.Window.FindByAutomationId("connect-quickconnect");
            Assert.NotNull(code);

            // No address, so there is no server to ask — and the form says which field.
            code!.Click();
            for (int i = 0; i < 4; i++) session.PumpFrame();

            Assert.False(app.QuickConnectActive, "an attempt started without a server to ask");
            Assert.True(Shows(session, "Enter your server's address"), session.Window.DumpAccessibilityTree());
            Assert.Null(session.Window.FindByAutomationId("quickconnect-panel"));
        });
    }

    [Fact]
    public void AnAbandonedCodeLeavesTheFormAsItWas()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.SignOut(keepAccount: false);
            for (int i = 0; i < 6; i++) session.PumpFrame();

            session.Window.FindByAutomationId("connect-address")!.Click();
            session.Window.TypeText("jellyfin.example");
            for (int i = 0; i < 4; i++) session.PumpFrame();

            // State, not frames: the attempt is abandoned before any build runs it, so
            // this test never asks a name server about anything.
            app.BeginQuickConnect();
            Assert.True(app.QuickConnectActive);

            app.CancelQuickConnect();
            Assert.False(app.QuickConnectActive);
            Assert.Null(app.QuickConnectCode);

            for (int i = 0; i < 4; i++) session.PumpFrame();
            Assert.NotNull(session.Window.FindByAutomationId("connect-password"));
            Assert.Null(session.Window.FindByAutomationId("quickconnect-panel"));
        });
    }

    [Fact]
    public void ACodeAttemptAgainstNothingPutsTheFormBackWithASentence()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.SignOut(keepAccount: false);
            for (int i = 0; i < 6; i++) session.PumpFrame();

            // A scheme and nothing else: it fails in the address walk, before a socket.
            session.Window.FindByAutomationId("connect-address")!.Click();
            session.Window.TypeText("http://");
            for (int i = 0; i < 4; i++) session.PumpFrame();

            app.BeginQuickConnect();
            for (int i = 0; i < 6; i++) session.PumpFrame();

            Assert.False(app.QuickConnectActive);
            Assert.Null(app.QuickConnectCode);
            // The complaint is the sentence itself, not a line about what a host answered.
            Assert.True(Shows(session, "That isn't an address"), session.Window.DumpAccessibilityTree());
            Assert.NotNull(session.Window.FindByAutomationId("connect-password"));
        });
    }
}

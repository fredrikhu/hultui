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
        Func<IMediaBackend>? backends = null, HttpMessageHandler? artwork = null)
    {
        using var session = HeadlessSession.Create(1440, 900);
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requested) Requested.Add(request.RequestUri!.PathAndQuery);
            using var bitmap = new SkiaSharp.SKBitmap(width, height);
            using var canvas = new SkiaSharp.SKCanvas(bitmap);
            canvas.Clear(new SkiaSharp.SKColor(40, 60, 90));
            using var data = SkiaSharp.SKImage.FromBitmap(bitmap).Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(data.ToArray()),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
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

            // Up twice: past "Next up", then past "Continue watching".
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

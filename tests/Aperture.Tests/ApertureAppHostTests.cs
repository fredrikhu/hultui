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

        library.ResumeIds.Add("m2");
        library.ResumeIds.Add("e1");
        return library;
    }

    /// <summary>Hosts the app over a fixture. The poster cache is given an HttpClient that
    /// answers nothing, so no test ever reaches the network for artwork.</summary>
    private static void Host(FakeMediaLibrary library, Action<HeadlessSession, ApertureApp> body,
        Func<IMediaBackend>? backends = null)
    {
        using var session = HeadlessSession.Create(1440, 900);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                backends, posterHttp: new HttpClient(new NoArtworkHandler()));
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
    public void ASeriesOpensItsEpisodesRatherThanTryingToPlayIt()
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
            PumpUntilShown(session, "I Went to Market");
            Assert.Null(app.Playback);
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
}

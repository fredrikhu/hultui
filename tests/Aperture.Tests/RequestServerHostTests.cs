using Aperture;
using Aperture.Core;
using Aperture.Core.Requests;
using Aperture.Core.Testing;
using HultUI.Core.Accessibility;
using HultUI.Core.Platform;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// The request server inside the app, hosted headless over a <see cref="FakeMediaLibrary"/>
/// and a <see cref="FakeRequestServer"/>: that the rail grows its two doors, that a shelf
/// card for something the library has opens the library's page, that asking for a film or
/// some seasons sends what was asked, and that someone who approves requests can.
/// </summary>
public class RequestServerHostTests
{
    private static FakeMediaLibrary Library()
    {
        var library = new FakeMediaLibrary()
            .AddLibrary("movies", "Films", LibraryKind.Movies)
            .AddLibrary("shows", "Series", LibraryKind.TvShows);
        library.AddMovie("0123456789abcdef0123456789abcdef", "Stalker", "movies", 1979, 162);
        return library;
    }

    private static FakeRequestServer Requests(RequestPermission permissions = RequestPermission.Request)
    {
        var server = new FakeRequestServer(user: new RequestUser(1, "tester", permissions));
        server.AddMovie(603, "The Matrix", 1999, shelves: [DiscoverShelf.Trending, DiscoverShelf.PopularMovies]);
        server.AddMovie(1398, "Stalker", 1979, Availability.Available, "0123456789abcdef0123456789abcdef",
            DiscoverShelf.Trending);
        server.AddSeries(1399, "Game of Thrones", 2011, seasons:
        [
            new DiscoverSeason(1, "Season 1", 10, 2011, Availability.Available, Availability.Unknown),
            new DiscoverSeason(2, "Season 2", 10, 2012, Availability.Unknown, Availability.Unknown),
            new DiscoverSeason(3, "Season 3", 10, 2013, Availability.Unknown, Availability.Unknown),
        ], shelves: [DiscoverShelf.PopularSeries]);
        return server;
    }

    private static void Host(FakeMediaLibrary library, IRequestServer? requests, Action<HeadlessSession, ApertureApp> body)
    {
        using var session = HeadlessSession.Create(1440, 900);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                posterHttp: new HttpClient(new NoArtwork()), requests: requests);
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

    private sealed class NoArtwork : HttpMessageHandler
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

    private static void PumpUntilShown(HeadlessSession session, string text, int maxFrames = 12)
    {
        for (int i = 0; i < maxFrames && !Shows(session, text); i++) session.PumpFrame();
        Assert.True(Shows(session, text), $"\"{text}\" never appeared.\n{session.Window.DumpAccessibilityTree()}");
    }

    private static void Pump(HeadlessSession session, int frames = 6)
    {
        for (int i = 0; i < frames; i++) session.PumpFrame();
    }

    private static void Click(HeadlessSession session, string automationId)
    {
        var element = session.Window.FindByAutomationId(automationId);
        Assert.True(element is not null, $"no \"{automationId}\" on screen.\n{session.Window.DumpAccessibilityTree()}");
        element!.Click();
        Pump(session);
    }

    // ── The doors ────────────────────────────────────────────────────────

    [Fact]
    public void WithoutARequestServerTheRailHasNoDiscoverButton()
    {
        Host(Library(), null, (session, _) =>
        {
            PumpUntilShown(session, "Films");
            Assert.Null(session.Window.FindByAutomationId("rail-discover"));
            Assert.Null(session.Window.FindByAutomationId("rail-requests"));
        });
    }

    [Fact]
    public void DiscoverShowsTheShelvesWithWhereEachTitleStands()
    {
        Host(Library(), Requests(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            Click(session, "rail-discover");
            Assert.Equal(ApertureView.Discover, app.Current.View);

            PumpUntilShown(session, "Trending");
            Assert.True(Shows(session, "Popular films"));
            Assert.True(Shows(session, "Popular series"));
            // The badge's words ride on the card's accessible name.
            Assert.True(Shows(session, "Stalker, film, 1979, in your library"), session.Window.DumpAccessibilityTree());
        });
    }

    [Fact]
    public void ATitleTheLibraryHasOpensTheLibrarysOwnPage()
    {
        Host(Library(), Requests(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            Click(session, "rail-discover");
            PumpUntilShown(session, "Trending");

            Click(session, "discover-card-movie:1398");
            Assert.Equal(ApertureView.Detail, app.Current.View);
            Assert.Equal("0123456789abcdef0123456789abcdef", app.Current.Id);
        });
    }

    [Fact]
    public void TheConnectButtonOnSettingsTakesAClick()
    {
        // It sits at the right-hand end of its card, and the settings page has a max
        // width: the layout engine used to clamp the page and lay the card out at the
        // window's width, which put the button outside the page's bounds — drawn, lit,
        // and out of the pointer's reach. Shipped that way in 1.1.0.
        Host(Library(), null, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.Settings));
            Pump(session);

            Click(session, "settings-requests-connect");

            // No address typed, so the click's whole effect is the form saying so.
            Assert.Equal(ApertureFailureKind.Invalid, app.RequestConnectFailure?.Kind);
            PumpUntilShown(session, "Enter Jellyseerr's address");

            var button = session.Window.FindByAutomationId("settings-requests-connect")!.Element;
            var card = session.Window.FindByAutomationId("settings-requests-connect")!.Element.Parent!.Parent!;
            Assert.True(button.Layout.AbsoluteBorderBox.Right <= card.Layout.AbsoluteBorderBox.Right,
                "the button is drawn outside its card");
            Assert.True(card.Layout.Width <= 720, $"the card is {card.Layout.Width} wide on a 720 page");
        });
    }

    [Fact]
    public void ASessionTheServerEndedLeavesDiscoverRatherThanStrandingItThere()
    {
        var requests = Requests();
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            requests.FailWith = new ApertureFailure(ApertureFailureKind.Unauthorized, "401 Unauthorized", 401);
            Click(session, "rail-discover");
            Pump(session, 10);

            // The session is gone, and so are the doors to it — and the page behind one.
            Assert.Null(app.RequestServer);
            Assert.Null(session.Window.FindByAutomationId("rail-discover"));
            Assert.Equal(ApertureView.Home, app.Current.View);
            // Jellyfin is untouched: a request server ending its session signs nobody out.
            Assert.Equal(ApertureStage.Browsing, app.Stage);
            Assert.Equal(ApertureFailureKind.Unauthorized, app.RequestConnectFailure?.Kind);
        });
    }

    // ── Asking for things ────────────────────────────────────────────────

    [Fact]
    public void RequestingAFilmSendsItAndThePageSaysItIsWaiting()
    {
        var requests = Requests();
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            Click(session, "rail-discover");
            PumpUntilShown(session, "Trending");

            Click(session, "discover-card-movie:603");
            Assert.Equal(ApertureView.DiscoverTitle, app.Current.View);
            PumpUntilShown(session, "The Matrix is a fixture film.");

            Click(session, "discover-request");
            Assert.Contains("Request(movie:603)", requests.Calls);
            PumpUntilShown(session, "Requested, waiting for approval");
            // Asked for already: the button is gone rather than offering a second request.
            Assert.Null(session.Window.FindByAutomationId("discover-request"));
        });
    }

    [Fact]
    public void ASeriesAsksWhichSeasonsAndOffersOnlyTheOnesNotAlreadyIn()
    {
        var requests = Requests();
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            // Straight to the page: the series shelf is the third, below the fold of an
            // unscrolled headless window, where a synthesized click lands on nothing.
            app.Navigate(new Route(ApertureView.DiscoverTitle, "tv:1399", "Game of Thrones"));
            PumpUntilShown(session, "Game of Thrones is a fixture series.");

            Click(session, "discover-request");
            PumpUntilShown(session, "Request 2 seasons");
            // Season 1 is in the library: not a choice, so not a checkbox.
            Assert.Null(session.Window.FindByAutomationId("season-pick-1"));
            Assert.NotNull(session.Window.FindByAutomationId("season-pick-2"));

            Click(session, "season-pick-3");   // untick the last one
            PumpUntilShown(session, "Request 1 season");

            Click(session, "season-picker-confirm");
            Assert.Contains("Request(tv:1399 s2)", requests.Calls);
            Assert.Equal(Availability.Pending, requests.Title("tv:1399")!.Seasons[1].Availability);
            Assert.Equal(Availability.Unknown, requests.Title("tv:1399")!.Seasons[2].Availability);
        });
    }

    [Fact]
    public void SomeoneWhoMayNotRequestIsNotOfferedTheButton()
    {
        Host(Library(), Requests(RequestPermission.None), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            Click(session, "rail-discover");
            PumpUntilShown(session, "Trending");
            Click(session, "discover-card-movie:603");
            PumpUntilShown(session, "The Matrix is a fixture film.");
            Assert.Null(session.Window.FindByAutomationId("discover-request"));
        });
    }

    // ── Deciding ─────────────────────────────────────────────────────────

    [Fact]
    public void AManagerSeesWaitingRequestsOnTheRailAndApprovesOneFromTheList()
    {
        var requests = Requests(RequestPermission.ManageRequests | RequestPermission.Request);
        var waiting = requests.AddPendingRequest("movie:603");
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Requests, 1 waiting");

            Click(session, "rail-requests");
            Assert.Equal(ApertureView.Requests, app.Current.View);
            PumpUntilShown(session, "The Matrix, requested by anna, waiting for approval");

            Click(session, $"request-{waiting.Id}-approve");
            Assert.Contains($"Approve({waiting.Id})", requests.Calls);
            // Approving does not also open the title under the button.
            Assert.Equal(ApertureView.Requests, app.Current.View);
            PumpUntilShown(session, "The Matrix, requested by anna, on its way");
            Assert.Equal(Availability.Processing, requests.Title("movie:603")!.Item.Availability);
        });
    }

    [Fact]
    public void SomeoneWhoCannotApproveSeesOnlyTheirOwnRequestsAndNoVerbs()
    {
        var requests = Requests();
        requests.AddPendingRequest("movie:603");   // anna's
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            Assert.False(Shows(session, "waiting"), "the rail counted requests this user cannot act on");
            Click(session, "rail-requests");
            PumpUntilShown(session, "No requests here");
            Assert.Contains("GetRequests(All,mine,0)", requests.Calls);
        });
    }

    // ── Search ───────────────────────────────────────────────────────────

    [Fact]
    public void ASearchTheLibraryCannotAnswerOffersWhatCanBeRequested()
    {
        var requests = Requests();
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.Search));
            app.SetSearchTerm("Matrix");
            PumpUntilShown(session, "Nothing in your library matches");
            PumpUntilShown(session, "Not in your library");
            Assert.NotNull(session.Window.FindByAutomationId("discover-card-movie:603"));
            Assert.Contains("Search(Matrix)", requests.Calls);
        });
    }

    [Fact]
    public void ASearchLeavesOutWhatTheLibraryAlreadyShows()
    {
        Host(Library(), Requests(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.Search));
            app.SetSearchTerm("Stalker");
            PumpUntilShown(session, "1 result in your library");
            Pump(session);
            // The request server knows Stalker too, as available — it is not offered twice.
            Assert.Null(session.Window.FindByAutomationId("discover-card-movie:1398"));
        });
    }
}

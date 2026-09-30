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

    private static FakeRequestServer Requests(RequestPermission permissions = RequestPermission.Request,
        RequestServerInfo? info = null)
    {
        var server = new FakeRequestServer(info, new RequestUser(1, "tester", permissions));
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

    private static void Host(FakeMediaLibrary library, IRequestServer? requests, Action<HeadlessSession, ApertureApp> body,
        Func<HultUI.Media.IMediaBackend>? backends = null, Action<ApertureSettings>? setup = null)
    {
        using var session = HeadlessSession.Create(1440, 900);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            setup?.Invoke(settings);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                backends: backends, posterHttp: new HttpClient(new NoArtwork()), requests: requests);
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

    // ── Slice 7b ─────────────────────────────────────────────────────────

    /// <summary>The library, with Game of Thrones in it — season 1 only, and the TMDB id
    /// Jellyfin scraped for it, which is what joins it to the request server's record.</summary>
    private static FakeMediaLibrary LibraryWithSeries()
    {
        var library = Library();
        library.AddItem(new MediaItem
        {
            Id = "got", Name = "Game of Thrones", Kind = MediaKind.Series, TmdbId = 1399, ChildCount = 1,
            Overview = "Seven noble families fight.",
        }, "shows");
        library.AddItem(new MediaItem
        {
            Id = "got-s1", Name = "Season 1", Kind = MediaKind.Season, SeriesId = "got",
            SeriesName = "Game of Thrones", IndexNumber = 1, ChildCount = 10,
        }, "got");
        return library;
    }

    [Fact]
    public void ASeriesInTheLibraryOffersTheSeasonsItIsMissing()
    {
        var requests = Requests();
        Host(LibraryWithSeries(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.Detail, "got", "Game of Thrones"));
            // Seasons 2 and 3 are the request server's and not the library's.
            PumpUntilShown(session, "Request Seasons 2–3");
            Assert.Contains("GetDetail(tv:1399)", requests.Calls);
            Assert.DoesNotContain(requests.Calls, c => c.StartsWith("GetRecommendations", StringComparison.Ordinal));

            Click(session, "detail-request-seasons");
            Assert.Equal(ApertureView.DiscoverTitle, app.Current.View);
            Assert.Equal("tv:1399", app.Current.Id);
            // The picker is open on arrival, with exactly the missing seasons ticked.
            PumpUntilShown(session, "Request 2 seasons");
        });
    }

    [Fact]
    public void ALibrarySeriesSaysWhichSeasonsAreAlreadyAskedFor()
    {
        var requests = Requests();
        requests.AddPendingRequest("tv:1399", [2]);
        Host(LibraryWithSeries(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.Detail, "got", "Game of Thrones"));
            PumpUntilShown(session, "Season 2 requested, waiting for approval");
            // Only the season nobody has asked for is offered.
            PumpUntilShown(session, "Request Season 3");
        });
    }

    [Fact]
    public void ALibraryItemWithoutATmdbIdAsksTheRequestServerNothing()
    {
        var requests = Requests();
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.Detail, "0123456789abcdef0123456789abcdef", "Stalker"));
            PumpUntilShown(session, "Stalker is a fixture.");
            Assert.DoesNotContain(requests.Calls, c => c.StartsWith("GetDetail", StringComparison.Ordinal));
            Assert.Null(session.Window.FindByAutomationId("detail-request-seasons"));
        });
    }

    [Fact]
    public void ADownloadShowsHowFarItHasGotOnTheTitleAndInTheList()
    {
        var requests = Requests(RequestPermission.Request | RequestPermission.AutoApprove | RequestPermission.ManageRequests);
        requests.RequestAsync(new NewRequest(DiscoverKind.Movie, 603, [])).GetAwaiter().GetResult();
        requests.SetDownloads("movie:603",
            [new DownloadProgress("The.Matrix.1999.mkv", 1000, 250, "downloading", TimeSpan.FromMinutes(12), null)]);
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.DiscoverTitle, "movie:603", "The Matrix"));
            PumpUntilShown(session, "Downloading · 75% · about 12 min left");

            // The title page keeps re-reading itself while something downloads…
            Assert.True(app.RefreshingProgress);

            Click(session, "rail-requests");
            PumpUntilShown(session, "The Matrix, requested by tester, on its way, downloading 75% · about 12 min left");
        });
    }

    [Fact]
    public void ATitleWithNothingDownloadingIsNotPolled()
    {
        Host(Library(), Requests(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.DiscoverTitle, "movie:603", "The Matrix"));
            PumpUntilShown(session, "The Matrix is a fixture film.");
            // …and one with nothing on its way costs the server nothing after it has loaded.
            Assert.False(app.RefreshingProgress);
        });
    }

    [Fact]
    public void ASpentAllowanceDisablesRequestAndSaysWhy()
    {
        var requests = Requests();
        requests.Quotas = new RequestQuotas(
            new RequestQuota(5, 7, 5, 0, Restricted: true),
            new RequestQuota(null, 7, 0, null, false));
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.DiscoverTitle, "movie:603", "The Matrix"));
            PumpUntilShown(session, "No film requests left this week (the limit is 5)");

            var button = (HultUI.Core.Elements.ButtonElement)session.Window.FindByAutomationId("discover-request")!.Element;
            Assert.False(button.IsEnabled);
            Click(session, "discover-request");
            Pump(session);
            Assert.DoesNotContain(requests.Calls, c => c.StartsWith("Request(", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void ARationedSeriesPickerTicksNoMoreThanTheAllowance()
    {
        var requests = Requests();
        requests.Quotas = new RequestQuotas(
            new RequestQuota(null, 7, 0, null, false),
            new RequestQuota(3, 7, 2, 1, false));
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.DiscoverTitle, "tv:1399", "Game of Thrones"));
            PumpUntilShown(session, "1 of 3 season requests left this week");   // under the button, before opening it
            Click(session, "discover-request");
            PumpUntilShown(session, "Request 1 season");

            // Ticking the other one is more than the allowance: said, and not sent.
            Click(session, "season-pick-3");
            PumpUntilShown(session, "Only 1 left this week — untick some");
            var confirm = (HultUI.Core.Elements.ButtonElement)session.Window.FindByAutomationId("season-picker-confirm")!.Element;
            Assert.False(confirm.IsEnabled);
        });
    }

    [Fact]
    public void TheServersHideAvailableSwitchLeavesTheLibrarysTitlesOffTheShelves()
    {
        var requests = Requests(info: new RequestServerInfo("http://jellyseerr.test:5055", "Jellyseerr", "2.7.3",
            HideAvailable: true));
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            Click(session, "rail-discover");
            PumpUntilShown(session, "The Matrix, film, 1999");
            Assert.Null(session.Window.FindByAutomationId("discover-card-movie:1398"));   // Stalker: in the library
        });
    }

    // ── Slice 7b, second half ────────────────────────────────────────────

    [Fact]
    public void DiscoverFollowsTheAdministratorsLayout()
    {
        var requests = Requests();
        requests.AddGenre(DiscoverKind.Movie, 18, "Drama", "movie:603", "movie:1398");
        requests.Layout =
        [
            new DiscoverSlider(1, SliderKind.PopularTv, null, null, Enabled: true, Order: 0),
            new DiscoverSlider(2, SliderKind.Trending, null, null, Enabled: false, Order: 1),
            new DiscoverSlider(3, SliderKind.MovieGenres, null, null, Enabled: true, Order: 2),
            new DiscoverSlider(4, SliderKind.Studios, null, null, Enabled: true, Order: 3),
        ];
        requests.AddCustomRow(new DiscoverSlider(5, SliderKind.MovieKeyword, "Mind-benders", "4565", true, 4), "movie:603");
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            Click(session, "rail-discover");
            PumpUntilShown(session, "Popular series");
            Assert.True(Shows(session, "Films by genre"));
            Assert.True(Shows(session, "Mind-benders"));   // a custom row, by the name it was given
            Assert.False(Shows(session, "Trending"), "a disabled row was drawn");
            // Studios come from the list compiled in, as the server's web page draws them —
            // there is nothing to ask the server for until one is opened.
            Assert.NotNull(session.Window.FindByAutomationId("company-movie-2"));
            Assert.DoesNotContain(requests.Calls, c => c.StartsWith("DiscoverCompany", StringComparison.Ordinal));

            Click(session, "genre-movie-18");
            Assert.Equal(ApertureView.DiscoverGenre, app.Current.View);
            PumpUntilShown(session, "The Matrix, film, 1999");
            Assert.Contains("DiscoverGenre(Movie,18,1)", requests.Calls);
        });
    }

    [Fact]
    public void ACustomRowWithMoreThanItsFirstPageOffersSeeAll()
    {
        var requests = Requests();
        requests.SliderPageSize = 1;   // so two titles make two pages
        requests.AddMovie(604, "The Matrix Reloaded", 2003, shelves: [DiscoverShelf.PopularMovies]);
        requests.Layout = [new DiscoverSlider(1, SliderKind.PopularMovies, null, null, Enabled: true, Order: 0)];
        requests.AddCustomRow(new DiscoverSlider(5, SliderKind.MovieKeyword, "Mind-benders", "4565", true, 1),
            "movie:603", "movie:604");
        requests.AddCustomRow(new DiscoverSlider(6, SliderKind.TvGenre, "Dramas", "18", true, 2), "tv:1399");
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            Click(session, "rail-discover");
            PumpUntilShown(session, "See all of Mind-benders");
            // A built-in row pages in place, as the server's own page does; a custom row
            // with one page has nothing more to see.
            Assert.Null(session.Window.FindByAutomationId("discover-row-slider-1-see-all"));
            Assert.Null(session.Window.FindByAutomationId("discover-row-slider-6-see-all"));

            Click(session, "discover-row-slider-5-see-all");
            Assert.Equal(ApertureView.DiscoverAll, app.Current.View);
            Assert.Equal("5", app.Current.Id);
            PumpUntilShown(session, "The Matrix, film, 1999");
            Assert.False(Shows(session, "The Matrix Reloaded"));

            Click(session, "see-all-view-more");
            PumpUntilShown(session, "The Matrix Reloaded, film, 2003");
            Assert.Contains("GetSlider(MovieKeyword,5,p2)", requests.Calls);

            // Back is Discover, with the rail still saying so.
            app.GoBack();
            Pump(session);
            Assert.Equal(ApertureView.Discover, app.Current.View);
        });
    }

    [Fact]
    public void SeeAllForARowNoLongerInTheLayoutSaysSo()
    {
        var requests = Requests();
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.DiscoverAll, "99", "Mind-benders"));
            PumpUntilShown(session, "That row is gone");
            Assert.DoesNotContain(requests.Calls, c => c.StartsWith("GetSlider(", StringComparison.Ordinal)
                                                      && c.Contains(",99", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void AStreamingServiceRowIsDrawnByTheNameItWasGiven()
    {
        var requests = Requests();
        requests.Layout = [new DiscoverSlider(1, SliderKind.PopularMovies, null, null, Enabled: true, Order: 0)];
        requests.AddCustomRow(new DiscoverSlider(7, SliderKind.TvStreaming, "On Netflix", "SE,8", true, 1), "tv:1399");
        requests.AddCustomRow(new DiscoverSlider(8, SliderKind.MovieStreaming, "On Disney+", "SE,337", false, 2), "movie:603");
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            Click(session, "rail-discover");
            PumpUntilShown(session, "On Netflix");
            Assert.NotNull(session.Window.FindByAutomationId("discover-card-tv:1399"));
            Assert.Contains("GetSlider(TvStreaming,7)", requests.Calls);
            // Switched off by the administrator: not asked for, not drawn.
            Assert.False(Shows(session, "On Disney+"));
            Assert.DoesNotContain("GetSlider(MovieStreaming,8)", requests.Calls);
        });
    }

    [Fact]
    public void ANetworkCardOpensItsSeriesAPageAtATime()
    {
        var requests = Requests();
        requests.AddCompanyTitles(DiscoverKind.Tv, 213, "tv:1399");
        requests.Layout =
        [
            new DiscoverSlider(1, SliderKind.Networks, null, null, Enabled: true, Order: 0),
            new DiscoverSlider(2, SliderKind.Studios, null, null, Enabled: false, Order: 1),
        ];
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            Click(session, "rail-discover");
            PumpUntilShown(session, "Networks");
            // Named for what it opens; the logo is decoration.
            PumpUntilShown(session, "Netflix series");
            Assert.Null(session.Window.FindByAutomationId("company-movie-2"));   // Studios is switched off

            Click(session, "company-tv-213");
            Assert.Equal(ApertureView.DiscoverCompany, app.Current.View);
            Assert.Equal("tv:213", app.Current.Id);
            PumpUntilShown(session, "Game of Thrones");
            Assert.Contains("DiscoverCompany(Tv,213,1)", requests.Calls);
            Assert.True(Shows(session, "Netflix series"));   // the page's heading
        });
    }

    [Fact]
    public void ANameInTheCastOpensEverythingTheyAreIn()
    {
        var requests = Requests();
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.DiscoverTitle, "movie:603", "The Matrix"));
            PumpUntilShown(session, "Fixture Actor as The Lead");
            Click(session, "cast-9603");
            Assert.Equal(ApertureView.DiscoverPerson, app.Current.View);
            PumpUntilShown(session, "Fixture Actor is a fixture actor.");
            PumpUntilShown(session, "The Matrix, film, 1999");
        });
    }

    [Fact]
    public void AWatchlistedTitleIsMarkedAndHasARowOfItsOwn()
    {
        var requests = Requests();
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.DiscoverTitle, "movie:603", "The Matrix"));
            PumpUntilShown(session, "Add to your watchlist");
            Click(session, "discover-watchlist");
            Assert.Contains("Watchlist(movie:603)", requests.Calls);
            PumpUntilShown(session, "On your watchlist");

            Click(session, "rail-discover");
            PumpUntilShown(session, "Your watchlist");
        });
    }

    [Fact]
    public void SomeoneWhoMayChooseSendsTheServerQualityAndFolderTheyPicked()
    {
        var requests = Requests(RequestPermission.Request | RequestPermission.RequestAdvanced);
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.DiscoverTitle, "movie:603", "The Matrix"));
            PumpUntilShown(session, "The Matrix is a fixture film.");
            Click(session, "discover-request-options");
            // The chips say what the server would use: its default profile and folder.
            PumpUntilShown(session, "Quality: HD-1080p");
            PumpUntilShown(session, "Folder: /media/movies");

            Click(session, "request-options-confirm");
            var sent = Assert.Single(requests.SentOptions);
            Assert.Equal(new RequestOptions(0, 4, "/media/movies"), sent);
        });
    }

    [Fact]
    public void SomeoneWhoMayNotChooseIsNotOfferedTheChoices()
    {
        Host(Library(), Requests(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.DiscoverTitle, "movie:603", "The Matrix"));
            PumpUntilShown(session, "The Matrix is a fixture film.");
            Assert.Null(session.Window.FindByAutomationId("discover-request-options"));
        });
    }

    [Fact]
    public void AProblemIsReportedFromThePlayerAgainstTheRequestServersRecord()
    {
        var library = Library();
        // Stalker, as Jellyfin scraped it: the TMDB id is what finds it on the request server.
        library.Update(library.Item("0123456789abcdef0123456789abcdef")! with { TmdbId = 1398 });
        var requests = Requests(RequestPermission.Request | RequestPermission.CreateIssues);
        var backend = new HultUI.Media.Testing.FakeMediaBackend { Duration = TimeSpan.FromMinutes(162) };
        Host(library, requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Play(library.Item("0123456789abcdef0123456789abcdef")!, TimeSpan.Zero);
            Pump(session, 10);
            Assert.NotNull(app.PlayingItem);

            app.ToggleReport();
            PumpUntilShown(session, "What's wrong with Stalker?");
            Click(session, "report-type-audio");
            session.Window.FindByAutomationId("report-message")!.Element.Focus();
            session.Window.TypeText("No sound after the first reel");
            Pump(session, 2);
            // Typing a space in the report is not the player's pause key.
            Assert.NotNull(app.PlayingItem);

            Click(session, "report-send");
            var issue = Assert.Single(requests.Reports);
            Assert.Equal(IssueType.Audio, issue.Type);
            Assert.Equal(100_000 + 1398, issue.MediaId);   // the server's record, not TMDB's number
            Assert.Equal("No sound after the first reel", issue.Message);
            Assert.False(app.ReportOpen);
        }, () => backend);
    }

    [Fact]
    public void ARequestThatArrivesIsAnnouncedOnceAndNotOnTheFirstLook()
    {
        var requests = Requests(RequestPermission.Request | RequestPermission.AutoApprove);
        requests.RequestAsync(new NewRequest(DiscoverKind.Movie, 603, [])).GetAwaiter().GetResult();
        requests.AddMovie(604, "The Matrix Reloaded", 2003, shelves: []);
        requests.RequestAsync(new NewRequest(DiscoverKind.Movie, 604, [])).GetAwaiter().GetResult();
        requests.Arrive("movie:604", "reloaded");   // already here before the first look
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");

            app.CheckArrivalsAsync().GetAwaiter().GetResult();
            Pump(session, 3);
            Assert.False(Shows(session, "is in your library"), "the first look announced what was already there");

            requests.Arrive("movie:603", "matrix");
            app.CheckArrivalsAsync().GetAwaiter().GetResult();
            PumpUntilShown(session, "The Matrix is in your library");

            // And only once.
            Pump(session, 400);   // past the toast
            app.CheckArrivalsAsync().GetAwaiter().GetResult();
            Pump(session, 3);
            Assert.False(Shows(session, "The Matrix is in your library"));
        });
    }

    // ── The watchlist on Home ────────────────────────────────────────────

    [Fact]
    public void TheWatchlistHasARowOnHomeWhoseTitlesOpen()
    {
        var requests = Requests();
        requests.AddToWatchlist("movie:603");
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Your watchlist");
            Assert.Equal(ApertureView.Home, app.Current.View);

            Click(session, "discover-card-movie:603");
            Assert.Equal(ApertureView.DiscoverTitle, app.Current.View);
            Assert.Equal("movie:603", app.Current.Id);
        });
    }

    [Fact]
    public void AWatchlistTitleTheLibraryHasComesFirst()
    {
        var requests = Requests();
        requests.AddToWatchlist("movie:603");    // not in the library
        requests.AddToWatchlist("movie:1398");   // Stalker: arrived, playable now
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Your watchlist");
            Assert.Equal(["movie:1398", "movie:603"], app.HomeWatchlist.Select(i => i.Key));
        });
    }

    [Fact]
    public void AnEmptyWatchlistDrawsNoRow()
    {
        var requests = Requests();
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            Pump(session, 4);
            Assert.Contains("GetWatchlist", requests.Calls);
            Assert.False(Shows(session, "Your watchlist"));
        });
    }

    [Fact]
    public void TheWatchlistRowMovesAndHidesLikeAnyOther()
    {
        var requests = Requests();
        requests.AddToWatchlist("movie:603");
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Your watchlist");
            app.SetHomeRowHidden(ApertureApp.WatchlistRowKey, true);
            Pump(session);
            Assert.Null(session.Window.FindByAutomationId("discover-card-movie:603"));
            // Hidden, not forgotten: the footer offers it back.
            PumpUntilShown(session, "Show Your watchlist");
            Assert.Contains(ApertureApp.WatchlistRowKey, app.Settings.Data.HiddenHomeRows);
        });
    }

    [Fact]
    public void AddingToTheWatchlistPutsItOnHome()
    {
        var requests = Requests();
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.DiscoverTitle, "movie:603", "The Matrix"));
            PumpUntilShown(session, "Add to your watchlist");
            Click(session, "discover-watchlist");
            PumpUntilShown(session, "On your watchlist");

            Click(session, "rail-home");
            PumpUntilShown(session, "Your watchlist");
        });
    }
    // ── Issues ───────────────────────────────────────────────────────────

    [Fact]
    public void ReadersGetEveryonesIssuesAndReportersTheirOwn()
    {
        // Filing a report is not reading everyone's: a reporter's door is their own reports.
        Host(Library(), Requests(RequestPermission.Request | RequestPermission.CreateIssues), (session, app) =>
        {
            PumpUntilShown(session, "Your reports");
            Assert.Null(session.Window.FindByAutomationId("rail-issues"));
            Assert.NotNull(session.Window.FindByAutomationId("rail-your-reports"));
        });
        Host(Library(), Requests(RequestPermission.ViewIssues | RequestPermission.CreateIssues), (session, app) =>
        {
            PumpUntilShown(session, "Issues");
            Assert.NotNull(session.Window.FindByAutomationId("rail-issues"));
            Assert.Null(session.Window.FindByAutomationId("rail-your-reports"));
        });
        // Neither, for someone who may only ask for things.
        Host(Library(), Requests(), (session, app) =>
        {
            PumpUntilShown(session, "Requests");
            Assert.Null(session.Window.FindByAutomationId("rail-issues"));
            Assert.Null(session.Window.FindByAutomationId("rail-your-reports"));
        });
    }

    [Fact]
    public void AReporterSeesOnlyTheirOwnReportsResolvedOnesIncluded()
    {
        var requests = Requests(RequestPermission.Request | RequestPermission.CreateIssues);
        var blocky = requests.AddIssue("movie:603", IssueType.Video, "Blocky picture", byId: 1, by: "tester");
        requests.AddIssue("tv:1399", IssueType.Subtitles, "Wrong subtitles", 2, 3, IssueStatus.Resolved, byId: 1, by: "tester");
        requests.AddIssue("movie:1398", IssueType.Audio, "No sound");   // anna's
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Your reports");
            Click(session, "rail-your-reports");
            Assert.Equal(ApertureView.Issues, app.Current.View);
            PumpUntilShown(session, "The Matrix, picture problem, reported by tester, open");
            // All of them by default: a resolve is news to whoever reported it.
            PumpUntilShown(session, "Game of Thrones, subtitles problem, S2 E3, reported by tester, resolved");
            Assert.False(Shows(session, "Stalker"), "someone else's report was on a reporter's list");
            // The first look calls nothing new.
            Assert.False(Shows(session, ", new"), "the first look called a report new");
            // Their own is theirs to add to.
            Click(session, "issue-" + blocky.Id);
            PumpUntilShown(session, "Blocky picture");
            Assert.NotNull(session.Window.FindByAutomationId("issue-reply"));
        });
    }

    [Fact]
    public void AnAnswerToAReportIsNewsUntilItsReporterReadsIt()
    {
        var requests = Requests(RequestPermission.Request | RequestPermission.CreateIssues);
        var blocky = requests.AddIssue("movie:603", IssueType.Video, "Blocky picture", byId: 1, by: "tester");
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Your reports");
            Pump(session);
            Assert.False(Shows(session, "Your reports, 1 new"));

            requests.AddComment(blocky.Id, "Swapped the file — try it now");
            app.CheckMyIssues();
            PumpUntilShown(session, "Your reports, 1 new");
            PumpUntilShown(session, "Your report on The Matrix has an answer");

            Click(session, "rail-your-reports");
            PumpUntilShown(session, "The Matrix, picture problem, reported by tester, open, 1 reply, new");

            Click(session, "issue-" + blocky.Id);
            PumpUntilShown(session, "Swapped the file — try it now");
            Pump(session);
            Assert.False(Shows(session, "Your reports, 1 new"), "reading the answer left the badge up");

            // Looking again says nothing more.
            app.CheckMyIssues();
            Pump(session);
            Assert.False(Shows(session, "Your reports, 1 new"));
        });
    }

    [Fact]
    public void AReportersOwnReplyIsNotTheirNewsButAResolveIs()
    {
        var requests = Requests(RequestPermission.Request | RequestPermission.CreateIssues);
        var blocky = requests.AddIssue("movie:603", IssueType.Video, "Blocky picture", byId: 1, by: "tester");
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Your reports");
            app.Navigate(new Route(ApertureView.Issue, blocky.Id.ToString(), "The Matrix"));
            PumpUntilShown(session, "Blocky picture");
            session.Window.FindByAutomationId("issue-reply")!.Element.Focus();
            session.Window.TypeText("Only in the dark scenes");
            Pump(session, 2);
            Click(session, "issue-send");
            Assert.Contains($"CommentOnIssue({blocky.Id},Only in the dark scenes)", requests.Calls);
            PumpUntilShown(session, "Only in the dark scenes");

            app.CheckMyIssues();
            Pump(session);
            Assert.False(Shows(session, "Your reports, 1 new"), "the reporter's own reply counted as news");

            // Someone else resolving it is.
            app.GoBack();
            requests.User = new RequestUser(2, FakeRequestServer.OtherUser, RequestPermission.ManageIssues);
            requests.SetIssueStatusAsync(blocky.Id, IssueStatus.Resolved).GetAwaiter().GetResult();
            requests.User = new RequestUser(1, "tester", RequestPermission.Request | RequestPermission.CreateIssues);
            app.CheckMyIssues();
            PumpUntilShown(session, "Your report on The Matrix was resolved");
            PumpUntilShown(session, "Your reports, 1 new");
        });
    }

    [Fact]
    public void WhatAReporterHasReadIsKeptWithTheirRequestServerAccount()
    {
        var requests = Requests(RequestPermission.Request | RequestPermission.CreateIssues);
        var blocky = requests.AddIssue("movie:603", IssueType.Video, "Blocky picture", byId: 1, by: "tester");
        requests.AddComment(blocky.Id, "Looking into it");   // answered before the first look
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Your reports");
            Pump(session);
            var account = app.Settings.RequestServerFor("test-server-id")!;
            // The first look records what is there, answer included, without calling it new.
            var seen = Assert.Single(account.SeenIssues!);
            Assert.Equal((blocky.Id, 2, false), (seen.Id, seen.Comments, seen.Resolved));
            Assert.False(Shows(session, "Your reports, 1 new"));

            requests.AddComment(blocky.Id, "Fixed");
            app.CheckMyIssues();
            PumpUntilShown(session, "Your reports, 1 new");
            app.Navigate(new Route(ApertureView.Issue, blocky.Id.ToString(), "The Matrix"));
            PumpUntilShown(session, "Fixed");
            Pump(session);
            Assert.Equal(3, Assert.Single(account.SeenIssues!).Comments);
        }, setup: settings => settings.RememberRequestServer(new RequestServerAccountData
        {
            JellyfinServerId = "test-server-id",
            Address = "http://jellyseerr.test:5055",
            Auth = RequestServerAuth.Session,
            UserId = 1,
            UserName = "tester",
        }, "session-cookie"));
    }

    [Fact]
    public void NewsSaysWhatChanged()
    {
        var issue = new MediaIssue
        {
            Id = 4, Type = IssueType.Audio, Status = IssueStatus.Resolved, Kind = DiscoverKind.Movie, TmdbId = 603,
            Title = "The Matrix",
            Comments = [new IssueComment(1, "a", 1, "tester", default), new IssueComment(2, "b", 2, "anna", default)],
        };
        var filed = new SeenIssue { Id = 4, Comments = 1, Resolved = false };
        Assert.Equal("Your report on The Matrix was answered and resolved", ApertureApp.NewsPhrase(issue, filed));
        Assert.Equal("Your report on The Matrix was resolved",
            ApertureApp.NewsPhrase(issue, new SeenIssue { Id = 4, Comments = 2 }));
        Assert.Equal("Your report on The Matrix was reopened",
            ApertureApp.NewsPhrase(issue with { Status = IssueStatus.Open }, new SeenIssue { Id = 4, Comments = 2, Resolved = true }));
        Assert.Equal("Your report on The Matrix has an answer",
            ApertureApp.NewsPhrase(issue with { Status = IssueStatus.Open }, filed));
    }

    [Fact]
    public void AnIssueManagerSeesOpenReportsOnTheRailAndResolvesOneFromTheList()
    {
        var requests = Requests(RequestPermission.ManageIssues | RequestPermission.Request);
        var blocky = requests.AddIssue("movie:603", IssueType.Video, "Blocky picture");
        requests.AddIssue("tv:1399", IssueType.Subtitles, "Wrong subtitles", 2, 3, IssueStatus.Resolved);
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Issues, 1 open");

            Click(session, "rail-issues");
            Assert.Equal(ApertureView.Issues, app.Current.View);
            PumpUntilShown(session, "The Matrix, picture problem, reported by anna, open");
            // The list starts at the open ones.
            Assert.False(Shows(session, "Game of Thrones"), "a resolved report was on the open list");

            Click(session, $"issue-{blocky.Id}-resolve");
            Assert.Contains($"SetIssueStatus({blocky.Id},Resolved)", requests.Calls);
            // Resolving does not also open the report under the button.
            Assert.Equal(ApertureView.Issues, app.Current.View);
            PumpUntilShown(session, "No open reports");
            Assert.False(Shows(session, "1 open"), "the badge still counted the resolved report");

            Click(session, "issues-filter-resolved");
            PumpUntilShown(session, "Game of Thrones, subtitles problem, S2 E3, reported by anna, resolved");
            PumpUntilShown(session, "The Matrix, picture problem, reported by anna, resolved");
        });
    }

    [Fact]
    public void AReportOpensWithItsConversationAndAnAnswerIsSentAndResolved()
    {
        var requests = Requests(RequestPermission.ManageIssues);
        var subs = requests.AddIssue("tv:1399", IssueType.Subtitles, "Subtitles are for the wrong episode", 2, 3);
        requests.AddComment(subs.Id, "It's episode 4's", byId: 2);
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Issues");
            Click(session, "rail-issues");
            PumpUntilShown(session, "1 reply");
            Click(session, "issue-" + subs.Id);
            Assert.Equal(ApertureView.Issue, app.Current.View);
            PumpUntilShown(session, "Subtitles are for the wrong episode");
            PumpUntilShown(session, "It's episode 4's");

            session.Window.FindByAutomationId("issue-reply")!.Element.Focus();
            session.Window.TypeText("Replaced them");
            Pump(session, 2);
            Assert.Equal("Replaced them", session.Window.FindByAutomationId("issue-reply")!.Value);
            Click(session, "issue-resolve");
            // The answer first, then the status — and the button said both.
            var answer = requests.Calls.IndexOf($"CommentOnIssue({subs.Id},Replaced them)");
            var resolve = requests.Calls.IndexOf($"SetIssueStatus({subs.Id},Resolved)");
            Assert.True(answer >= 0 && resolve > answer, string.Join("\n", requests.Calls));
            PumpUntilShown(session, "tester, just now: Replaced them");
            Assert.Equal(IssueStatus.Resolved, requests.Issue(subs.Id)!.Status);
            // The box is empty again, and the page offers the way back.
            Assert.Equal("", session.Window.FindByAutomationId("issue-reply")!.Value ?? "");
            Assert.NotNull(session.Window.FindByAutomationId("issue-reopen"));
        });
    }

    [Fact]
    public void AReaderWhoCannotManageSeesTheConversationButCannotAnswer()
    {
        var requests = Requests(RequestPermission.ViewIssues);
        var blocky = requests.AddIssue("movie:603", IssueType.Video, "Blocky picture");
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Issues");
            Assert.False(Shows(session, "1 open"), "the rail counted reports this user cannot act on");
            app.Navigate(new Route(ApertureView.Issue, blocky.Id.ToString(), "The Matrix"));
            PumpUntilShown(session, "Blocky picture");
            Assert.NotNull(session.Window.FindByAutomationId("issue-read-only"));
            Assert.Null(session.Window.FindByAutomationId("issue-reply"));
            Assert.Null(session.Window.FindByAutomationId("issue-resolve"));
        });
    }

    [Fact]
    public void AReportThatWillNotLoadSaysSoRatherThanSpinning()
    {
        var requests = Requests(RequestPermission.ManageIssues);
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Issues");
            app.Navigate(new Route(ApertureView.Issue, "999", null));
            PumpUntilShown(session, "This report didn't load");
            // A missing report is not a lost session.
            Assert.NotNull(app.RequestServer);
        });
    }

    [Fact]
    public void AReportAboutATitleInTheLibraryOpensTheLibrarysPage()
    {
        var requests = Requests(RequestPermission.ManageIssues);
        var stalker = requests.AddIssue("movie:1398", IssueType.Audio, "No sound");
        Host(Library(), requests, (session, app) =>
        {
            PumpUntilShown(session, "Issues");
            app.Navigate(new Route(ApertureView.Issue, stalker.Id.ToString(), "Stalker"));
            PumpUntilShown(session, "No sound");
            Click(session, "issue-open-library");
            Assert.Equal(ApertureView.Detail, app.Current.View);
            Assert.Equal("0123456789abcdef0123456789abcdef", app.Current.Id);
        });
    }
}

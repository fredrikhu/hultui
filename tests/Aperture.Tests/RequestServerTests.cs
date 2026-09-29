using System.Net;
using System.Text;
using System.Text.Json;
using Aperture;
using Aperture.Core;
using Aperture.Core.Requests;
using HultUI.Core.Platform;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// The request-server client below the UI: what the wire says and what Aperture makes of
/// it, and what goes out on the wire — the session cookie, the API key's user, the Quick
/// Connect exchange. The payloads are shaped after the server's own entities (Seerr 3.5,
/// <c>server/entity/Media.ts</c> and friends), including the fields it sends that the
/// published spec leaves out, because those are exactly the ones Aperture reads.
/// </summary>
public class RequestServerTests
{
    // ── Mapping ──────────────────────────────────────────────────────────

    private const string SearchPage = """
    {
      "page": 1, "totalPages": 3, "totalResults": 47,
      "results": [
        { "id": 603, "mediaType": "movie", "title": "The Matrix", "originalTitle": "The Matrix",
          "releaseDate": "1999-03-30", "overview": "A hacker learns the truth.", "posterPath": "/f89U3ADr1oiB1s9GkdPOEpXUk5H.jpg",
          "backdropPath": "/l4QHerTSbMI7qgvasqxP36pqjN6.jpg", "voteAverage": 8.2, "voteCount": 25000, "adult": false, "video": false,
          "mediaInfo": { "id": 12, "tmdbId": 603, "mediaType": "movie", "status": 5, "status4k": 1,
                         "jellyfinMediaId": "1e2c7967a01c4918b1c0d5e6f7a8b9c0", "seasons": [], "downloadStatus": [],
                         "createdAt": "2026-01-02T10:00:00.000Z", "watchlists": null, "blocklist": {} } },
        { "id": 1399, "mediaType": "tv", "name": "Game of Thrones", "originalName": "Game of Thrones",
          "firstAirDate": "2011-04-17", "posterPath": "/1XS1oqL89opfnbLl8WnZY1O1uJx.jpg", "voteAverage": 8.4, "voteCount": 22000,
          "originCountry": ["US"] },
        { "id": 6384, "mediaType": "person", "name": "Keanu Reeves", "profilePath": "/abc.jpg", "knownFor": [] },
        { "id": 2344, "mediaType": "collection", "title": "The Matrix Collection", "posterPath": "/c.jpg" }
      ]
    }
    """;

    [Fact]
    public void ASearchPageMapsFilmsAndSeriesAndLeavesPeopleAndCollectionsOut()
    {
        var page = SeerrServer.MapPage(Parse(SearchPage, SeerrJson.Default.SeerrResultPageDto), null);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(3, page.TotalPages);
        Assert.True(page.HasMore);

        var film = page.Items[0];
        Assert.Equal(DiscoverKind.Movie, film.Kind);
        Assert.Equal("The Matrix", film.Title);
        Assert.Equal(1999, film.Year);
        Assert.Equal(Availability.Available, film.Availability);
        Assert.Equal("1e2c7967a01c4918b1c0d5e6f7a8b9c0", film.JellyfinItemId);
        Assert.True(film.InLibrary);
        Assert.Equal("movie:603", film.Key);

        var series = page.Items[1];
        Assert.Equal(DiscoverKind.Tv, series.Kind);
        Assert.Equal("Game of Thrones", series.Title);
        Assert.Equal(2011, series.Year);
        // No mediaInfo at all is how the server says "never heard of it".
        Assert.Equal(Availability.Unknown, series.Availability);
        Assert.False(series.InLibrary);
    }

    [Fact]
    public void StatusSixIsBlocklistedAndAnythingNewerReadsAsUnknown()
    {
        // The published spec says 6 is "deleted" — Overseerr's numbering. Jellyseerr and
        // Seerr put Blocklisted there and Deleted at 7.
        Assert.Equal(Availability.Blocklisted, SeerrServer.MapAvailability(6));
        Assert.Equal(Availability.Deleted, SeerrServer.MapAvailability(7));
        Assert.Equal(Availability.Unknown, SeerrServer.MapAvailability(9));
        Assert.Equal(Availability.Unknown, SeerrServer.MapAvailability(null));
    }

    [Fact]
    public void AJellyfinIdWithDashesIsStoredTheWayTheLibraryWritesIt()
    {
        Assert.Equal("1e2c7967a01c4918b1c0d5e6f7a8b9c0",
            SeerrServer.JellyfinId("1E2C7967-A01C-4918-B1C0-D5E6F7A8B9C0"));
        Assert.Null(SeerrServer.JellyfinId(""));
    }

    private const string TvDetail = """
    {
      "id": 1399, "name": "Game of Thrones", "firstAirDate": "2011-04-17", "tagline": "Winter is coming.",
      "overview": "Seven noble families fight.", "posterPath": "/p.jpg", "backdropPath": "/b.jpg",
      "episodeRunTime": [60], "voteAverage": 8.4, "status": "Ended",
      "genres": [{ "id": 18, "name": "Drama" }, { "id": 10765, "name": "Sci-Fi & Fantasy" }],
      "externalIds": { "tvdbId": 121361, "imdbId": "tt0944947" },
      "credits": { "cast": [ { "id": 22970, "name": "Peter Dinklage", "character": "Tyrion Lannister", "profilePath": "/pd.jpg" } ], "crew": [] },
      "seasons": [
        { "id": 3627, "seasonNumber": 0, "name": "Specials", "episodeCount": 14, "airDate": "2010-12-05" },
        { "id": 3624, "seasonNumber": 1, "name": "Season 1", "episodeCount": 10, "airDate": "2011-04-17" },
        { "id": 3625, "seasonNumber": 2, "name": "Season 2", "episodeCount": 10, "airDate": "2012-04-01" },
        { "id": 3626, "seasonNumber": 3, "name": "Season 3", "episodeCount": 10, "airDate": "2013-03-31" }
      ],
      "mediaInfo": {
        "id": 40, "tmdbId": 1399, "tvdbId": 121361, "mediaType": "tv", "status": 4, "status4k": 1,
        "jellyfinMediaId": "aaaabbbbccccddddeeeeffff00001111",
        "seasons": [ { "id": 1, "seasonNumber": 1, "status": 5, "status4k": 1 },
                     { "id": 2, "seasonNumber": 2, "status": 2, "status4k": 1 } ],
        "requests": [
          { "id": 77, "status": 1, "type": "tv", "is4k": false, "createdAt": "2026-09-28T08:00:00.000Z",
            "requestedBy": { "id": 4, "displayName": "anna", "permissions": 32 },
            "seasons": [ { "id": 9, "seasonNumber": 2, "status": 1 } ] }
        ]
      }
    }
    """;

    [Fact]
    public void ATvDetailCarriesEachSeasonsOwnStateAndLeavesSpecialsOut()
    {
        var detail = SeerrServer.MapTv(Parse(TvDetail, SeerrJson.Default.SeerrTvDetailsDto));

        Assert.Equal(Availability.PartiallyAvailable, detail.Item.Availability);
        Assert.Equal(121361, detail.TvdbId);
        Assert.Equal(TimeSpan.FromMinutes(60), detail.Runtime);
        Assert.Equal(["Drama", "Sci-Fi & Fantasy"], detail.Genres);

        Assert.Equal([1, 2, 3], detail.Seasons.Select(s => s.Number));
        Assert.Equal(Availability.Available, detail.Seasons[0].Availability);
        Assert.Equal(Availability.Pending, detail.Seasons[1].Availability);
        // A season the server keeps no row for is simply not asked for yet.
        Assert.Equal(Availability.Unknown, detail.Seasons[2].Availability);

        var request = Assert.Single(detail.Requests);
        Assert.Equal(RequestStatus.Pending, request.Status);
        Assert.Equal(DiscoverKind.Tv, request.Kind);
        Assert.Equal(1399, request.TmdbId);   // handed down: a detail's requests carry no media of their own
        Assert.Equal("anna", request.RequestedBy);
        Assert.Equal([2], request.Seasons);

        Assert.Equal("Peter Dinklage", Assert.Single(detail.Cast).Name);
    }

    private const string RequestPage = """
    {
      "pageInfo": { "pages": 3, "pageSize": 2, "results": 5, "page": 1 },
      "results": [
        { "id": 81, "status": 2, "type": "movie", "is4k": false, "createdAt": "2026-09-29T12:00:00.000Z",
          "media": { "id": 50, "tmdbId": 27205, "mediaType": "movie", "status": 5, "status4k": 1,
                     "jellyfinMediaId": "0123456789abcdef0123456789abcdef" },
          "requestedBy": { "id": 1, "displayName": "fredrik", "permissions": 2 }, "seasons": [] },
        { "id": 80, "status": 3, "type": "tv", "is4k": true, "createdAt": "2026-09-20T12:00:00.000Z",
          "media": { "id": 51, "tmdbId": 1396, "mediaType": "tv", "status": 1, "status4k": 1 },
          "requestedBy": { "id": 4, "username": "anna@example", "jellyfinUsername": "anna", "permissions": 32 },
          "seasons": [ { "seasonNumber": 2, "status": 3 }, { "seasonNumber": 1, "status": 3 } ] }
      ],
      "serviceErrors": { "radarr": [], "sonarr": [] }
    }
    """;

    [Fact]
    public void ARequestReadsTheTitlesStateNotJustItsOwn()
    {
        var page = Parse(RequestPage, SeerrJson.Default.SeerrRequestPageDto);
        var requests = page.Results!.Select(r => SeerrServer.MapRequest(r)).ToList();

        Assert.Equal(5, page.PageInfo!.Results);

        var film = requests[0];
        Assert.Equal(RequestStatus.Approved, film.Status);
        Assert.Equal(27205, film.TmdbId);
        Assert.Equal(Availability.Available, film.Availability);
        Assert.Equal("0123456789abcdef0123456789abcdef", film.JellyfinItemId);
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero), film.CreatedAt);

        var series = requests[1];
        Assert.Equal(RequestStatus.Declined, series.Status);
        Assert.True(series.Is4k);
        Assert.Equal([1, 2], series.Seasons);
        // No display name: the Jellyfin name is the next best thing, never the e-mail.
        Assert.Equal("anna", series.RequestedBy);
    }

    [Fact]
    public void AdminHoldsEveryPermissionAndMovieAndTvAreAskedSeparately()
    {
        var admin = new RequestUser(1, "owner", RequestPermission.Admin);
        Assert.True(admin.CanManageRequests);
        Assert.True(admin.CanRequest4k(DiscoverKind.Tv));

        var filmsOnly = new RequestUser(2, "kid", RequestPermission.RequestMovie);
        Assert.True(filmsOnly.CanRequest(DiscoverKind.Movie));
        Assert.False(filmsOnly.CanRequest(DiscoverKind.Tv));
        Assert.False(filmsOnly.CanManageRequests);

        var plain = new RequestUser(3, "anna", RequestPermission.Request | RequestPermission.AutoApproveTv);
        Assert.True(plain.CanRequest(DiscoverKind.Tv));
        Assert.True(plain.AutoApproves(DiscoverKind.Tv));
        Assert.False(plain.AutoApproves(DiscoverKind.Movie));
    }

    [Fact]
    public void EveryFilterIsOneTheServersValidatorAccepts()
    {
        // Anything else is a 400 — there is, for instance, no "declined" filter.
        string[] accepted = ["all", "approved", "available", "pending", "processing", "unavailable", "failed", "deleted", "completed"];
        foreach (var filter in Enum.GetValues<RequestFilter>())
            Assert.Contains(SeerrServer.FilterName(filter), accepted);
    }

    // ── Addresses and artwork ────────────────────────────────────────────

    [Fact]
    public void ABareHostIsTriedOnItsOwnPortAndOn5055()
    {
        Assert.Equal(
            ["http://jellyseerr.home", "http://jellyseerr.home:5055", "https://jellyseerr.home", "https://jellyseerr.home:5055"],
            SeerrConnection.Candidates("jellyseerr.home"));
        Assert.Equal(["http://seerr.home:5055"], SeerrConnection.Candidates("http://seerr.home:5055/api/v1/"));
        Assert.Empty(SeerrConnection.Candidates("ftp://seerr.home"));
        Assert.Empty(SeerrConnection.Candidates("   "));
    }

    [Fact]
    public void ArtworkIsOnlyEverATmdbPathJoinedOntoAKnownBase()
    {
        Assert.Equal("https://image.tmdb.org/t/p/w342/abc.jpg", RequestImages.Poster("/abc.jpg", 300)!.ToString());
        Assert.Equal("http://seerr.home:5055/imageproxy/tmdb/t/p/w780/b.jpg",
            RequestImages.Backdrop("/b.jpg", 700, "http://seerr.home:5055/imageproxy/tmdb/t/p/")!.ToString());
        Assert.Null(RequestImages.Poster(null, 300));
        Assert.Null(RequestImages.Poster("https://evil.example/x.jpg", 300));
        Assert.Null(RequestImages.Poster("/../../etc/passwd", 300));
        Assert.Null(RequestImages.Poster("/a.jpg?x=1", 300));
    }

    [Fact]
    public void TheServersOwnImageCacheIsUsedOnlyWhenItKeepsOne()
    {
        var direct = new RequestServerInfo("http://seerr.home:5055", "Seerr", "3.5.0");
        var cached = direct with { CacheImages = true };
        Assert.Equal(RequestImages.TmdbImageBase, direct.ImageBase);
        Assert.Equal("http://seerr.home:5055/imageproxy/tmdb/t/p/", cached.ImageBase);
    }

    [Fact]
    public void QuickConnectSignInNeedsSeerr34AndAJellyfinBackedServer()
    {
        Assert.True(new RequestServerInfo("http://s", "Seerr", "3.4.0").SupportsQuickConnect);
        Assert.True(new RequestServerInfo("http://s", "Seerr", "v3.5.0").SupportsQuickConnect);
        Assert.False(new RequestServerInfo("http://s", "Jellyseerr", "2.7.3").SupportsQuickConnect);
        Assert.False(new RequestServerInfo("http://s", "Seerr", "3.5.0", JellyfinBacked: false).SupportsQuickConnect);
        Assert.False(new RequestServerInfo("http://s", "Seerr", "develop").SupportsQuickConnect);
    }

    [Fact]
    public void ThePosterCacheSendsTheJellyfinTokenToJellyfinAndNowhereElse()
    {
        using var cache = new PosterCache(() => { }, http: new HttpClient(new Router()));
        cache.Authorize("http://jellyfin.home:8096", "MediaBrowser Token=\"secret\"");

        Assert.True(cache.IsAuthorizedFor(new Uri("http://jellyfin.home:8096/Videos/x/Trickplay/320/0.jpg")));
        Assert.False(cache.IsAuthorizedFor(new Uri("https://image.tmdb.org/t/p/w342/abc.jpg")));
        Assert.False(cache.IsAuthorizedFor(new Uri("http://jellyfin.home:5055/imageproxy/tmdb/t/p/w342/abc.jpg")));
        Assert.False(cache.IsAuthorizedFor(new Uri("https://jellyfin.home:8096/x")));

        cache.Authorize(null, null);
        Assert.False(cache.IsAuthorizedFor(new Uri("http://jellyfin.home:8096/x")));
    }

    // ── What goes out on the wire ────────────────────────────────────────

    private static readonly RequestServerInfo Server = new("http://seerr.test:5055", "Seerr", "3.5.0");

    [Fact]
    public async Task APasswordSignInKeepsTheSessionCookieAndSendsItBack()
    {
        var router = new Router()
            .On("POST", "/api/v1/auth/jellyfin", req =>
            {
                var body = JsonDocument.Parse(req.Body!).RootElement;
                Assert.Equal("fredrik", body.GetProperty("username").GetString());
                Assert.Equal("hunter2", body.GetProperty("password").GetString());
                // The Jellyfin address is the server's own setting: sending one is a 500.
                Assert.False(body.TryGetProperty("hostname", out _));
                return Json("""{ "id": 3, "displayName": "fredrik", "permissions": 32 }""",
                    cookie: "connect.sid=s%3AAbC.def; Path=/; Expires=Thu, 30 Oct 2026 00:00:00 GMT; HttpOnly; SameSite=Lax");
            })
            .On("GET", "/api/v1/discover/trending", _ => Json("""{ "page": 1, "totalPages": 1, "totalResults": 0, "results": [] }"""));

        var signIn = await SeerrConnection.SignInAsync(Server, "jf-1", "fredrik", "hunter2", handler: router);
        Assert.True(signIn.Ok, signIn.Failure?.Message);
        using var server = signIn.Value;

        Assert.Equal("connect.sid=s%3AAbC.def", server.Secret);
        Assert.Equal(RequestServerAuth.Session, server.Account.Auth);
        Assert.Equal(3, server.Account.UserId);
        Assert.Equal("jf-1", server.Account.JellyfinServerId);

        var shelf = await server.DiscoverAsync(DiscoverShelf.Trending);
        Assert.True(shelf.Ok, shelf.Failure?.Message);
        Assert.Equal("connect.sid=s%3AAbC.def", router.Last("/api/v1/discover/trending").Headers["Cookie"]);
    }

    [Fact]
    public async Task ARefusedPasswordReadsAsBadCredentialsNotAnExpiredSession()
    {
        var router = new Router().On("POST", "/api/v1/auth/jellyfin",
            _ => Json("""{ "message": "INVALID_CREDENTIALS" }""", HttpStatusCode.Unauthorized));
        var signIn = await SeerrConnection.SignInAsync(Server, "jf-1", "fredrik", "nope", handler: router);
        Assert.False(signIn.Ok);
        Assert.Equal(ApertureFailureKind.BadCredentials, signIn.Failure!.Kind);
    }

    [Fact]
    public async Task AnApiKeyActsAsTheJellyfinUserWhenTheServerKnowsThem()
    {
        var jellyfinUser = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var router = new Router()
            .On("GET", "/api/v1/auth/me", req => req.Headers.TryGetValue("X-API-User", out var who) && who == "7"
                ? Json("""{ "id": 7, "displayName": "anna", "permissions": 32 }""")
                : Json("""{ "id": 1, "displayName": "owner", "permissions": 2 }"""))
            .On("GET", "/api/v1/user/jellyfin/11111111222233334444555555555555",
                _ => Json("""{ "id": 7, "displayName": "anna", "jellyfinUserId": "11111111222233334444555555555555", "permissions": 32 }"""))
            .On("GET", "/api/v1/request/count", _ => Json("""{ "total": 3, "pending": 2 }"""));

        var signIn = await SeerrConnection.SignInWithApiKeyAsync(Server, "jf-1", "the-key", jellyfinUser, handler: router);
        Assert.True(signIn.Ok, signIn.Failure?.Message);
        using var server = signIn.Value;

        Assert.Equal("anna", server.User.DisplayName);
        Assert.False(server.User.CanManageRequests);   // anna's permissions, not the key's
        Assert.Equal(7, server.Account.UserId);
        Assert.Equal(RequestServerAuth.ApiKey, server.Account.Auth);

        await server.GetPendingCountAsync();
        var sent = router.Last("/api/v1/request/count");
        Assert.Equal("the-key", sent.Headers["X-Api-Key"]);
        Assert.Equal("7", sent.Headers["X-API-User"]);
    }

    [Fact]
    public async Task AnApiKeyForAnUnknownJellyfinUserActsAsTheAdministrator()
    {
        var router = new Router()
            .On("GET", "/api/v1/auth/me", _ => Json("""{ "id": 1, "displayName": "owner", "permissions": 2 }"""))
            .On("GET", "/api/v1/user/jellyfin/", _ => Json("""{ "message": "User not found." }""", HttpStatusCode.NotFound))
            .On("GET", "/api/v1/user", _ => Json("""{ "pageInfo": { "results": 1 }, "results": [ { "id": 1, "displayName": "owner", "permissions": 2 } ] }"""));

        var signIn = await SeerrConnection.SignInWithApiKeyAsync(Server, "jf-1", "the-key", Guid.NewGuid(), handler: router);
        Assert.True(signIn.Ok, signIn.Failure?.Message);
        using var server = signIn.Value;
        Assert.Equal("owner", server.User.DisplayName);
        Assert.True(server.User.CanManageRequests);
    }

    [Fact]
    public async Task QuickConnectSignInApprovesTheCodeOnJellyfinAndKeepsTheSession()
    {
        string? approvedCode = null;
        var router = new Router()
            .On("POST", "/api/v1/auth/jellyfin/quickconnect/initiate",
                _ => Json("""{ "code": "482913", "secret": "abcdef0123456789abcdef0123456789" }"""))
            .On("POST", "/api/v1/auth/jellyfin/quickconnect/authenticate", req =>
            {
                Assert.Equal("482913", approvedCode);   // approved before it was traded
                var body = JsonDocument.Parse(req.Body!).RootElement;
                Assert.Equal("abcdef0123456789abcdef0123456789", body.GetProperty("secret").GetString());
                return Json("""{ "id": 3, "displayName": "fredrik", "permissions": 32 }""", cookie: "connect.sid=s%3Aqc; Path=/");
            });

        var signIn = await SeerrConnection.SignInWithJellyfinAsync(Server, "jf-1", (code, _) =>
        {
            approvedCode = code;
            return Task.FromResult(ApertureResult<bool>.Success(true));
        }, handler: router);

        Assert.True(signIn.Ok, signIn.Failure?.Message);
        using var server = signIn.Value;
        Assert.Equal("connect.sid=s%3Aqc", server.Secret);
        Assert.Equal(RequestServerAuth.Session, server.Account.Auth);
    }

    [Fact]
    public async Task QuickConnectSwitchedOffInJellyfinSaysSoAndAsksForThePassword()
    {
        var router = new Router().On("POST", "/api/v1/auth/jellyfin/quickconnect/initiate",
            _ => Json("""{ "message": "Failed to initiate quick connect." }""", HttpStatusCode.InternalServerError));
        var signIn = await SeerrConnection.SignInWithJellyfinAsync(Server, "jf-1",
            (_, _) => throw new InvalidOperationException("nothing should be approved"), handler: router);
        Assert.False(signIn.Ok);
        Assert.Equal(ApertureFailureKind.Forbidden, signIn.Failure!.Kind);
        Assert.Contains("password", signIn.Failure.Message);
    }

    [Fact]
    public async Task ARequestSendsTheSeasonsTickedAndA202IsTheServersSentence()
    {
        var router = new Router()
            .On("POST", "/api/v1/request", req =>
            {
                var body = JsonDocument.Parse(req.Body!).RootElement;
                Assert.Equal("tv", body.GetProperty("mediaType").GetString());
                Assert.Equal(1399, body.GetProperty("mediaId").GetInt32());
                Assert.Equal(121361, body.GetProperty("tvdbId").GetInt32());
                Assert.Equal([2, 3], body.GetProperty("seasons").EnumerateArray().Select(e => e.GetInt32()));
                return Json("""{ "message": "No seasons available to request" }""", HttpStatusCode.Accepted);
            });
        using var server = await SessionServer(router);

        var made = await server.RequestAsync(new NewRequest(DiscoverKind.Tv, 1399, [2, 3], TvdbId: 121361));
        Assert.False(made.Ok);
        Assert.Equal(ApertureFailureKind.Invalid, made.Failure!.Kind);
        Assert.Equal("No seasons available to request", made.Failure.UserMessage("seerr.test"));
    }

    [Fact]
    public async Task ARefusalCarriesTheServersOwnSentence()
    {
        var router = new Router().On("POST", "/api/v1/request",
            _ => Json("""{ "status": 403, "error": "You do not have permission to make 4K requests." }""", HttpStatusCode.Forbidden));
        using var server = await SessionServer(router);

        var made = await server.RequestAsync(new NewRequest(DiscoverKind.Movie, 603, [], Is4k: true));
        Assert.Equal(ApertureFailureKind.Forbidden, made.Failure!.Kind);
        Assert.Equal("You do not have permission to make 4K requests.", made.Failure.Message);
    }

    [Fact]
    public async Task TheRequestsListFillsInTitlesAndAsksForMineByUser()
    {
        var router = new Router()
            .On("GET", "/api/v1/request", _ => Json(RequestPage))
            .On("GET", "/api/v1/movie/27205", _ => Json("""{ "id": 27205, "title": "Inception", "releaseDate": "2010-07-15", "posterPath": "/i.jpg" }"""))
            .On("GET", "/api/v1/tv/1396", _ => Json("""{ "id": 1396, "name": "Breaking Bad", "firstAirDate": "2008-01-20" }"""));
        using var server = await SessionServer(router);

        var page = await server.GetRequestsAsync(RequestFilter.Pending, mine: true, skip: 0, take: 2);
        Assert.True(page.Ok, page.Failure?.Message);
        Assert.Equal(5, page.Value.Total);
        Assert.True(page.Value.HasMore);
        Assert.Equal(["Inception", "Breaking Bad"], page.Value.Items.Select(r => r.Title));
        Assert.Equal("/i.jpg", page.Value.Items[0].PosterPath);

        var query = router.Last("/api/v1/request").Query;
        Assert.Contains("filter=pending", query);
        Assert.Contains("requestedBy=3", query);
        Assert.Contains("take=2", query);
    }

    [Fact]
    public async Task ASearchEscapesWhatTheValidatorWouldRefuse()
    {
        var router = new Router().On("GET", "/api/v1/search", _ => Json("""{ "page": 1, "totalPages": 0, "totalResults": 0, "results": [] }"""));
        using var server = await SessionServer(router);
        await server.SearchAsync("Ocean's Eleven!");
        Assert.Contains("query=Ocean%27s%20Eleven%21", router.Last("/api/v1/search").Query);
    }

    [Fact]
    public async Task AnHtmlPageOnThePortIsNotARequestServer()
    {
        var router = new Router().On("GET", "/api/v1/status", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>Router login</body></html>", Encoding.UTF8, "text/html"),
        });
        var probe = await SeerrConnection.ProbeAsync("http://seerr.test:5055", handler: router);
        Assert.False(probe.Ok);
        Assert.Equal(ApertureFailureKind.NotJellyfin, probe.Failure!.Kind);
    }

    [Fact]
    public async Task AProbeReadsTheNameAndTheSwitchesTheFormDependsOn()
    {
        var router = new Router()
            .On("GET", "/api/v1/status", _ => Json("""{ "version": "3.5.0", "commitTag": "v3.5.0", "restartRequired": false }"""))
            .On("GET", "/api/v1/settings/public", _ => Json("""
                { "initialized": true, "applicationTitle": "Requests", "mediaServerType": 2, "movie4kEnabled": true,
                  "series4kEnabled": false, "partialRequestsEnabled": false, "cacheImages": true, "localLogin": true }
                """));
        var probe = await SeerrConnection.ProbeAsync("http://seerr.test:5055", handler: router);
        Assert.True(probe.Ok, probe.Failure?.Message);
        Assert.Equal("Requests", probe.Value.Title);
        Assert.True(probe.Value.Movie4kEnabled);
        Assert.False(probe.Value.PartialRequestsEnabled);
        Assert.True(probe.Value.CacheImages);
        Assert.True(probe.Value.SupportsQuickConnect);
    }

    // ── Settings ─────────────────────────────────────────────────────────

    [Fact]
    public void TheRequestServerIsRememberedBesideItsJellyfinServerWithTheSecretInTheStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        var store = new InMemoryCredentialStore();
        try
        {
            var settings = new ApertureSettings(store, path);
            settings.Remember(new ServerInfo("http://jellyfin.home:8096", "Home", "12.1.0", "jf-1"),
                new SignedInUser(Guid.NewGuid(), "fredrik", "jf-token", null));
            Assert.True(settings.RememberRequestServer(new RequestServerAccountData
            {
                JellyfinServerId = "jf-1", Address = "http://seerr.home:5055", Auth = RequestServerAuth.Session,
                UserId = 3, UserName = "fredrik",
            }, "connect.sid=s%3Asecret"));

            var text = File.ReadAllText(path);
            Assert.DoesNotContain("secret", text);   // the cookie is in the store, never the file

            var reread = new ApertureSettings(store, path);
            Assert.Equal("http://seerr.home:5055", reread.RequestServerFor("jf-1")!.Address);
            Assert.Equal("connect.sid=s%3Asecret", reread.RequestSecretFor("jf-1"));

            // Forgetting the Jellyfin server forgets what was paired with it.
            reread.Forget("jf-1");
            Assert.Null(reread.RequestServerFor("jf-1"));
            Assert.Null(reread.RequestSecretFor("jf-1"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    // ── Words ────────────────────────────────────────────────────────────

    [Fact]
    public void SeasonsReadAsARunWhenTheyAreOne()
    {
        Assert.Equal("Season 2", ApertureApp.SeasonsPhrase([2]));
        Assert.Equal("Seasons 1–3", ApertureApp.SeasonsPhrase([3, 1, 2]));
        Assert.Equal("Seasons 1, 3, 4", ApertureApp.SeasonsPhrase([1, 3, 4]));
    }

    [Fact]
    public void WhenARequestWasMadeReadsInDays()
    {
        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("just now", Ui.Ago(now.AddSeconds(-20), now));
        Assert.Equal("5 min ago", Ui.Ago(now.AddMinutes(-5), now));
        Assert.Equal("3 h ago", Ui.Ago(now.AddHours(-3), now));
        Assert.Equal("yesterday", Ui.Ago(now.AddHours(-30), now));
        Assert.Equal("4 days ago", Ui.Ago(now.AddDays(-4), now));
    }

    // ── Plumbing ─────────────────────────────────────────────────────────

    private static T Parse<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) =>
        JsonSerializer.Deserialize(json, type)!;

    private static async Task<SeerrServer> SessionServer(Router router)
    {
        router.On("POST", "/api/v1/auth/jellyfin",
            _ => Json("""{ "id": 3, "displayName": "fredrik", "permissions": 32 }""", cookie: "connect.sid=s%3Ax; Path=/"));
        var signIn = await SeerrConnection.SignInAsync(Server, "jf-1", "fredrik", "pw", handler: router);
        Assert.True(signIn.Ok, signIn.Failure?.Message);
        return signIn.Value;
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK, string? cookie = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (cookie is not null) response.Headers.TryAddWithoutValidation("Set-Cookie", cookie);
        return response;
    }

    /// <summary>What a request looked like when it left: path, query, the headers that
    /// matter, the body.</summary>
    internal sealed record Sent(string Method, string Path, string Query, Dictionary<string, string> Headers, string? Body);

    /// <summary>A server in a handler: routes by method and path (the longest matching
    /// prefix wins, so <c>/user/jellyfin/</c> can sit beside <c>/user</c>), records every
    /// request, and answers 404 for anything unrouted.</summary>
    internal sealed class Router : HttpMessageHandler
    {
        private readonly List<(string Method, string Path, Func<Sent, HttpResponseMessage> Answer)> _routes = [];
        public List<Sent> Requests { get; } = [];

        public Router On(string method, string path, Func<Sent, HttpResponseMessage> answer)
        {
            _routes.Add((method, path, answer));
            return this;
        }

        public Sent Last(string path) => Requests.Last(r => r.Path == path);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in request.Headers) headers[h.Key] = string.Join(",", h.Value);
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            var uri = request.RequestUri!;
            var sent = new Sent(request.Method.Method, uri.AbsolutePath, uri.Query, headers, body);
            lock (Requests) Requests.Add(sent);

            var route = _routes
                .Where(r => r.Method == sent.Method
                            && (sent.Path == r.Path || (r.Path.EndsWith('/') && sent.Path.StartsWith(r.Path, StringComparison.Ordinal))))
                .OrderByDescending(r => r.Path.Length)
                .Select(r => r.Answer)
                .FirstOrDefault();
            return route?.Invoke(sent) ?? new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{ "message": "Not found" }""", Encoding.UTF8, "application/json"),
            };
        }
    }
}

using Aperture.Core;
using Aperture.Core.Requests;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// A fact that runs only against a real Jellyseerr or Seerr, and skips everywhere else.
/// Armed by <c>APERTURE_SEERR_SERVER</c> (e.g. <c>http://seerr.home:5055</c>) and
/// <c>APERTURE_SEERR_API_KEY</c>, or <c>APERTURE_SEERR_API_KEY_FILE</c> pointing at a file
/// holding the key from its Settings → General. The file form keeps the key out of shell
/// history and process listings.
/// </summary>
public sealed class LiveRequestServerFactAttribute : FactAttribute
{
    public LiveRequestServerFactAttribute()
    {
        if (LiveRequestServer.Address is null)
            Skip = "No live request server: set APERTURE_SEERR_SERVER (and APERTURE_SEERR_API_KEY or APERTURE_SEERR_API_KEY_FILE).";
        else if (LiveRequestServer.ApiKey is null)
            Skip = "No request-server key: set APERTURE_SEERR_API_KEY, or APERTURE_SEERR_API_KEY_FILE pointing at a file holding one.";
    }
}

public static class LiveRequestServer
{
    public static string? Address => Value("APERTURE_SEERR_SERVER");

    public static string? ApiKey
    {
        get
        {
            if (Value("APERTURE_SEERR_API_KEY") is { } direct) return direct;
            if (Value("APERTURE_SEERR_API_KEY_FILE") is { } path && File.Exists(path))
            {
                var text = File.ReadAllText(path).Trim();
                return text.Length > 0 ? text : null;
            }
            return null;
        }
    }

    /// <summary>Connects with the key — as the Jellyfin user of the live Jellyfin tests when
    /// those are armed too, which is also what proves the user lookup; as the administrator
    /// otherwise.</summary>
    public static async Task<SeerrServer> ConnectAsync(IMediaLibrary? jellyfin, CancellationToken ct)
    {
        var found = await SeerrConnection.DiscoverAsync(Address!, ct);
        Assert.True(found.Ok, found.Failure?.Message);
        var signedIn = await SeerrConnection.SignInWithApiKeyAsync(found.Value, jellyfin?.Server.Id ?? "live-tests",
            ApiKey!, jellyfin?.User.Id, ct);
        Assert.True(signedIn.Ok, signedIn.Failure?.Message);
        return signedIn.Value;
    }

    private static string? Value(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}

/// <summary>
/// The request-server client against a real one. <b>Read-only</b>: nothing here requests,
/// approves or deletes anything — a test that did would fill a household's request list and
/// start real downloads. What these prove is the part a fixture cannot: that the wire says
/// what <see cref="SeerrServer"/> expects it to, on this server's version.
/// </summary>
public class LiveRequestServerTests
{
    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;

    [LiveRequestServerFact]
    public async Task TheProbeNamesTheServerAndItsVersion()
    {
        var found = await SeerrConnection.DiscoverAsync(new Uri(LiveRequestServer.Address!).Host, Ct);
        Assert.True(found.Ok, found.Failure?.Message);
        Assert.False(string.IsNullOrWhiteSpace(found.Value.Title));
        Assert.NotEqual(new Version(0, 0), found.Value.ParsedVersion);
    }

    [LiveRequestServerFact]
    public async Task EveryShelfReadsAndTrendingHasSomething()
    {
        using var server = await LiveRequestServer.ConnectAsync(null, Ct);
        foreach (var shelf in Enum.GetValues<DiscoverShelf>())
        {
            var page = await server.DiscoverAsync(shelf, ct: Ct);
            Assert.True(page.Ok, $"{shelf}: {page.Failure?.Message}");
        }
        var trending = await server.DiscoverAsync(DiscoverShelf.Trending, ct: Ct);
        Assert.NotEmpty(trending.Value.Items);
        Assert.All(trending.Value.Items, i => Assert.True(i.TmdbId > 0 && i.Title.Length > 0));
    }

    [LiveRequestServerFact]
    public async Task ASearchFindsAFilmEveryoneHasHeardOf()
    {
        using var server = await LiveRequestServer.ConnectAsync(null, Ct);
        var page = await server.SearchAsync("The Matrix", ct: Ct);
        Assert.True(page.Ok, page.Failure?.Message);
        Assert.Contains(page.Value.Items, i => i.Kind == DiscoverKind.Movie && i.TmdbId == 603);
    }

    [LiveRequestServerFact]
    public async Task ASeriesDetailCarriesItsSeasonsAndTvdbId()
    {
        using var server = await LiveRequestServer.ConnectAsync(null, Ct);
        var detail = await server.GetDetailAsync(DiscoverKind.Tv, 1399, Ct);   // Game of Thrones
        Assert.True(detail.Ok, detail.Failure?.Message);
        Assert.Equal(8, detail.Value.Seasons.Count);
        Assert.DoesNotContain(detail.Value.Seasons, s => s.Number == 0);
        Assert.NotNull(detail.Value.TvdbId);
    }

    [LiveRequestServerFact]
    public async Task TheRequestListAndItsCountsRead()
    {
        using var server = await LiveRequestServer.ConnectAsync(null, Ct);
        var page = await server.GetRequestsAsync(RequestFilter.All, mine: false, 0, 10, Ct);
        Assert.True(page.Ok, page.Failure?.Message);
        Assert.All(page.Value.Items, r => Assert.True(r.TmdbId > 0));
        var pending = await server.GetPendingCountAsync(Ct);
        Assert.True(pending.Ok, pending.Failure?.Message);
        var quota = await server.GetQuotaAsync(Ct);
        Assert.True(quota.Ok, quota.Failure?.Message);
    }

    /// <summary>
    /// The join this whole feature turns on: a title the request server says is available
    /// carries an id the <i>Jellyfin</i> server resolves. Needs both live servers armed.
    /// </summary>
    [LiveRequestServerFact]
    public async Task AnAvailableTitlesJellyfinIdOpensInTheLibrary()
    {
        if (LiveServer.Address is null || LiveServer.ApiKey is null) return;   // Jellyfin side not armed
        using var library = await LiveServer.ConnectAsync(Ct);
        using var server = await LiveRequestServer.ConnectAsync(library, Ct);

        var requests = await server.GetRequestsAsync(RequestFilter.Available, mine: false, 0, 20, Ct);
        Assert.True(requests.Ok, requests.Failure?.Message);
        var withId = requests.Value.Items.FirstOrDefault(r => r.JellyfinItemId is not null);
        if (withId is null) return;   // nothing requested has arrived yet — nothing to join

        var item = await library.GetItemAsync(withId.JellyfinItemId!, Ct);
        Assert.True(item.Ok, $"{withId.Title}: Jellyfin has no item {withId.JellyfinItemId} ({item.Failure?.Message})");
    }

    [LiveRequestServerFact]
    public async Task TheLayoutGenresAPersonAndTheWatchlistRead()
    {
        using var server = await LiveRequestServer.ConnectAsync(null, Ct);

        var layout = await server.GetDiscoverLayoutAsync(Ct);
        Assert.True(layout.Ok, layout.Failure?.Message);
        Assert.NotEmpty(layout.Value);
        // Every row Aperture draws fills without an error.
        foreach (var slider in layout.Value.Where(s => s.Enabled && s.Kind is not (SliderKind.RecentlyAdded
                     or SliderKind.RecentRequests or SliderKind.Watchlist or SliderKind.MovieGenres or SliderKind.TvGenres
                     or SliderKind.Studios or SliderKind.Networks or SliderKind.MovieStreaming or SliderKind.TvStreaming)))
        {
            var row = await server.GetSliderAsync(slider, ct: Ct);
            Assert.True(row.Ok, $"{slider.Kind}: {row.Failure?.Message}");
        }

        var genres = await server.GetGenresAsync(DiscoverKind.Movie, Ct);
        Assert.True(genres.Ok, genres.Failure?.Message);
        Assert.Contains(genres.Value, g => g.Name == "Drama");
        var drama = await server.DiscoverGenreAsync(DiscoverKind.Movie, genres.Value.First(g => g.Name == "Drama").Id, ct: Ct);
        Assert.NotEmpty(drama.Value.Items);

        var keanu = await server.GetPersonAsync(6384, Ct);
        Assert.True(keanu.Ok, keanu.Failure?.Message);
        Assert.Contains(keanu.Value.Credits, c => c.TmdbId == 603);

        var watchlist = await server.GetWatchlistAsync(ct: Ct);
        Assert.True(watchlist.Ok, watchlist.Failure?.Message);
    }

    [LiveRequestServerFact]
    public async Task TheDownloadServersAndTheirChoicesRead()
    {
        using var server = await LiveRequestServer.ConnectAsync(null, Ct);
        var radarr = await server.GetServicesAsync(DiscoverKind.Movie, Ct);
        Assert.True(radarr.Ok, radarr.Failure?.Message);
        if (radarr.Value.Count == 0) return;   // no Radarr set up — nothing to read
        var details = await server.GetServiceDetailsAsync(DiscoverKind.Movie, radarr.Value[0].Id, Ct);
        Assert.True(details.Ok, details.Failure?.Message);
        Assert.NotEmpty(details.Value.Profiles);
        Assert.NotEmpty(details.Value.RootFolders);
    }
    /// <summary>A streaming-service row the way the server stores one (Netflix, in the US):
    /// titles come back, and fewer than without the filter — which proves it was applied.
    /// Read only: the row is made up here, never saved to the server's layout.</summary>
    [LiveRequestServerFact]
    public async Task AStreamingServiceRowNarrowsTheShelf()
    {
        using var server = await LiveRequestServer.ConnectAsync(null, Ct);
        var netflix = await server.GetSliderAsync(new DiscoverSlider(0, SliderKind.MovieStreaming, "Netflix", "US,8", true, 0), ct: Ct);
        Assert.True(netflix.Ok, netflix.Failure?.Message);
        Assert.NotEmpty(netflix.Value.Items);
        var everything = await server.GetSliderAsync(new DiscoverSlider(0, SliderKind.PopularMovies, null, null, true, 0), ct: Ct);
        Assert.True(everything.Ok, everything.Failure?.Message);
        Assert.InRange(netflix.Value.TotalResults, 1, everything.Value.TotalResults - 1);

        var either = await server.GetSliderAsync(new DiscoverSlider(0, SliderKind.MovieStreaming, "Netflix or Disney+", "US,8|337", true, 0), ct: Ct);
        Assert.True(either.Value.TotalResults > netflix.Value.TotalResults, "the services were not read as any-of");
    }

    /// <summary>The studio and network lists are compiled in, so this is what notices one
    /// going stale: every id still has titles behind it (the first of each list, which
    /// is enough to prove the routes), and every logo still loads through the server's
    /// image path.</summary>
    [LiveRequestServerFact]
    public async Task TheCompiledStudiosAndNetworksStillResolve()
    {
        using var server = await LiveRequestServer.ConnectAsync(null, Ct);
        var disney = await server.DiscoverCompanyAsync(DiscoverKind.Movie, DiscoverCompanies.Studios[0].Id, ct: Ct);
        Assert.True(disney.Ok, disney.Failure?.Message);
        Assert.NotEmpty(disney.Value.Items);
        var netflix = await server.DiscoverCompanyAsync(DiscoverKind.Tv, DiscoverCompanies.Networks[0].Id, ct: Ct);
        Assert.True(netflix.Ok, netflix.Failure?.Message);
        Assert.NotEmpty(netflix.Value.Items);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var missing = new List<string>();
        foreach (var company in DiscoverCompanies.Studios.Concat(DiscoverCompanies.Networks))
        {
            using var response = await http.GetAsync(RequestImages.Logo(company.LogoPath, 224, server.Server.ImageBase), Ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "image/png")
                missing.Add($"{company.Name}: {(int)response.StatusCode}");
        }
        Assert.Empty(missing);
    }

    /// <summary>Read only: listing and counting; answering or resolving would change the
    /// household's real reports.</summary>
    [LiveRequestServerFact]
    public async Task TheIssueListAndItsCountsRead()
    {
        using var server = await LiveRequestServer.ConnectAsync(null, Ct);
        var counts = await server.GetIssueCountsAsync(Ct);
        Assert.True(counts.Ok, counts.Failure?.Message);
        foreach (var filter in new[] { IssueFilter.Open, IssueFilter.Resolved, IssueFilter.All })
        {
            var page = await server.GetIssuesAsync(filter, 0, 5, Ct);
            Assert.True(page.Ok, $"{filter}: {page.Failure?.Message}");
            if (filter == IssueFilter.All) Assert.Equal(counts.Value.Open + counts.Value.Resolved, page.Value.Total);
            if (page.Value.Items.FirstOrDefault() is { } first)
            {
                var one = await server.GetIssueAsync(first.Id, Ct);
                Assert.True(one.Ok, one.Failure?.Message);
                Assert.NotEmpty(one.Value.Comments);
            }
        }
    }
}

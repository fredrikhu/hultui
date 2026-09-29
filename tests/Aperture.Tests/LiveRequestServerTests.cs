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
}

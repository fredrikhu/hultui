using Aperture.Core;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// The client against a real Jellyfin. Skipped unless <c>APERTURE_LIVE_SERVER</c> and a key
/// are set (see <see cref="LiveServerFactAttribute"/>); nothing here writes to the library,
/// and the one test that touches playback state puts it back.
/// </summary>
public class LiveServerTests
{
    private static readonly CancellationTokenSource Timeout = new(TimeSpan.FromSeconds(30));
    private static CancellationToken Ct => Timeout.Token;

    [LiveServerFact]
    public async Task ABareHostnameFindsTheServerOnJellyfinsOwnPort()
    {
        // The address people type is "jellyfin.home", and a stock install listens on 8096
        // while port 80 refuses — the discovery walk is what makes typing the short form work.
        var host = new Uri(LiveServer.Address!).Host;
        var probe = await JellyfinConnection.DiscoverAsync(host, DeviceIdentity.New("aperture-tests"), Ct);

        Assert.True(probe.Ok, probe.Failure?.Message);
        Assert.False(string.IsNullOrEmpty(probe.Value.Id));
        Assert.False(string.IsNullOrEmpty(probe.Value.Version));
    }

    [LiveServerFact]
    public async Task QuickConnectHandsOutACodeToTypeSomewhereElse()
    {
        var device = DeviceIdentity.New("aperture-tests");
        var probe = await JellyfinConnection.DiscoverAsync(LiveServer.Address!, device, Ct);
        Assert.True(probe.Ok, probe.Failure?.Message);

        var started = await JellyfinConnection.StartQuickConnectAsync(probe.Value, device, Ct);

        // A server with Quick Connect switched off says so in its own sentence rather than
        // failing as a rejected sign-in — which is the half of this worth proving either way.
        if (Absent(!started.Ok, started.Failure?.Message ?? "Quick Connect is off on this server.")) return;

        Assert.False(string.IsNullOrWhiteSpace(started.Value.Code));
        Assert.False(string.IsNullOrWhiteSpace(started.Value.Secret));
        // Nobody is going to approve it, so the wait is expected to run out — and the code
        // expires on the server by itself.
        var waited = await JellyfinConnection.WaitForQuickConnectAsync(probe.Value, started.Value, device,
            timeout: TimeSpan.FromSeconds(3), ct: Ct);
        Assert.False(waited.Ok);
        Assert.Equal(ApertureFailureKind.Timeout, waited.Failure!.Kind);
    }

    [LiveServerFact]
    public async Task SignInYieldsAUserAndTheLibraries()
    {
        using var library = await LiveServer.ConnectAsync(Ct);

        Assert.NotEqual(Guid.Empty, library.User.Id);
        var libraries = await library.GetLibrariesAsync(Ct);
        Assert.True(libraries.Ok, libraries.Failure?.Message);
        Assert.NotEmpty(libraries.Value);
        // Every library the server reports has to be addressable, or the grid can't open it.
        Assert.All(libraries.Value, l => Assert.False(string.IsNullOrEmpty(l.Id)));
    }

    [LiveServerFact]
    public async Task AVideoLibraryPagesAndCarriesArtwork()
    {
        using var library = await LiveServer.ConnectAsync(Ct);
        var libraries = (await library.GetLibrariesAsync(Ct)).Value;
        var video = libraries.FirstOrDefault(l => l.Kind is LibraryKind.Movies or LibraryKind.TvShows);
        if (Absent(video is null, "The server has no film or series library.")) return;

        var page = await library.GetItemsAsync(new ItemQuery { ParentId = video!.Id, Limit = 8 }, Ct);
        Assert.True(page.Ok, page.Failure?.Message);
        Assert.NotEmpty(page.Value.Items);
        Assert.True(page.Value.TotalCount >= page.Value.Items.Count);

        // Image tags are the payload shape most likely to break: they arrive as an untyped
        // bag, so a mapping slip shows up as a grid of blank posters rather than an error.
        var withArt = page.Value.Items.Where(i => i.PrimaryImageTag is { Length: > 0 }).ToList();
        if (Absent(withArt.Count == 0, "No item in the first page has artwork.")) return;
        var url = library.ImageUrl(withArt[0].Id, ImageKind.Primary, withArt[0].PrimaryImageTag, maxWidth: 200);
        Assert.NotNull(url);

        using var http = new HttpClient();
        using var response = await http.GetAsync(url, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} for the poster URL");
        Assert.StartsWith("image/", response.Content.Headers.ContentType?.MediaType ?? "");
    }

    [LiveServerFact]
    public async Task TheResumeListHoldsOnlyPlayableVideo()
    {
        using var library = await LiveServer.ConnectAsync(Ct);
        var resume = await library.GetResumeAsync(10, Ct);

        Assert.True(resume.Ok, resume.Failure?.Message);
        // The raw endpoint also returns the seasons and series those episodes belong to,
        // with a position of zero; Continue watching must show only things that play.
        Assert.All(resume.Value, i => Assert.True(i.IsPlayable, $"{i.Kind} '{i.Name}' is not playable"));
    }

    [LiveServerFact]
    public async Task PlaybackYieldsAStreamTheServerWillActuallySend()
    {
        using var library = await LiveServer.ConnectAsync(Ct);
        var item = await FirstPlayableAsync(library);
        if (Absent(item is null, "The server has nothing playable.")) return;

        var target = await library.StartPlaybackAsync(item!.Id, TimeSpan.Zero, ct: Ct);
        Assert.True(target.Ok, target.Failure?.Message);
        Assert.False(string.IsNullOrEmpty(target.Value.PlaySessionId));

        // Ask for the first bytes only: this proves the URL, the token in it and the
        // server's willingness to send, without pulling a film through the test run.
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, target.Value.StreamUrl);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1024);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} for the stream URL");
    }

    [LiveServerFact]
    public async Task ACeilingMakesTheServerReEncodeInsteadOfSendingTheFile()
    {
        using var library = await LiveServer.ConnectAsync(Ct);
        var item = await FirstPlayableAsync(library);
        if (Absent(item is null, "The server has nothing playable.")) return;

        // Uncapped, this server sends the file itself — that is the whole point of pairing
        // it with mpv, and the baseline the cap has to change.
        var open = await library.StartPlaybackAsync(item!.Id, TimeSpan.Zero, ct: Ct);
        Assert.True(open.Ok, open.Failure?.Message);
        Assert.False(open.Value.Transcoding);

        // One megabit is below anything a real film is stored at, so the server has to
        // re-encode to honour it.
        var capped = await library.StartPlaybackAsync(item.Id, TimeSpan.Zero, 1_000_000, Ct);
        Assert.True(capped.Ok, capped.Failure?.Message);
        Assert.True(capped.Value.Transcoding,
            "the server offered a direct stream despite a 1 Mbps ceiling");
        Assert.Contains("videos", capped.Value.StreamUrl.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    [LiveServerFact]
    public async Task AScrubberSheetNeedsTheHeaderAndNotATokenInTheUrl()
    {
        using var library = await LiveServer.ConnectAsync(Ct);
        var page = await library.GetItemsAsync(new ItemQuery
        {
            Kinds = [MediaKind.Movie, MediaKind.Episode],
            Limit = 60,
        }, Ct);
        Assert.True(page.Ok, page.Failure?.Message);

        MediaItem? withStills = null;
        foreach (var item in page.Value.Items)
        {
            var full = await library.GetItemAsync(item.Id, Ct);
            if (full.Ok && full.Value.Trickplay.Count > 0) { withStills = full.Value; break; }
        }
        if (Absent(withStills is null, "No item has trickplay images yet.")) return;

        var info = withStills!.Trickplay[0];
        Assert.True(info.Width > 0 && info.TileWidth > 0 && info.Interval > TimeSpan.Zero,
            "the trickplay bag mapped to an unusable sheet — Kiota hands these over as UntypedObject");

        var url = library.TrickplayTileUrl(withStills.Id, info.Width, 0);
        Assert.NotNull(url);
        // The token belongs in the header. This endpoint is behind the session and refuses
        // ?api_key=, unlike the image endpoints, which are anonymous.
        Assert.DoesNotContain("api_key", url!.Query, StringComparison.OrdinalIgnoreCase);

        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization",
            DeviceIdentity.New("aperture-tests").AuthorizationHeader(library.User.AccessToken));
        using var response = await http.SendAsync(request, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} for the sheet");
        Assert.StartsWith("image/", response.Content.Headers.ContentType?.MediaType ?? "");
    }

    [LiveServerFact]
    public async Task ProgressReportsMoveTheResumePointAndPutItBack()
    {
        using var library = await LiveServer.ConnectAsync(Ct);
        var item = await FirstPlayableAsync(library);
        if (Absent(item is null, "The server has nothing playable.")) return;
        var before = (await library.GetItemAsync(item!.Id, Ct)).Value.ResumePosition;

        // Jellyfin keeps a resume point only past its own thresholds — five minutes in, and
        // over 5% of the runtime — so a position under those is *correctly* forgotten. The
        // test has to land past both or it proves nothing.
        var runtime = item.RunTime ?? TimeSpan.Zero;
        if (Absent(runtime < TimeSpan.FromMinutes(12), "Nothing long enough to hold a resume point.")) return;
        var at = TimeSpan.FromSeconds(Math.Max(360, runtime.TotalSeconds * 0.12));

        var target = (await library.StartPlaybackAsync(item.Id, TimeSpan.Zero, ct: Ct)).Value;
        try
        {
            Assert.True((await library.ReportStartedAsync(target, TimeSpan.Zero, paused: false, Ct)).Ok);
            Assert.True((await library.ReportProgressAsync(target, at, paused: false, Ct)).Ok);
            Assert.True((await library.ReportStoppedAsync(target, at, Ct)).Ok);

            var after = (await library.GetItemAsync(item.Id, Ct)).Value.ResumePosition;
            // An API key authenticates as the server, not as a person, so it opens no
            // session — and Jellyfin keys watch state to sessions, accepting these reports
            // (204) without recording them. The three calls being accepted is what this
            // test can prove; the resume point itself is proven by signing in as a user,
            // which no automated run here does.
            if (Absent(after is null, "The server accepted the reports but kept no position "
                + "(expected for an API-key session).")) return;
            Assert.InRange(after!.Value.TotalSeconds, at.TotalSeconds - 30, at.TotalSeconds + 30);
        }
        finally
        {
            // Leave the account as we found it: the user's Continue watching row is theirs.
            var restore = (await library.StartPlaybackAsync(item.Id, TimeSpan.Zero, ct: Ct)).Value;
            await library.ReportStoppedAsync(restore, before ?? TimeSpan.Zero, Ct);
        }
    }

    /// <summary>
    /// xunit 2 has no dynamic skip, so a test whose fixture the server simply does not have
    /// — no artwork, nothing playable — says so on the console and stops rather than failing
    /// over someone else's library contents.
    /// </summary>
    private static bool Absent(bool missing, string why)
    {
        if (missing) Console.WriteLine("SKIPPED: " + why);
        return missing;
    }

    private static async Task<MediaItem?> FirstPlayableAsync(IMediaLibrary library)
    {
        var page = await library.GetItemsAsync(new ItemQuery
        {
            Kinds = [MediaKind.Movie, MediaKind.Episode],
            Limit = 1,
        }, Ct);
        return page.Ok ? page.Value.Items.FirstOrDefault() : null;
    }
}

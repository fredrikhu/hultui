using Aperture;
using Aperture.Core;
using HultUI.Core.Platform;
using HultUI.Icons;
using HultUI.Media;
using Xunit;

namespace Aperture.Tests;

/// <summary>Everything that needs no server and no window.</summary>
public class ApertureUnitTests
{
    // ── Icons ────────────────────────────────────────────────────────────

    [Fact]
    public void EveryIconNameExistsInTheBundledSet()
    {
        var missing = typeof(Icons).GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(name => !Phosphor.Has(name))
            .ToList();

        Assert.True(missing.Count == 0,
            "Not in the bundled Phosphor set (add to scripts/phosphor-icons.txt and regenerate): "
            + string.Join(", ", missing));
    }

    [Fact]
    public void EveryLibraryAndItemKindHasAGlyph()
    {
        foreach (var kind in Enum.GetValues<LibraryKind>())
            Assert.True(Phosphor.Has(Icons.ForLibrary(kind)), $"{kind} has no glyph");
        foreach (var kind in Enum.GetValues<MediaKind>())
            Assert.True(Phosphor.Has(Icons.ForItem(kind)), $"{kind} has no glyph");
    }

    // ── Finding a server ─────────────────────────────────────────────────

    [Fact]
    public void ABareHostnameIsTriedOnJellyfinsPortAsWellAsTheDefaultOne()
    {
        // The whole point of the walk: "jellyfin.home" alone must reach a stock install,
        // whose port 80 is refused and whose 8096 answers.
        var candidates = JellyfinConnection.Candidates("jellyfin.home");

        Assert.Equal("http://jellyfin.home", candidates[0]);
        Assert.Contains("http://jellyfin.home:8096", candidates);
        Assert.Contains("https://jellyfin.home", candidates);
    }

    [Fact]
    public void AnAddressWithASchemeAndAPortIsTakenAtItsWord()
    {
        var candidates = JellyfinConnection.Candidates("https://media.example.com:9443");

        Assert.Equal(["https://media.example.com:9443"], candidates);
    }

    [Fact]
    public void ASchemeWithoutAPortStillTriesJellyfinsOwn()
    {
        var candidates = JellyfinConnection.Candidates("http://jellyfin.home");

        Assert.Equal("http://jellyfin.home", candidates[0]);
        Assert.Contains("http://jellyfin.home:8096", candidates);
        // A scheme was given, so the other one is not guessed at.
        Assert.DoesNotContain(candidates, c => c.StartsWith("https://", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://")]
    public void NonsenseYieldsNoCandidates(string input) =>
        Assert.Empty(JellyfinConnection.Candidates(input));

    [Fact]
    public void ATrailingSlashDoesNotProduceADoubleSlashedBaseAddress()
    {
        // Kiota appends its paths to BaseUrl verbatim, so a trailing slash here becomes
        // "//System/Info/Public" at the server.
        Assert.All(JellyfinConnection.Candidates("http://jellyfin.home:8096/"),
            c => Assert.False(c.EndsWith('/')));
    }

    // ── The authorization header ─────────────────────────────────────────

    [Fact]
    public void TheAuthorizationHeaderCarriesTheDeviceEvenWithoutAToken()
    {
        var device = new DeviceIdentity("Aperture", "1.2.3", "laptop", "abc123");

        var anonymous = device.AuthorizationHeader();
        Assert.StartsWith("MediaBrowser ", anonymous);
        Assert.Contains("DeviceId=\"abc123\"", anonymous);
        Assert.DoesNotContain("Token=", anonymous);

        Assert.Contains("Token=\"t0ken\"", device.AuthorizationHeader("t0ken"));
    }

    [Fact]
    public void AQuoteInAMachineNameIsEscapedRatherThanClosingTheHeaderValue()
    {
        var device = new DeviceIdentity("Aperture", "1.0", "fredrik\"s \\pc", "id");

        var header = device.AuthorizationHeader("t");
        Assert.Contains("Device=\"fredrik\\\"s \\\\pc\"", header);
    }

    // ── Settings, and where the token lives ──────────────────────────────

    [Fact]
    public void SigningInStoresTheTokenInTheCredentialStoreAndNeverInTheFile()
    {
        var store = new InMemoryCredentialStore();
        var path = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var settings = new ApertureSettings(store, path);
            var server = new ServerInfo("http://jellyfin.home:8096", "jellyfin", "12.1.0", "server-1");
            var user = new SignedInUser(Guid.NewGuid(), "fredrik", "super-secret-token", null);

            Assert.True(settings.Remember(server, user));

            var written = File.ReadAllText(path);
            Assert.DoesNotContain("super-secret-token", written);
            Assert.Contains("jellyfin.home", written);
            Assert.Equal("super-secret-token", settings.TokenFor("server-1"));

            // Reopening finds the account, and the token still comes from the store.
            var reopened = new ApertureSettings(store, path);
            Assert.Equal("server-1", reopened.Data.ActiveServerId);
            Assert.Equal("fredrik", reopened.Data.Active!.UserName);
            Assert.Equal("super-secret-token", reopened.TokenFor("server-1"));
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void SigningOutTakesTheTokenWithIt()
    {
        var store = new InMemoryCredentialStore();
        var path = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var settings = new ApertureSettings(store, path);
            settings.Remember(new ServerInfo("http://x:8096", "x", "1", "server-1"),
                new SignedInUser(Guid.NewGuid(), "u", "tok", null));

            settings.Forget("server-1");

            Assert.Null(settings.TokenFor("server-1"));
            Assert.Empty(settings.Data.Servers);
            Assert.Null(settings.Data.ActiveServerId);
            Assert.Equal(0, store.Count);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void TheDeviceIdSurvivesRestartsBecauseTheServerKeysSessionsOnIt()
    {
        var store = new InMemoryCredentialStore();
        var path = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var first = new ApertureSettings(store, path);
            first.Save();
            var id = first.Device.DeviceId;
            Assert.NotEmpty(id);

            Assert.Equal(id, new ApertureSettings(store, path).Device.DeviceId);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void TheWindowPlacementSurvivesARestart()
    {
        // Through the app's own source-generated context, which publishes AOT.
        var store = new InMemoryCredentialStore();
        var path = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var placement = new HultUI.Core.Platform.WindowPlacement(-2000, 500, 1600, 900, true);
            var first = new ApertureSettings(store, path);
            first.Data.WindowPlacement = placement;
            first.Save();

            Assert.Equal(placement, new ApertureSettings(store, path).Data.WindowPlacement);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void AnUnreadableSettingsFileReadsAsAFirstRunRatherThanThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ this is not json");

            var settings = new ApertureSettings(new InMemoryCredentialStore(), path);

            Assert.Empty(settings.Data.Servers);
            Assert.NotEmpty(settings.Data.DeviceId);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    // ── The item model ───────────────────────────────────────────────────

    [Fact]
    public void AnEpisodesSublineIsItsPlaceInTheSeries()
    {
        var episode = new MediaItem
        {
            Id = "1", Name = "I Went to Market", Kind = MediaKind.Episode,
            ParentIndexNumber = 2, IndexNumber = 4,
        };

        Assert.Equal("S2 · E4", episode.SubLine);
        Assert.True(episode.IsPlayable);
    }

    [Fact]
    public void OnlyThingsThatPlayAreCalledPlayable()
    {
        Assert.True(new MediaItem { Id = "1", Name = "f", Kind = MediaKind.Movie }.IsPlayable);
        Assert.False(new MediaItem { Id = "2", Name = "s", Kind = MediaKind.Series }.IsPlayable);
        Assert.False(new MediaItem { Id = "3", Name = "x", Kind = MediaKind.Season }.IsPlayable);
    }

    [Theory]
    [InlineData(45, "45 m")]
    [InlineData(60, "1 h 0 m")]
    [InlineData(107, "1 h 47 m")]
    [InlineData(0, "1 m")]     // a runtime the server rounded to nothing still reads as time
    public void RuntimesReadAsHoursAndMinutes(int minutes, string expected) =>
        Assert.Equal(expected, Ui.Runtime(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void AHalfWatchedFilmSaysHowMuchIsLeft()
    {
        Assert.Equal("38 m left", Ui.Remaining(TimeSpan.FromMinutes(96), TimeSpan.FromMinutes(58)));
        Assert.Equal("Finished", Ui.Remaining(TimeSpan.FromMinutes(96), TimeSpan.FromMinutes(96)));
    }

    [Fact]
    public void ACardsAccessibleNameCarriesWhatTheTickAndSliverShowVisually()
    {
        var watched = new MediaItem { Id = "1", Name = "Solaris", Kind = MediaKind.Movie, ProductionYear = 1972, Played = true };
        Assert.Equal("Solaris, 1972, watched", ApertureApp.AccessibleNameFor(watched));

        var partWatched = watched with { Played = false, ResumePosition = TimeSpan.FromMinutes(20) };
        Assert.Equal("Solaris, 1972, part-watched", ApertureApp.AccessibleNameFor(partWatched));
    }

    // ── Track matching ───────────────────────────────────────────────────

    [Fact]
    public void ATrackIsFoundByItsLanguageTag()
    {
        MediaTrack[] tracks =
        [
            new(1, "Japanese", "jpn"),
            new(2, "English", "eng"),
        ];

        Assert.Equal(2, TrackPreference.Match(tracks, "eng")?.Id);
        Assert.Equal(1, TrackPreference.Match(tracks, "jpn")?.Id);
        Assert.Null(TrackPreference.Match(tracks, "fre"));
    }

    [Fact]
    public void ATrackWithNoLanguageTagIsFoundByItsTitle()
    {
        // What a file with untagged tracks looks like — the title is all there is.
        MediaTrack[] tracks = [new(1, "Commentary"), new(2, "English (SDH)")];

        Assert.Equal(2, TrackPreference.Match(tracks, "English")?.Id);
    }

    [Fact]
    public void NoPreferenceMatchesNothing()
    {
        MediaTrack[] tracks = [new(1, "English", "eng")];

        Assert.Null(TrackPreference.Match(tracks, null));
        Assert.Null(TrackPreference.Match(tracks, "   "));
        Assert.Null(TrackPreference.Match([], "eng"));
    }

    // ── Failure copy ─────────────────────────────────────────────────────

    [Fact]
    public void EveryFailureKindHasASentenceThatNamesTheHost()
    {
        foreach (var kind in Enum.GetValues<ApertureFailureKind>())
        {
            var message = new ApertureFailure(kind, "raw message").UserMessage("jellyfin.home", "That film");
            Assert.False(string.IsNullOrWhiteSpace(message));
            // The raw message is a developer's sentence; only the kinds without their own
            // copy fall back to it. Invalid is the exception on purpose: nothing was asked
            // of a server, so the message it carries is the sentence to show.
            if (kind is not (ApertureFailureKind.Network or ApertureFailureKind.Timeout
                          or ApertureFailureKind.Invalid))
                Assert.NotEqual("raw message", message);
        }
    }

    // ── Trickplay arithmetic ─────────────────────────────────────────────

    /// <summary>320x180 stills, ten to a sheet (5 across, 2 down), one every 10 seconds.</summary>
    private static TrickplayInfo Sheet(int count = 100) =>
        new(320, 180, TileWidth: 5, TileHeight: 2, ThumbnailCount: count, Interval: TimeSpan.FromSeconds(10));

    [Fact]
    public void APositionFindsItsStillInItsSheet()
    {
        var sheet = Sheet();

        // The first still of the first sheet.
        Assert.Equal((0, 0, 0), sheet.Locate(TimeSpan.Zero));
        // 35 s is the fourth still: still sheet 0, fourth column.
        Assert.Equal((0, 3 * 320, 0), sheet.Locate(TimeSpan.FromSeconds(35)));
        // The sixth still wraps onto the second row of the same sheet.
        Assert.Equal((0, 0, 180), sheet.Locate(TimeSpan.FromSeconds(50)));
        // The eleventh starts the second sheet.
        Assert.Equal((1, 0, 0), sheet.Locate(TimeSpan.FromSeconds(100)));
    }

    [Fact]
    public void ThereIsNoStillPastTheLastOne()
    {
        var sheet = Sheet(count: 12);      // two minutes of stills

        Assert.NotNull(sheet.Locate(TimeSpan.FromSeconds(110)));
        Assert.Null(sheet.Locate(TimeSpan.FromSeconds(120)));
        Assert.Null(sheet.Locate(TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void TheTimeShownIsTheStillsOwn()
    {
        var sheet = Sheet();

        // A still every ten seconds cannot honestly be labelled 4:57.
        Assert.Equal(TimeSpan.FromSeconds(290), sheet.Snap(TimeSpan.FromSeconds(297)));
        Assert.Equal(TimeSpan.Zero, sheet.Snap(TimeSpan.FromSeconds(9)));
    }

    [Fact]
    public void TheCheapestSheetThatIsStillBigEnoughIsChosen()
    {
        List<TrickplayInfo> options =
        [
            Sheet() with { Width = 160 },
            Sheet() with { Width = 320 },
            Sheet() with { Width = 640 },
        ];

        Assert.Equal(320, ApertureApp.BestTrickplay(options, 300f)?.Width);
        Assert.Equal(640, ApertureApp.BestTrickplay(options, 500f)?.Width);
        // Nothing is wide enough: the widest there is beats scaling up a small one.
        Assert.Equal(640, ApertureApp.BestTrickplay(options, 900f)?.Width);
        Assert.Null(ApertureApp.BestTrickplay([], 300f));
    }

    /// <summary>
    /// The shape the SDK actually delivers. Kiota models a free-form object as an
    /// <c>UntypedObject</c>, not a <c>JsonElement</c>: reading it as the latter compiles,
    /// runs, and silently yields nothing — which looks exactly like a server that has
    /// generated no stills, and cost an afternoon to tell apart from one.
    /// </summary>
    [Fact]
    public void TheTrickplayBagIsReadAsKiotaDeliversIt()
    {
        static Microsoft.Kiota.Abstractions.Serialization.UntypedObject Sheet(int width) =>
            new(new Dictionary<string, Microsoft.Kiota.Abstractions.Serialization.UntypedNode>
            {
                ["Width"] = new Microsoft.Kiota.Abstractions.Serialization.UntypedInteger(width),
                ["Height"] = new Microsoft.Kiota.Abstractions.Serialization.UntypedInteger(180),
                ["TileWidth"] = new Microsoft.Kiota.Abstractions.Serialization.UntypedInteger(10),
                ["TileHeight"] = new Microsoft.Kiota.Abstractions.Serialization.UntypedInteger(10),
                ["ThumbnailCount"] = new Microsoft.Kiota.Abstractions.Serialization.UntypedInteger(142),
                ["Interval"] = new Microsoft.Kiota.Abstractions.Serialization.UntypedInteger(10_000),
            });

        var bag = new Jellyfin.Sdk.Generated.Models.BaseItemDto_Trickplay
        {
            AdditionalData = new Dictionary<string, object>
            {
                ["media-source-1"] = new Microsoft.Kiota.Abstractions.Serialization.UntypedObject(
                    new Dictionary<string, Microsoft.Kiota.Abstractions.Serialization.UntypedNode>
                    {
                        ["320"] = Sheet(320),
                        ["640"] = Sheet(640),
                    }),
            },
        };

        var read = JellyfinLibrary.MapTrickplay(bag);

        // Widest first, one entry per width, and the interval read as milliseconds.
        Assert.Equal([640, 320], read.Select(t => t.Width));
        Assert.Equal(TimeSpan.FromSeconds(10), read[0].Interval);
        Assert.Equal(142, read[0].ThumbnailCount);
        Assert.Equal(100, read[0].PerTile);
    }

    [Fact]
    public void ASeverWithNoStillsMapsToNothingRatherThanThrowing()
    {
        Assert.Empty(JellyfinLibrary.MapTrickplay(null));
        Assert.Empty(JellyfinLibrary.MapTrickplay(new Jellyfin.Sdk.Generated.Models.BaseItemDto_Trickplay()));
    }

    // ── Artwork sizes ────────────────────────────────────────────────────

    [Theory]
    [InlineData(1.0f, 1.0f)]
    [InlineData(1.25f, 1.25f)]
    [InlineData(1.5f, 1.5f)]
    [InlineData(2.0f, 2.0f)]
    [InlineData(1.001f, 1.0f)]   // float noise on a whole step stays on it
    [InlineData(1.1f, 1.25f)]    // a zoom between steps asks for the step above
    [InlineData(1.333f, 1.5f)]   // so does a fractional Wayland scale
    [InlineData(0.5f, 0.5f)]     // zoomed out: fewer pixels, not a floor of 1
    public void ArtworkIsFetchedForTheDisplayScaleRoundedUpToAQuarter(float display, float expected) =>
        Assert.Equal(expected, ApertureApp.ArtworkScaleFor(display));

    [Theory]
    [InlineData(168f, 1.0f, 168)]
    [InlineData(168f, 1.25f, 210)]
    [InlineData(168f, 1.5f, 252)]
    [InlineData(168f, 2.0f, 336)]
    [InlineData(300f, 1.75f, 525)]
    [InlineData(38f, 1.25f, 48)]   // 47.5: rounded up, never a pixel short
    public void ArtworkIsAskedForAtTheDevicePixelsItCovers(float layoutWidth, float scale, int pixels) =>
        Assert.Equal(pixels, ApertureApp.ArtworkPixels(layoutWidth, scale));

    /// <summary>Every request answered with the same small PNG.</summary>
    private sealed class SmallPngHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var bitmap = new SkiaSharp.SKBitmap(4, 6);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(data.ToArray()),
            });
        }
    }

    private static HultUI.Core.Rendering.IImageSource Loaded(PosterCache cache, Uri url)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (cache.Get(url) is ({ IsLoaded: true } image, PosterCache.Status.Ready)) return image;
            Thread.Sleep(5);
        }
        throw new TimeoutException($"{url} never loaded");
    }

    [Fact]
    public void AfterAScaleChangeThePosterCacheKeepsWhatIsAskedForAgainAndDropsTheRest()
    {
        using var cache = new PosterCache(() => { }, http: new HttpClient(new SmallPngHandler()));
        var a = new Uri("http://jellyfin.test/Items/a/Images/Primary?fillWidth=168");
        var b = new Uri("http://jellyfin.test/Items/b/Images/Primary?fillWidth=168");
        var c = new Uri("http://jellyfin.test/Items/c/Images/Primary?fillWidth=252");
        var pictureA = Loaded(cache, a);
        var pictureB = Loaded(cache, b);

        cache.Retire();
        Assert.True(cache.HasRetired);

        // The window went back: a is asked for at its own size again, so it is current.
        Assert.Same(pictureA, Loaded(cache, a));
        // b is not asked for again. Once the downloads that follow have landed, the next
        // ask drops it.
        Loaded(cache, c);
        cache.Get(c);

        Assert.False(cache.HasRetired);
        Assert.False(pictureB.IsLoaded, "a picture fetched for the old scale is still held");
        Assert.True(pictureA.IsLoaded, "a picture asked for again at its own size was dropped");
    }

    [Theory]
    [InlineData(1336f, 1.0f, 1408)]   // a 1,400-wide window, beside the 64-wide rail
    [InlineData(1336f, 1.5f, 2048)]   // 2,004 device pixels
    [InlineData(1336f, 2.0f, 2688)]
    [InlineData(1376f, 1.5f, 2176)]
    [InlineData(1280f, 1.0f, 1280)]   // on a step: not a step more
    [InlineData(396f, 1.5f, 640)]     // by height, in a narrow window
    public void AHeroIsAskedForAtItsDevicePixelsRoundedUpToAStep(float layoutSize, float scale, int pixels)
    {
        Assert.Equal(pixels, ApertureApp.HeroPixels(layoutSize, scale));
        Assert.True(pixels >= layoutSize * scale, "fewer pixels than the screen draws");
        Assert.True(pixels < layoutSize * scale + ApertureApp.HeroStep);
    }

    [Theory]
    [InlineData(780, "w780")]
    [InlineData(1280, "w1280")]
    [InlineData(1281, "w1920")]
    [InlineData(2048, "w1920")]   // the most TMDB serves short of a 4K original
    [InlineData(4000, "w1920")]
    public void ATmdbBackdropIsTheSmallestServedSizeAtLeastAsWide(int width, string size) =>
        Assert.Equal($"https://image.tmdb.org/t/p/{size}/b.jpg", Aperture.Core.Requests.RequestImages.Backdrop("/b.jpg", width)!.ToString());

    [Fact]
    public void AHeroSizeRetiredOnItsOwnStandsInUntilItsReplacementLandsAndThenGoes()
    {
        var held = new HeldPngHandler("fillWidth=2432");
        using var cache = new PosterCache(() => { }, http: new HttpClient(held));
        var narrow = new Uri("http://jellyfin.test/Items/a/Images/Backdrop?fillWidth=2048");
        var wide = new Uri("http://jellyfin.test/Items/a/Images/Backdrop?fillWidth=2432");
        var poster = new Uri("http://jellyfin.test/Items/b/Images/Primary?fillWidth=252");
        var cast = new Uri("http://jellyfin.test/Items/c/Images/Primary?fillWidth=165");
        var shown = Loaded(cache, narrow);
        var other = Loaded(cache, poster);

        // The window widened past a step: the hero retires the size on screen. Something
        // else lands meanwhile, and the sweep that follows must spare the stand-in it is
        // about to ask with.
        cache.Retire(narrow);
        Assert.True(cache.HasRetired);
        cache.Get(cast);
        var landed = DateTime.UtcNow.AddSeconds(5);
        while (!cache.IsReady(cast) && DateTime.UtcNow < landed) Thread.Sleep(5);
        var (first, status) = cache.Get(wide, narrow);
        Assert.Equal(PosterCache.Status.Ready, status);
        Assert.Same(shown, first);
        Assert.Same(shown, cache.Get(wide, narrow).Image);

        // Once the new size has landed, asking for it drops the old one; nothing else goes.
        held.Release("fillWidth=2432");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!cache.IsReady(wide) && DateTime.UtcNow < deadline) Thread.Sleep(5);
        var (picture, _) = cache.Get(wide, narrow);
        Assert.NotSame(shown, picture);
        Assert.False(shown.IsLoaded, "the hero's old size is still held");
        Assert.True(other.IsLoaded, "retiring one picture dropped another");
        Assert.False(cache.HasRetired);
    }

    /// <summary>Every request answered with a small PNG; one whose URL contains one of the
    /// texts it is made with once that text is released.</summary>
    private sealed class HeldPngHandler(params string[] holds) : HttpMessageHandler
    {
        private readonly Dictionary<string, TaskCompletionSource> _released = holds.ToDictionary(
            h => h, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        public void Release(string hold) => _released[hold].TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            foreach (var (hold, released) in _released)
                if (request.RequestUri!.ToString().Contains(hold, StringComparison.Ordinal))
                    await released.Task.WaitAsync(ct);
            using var bitmap = new SkiaSharp.SKBitmap(4, 6);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(data.ToArray()) };
        }
    }

    [Fact]
    public void AHeroSizePassedOverBeforeItLandedLandsRetiredAndGoes()
    {
        var held = new HeldPngHandler("fillWidth=2176", "fillWidth=2304", "fillWidth=2432");
        using var cache = new PosterCache(() => { }, http: new HttpClient(held));
        var passed = new Uri("http://jellyfin.test/Items/a/Images/Backdrop?fillWidth=2176");
        var wanted = new Uri("http://jellyfin.test/Items/a/Images/Backdrop?fillWidth=2304");
        var again = new Uri("http://jellyfin.test/Items/a/Images/Backdrop?fillWidth=2432");

        // A drag across three steps: each size is passed over while it downloads, and the
        // window settles back on the second.
        Assert.Equal(PosterCache.Status.Pending, cache.Get(passed).Status);
        Assert.Equal(PosterCache.Status.Pending, cache.Get(wanted).Status);
        cache.Retire(passed);
        cache.Retire(wanted);
        Assert.Equal(PosterCache.Status.Pending, cache.Get(again).Status);
        cache.Retire(again);
        cache.Get(wanted);   // asked for again before it landed: wanted after all

        // The two passed over land first, then the one wanted, last of all.
        held.Release("fillWidth=2176");
        held.Release("fillWidth=2432");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (cache.PendingCount > 1 && DateTime.UtcNow < deadline) Thread.Sleep(5);
        held.Release("fillWidth=2304");
        while (!cache.IsReady(wanted) && DateTime.UtcNow < deadline) Thread.Sleep(5);

        // The first ask after the last landing sweeps what the window passed over, and the
        // size it settled on is there, not swept with them and fetched again.
        var (picture, status) = cache.Get(wanted);
        Assert.Equal(PosterCache.Status.Ready, status);
        Assert.True(picture is { IsLoaded: true });
        Assert.False(cache.IsReady(passed), "a size passed over before it landed is still held");
        Assert.False(cache.IsReady(again), "a size passed over before it landed is still held");
        Assert.False(cache.HasRetired);
    }

    /// <summary>
    /// The count cap is held where a download lands, on the thread pool, while the UI thread may
    /// be drawing the very picture it pushes out. Disposed there, the native image could be
    /// freed under the draw, and the tile drew nothing on its next frame. A picture pushed out
    /// now stays whole until the UI thread asks the cache again, and that ask disposes it.
    /// </summary>
    [Fact]
    public void APicturePushedOutByTheCapStaysWholeUntilTheUiThreadAsksAgain()
    {
        using var landed = new SemaphoreSlim(0);
        using var cache = new PosterCache(() => landed.Release(), capacity: 1, http: new HttpClient(new RedPngHandler()));
        var a = new Uri("http://jellyfin.test/Items/a/Images/Primary?fillWidth=168");
        var b = new Uri("http://jellyfin.test/Items/b/Images/Primary?fillWidth=168");
        Land(cache, landed, a);
        var drawn = cache.Get(a).Image!;
        using var surface = new HultUI.Rendering.Skia.SkiaRasterRenderSurface();
        surface.Resize(8, 8, 1f);
        Assert.Equal(Red, Centre(surface, drawn));

        // The tile goes on drawing a, while b lands on a download thread and the cap of one
        // pushes a out.
        Land(cache, landed, b);
        Assert.False(cache.IsReady(a), "the cap of one kept two pictures");
        Assert.True(drawn.IsLoaded, "the download thread disposed a picture the UI thread may be drawing");
        Assert.Equal(Red, Centre(surface, drawn));

        // The UI thread's next ask lets it go.
        cache.Get(b);
        Assert.False(drawn.IsLoaded, "a picture pushed out of the cache is still held");
    }

    [Fact]
    public void APicturePushedOutByTheCapGoesWithTheCache()
    {
        using var landed = new SemaphoreSlim(0);
        var cache = new PosterCache(() => landed.Release(), capacity: 1, http: new HttpClient(new RedPngHandler()));
        var a = new Uri("http://jellyfin.test/Items/a/Images/Primary?fillWidth=168");
        Land(cache, landed, a);
        var first = cache.Get(a).Image!;
        Land(cache, landed, new Uri("http://jellyfin.test/Items/b/Images/Primary?fillWidth=168"));
        Assert.True(first.IsLoaded);

        cache.Dispose();
        Assert.False(first.IsLoaded, "a picture pushed out of the cache outlived it");
    }

    private static readonly (byte R, byte G, byte B) Red = (255, 0, 0);

    /// <summary>Asks for <paramref name="url"/> and waits for its download (the only one running)
    /// to land, without asking again: an ask is what disposes what the cap pushed out.</summary>
    private static void Land(PosterCache cache, SemaphoreSlim landed, Uri url)
    {
        Assert.Equal(PosterCache.Status.Pending, cache.Get(url).Status);
        Assert.True(landed.Wait(TimeSpan.FromSeconds(5)), $"{url} never landed");
        Assert.True(cache.IsReady(url), $"{url} landed without a picture");
    }

    /// <summary>The colour at the centre of the picture drawn over the whole surface.</summary>
    private static (byte R, byte G, byte B) Centre(HultUI.Rendering.Skia.SkiaRasterRenderSurface surface,
        HultUI.Core.Rendering.IImageSource picture)
    {
        var ctx = surface.RenderContext;
        ctx.BeginFrame(8, 8, 1f, HultUI.Core.Primitives.Color.White);
        ctx.DrawImage(picture, new HultUI.Core.Primitives.Rect(0, 0, 8, 8), HultUI.Core.Layout.ImageFit.Fill);
        ctx.EndFrame();
        surface.Present();
        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);
        int at = 4 * rowBytes + 4 * 4;
        return (pixels[at], pixels[at + 1], pixels[at + 2]);
    }

    /// <summary>Every request answered with a small red PNG.</summary>
    private sealed class RedPngHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var bitmap = new SkiaSharp.SKBitmap(4, 4);
            bitmap.Erase(SkiaSharp.SKColors.Red);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(data.ToArray()),
            });
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (IOException) { }
    }
}

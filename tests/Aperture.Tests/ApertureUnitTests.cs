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

        Assert.Equal(2, ApertureApp.Match(tracks, "eng")?.Id);
        Assert.Equal(1, ApertureApp.Match(tracks, "jpn")?.Id);
        Assert.Null(ApertureApp.Match(tracks, "fre"));
    }

    [Fact]
    public void ATrackWithNoLanguageTagIsFoundByItsTitle()
    {
        // What a file with untagged tracks looks like — the title is all there is.
        MediaTrack[] tracks = [new(1, "Commentary"), new(2, "English (SDH)")];

        Assert.Equal(2, ApertureApp.Match(tracks, "English")?.Id);
    }

    [Fact]
    public void NoPreferenceMatchesNothing()
    {
        MediaTrack[] tracks = [new(1, "English", "eng")];

        Assert.Null(ApertureApp.Match(tracks, null));
        Assert.Null(ApertureApp.Match(tracks, "   "));
        Assert.Null(ApertureApp.Match([], "eng"));
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

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (IOException) { }
    }
}

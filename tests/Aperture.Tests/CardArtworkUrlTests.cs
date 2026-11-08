using System.Collections.Concurrent;
using System.Reflection;
using Aperture;
using Aperture.Core;
using Aperture.Core.Testing;
using Aperture.Theme;
using HultUI.Core.Platform;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// Card picture addresses remembered (plan 17 round 8 step 6b). Every build asks for every card's
/// picture, kept or not, and made its address and the address's string again each time: 577 B a card
/// against the fake library, 1,136 B against Jellyfin's. An address is now made once per item, kind and
/// size, and again for an item whose picture changed, or once the data is reloaded. Home as
/// HomeCursorTests has it: 36 cards.
/// </summary>
[Collection("ApertureProcessWide")]
public class CardArtworkUrlTests
{
    [Fact]
    public void ABuild_MakesNoPictureAddress()
    {
        Host((session, app, library, asked) =>
        {
            int made = app.ArtworkUrlsMade;
            Rebuild(app);
            session.PumpFrames(2);
            Rebuild(app);
            session.PumpFrames(2);
            Assert.Equal(made, app.ArtworkUrlsMade);
        });
    }

    /// <summary>The same film with a new picture asks the cache, and the server, for the new one.</summary>
    [Fact]
    public void AnItemWhosePictureChanged_AsksForItsNewPicture()
    {
        Host((session, app, library, asked) =>
        {
            var film = library.Item("m010")!;
            Assert.Contains(asked, u => u.Contains("/Items/m010/", StringComparison.Ordinal));

            app.Artwork(film with { PrimaryImageTag = "tag-m010-new" }, ImageKind.Primary,
                Halide.PosterWidth, MathF.Round(Halide.PosterWidth * 1.5f), Halide.Radius2);
            session.PumpUntil(() => asked.Any(u => u.Contains("tag=tag-m010-new", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(5));

            Assert.Contains(asked, u => u.Contains("tag=tag-m010-new", StringComparison.Ordinal));
        });
    }

    /// <summary>A refresh lets every address go, so what is remembered is what the data on screen has.</summary>
    [Fact]
    public void ARefresh_MakesTheAddressesAgain()
    {
        Host((session, app, library, asked) =>
        {
            int made = app.ArtworkUrlsMade;
            app.Refresh();
            Settle(session);
            Assert.True(app.ArtworkUrlsMade - made >= 36, $"{app.ArtworkUrlsMade - made} made after a refresh");
        });
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    private static void Host(Action<HeadlessSession, ApertureApp, FakeMediaLibrary, ConcurrentQueue<string>> body)
    {
        var library = new FakeMediaLibrary();
        for (int l = 0; l < 5; l++)
        {
            library.AddLibrary($"lib{l}", $"Shelf {l}", LibraryKind.Movies);
            for (int i = 0; i < 7; i++)
            {
                int n = l * 7 + i;
                library.AddMovie($"m{n:000}", $"Film {n:000}", $"lib{l}", 1950 + n, 80 + n,
                    resume: n == 3 ? TimeSpan.FromMinutes(20) : null);
            }
        }
        library.ResumeIds.Add("m003");

        using var session = HeadlessSession.Create(1440, 900);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var handler = new Recording();
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                posterHttp: new HttpClient(handler));
            session.Application.SetRoot(app.Mount());
            session.PumpUntil(() => handler.Asked.Count >= 36, TimeSpan.FromSeconds(10));
            Settle(session);
            body(session, app, library, handler.Asked);
        }
        finally
        {
            app?.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(settingsPath)!, true); } catch (IOException) { }
        }
    }

    private static void Settle(HeadlessSession session)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < 300) { session.PumpFrame(); Thread.Sleep(5); }
        session.PumpFrames(2);
    }

    /// <summary>A view-only rebuild, as a picture landing asks for.</summary>
    private static void Rebuild(ApertureApp app) =>
        typeof(ApertureApp).GetMethod("RerenderView", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);

    /// <summary>Notes every picture asked for and has none.</summary>
    private sealed class Recording : HttpMessageHandler
    {
        public ConcurrentQueue<string> Asked { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked.Enqueue(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}

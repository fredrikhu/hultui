using Aperture;
using Aperture.Core;
using Aperture.Theme;
using HultUI;
using HultUI.Core.Platform;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Media;
using HultUI.Media.Mpv;

/// <summary>
/// The entry point is explicit, and STA, rather than top-level statements — the same
/// reason the other apps give: Windows hands a .NET main thread the MTA apartment, OLE
/// refuses to initialise on one, and the shell's dialogs and drag-and-drop go with it.
/// The attribute is inert on Linux and macOS.
/// </summary>
internal static class ApertureProgram
{
    [STAThread]
    private static void Main(string[] args)
    {
        // `--probe <address> [--api-key-file <path>]` asks the server who it is, signs in
        // when given a key, and prints what came back — no window, no settings touched.
        // It is how a server is diagnosed from a command line, and it is also the smoke
        // test for the native build: everything it does goes through Kiota and
        // System.Text.Json, which are the parts of this app that AOT could break.
        if (args.Contains("--probe"))
        {
            Environment.ExitCode = Probe(args).GetAwaiter().GetResult() ? 0 : 1;
            return;
        }

        // The Application ctor creates the SDL window + Vulkan device + Skia GRContext
        // synchronously, on this thread.
        //
        // Vulkan, because Aperture plays HDR video and only Vulkan can present it — see
        // docs/design/HDR.md. It costs roughly 130 ms more to the first frame and more CPU
        // through a resize, which the library browser pays without using; the trade is accepted
        // for the same reason as Reel's, that the point of the app is on the other side of it.
        // HULTUI_RENDERER=gl goes back if a machine needs it.
        //
        // Display-colour events too, so that switching HDR or dragging the SDR brightness slider
        // reaches the picture at once instead of on the next 100 ms poll — the film and the
        // chrome around it are exactly what that slider is changing.
        using var app = new Application("Aperture", 1440, 900, WindowStyle.Borderless,
            renderer: RendererKind.Vulkan, displayColorEvents: true);
        app.Theme = Halide.ToHultTheme();

#if !DEBUG
        // No developer surfaces in a shipped build (inspector, perf HUD, trace hotkeys)
        // unless HULTUI_DEVTOOLS=1 asks for them — for looking into a shipped installer.
        app.DevToolsHotkeysEnabled = Environment.GetEnvironmentVariable("HULTUI_DEVTOOLS") == "1";
#endif

        var window = app.PrimaryWindow;
        window.SetMinimumSize(900, 600);
        window.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "aperture.png"));

        // No decoder, no playback — but the rest of the app is still worth having, so a
        // missing libmpv is a fact the Settings page states rather than a refusal to start.
        Func<IMediaBackend>? backends = MpvBackend.IsAvailable ? () => new MpvBackend() : null;

        var settings = new ApertureSettings(CredentialStore.Default);

        // Open where it was closed. Recorded before the app's own teardown, while the native
        // window still exists to be asked.
        window.Placement = settings.Data.WindowPlacement;
        window.Closed += () =>
        {
            settings.Data.WindowPlacement = window.Placement;
            settings.Save();
        };

        var aperture = new ApertureApp(app, window, settings, library: null, backends: backends);
        window.Closed += aperture.Dispose;
        app.SetRoot(aperture.Mount());

        app.Run();
    }

    private static async Task<bool> Probe(string[] args)
    {
        string? After(string flag)
        {
            int at = Array.IndexOf(args, flag);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        var address = After("--probe");
        if (string.IsNullOrWhiteSpace(address))
        {
            Console.Error.WriteLine("usage: Aperture --probe <address> [--api-key-file <path>] [--user <name>]"
                                    + " [--seerr <address> [--seerr-api-key-file <path>]]");
            return false;
        }

        IMediaLibrary? signedIn = null;
        try
        {
            if (!await ProbeJellyfin(address, After("--api-key-file"), After("--user"), l => signedIn = l).ConfigureAwait(false))
                return false;
            return After("--seerr") is not { Length: > 0 } seerr
                || await ProbeRequestServer(seerr, After("--seerr-api-key-file"), signedIn).ConfigureAwait(false);
        }
        finally
        {
            signedIn?.Dispose();
        }
    }

    private static async Task<bool> ProbeJellyfin(string address, string? keyFile, string? user, Action<IMediaLibrary> keep)
    {
        var device = DeviceIdentity.New("aperture-probe");
        var found = await JellyfinConnection.DiscoverAsync(address, device).ConfigureAwait(false);
        if (!found.Ok)
        {
            Console.Error.WriteLine($"no: {found.Failure!.UserMessage(address)}");
            return false;
        }
        var server = found.Value;
        Console.WriteLine($"{server.Name} {server.Version} at {server.Address}");

        var key = keyFile is not null && File.Exists(keyFile) ? File.ReadAllText(keyFile).Trim() : null;
        if (key is null) return true;

        var session = await JellyfinConnection.SignInWithApiKeyAsync(server, key, user, device)
            .ConfigureAwait(false);
        if (!session.Ok)
        {
            Console.Error.WriteLine($"no: {session.Failure!.UserMessage(server.Host)}");
            return false;
        }
        var library = session.Value;
        keep(library);
        Console.WriteLine($"signed in as {library.User.Name}");

        var libraries = await library.GetLibrariesAsync().ConfigureAwait(false);
        if (!libraries.Ok)
        {
            Console.Error.WriteLine($"no: {libraries.Failure!.UserMessage(server.Host)}");
            return false;
        }
        Console.WriteLine($"{libraries.Value.Count} libraries: {string.Join(", ", libraries.Value.Select(l => l.Name))}");

        // One item, fetched in full: the detail path is where the untyped payloads live
        // (image tags, chapters, the trickplay bag), and the one most likely to differ
        // between a JIT run and a native one.
        var page = await library.GetItemsAsync(new ItemQuery { Kinds = [MediaKind.Movie, MediaKind.Episode], Limit = 1 })
            .ConfigureAwait(false);
        if (page.Ok && page.Value.Items.Count > 0)
        {
            var full = await library.GetItemAsync(page.Value.Items[0].Id).ConfigureAwait(false);
            if (full.Ok)
                Console.WriteLine($"read \"{full.Value.Name}\": {full.Value.Chapters.Count} chapters, "
                    + $"{full.Value.Trickplay.Count} trickplay widths, artwork tag {full.Value.PrimaryImageTag is not null}");
        }
        return true;
    }

    /// <summary>
    /// The request server, the same way: who it is, and — given its API key — a shelf, a
    /// search and a series read in full. Everything here is System.Text.Json source
    /// generation, which is the path a native build has to prove it kept. Read-only: the
    /// probe never requests anything.
    /// </summary>
    private static async Task<bool> ProbeRequestServer(string address, string? keyFile, IMediaLibrary? jellyfin)
    {
        var found = await Aperture.Core.Requests.SeerrConnection.DiscoverAsync(address).ConfigureAwait(false);
        if (!found.Ok)
        {
            Console.Error.WriteLine($"no: {found.Failure!.UserMessage(address)}");
            return false;
        }
        var info = found.Value;
        Console.WriteLine($"{info.Title} {info.Version} at {info.Address}"
                          + $" (Jellyfin sign-in without a password: {(info.SupportsQuickConnect ? "yes" : "no")},"
                          + $" 4K: {(info.Movie4kEnabled || info.Series4kEnabled ? "yes" : "no")})");

        var key = keyFile is not null && File.Exists(keyFile) ? File.ReadAllText(keyFile).Trim() : null;
        if (key is null) return true;

        var session = await Aperture.Core.Requests.SeerrConnection
            .SignInWithApiKeyAsync(info, jellyfin?.Server.Id ?? "probe", key, jellyfin?.User.Id).ConfigureAwait(false);
        if (!session.Ok)
        {
            Console.Error.WriteLine($"no: {session.Failure!.UserMessage(info.Host)}");
            return false;
        }
        using var server = session.Value;
        Console.WriteLine($"requests as {server.User.DisplayName}: {PermissionsLine(server.User)}");

        var trending = await server.DiscoverAsync(Aperture.Core.Requests.DiscoverShelf.Trending).ConfigureAwait(false);
        if (!trending.Ok)
        {
            Console.Error.WriteLine($"no: {trending.Failure!.UserMessage(info.Host)}");
            return false;
        }
        int inLibrary = trending.Value.Items.Count(i => i.InLibrary);
        Console.WriteLine($"trending: {trending.Value.Items.Count} titles, {inLibrary} already in the library");

        var series = await server.GetDetailAsync(Aperture.Core.Requests.DiscoverKind.Tv, 1399).ConfigureAwait(false);
        if (series.Ok)
            Console.WriteLine($"read \"{series.Value.Item.Title}\": {series.Value.Seasons.Count} seasons, "
                              + $"tvdb {series.Value.TvdbId}, {series.Value.Cast.Count} cast");

        var requests = await server.GetRequestsAsync(Aperture.Core.Requests.RequestFilter.All, mine: false, 0, 5)
            .ConfigureAwait(false);
        if (requests.Ok)
            Console.WriteLine($"{requests.Value.Total} requests; newest: "
                              + string.Join(", ", requests.Value.Items.Select(r => $"{r.Title} ({r.Status})")));

        // The discover layout, the genre rows and the downloaders' choices — each a JSON shape
        // of its own, so each has to be read once natively to count as proven.
        var layout = await server.GetDiscoverLayoutAsync().ConfigureAwait(false);
        if (layout.Ok)
            Console.WriteLine($"discover layout: {layout.Value.Count(s => s.Enabled)} of {layout.Value.Count} rows on");
        var genres = await server.GetGenresAsync(Aperture.Core.Requests.DiscoverKind.Movie).ConfigureAwait(false);
        if (genres.Ok) Console.WriteLine($"film genres: {genres.Value.Count}");
        var radarr = await server.GetServicesAsync(Aperture.Core.Requests.DiscoverKind.Movie).ConfigureAwait(false);
        if (radarr.Ok && radarr.Value.Count > 0)
        {
            var details = await server.GetServiceDetailsAsync(Aperture.Core.Requests.DiscoverKind.Movie, radarr.Value[0].Id)
                .ConfigureAwait(false);
            if (details.Ok)
                Console.WriteLine($"{radarr.Value[0].Name}: {details.Value.Profiles.Count} quality profiles, "
                                  + $"{details.Value.RootFolders.Count} root folders");
        }
        return true;
    }

    private static string PermissionsLine(Aperture.Core.Requests.RequestUser user) =>
        user.Has(Aperture.Core.Requests.RequestPermission.Admin) ? "administrator"
        : user.CanManageRequests ? "manages requests"
        : user.CanRequest(Aperture.Core.Requests.DiscoverKind.Movie) ? "can request"
        : "browse only";
}

using Aperture;
using Aperture.Core;
using Aperture.Theme;
using HultUI;
using HultUI.Core.Platform;
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

        // The Application ctor creates the SDL window + GL context + Skia GRContext
        // synchronously, on this thread.
        using var app = new Application("Aperture", 1440, 900, WindowStyle.Borderless);
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
            Console.Error.WriteLine("usage: Aperture --probe <address> [--api-key-file <path>] [--user <name>]");
            return false;
        }

        var device = DeviceIdentity.New("aperture-probe");
        var found = await JellyfinConnection.DiscoverAsync(address, device).ConfigureAwait(false);
        if (!found.Ok)
        {
            Console.Error.WriteLine($"no: {found.Failure!.UserMessage(address)}");
            return false;
        }
        var server = found.Value;
        Console.WriteLine($"{server.Name} {server.Version} at {server.Address}");

        var keyFile = After("--api-key-file");
        var key = keyFile is not null && File.Exists(keyFile) ? File.ReadAllText(keyFile).Trim() : null;
        if (key is null) return true;

        var session = await JellyfinConnection.SignInWithApiKeyAsync(server, key, After("--user"), device)
            .ConfigureAwait(false);
        if (!session.Ok)
        {
            Console.Error.WriteLine($"no: {session.Failure!.UserMessage(server.Host)}");
            return false;
        }
        using var library = session.Value;
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
}

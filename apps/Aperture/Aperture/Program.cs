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
}

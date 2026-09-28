using HultUI;
using HultUI.Core.Styling;
using HultUI.Core.Rendering;
using HultUI.Media;
using HultUI.Media.Mpv;
using Reel;

/// <summary>
/// Reel's entry point: a file in, a picture out. Explicit and STA rather than top-level
/// statements, for the reason the other apps give — Windows hands a .NET main thread the
/// MTA apartment, and OLE (the shell's file dialog, and drag-and-drop) refuses to
/// initialise on one. The attribute is inert elsewhere.
/// </summary>
internal static class ReelProgram
{
    [STAThread]
    private static void Main(string[] args)
    {
        // The GL driver load is the single largest piece of a cold start — ~150 ms that SDL
        // does lazily inside the first window — and most of it cannot be moved at all, for
        // the reason docs/NATIVE-AOT.md gives: no window can exist until it finishes. What
        // the preload does move is the part that overlaps the app's own pre-window work and
        // the text pipeline's warm-up. Measured on this app, Release JIT, five runs each
        // way: first frame at 502 ms without and 473 ms with, and no run overlapped.
        //
        // The framework keeps this opt-in because loading the driver off the main thread is
        // outside SDL's documented threading contract — validated on Windows/WGL, not on
        // X11 or Wayland. Reel is a Windows app with an installer and file associations, so
        // it opts in on Windows on its own behalf, and only where that validation holds. An
        // explicit setting either way is the user's and is left alone.
        //
        // Reel asks for Vulkan (below), and a Vulkan window never loads the GL driver — so the
        // renderer is named here too, and the preload only happens when HULTUI_RENDERER=gl puts
        // the app back on GL. Called without it, the preload could not know, loaded the GL
        // driver on every launch, and the Vulkan window waited for it.
        if (OperatingSystem.IsWindows() &&
            Environment.GetEnvironmentVariable("HULTUI_GL_PRELOAD") is null)
        {
            Environment.SetEnvironmentVariable("HULTUI_GL_PRELOAD", "1");
        }
        Application.BeginGraphicsPreload(RendererKind.Vulkan);

        // Registration runs without a window: an installer, a script, or a person who
        // would rather not hunt for a button can do it and be told what happened.
        if (OperatingSystem.IsWindows() && args.Any(a => a is "--register" or "--unregister"))
        {
            bool register = args.Contains("--register");
            bool ok = register ? FileAssociations.Register() : FileAssociations.Unregister();
            if (register && ok && !args.Contains("--quiet")) FileAssociations.OpenDefaultAppsSettings();
            Environment.ExitCode = ok ? 0 : 1;
            return;
        }

        var file = args.FirstOrDefault(a => !a.StartsWith('-'));
        var settings = new ReelSettings();

        // A second launch hands its file to the window that is already open, rather than
        // opening a tenth copy of the app — which is what "set as default player and
        // select ten files" does otherwise. Vesper's arrangement, for the same reason.
        System.Threading.Mutex? instanceLock = null;
        bool single = OperatingSystem.IsWindows() && settings.Data.SingleInstance;
        if (single)
        {
            instanceLock = new System.Threading.Mutex(initiallyOwned: true, @"Local\Reel.SingleInstance",
                out bool isNew);
            if (!isNew)
            {
                if (file is not null) SingleInstance.TrySignal(file);
                return;
            }
        }

        // Vulkan, because Reel plays films and HDR needs it: it is the only renderer that can
        // present an HDR swapchain, and the only one that carries sixteen bits a channel from
        // the decoder to the screen. See docs/design/HDR.md.
        //
        // It is not free — roughly 120 ms more to the first frame and more CPU through a window
        // resize, both measured there — and a library view showing no video pays that too. For a
        // film player that is the right way round: the cost is once at launch, the benefit is
        // every frame of every film. HULTUI_RENDERER=gl goes back if a machine needs it.
        //
        // Display-colour events too, so that switching HDR or dragging the SDR brightness slider
        // reaches the picture at once instead of on the next 100 ms poll. About 9 ms of start-up,
        // and HULTUI_DISPLAY_INFORMATION=0 turns it off.
        using var app = new Application("Reel", 1280, 760, renderer: RendererKind.Vulkan,
            displayColorEvents: true);
        app.Theme = Emulsion.ToHultTheme();

#if !DEBUG
        app.DevToolsHotkeysEnabled = Environment.GetEnvironmentVariable("HULTUI_DEVTOOLS") == "1";
#endif

        var window = app.PrimaryWindow;
        window.SetMinimumSize(480, 320);
        window.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "reel.png"));

        // No decoder is a broken install, not a reason to refuse to start: the window says
        // what is missing and where it looks.
        Func<IMediaBackend>? backends = MpvBackend.IsAvailable ? () => new MpvBackend() : null;

        var reel = new ReelApp(app, window, settings, backends);
        window.Closed += reel.Dispose;
        app.SetRoot(reel.Mount());

        if (file is not null) reel.Open(file);

        using var ipc = new CancellationTokenSource();
        if (single)
            SingleInstance.StartServer(path => Application.InvokeOnMainThread(() => reel.Open(path)), ipc.Token);

        app.Run();

        ipc.Cancel();
        instanceLock?.ReleaseMutex();
        instanceLock?.Dispose();
    }
}

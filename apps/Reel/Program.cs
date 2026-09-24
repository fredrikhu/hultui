using HultUI;
using HultUI.Core.Styling;
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

        using var app = new Application("Reel", 1280, 760);
        app.Theme = Theme.Dark;

#if !DEBUG
        app.DevToolsHotkeysEnabled = Environment.GetEnvironmentVariable("HULTUI_DEVTOOLS") == "1";
#endif

        var window = app.PrimaryWindow;
        window.SetMinimumSize(480, 320);

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

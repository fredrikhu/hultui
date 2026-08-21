using HultUI;
using HultUI.Core.Styling;
using VesperCommander;
using VesperCommander.Theme;

/// <summary>
/// The entry point is explicit, and STA, rather than top-level statements.
///
/// <para>Windows hands a .NET main thread the MTA apartment by default, and OLE refuses to
/// initialise on one — <c>OleInitialize</c> answers RPC_E_CHANGED_MODE. Everything that runs
/// through OLE is then unavailable for the life of the process: dragging files out to another
/// application above all, but the shell's own dialogs and menus are happier here too. Every
/// Windows desktop framework marks its entry point this way; the attribute is inert on Linux
/// and macOS, where SDL owns the thread on its own terms.</para>
/// </summary>
internal static class VesperProgram
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Elevated worker mode: this process was re-launched with administrator/root rights to
        // perform one batch of file operations (see Elevation). It must run before anything else
        // — no window, no settings, no single-instance handshake — and exit with the outcome.
        if (args.Length >= 2 && args[0] == Elevation.WorkerFlag)
            return Elevation.RunWorker(args[1]);

        // The folder this launch wants to open: a directory argument, else the working directory.
        var startPath = args.Length > 0 && Directory.Exists(args[0]) ? args[0] : Directory.GetCurrentDirectory();

        // Single-instance (Windows): the owning instance holds a named mutex and runs an IPC server;
        // a second launch hands its folder to the running window (which opens it as a tab) and exits.
        System.Threading.Mutex? instanceLock = null;
        bool single = OperatingSystem.IsWindows() && SettingsStore.Load().SingleInstance;
        if (single)
        {
            instanceLock = new System.Threading.Mutex(initiallyOwned: true, @"Local\VesperCommander.SingleInstance", out bool isNew);
            if (!isNew)
            {
                SingleInstance.TrySignal(startPath);   // fold this launch into the running instance
                return 0;
            }
        }

        // The Application ctor creates the SDL window + GL context + Skia GRContext
        // synchronously on the main thread. Borderless: Vesper draws its own chrome
        // (the Nocturne shell is a rounded, bordered card).
        using var app = new Application("Vesper Commander", 1240, 820, WindowStyle.Borderless);
        app.Theme = Nocturne.ToHultTheme();

        #if !DEBUG
        // No developer surfaces in a shipped build: the element inspector (Ctrl+Shift+I),
        // the performance HUD (F12), and trace recording (F11) all stay dark. Vesper also
        // owns Ctrl+Shift+C for "copy as path", so leaving the framework's chords live has
        // already cost one binding once. Debug builds keep the lot.
        app.DevToolsHotkeysEnabled = false;
        #endif

        var window = app.PrimaryWindow;
        // Floor the window so the two panes + preview never collapse below usability.
        window.SetMinimumSize(880, 560);

        var commander = new CommanderApp(app, window, startPath);
        window.Closed += commander.Dispose;
        app.SetRoot(commander.Mount());

        // Listen for second launches and open their folder as a tab (marshaled to the UI thread).
        using var ipcCts = new System.Threading.CancellationTokenSource();
        if (single)
            SingleInstance.StartServer(path => Application.InvokeOnMainThread(() => commander.OpenPathAsTab(path)), ipcCts.Token);

        app.Run();
        ipcCts.Cancel();
        instanceLock?.Dispose();
        return 0;

    }
}

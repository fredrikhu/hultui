using HultUI;
using HultUI.Core.Styling;
using VesperCommander;
using VesperCommander.Theme;

// Single-instance guard (Windows): when the setting is on, a second launch exits so
// only one Vesper window runs. (Forwarding the second launch's folder to the running
// instance as a tab would need IPC and is out of scope.)
System.Threading.Mutex? instanceLock = null;
if (OperatingSystem.IsWindows() && SettingsStore.Load().SingleInstance)
{
    instanceLock = new System.Threading.Mutex(initiallyOwned: true, @"Local\VesperCommander.SingleInstance", out bool isNew);
    if (!isNew) return;
}

// The Application ctor creates the SDL window + GL context + Skia GRContext
// synchronously on the main thread. Borderless: Vesper draws its own chrome
// (the Nocturne shell is a rounded, bordered card).
using var app = new Application("Vesper Commander", 1240, 820, WindowStyle.Borderless);
app.Theme = Nocturne.ToHultTheme();

var window = app.PrimaryWindow;
// Floor the window so the two panes + preview never collapse below usability.
window.SetMinimumSize(880, 560);

// Both panes open on the current directory until real navigation lands.
var start = Directory.GetCurrentDirectory();
var commander = new CommanderApp(app, window, start);
window.Closed += commander.Dispose;
app.SetRoot(commander.Mount());

app.Run();
instanceLock?.Dispose();

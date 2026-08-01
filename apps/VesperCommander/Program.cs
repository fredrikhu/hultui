using HultUI;
using HultUI.Core.Styling;
using VesperCommander;
using VesperCommander.Theme;

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

using HultUI;
using HultUI.Core.Styling;
using Meridian;
using Meridian.Theme;

// The Application ctor creates the SDL window + GL context + Skia GRContext
// synchronously, on the main thread.
using var app = new Application("Meridian", 1440, 900, WindowStyle.Borderless);
app.Theme = Azimuth.ToHultTheme();

#if !DEBUG
// No developer surfaces in a shipped build (inspector, perf HUD, trace hotkeys), unless
// HULTUI_DEVTOOLS=1 asks for them — for looking into a shipped installer.
app.DevToolsHotkeysEnabled = Environment.GetEnvironmentVariable("HULTUI_DEVTOOLS") == "1";
#endif

var window = app.PrimaryWindow;
window.SetMinimumSize(760, 540);
window.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "meridian.png"));

var meridian = new MeridianApp(app, window);
window.Closed += meridian.Dispose;
app.SetRoot(meridian.Mount());

app.Run();

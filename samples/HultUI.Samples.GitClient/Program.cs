using HultUI;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient;
using GitClient.Junction;
using GitClient.Theme;

// Settings load + git-root resolution touch no graphics state, so run them on a
// worker while the main thread creates the window/GPU context (the bulk of
// startup). SDL requires the window + event pump on the main thread, so the
// graphics init itself can't move — but this GL-free work can, and it overlaps
// almost entirely with the native init below.
var settingsTask = System.Threading.Tasks.Task.Run(() =>
{
    var settings = GitClientSettings.Load();
    IEnumerable<string> requestedPaths = args.Length > 0 ? args : settings.RepositoryPaths;
    var repoPaths = requestedPaths
        .Select(RepositoryPathResolver.FindGitRoot)
        .Where(path => path != null)
        .Select(path => path!)
        .Distinct()
        .ToList();

    if (repoPaths.Count == 0)
    {
        var fallback = RepositoryPathResolver.FindGitRoot(Environment.CurrentDirectory)
                       ?? RepositoryPathResolver.FindGitRoot(AppContext.BaseDirectory)
                       ?? ".";
        repoPaths.Add(fallback);
    }

    var activeIndex = args.Length > 0 ? 0 : settings.ActiveRepositoryIndex;
    settings.RepositoryPaths = repoPaths;
    settings.ActiveRepositoryIndex = activeIndex;
    if (args.Length > 0)
        settings.ActiveRepoPath = repoPaths[0];
    settings.Normalize();
    return (settings, repoPaths);
});

// The Application ctor creates the SDL window + GL context + Skia GRContext
// synchronously, on the main thread, concurrently with the settings worker.
using var app = new Application("Junction", 1440, 900, WindowStyle.Borderless);
app.Theme = Nocturne.ToHultTheme();

#if !DEBUG
// No developer surfaces in a shipped build: the element inspector (Ctrl+Shift+I),
// the performance HUD (F12), and trace recording (F11) all stay dark. Debug builds
// keep them, and HULTUI_TRACE still drives the trace capture below either way.
app.DevToolsHotkeysEnabled = false;
#endif

var window = app.PrimaryWindow;
// Floor the window size so the chrome never collapses. Below the compact-chrome
// breakpoint (~930 dip) JunctionApp switches the toolbar/breadcrumb to icon-only,
// which stays legible down to this minimum.
window.SetMinimumSize(760, 540);
window.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "junction.png"));

// Join the worker — usually already finished under the native init, so this
// blocks for ~0. (Any exception on the worker surfaces here.)
var (settings, repoPaths) = settingsTask.Result;

Console.WriteLine($"Opening {repoPaths.Count} repository/repositories:");
foreach (var path in repoPaths)
    Console.WriteLine($"  {path}");

var junction = new JunctionApp(app, window, settings);
window.Closed += junction.Dispose;
app.SetRoot(junction.Mount());

// Trace-capture hook: with HULTUI_TRACE_MS set, drive continuous frames for that
// long, then close the window so the normal shutdown flushes HULTUI_TRACE. The
// window is otherwise dirty-driven (an idle repo view renders a handful of cold
// startup frames and then stops), which is useless for steady-state GL numbers;
// a self-re-registering post-frame callback keeps NeedsRender true so Render /
// Flush / Present are exercised every frame. This is how automation grabs a
// steady-state GL trace without interaction (SDL3/Wayland doesn't turn
// SIGINT/SIGTERM into a clean quit, so a signal can't do the flush).
if (int.TryParse(Environment.GetEnvironmentVariable("HULTUI_TRACE_MS"), out var traceMs) && traceMs > 0)
{
    var deadline = Environment.TickCount64 + traceMs;
    void KeepRendering()
    {
        if (Environment.TickCount64 >= deadline) { window.Close(); return; }
        window.RequestPostFrameCallback(KeepRendering);
    }
    window.RequestPostFrameCallback(KeepRendering);
}

app.Run();

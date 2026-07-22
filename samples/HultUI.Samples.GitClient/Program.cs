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
app.Run();

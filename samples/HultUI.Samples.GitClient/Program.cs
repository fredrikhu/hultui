using HultUI;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient;
using GitClient.Junction;
using GitClient.Theme;

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

Console.WriteLine($"Opening {repoPaths.Count} repository/repositories:");
foreach (var path in repoPaths)
    Console.WriteLine($"  {path}");

using var app = new Application("Junction", 1440, 900, WindowStyle.Borderless);
app.Theme = Nocturne.ToHultTheme();

var window = app.PrimaryWindow;
// Floor the window size so the chrome never collapses. Below the compact-chrome
// breakpoint (~930 dip) JunctionApp switches the toolbar/breadcrumb to icon-only,
// which stays legible down to this minimum.
window.SetMinimumSize(760, 540);
window.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "junction.png"));
var junction = new JunctionApp(app, window, settings);
window.Closed += junction.Dispose;
app.SetRoot(junction.Mount());
app.Run();

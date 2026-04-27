using HultUI;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient;
using GitClient.Components.Layout;
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

Console.WriteLine($"Opening {repoPaths.Count} repository tab(s):");
foreach (var path in repoPaths)
    Console.WriteLine($"  {path}");

using var app = new Application("HultGit", 1200, 800, WindowStyle.Borderless);
app.Theme = new Theme
{
    Background = GitClientColors.Background,
    TextColor = GitClientColors.TextPrimary,
    Surface = GitClientColors.Surface,
    Primary = GitClientColors.ButtonPrimary,
    Secondary = GitClientColors.TextSecondary,
    ContextMenu = new ContextMenuTheme
    {
        Background = GitClientColors.Surface,
        BorderColor = GitClientColors.BorderLight,
        TextColor = GitClientColors.TextPrimary,
        DisabledTextColor = GitClientColors.TextMuted,
        HoverBackground = GitClientColors.SurfaceHover,
        SeparatorColor = GitClientColors.Border,
        ShortcutTextColor = GitClientColors.TextSecondary,
    },
};

var window = app.PrimaryWindow;
app.SetRoot(new TabbedApp(window, repoPaths, activeIndex).Mount());
app.Run();

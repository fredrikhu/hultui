using HultUI;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient;
using GitClient.Git;
using GitClient.Theme;

// Determine which repository to open:
// 1. Command-line argument
// 2. Current working directory
// 3. Fall back to this repo itself
var repoPath = args.Length > 0
    ? args[0]
    : FindGitRoot(Environment.CurrentDirectory)
      ?? FindGitRoot(AppContext.BaseDirectory)
      ?? ".";

Console.WriteLine($"Opening repository: {repoPath}");

var repo = new GitRepository(repoPath);

using var app = new Application("HultGit", 1200, 800, WindowStyle.Borderless);
app.Theme = new Theme
{
    Background = GitClientColors.Background,
    TextColor = GitClientColors.TextPrimary,
    Surface = GitClientColors.Surface,
    Primary = GitClientColors.ButtonPrimary,
    Secondary = GitClientColors.TextSecondary,
};

var window = app.PrimaryWindow;
app.SetRoot(new App(window, repo).Mount());
app.Run();

static string? FindGitRoot(string startPath)
{
    var dir = new DirectoryInfo(startPath);
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}

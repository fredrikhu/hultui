using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitClient;

public sealed class GitClientSettingsData
{
    public List<string> RepositoryPaths { get; set; } = [];
    public int ActiveRepositoryIndex { get; set; }
    public GeneralOptions General { get; set; } = new();
    public GitOptions Git { get; set; } = new();
    public ExternalToolOptions ExternalTools { get; set; } = new();
    public AppearanceOptions Appearance { get; set; } = new();

    public void Normalize()
    {
        RepositoryPaths ??= [];
        General ??= new GeneralOptions();
        Git ??= new GitOptions();
        ExternalTools ??= new ExternalToolOptions();
        Appearance ??= new AppearanceOptions();
        General.AutoFetchIntervalMinutes = Math.Clamp(General.AutoFetchIntervalMinutes, 1, 120);
    }

    public GitClientSettingsData Clone() => new()
    {
        RepositoryPaths = [.. RepositoryPaths],
        ActiveRepositoryIndex = ActiveRepositoryIndex,
        General = General.Clone(),
        Git = Git.Clone(),
        ExternalTools = ExternalTools.Clone(),
        Appearance = Appearance.Clone(),
    };
}

public sealed class GeneralOptions
{
    public bool AutoFetchEnabled { get; set; }
    public int AutoFetchIntervalMinutes { get; set; } = 5;
    public AutoFetchMode AutoFetchMode { get; set; } = AutoFetchMode.CurrentRepository;

    public GeneralOptions Clone() => new()
    {
        AutoFetchEnabled = AutoFetchEnabled,
        AutoFetchIntervalMinutes = AutoFetchIntervalMinutes,
        AutoFetchMode = AutoFetchMode,
    };
}

public enum AutoFetchMode
{
    CurrentRepository,
    AllOpenRepositories,
}

public sealed class GitOptions
{
    public PullStrategy PullStrategy { get; set; } = PullStrategy.Rebase;
    public bool PullAutoStash { get; set; } = true;
    public bool PruneOnFetch { get; set; } = true;

    public GitOptions Clone() => new()
    {
        PullStrategy = PullStrategy,
        PullAutoStash = PullAutoStash,
        PruneOnFetch = PruneOnFetch,
    };
}

public enum PullStrategy
{
    Merge,
    Rebase,
    FastForwardOnly,
}

public sealed class ExternalToolOptions
{
    public ExternalToolPreset DiffToolPreset { get; set; } = ExternalToolPreset.None;
    public string ExternalDiffToolPath { get; set; } = "";
    public string ExternalDiffToolArgs { get; set; } = "";
    public string ExternalMergeToolPath { get; set; } = "";
    public string ExternalMergeToolArgs { get; set; } = "";

    public ExternalToolOptions Clone() => new()
    {
        DiffToolPreset = DiffToolPreset,
        ExternalDiffToolPath = ExternalDiffToolPath,
        ExternalDiffToolArgs = ExternalDiffToolArgs,
        ExternalMergeToolPath = ExternalMergeToolPath,
        ExternalMergeToolArgs = ExternalMergeToolArgs,
    };
}

public enum ExternalToolPreset
{
    None,
    VSCode,
    VisualStudio,
    Rider,
    BeyondCompare,
    Meld,
    KDiff3,
    Custom,
}

public sealed class AppearanceOptions
{
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;

    public AppearanceOptions Clone() => new()
    {
        ThemeMode = ThemeMode,
    };
}

public enum ThemeMode
{
    System,
    Dark,
    Light,
}

public static class GitClientSettings
{
    public static GitClientSettingsData Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path)) return new GitClientSettingsData();

            var settings = JsonSerializer.Deserialize(
                               File.ReadAllText(path),
                               GitClientSettingsJsonContext.Default.GitClientSettingsData)
                           ?? new GitClientSettingsData();
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new GitClientSettingsData();
        }
    }

    public static void Save(GitClientSettingsData settings)
    {
        try
        {
            var path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(
                settings,
                GitClientSettingsJsonContext.Default.GitClientSettingsData));
        }
        catch
        {
            // Persistence is best-effort; the UI should keep working if config writes fail.
        }
    }

    private static string SettingsPath => Path.Combine(GetConfigDirectory(), "settings.json");

    private static string GetConfigDirectory()
    {
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
            return Path.Combine(xdgConfigHome, "HultGit");

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
            return Path.Combine(appData, "HultGit");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "HultGit");
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GitClientSettingsData))]
internal partial class GitClientSettingsJsonContext : JsonSerializerContext;

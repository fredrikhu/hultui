using System.Text.Json;
using System.Text.Json.Serialization;

namespace Junction;

public sealed class JunctionSettingsData
{
    public List<string> RepositoryPaths { get; set; } = [];
    public int ActiveRepositoryIndex { get; set; }

    // Junction: repositories are grouped into user-defined workspaces that are
    // opened together. RepositoryPaths is retained as the flat set of every
    // known repo (and for back-compat migration into a default workspace).
    public List<WorkspaceData> Workspaces { get; set; } = [];
    public string? ActiveWorkspaceId { get; set; }
    public string? ActiveRepoPath { get; set; }

    // Junction: per-repo badge swatch id (path → swatch id), driving the repo's
    // initial tile everywhere. Missing entries fall back to a deterministic seed.
    public Dictionary<string, string> RepoBadges { get; set; } = new();

    public GeneralOptions General { get; set; } = new();
    public GitOptions Git { get; set; } = new();
    public ExternalToolOptions ExternalTools { get; set; } = new();
    public AppearanceOptions Appearance { get; set; } = new();

    public void Normalize()
    {
        RepositoryPaths ??= [];
        Workspaces ??= [];
        RepoBadges ??= new();
        General ??= new GeneralOptions();
        Git ??= new GitOptions();
        ExternalTools ??= new ExternalToolOptions();
        Appearance ??= new AppearanceOptions();
        General.AutoFetchIntervalMinutes = Math.Clamp(General.AutoFetchIntervalMinutes, 1, 120);

        foreach (var ws in Workspaces)
            ws.Normalize();

        // Migrate a pre-workspaces config: fold every known repo into one
        // default workspace so the sidebar has something to show.
        if (Workspaces.Count == 0 && RepositoryPaths.Count > 0)
        {
            Workspaces.Add(new WorkspaceData
            {
                Id = "default",
                Name = "My repositories",
                Icon = "stack",
                RepoPaths = [.. RepositoryPaths],
            });
        }

        // Keep the flat RepositoryPaths in sync as the union of all workspaces.
        var union = Workspaces.SelectMany(w => w.RepoPaths).Distinct().ToList();
        foreach (var p in union)
            if (!RepositoryPaths.Contains(p))
                RepositoryPaths.Add(p);

        // Clamp the active-workspace / active-repo pointers to something valid.
        if (Workspaces.Count > 0)
        {
            var active = Workspaces.FirstOrDefault(w => w.Id == ActiveWorkspaceId) ?? Workspaces[0];
            ActiveWorkspaceId = active.Id;
            if (ActiveRepoPath == null || !active.RepoPaths.Contains(ActiveRepoPath))
                ActiveRepoPath = active.RepoPaths.FirstOrDefault();
        }
    }

    public JunctionSettingsData Clone() => new()
    {
        RepositoryPaths = [.. RepositoryPaths],
        ActiveRepositoryIndex = ActiveRepositoryIndex,
        Workspaces = Workspaces.Select(w => w.Clone()).ToList(),
        ActiveWorkspaceId = ActiveWorkspaceId,
        ActiveRepoPath = ActiveRepoPath,
        RepoBadges = new(RepoBadges),
        General = General.Clone(),
        Git = Git.Clone(),
        ExternalTools = ExternalTools.Clone(),
        Appearance = Appearance.Clone(),
    };
}

/// <summary>A named group of repositories opened together.</summary>
public sealed class WorkspaceData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>A logical icon key (e.g. "stack", "browsers", "shapes"); the UI maps it to a glyph.</summary>
    public string Icon { get; set; } = "stack";
    /// <summary>The workspace's icon-colour swatch id (see Swatches).</summary>
    public string Color { get; set; } = "blurple";
    public List<string> RepoPaths { get; set; } = [];

    public void Normalize()
    {
        RepoPaths ??= [];
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N")[..8];
        if (string.IsNullOrWhiteSpace(Icon))
            Icon = "stack";
        if (string.IsNullOrWhiteSpace(Color))
            Color = "blurple";
    }

    public WorkspaceData Clone() => new()
    {
        Id = Id,
        Name = Name,
        Icon = Icon,
        Color = Color,
        RepoPaths = [.. RepoPaths],
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
    /// <summary>Default for the composer's Sign toggle: GPG/SSH-sign new commits
    /// (<c>git commit -S</c>). Requires a signing key configured in git.</summary>
    public bool SignCommits { get; set; }

    /// <summary>Diff viewer: pass <c>--ignore-all-space</c> so whitespace-only
    /// changes are hidden. Display-only — interactive staging is disabled while on.</summary>
    public bool IgnoreWhitespace { get; set; }

    public GitOptions Clone() => new()
    {
        PullStrategy = PullStrategy,
        PullAutoStash = PullAutoStash,
        PruneOnFetch = PruneOnFetch,
        SignCommits = SignCommits,
        IgnoreWhitespace = IgnoreWhitespace,
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

public static class JunctionSettings
{
    public static JunctionSettingsData Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path)) return new JunctionSettingsData();

            var settings = JsonSerializer.Deserialize(
                               File.ReadAllText(path),
                               JunctionSettingsJsonContext.Default.JunctionSettingsData)
                           ?? new JunctionSettingsData();
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new JunctionSettingsData();
        }
    }

    public static void Save(JunctionSettingsData settings)
    {
        try
        {
            var path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(
                settings,
                JunctionSettingsJsonContext.Default.JunctionSettingsData));
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
[JsonSerializable(typeof(JunctionSettingsData))]
[JsonSerializable(typeof(WorkspaceData))]
internal partial class JunctionSettingsJsonContext : JsonSerializerContext;

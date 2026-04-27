using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitClient;

public sealed class GitClientSettingsData
{
    public List<string> RepositoryPaths { get; set; } = [];
    public int ActiveRepositoryIndex { get; set; }
}

public static class GitClientSettings
{
    public static GitClientSettingsData Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path)) return new GitClientSettingsData();

            return JsonSerializer.Deserialize(
                       File.ReadAllText(path),
                       GitClientSettingsJsonContext.Default.GitClientSettingsData)
                   ?? new GitClientSettingsData();
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

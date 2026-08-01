using System.Text.Json;
using System.Text.Json.Serialization;

namespace VesperCommander;

public enum ConflictMode { Ask, KeepNewer, AutoRename }
public enum SortMode { Name, Ext, Size, Date }
public enum Density { Compact, Comfortable }
public enum DiffToolKind { Builtin, VSCode, Junction }
public enum ArchiverKind { Builtin, SevenZip }

/// <summary>User settings, edited in the Options window and persisted as JSON.
/// Public fields (not properties) so the Options page can flip them by <c>ref</c>;
/// JSON uses <c>IncludeFields</c>.</summary>
public sealed class Settings
{
    // General
    public bool RestoreTabs = true;
    public bool SingleInstance;
    public bool ConfirmDelete = true;
    public bool Recycle = true;
    public ConflictMode Conflict = ConflictMode.Ask;

    // Panes
    public bool ShowSize = true;
    public bool ShowDate = true;
    public SortMode Sort = SortMode.Name;
    public Density Density = Density.Comfortable;
    public int PreviewWidth = 300;
    public bool DirsFirst = true;
    public bool ShowHidden;
    public bool ShowPreview = true;

    // Tools
    public DiffToolKind DiffTool = DiffToolKind.Builtin;
    public string GitClient = "";
    public string Editor = "code --goto {file}:{line}";
    public string Terminal = "wt.exe -d {dir}";
    public ArchiverKind Archiver = ArchiverKind.Builtin;

    // Colors
    public int Accent;                      // index into the accent palette
    public bool Glow;
    public bool Zebra;

    public Settings Clone() => (Settings)MemberwiseClone();
}

[JsonSourceGenerationOptions(WriteIndented = true, IncludeFields = true)]
[JsonSerializable(typeof(Settings))]
internal partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>Persists <see cref="Settings"/> to
/// <c>%LOCALAPPDATA%\VesperCommander\settings.json</c> (best-effort, AOT-safe).</summary>
public static class SettingsStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VesperCommander", "settings.json");

    public static Settings Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return new Settings();
            return JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.Settings)
                   ?? new Settings();
        }
        catch { return new Settings(); }
    }

    public static void Save(Settings s)
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(s, SettingsJsonContext.Default.Settings));
        }
        catch { /* best-effort */ }
    }
}

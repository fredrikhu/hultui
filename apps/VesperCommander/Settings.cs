using System.Text.Json;
using System.Text.Json.Serialization;

namespace VesperCommander;

public enum ConflictMode { Ask, KeepNewer, AutoRename }
public enum PreviewMode { Off, Normal, Full }   // hidden · side column · fills the right pane
public enum SortMode { Name, Ext, Size, Date }
public enum Density { Compact, Comfortable }
public enum DiffToolKind { Builtin, VSCode, Junction }
public enum ArchiverKind { Builtin, SevenZip }
public enum ViewMode { Detail, Brief, Thumbnails }   // per-pane file-list layout

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
    public bool SortDescending;
    public Density Density = Density.Comfortable;
    public int PreviewWidth = 300;
    public bool DirsFirst = true;
    public bool ShowHidden;
    public PreviewMode Preview = PreviewMode.Normal;
    public ViewMode DefaultView = ViewMode.Detail;   // new tabs/panes open in this mode

    // Tools
    public DiffToolKind DiffTool = DiffToolKind.Builtin;
    // Built-in diff: wrap a line too wide for its column instead of scrolling sideways.
    // Off by default — scrolling keeps every line one row tall, which is what makes the
    // two sides read as a table.
    public bool DiffWrap;
    public string GitClient = "";
    public string Editor = "code --goto {file}:{line}";
    public string ImageEditor = "mspaint.exe {file}";
    public string Terminal = "wt.exe -d {dir}";
    public ArchiverKind Archiver = ArchiverKind.Builtin;
    public string SevenZipPath = "";        // path to 7z.exe for 7z/rar/encryption; "" = try PATH

    // Colors
    public int Accent;                      // index into the accent palette
    public bool Glow;
    public bool Zebra;

    // Keyboard — command-name → chord overrides (only non-default bindings are stored).
    public Dictionary<string, string> KeyBindings = new();

    public Settings Clone()
    {
        var c = (Settings)MemberwiseClone();
        c.KeyBindings = new Dictionary<string, string>(KeyBindings);   // deep-copy the map
        return c;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, IncludeFields = true)]
[JsonSerializable(typeof(Settings))]
internal partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>
/// Where the app keeps its settings and session: <c>%LOCALAPPDATA%\VesperCommander</c>,
/// or whatever <c>VESPER_CONFIG_DIR</c> names.
///
/// <para>The override exists for tests. They construct the real app, which loads these
/// files and — since the diff's wrap toggle persists — writes them, so without somewhere
/// else to point them a test run reads and rewrites the developer's own configuration.</para>
/// </summary>
public static class ConfigDir
{
    public static string Path =>
        Environment.GetEnvironmentVariable("VESPER_CONFIG_DIR") is { Length: > 0 } dir
            ? dir
            : System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VesperCommander");
}

/// <summary>Persists <see cref="Settings"/> to <c>settings.json</c> in
/// <see cref="ConfigDir"/> (best-effort, AOT-safe).</summary>
public static class SettingsStore
{
    private static string FilePath => Path.Combine(ConfigDir.Path, "settings.json");

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

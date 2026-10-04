using System.Text.Json.Serialization;
using HultUI.Core.Storage;

namespace VesperCommander;

public enum ConflictMode { Ask, KeepNewer, AutoRename }
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
    public bool DirsFirst = true;
    public bool ShowHidden;
    public ViewMode DefaultView = ViewMode.Detail;   // new tabs/panes open in this mode

    /// <summary>Whether the preview panel is showing. It takes over the pane you are not
    /// standing in, so it is a two-state toggle rather than the old off/side/full cycle: the
    /// narrow side column it replaces was too narrow to read a file in and cost the panes the
    /// width instead.
    ///
    /// <para>Deliberately a new key rather than the old <c>Preview</c> enum. That one
    /// persisted as a number, and a number landing in a bool throws inside
    /// <see cref="SettingsStore.Load"/>, whose catch hands back a default
    /// <see cref="Settings"/> — one preview rename would quietly reset every other
    /// preference. Unknown keys are ignored, so the stale pair just falls away on the next
    /// save.</para></summary>
    public bool PreviewPane;

    // Find files (Alt+F7) — the dialog seeds itself from these and writes a change back
    // when it closes, so a depth/exclude set once carries into the next search.
    public int FindMaxDepth;                    // folder levels below the search root; 0 = unlimited
    public string FindExcludeFolders = "";      // ";"-separated folder patterns, e.g. ".git;.vs"

    // Text
    /// <summary>The face text files are read in — the viewer (Shift+F3) and the built-in
    /// diff, which show the same kind of content and have always agreed on how. Empty means
    /// the platform's monospace, which is what every earlier build used; a name this machine
    /// has no font for falls back to it, so a settings file carried between machines opens
    /// either way.</summary>
    public string TextFont = "";

    /// <summary>Point size for that same text. Row heights follow it, so the lines do not
    /// crowd as it grows.</summary>
    public float TextFontSize = 11.5f;

    /// <summary>Bounds on <see cref="TextFontSize"/>, shared by the loader's clamp and the
    /// stepper that offers it.</summary>
    public const float MinTextFontSize = 8f;
    public const float MaxTextFontSize = 24f;

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
    /// <summary>Which colour scheme, by <c>NamedTheme.Id</c>. An id this build does not know
    /// reads back as the default rather than as an error, so a settings file that has been
    /// round-tripped through a newer build still opens.</summary>
    public string Theme = "nocturne";
    public int Accent;                      // 0 = the theme's own accent; 1..3 override it
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
/// Where the app keeps its settings and session: <c>%LOCALAPPDATA%\VesperCommander</c> on
/// Windows, <c>$XDG_DATA_HOME/VesperCommander</c> (by default
/// <c>~/.local/share/VesperCommander</c>) elsewhere — HultUI's <see cref="AppFolders.Data"/> —
/// or whatever <c>VESPER_CONFIG_DIR</c> names.
///
/// <para>The override exists for tests. They construct the real app, which loads these
/// files and — since the diff's wrap toggle persists — writes them, so without somewhere
/// else to point them a test run reads and rewrites the developer's own configuration.</para>
/// </summary>
public static class ConfigDir
{
    public static string Path => Resolve(Environment.GetEnvironmentVariable("VESPER_CONFIG_DIR"));

    /// <summary>The per-user folder, ignoring <c>VESPER_CONFIG_DIR</c>.</summary>
    internal static string Default => Resolve(null);

    private static string Resolve(string? overrideDir) => string.IsNullOrEmpty(overrideDir)
        ? AppFolders.Data("VesperCommander")
        : System.IO.Path.GetFullPath(overrideDir);

    /// <summary>The folder, from the values the environment gives: the override, made absolute
    /// against the working directory (where a relative one pointed anyway), else
    /// <see cref="AppFolders.ResolveData"/> — which is where the fix for the fresh-Linux-account
    /// empty folder now lives, for every app.</summary>
    internal static string Resolve(string? overrideDir, string? xdgDataHome, string? localAppData,
                                   string? userProfile, bool isWindows) =>
        !string.IsNullOrEmpty(overrideDir)
            ? System.IO.Path.GetFullPath(overrideDir)
            : AppFolders.ResolveData("VesperCommander", xdgDataHome, localAppData, userProfile, isWindows);
}

/// <summary>Persists <see cref="Settings"/> to <c>settings.json</c> in <see cref="ConfigDir"/>:
/// written beside and swapped in, the previous kept as a backup (see <see cref="JsonFile{T}"/>).</summary>
public static class SettingsStore
{
    private static JsonFile<Settings> File => new(ConfigDir.Path, "settings.json", SettingsJsonContext.Default.Settings);

    public static Settings Load()
    {
        var loaded = File.Load() ?? new Settings();
        // A hand-edited (or older) file must not be able to leave the viewer at a size
        // with no glyphs in it, or with a null family the text pipeline would throw on.
        loaded.TextFont ??= string.Empty;
        loaded.TextFontSize = loaded.TextFontSize <= 0f
            ? 11.5f
            : Math.Clamp(loaded.TextFontSize, Settings.MinTextFontSize, Settings.MaxTextFontSize);
        return loaded;
    }

    public static void Save(Settings s) => File.Save(s);
}

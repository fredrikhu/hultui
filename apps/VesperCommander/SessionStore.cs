using System.Text.Json;
using System.Text.Json.Serialization;

namespace VesperCommander;

/// <summary>The open tabs per pane + active index, persisted for "Restore tabs on start".
/// The view-mode lists are parallel to the dir lists (index-aligned) and optional — an
/// older session file without them restores every tab in Detail.</summary>
public sealed record SessionData(
    List<string> Left, int LeftActive, List<string> Right, int RightActive,
    List<int>? LeftViews = null, List<int>? RightViews = null);

// Source-generated (de)serialization — reflection-based System.Text.Json is not
// trim/AOT-safe, and this app publishes AOT.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SessionData))]
internal partial class SessionJsonContext : JsonSerializerContext;

/// <summary>Persists the tab session to <c>%LOCALAPPDATA%\VesperCommander\session.json</c>.
/// Best-effort: any I/O or parse failure is swallowed.</summary>
public static class SessionStore
{
    private static string FilePath => Path.Combine(ConfigDir.Path, "session.json");

    public static SessionData? Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize(File.ReadAllText(path), SessionJsonContext.Default.SessionData);
        }
        catch { return null; }
    }

    public static void Save(SessionData session)
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(session, SessionJsonContext.Default.SessionData));
        }
        catch { /* best-effort persistence */ }
    }
}

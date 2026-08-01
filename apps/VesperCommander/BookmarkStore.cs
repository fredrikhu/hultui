using System.Text.Json;
using System.Text.Json.Serialization;

namespace VesperCommander;

/// <summary>Serializable bookmark record (the app's private Bookmark maps to this).</summary>
public sealed record BookmarkData(string Name, string Path);

// Source-generated (de)serialization — reflection-based System.Text.Json is not
// trim/AOT-safe, and this app publishes AOT.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<BookmarkData>))]
internal partial class BookmarkJsonContext : JsonSerializerContext;

/// <summary>
/// Persists the user's bookmarks to <c>%LOCALAPPDATA%\VesperCommander\bookmarks.json</c>.
/// All operations are best-effort: any I/O or parse failure is swallowed so a corrupt
/// or unwritable file never blocks the app.
/// </summary>
public static class BookmarkStore
{
    private static string FilePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VesperCommander");
            return Path.Combine(dir, "bookmarks.json");
        }
    }

    public static List<BookmarkData> Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return [];
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, BookmarkJsonContext.Default.ListBookmarkData) ?? [];
        }
        catch { return []; }
    }

    public static void Save(IEnumerable<BookmarkData> items)
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(items.ToList(), BookmarkJsonContext.Default.ListBookmarkData);
            File.WriteAllText(path, json);
        }
        catch { /* best-effort persistence */ }
    }
}

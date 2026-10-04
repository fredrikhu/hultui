using System.Text.Json.Serialization;
using HultUI.Core.Storage;

namespace VesperCommander;

/// <summary>Serializable bookmark record (the app's private Bookmark maps to this).</summary>
public sealed record BookmarkData(string Name, string Path);

// Source-generated (de)serialization — reflection-based System.Text.Json is not
// trim/AOT-safe, and this app publishes AOT.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<BookmarkData>))]
internal partial class BookmarkJsonContext : JsonSerializerContext;

/// <summary>
/// Persists the user's bookmarks to <c>bookmarks.json</c> in <see cref="ConfigDir"/>. Best-effort:
/// a corrupt or unwritable file never blocks the app, and a write is swapped in with the previous
/// kept as a backup (see <see cref="JsonFile{T}"/>) — it used to be written over in place, where a
/// crash mid-write lost every bookmark.
/// </summary>
public static class BookmarkStore
{
    private static JsonFile<List<BookmarkData>> File =>
        new(ConfigDir.Path, "bookmarks.json", BookmarkJsonContext.Default.ListBookmarkData);

    public static List<BookmarkData> Load() => File.Load() ?? [];

    public static void Save(IEnumerable<BookmarkData> items) => File.Save(items.ToList());
}

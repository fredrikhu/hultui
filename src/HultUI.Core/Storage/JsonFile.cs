using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HultUI.Core.Storage;

/// <summary>
/// One JSON file of an app's state — its settings, its session, its bookmarks — read and written
/// so that neither a crash nor a bad edit can cost the person what was in it.
/// </summary>
/// <remarks>
/// <para><b>Writing</b> builds the new file beside the live one, flushes it to disk, and swaps it
/// in, keeping what it replaces as <c><i>name</i>.bak</c>. Writing over the live file instead
/// risks a truncated one, and an unreadable settings file looks exactly like a first run: the
/// app comes back with nothing in it, and nothing says why. The backup is one step of undo, not
/// a history.</para>
/// <para><b>Reading</b> falls back to the backup when the live file cannot be read or parsed —
/// a hand edit gone wrong, or a crash in the moment between the swap's two renames, when only
/// the backup is there. Neither readable: null, and the caller starts from its defaults.</para>
/// <para>Neither ever throws for the file's sake: persistence is best-effort, and an app has to
/// go on working on a read-only disk. <see cref="Save"/> says whether it worked.</para>
/// <para>The type info comes from a source-generated <c>JsonSerializerContext</c>, which is
/// what keeps this trimmable and AOT-safe.</para>
/// </remarks>
public sealed class JsonFile<T> where T : class
{
    private const string BackupSuffix = ".bak";
    private const string PendingSuffix = ".tmp";

    private readonly JsonTypeInfo<T> _type;

    public JsonFile(string path, JsonTypeInfo<T> typeInfo)
    {
        Path = path;
        _type = typeInfo;
    }

    /// <summary>A file named <paramref name="fileName"/> in <paramref name="folder"/> — an
    /// <see cref="AppFolders"/> folder, usually.</summary>
    public JsonFile(string folder, string fileName, JsonTypeInfo<T> typeInfo)
        : this(System.IO.Path.Combine(folder, fileName), typeInfo) { }

    public string Path { get; }

    /// <summary>The stored value, from the file or else its backup; null when neither exists or
    /// can be read.</summary>
    public T? Load() => Read(Path) ?? Read(Path + BackupSuffix);

    /// <summary>Writes <paramref name="value"/>, keeping the previous contents as the backup.
    /// False when it could not be written; the file on disk is then as it was.</summary>
    public bool Save(T value)
    {
        var pending = Path + PendingSuffix;
        try
        {
            if (System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } folder)
                Directory.CreateDirectory(folder);
            using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, _type);
                // On disk before the rename makes it the file: a power cut otherwise leaves a
                // renamed file whose contents never arrived.
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(Path)) File.Replace(pending, Path, Path + BackupSuffix, ignoreMetadataErrors: true);
            else File.Move(pending, Path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The half-written file is the failure; it must not be left to be found.
            try { File.Delete(pending); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }

    private T? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), _type) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}

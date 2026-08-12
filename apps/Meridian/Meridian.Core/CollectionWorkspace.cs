namespace Meridian.Core;

/// <summary>
/// An opened collection: a folder of plain text files (<c>*.req</c>, <c>envs/*.env</c>)
/// that is the single source of truth for everything the app shows. The GUI and the CLI
/// both start from one of these.
/// </summary>
public sealed record CollectionWorkspace(string Root)
{
    /// <summary>The folder's own name — what the UI titles the collection.</summary>
    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(Root));

    /// <summary>
    /// Opens <paramref name="path"/> as a workspace, or null when it isn't an existing
    /// directory. Any folder qualifies — a collection with no requests yet is a
    /// collection, not an error.
    /// </summary>
    public static CollectionWorkspace? TryOpen(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return Directory.Exists(path)
                ? new CollectionWorkspace(Path.GetFullPath(path))
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }
}

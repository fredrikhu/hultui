namespace Meridian.Core;

public sealed record CollectionTree(CollectionFolder Root, IReadOnlyList<EnvFileRef> Environments);

/// <summary>A folder that (transitively) contains requests. Folders that don't —
/// <c>src/</c>, <c>docs/</c> — are pruned: the tree shows the collection, not
/// the whole repository.</summary>
public sealed record CollectionFolder(
    string Name,
    string Path,
    IReadOnlyList<CollectionFolder> Folders,
    IReadOnlyList<RequestRef> Requests,
    bool HasFolderDefaults);

/// <summary>A request in the tree: enough to draw a row (name + method chip)
/// without holding the parsed document.</summary>
public sealed record RequestRef(string DisplayName, string Path, string? Method);

/// <summary>An environment file, named by its stem (<c>local.env</c> → "local").</summary>
public sealed record EnvFileRef(string Name, string Path);

/// <summary>
/// Walks a workspace into a <see cref="CollectionTree"/>. IO errors on
/// individual entries are skipped, not thrown — a scan of a live directory
/// must survive files vanishing under it. Dot-directories (<c>.git</c>,
/// <c>.meridian</c>) are never entered.
/// </summary>
public static class CollectionScanner
{
    public static CollectionTree Scan(CollectionWorkspace workspace)
    {
        var root = ScanFolder(workspace.Root, workspace.Name);
        var envs = ScanEnvironments(workspace.Root);
        return new CollectionTree(
            root ?? new CollectionFolder(workspace.Name, workspace.Root, [], [], false),
            envs);
    }

    private static CollectionFolder? ScanFolder(string path, string name)
    {
        var folders = new List<CollectionFolder>();
        var requests = new List<RequestRef>();
        bool hasDefaults = false;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path).Order(StringComparer.OrdinalIgnoreCase))
            {
                var dirName = System.IO.Path.GetFileName(dir);
                if (dirName.StartsWith('.')) continue;
                // A pruned subfolder (no requests anywhere below) just isn't part
                // of the collection.
                if (ScanFolder(dir, dirName) is { } sub)
                    folders.Add(sub);
            }

            foreach (var file in Directory.EnumerateFiles(path, "*.req").Order(StringComparer.OrdinalIgnoreCase))
            {
                var fileName = System.IO.Path.GetFileName(file);
                if (fileName.Equals("_folder.req", StringComparison.OrdinalIgnoreCase))
                {
                    hasDefaults = true;
                    continue;
                }
                requests.Add(ReadRef(file));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (folders.Count == 0 && requests.Count == 0)
            return null;
        return new CollectionFolder(name, path, folders, requests, hasDefaults);
    }

    private static RequestRef ReadRef(string file)
    {
        string? method = null, name = null;
        try
        {
            var doc = ReqDocument.Parse(File.ReadAllText(file));
            method = doc.Method;
            name = doc.Name;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The row still exists; it just has no chip until the file is readable.
        }
        return new RequestRef(name ?? Prettify(System.IO.Path.GetFileNameWithoutExtension(file)), file, method);
    }

    private static IReadOnlyList<EnvFileRef> ScanEnvironments(string root)
    {
        var envsDir = System.IO.Path.Combine(root, "envs");
        if (!Directory.Exists(envsDir)) return [];
        try
        {
            return Directory.EnumerateFiles(envsDir, "*.env")
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(f => new EnvFileRef(System.IO.Path.GetFileNameWithoutExtension(f), f))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>"get-charge" → "Get charge": the display name when a file has no
    /// <c>name</c> directive.</summary>
    internal static string Prettify(string stem)
    {
        var words = stem.Replace('-', ' ').Replace('_', ' ');
        return words.Length == 0 ? stem : char.ToUpperInvariant(words[0]) + words[1..];
    }
}

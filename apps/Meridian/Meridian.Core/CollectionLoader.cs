namespace Meridian.Core;

/// <summary>
/// Turns a path into a runnable scope + environment — the loading the GUI does
/// through its tree and env documents, done once for a headless run. Shared so
/// the CLI cannot drift from the app in what it considers "the collection".
/// </summary>
public static class CollectionLoader
{
    public sealed record Loaded(
        CollectionWorkspace Workspace,
        IReadOnlyList<CollectionRunner.Request> Requests,
        Dictionary<string, string> Environment,
        IReadOnlyList<string> MissingSecrets,
        string ScopeName);

    /// <summary>
    /// Loads <paramref name="path"/> — a collection root or any folder inside
    /// one — plus the named environment. Returns null with a reason when the
    /// path isn't a folder or the environment doesn't exist.
    ///
    /// <para>The workspace root is the nearest ancestor with an <c>envs/</c>
    /// directory (so <c>meridian run payments-api/charges</c> still finds the
    /// collection's environments), falling back to the path itself.</para>
    /// </summary>
    public static Loaded? Load(string path, string? envName, IKeyring keyring, out string? error)
    {
        error = null;
        if (CollectionWorkspace.TryOpen(path) is not { } scope)
        {
            error = $"not a folder: {path}";
            return null;
        }

        var root = FindRoot(scope.Root);
        var tree = CollectionScanner.Scan(root);
        var requests = new List<CollectionRunner.Request>();
        Walk(tree.Root, scope.Root, requests);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var missing = new List<string>();
        if (envName != null)
        {
            var envFile = tree.Environments.FirstOrDefault(e => e.Name == envName);
            if (envFile == null)
            {
                error = tree.Environments.Count == 0
                    ? $"no environments in {root.Root}"
                    : $"no environment \"{envName}\" — have: {string.Join(", ", tree.Environments.Select(e => e.Name))}";
                return null;
            }
            string text;
            try { text = File.ReadAllText(envFile.Path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                error = $"could not read {envFile.Path}: {e.Message}";
                return null;
            }
            var doc = EnvDocument.Parse(text);
            foreach (var key in doc.Keys)
            {
                if (doc.IsSecret(key))
                {
                    // Keyring, then the CI-injected environment variable. A
                    // secret this machine lacks is reported, not silently
                    // resolved to its placeholder.
                    if (keyring.Get(root.Root, key) is { } secret) values[key] = secret;
                    else missing.Add(key);
                }
                else if (doc.Values.TryGetValue(key, out var value))
                    values[key] = value;
            }
        }

        string scopeName = Path.TrimEndingDirectorySeparator(scope.Root) == Path.TrimEndingDirectorySeparator(root.Root)
            ? root.Name + "/"
            : Path.GetRelativePath(root.Root, scope.Root).Replace('\\', '/') + "/";
        return new Loaded(root, requests, values, missing, scopeName);
    }

    /// <summary>Requests at or below <paramref name="scopeRoot"/>, in tree order.</summary>
    private static void Walk(CollectionFolder folder, string scopeRoot, List<CollectionRunner.Request> into)
    {
        foreach (var sub in folder.Folders) Walk(sub, scopeRoot, into);
        foreach (var request in folder.Requests)
        {
            if (!IsUnder(request.Path, scopeRoot)) continue;
            try
            {
                into.Add(new CollectionRunner.Request(request.Path, request.DisplayName,
                    ReqDocument.Parse(File.ReadAllText(request.Path))));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // An unreadable file simply isn't in the scope; the run reports
                // on what it could load.
            }
        }
    }

    private static bool IsUnder(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    /// <summary>The nearest ancestor (including <paramref name="start"/>) that
    /// has an <c>envs/</c> directory — the collection root.</summary>
    private static CollectionWorkspace FindRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        for (var cursor = dir; cursor != null; cursor = cursor.Parent)
            if (Directory.Exists(Path.Combine(cursor.FullName, "envs")))
                return new CollectionWorkspace(cursor.FullName);
        return new CollectionWorkspace(start);
    }
}

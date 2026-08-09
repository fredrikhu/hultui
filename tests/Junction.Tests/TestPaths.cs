namespace Junction.Tests;

/// <summary>
/// Locating the app's source tree from a test binary. Tests run out of
/// <c>tests/Junction.Tests/bin/&lt;config&gt;/net10.0/</c>, so the repo root is found by
/// walking up until a known landmark appears rather than by counting directory levels —
/// the count changes with the build configuration and the RID subfolder.
/// </summary>
internal static class TestPaths
{
    /// <summary>The repo's <c>apps/Junction</c> directory.</summary>
    public static string AppDirectory { get; } = FindDirectory(Path.Combine("apps", "Junction"));

    private static string FindDirectory(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"Could not find {relative} above {AppContext.BaseDirectory}");
    }
}

namespace HultUI.UiTests;

/// <summary>Locates a built sample executable relative to the repo root.</summary>
internal static class SamplePath
{
    public static string Accessibility() => Resolve("HultUI.Samples.Accessibility");

    private static string Resolve(string sample)
    {
        string config =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        string exe = Path.Combine(RepoRoot(), "samples", sample, "bin", config, "net10.0", sample + ".exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException($"Sample exe not found: {exe}. Build the sample first.");
        return exe;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HultUI.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root (HultUI.slnx) not found");
    }
}

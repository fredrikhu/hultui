namespace HultUI.UiTests;

/// <summary>Locates a built sample executable relative to the repo root.</summary>
internal static class SamplePath
{
    public static string Accessibility() => Resolve("HultUI.Samples.Accessibility");
    public static string VirtualList() => Resolve("HultUI.Samples.VirtualList");

    /// <summary>The WinForms host sample, which targets net10.0-windows rather than net10.0.</summary>
    public static string WinFormsHost() => Resolve("HultUI.Samples.WinFormsHost", "net10.0-windows");

    /// <summary>The Vulkan probe window, which lives beside these tests rather than among the samples.</summary>
    public static string VulkanProbe() => Resolve("HultUI.UiTests.VulkanProbe", folder: "tests");

    private static string Resolve(string sample, string targetFramework = "net10.0", string folder = "samples")
    {
        string config =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        string exe = Path.Combine(RepoRoot(), folder, sample, "bin", config, targetFramework, sample + ".exe");
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

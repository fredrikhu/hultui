using System.Reflection;
using System.Runtime.Versioning;

namespace HultUI.UiTests;

/// <summary>Locates a built sample executable relative to the repo root.</summary>
internal static class SamplePath
{
    public static string Accessibility() => Resolve("HultUI.Samples.Accessibility");
    public static string VirtualList() => Resolve("HultUI.Samples.VirtualList");
    public static string WebView() => Resolve("HultUI.Samples.WebView");

    /// <summary>The WinForms host sample, which targets the framework's "-windows" flavour.</summary>
    public static string WinFormsHost() => Resolve("HultUI.Samples.WinFormsHost", TargetFramework + "-windows");

    /// <summary>The Vulkan probe window, which lives beside these tests rather than among the samples.</summary>
    public static string VulkanProbe() => Resolve("HultUI.UiTests.VulkanProbe", folder: "tests");

    /// <summary>The samples' output folder name ("net11.0"): every project here targets the same
    /// .NET (Directory.Build.props), so it is read off this assembly rather than written down.</summary>
    private static readonly string TargetFramework = "net" +
        typeof(SamplePath).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName.Split("Version=v")[1];

    private static string Resolve(string sample, string? targetFramework = null, string folder = "samples")
    {
        targetFramework ??= TargetFramework;
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

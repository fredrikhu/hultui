using System.Runtime.InteropServices;

namespace HultUI.WebView.Cef.Interop;

/// <summary>
/// Finding libcef and the helper, loading the library, and the API handshake. Once per process:
/// the library is never unloaded (CEF cannot start twice in a process anyway).
/// </summary>
internal static unsafe class CefLibrary
{
    /// <summary>The CEF API version the interop was generated for. Any CEF that still offers
    /// it serves the engine unchanged (spike L0, question 2): 146 and later, at the time of
    /// writing, which offer 13300 to their own release.</summary>
    public const int ApiVersion = CefApi.Version;

    /// <summary>The helper's file name; see native/cef-helper.</summary>
    public const string HelperName = "hultui-cef-helper";

    /// <summary>What a CEF directory must hold beside libcef.so for CEF to start: the
    /// distribution's Release and Resources merged, as Fedora's package is.</summary>
    private static readonly string[] Required = ["libcef.so", "icudtl.dat", "v8_context_snapshot.bin", "resources.pak"];

    /// <summary>
    /// Where a CEF directory is looked for, in order: <c>HULTUI_CEF_DIR</c>, the application's
    /// <c>cef/</c>, its <c>runtimes/&lt;rid&gt;/native/cef/</c>, then the distribution's
    /// (Fedora's <c>/usr/lib64/cef</c>, Arch's <c>/usr/lib/cef</c>).
    /// </summary>
    internal static IEnumerable<string> CandidateDirectories(string? environment, string appDirectory, string rid)
    {
        if (!string.IsNullOrEmpty(environment)) yield return environment;
        yield return Path.Combine(appDirectory, "cef");
        yield return Path.Combine(appDirectory, "runtimes", rid, "native", "cef");
        yield return "/usr/lib64/cef";
        yield return "/usr/lib/cef";
    }

    /// <summary>
    /// Where the helper is looked for: <c>HULTUI_CEF_HELPER</c>, beside the application, then in
    /// its <c>runtimes/&lt;rid&gt;/native/</c>, where the package puts it.
    /// </summary>
    internal static IEnumerable<string> CandidateHelpers(string? environment, string appDirectory, string rid)
    {
        if (!string.IsNullOrEmpty(environment)) yield return environment;
        yield return Path.Combine(appDirectory, HelperName);
        yield return Path.Combine(appDirectory, "runtimes", rid, "native", HelperName);
    }

    /// <summary>The first candidate that holds everything CEF needs, or null; and, when one
    /// held libcef.so but not the rest, which files it lacked.</summary>
    internal static string? FindDirectory(IEnumerable<string> candidates, out string? incomplete)
    {
        incomplete = null;
        foreach (var dir in candidates)
        {
            if (!File.Exists(Path.Combine(dir, "libcef.so"))) continue;
            var missing = Required.Where(f => !File.Exists(Path.Combine(dir, f))).ToList();
            if (missing.Count == 0) return dir;
            incomplete ??= $"{dir} has libcef.so but not {string.Join(", ", missing)}" +
                (Directory.Exists(Path.Combine(dir, "..", "Resources"))
                    ? " (the official distribution's Resources/ has to be merged into Release/)" : "");
        }
        return null;
    }

    /// <summary>
    /// The portable RID packages put natives under (<c>linux-x64</c>, <c>linux-arm64</c>). Not
    /// <see cref="RuntimeInformation.RuntimeIdentifier"/>: a distribution's own .NET reports its
    /// own (<c>fedora.44-x64</c>), and no package has a folder by that name.
    /// </summary>
    internal static string PortableRid => "linux-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    private static readonly Lazy<Probe> Result = new(Load);

    /// <summary>The library, loaded and handshaken, or why not. Never throws.</summary>
    public static Probe Loaded => Result.Value;

    internal sealed record Probe(string? Directory, string? Helper, string? Version, string? Error)
    {
        public bool IsLoaded => Error is null;
    }

    private static Probe Load()
    {
        try
        {
            if (!OperatingSystem.IsLinux())
                return new(null, null, null, "The CEF engine runs on Linux; on Windows use WebView2Engine.");

            var rid = PortableRid;
            var app = AppContext.BaseDirectory;
            var dir = FindDirectory(CandidateDirectories(Environment.GetEnvironmentVariable("HULTUI_CEF_DIR"), app, rid), out var incomplete);
            if (dir is null)
                return new(null, null, null, incomplete ?? "CEF (the Chromium Embedded Framework) is not installed. On Fedora: sudo dnf install cef");

            var helper = CandidateHelpers(Environment.GetEnvironmentVariable("HULTUI_CEF_HELPER"), app, rid).FirstOrDefault(File.Exists);
            if (helper is null)
                return new(dir, null, null, $"{HelperName} was not found beside the application. It comes with HultUI.WebView.Cef " +
                    "(runtimes/<rid>/native); building from source needs a C compiler (native/cef-helper).");

            nint library;
            try { library = NativeLibrary.Load(Path.Combine(dir, "libcef.so")); }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                return new(dir, helper, null, $"{dir}/libcef.so would not load: {ex.Message}");
            }
            Capi.Load(library);
            if (Capi.cef_api_hash is null || Capi.cef_initialize is null || Capi.cef_version_info is null)
                return new(dir, helper, null, $"{dir}/libcef.so is not a CEF library this engine knows.");

            // cef_version_info may be called before the handshake; cef_api_hash must be the
            // first call that commits to an API version, and an unsupported one answers null.
            var version = $"{Capi.cef_version_info(0)}.{Capi.cef_version_info(1)}.{Capi.cef_version_info(2)} " +
                          $"(Chromium {Capi.cef_version_info(4)}.{Capi.cef_version_info(5)}.{Capi.cef_version_info(6)}.{Capi.cef_version_info(7)})";
            if (Capi.cef_api_hash(ApiVersion, 0) is null)
                return new(dir, helper, version, $"CEF {version} in {dir} does not offer the API version this engine needs ({ApiVersion}).");
            return new(dir, helper, version, null);
        }
        catch (Exception ex)
        {
            return new(null, null, null, "CEF could not be loaded: " + ex.Message);
        }
    }
}

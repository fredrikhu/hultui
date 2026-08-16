using System.Runtime.InteropServices;

namespace HultUI.Text;

/// <summary>
/// Locates the native ICU library and resolves its versioned exports — extracted
/// from LineBreaker so the BiDi analyzer binds through the same mechanism instead
/// of solving the suffix problem twice. Windows ships a combined icu.dll; Linux
/// suffixes every symbol with the major version (<c>ubrk_open_74</c>).
/// </summary>
internal static class IcuLibrary
{
    private static string _suffix = "";
    private static IntPtr _handle = IntPtr.Zero;
    private static bool _initialized;

    internal static void EnsureInitialized()
    {
        if (_initialized) return;

        // 1. The clean way first (Windows or well-configured Linux environments).
        if (NativeLibrary.TryLoad("icu", out _handle) || NativeLibrary.TryLoad("libicuuc.so", out _handle))
        {
            _suffix = DetectSuffixByProbing(_handle);
            _initialized = true;
            return;
        }

        // 2. Linux-specific: scan and extract the version from the filename.
        string[] searchPaths = { "/usr/lib64", "/usr/lib/x86_64-linux-gnu", "/usr/lib", "/lib64" };

        var bestLib = searchPaths
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "libicuuc.so.*"))
            .Select(path =>
            {
                int lastDot = path.LastIndexOf('.');
                string ver = path[(lastDot + 1)..];
                return new { Path = path, Success = int.TryParse(ver, out var verInt), Version = verInt };
            })
            .Where(x => x.Success)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();

        if (bestLib != null && NativeLibrary.TryLoad(bestLib.Path, out _handle))
        {
            _suffix = $"_{bestLib.Version}";
            _initialized = true;
            return;
        }

        throw new DllNotFoundException("Could not find ICU library with a valid version suffix.");
    }

    // Fallback for when "libicuuc.so" (a symlink) loads and the version isn't in the path.
    private static string DetectSuffixByProbing(IntPtr handle)
    {
        for (int v = 50; v < 100; v++)
            if (NativeLibrary.TryGetExport(handle, $"u_errorName_{v}", out _)) return $"_{v}";
        return "";
    }

    internal static T GetDelegate<T>(string name) where T : Delegate
    {
        EnsureInitialized();
        // Try with suffix first (Linux), then without (Windows / some distros).
        if (!NativeLibrary.TryGetExport(_handle, name + _suffix, out var ptr))
        {
            if (!NativeLibrary.TryGetExport(_handle, name, out ptr))
                throw new EntryPointNotFoundException($"Could not find {name} in ICU library.");
        }
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }
}

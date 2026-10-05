using System.Runtime.InteropServices;

namespace HultUI.Text;

/// <summary>
/// Locates the native ICU library and resolves its versioned exports — extracted
/// from LineBreaker so the BiDi analyzer binds through the same mechanism instead
/// of solving the suffix problem twice. Windows ships a combined icu.dll; Linux
/// suffixes every symbol with the major version (<c>ubrk_open_74</c>).
/// </summary>
/// <remarks>
/// Text is laid out on several threads at once, so the library is loaded once, under a lock,
/// and the handle and suffix published together when they are both known. The loader used to
/// write each attempt straight into the shared handle; on Linux the first attempt always fails,
/// so a thread still loading zeroed the handle after another had finished, and a third looked
/// its function up in no library ("Value cannot be null (Parameter 'handle')").
/// </remarks>
internal static class IcuLibrary
{
    /// <summary>The library and its symbols' version suffix.</summary>
    private sealed record Loaded(IntPtr Handle, string Suffix);

    private static readonly Lock Gate = new();
    private static volatile Loaded? _loaded;

    private static Loaded Library => _loaded ?? LoadOnce();

    private static Loaded LoadOnce()
    {
        lock (Gate)
            return _loaded ??= Load();
    }

    /// <summary>Forgets the loaded library, so a test can load it again as a fresh process would.</summary>
    internal static void Reset()
    {
        lock (Gate)
            _loaded = null;
    }

    private static Loaded Load()
    {
        // 1. The clean way first (Windows or well-configured Linux environments).
        if (NativeLibrary.TryLoad("icu", out var handle) || NativeLibrary.TryLoad("libicuuc.so", out handle))
            return new Loaded(handle, DetectSuffixByProbing(handle));

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

        if (bestLib != null && NativeLibrary.TryLoad(bestLib.Path, out handle))
            return new Loaded(handle, $"_{bestLib.Version}");

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
        var library = Library;
        // Try with suffix first (Linux), then without (Windows / some distros).
        if (!NativeLibrary.TryGetExport(library.Handle, name + library.Suffix, out var ptr))
        {
            if (!NativeLibrary.TryGetExport(library.Handle, name, out ptr))
                throw new EntryPointNotFoundException($"Could not find {name} in ICU library.");
        }
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }
}

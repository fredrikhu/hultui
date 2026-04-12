using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace HultUI.Text;

/// <summary>
/// Finds line break opportunities in text using ICU4N's line break rules.
/// </summary>
internal static class LineBreaker
{
    private static string _suffix = "";
    private static IntPtr _icuHandle = IntPtr.Zero;
    private static bool _initialized = false;

    private static void EnsureInitialized()
    {
        if (_initialized) return;

        // 1. Try the "Clean" way first (Windows or well-configured Linux environments)
        if (NativeLibrary.TryLoad("icu", out _icuHandle) || NativeLibrary.TryLoad("libicuuc.so", out _icuHandle))
        {
            _suffix = DetectSuffixByProbing(_icuHandle);
            _initialized = true;
            return;
        }

        // 2. Linux-Specific: Scan and extract version from filename
        string[] searchPaths = { "/usr/lib64", "/usr/lib/x86_64-linux-gnu", "/usr/lib", "/lib64" };
    
        var bestLib = searchPaths
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "libicuuc.so.*"))
            // Regex looks for the digits after ".so."
            .Select(path => {
                int lastDot = path.LastIndexOf('.');
                string ver = path.Substring(lastDot + 1);
                return new { Path = path, Success = int.TryParse(ver, out var verInt), Version = verInt };
            })
            .Where(x => x.Success)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();

        if (bestLib != null && NativeLibrary.TryLoad(bestLib.Path, out _icuHandle))
        {
            _suffix = $"_{bestLib.Version}";
            _initialized = true;
            return;
        }

        throw new DllNotFoundException("Could not find ICU library with a valid version suffix.");
    }

// Fallback for when we load "libicuuc.so" (a symlink) and don't know the version from the path
    private static string DetectSuffixByProbing(IntPtr handle)
    {
        for (int v = 50; v < 100; v++)
        {
            if (NativeLibrary.TryGetExport(handle, $"u_errorName_{v}", out _)) return $"_{v}";
        }
        return "";
    }
    
    public static unsafe List<int> FindBreakOpportunities(string text)
    {
        EnsureInitialized();
        var breaks = new List<int>();
        if (string.IsNullOrEmpty(text)) return breaks;

        fixed (char* pText = text)
        {
            int status = 0;
            
            var open = GetDelegate<ubrk_open_delegate>("ubrk_open");
            var next = GetDelegate<ubrk_next_delegate>("ubrk_next");
            var close = GetDelegate<ubrk_close_delegate>("ubrk_close");

            // 2 = UBRK_LINE
            IntPtr iterator = open(2, "", pText, text.Length, ref status);
            
            if (status > 0 || iterator == IntPtr.Zero) return breaks;

            try
            {
                int pos;
                while ((pos = next(iterator)) != -1) // -1 = UBRK_DONE
                {
                    breaks.Add(pos);
                }
            }
            finally
            {
                close(iterator);
            }
        }
        return breaks;
    }

    private static T GetDelegate<T>(string name) where T : Delegate
    {
        // Try with suffix first (Linux), then without (Windows/Some distros)
        if (!NativeLibrary.TryGetExport(_icuHandle, name + _suffix, out var ptr))
        {
            if (!NativeLibrary.TryGetExport(_icuHandle, name, out ptr))
                throw new EntryPointNotFoundException($"Could not find {name} in ICU library.");
        }
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    private unsafe delegate IntPtr ubrk_open_delegate(int type, string locale, char* text, int textLen, ref int status);
    private delegate int ubrk_next_delegate(IntPtr iter);
    private delegate void ubrk_close_delegate(IntPtr iter);}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CefOsr.Capi;

namespace CefOsr;

/// <summary>Loading libcef, strings, and our side of CEF's reference counting.</summary>
internal static unsafe class Native
{
    public static string Dir = "";
    public static nint Library;

    /// <summary>
    /// dlopen libcef.so from a directory and resolve every export. Nothing is called yet:
    /// the first call into the library must be <c>cef_api_hash</c>.
    /// </summary>
    public static void Load(string dir)
    {
        Dir = dir;
        Library = NativeLibrary.Load(Path.Combine(dir, "libcef.so"));
        Cef.Load(Library);
    }

    /// <summary>
    /// The versioned API's handshake: names the API version this program was generated
    /// against. Must be the first call; a library that no longer supports the version
    /// stops the process here rather than returning an error.
    /// </summary>
    public static string ApiHash(int version = CefApi.Version)
        => Marshal.PtrToStringUTF8((nint)Cef.cef_api_hash(version, 0)) ?? "";

    /// <summary>Which platform hash this library reports for a version (entry 1: platform).</summary>
    public static string ApiHashEntry(int version, int entry)
        => Marshal.PtrToStringUTF8((nint)Cef.cef_api_hash(version, entry)) ?? "";

    // ---- strings (UTF-16, the default CEF_STRING_TYPE) ----

    public static void Set(cef_string_utf16_t* s, string value)
    {
        fixed (char* c = value) Cef.cef_string_utf16_set(c, (ulong)value.Length, s, 1);
    }

    public static cef_string_utf16_t Make(string value)
    {
        cef_string_utf16_t s = default;
        Set(&s, value);
        return s;
    }

    public static string Get(cef_string_utf16_t* s)
        => s == null || s->str == null ? "" : new string(s->str, 0, (int)s->length);

    /// <summary>Reads and frees a <c>cef_string_userfree_t</c>, which the caller owns.</summary>
    public static string Take(cef_string_utf16_t* userfree)
    {
        if (userfree == null) return "";
        var r = Get(userfree);
        Cef.cef_string_userfree_utf16_free(userfree);
        return r;
    }

    // ---- reference counting ----
    //
    // CEF's rule, read from libcef_dll/cpptoc/cpptoc_ref_counted.h: every struct pointer that
    // crosses the boundary as an argument or a return value carries one reference, owned by
    // the side that receives it. `self` is the exception: no reference. So we add a
    // reference before handing CEF one of ours, and release every struct CEF hands us.
    //
    // Our structs are never freed in the spike; the counts are kept so question 10 can see
    // whether CEF has given back everything it took.

    public static readonly ConcurrentDictionary<nint, int> Refs = new();
    public static readonly ConcurrentDictionary<nint, string> Names = new();

    public static T* New<T>(string name) where T : unmanaged
    {
        var p = (T*)NativeMemory.AllocZeroed((nuint)sizeof(T));
        var b = (cef_base_ref_counted_t*)p;
        b->size = (ulong)sizeof(T);
        b->add_ref = &AddRefCb;
        b->release = &ReleaseCb;
        b->has_one_ref = &HasOneRefCb;
        b->has_at_least_one_ref = &HasAtLeastOneRefCb;
        Refs[(nint)p] = 0;
        Names[(nint)p] = name;
        return p;
    }

    /// <summary>Ours, about to be handed to CEF (as an argument or a return value).</summary>
    public static T* Give<T>(T* ours) where T : unmanaged
    {
        if (ours != null) Refs.AddOrUpdate((nint)ours, 1, (_, v) => v + 1);
        return ours;
    }

    /// <summary>CEF's, received as an argument or a return value: give its reference back.</summary>
    public static void Release(void* theirs)
    {
        var b = (cef_base_ref_counted_t*)theirs;
        if (b != null) b->release(b);
    }

    public static void AddRef(void* theirs)
    {
        var b = (cef_base_ref_counted_t*)theirs;
        if (b != null) b->add_ref(b);
    }

    [UnmanagedCallersOnly] private static void AddRefCb(cef_base_ref_counted_t* self)
        => Refs.AddOrUpdate((nint)self, 1, (_, v) => v + 1);

    [UnmanagedCallersOnly] private static int ReleaseCb(cef_base_ref_counted_t* self)
        => Refs.AddOrUpdate((nint)self, -1, (_, v) => v - 1) == 0 ? 1 : 0;

    [UnmanagedCallersOnly] private static int HasOneRefCb(cef_base_ref_counted_t* self)
        => Refs.TryGetValue((nint)self, out var v) && v == 1 ? 1 : 0;

    [UnmanagedCallersOnly] private static int HasAtLeastOneRefCb(cef_base_ref_counted_t* self)
        => Refs.TryGetValue((nint)self, out var v) && v >= 1 ? 1 : 0;

    public static string RefReport()
        => string.Join(", ", Refs.OrderBy(kv => Names[kv.Key]).Select(kv => $"{Names[kv.Key]}={kv.Value}"));

    // ---- main args: CEF wants the C argv ----

    public static cef_main_args_t MainArgs(string[] args)
    {
        var all = new[] { Environment.ProcessPath ?? "cefosr" }.Concat(args).ToArray();
        var argv = (byte**)NativeMemory.AllocZeroed((nuint)(sizeof(byte*) * (all.Length + 1)));
        for (int i = 0; i < all.Length; i++) argv[i] = (byte*)Marshal.StringToCoTaskMemUTF8(all[i]);
        return new cef_main_args_t { argc = all.Length, argv = argv };
    }

    // ---- threads and time, for the logs ----

    [DllImport("libc", EntryPoint = "gettid")] public static extern int GetTid();
    public static int MainTid;
    public static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static string Thread()
    {
        int t = GetTid();
        return t == MainTid ? "main" : $"tid {t}";
    }

    public static void Log(string text)
        => Console.WriteLine($"[{Clock.Elapsed.TotalMilliseconds,8:F1} ms {Thread(),-10}] {text}");
}

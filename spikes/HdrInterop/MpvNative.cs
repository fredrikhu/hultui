using System.Runtime.InteropServices;

namespace HdrInterop;

/// <summary>
/// The dozen libmpv entry points this spike needs, declared here rather than borrowed from
/// <c>HultUI.Media.Mpv</c>.
///
/// <para>That assembly keeps its interop <c>internal</c>, and widening it — or adding an
/// <c>InternalsVisibleTo</c> for a throwaway — would be a shipping library changing shape
/// to suit an experiment. Twenty-five lines of duplication is the cheaper price, and it
/// leaves the spike standing on its own.</para>
///
/// <para>Point <c>HULTUI_MPV_PATH</c> at a libmpv, or put one beside the binary.</para>
/// </summary>
internal static unsafe partial class MpvNative
{
    private const string Library = "libmpv";

    public const int MPV_EVENT_SHUTDOWN = 1, MPV_EVENT_END_FILE = 7, MPV_EVENT_FILE_LOADED = 8,
        MPV_EVENT_PLAYBACK_RESTART = 21;

    public const int MPV_RENDER_PARAM_API_TYPE = 1, MPV_RENDER_PARAM_OPENGL_INIT_PARAMS = 2,
        MPV_RENDER_PARAM_OPENGL_FBO = 3, MPV_RENDER_PARAM_FLIP_Y = 4;

    static MpvNative() => NativeLibrary.SetDllImportResolver(typeof(MpvNative).Assembly, Resolve);

    private static nint Resolve(string name, System.Reflection.Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Library) return 0;

        var names = OperatingSystem.IsWindows()
            ? new[] { "libmpv-2.dll", "mpv-2.dll" }
            : new[] { "libmpv.so.2", "libmpv.so" };

        var directories = new List<string?>();
        var hint = Environment.GetEnvironmentVariable("HULTUI_MPV_PATH");
        if (!string.IsNullOrEmpty(hint))
        {
            if (File.Exists(hint) && NativeLibrary.TryLoad(hint, out var direct)) return direct;
            directories.Add(hint);
        }
        directories.Add(AppContext.BaseDirectory);
        directories.Add(null);

        foreach (var directory in directories)
            foreach (var candidate in names)
            {
                var full = directory is null ? candidate : Path.Combine(directory, candidate);
                if (NativeLibrary.TryLoad(full, out var handle)) return handle;
            }
        return 0;
    }

    public static bool IsAvailable
    {
        get { try { return mpv_create() is var h && h != 0 && Free(h); } catch (DllNotFoundException) { return false; } }
    }

    private static bool Free(nint handle) { mpv_terminate_destroy(handle); return true; }

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_event { public int event_id; public int error; public ulong reply_userdata; public void* data; }

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_render_param { public int type; public void* data; }

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_opengl_init_params
    {
        public delegate* unmanaged[Cdecl]<void*, byte*, void*> get_proc_address;
        public void* get_proc_address_ctx;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_opengl_fbo { public int fbo; public int w; public int h; public int internal_format; }

    [LibraryImport(Library)] public static partial nint mpv_create();
    [LibraryImport(Library)] public static partial int mpv_initialize(nint handle);
    [LibraryImport(Library)] public static partial void mpv_terminate_destroy(nint handle);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int mpv_set_option_string(nint handle, string name, string data);
    [LibraryImport(Library)] public static partial mpv_event* mpv_wait_event(nint handle, double timeout);
    [LibraryImport(Library)] public static partial int mpv_command(nint handle, byte** args);
    [LibraryImport(Library)] public static partial int mpv_render_context_create(nint* result, nint handle, mpv_render_param* parameters);
    [LibraryImport(Library)] public static partial ulong mpv_render_context_update(nint context);
    [LibraryImport(Library)] public static partial int mpv_render_context_render(nint context, mpv_render_param* parameters);
    [LibraryImport(Library)] public static partial void mpv_render_context_free(nint context);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint mpv_get_property_string(nint handle, string name);
    [LibraryImport(Library)] public static partial void mpv_free(nint data);

    /// <summary>A property as a string, or null. mpv owns the buffer until mpv_free.</summary>
    public static string? Property(nint handle, string name)
    {
        nint p = mpv_get_property_string(handle, name);
        if (p == 0) return null;
        try { return Marshal.PtrToStringUTF8(p); }
        finally { mpv_free(p); }
    }

    /// <summary>A command as mpv takes it: a null-terminated array of UTF-8 strings.</summary>
    public static int Command(nint handle, params string[] args)
    {
        var bytes = new byte[args.Length][];
        for (int i = 0; i < args.Length; i++) bytes[i] = System.Text.Encoding.UTF8.GetBytes(args[i] + '\0');

        var pointers = stackalloc byte*[args.Length + 1];
        var pins = new GCHandle[args.Length];
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                pins[i] = GCHandle.Alloc(bytes[i], GCHandleType.Pinned);
                pointers[i] = (byte*)pins[i].AddrOfPinnedObject();
            }
            pointers[args.Length] = null;
            return mpv_command(handle, pointers);
        }
        finally
        {
            foreach (var pin in pins) if (pin.IsAllocated) pin.Free();
        }
    }
}

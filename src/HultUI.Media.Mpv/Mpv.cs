using System.Runtime.InteropServices;

namespace HultUI.Media.Mpv;

/// <summary>
/// Bindings to the parts of libmpv's client and render APIs the backend uses. The C
/// API is small and has been ABI-stable since mpv 0.30 (client API 2.x), which is why
/// these are written by hand: a few dozen lines, no generator, nothing to update when
/// mpv does.
/// </summary>
internal static unsafe partial class Mpv
{
    private const string Library = "mpv";

    static Mpv()
    {
        NativeLibrary.SetDllImportResolver(typeof(Mpv).Assembly, Resolve);
    }

    /// <summary>The names tried, in order, on this platform.</summary>
    internal static IEnumerable<string> CandidateNames()
    {
        if (OperatingSystem.IsWindows()) { yield return "libmpv-2.dll"; yield return "mpv-2.dll"; yield return "mpv-1.dll"; }
        else if (OperatingSystem.IsMacOS()) { yield return "libmpv.2.dylib"; yield return "libmpv.dylib"; }
        else { yield return "libmpv.so.2"; yield return "libmpv.so"; }
    }

    private static nint Resolve(string name, System.Reflection.Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Library) return 0;
        return TryLoad(out var handle) ? handle : 0;
    }

    /// <summary>Finds and loads the library without throwing.</summary>
    internal static bool TryLoad(out nint handle) => TryLoad(out handle, out _);

    /// <summary>
    /// Finds and loads the library without throwing, and when it fails says why.
    ///
    /// <para><paramref name="error"/> is the loader's own message for the first library file
    /// that exists and would not load — a missing dependency, most often. Without it, a
    /// <c>libmpv.so.2</c> sitting right beside the application on a machine without
    /// libpipewire was reported as "libmpv was not found", which sends a person looking for a
    /// file they already have. Null when the load succeeded or no file was found at all.</para>
    /// </summary>
    internal static bool TryLoad(out nint handle, out string? error)
    {
        var hint = Environment.GetEnvironmentVariable("HULTUI_MPV_PATH");
        var files = new List<string>();
        var directories = new List<string?>();
        if (!string.IsNullOrEmpty(hint))
        {
            if (File.Exists(hint)) files.Add(hint);
            directories.Add(hint);
        }
        directories.Add(AppContext.BaseDirectory);
        // Where a NuGet natives package lands in a framework-dependent build.
        directories.Add(Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native"));
        directories.Add(null);   // the loader's own search

        if (TryLoadFrom(files, directories, CandidateNames(), out handle, out error)) return true;

        // Last, the host's own probing for this assembly, which reads deps.json and
        // knows about runtimes/<rid>/native for a published application.
        foreach (var candidate in CandidateNames())
            if (NativeLibrary.TryLoad(candidate, typeof(Mpv).Assembly, null, out handle)) { error = null; return true; }
        return false;
    }

    /// <summary>
    /// Tries <paramref name="files"/>, then each name in each directory (null being the
    /// loader's own search). A file that exists and fails to load records the loader's message
    /// — the first one, being the most specific place the caller asked to look.
    /// </summary>
    internal static bool TryLoadFrom(IEnumerable<string> files, IEnumerable<string?> directories,
        IEnumerable<string> names, out nint handle, out string? error)
    {
        error = null;
        foreach (var file in files)
            if (TryOne(file, mustExist: true, out handle, ref error)) return true;

        foreach (var directory in directories)
            foreach (var name in names)
            {
                var file = directory is null ? name : Path.Combine(directory, name);
                if (TryOne(file, mustExist: directory is not null, out handle, ref error)) return true;
            }

        handle = 0;
        return false;

        static bool TryOne(string file, bool mustExist, out nint handle, ref string? error)
        {
            handle = 0;
            if (mustExist && !File.Exists(file)) return false;
            try
            {
                handle = NativeLibrary.Load(file);
                error = null;
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                // A bare name the loader could not find is not an error worth reporting: it
                // only means "not on the search path". A file that is there and failed is.
                if (mustExist) error ??= $"{file}: {ex.Message}";
                return false;
            }
        }
    }

    // ──── Client API ────

    public const int MPV_FORMAT_NONE = 0, MPV_FORMAT_STRING = 1, MPV_FORMAT_FLAG = 3, MPV_FORMAT_INT64 = 4, MPV_FORMAT_DOUBLE = 5;

    public const int MPV_EVENT_NONE = 0, MPV_EVENT_SHUTDOWN = 1, MPV_EVENT_LOG_MESSAGE = 2, MPV_EVENT_START_FILE = 6,
        MPV_EVENT_END_FILE = 7, MPV_EVENT_FILE_LOADED = 8, MPV_EVENT_VIDEO_RECONFIG = 17, MPV_EVENT_SEEK = 20,
        MPV_EVENT_PLAYBACK_RESTART = 21, MPV_EVENT_PROPERTY_CHANGE = 22;

    public const int MPV_END_FILE_REASON_EOF = 0, MPV_END_FILE_REASON_STOP = 2, MPV_END_FILE_REASON_QUIT = 3,
        MPV_END_FILE_REASON_ERROR = 4, MPV_END_FILE_REASON_REDIRECT = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_event
    {
        public int event_id;
        public int error;
        public ulong reply_userdata;
        public void* data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_event_property
    {
        public byte* name;
        public int format;
        public void* data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_event_log_message
    {
        public byte* prefix;
        public byte* level;
        public byte* text;
        public int log_level;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_event_end_file
    {
        public int reason;
        public int error;
        public long playlist_entry_id;
        public long playlist_insert_id;
        public int playlist_insert_num_entries;
    }

    [LibraryImport(Library)] public static partial nint mpv_create();
    [LibraryImport(Library)] public static partial int mpv_initialize(nint handle);
    [LibraryImport(Library)] public static partial void mpv_terminate_destroy(nint handle);
    [LibraryImport(Library)] public static partial void mpv_wakeup(nint handle);
    [LibraryImport(Library)] public static partial void mpv_free(void* data);
    [LibraryImport(Library)] public static partial byte* mpv_error_string(int error);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] public static partial int mpv_set_option_string(nint handle, string name, string data);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] public static partial int mpv_set_property_string(nint handle, string name, string data);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] public static partial int mpv_set_property(nint handle, string name, int format, void* data);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] public static partial int mpv_get_property(nint handle, string name, int format, void* data);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] public static partial byte* mpv_get_property_string(nint handle, string name);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] public static partial int mpv_observe_property(nint handle, ulong replyUserdata, string name, int format);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] public static partial int mpv_request_log_messages(nint handle, string minLevel);
    [LibraryImport(Library)] public static partial int mpv_command(nint handle, byte** args);
    [LibraryImport(Library)] public static partial mpv_event* mpv_wait_event(nint handle, double timeout);

    // ──── Render API ────

    // Values from render.h of client API 2.5; a wrong number here is a silent no-op
    // or a crash inside mpv, never a compile error, so they are checked against the
    // header, not remembered.
    public const int MPV_RENDER_PARAM_INVALID = 0, MPV_RENDER_PARAM_API_TYPE = 1, MPV_RENDER_PARAM_OPENGL_INIT_PARAMS = 2,
        MPV_RENDER_PARAM_OPENGL_FBO = 3, MPV_RENDER_PARAM_FLIP_Y = 4, MPV_RENDER_PARAM_ADVANCED_CONTROL = 10,
        MPV_RENDER_PARAM_DEPTH = 5, MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME = 12,
        MPV_RENDER_PARAM_SW_SIZE = 17, MPV_RENDER_PARAM_SW_FORMAT = 18, MPV_RENDER_PARAM_SW_STRIDE = 19,
        MPV_RENDER_PARAM_SW_POINTER = 20;

    public const ulong MPV_RENDER_UPDATE_FRAME = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_render_param
    {
        public int type;
        public void* data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_opengl_init_params
    {
        public delegate* unmanaged[Cdecl]<void*, byte*, void*> get_proc_address;
        public void* get_proc_address_ctx;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_opengl_fbo
    {
        public int fbo;
        public int w;
        public int h;
        public int internal_format;
    }

    [LibraryImport(Library)] public static partial int mpv_render_context_create(nint* result, nint handle, mpv_render_param* parameters);
    [LibraryImport(Library)] public static partial void mpv_render_context_set_update_callback(nint context, delegate* unmanaged[Cdecl]<void*, void> callback, void* userdata);
    [LibraryImport(Library)] public static partial ulong mpv_render_context_update(nint context);
    [LibraryImport(Library)] public static partial int mpv_render_context_render(nint context, mpv_render_param* parameters);
    [LibraryImport(Library)] public static partial void mpv_render_context_free(nint context);

    // ──── Helpers ────

    public static string ErrorString(int error)
    {
        var p = mpv_error_string(error);
        return p is null ? $"mpv error {error}" : Marshal.PtrToStringUTF8((nint)p) ?? $"mpv error {error}";
    }

    /// <summary>Reads a string property; null when unset or unavailable.</summary>
    public static string? GetString(nint handle, string name)
    {
        var p = mpv_get_property_string(handle, name);
        if (p is null) return null;
        try { return Marshal.PtrToStringUTF8((nint)p); }
        finally { mpv_free(p); }
    }

    public static long? GetInt64(nint handle, string name)
    {
        long value;
        return mpv_get_property(handle, name, MPV_FORMAT_INT64, &value) >= 0 ? value : null;
    }

    public static double? GetDouble(nint handle, string name)
    {
        double value;
        return mpv_get_property(handle, name, MPV_FORMAT_DOUBLE, &value) >= 0 ? value : null;
    }

    public static bool? GetFlag(nint handle, string name)
    {
        int value;
        return mpv_get_property(handle, name, MPV_FORMAT_FLAG, &value) >= 0 ? value != 0 : null;
    }

    public static int SetFlag(nint handle, string name, bool value)
    {
        int flag = value ? 1 : 0;
        return mpv_set_property(handle, name, MPV_FORMAT_FLAG, &flag);
    }

    public static int SetDouble(nint handle, string name, double value)
        => mpv_set_property(handle, name, MPV_FORMAT_DOUBLE, &value);

    /// <summary>Runs a command given as its argument list, UTF-8 marshalled and null-terminated.</summary>
    public static int Command(nint handle, params string[] args)
    {
        var pinned = new byte[args.Length][];
        var handles = new GCHandle[args.Length];
        var pointers = stackalloc byte*[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                pinned[i] = System.Text.Encoding.UTF8.GetBytes(args[i] + "\0");
                handles[i] = GCHandle.Alloc(pinned[i], GCHandleType.Pinned);
                pointers[i] = (byte*)handles[i].AddrOfPinnedObject();
            }
            pointers[args.Length] = null;
            return mpv_command(handle, pointers);
        }
        finally
        {
            foreach (var h in handles) if (h.IsAllocated) h.Free();
        }
    }
}

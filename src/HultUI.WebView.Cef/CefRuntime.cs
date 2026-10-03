using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// CEF itself, once per process. <see cref="CefEngine"/>s are handles onto it: CEF starts at
/// the first page's first placement and stops when the process exits, however many engines
/// an application (or a test run) creates and disposes in between.
/// </summary>
/// <remarks>
/// <para><b>Threads.</b> <c>cef_initialize</c> and <c>cef_shutdown</c> must be made on the
/// same thread, once each. That thread is the runtime's own ("CEF startup"): it initialises
/// and then waits for the process to end, so neither call depends on which thread an
/// application runs its UI on or how many times it creates one. With
/// <c>multi_threaded_message_loop</c>, CEF then runs its UI thread itself
/// (<c>CrBrowserMain</c>), where every callback arrives; HultUI's thread does no CEF work and
/// never waits for that thread (spike L0, question 4).</para>
/// <para><b>Exit.</b> On <see cref="AppDomain.ProcessExit"/> everything still waiting for an
/// answer from HultUI's thread is answered (<see cref="ShuttingDown"/>), because that thread
/// is not coming back, and then the startup thread shuts CEF down: about 20 ms, and the
/// browser's child processes leave with ours (question 10).</para>
/// </remarks>
internal static unsafe partial class CefRuntime
{
    private enum State { NotStarted, Starting, Running, Failed, ShutDown }

    private static readonly object Gate = new();
    private static State _state;
    private static string? _error;
    private static TaskCompletionSource<string?>? _started;
    private static readonly ManualResetEventSlim ShutdownRequested = new();
    private static readonly ManualResetEventSlim ShutdownDone = new();

    private static cef_app_t* _app;
    private static cef_browser_process_handler_t* _processHandler;
    private static string? _windowSystem;

    /// <summary>The window system CEF was started for ("wayland", "x11"), or null for none.</summary>
    public static string? WindowSystem => _windowSystem;

    /// <summary>The profile folder CEF runs with (<c>root_cache_path</c>), once started.</summary>
    public static string? Profile { get; private set; }

    /// <summary>The CEF directory and version in use, once started.</summary>
    public static string? Description { get; private set; }

    /// <summary>
    /// Raised on the exiting thread before <c>cef_shutdown</c>: answer whatever a CEF thread is
    /// waiting on HultUI's thread for, or the shutdown waits for a thread that waits for it.
    /// </summary>
    public static event Action? ShuttingDown;

    /// <summary>Raised on CEF's startup thread once <c>cef_shutdown</c> has returned, with how
    /// long it took. For tests and diagnostics.</summary>
    public static event Action<TimeSpan>? ShutDown;

    /// <summary>
    /// Starts CEF if it has not been, for <paramref name="windowSystem"/> (the SDL video driver:
    /// "wayland", "x11"; null for no window system, which renders headless). The first call
    /// decides the window system for the process. Completes, on CEF's UI thread, with null once
    /// the context is ready, or with why it is not.
    /// </summary>
    public static Task<string?> Start(string? windowSystem, string? userDataFolder = null, bool sandbox = true)
    {
        lock (Gate)
        {
            if (_started is not null) return _started.Task;
            _sandboxed = sandbox;
            _preferredRoot = Normalize(userDataFolder ?? DefaultRootCachePath());
            _started = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var probe = CefLibrary.Loaded;
            if (!probe.IsLoaded) { Fail(probe.Error!); return _started.Task; }
            // Where Chromium's sandbox cannot start, CEF ends the process: say why instead.
            if (Sandbox(probe.Directory!, sandbox) is { } why) { Fail(why); return _started.Task; }
            // Never silent: a process whose pages run without Chromium's sandbox says so.
            if (!sandbox) CefLog.Note("CEF starts without Chromium's sandbox: pages load only the application's own content, unless it allows untrusted content.");

            _windowSystem = windowSystem;
            _state = State.Starting;
            Description = $"CEF {probe.Version} from {probe.Directory}";
            var thread = new Thread(() => Run(probe)) { Name = "CEF startup", IsBackground = true };
            thread.Start();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
            return _started.Task;
        }
    }

    /// <summary>Why Chromium's sandbox cannot start here for an engine that wants it (or why
    /// CEF cannot run as asked), or null. See <see cref="CefSandbox"/>.</summary>
    internal static string? Sandbox(string cefDirectory, bool sandbox) =>
        // HULTUI_CEF_SANDBOX_CHECK=0: for a system where the check misjudges; CEF then
        // decides, and ends the process where the sandbox cannot start.
        Environment.GetEnvironmentVariable("HULTUI_CEF_SANDBOX_CHECK") == "0" ? null : CefSandbox.Problem(cefDirectory, noSandbox: !sandbox);

    /// <summary>Whether an engine with <paramref name="options"/> runs its pages in the sandbox:
    /// its option, unless the diagnostic <c>HULTUI_CEF_SWITCHES=no-sandbox</c> turns it off.
    /// One rule either way, the content limits included.</summary>
    internal static bool WantsSandbox(CefEngineOptions options) =>
        options.Sandbox && !ExtraSwitches(Environment.GetEnvironmentVariable("HULTUI_CEF_SWITCHES")).Any(s => s.Name == "no-sandbox");

    private static bool? _sandboxed;

    /// <summary>How CEF runs in this process: null until it has been started, then whether in
    /// Chromium's sandbox.</summary>
    public static bool? IsSandboxed { get { lock (Gate) return _started is null ? null : _sandboxed; } }

    /// <summary>
    /// CEF runs once per process, in one mode, which the engine that started it chose. Why an
    /// engine wanting <paramref name="wanted"/> cannot have it, or null.
    /// </summary>
    internal static string? ModeConflict(bool? running, bool wanted) => running switch
    {
        true when !wanted => "CEF already runs in this process with Chromium's sandbox, as the engine that started it " +
                             "asked: an engine without the sandbox cannot run beside it. Give every CefEngine the same Sandbox option.",
        false when wanted => "CEF already runs in this process without Chromium's sandbox, as the engine that started it " +
                             "asked: an engine with the sandbox cannot run beside it. Give every CefEngine the same Sandbox option.",
        _ => null,
    };

    /// <summary>Whether CEF has started and not stopped.</summary>
    public static bool IsRunning { get { lock (Gate) return _state == State.Running; } }

    /// <summary>Why CEF could not start, once it could not.</summary>
    public static string? Error => _error;

    private static void Fail(string error)
    {
        _state = State.Failed;
        _error = error;
        _started!.TrySetResult(error);
    }

    private static void Run(CefLibrary.Probe probe)
    {
        try
        {
            // The helper finds the library and the API version through the environment, which
            // children inherit: the native one, not .NET's copy of it.
            setenv("HULTUI_CEF_DIR", probe.Directory!, 1);
            setenv("HULTUI_CEF_API_VERSION", CefLibrary.ApiVersion.ToString(), 1);

            _app = CefObject.New<cef_app_t>();
            _app->on_before_command_line_processing = &OnBeforeCommandLine;
            _app->get_browser_process_handler = &GetProcessHandler;
            _processHandler = CefObject.New<cef_browser_process_handler_t>();
            _processHandler->on_context_initialized = &OnContextInitialized;

            var settings = new cef_settings_t { size = (ulong)sizeof(cef_settings_t) };
            settings.windowless_rendering_enabled = 1;
            settings.multi_threaded_message_loop = 1;
            // Without the sandbox when the starting engine asked; CEF hands it to its processes.
            settings.no_sandbox = _sandboxed == false ? 1 : 0;
            settings.log_severity = LogSeverity();
            // .NET's own signal handlers must survive CEF. Chromium otherwise resets SIGSEGV,
            // SIGILL, SIGFPE, SIGBUS, SIGABRT and SIGQUIT to their defaults, and .NET turns a
            // null dereference into a NullReferenceException through its SIGSEGV handler: an
            // application's first one would end the process. Measured, not assumed.
            settings.disable_signal_handlers = 1;
            var root = ProfileFor(_preferredRoot!, out _temporaryProfile);
            Directory.CreateDirectory(root);
            Profile = root;
            CefString.Set(&settings.root_cache_path, root);
            CefString.Set(&settings.cache_path, root);
            CefString.Set(&settings.browser_subprocess_path, probe.Helper!);

            var argv0 = Marshal.StringToCoTaskMemUTF8(Environment.ProcessPath ?? "hultui");
            var argv = stackalloc byte*[2] { (byte*)argv0, null };
            var mainArgs = new cef_main_args_t { argc = 1, argv = argv };
            // Even so, Chromium takes signals while it starts: SIGCHLD, and .NET's Process learns
            // of a child's exit only through its own handler (WaitForExit would never return, for
            // any process the application starts); and SIGINT, SIGTERM and SIGHUP for a shutdown
            // of its own, so Ctrl+C or a kill skipped .NET's (no ProcessExit, no CancelKeyPress).
            // Every signal's action from before is put back once CEF is up.
            EnsureDotNetsSigchld();
            _before = SaveSignals();
            int ok = Capi.cef_initialize(&mainArgs, &settings, CefObject.Give(_app), null);
            if (ok != 1)
            {
                lock (Gate) Fail($"CEF would not start ({Description}); its log, on standard error, says why.");
                return;
            }
        }
        catch (Exception ex)
        {
            lock (Gate) Fail("CEF would not start: " + ex.Message);
            return;
        }

        // The rest of this thread's life: wait for the process to end, then shut down here.
        ShutdownRequested.Wait();
        var clock = Stopwatch.StartNew();
        Capi.cef_shutdown();
        lock (Gate) _state = State.ShutDown;
        if (_temporaryProfile is { } temporary)
            try { Directory.Delete(temporary, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { ShutDown?.Invoke(clock.Elapsed); }
        catch (Exception ex) { CefLog.Error("shutting down", ex); }
        ShutdownDone.Set();
    }

    private static void Shutdown()
    {
        lock (Gate)
        {
            if (_state is not (State.Starting or State.Running)) return;
        }
        try { ShuttingDown?.Invoke(); }
        catch (Exception ex) { CefLog.Error("shutting down", ex); }
        ShutdownRequested.Set();
        ShutdownDone.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>Runs <paramref name="work"/> on CEF's UI thread (<c>CrBrowserMain</c>), soon.
    /// False when CEF is not running to take it.</summary>
    public static bool Post(Action work)
    {
        if (!IsRunning) return false;
        var task = CefObject.New<cef_task_t>(work);
        task->execute = &RunTask;
        bool posted = Capi.cef_post_task(cef_thread_id_t.TID_UI, CefObject.Give(task)) != 0;
        CefObject.Release(task);
        return posted;
    }

    [UnmanagedCallersOnly]
    private static void RunTask(cef_task_t* self)
    {
        try { CefObject.Owner<Action>(self)?.Invoke(); }
        catch (Exception ex) { CefLog.Error("posted task", ex); }
    }

    /// <summary>
    /// The browser process's switches. The window system decides the Ozone platform (spike L0,
    /// questions 1, 3 and 8): Wayland needs fractional scaling off, or the scale the engine
    /// gives is ignored; with no window system, headless, with ANGLE on GL for the GPU.
    /// </summary>
    internal static IEnumerable<(string Name, string? Value)> Switches(string? windowSystem, string? extra = null)
    {
        IEnumerable<(string Name, string? Value)> own = windowSystem switch
        {
            "wayland" => [("ozone-platform", "wayland"), ("disable-features", "WaylandFractionalScaleV1")],
            "x11" => [("ozone-platform", "x11")],
            _ => [("ozone-platform", "headless"), ("use-angle", "gl-egl")],
        };
        return own.Concat(ExtraSwitches(extra));
    }

    /// <summary>
    /// <c>HULTUI_CEF_SWITCHES</c>: more of Chromium's switches, for diagnosis and tests, as
    /// <c>name[=value]</c> separated by spaces (the tests' fake camera and microphone, say).
    /// </summary>
    internal static IEnumerable<(string Name, string? Value)> ExtraSwitches(string? text)
    {
        foreach (var part in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var s = part.TrimStart('-');
            int eq = s.IndexOf('=');
            if (s.Length > 0) yield return eq < 0 ? (s, null) : (s[..eq], s[(eq + 1)..]);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnBeforeCommandLine(cef_app_t* self, cef_string_utf16_t* processType, cef_command_line_t* cl)
    {
        try
        {
            if (CefString.Read(processType) != "") return;   // the browser process only
            foreach (var (name, value) in Switches(_windowSystem, Environment.GetEnvironmentVariable("HULTUI_CEF_SWITCHES")))
            {
                var n = CefString.Make(name);
                if (value is null) cl->append_switch(cl, &n);
                else
                {
                    // Feature lists are one switch: add to CEF's own rather than replace it.
                    var existing = cl->has_switch(cl, &n) != 0 ? CefString.Take(cl->get_switch_value(cl, &n)) : "";
                    var v = CefString.Make(existing.Length > 0 && name.EndsWith("-features") ? existing + "," + value : value);
                    cl->append_switch_with_value(cl, &n, &v);
                    CefString.Clear(&v);
                }
                CefString.Clear(&n);
            }
        }
        catch (Exception ex) { CefLog.Error("on_before_command_line_processing", ex); }
        finally { CefObject.Release(cl); }
    }

    [UnmanagedCallersOnly]
    private static cef_browser_process_handler_t* GetProcessHandler(cef_app_t* self)
    {
        try { return CefObject.Give(_processHandler); }
        catch (Exception ex) { CefLog.Error("get_browser_process_handler", ex); return null; }
    }

    [UnmanagedCallersOnly]
    private static void OnContextInitialized(cef_browser_process_handler_t* self)
    {
        try
        {
            lock (Gate)
            {
                if (_state != State.Starting) return;
                _state = State.Running;
            }
            if (_before is { } before) RestoreSignals(before);
            StringPages.Register();
            _started!.TrySetResult(null);
        }
        catch (Exception ex)
        {
            CefLog.Error("on_context_initialized", ex);
            _started?.TrySetResult("CEF started, but the engine could not set itself up: " + ex.Message);
        }
    }

    /// <summary>
    /// Where CEF keeps its profile when the page names no folder: per user, per application,
    /// beside WebView2's on Windows (<c>~/.local/share/&lt;app&gt;/CEF</c>).
    /// </summary>
    internal static string DefaultRootCachePath()
    {
        // The entry assembly, not the executable: under `dotnet app.dll` that is "dotnet", and
        // every such application would share one profile.
        var app = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name
                  ?? Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "HultUI";
        // DoNotVerify: without it .NET answers "" on Linux while ~/.local/share does not exist
        // yet, as on a new account, and the profile became a path relative to the working
        // directory (found on a fresh Debian, where it ran into the executable's own name).
        // The folder is made when CEF starts.
        var data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrEmpty(data))
            data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify), ".local", "share");
        return Path.Combine(data, app, "CEF");
    }

    private static string? _temporaryProfile;
    private static string? _preferredRoot;
    private static readonly Dictionary<string, nint> Contexts = new(StringComparer.Ordinal);

    internal static string Normalize(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

    /// <summary>
    /// The owner's rule for <see cref="WebViewOptions.UserDataFolder"/> (2026-10-02): CEF has one
    /// profile per process, and the first page's folder (the application's default when it
    /// names none) is it. A page naming the same folder, or none, shares it; a folder inside it
    /// gets a request context of its own (CEF requires a context's cache_path to be inside the
    /// root); any other folder is refused. <paramref name="actualRoot"/> is where the profile
    /// really is (a second instance's temporary one), and child folders are placed there.
    /// </summary>
    internal static (string? ChildPath, string? Error) ResolveFolder(string preferredRoot, string actualRoot, string? folder)
    {
        if (folder is null) return (null, null);
        var f = Normalize(folder);
        var root = Normalize(preferredRoot);
        if (f == root) return (null, null);
        if (f.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return (Path.Combine(actualRoot, Path.GetRelativePath(root, f)), null);
        return (null, $"CEF keeps one profile per process, and this one's is {root}: a page's UserDataFolder may be that " +
                      $"folder or one inside it, not {f}. The first page's folder (or the application's default) decides it.");
    }

    /// <summary><see cref="ResolveFolder(string, string, string?)"/> for this process, once CEF has started.</summary>
    public static (string? ChildPath, string? Error) ResolveFolder(string? folder)
        => ResolveFolder(_preferredRoot!, Profile!, folder);

    /// <summary>
    /// CEF's UI thread: the request context for a folder inside the profile, made the first time
    /// and kept for the process (string documents served in it too); null for the profile itself.
    /// </summary>
    public static cef_request_context_t* ContextFor(string? childPath)
    {
        if (childPath is null) return null;
        if (Contexts.TryGetValue(childPath, out var existing)) return (cef_request_context_t*)existing;
        Directory.CreateDirectory(childPath);
        var settings = new cef_request_context_settings_t { size = (ulong)sizeof(cef_request_context_settings_t) };
        CefString.Set(&settings.cache_path, childPath);
        var context = Capi.cef_request_context_create_context(&settings, null);
        CefString.Clear(&settings.cache_path);
        if (context == null) return null;
        StringPages.RegisterOn(context);
        Contexts[childPath] = (nint)context;
        return context;
    }

    /// <summary>
    /// The profile to start with: <paramref name="preferred"/>, unless another running process
    /// holds it. Chromium allows one browser per profile, and a second <c>cef_initialize</c> on
    /// a held one hands over to the first ("Opening in existing browser session") and fails: a
    /// second instance of the same application would have no pages. It gets a temporary profile
    /// instead, deleted when it exits; its pages work, without the first one's cookies and
    /// storage.
    /// </summary>
    internal static string ProfileFor(string preferred, out string? temporary)
    {
        temporary = null;
        if (HeldByAnother(preferred) is not { } holder) return preferred;
        temporary = Path.Combine(Path.GetTempPath(), $"hultui-cef-{Environment.ProcessId}");
        CefLog.Note($"{preferred} is in use by process {holder} (another instance of this application?); this one runs on a temporary profile, {temporary}.");
        return temporary;
    }

    /// <summary>The live process holding a profile's <c>SingletonLock</c> (a symlink to
    /// "host-pid"), or null when none does.</summary>
    internal static int? HeldByAnother(string profile, string? hostName = null)
    {
        try
        {
            var target = new FileInfo(Path.Combine(profile, "SingletonLock")).LinkTarget;
            if (target is null) return null;
            int dash = target.LastIndexOf('-');
            if (dash < 0 || !int.TryParse(target[(dash + 1)..], out var pid)) return null;
            // Chromium writes gethostname's whole answer; Environment.MachineName stops at the
            // first dot, so on host.example.com it never matched and a held profile was taken.
            if (target[..dash] != (hostName ?? HostName()) || pid == Environment.ProcessId) return null;
            return Directory.Exists($"/proc/{pid}") ? pid : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary><c>HULTUI_CEF_LOG</c>: verbose, info, warning, error or disable; CEF's default otherwise.</summary>
    private static cef_log_severity_t LogSeverity() => Environment.GetEnvironmentVariable("HULTUI_CEF_LOG") switch
    {
        "verbose" => cef_log_severity_t.LOGSEVERITY_VERBOSE,
        "info" => cef_log_severity_t.LOGSEVERITY_INFO,
        "warning" => cef_log_severity_t.LOGSEVERITY_WARNING,
        "error" => cef_log_severity_t.LOGSEVERITY_ERROR,
        "disable" => cef_log_severity_t.LOGSEVERITY_DISABLE,
        _ => cef_log_severity_t.LOGSEVERITY_DEFAULT,
    };

    /// <summary>The machine's host name as <c>gethostname</c> gives it, dots and all.</summary>
    internal static string HostName()
    {
        var buffer = stackalloc byte[256];
        if (gethostname(buffer, 256) != 0) return Environment.MachineName;
        return Marshal.PtrToStringUTF8((nint)buffer) ?? Environment.MachineName;
    }

    [LibraryImport("libc", EntryPoint = "gethostname")]
    private static partial int gethostname(byte* name, nuint length);

    private static Dictionary<int, byte[]>? _before;
    private static int _restoredAfterFirstPage;

    /// <summary>
    /// The first page's first frame, in this process (CEF's UI thread): put back once more what
    /// Chromium changed since the context came up. The browser and its GPU process come up
    /// only with the first page, and a handler either installs then would otherwise stand
    /// (review of 5.5, item 1).
    /// </summary>
    internal static void FirstPageDrew()
    {
        if (Interlocked.Exchange(ref _restoredAfterFirstPage, 1) != 0) return;
        var restored = _before is { } before ? RestoreSignals(before) : [];
        if (restored.Count > 0) CefLog.Info("put back after the first page: signals " + string.Join(", ", restored));
        FirstPageRestored?.Invoke(restored);
    }

    /// <summary>For tests: the restore after the first page has run, and what it put back.</summary>
    internal static event Action<List<int>>? FirstPageRestored;

    /// <summary>
    /// .NET installs its SIGCHLD handler only at an application's first <c>Process.Start</c>.
    /// Until then the saved action is the default, and a first process started while CEF starts
    /// installed .NET's handler only for Chromium to reset it to that same default, which then
    /// looked untouched: no child was reaped after (the process tests' rare timeouts). So the
    /// engine makes .NET install it first, by starting one trivial process.
    /// </summary>
    private static void EnsureDotNetsSigchld()
    {
        const int SIGCHLD = 17;
        if (Handler(SaveSignal(SIGCHLD)) is not (0 or 1)) return;   // a handler is there already
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/true") { UseShellExecute = false });
            p?.WaitForExit(2000);
        }
        catch (Exception ex) { CefLog.Note(".NET's SIGCHLD handler could not be installed first: " + ex.Message); }
    }

    /// <summary>Every signal's action, but those no process may change (KILL, STOP) and glibc's
    /// own two below SIGRTMIN.</summary>
    internal static Dictionary<int, byte[]> SaveSignals()
    {
        var saved = new Dictionary<int, byte[]>();
        for (int n = 1; n <= 64; n++)
            if (n is not (9 or 19 or 32 or 33)) saved[n] = SaveSignal(n);
        return saved;
    }

    /// <summary>
    /// Puts back a signal's action only where Chromium changed it: the handler there now is in
    /// libcef, or the default stands where there had been a handler. One installed meanwhile by
    /// anyone else stays: .NET installs its SIGCHLD handler at an application's first
    /// <c>Process.Start</c>, which may come while CEF starts, and putting back the default saved
    /// before it left every later child unreaped (the process tests' timeouts, review item 2).
    /// </summary>
    internal static List<int> RestoreSignals(Dictionary<int, byte[]> saved)
    {
        var restored = new List<int>();
        foreach (var (n, action) in saved)
        {
            var now = SaveSignal(n);
            if (now.AsSpan().SequenceEqual(action)) continue;
            if (ChromiumTook(Handler(now), Handler(action)))
            {
                RestoreSignal(n, action);
                restored.Add(n);
            }
        }
        return restored;
    }

    /// <summary>The handler a saved action holds: its first field.</summary>
    private static nint Handler(byte[] action) => (nint)BitConverter.ToInt64(action, 0);

    /// <summary>Whether the handler now there is Chromium's doing, rather than someone's since.</summary>
    internal static bool ChromiumTook(nint now, nint before)
    {
        const nint SigDfl = 0, SigIgn = 1;
        if (now is SigDfl or SigIgn) return before is not (SigDfl or SigIgn);   // a handler reset
        return ModuleOf(now) is { } module && Path.GetFileName(module).StartsWith("libcef", StringComparison.Ordinal);
    }

    private static string? ModuleOf(nint address)
    {
        DlInfo info;
        return dladdr(address, &info) != 0 && info.FileName != 0 ? Marshal.PtrToStringUTF8(info.FileName) : null;
    }

    private struct DlInfo
    {
        public nint FileName, Base, SymbolName, SymbolAddress;
    }

    [LibraryImport("libc")]
    private static partial int dladdr(nint address, DlInfo* info);

    /// <summary>The <c>sigaction</c> of a signal, as an opaque buffer (larger than glibc's struct).</summary>
    internal static byte[] SaveSignal(int signal)
    {
        var saved = new byte[512];
        fixed (byte* p = saved) sigaction(signal, null, p);
        return saved;
    }

    internal static void RestoreSignal(int signal, byte[] saved)
    {
        fixed (byte* p = saved) sigaction(signal, p, null);
    }

    [LibraryImport("libc", EntryPoint = "sigaction")]
    private static partial int sigaction(int signal, byte* act, byte* oldAct);

    [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int setenv(string name, string value, int overwrite);
}

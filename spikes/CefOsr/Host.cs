using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using CefOsr.Capi;
using static CefOsr.Native;

namespace CefOsr;

/// <summary>What a run is started with: <c>--name=value</c> on the command line.</summary>
internal sealed class Options
{
    public string CefDir = "/usr/lib64/cef";
    public string? Ozone;                  // --ozone=wayland|x11|headless; none: no flag
    public bool Sandbox = true;            // --sandbox=off: settings.no_sandbox
    public string? Subprocess;             // --subprocess=<path>|self: browser_subprocess_path
    public string Loop = "work";           // work: poll cef_do_message_loop_work; external; mt
    public bool ResourceDirs;              // --resource-dirs: set resources_ and locales_dir_path
    public string LogSeverity = "warning"; // verbose|info|warning|error|disable
    public string Cwd = "";                // --cwd=<dir> before cef_initialize
    public List<string> Switches = new();  // --switch=name[=value], to the browser's command line
    public List<string> Rest = new();

    public static Options Parse(IEnumerable<string> args)
    {
        var o = new Options();
        foreach (var a in args)
        {
            string Val() => a[(a.IndexOf('=') + 1)..];
            if (a.StartsWith("--cef-dir=")) o.CefDir = Val();
            else if (a.StartsWith("--ozone=")) o.Ozone = Val();
            else if (a == "--sandbox=off") o.Sandbox = false;
            else if (a.StartsWith("--subprocess=")) o.Subprocess = Val();
            else if (a.StartsWith("--loop=")) o.Loop = Val();
            else if (a == "--resource-dirs") o.ResourceDirs = true;
            else if (a.StartsWith("--log=")) o.LogSeverity = Val();
            else if (a.StartsWith("--cwd=")) o.Cwd = Val();
            else if (a.StartsWith("--switch=")) o.Switches.Add(Val());
            else o.Rest.Add(a);
        }
        // The C helper (question 1), when built beside the program as the README says.
        var helper = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "cefosr-helper-c"));
        if (o.Subprocess == null && File.Exists(helper)) o.Subprocess = helper;
        if (o.Subprocess == "self") o.Subprocess = null;
        return o;
    }
}

/// <summary>
/// One CEF process: the app and browser-process handler, <c>cef_initialize</c> with the
/// options, and one client (shared by every browser) whose handlers raise the events below.
/// Every callback releases the structs CEF hands it, after the event has run.
/// </summary>
internal static unsafe class Host
{
    public static Options Opt = new();
    public static readonly ConcurrentQueue<string> ChildLaunches = new();
    public static volatile bool ContextReady;

    // ---- events, raised on whatever thread CEF calls from ----
    public static Action<long>? PumpRequested;
    public static Action<nint>? AfterCreated;
    public static Action<nint>? BeforeClose;
    public static Action<nint, cef_paint_element_type_t, int, int, nint, int, nint>? Paint;
    public static Action<nint, cef_paint_element_type_t, nint>? AcceleratedPaint;
    public static Action<nint, bool>? PopupShow;
    public static Action<nint, cef_rect_t>? PopupSize;
    public static Action<nint, cef_range_t, cef_rect_t[]>? ImeRange;
    public static Action<nint, string>? Title;
    public static Action<nint, string>? Address;
    public static Action<nint, string, string, int>? ConsoleMessage;
    public static Action<nint, bool>? LoadingState;
    public static Action<nint, nint, int>? LoadEnd;
    public static Action<nint, int, string, string>? LoadError;
    public static Func<nint, nint, string, bool, bool>? BeforeBrowse;   // true cancels
    public static Func<nint, string, bool>? BeforePopup;                // true refuses
    public static Action<nint, cef_termination_status_t, int, string>? RenderProcessTerminated;
    public static Action<nint, bool>? TakeFocus;
    public static Action<nint, cef_key_event_t>? KeyEvent;
    public static Action<nint, cef_cursor_type_t>? Cursor;
    public static Func<nint>? AccessibilityHandler;

    // ---- the view, as get_view_rect and get_screen_info answer it ----
    public static int ViewWidth = 800, ViewHeight = 600;
    public static float Scale = 1.5f;
    public static int ScreenInfoCalls;

    public static cef_app_t* App;
    public static cef_browser_process_handler_t* ProcessHandler;
    public static cef_client_t* Client;
    public static cef_render_handler_t* RenderHandler;
    public static cef_life_span_handler_t* LifeSpan;
    public static cef_display_handler_t* Display;
    public static cef_load_handler_t* Load;
    public static cef_request_handler_t* Request;
    public static cef_focus_handler_t* Focus;
    public static cef_keyboard_handler_t* Keyboard;

    /// <summary>
    /// Loads libcef from the options' directory, names the API version, and runs
    /// <c>cef_initialize</c>. Returns its result (1 is success).
    /// </summary>
    public static int Initialize(string[] args, Options o)
    {
        Opt = o;
        MainTid = GetTid();
        Load(o.CefDir);
        var hash = ApiHash();
        Log($"libcef {o.CefDir}: cef_api_hash({CefApi.Version}) = {hash}, cef_api_version() = {Cef.cef_api_version()}");

        App = New<cef_app_t>("app");
        App->on_before_command_line_processing = &OnBeforeCommandLine;
        App->get_browser_process_handler = &GetProcessHandler;
        ProcessHandler = New<cef_browser_process_handler_t>("browser_process_handler");
        ProcessHandler->on_context_initialized = &OnContextInitialized;
        ProcessHandler->on_before_child_process_launch = &OnBeforeChildLaunch;
        ProcessHandler->on_schedule_message_pump_work = &OnSchedulePumpWork;
        BuildClient();

        var settings = new cef_settings_t { size = (ulong)sizeof(cef_settings_t) };
        settings.no_sandbox = o.Sandbox ? 0 : 1;
        settings.windowless_rendering_enabled = 1;
        settings.multi_threaded_message_loop = o.Loop == "mt" ? 1 : 0;
        settings.external_message_pump = o.Loop == "external" ? 1 : 0;
        settings.log_severity = o.LogSeverity switch
        {
            "verbose" => cef_log_severity_t.LOGSEVERITY_VERBOSE,
            "info" => cef_log_severity_t.LOGSEVERITY_INFO,
            "error" => cef_log_severity_t.LOGSEVERITY_ERROR,
            "disable" => cef_log_severity_t.LOGSEVERITY_DISABLE,
            _ => cef_log_severity_t.LOGSEVERITY_WARNING,
        };
        var root = Path.Combine(Path.GetTempPath(), "cefosr-" + Environment.ProcessId);
        Set(&settings.root_cache_path, root);
        Set(&settings.cache_path, Path.Combine(root, "profile"));
        if (o.Subprocess != null) Set(&settings.browser_subprocess_path, o.Subprocess);
        if (o.ResourceDirs)
        {
            var res = Directory.Exists(Path.Combine(o.CefDir, "..", "Resources"))
                ? Path.GetFullPath(Path.Combine(o.CefDir, "..", "Resources")) : o.CefDir;
            Set(&settings.resources_dir_path, res);
            Set(&settings.locales_dir_path, Path.Combine(res, "locales"));
            Log($"resources_dir_path = {res}");
        }
        if (o.Cwd != "") Directory.SetCurrentDirectory(o.Cwd);

        var mainArgs = MainArgs(args);
        int ok = Cef.cef_initialize(&mainArgs, &settings, Give(App), null);
        Log($"cef_initialize = {ok}");
        return ok;
    }

    /// <summary>The subprocess entry: the same handshake, then <c>cef_execute_process</c>.</summary>
    public static int ExecuteProcess(string[] args, string cefDir)
    {
        Load(cefDir);
        ApiHash();
        var mainArgs = MainArgs(args);
        return Cef.cef_execute_process(&mainArgs, null, null);
    }

    /// <summary>Runs the message loop the options chose until <paramref name="done"/> says so.</summary>
    public static void Pump(Func<bool> done, int timeoutMs = 30000)
    {
        var end = Clock.ElapsedMilliseconds + timeoutMs;
        while (!done() && Clock.ElapsedMilliseconds < end)
        {
            if (Opt.Loop == "mt") { System.Threading.Thread.Sleep(5); continue; }
            if (Opt.Loop == "external") { PumpExternal(5); continue; }
            Cef.cef_do_message_loop_work();
            System.Threading.Thread.Sleep(2);
        }
        if (!done()) Log($"Pump: timed out after {timeoutMs} ms");
    }

    // external_message_pump: do the work when CEF asks, at the time it asks for.
    private static long _pumpDue = long.MaxValue;
    private static readonly AutoResetEvent PumpWake = new(false);
    public static int PumpCalls, PumpRequests;

    private static void PumpExternal(int maxWaitMs)
    {
        long now = Clock.ElapsedMilliseconds, due = Interlocked.Read(ref _pumpDue);
        if (due > now) PumpWake.WaitOne((int)Math.Min(maxWaitMs, Math.Max(0, due - now)));
        if (Interlocked.Read(ref _pumpDue) <= Clock.ElapsedMilliseconds)
        {
            Interlocked.Exchange(ref _pumpDue, long.MaxValue);
            PumpCalls++;
            Cef.cef_do_message_loop_work();
        }
    }

    public static cef_browser_t* CreateBrowser(string url, int frameRate = 60, bool sharedTexture = false, bool sync = true)
    {
        var info = new cef_window_info_t { size = (ulong)sizeof(cef_window_info_t) };
        info.windowless_rendering_enabled = 1;
        info.shared_texture_enabled = sharedTexture ? 1 : 0;
        info.runtime_style = cef_runtime_style_t.CEF_RUNTIME_STYLE_ALLOY;
        var bs = new cef_browser_settings_t { size = (ulong)sizeof(cef_browser_settings_t) };
        bs.windowless_frame_rate = frameRate;
        var u = Make(url);
        if (!sync)
        {
            int r = Cef.cef_browser_host_create_browser(&info, Give(Client), &u, &bs, null, null);
            Log($"create_browser = {r}");
            return null;
        }
        var b = Cef.cef_browser_host_create_browser_sync(&info, Give(Client), &u, &bs, null, null);
        Log($"create_browser_sync = {(nint)b:x}");
        return b;
    }

    public static cef_browser_host_t* HostOf(cef_browser_t* b) => b->get_host(b);
    public static int Id(cef_browser_t* b) => b->get_identifier(b);

    public static void WithHost(cef_browser_t* b, Action<nint> act)
    {
        var h = b->get_host(b);
        try { act((nint)h); } finally { Release(h); }
    }

    public static string UrlOf(cef_frame_t* f) => Take(f->get_url(f));

    /// <summary>Closes a browser and pumps until <c>on_before_close</c>, then drops our reference.</summary>
    public static void Close(cef_browser_t* b, bool force = true)
    {
        int id = Id(b);
        bool closed = false;
        Action<nint> onClose = x => { if (Id((cef_browser_t*)x) == id) closed = true; };
        BeforeClose += onClose;
        var h = b->get_host(b);
        h->close_browser(h, force ? 1 : 0);
        Release(h);
        Pump(() => closed, 10000);
        BeforeClose -= onClose;
        Release(b);
    }

    // frame->get_text: the text arrives later, through a visitor.
    private static string? _text;

    public static string GetText(cef_frame_t* f)
    {
        _text = null;
        var v = New<cef_string_visitor_t>("string_visitor");
        v->visit = &OnVisit;
        f->get_text(f, Give(v));
        Pump(() => _text != null, 10000);
        return _text ?? "";
    }

    [UnmanagedCallersOnly]
    private static void OnVisit(cef_string_visitor_t* self, cef_string_utf16_t* s) => _text = Get(s);

    public static string DataUrl(string html)
        => "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(html));

    // ---- children: how many renderer, GPU, utility and zygote processes are alive ----
    public static string[] Children()
    {
        var mine = Environment.ProcessId.ToString();
        var r = new List<string>();
        foreach (var d in Directory.GetDirectories("/proc"))
        {
            var pid = Path.GetFileName(d);
            if (!int.TryParse(pid, out _) || pid == mine) continue;
            try
            {
                var cmd = File.ReadAllText(Path.Combine(d, "cmdline")).Replace('\0', ' ');
                if (cmd.Contains("--type=") && cmd.Contains("cefosr-" + mine))
                    r.Add($"{pid} {System.Text.RegularExpressions.Regex.Match(cmd, @"--type=\S+").Value}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return r.ToArray();
    }

    // ================= callbacks =================

    [UnmanagedCallersOnly]
    private static void OnBeforeCommandLine(cef_app_t* self, cef_string_utf16_t* processType, cef_command_line_t* cl)
    {
        var type = Get(processType);
        if (type == "")
        {
            void Append(string name, string? value)
            {
                var n = Make(name);
                if (value == null) cl->append_switch(cl, &n);
                else { var v = Make(value); cl->append_switch_with_value(cl, &n, &v); }
            }
            if (Opt.Ozone != null) Append("ozone-platform", Opt.Ozone);
            foreach (var s in Opt.Switches)
            {
                var i = s.IndexOf('=');
                Append(i < 0 ? s : s[..i], i < 0 ? null : s[(i + 1)..]);
            }
            Log("browser command line: " + Take(cl->get_command_line_string(cl)));
        }
        Release(cl);
    }

    [UnmanagedCallersOnly]
    private static cef_browser_process_handler_t* GetProcessHandler(cef_app_t* self) => Give(ProcessHandler);

    [UnmanagedCallersOnly]
    private static void OnContextInitialized(cef_browser_process_handler_t* self)
    {
        Log("on_context_initialized");
        ContextReady = true;
    }

    [UnmanagedCallersOnly]
    private static void OnBeforeChildLaunch(cef_browser_process_handler_t* self, cef_command_line_t* cl)
    {
        ChildLaunches.Enqueue(Take(cl->get_command_line_string(cl)));
        Release(cl);
    }

    [UnmanagedCallersOnly]
    private static void OnSchedulePumpWork(cef_browser_process_handler_t* self, long delayMs)
    {
        Interlocked.Increment(ref PumpRequests);
        long due = Clock.ElapsedMilliseconds + Math.Max(0, delayMs);
        long cur;
        do { cur = Interlocked.Read(ref _pumpDue); }
        while (due < cur && Interlocked.CompareExchange(ref _pumpDue, due, cur) != cur);
        PumpWake.Set();
        PumpRequested?.Invoke(delayMs);
    }

    private static void BuildClient()
    {
        Client = New<cef_client_t>("client");
        Client->get_render_handler = &GetRenderHandler;
        Client->get_life_span_handler = &GetLifeSpan;
        Client->get_display_handler = &GetDisplay;
        Client->get_load_handler = &GetLoad;
        Client->get_request_handler = &GetRequest;
        Client->get_focus_handler = &GetFocus;
        Client->get_keyboard_handler = &GetKeyboard;

        RenderHandler = New<cef_render_handler_t>("render_handler");
        RenderHandler->get_view_rect = &GetViewRect;
        RenderHandler->get_screen_info = &GetScreenInfo;
        RenderHandler->on_paint = &OnPaint;
        RenderHandler->on_accelerated_paint = &OnAcceleratedPaint;
        RenderHandler->on_popup_show = &OnPopupShow;
        RenderHandler->on_popup_size = &OnPopupSize;
        RenderHandler->on_ime_composition_range_changed = &OnImeRange;
        RenderHandler->get_accessibility_handler = &GetAccessibilityHandler;

        LifeSpan = New<cef_life_span_handler_t>("life_span_handler");
        LifeSpan->on_after_created = &OnAfterCreated;
        LifeSpan->on_before_close = &OnBeforeClose;
        LifeSpan->on_before_popup = &OnBeforePopup;

        Display = New<cef_display_handler_t>("display_handler");
        Display->on_title_change = &OnTitle;
        Display->on_address_change = &OnAddress;
        Display->on_console_message = &OnConsole;
        Display->on_cursor_change = &OnCursor;

        Load = New<cef_load_handler_t>("load_handler");
        Load->on_loading_state_change = &OnLoadingState;
        Load->on_load_end = &OnLoadEnd;
        Load->on_load_error = &OnLoadError;

        Request = New<cef_request_handler_t>("request_handler");
        Request->on_before_browse = &OnBeforeBrowse;
        Request->on_render_process_terminated = &OnRenderTerminated;

        Focus = New<cef_focus_handler_t>("focus_handler");
        Focus->on_take_focus = &OnTakeFocus;

        Keyboard = New<cef_keyboard_handler_t>("keyboard_handler");
        Keyboard->on_key_event = &OnKeyEvent;
    }

    [UnmanagedCallersOnly] private static cef_render_handler_t* GetRenderHandler(cef_client_t* s) => Give(RenderHandler);
    [UnmanagedCallersOnly] private static cef_life_span_handler_t* GetLifeSpan(cef_client_t* s) => Give(LifeSpan);
    [UnmanagedCallersOnly] private static cef_display_handler_t* GetDisplay(cef_client_t* s) => Give(Display);
    [UnmanagedCallersOnly] private static cef_load_handler_t* GetLoad(cef_client_t* s) => Give(Load);
    [UnmanagedCallersOnly] private static cef_request_handler_t* GetRequest(cef_client_t* s) => Give(Request);
    [UnmanagedCallersOnly] private static cef_focus_handler_t* GetFocus(cef_client_t* s) => Give(Focus);
    [UnmanagedCallersOnly] private static cef_keyboard_handler_t* GetKeyboard(cef_client_t* s) => Give(Keyboard);

    [UnmanagedCallersOnly]
    private static void GetViewRect(cef_render_handler_t* s, cef_browser_t* b, cef_rect_t* r)
    {
        *r = new cef_rect_t { x = 0, y = 0, width = ViewWidth, height = ViewHeight };
        Release(b);
    }

    [UnmanagedCallersOnly]
    private static int GetScreenInfo(cef_render_handler_t* s, cef_browser_t* b, cef_screen_info_t* info)
    {
        if (Interlocked.Increment(ref ScreenInfoCalls) <= 3)
            Log($"get_screen_info #{ScreenInfoCalls}: struct size {info->size}, answering scale {Scale}");
        info->device_scale_factor = Scale;
        info->depth = 24;
        info->depth_per_component = 8;
        info->rect = new cef_rect_t { width = 1920, height = 1080 };
        info->available_rect = info->rect;
        Release(b);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnPaint(cef_render_handler_t* s, cef_browser_t* b, cef_paint_element_type_t type,
        ulong dirtyCount, cef_rect_t* dirty, void* buffer, int width, int height)
    {
        Paint?.Invoke((nint)b, type, width, height, (nint)buffer, (int)dirtyCount, (nint)dirty);
        Release(b);
    }

    [UnmanagedCallersOnly]
    private static void OnAcceleratedPaint(cef_render_handler_t* s, cef_browser_t* b, cef_paint_element_type_t type,
        ulong dirtyCount, cef_rect_t* dirty, cef_accelerated_paint_info_t* info)
    {
        AcceleratedPaint?.Invoke((nint)b, type, (nint)info);
        Release(b);
    }

    [UnmanagedCallersOnly]
    private static void OnPopupShow(cef_render_handler_t* s, cef_browser_t* b, int show)
    {
        PopupShow?.Invoke((nint)b, show != 0);
        Release(b);
    }

    [UnmanagedCallersOnly]
    private static void OnPopupSize(cef_render_handler_t* s, cef_browser_t* b, cef_rect_t* r)
    {
        PopupSize?.Invoke((nint)b, *r);
        Release(b);
    }

    [UnmanagedCallersOnly]
    private static void OnImeRange(cef_render_handler_t* s, cef_browser_t* b, cef_range_t* range, ulong count, cef_rect_t* bounds)
    {
        var rects = new cef_rect_t[count];
        for (int i = 0; i < (int)count; i++) rects[i] = bounds[i];
        ImeRange?.Invoke((nint)b, *range, rects);
        Release(b);
    }

    [UnmanagedCallersOnly]
    private static cef_accessibility_handler_t* GetAccessibilityHandler(cef_render_handler_t* s)
        => Give((cef_accessibility_handler_t*)(AccessibilityHandler?.Invoke() ?? 0));

    [UnmanagedCallersOnly]
    private static void OnAfterCreated(cef_life_span_handler_t* s, cef_browser_t* b)
    {
        Log($"on_after_created: browser {Id(b)}");
        AfterCreated?.Invoke((nint)b);
        Release(b);
    }

    [UnmanagedCallersOnly]
    private static void OnBeforeClose(cef_life_span_handler_t* s, cef_browser_t* b)
    {
        Log($"on_before_close: browser {Id(b)}");
        BeforeClose?.Invoke((nint)b);
        Release(b);
    }

    [UnmanagedCallersOnly]
    private static int OnBeforePopup(cef_life_span_handler_t* s, cef_browser_t* b, cef_frame_t* f, int popupId,
        cef_string_utf16_t* url, cef_string_utf16_t* name, cef_window_open_disposition_t disposition, int userGesture,
        cef_popup_features_t* features, cef_window_info_t* info, cef_client_t** client, cef_browser_settings_t* settings,
        cef_dictionary_value_t** extra, int* noJs)
    {
        var u = Get(url);
        Log($"on_before_popup: {u} disposition {disposition} gesture {userGesture}");
        bool refuse = BeforePopup?.Invoke((nint)b, u) ?? true;
        Release(b);
        Release(f);
        return refuse ? 1 : 0;
    }

    [UnmanagedCallersOnly]
    private static void OnTitle(cef_display_handler_t* s, cef_browser_t* b, cef_string_utf16_t* title)
    {
        Title?.Invoke((nint)b, Get(title));
        Release(b);
    }

    [UnmanagedCallersOnly]
    private static void OnAddress(cef_display_handler_t* s, cef_browser_t* b, cef_frame_t* f, cef_string_utf16_t* url)
    {
        Address?.Invoke((nint)b, Get(url));
        Release(b);
        Release(f);
    }

    [UnmanagedCallersOnly]
    private static int OnConsole(cef_display_handler_t* s, cef_browser_t* b, cef_log_severity_t level,
        cef_string_utf16_t* message, cef_string_utf16_t* source, int line)
    {
        var m = Get(message);
        if (ConsoleMessage != null) ConsoleMessage((nint)b, m, Get(source), line);
        else Log($"console: {m}");
        Release(b);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static int OnCursor(cef_display_handler_t* s, cef_browser_t* b, ulong cursor, cef_cursor_type_t type, cef_cursor_info_t* info)
    {
        Cursor?.Invoke((nint)b, type);
        Release(b);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnLoadingState(cef_load_handler_t* s, cef_browser_t* b, int loading, int back, int fwd)
    {
        LoadingState?.Invoke((nint)b, loading != 0);
        Release(b);
    }

    [UnmanagedCallersOnly]
    private static void OnLoadEnd(cef_load_handler_t* s, cef_browser_t* b, cef_frame_t* f, int status)
    {
        LoadEnd?.Invoke((nint)b, (nint)f, status);
        Release(b);
        Release(f);
    }

    [UnmanagedCallersOnly]
    private static void OnLoadError(cef_load_handler_t* s, cef_browser_t* b, cef_frame_t* f, cef_errorcode_t code,
        cef_string_utf16_t* text, cef_string_utf16_t* url)
    {
        var t = Get(text); var u = Get(url);
        Log($"on_load_error: {code} {t} {u}");
        LoadError?.Invoke((nint)b, (int)code, t, u);
        Release(b);
        Release(f);
    }

    [UnmanagedCallersOnly]
    private static int OnBeforeBrowse(cef_request_handler_t* s, cef_browser_t* b, cef_frame_t* f, cef_request_t* req,
        int userGesture, int isRedirect)
    {
        var url = Take(req->get_url(req));
        bool cancel = BeforeBrowse?.Invoke((nint)b, (nint)f, url, userGesture != 0) ?? false;
        Release(b);
        Release(f);
        Release(req);
        return cancel ? 1 : 0;
    }

    [UnmanagedCallersOnly]
    private static void OnRenderTerminated(cef_request_handler_t* s, cef_browser_t* b, cef_termination_status_t status,
        int code, cef_string_utf16_t* text)
    {
        RenderProcessTerminated?.Invoke((nint)b, status, code, Get(text));
        Release(b);
    }

    [UnmanagedCallersOnly]
    private static void OnTakeFocus(cef_focus_handler_t* s, cef_browser_t* b, int next)
    {
        TakeFocus?.Invoke((nint)b, next != 0);
        Release(b);
    }

    [UnmanagedCallersOnly]
    private static int OnKeyEvent(cef_keyboard_handler_t* s, cef_browser_t* b, cef_key_event_t* e, void* osEvent)
    {
        KeyEvent?.Invoke((nint)b, *e);
        Release(b);
        return 0;
    }
}

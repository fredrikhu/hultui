using HultUI.WebView.WebView2.Interop;

namespace HultUI.WebView.WebView2;

/// <summary>
/// Microsoft Edge WebView2, composited: every page renders offscreen into a visual that is
/// captured and drawn by its <see cref="WebViewElement"/>, so HultUI's own menus, dialogs
/// and tooltips draw over it. See docs/design/WEBVIEW.md.
/// </summary>
/// <remarks>
/// <para>Needs Windows 10 1903 or later (for capturing a visual), the WebView2 Runtime —
/// shipped with Windows 11, installable elsewhere from <see cref="RuntimeDownload"/> — and a
/// UI thread that is a single-threaded apartment: put <c>[STAThread]</c> on <c>Main</c>.
/// <see cref="IWebEngine.Availability"/> says which is missing without throwing.</para>
/// <para>One browser process serves every view with the same user data folder; the engine
/// keeps those environments and releases them when disposed. Dispose it after the
/// controllers.</para>
/// </remarks>
public sealed unsafe class WebView2Engine : IWebEngine
{
    /// <summary>Where the WebView2 Runtime comes from.</summary>
    public static readonly Uri RuntimeDownload = new("https://developer.microsoft.com/microsoft-edge/webview2/");

    private readonly Dictionary<string, Environment> _environments = new(StringComparer.OrdinalIgnoreCase);
    private WebEngineAvailability? _availability;
    private bool _disposed;

    public string Name => "WebView2";

    public WebEngineAvailability Availability => _availability ??= Probe();

    /// <summary>The installed runtime's version, or null when there is none.</summary>
    public string? RuntimeVersion { get; private set; }

    public IWebEngineView CreateView(WebViewOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new WebView2View(this, options);
    }

    private WebEngineAvailability Probe()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
            return WebEngineAvailability.Missing("WebView2 pages need Windows 10 version 1903 or later.");
        if (!Native.TryLoadLoader(out _, out var loaderError))
            return WebEngineAvailability.Missing("WebView2Loader.dll could not be loaded. " + loaderError);

        char* version = null;
        int hr = Native.GetAvailableCoreWebView2BrowserVersionString(null, &version);
        if (version is not null)
        {
            RuntimeVersion = new string(version);
            System.Runtime.InteropServices.Marshal.FreeCoTaskMem((nint)version);
        }
        if (hr < 0 || RuntimeVersion is null)
            return WebEngineAvailability.Missing("The Microsoft Edge WebView2 Runtime is not installed.", RuntimeDownload);
        return WebEngineAvailability.Available;
    }

    /// <summary>
    /// The folder a view keeps its profile in when the options name none: per user, per
    /// application. WebView2's own default is beside the executable, which an installed
    /// application cannot write to.
    /// </summary>
    internal static string DefaultUserDataFolder()
    {
        var app = Path.GetFileNameWithoutExtension(System.Environment.ProcessPath) ?? "HultUI";
        return Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), app, "WebView2");
    }

    /// <summary>
    /// Hands <paramref name="then"/> the environment for a user data folder, creating it the
    /// first time. UI thread; the answer comes on the UI thread, now or when creation ends.
    /// </summary>
    internal void WithEnvironment(string userDataFolder, Action<nint, WebFailure, string?> then)
    {
        if (_disposed) { then(0, WebFailure.InitializationFailed, "The engine was disposed."); return; }
        if (!_environments.TryGetValue(userDataFolder, out var env))
        {
            env = new Environment();
            _environments[userDataFolder] = env;
            Create(userDataFolder, env);
        }
        if (env.Pointer != 0) then(env.Pointer, default, null);
        else if (env.Error is not null) then(0, env.Failure, env.Error);
        else env.Waiting.Add(then);
    }

    private void Create(string folder, Environment env)
    {
        ComHandler? handler = null;
        handler = ComHandler.ForResult(ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler.IID, (hr, created) =>
        {
            handler!.Release();
            if (hr >= 0 && created != 0)
            {
                Com.AddRef(created);
                env.Pointer = created;
            }
            else Fail(env, hr);
            env.Notify();
        });

        int hr;
        fixed (char* folderPtr = folder)
            hr = Native.CreateCoreWebView2EnvironmentWithOptions(null, folderPtr, 0, handler.Pointer);
        if (hr < 0)
        {
            handler.Release();
            Fail(env, hr);
            env.Notify();
        }
    }

    private static void Fail(Environment env, int hr)
    {
        // HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND): no runtime where the loader looked.
        env.Failure = hr == unchecked((int)0x80070002) ? WebFailure.RuntimeMissing : WebFailure.InitializationFailed;
        env.Error = env.Failure == WebFailure.RuntimeMissing
            ? "The Microsoft Edge WebView2 Runtime is not installed."
            : $"WebView2 would not start (0x{hr:X8}).";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var env in _environments.Values)
            Com.Release(ref env.Pointer);
        _environments.Clear();
    }

    private sealed class Environment
    {
        public nint Pointer;
        public string? Error;
        public WebFailure Failure;
        public readonly List<Action<nint, WebFailure, string?>> Waiting = [];

        public void Notify()
        {
            var waiting = Waiting.ToArray();
            Waiting.Clear();
            foreach (var then in waiting) then(Pointer, Failure, Error);
        }
    }
}

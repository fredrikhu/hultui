using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// The Chromium Embedded Framework, rendered off-screen and drawn by HultUI: the Linux engine.
/// Every page is composited like a video frame, so HultUI's own menus, dialogs and tooltips
/// draw over it. See docs/design/WEBVIEW.md and docs/design/WEBVIEW-LINUX-BRIEF.md.
/// </summary>
/// <remarks>
/// <para><b>Where CEF comes from.</b> A directory holding <c>libcef.so</c> and its resources,
/// looked for in <c>HULTUI_CEF_DIR</c>, the application's <c>cef/</c>, its
/// <c>runtimes/&lt;rid&gt;/native/cef/</c>, then the distribution's (<c>/usr/lib64/cef</c> on
/// Fedora: <c>sudo dnf install cef</c>). Any CEF that offers this engine's API version will
/// do (146 and later). The helper the browser starts its child processes from,
/// <c>hultui-cef-helper</c>, comes with this package. <see cref="Availability"/> says what is
/// missing without throwing.</para>
/// <para><b>One CEF per process.</b> CEF starts once in a process and stops when it exits.
/// Engines are handles onto it: an application, or a test run, may create and dispose as
/// many as it likes, and every view shares the one browser.</para>
/// </remarks>
public sealed class CefEngine : IWebEngine
{
    private bool _disposed;
    private readonly CefEngineOptions _options;

    public CefEngine() : this(new CefEngineOptions()) { }

    /// <param name="options">How CEF runs; the engine whose page starts CEF decides for the process.</param>
    public CefEngine(CefEngineOptions options) => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Whether this engine's pages run in Chromium's sandbox: its option, unless the
    /// diagnostic <c>HULTUI_CEF_SWITCHES=no-sandbox</c> turns it off.</summary>
    internal bool WantsSandbox => CefRuntime.WantsSandbox(_options);

    /// <summary>
    /// Whether CEF runs in Chromium's sandbox: null until CEF has started, then how it actually
    /// runs in this process, whichever engine started it.
    /// </summary>
    public bool? IsSandboxed => CefRuntime.IsSandboxed;

    public string Name => "CEF";

    public WebEngineAvailability Availability
    {
        get
        {
            var probe = CefLibrary.Loaded;
            if (!probe.IsLoaded) return WebEngineAvailability.Missing(probe.Error!);
            // CEF runs once per process, in one mode: a later engine asking for the other cannot have it.
            if (CefRuntime.ModeConflict(CefRuntime.IsSandboxed, WantsSandbox) is { } conflict) return WebEngineAvailability.Missing(conflict);
            // Known before any page: an application can choose something else, and say why.
            if (CefRuntime.Sandbox(probe.Directory!, WantsSandbox) is { } sandbox) return WebEngineAvailability.Missing(sandbox);
            return CefRuntime.Error is { } error ? WebEngineAvailability.Missing(error) : WebEngineAvailability.Available;
        }
    }

    /// <summary>The CEF in use, as "146.0.11 (Chromium 146.0.7680.177)", or null when there is none.</summary>
    public string? Version => CefLibrary.Loaded.Version;

    /// <summary>The directory CEF was found in, or null when it was not.</summary>
    public string? Directory => CefLibrary.Loaded.Directory;

    public IWebEngineView CreateView(WebViewOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new CefView(options, _options);
    }

    /// <summary>Releases this handle. CEF itself runs until the process exits.</summary>
    public void Dispose() => _disposed = true;
}

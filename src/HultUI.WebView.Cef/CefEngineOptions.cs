namespace HultUI.WebView.Cef;

/// <summary>
/// How the CEF engine runs, for the whole process: CEF starts once per process, and the engine
/// whose page starts it decides. A later engine asking for the other mode reports itself
/// unavailable, and its pages fail.
/// </summary>
/// <remarks>
/// <para><b>Running without the sandbox.</b> Chromium's sandbox keeps a page that exploits a
/// browser bug from the rest of the system. Without it, such a page runs with the user's full
/// rights: it can read and change the user's files and run programs as them. The sandbox
/// cannot start as root, on Ubuntu 23.10 and later without an AppArmor profile for the
/// application, or where user namespaces are turned off; there the engine is otherwise
/// unavailable.</para>
/// <para>So without the sandbox the engine loads only the application's own content: the
/// pages it gives as strings, <see cref="WebViewOptions.AppOrigin"/>, and <c>data:</c>,
/// <c>blob:</c> and <c>about:blank</c>. Every other request, in every frame and of every kind
/// (documents, frames, scripts, styles, images, <c>fetch</c>, WebSockets), is refused, unless
/// <see cref="AllowUntrustedContentWithoutSandbox"/> says otherwise.</para>
/// </remarks>
public sealed record CefEngineOptions
{
    /// <summary>
    /// Whether CEF runs with Chromium's sandbox. Default true. False runs every page without
    /// it, and limits them to the application's own content (see the remarks). The diagnostic
    /// <c>HULTUI_CEF_SWITCHES=no-sandbox</c> turns it off the same way, limits included.
    /// </summary>
    public bool Sandbox { get; init; } = true;

    /// <summary>
    /// With <see cref="Sandbox"/> off, let pages load anything, the internet included: a page
    /// that exploits a browser bug then runs with the user's full rights. Default false.
    /// Ignored while the sandbox is on.
    /// </summary>
    public bool AllowUntrustedContentWithoutSandbox { get; init; }
}

namespace HultUI.WebView;

/// <summary>How a page is set up. Fixed when the controller is created.</summary>
public sealed record WebViewOptions
{
    /// <summary>
    /// The origin local content is served from, and the one origin whose messages are
    /// accepted by default.
    /// </summary>
    public const string AppOrigin = "https://app.local";

    /// <summary>
    /// Where the engine keeps cookies, cache and storage. Null lets the engine choose a
    /// per-user folder for the application — never next to the executable, which is
    /// read-only once installed. Views with the same folder share one browser session.
    /// </summary>
    public string? UserDataFolder { get; init; }

    /// <summary>
    /// Origins whose <c>postMessage</c> reaches <see cref="WebViewController.MessageReceived"/>,
    /// as <c>scheme://host[:port]</c>, or <c>"*"</c> for any. Messages from documents
    /// loaded with <see cref="WebViewController.NavigateToString"/> are always accepted.
    /// The default is <see cref="AppOrigin"/> only: a page from the internet cannot talk
    /// to the application unless it is listed.
    /// </summary>
    public IReadOnlyList<string> MessageOrigins { get; init; } = [AppOrigin];

    /// <summary>Whether the engine's developer tools may be opened (F12, context menu).</summary>
    public bool DevTools { get; init; }

    /// <summary>Replaces the engine's user agent string; null keeps it.</summary>
    public string? UserAgent { get; init; }

    internal bool AcceptsMessagesFrom(Uri? origin)
    {
        if (origin is null) return true;
        foreach (var allowed in MessageOrigins)
        {
            if (allowed == "*") return true;
            if (Uri.TryCreate(allowed, UriKind.Absolute, out var a)
                && string.Equals(a.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
                && a.Port == origin.Port)
                return true;
        }
        return false;
    }
}

/// <summary>How a navigation ended, for the application.</summary>
/// <param name="Source">Where the page is now.</param>
/// <param name="IsSuccess">True when the document loaded.</param>
/// <param name="HttpStatus">The response status when the engine knows one.</param>
/// <param name="Error">Why it failed; null on success.</param>
public sealed record WebNavigationResult(Uri? Source, bool IsSuccess, int? HttpStatus, string? Error);

/// <summary>A message the page posted.</summary>
/// <param name="Json">The message, as JSON.</param>
/// <param name="Origin">The sending document's origin; null for a string document.</param>
public sealed record WebMessage(string Json, Uri? Origin);

/// <summary>The page asked to open a window, and was refused. Open <paramref name="Uri"/>
/// in the system browser, navigate this page to it, or ignore it.</summary>
public sealed record WebNewWindowRequest(Uri Uri, bool IsUserInitiated);

/// <summary>Where a controller is in its life.</summary>
public enum WebViewState
{
    /// <summary>Waiting for the engine — for some engines, until the page is first placed in a window.</summary>
    Initializing,
    Ready,
    /// <summary>The page's process died; <see cref="WebViewController.Reload"/> may recover it.</summary>
    Crashed,
    /// <summary>The engine cannot run; <see cref="WebViewController.Error"/> says why.</summary>
    Failed,
}

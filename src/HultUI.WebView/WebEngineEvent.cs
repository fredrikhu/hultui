using HultUI.Core.Events;
using HultUI.Core.Styling;

namespace HultUI.WebView;

/// <summary>
/// What a view tells its controller. Raised on whatever thread the engine runs on;
/// <see cref="WebViewController"/> marshals them to the UI thread. Frames are not
/// events — they go through the host's sink.
/// </summary>
public abstract record WebEngineEvent
{
    private WebEngineEvent() { }

    /// <summary>The engine is up; the controller releases what it held.</summary>
    public sealed record Ready : WebEngineEvent;

    /// <summary>The view cannot work, and will not start working.</summary>
    public sealed record Failed(WebFailure Kind, string Message) : WebEngineEvent;

    /// <summary>
    /// A navigation is about to happen. The controller lets the application cancel it,
    /// then completes <paramref name="Request"/>; the engine proceeds only on allow.
    /// </summary>
    public sealed record NavigationStarting(WebNavigationRequest Request) : WebEngineEvent;

    /// <summary>A navigation ended: loaded, failed, or cancelled.</summary>
    /// <param name="NavigationId">The <see cref="WebNavigationRequest.NavigationId"/> it started with.</param>
    /// <param name="IsSuccess">True when the document loaded.</param>
    /// <param name="HttpStatus">The response status when the engine knows one.</param>
    /// <param name="Error">Why it failed, when it did; "Cancelled" for a refused navigation.</param>
    public sealed record NavigationCompleted(long NavigationId, bool IsSuccess, int? HttpStatus = null, string? Error = null) : WebEngineEvent;

    /// <summary>The page's address changed — at a navigation's commit, or in-page.</summary>
    public sealed record SourceChanged(Uri? Source) : WebEngineEvent;

    public sealed record TitleChanged(string Title) : WebEngineEvent;

    public sealed record HistoryChanged(bool CanGoBack, bool CanGoForward) : WebEngineEvent;

    /// <summary>The page posted a message. <paramref name="Origin"/> is the sending
    /// document's origin, or null for one with an opaque origin (a string document).</summary>
    public sealed record MessageReceived(string Json, Uri? Origin) : WebEngineEvent;

    /// <summary>The page asked for a new window. The engine has already refused it; the
    /// application decides what to do with the address.</summary>
    public sealed record NewWindowRequested(Uri Uri, bool IsUserInitiated) : WebEngineEvent;

    /// <summary>The pointer is over something that wants a different cursor.</summary>
    public sealed record CursorChanged(CursorType Cursor) : WebEngineEvent;

    /// <summary>Tab (or Shift+Tab) went past the page's last (or first) stop: focus should
    /// move on to the next element in the window.</summary>
    public sealed record FocusLeaving(bool Forward) : WebEngineEvent;

    /// <summary>
    /// A shortcut pressed while the page has the keyboard, offered to the application
    /// before the page acts on it — only from engines with
    /// <see cref="WebEngineCapabilities.NativeKeyboard"/>, whose keys never pass through
    /// the window. The controller answers synchronously; a handled key never reaches the page.
    /// </summary>
    public sealed record KeyFromPage(WebKeyRequest Request) : WebEngineEvent;

    /// <summary>The page's process died. <see cref="WebViewController.Reload"/> recovers
    /// when <paramref name="CanReload"/>.</summary>
    public sealed record ProcessFailed(string Message, bool CanReload) : WebEngineEvent;
}

/// <summary>
/// A key the page's engine offers the application. Engines offer shortcuts only — Ctrl or
/// Alt held, or a function key — so typing, arrows and Escape stay the page's.
/// </summary>
public sealed class WebKeyRequest(KeyCode key, KeyModifiers modifiers, bool isDown, bool isRepeat = false)
{
    public KeyCode Key { get; } = key;
    public KeyModifiers Modifiers { get; } = modifiers;
    public bool IsDown { get; } = isDown;
    public bool IsRepeat { get; } = isRepeat;

    /// <summary>Set when something in the application took the key: the page does not see it.</summary>
    public bool Handled { get; set; }
}

/// <summary>Why a view failed.</summary>
public enum WebFailure
{
    /// <summary>The engine's runtime is not installed.</summary>
    RuntimeMissing,
    /// <summary>The engine is installed but would not start.</summary>
    InitializationFailed,
    /// <summary>The engine's main process exited and took every view with it.</summary>
    EngineExited,
}

/// <summary>
/// A navigation waiting for a yes or no. The engine creates it with a continuation; the
/// controller shows it to the application, which may <see cref="Cancel"/> it, and then
/// completes it — exactly once, so an engine may wait for the answer across threads.
/// </summary>
public sealed class WebNavigationRequest
{
    private Action<bool>? _complete;

    /// <param name="navigationId">The engine's id, repeated on the matching completion.</param>
    /// <param name="uri">Where the navigation goes.</param>
    /// <param name="isUserInitiated">The person did it — a click, a form — rather than script.</param>
    /// <param name="isRedirect">The server redirected an earlier request here.</param>
    /// <param name="complete">Called once with true to proceed, false to cancel.</param>
    public WebNavigationRequest(long navigationId, Uri uri, bool isUserInitiated, bool isRedirect, Action<bool> complete)
    {
        NavigationId = navigationId;
        Uri = uri;
        IsUserInitiated = isUserInitiated;
        IsRedirect = isRedirect;
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
    }

    public long NavigationId { get; }
    public Uri Uri { get; }
    /// <summary>The person did it — a click, a form — rather than script.</summary>
    public bool IsUserInitiated { get; }
    public bool IsRedirect { get; }
    public bool IsCancelled { get; private set; }

    /// <summary>Stops the navigation. Only meaningful inside the controller's
    /// <see cref="WebViewController.NavigationStarting"/> handlers.</summary>
    public void Cancel() => IsCancelled = true;

    /// <summary>Tells the engine the answer. The controller calls this; later calls do nothing.</summary>
    internal void Complete()
    {
        var complete = Interlocked.Exchange(ref _complete, null);
        complete?.Invoke(!IsCancelled);
    }
}

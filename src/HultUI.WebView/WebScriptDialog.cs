namespace HultUI.WebView;

/// <summary>Which of the page's own dialogs it is.</summary>
public enum WebScriptDialogKind
{
    /// <summary><c>alert()</c>: a message and OK.</summary>
    Alert,
    /// <summary><c>confirm()</c>: a question, OK or Cancel.</summary>
    Confirm,
    /// <summary><c>prompt()</c>: a question and a line of text.</summary>
    Prompt,
    /// <summary>The page asks, through <c>beforeunload</c>, whether to leave it with changes unsaved.</summary>
    BeforeUnload,
}

/// <summary>
/// A page opened one of its own dialogs — <c>alert</c>, <c>confirm</c>, <c>prompt</c>, or the
/// question before leaving a page with unsaved changes. The page's script waits until it is
/// answered. The engine creates it with a continuation; the controller shows it to the
/// application's <see cref="WebViewController.ScriptDialogRequested"/> handlers, and if none
/// answers or takes it over, the element shows it as a HultUI dialog over the page. It is
/// answered exactly once, so an engine may wait for the answer across threads.
/// </summary>
public sealed class WebScriptDialogRequest
{
    private Action<bool, string?>? _complete;

    /// <param name="kind">Which dialog.</param>
    /// <param name="message">What the page says. Empty for <see cref="WebScriptDialogKind.BeforeUnload"/>,
    /// where engines no longer pass the page's text on.</param>
    /// <param name="defaultText">The text a prompt starts with.</param>
    /// <param name="origin">Whose page asks — shown so a page cannot pass itself off as the application.</param>
    /// <param name="complete">Called once: accepted or not, and the prompt's text.</param>
    public WebScriptDialogRequest(WebScriptDialogKind kind, string message, string? defaultText, Uri? origin,
        Action<bool, string?> complete)
    {
        Kind = kind;
        Message = message;
        DefaultText = defaultText;
        Origin = origin;
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
    }

    public WebScriptDialogKind Kind { get; }
    public string Message { get; }
    public string? DefaultText { get; }
    public Uri? Origin { get; }

    /// <summary>
    /// Set by a handler that answers later, with its own interface: the element then shows
    /// nothing, and the handler must call <see cref="Accept"/> or <see cref="Cancel"/>.
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>The dialog has been answered.</summary>
    public bool IsCompleted => Volatile.Read(ref _complete) is null;

    /// <summary>
    /// OK — or, before unload, leave the page. <paramref name="text"/> is a prompt's answer;
    /// for a prompt, null answers with <see cref="DefaultText"/>. Later calls do nothing.
    /// </summary>
    public void Accept(string? text = null) =>
        Interlocked.Exchange(ref _complete, null)?.Invoke(true,
            Kind == WebScriptDialogKind.Prompt ? text ?? DefaultText ?? "" : null);

    /// <summary>Cancel — or, before unload, stay on the page. Later calls do nothing.</summary>
    public void Cancel() => Interlocked.Exchange(ref _complete, null)?.Invoke(false, null);
}

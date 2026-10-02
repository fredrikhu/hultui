namespace HultUI.WebView.Cef;

/// <summary>The view's DevTools conversations that wait for answers, apart from its callbacks
/// (methods of an <c>unsafe</c> class cannot be <c>async</c>).</summary>
internal static class CefScript
{
    /// <summary>
    /// The bridge, before the page is said to be ready: the Page domain on (scripts registered
    /// for new documents run only while it is), the Runtime domain on (each context's origin),
    /// the binding, and the <c>window.chrome.webview</c> shim for every new document.
    /// </summary>
    public static async Task SetUpBridge(DevToolsSession devTools)
    {
        await devTools.Send("Page.enable").ConfigureAwait(false);
        await devTools.Send("Runtime.enable").ConfigureAwait(false);
        await devTools.Send("Runtime.addBinding", w => w.WriteString("name", CefView.Binding)).ConfigureAwait(false);
        await devTools.Send("Page.addScriptToEvaluateOnNewDocument", w => w.WriteString("source", CefView.Shim)).ConfigureAwait(false);
    }

    /// <summary><c>ExecuteScriptAsync</c>: <c>Runtime.evaluate</c>, awaiting a promise, the value as JSON.</summary>
    public static async Task<string> Evaluate(DevToolsSession devTools, string script)
    {
        var result = await devTools.Send("Runtime.evaluate", w =>
        {
            w.WriteString("expression", script);
            w.WriteBoolean("returnByValue", true);
            w.WriteBoolean("awaitPromise", true);
        }).ConfigureAwait(false);
        return CefView.ScriptResult(result);
    }
}

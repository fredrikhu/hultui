# Web Views

How to put a web page in a HultUI app. The design and the reasons behind it are in
[design/WEBVIEW.md](design/WEBVIEW.md); this page is the consumer's view.

```csharp
using HultUI.WebView;           // WebViewController, WebViewElement, WebViewOptions, IWebEngine
using HultUI.WebView.WebView2;  // WebView2Engine (Windows)
using HultUI.WebView.Testing;   // FakeWebEngine (ships inside HultUI.WebView)
```

## Packages

| Package | What it is |
|---|---|
| `HultUI.WebView` | The engine contract, the reactive `WebViewController`, `WebViewElement`, and `FakeWebEngine` for tests. **No browser.** |
| `HultUI.WebView.WebView2` | The Windows engine: Microsoft Edge WebView2. Brings `WebView2Loader.dll`; the WebView2 Runtime itself is the user's — Windows 11 ships it, and `Availability` says when it is missing. |
| `HultUI.WebView.Wpe` | The Linux engine, WPE WebKit. **Not built yet.** |

An app that never shows a web page references none of these and pays nothing.

## The shape

```csharp
[STAThread]                                           // WebView2 refuses any other apartment
static void Main()
{
    using var app = new Application("Help", 1000, 700);
    using var engine = new WebView2Engine();          // one per app: it owns the browser process
    if (!engine.Availability.IsAvailable)
        Console.Error.WriteLine(engine.Availability.Reason);   // + InstallLink, when there is one

    using var web = new WebViewController(engine);    // one per page
    web.Navigate(new Uri("https://example.org"));      // held until the engine is ready

    app.SetRoot(Flex.Column().Children(
        Toolbar(web),
        WebViewElement.New(web).FlexGrow(1)));         // has no size of its own: give it one
    app.Run();
}
```

`WebViewController` is signal-backed: `State`, `Error`, `Source`, `Title`, `IsLoading`,
`CanGoBack` and `CanGoForward` are tracked when read inside a `Build()`. **State is the
engine's word**: `Navigate` does not set `Source`; the engine does when the navigation
commits. Commands — `Navigate`, `NavigateToString`, `GoBack`, `GoForward`, `Reload`,
`Stop`, `ExecuteScriptAsync`, `PostMessage` — run on the UI thread.

Some engines are not ready until the page is placed in a window (WebView2 needs one to
create itself under), so a controller whose element has never been drawn stays
`Initializing`. Commands and scripts issued before then are held and sent in order.

## What the app is asked

```csharp
web.NavigationStarting += request =>                  // UI thread; cancel inside the handler
{
    if (request.Uri.Host != "docs.example.org") request.Cancel();
};
web.NewWindowRequested += request =>                  // target=_blank, window.open: already refused
    OpenInSystemBrowser(request.Uri);                  // or web.Navigate(request.Uri)
web.MessageReceived += message =>                     // window.chrome.webview.postMessage(...)
    Handle(message.Json, message.Origin);             // null origin: a NavigateToString document
```

Messages are JSON both ways: `PostMessage(json)` reaches the page's
`chrome.webview.addEventListener('message', e => …)` with `e.data` parsed. Only origins in
`WebViewOptions.MessageOrigins` reach `MessageReceived` — by default just
`WebViewOptions.AppOrigin` (`https://app.local`) and string documents — so a page from
the internet cannot talk to the app unless it is listed. `["*"]` accepts any origin.

## Options

```csharp
new WebViewController(engine, new WebViewOptions
{
    UserDataFolder = Path.Combine(appData, "WebView"),  // cookies, cache; default is per user, per app
    DevTools = true,                                     // F12 / context menu; off by default
    UserAgent = "MyApp/1.0",
    MessageOrigins = ["https://docs.example.org"],
});
```

## What behaves like any element

The page is drawn by HultUI, not laid over it by the operating system: it clips, scrolls
and sits under dropdowns, menus, tooltips and dialogs like anything else. It is focusable;
Tab into it lands on the page's first field and Shift+Tab on its last, and tabbing out of
the page moves on to the next element. Its accessible name is the page's title unless the
app sets one. Unmounting the element hides the page without reloading it: a tab switch
keeps the page as it was.

**One element per controller.** The element tells the engine where the page is; two
elements showing one controller would fight over it.

## Keys while the page has focus

Typing goes to the page — on Windows through WebView2's own focus window, so input methods
work as in Edge. Shortcuts are offered to the app first: a key with Ctrl or Alt held, or a
function key, is dispatched as a `KeyDownEvent` from the web view element up through its
ancestors, so an app's `OnKeyDown` on the root sees Ctrl+L or F11 as usual. A handler that
sets `StopPropagation` takes the key, and the page never sees it; otherwise the page gets it
too. Typing, arrows, Tab and Escape are the page's alone.

## What it costs

A still page costs nothing: frames come only when the page changes. A page that animates
every frame is carried on the GPU from the browser to the screen on both renderers — the
capture's D3D11 texture is read by GL in place and blitted into the frame the renderer
composites — and costs this process roughly 60–100 ms of CPU a second on the GL renderer
and 20–90 on Vulkan, at 1650×1072 and 60 frames a second, most of it HultUI redrawing
the window. Where the driver lacks the
interop the frame is read back and uploaded instead, at about 220 ms/s;
`HULTUI_WEBVIEW2_INTEROP=0` forces that, for a driver that mishandles the interop.

## Limits, for now

- On the Vulkan renderer, one GPU source per window: a web view and a playing video in the
  same Vulkan window share the renderer's one interop image, so the second draws nothing.
  Separate windows, or the GL renderer, are unaffected.
- The page is announced to screen readers as a named group with nothing inside yet.
- Files dropped on the page do not reach it.
- Windows only until the WPE engine exists; `FakeWebEngine` runs anywhere.

## Testing

`FakeWebEngine` is a scripted engine with no browser: pages are solid colours with
titles, navigation has a real history, and every command, pointer event and key it was
sent is recorded on its view. Like WebView2, it becomes ready at the first placement in a
window, so under a headless session pump a couple of frames after setting the root.

```csharp
using var session = HeadlessSession.Create(800, 600);
var engine = new FakeWebEngine();
engine.Pages[new Uri("https://a.test/")] = new FakePage("Docs", Color.White);
using var web = new WebViewController(engine);
session.Application.SetRoot(Flex.Column().Children(WebViewElement.New(web).FlexGrow(1)));
session.PumpFrames(2);                                // draw, then deliver the placement

web.Navigate(new Uri("https://a.test/"));
Assert.Equal("Docs", web.Title);
engine.Views[0].RaiseMessage("{\"clicked\":1}", origin: null);   // what the page would do
```

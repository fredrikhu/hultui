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
| `HultUI.WebView.Cef` | The Linux engine: CEF (Chromium), from the distribution's package on Fedora (`sudo dnf install cef`; CEF 146 or later) and bundled on Debian and Ubuntu (not yet). Brings `hultui-cef-helper`, the small executable CEF starts its own processes from. **Being built:** pages, script, messages, input, focus and the input method work; navigation policy, zero-copy and accessibility follow. |

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

## The page's menus, dialogs, downloads and permissions

The engine's own interface for these never shows. Each reaches the controller as a request
that is answered exactly once — the page, or the engine, waits for the answer — and each has
a default that needs nothing from the app:

| Request | Default | The app can |
|---|---|---|
| `ContextMenuRequested` | A HultUI context menu where the page asked, with the page's editing and copying entries (cut, copy, paste, select all, copy link, spelling) and none of a browser's (back, reload, save as, print) | Change `request.Items` — remove, reorder, add its own `new WebContextMenuItem("Open in browser", action)`, pick from `request.EngineItems` — or set `Handled` and answer with `Select`/`Dismiss` itself |
| `ScriptDialogRequested` | `alert`, `confirm`, `prompt` and the leave-page question as a HultUI dialog that says whose page is asking; Enter answers yes, Escape no | `Accept(text)` or `Cancel()` in the handler, or set `Handled` and answer later |
| `DownloadRequested` | **Cancelled** | `SaveTo(path)`, or `Defer()` and call `SaveTo` after a save dialog; `request.Download` reports progress |
| `PermissionRequested` | **Denied** — camera, microphone, location, notifications, clipboard reading, everything | `Allow()`, or `Defer()` and answer after asking the person |

```csharp
web.ContextMenuRequested += menu =>
{
    if (menu.Target.LinkUri is { } link)
        menu.Items.Add(new WebContextMenuItem("Open in browser", () => OpenInSystemBrowser(link)));
};
web.DownloadRequested += download =>
{
    download.Defer();                                   // the answer comes after a dialog
    window.FileDialogs.SaveFileAsync(download.SuggestedFileName).ContinueWith(t =>
        Application.InvokeOnMainThread(() =>            // answers go back on the UI thread
        {
            if (t.Result is { } path) download.SaveTo(path).Changed += ShowProgress;
            else download.Cancel();
        }));
};
web.PermissionRequested += p =>
{
    if (p.Permission == WebPermission.ClipboardRead && p.Origin?.Host == "docs.example.org") p.Allow();
};
```

While the page's menu or dialog is open the keyboard is HultUI's — arrows, Enter and Escape
work the menu, typing goes into a prompt — and it goes back to the page when it closes. The
sample saves downloads through a save dialog and shows their progress in its status line.

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

## Screen readers

The page's own accessibility tree — the browser's, with its headings, links and fields — is
part of the window's: it hangs under the web view element, where a reader walking the
app's tree comes to it. The element itself is a group named after the page's title (or
whatever the app set as `AccessibleName`), with the rectangle it has on screen. A point
over the page resolves to the page's element under it, not to the group, and focus inside
the page is reported as the focused field or button. None of it needs anything from the
app.

Between the element and the page's document a reader passes the embedded browser's own
containers, as in any WebView2 application; they are unnamed panes and the control view
skips most of them. The tree arrives a moment after the page does — the engine waits for
the browser to make its windows — and until then the element is an empty group.

Give the element an `AutomationId` if UI tests need to find the page's place in the tree.

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
the window. Several pages, and videos beside them, can share a window on either renderer:
on Vulkan each is lent a shared image of its own. Where the driver lacks the
interop the frame is read back and uploaded instead, at about 220 ms/s;
`HULTUI_WEBVIEW2_INTEROP=0` forces that, for a driver that mishandles the interop.

## Limits, for now

- Drag and drop needs `[STAThread]` on Windows: files and text dropped from outside reach the
  page, drags inside the page work, and a page's drag can be dropped on another application.
  A page learns of a drag about a quarter of a second after it enters (Edge is quicker: its
  drop target is in the browser's own process). On Linux, drops wait
  for the CEF engine.
- On Linux, **one profile per process**. CEF keeps cookies, storage and cache in one folder
  per process, and the first page's `UserDataFolder` decides which (the application's
  default, `~/.local/share/<app>/CEF`, when it names none). A later page may name that folder,
  name none, or name a folder inside it, which gets a session of its own; a page naming any
  other folder fails (`WebViewState.Failed`, with a message saying which folder is in use).
  WebView2 has no such rule: there each folder is its own browser.
- On Linux, **a second instance of an application runs on a temporary profile.** Chromium
  allows one browser per profile, so the second copy of an application cannot share the
  first's: its pages work, but without the first's cookies and storage, and what it stores is
  deleted when it exits. On Windows, instances share their user data folder's browser.
- On Linux, **pages under X11 take the pixel path**: GPU zero-copy needs HultUI's window on
  Wayland (EGL; under X11 HultUI's GL context is GLX, which cannot take CEF's buffers).
- The CEF engine is being built (above); `FakeWebEngine` runs anywhere.

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

# HultUI.WebView — Design

A web page as an element, shipped as packages an application opts into, so an
application that never shows a web page never carries an engine, a loader, or
the interop for one.

Written 2026-09-30 from a read of the framework as it stands. The Windows path
was spiked the same day (W0, `spikes/WebView2Capture`, results below); the Linux
path has not been, and its engine changed from WPE WebKit to CEF on the day Windows
was finished — see *Linux engines*, and `WEBVIEW-LINUX-BRIEF.md` for the work itself. Where a claim still needs confirming on real hardware it says
**verify**. Three decisions were put to the owner and are
marked **DECIDED** (2026-09-30).

This reverses an earlier call. `docs/plans/10-capability-roadmap.md` lists
WebView as out of spirit; the external review of 2026-08-12 recommended a
satellite package if it ever shipped. That is the shape here, and the roadmap's
line changes when this lands.

## What an application gets

```csharp
IWebEngine engine = OperatingSystem.IsWindows() ? new WebView2Engine() : new CefEngine();
var web = new WebViewController(engine, new WebViewOptions
{
    UserDataFolder = Path.Combine(appData, "WebView"),
});
web.Navigate(new Uri("https://example.org"));

Box.New().Children(
    Toolbar(web),                        // web.Title, web.CanGoBack, web.IsLoading are signals
    WebViewElement.New(web).FlexGrow(1));

web.MessageReceived += m => ...;          // window.chrome.webview.postMessage / its CEF twin
await web.ExecuteScriptAsync("document.title");
```

The app picks the engine; `HultUI.WebView` cannot name the engine packages, which
depend on it. The element is `WebViewElement.New`, not a `WebView.New` builder: a
class named after its own namespace (`HultUI.WebView.WebView`) is unreachable by
its short name from anywhere inside `HultUI.*` — the `Text` trap again.

Out of scope: tabs, a download manager, extensions, printing, a DOM API from C#,
host objects in script (`AddHostObjectToScript` is `IDispatch` and fights AOT).
Messaging is JSON strings both ways.

## The fork: where the pixels go

Every embedding makes this choice first, and everything else follows from it.

**Windowed.** The engine owns a native child window laid over ours. Input, IME,
accessibility, drag and drop, cursors and popups are all the engine's and work
on day one. The cost is *airspace*: the child window is above our swapchain, so
nothing HultUI draws can be on top of it.

**Composited.** The engine renders offscreen and hands us frames; the element
draws them like a video frame. The page clips, scrolls, fades and sits under
overlays like any other element. The cost is that input, cursor, focus and
accessibility have to be forwarded or stitched by us.

Three facts about HultUI decide it:

1. **Every popup is in-canvas.** Dropdowns, date pickers, context menus, tooltips
   and dialogs are `OverlayHost` overlays; toasts are in the app's own tree. All
   are drawn into the same surface — the only other OS windows are top-level ones.
   Under a windowed web view every one of them would vanish behind the page — a
   dropdown in a toolbar above the page would open *under* it. The framework has
   met this twice: the WinForms host documents airspace as "accepted, not
   solved", and `HDR.md` rejected mpv's `--wid` child window because of it.
2. **Linux has no windowed option.** Wayland has no way to put another toolkit's
   surface inside our window, and the X11 route (XEmbed) is GTK3-only. Every
   viable Linux engine is composited. (See *Linux engines*.)
3. **The composited path already exists for video.** `IPixelImageSource` /
   `PixelFrameSink` and `IGpuImageSource` carry frames from a producer thread into
   Skia, with wake-on-frame and the borrow-until-flush rule. A web frame is a
   video frame with a different producer.

So: **composited on both platforms.** One element model, one set of behaviours,
headless-testable with a fake engine. Windowed stays documented as the fallback
if the Windows spike (below) shows capture latency is unacceptable — the element
API does not change either way.

**DECIDED (Windows composition): composited.** The alternative was a windowed
child HWND on Windows only, with the airspace documented and a snapshot swap
(`CapturePreview` into an image, child hidden) while an overlay intersects the page.

## Packages

```
src/
  HultUI.WebView/            contract, WebViewController, WebView element, fake engine — no natives
  HultUI.WebView.WebView2/   Windows engine: hand-rolled COM, WinRT capture, D3D11
  HultUI.WebView.Cef/        Linux engine: CEF rendered off-screen, through its C API, hand P/Invoke
tests/
  HultUI.WebView.Tests/          headless, fake engine
  HultUI.WebView.Engines.Tests/  real engines, skipped when the runtime is missing
samples/
  HultUI.Samples.WebView/    address bar, back/forward, a JS↔C# message demo, an overlay over the page
```

Same split as Media: the element and controller never know which engine they
have, and an application's headless tests use `FakeWebEngine` from
`HultUI.WebView.Testing` with nothing native installed.

**DECIDED (repository): packages in this repository, like Media.** "Own
repository" meant opt-in packages: an app that does not reference
`HultUI.WebView*` gets no assembly, no loader DLL and no engine probing. A
separate git repository was weighed and would have added a real cost: nothing is
published to a feed yet, so it would consume HultUI through a local feed or a
sibling checkout, and Media already needed `InternalsVisibleTo` for
`MeasureContent`/`PatchFrom`, which a separate repository cannot sensibly have.

### AOT decides the Windows interop

All six apps publish NativeAOT. Microsoft's managed wrapper
(`Microsoft.Web.WebView2.Core.dll`) uses built-in COM interop, which NativeAOT
turns off; the package also drags in its WinForms and WPF assemblies. So the
engine talks to WebView2 through **hand-rolled vtables**, the house style of the
UIA bridge (`NativeProvider.cs`) and Vesper's shell menu. The ABI is stable and
versioned (`ICoreWebView2_N`); vtable order is copied from `WebView2.h`, never
recalled — the lesson of mpv's `render.h`.

The one native piece is `WebView2Loader.dll` (195 KB for win-x64 in SDK 1.0.4258, redistributable), which
finds the installed Evergreen runtime. Reimplementing its discovery means calling
undocumented internals of the runtime, so it is shipped, resolved the way the mpv
backend resolves libmpv (`HULTUI_WEBVIEW2_LOADER` → app dir → `runtimes/<rid>/native`).
**Verify** which package assets carry it, so the package can take the DLL without
the managed assemblies.

The Windows engine targets `net10.0` with `SupportedOSPlatform("windows")`, like
the accessibility bridge, so it stays in the cross-platform solution filter and
the docs build.

## Windows: WebView2, composited

**Hosting.** `ICoreWebView2Environment3.CreateCoreWebView2CompositionController`
with the SDL window as `ParentWindow`. `RootVisualTarget` is a
`Windows.UI.Composition` `ContainerVisual` from a compositor on the UI thread
(needs a `DispatcherQueue` on that thread, created once).

**Frames.** WebView2 has no offscreen-rendering API; the request has been open
since 2020 (WebView2Feedback #547). The known route, used by the Unity and
Flutter embeddings, is `Windows.Graphics.Capture`:
`GraphicsCaptureItem.CreateFromVisual(container)` → a free-threaded
`Direct3D11CaptureFramePool` → `FrameArrived` on a pool thread with an
`ID3D11Texture2D`. Frames arrive only when the page changes, so a still page
costs nothing. **W0: works on a visual attached to no window at all**, and the page
always lands at the visual's origin whatever `Bounds.X/Y` says, so no cropping.
A page change reaches a captured frame in 6.7 ms median (p90 11, max 17).

- **Phase 1, pixels.** Copy the frame to a staging texture, map it, write it into
  a `PixelFrameSink` (BGRA8, premultiplied). Works on the GL, Vulkan and raster
  surfaces unchanged — no renderer code at all. **W0 cost:** a continuously
  animating 1650×1072 page captures at 60 fps with a 1.7 ms staging copy, and costs
  our process ~300 ms/s of CPU against ~5 ms/s for the same page windowed (the
  browser's own processes cost the same either way). Most of it is the copy plus
  Skia re-uploading 7 MB per drawn frame.
- **Phase 2, zero-copy — done (step 3).** `WGL_NV_DX_interop2` lets GL read a D3D11
  texture we own, filled by one `CopySubresourceRegion` per frame, and a GL blit puts it in
  the frame sink's framebuffer — the window's GL context on the GL surface, the hidden
  interop context on the Vulkan one, so one path serves both renderers. It cut an animating
  page from ~300 ms/s to roughly 20–100 (numbers under *Implementation order*).

**Size and scale.** `Bounds` is set in the parent window's client pixels to where
the element actually is, so the browser places its own popups (`<select>`, date
pickers, autofill) correctly on screen. **W0:** a `<select>` popup opened as the
browser's own top-level window at the element's bottom-left, to within Chromium's
usual few-pixel gap. `RasterizationScale` follows the window's
display scale, `ShouldDetectMonitorScaleChanges` is off (we tell it). The frame
pool is recreated on size change.

**Input.** Pointer: `SendMouseInput` (move, buttons, wheel, horizontal wheel,
leave) from the element's pointer events, in device pixels relative to `Bounds`
(W0 confirmed bounds-relative, not parent-relative), with
pointer capture held from down to up so a drag leaving the element keeps
flowing. Pen and touch through `SendPointerInput` later. Cursor: `CursorChanged`
→ `SystemCursorId` → the nearest HultUI cursor kind.

**Keyboard and IME are native.** Microsoft's hosting guide lists only *spatial*
input (mouse, touch, pen) as the host's to forward in visual hosting; keyboard
focus stays with a window WebView2 keeps under `ParentWindow` — W0 found exactly
one, a 0×0 `Chrome_WidgetWin_0` — so typing, IME composition and the candidate
window are Chromium's own (**verify** the candidate position by hand in the
spike's `interactive` mode). HultUI focus and Win32 focus have to agree:

- element gains focus → `MoveFocus(Programmatic)`; Win32 focus goes to the child.
- page tabs past its last element → `MoveFocusRequested` → HultUI moves focus to
  the next element and `SetFocus`es the SDL window.
- element loses focus (a click on HultUI content) → `SetFocus(sdlHwnd)`; a click
  on a parent's client area does not take focus back from a child by itself.
- While the page has focus our window receives no key events, so app shortcuts
  (F11, Ctrl+L, Escape out of full screen) go dead. `AcceleratorKeyPressed`
  offers them to the host first; the controller routes them through HultUI's key
  handling and marks them handled when something took them.

Win32 focus moving into that child and back is not a window focus change to SDL:
**W0 counted zero `Window.Activated` events either way**, so Vesper's
edit-and-return (which runs on that event) is safe.

**Accessibility** (step 4, done). WebView2 keeps two windows for a composited page, and
the page's UI Automation tree is reached through them, not through WebView2's API:

- **The focus window**: `Chrome_WidgetWin_0`, a 0×0 child of the app's window. UIA lists
  it under the window root, with everything the browser exposes below it — the embedded
  browser's own views (about a hundred, most of them hidden) and, nine levels down, the
  page's document. Left alone, a screen reader finds the page beside the app's tree, not
  in it, and the web view element is an empty group.
- **The browser window**: `Chrome_WidgetWin_1`, a top-level window of the browser
  process, invisible and click-through, kept exactly over the page on screen. Its
  providers resolve a point to the page's element. The focus window's cannot: it has no
  area, and the browser window is not its child in Win32, so UIA cannot find it by
  position either.

The web view's node **is the override provider for the focus window** (UIA's "provider
repositioning"): the root answers `IRawElementProviderHwndOverride` with the node's
provider, which names the window as its host (`UiaHostProviderFromHwnd`) and declares
`ProviderOptions_OverrideProvider`. UIA then joins the node with the window's own
providers, so the browser's tree hangs under the element and no longer under the window
root, with the element's name, rectangle and place among its siblings. A hit test inside
the element is answered with a provider that only stands for the browser window
(`UiaWindowStandIn`), and UIA asks that window's providers, which drill to the page's
button. Focus needs nothing: the focus window holds Win32 focus, and UIA asks it.

Three things found on the way:

- **WebView2's own provider is not usable.** `ICoreWebView2CompositionController2.
  AutomationProvider` (the same object `GetAutomationProviderForWindow` returns for the
  focus window) has options ServerSide | Override | UseComThreading, no name, no children,
  a client-relative rectangle, and answers neither `ElementProviderFromPoint` nor
  `GetFocus` — in screen or client coordinates. Registered as the override it moved
  nothing and gave the window a wrong rectangle. It is kept for one job: it is what tells
  this page's focus window from another page's.
- **The override flag is load-bearing.** A node that names a host window without it is
  taken for the window's main provider; UIA asks for the override again and never stops —
  a client's `GetNextSibling` hung until the app was killed.
- **The bridge's `ProviderOptions` constants were wrong** (`ServerSideProvider` read 0x1,
  which is `ClientSideProvider`). UIA had been lenient; fixed to the header's values.

**The window's position.** The browser reports every rectangle in screen coordinates and
opens its popups there, and only learns where the app's window is from
`NotifyParentWindowPositionChanged`. HultUI has no moved event, so the engine subclasses
the parent window (`ParentWindowWatch`, comctl32) and calls it on `WM_WINDOWPOSCHANGED`.
Before this a moved window left the page's rectangles where the window had been, and a
`<select>`'s list with them. Both measured, by removing the call: the accessibility test
reports the button where it was, and `ASelectsList_OpensUnderItsField…` finds the list
(−90, −64) px from its field after the window moved by (90, 70), where it had been (0, 6)
before. With the call, the list is under its field both times.

**NavigationStarting has no deferral** (the IDL offers none), so the answer must be
given before the handler returns. It is: WebView2 raises it on the UI thread, where the
controller answers synchronously. An engine that raises elsewhere (CEF, if it runs its
own UI thread) keeps the request's continuation instead.

**Threading.** WebView2 must be created on an STA thread with a message pump.
Junction's and Vesper's `Main` are already `[STAThread]` (for OLE); any app that
shows a page needs the same. SDL's wait wakes on any
posted message, so WebView2's callbacks arrive on the UI thread through the
normal loop. The controller checks the apartment and fails with a clear message
rather than a COM error. `FrameArrived` is a pool thread: it writes the sink and
requests a repaint with the Media hop — a thread the frame waits for never posts.

**Runtime.** The Evergreen runtime ships with Windows 11 and updates itself
(154.0 on this machine). Missing runtime → controller `State = RuntimeMissing`
with the download link; a fixed-version runtime folder is an option.
`UserDataFolder` defaults under `%LOCALAPPDATA%\<app>` — WebView2's default is
next to the exe, which fails under Program Files. One environment (one browser
process) per user data folder, shared by every view.

**Spike W0 — done 2026-09-30** (`spikes/WebView2Capture`, README has the table).
All four questions answered by measurement: capture of an unattached visual,
latency and cost, `<select>` placement and focus, and the accessibility route —
each folded into the paragraphs above. Left for a person at the keyboard: IME
candidate position, and how scrolling and drag-selection feel.

**Checked by the owner, 2026-09-30, on the shipped engine (the WebView sample):**

- **Narrator** reads the page as it reads the web view in Claude Desktop — the same
  stops, the same containers between the app and the document. Good enough.
- **IME (Japanese): works, and matches Edge.** The candidate list opens by itself in the
  right place on google.com. In wikipedia.org's search box the composition text shows and
  Enter commits it, but the list opens only on Tab — and **Edge itself does the same on
  that page**, so it is the engine's behaviour and not the hosting's. The fields differ in
  kind (`<input type="search">` against Google's `<textarea>`), which is the likely reason:
  a search field shows its own suggestions. Brave opens the list on both; that is a
  difference between Brave and Edge, not ours to chase. Closed.

## Linux engines

**DECIDED (Linux engine), revised 2026-09-30: CEF, rendered off-screen.** WPE WebKit was
chosen first, from the comparison below, and withdrawn the same day on a rule the owner
added: an app must run on **Fedora with only Fedora's packages, RPM Fusion and official
COPRs**, and on Debian and Ubuntu too.

- **WPE WebKit is not packaged in Fedora** (43 to 45, Rawhide, EPEL); only personal COPRs
  carry it. The comparison below had left that cell marked *verify*.
- **CEF is**: `cef` and `cef-devel`, 146 on the stable releases and 151 in Rawhide, kept up
  with Chromium's security releases. Arch packages it as well. So on Fedora the app ships
  none of it — the argument that had been WPE's.
- **Debian and Ubuntu do not**, so there the app bundles CEF (about 110 MB compressed) and
  owns its security updates. CEF's versioned API (since M133: a client names the API version
  it was written against and the library keeps that ABI) is what should let one engine
  binary run against Fedora's CEF across major bumps and against a bundled one. **Verify**:
  it is question 2 of the spike.
- **What it costs**: accessibility is ours to build from CEF's raw tree updates, where WebKit
  would have spoken AT-SPI itself; a helper process; and zero-copy on NVIDIA waits on
  cef#4237, which is still open. **Spike L0 found (question 1) that the helper must be
  native**: Chromium's zygote has to be single-threaded to sandbox itself and cannot fork a
  .NET process, so neither the app run again nor a .NET helper (NativeAOT included) works
  with the sandbox on. A 20-line C executable does, and is the engine's one native piece.
- **What it gains**: the same engine as Windows, so a page renders alike on both.

The spike, the machine and the build order are in `docs/design/WEBVIEW-LINUX-BRIEF.md`,
written for a session on the Linux machine. The comparison, as first written:

| | WPE WebKit (WPEPlatform) | CEF off-screen | WebKitGTK | None for now |
|---|---|---|---|---|
| Engine | WebKit (Safari-class) | Chromium (same as WebView2) | WebKit | — |
| How it gets into our window | We implement a `WPEDisplay`/`WPEView`; frames arrive as `WPEBuffer` (dmabuf, or shared memory without a DRM device) through `render_buffer` | `OnPaint` gives BGRA with dirty rects; `OnAcceleratedPaint` gives dmabuf planes | It cannot: a GTK widget, no foreign embedding on Wayland, XEmbed is GTK3/X11 only | A placeholder; OAuth goes through the system browser and a loopback redirect |
| Frame path phase 1 | shm / mapped buffer → `PixelFrameSink` | `OnPaint` → `PixelFrameSink` | — | — |
| Zero-copy | dmabuf → EGLImage (GL) / `VK_EXT_external_memory_dma_buf` (Vulkan) | dmabuf, but CEF's Linux shared-texture path breaks on NVIDIA today (cef#4237, open) | — | — |
| Input | Everything forwarded as `WPEEvent` — keyboard too; IME through a `WebKitInputMethodContext` we feed from SDL's text-editing events | Everything forwarded (`SendMouseEvent`, `SendKeyEvent`, IME composition calls) | — | — |
| Popups, menus, dialogs, file chooser | Signals to the embedder: we draw `<select>` and context menus with HultUI's Dropdown/ContextMenu and use HultUI's file dialogs | Popup widget painted into a second buffer we composite; dialogs via handlers | — | — |
| Accessibility | WebKit speaks AT-SPI over D-Bus itself; our bridge references its root as the element's child (**verify** the embed API) | `CefAccessibilityHandler` sends raw tree updates; we would build the whole AT-SPI tree ourselves | — | — |
| What the app ships | Nothing where a distro packages it (`libwpewebkit-2.0`) — **Fedora does not** | libcef + resources + a helper executable: ~110 MB compressed, ~350 MB installed (Arch's package), plus sandbox setup | — | Nothing |
| Security updates | The distribution's | Ours, every Chromium release | — | — |
| Maturity risk | WPEPlatform became stable and default only in 2.54 (Sept 2026); the libwpe API is now deprecated. Debian testing has 2.54, Debian stable 2.48; Ubuntu 24.04 (our CI runner) is older; **Fedora: not packaged** | Mature; the heavy parts are packaging and the Linux GPU path | — | None |
| Rendering parity with Windows | Different engine: pages may differ from WebView2 | Same engine as WebView2 | — | — |

**Why WPE WebKit was chosen first.** It is the only engine built for exactly this
model — the embedder owns the renderer, buffers are handed over, input is
injected — and the application ships zero bytes of it. Its risk is recency: it
needs a 2.54-era distribution, and until the CI runner has one, the engine tests
skip there. CEF is the choice if identical rendering to Windows matters more than
size and packaging; it is a permanent native-distribution commitment like the mpv
build, but ten times larger. "None for now" is the honest fallback if Linux can
wait: ship Windows, keep the contract ready.

Rejected: WebKitGTK (no way into an SDL window on Wayland), the deprecated
libwpe/`wpebackend-fdo` API (building on something being removed), Servo (no
stable C embedding API yet, compatibility gaps), Ultralight (proprietary licence).

**Threading on Linux (CEF).** Two models, and the spike picks: CEF's own UI thread
(`multi_threaded_message_loop`), with controller calls posted to it and events marshalled
back, or CEF pumped from HultUI's thread (`external_message_pump`). The contract allows
either — `IWebEngineView.Event` may be raised from any thread — under one rule: the UI
thread never blocks on CEF's. Frames go into the sink from whichever thread paints, like
`FrameArrived` on Windows.

## The contract

**Shipped in step 1** (`src/HultUI.WebView`); the source is the reference, this is
the shape.

```csharp
namespace HultUI.WebView;

public interface IWebEngine : IDisposable          // one per app: owns the browser processes
{
    string Name { get; }
    WebEngineAvailability Availability { get; }    // cheap, never throws: "install the runtime"
    IWebEngineView CreateView(WebViewOptions options);
}

public interface IWebEngineView : IDisposable      // one page; the controller is its only caller
{
    WebEngineCapabilities Capabilities { get; }    // NativeKeyboard, DevTools
    void Attach(IWebViewHost host);                // once, first: the PixelFrameSink frames go to
    void SetPlacement(in WebViewPlacement p);      // (native window, device-pixel bounds, scale)
    void SetVisible(bool visible);
    void Navigate(Uri uri); void NavigateToString(string html);
    void GoBack(); void GoForward(); void Reload(); void Stop();
    Task<string> ExecuteScriptAsync(string script); // JSON result
    void PostMessage(string json);
    void SendPointer(in WebPointerInput input);    // device pixels from the page's top-left
    void SendKey(in WebKeyInput input);            // only without NativeKeyboard
    void SendText(in WebTextInput input);          // commit or composition, likewise
    void Focus(FocusReason reason); void Blur();   // Core's FocusReason (seam W2)
    event Action<WebEngineEvent>? Event;           // any thread; the controller marshals
}
```

Every member is called on the UI thread. `WebEngineEvent` is a closed set of
records: `Ready`, `Failed(kind, message)`, `NavigationStarting(request)`,
`NavigationCompleted`, `SourceChanged`, `TitleChanged`, `HistoryChanged`,
`MessageReceived(json, origin)`, `NewWindowRequested`, `CursorChanged`,
`FocusLeaving(forward)`, `ProcessFailed(message, canReload)`. Frames go through
the sink, not the event, exactly as in Media.

`NavigationStarting` carries a `WebNavigationRequest` built with a continuation:
the controller shows it to the app's handlers (which may `Cancel()` it) and then
completes it exactly once — in a `finally`, so a throwing handler cannot leave an
engine waiting. WebView2 raises on the UI thread and gets its answer before the
handler returns; CEF, on its own thread, waits for the continuation.

`WebViewController` owns one view and the sink, holds signals (`State`, `Error`,
`Source`, `Title`, `IsLoading`, `CanGoBack`, `CanGoForward`), and marshals events
like `MediaPlayer`. Until `Ready` it holds navigation, script and messages in
order; a held script's task completes after, or faults if the engine fails. An
unavailable engine fails the controller at once with the engine's reason and no
view. `WebViewElement.New(controller)` has no size of its own, draws the sink one
device pixel per pixel from its top-left (clipped, so a resize shows the old frame
unstretched), reports placement after the frame, hides the page on unmount without
reloading it, and survives rebuild twins the way `VideoElement` does.

`FakeWebEngine` (`HultUI.WebView.Testing`) is ready at its first placement, as
WebView2 is; pages are solid colours with titles and a real history; every call
and input is recorded; `Raise*` injects what a page does.

**Defaults that are policy, not plumbing:** new windows are denied and raised as
an event (the app opens the system browser or navigates in place); `file://` is
off; local content is served through a virtual host
(`SetVirtualHostNameToFolderMapping` / a CEF scheme handler factory) under a fixed
`https://app.local/`; `MessageReceived` only from origins the options allow;
DevTools off unless `WebViewOptions.DevTools`.

## Framework seams

From a survey of the framework on 2026-09-30. Each is useful beyond web views;
no engine package may need anything not listed here — if implementation finds
another, it goes here first. W1–W4 are needed for the first Windows version.

**W1 — Reach the window from an element (`HultUI`, `HultUI.Core`). Shipped in
step 1** as `Window.NativeHandle` and `Window.ContentBoxInWindow(element)` — the
second because `ScreenContentBox` is off by the overlay's position for anything in
a popup or dialog, where a sign-in page is likely to live. An element
finds its `Window` through `OverlayHost is Window` (the `VideoPlayer` pattern),
which is not set yet in `OnMounted` — resolve at render time. What is missing is
the handle: `Window.PlatformWindow` is internal and `IWin32WindowHandle` has one
consumer, the UIA bridge. Add a public `Window.NativeHandle` (HWND on Windows,
zero elsewhere; CEF needs none, since it renders off-screen). Core
grants `InternalsVisibleTo HultUI.WebView` for `MeasureContent`, `PatchFrom` and
`WantsTextInput`, as it does for Media.

**W2 — Focus carries its reason (`HultUI.Core`). Shipped in step 1** as
`FocusEvent.Reason` (`Programmatic`, `Pointer`, `Next`, `Previous`), set by
`FocusManager` from Tab, Shift+Tab and the window's press. The page needs it: Tab
in lands on its first field, Shift+Tab on its last, a click on the element under
the pointer. Tabbing *out* already had `Window.MoveFocus(forward)`.

Found on the way: **`Element.IsFocused` was false for any element without a focus
style** — it lived in the interaction state, which only a focus style creates.
Junction's tests had worked around it with a comment. Now a plain flag, set for
every element; not by creating the interaction state, whose first recompute
snapshots the element's visuals and would later revert an app's own changes.

**W3 — Pointer leaves the window (`HultUI.Platform.SDL3`, `HultUI`). Shipped in
step 2** as `InputEvent.PointerLeftWindowInput`. SDL's
window mouse-leave is not handled, so nothing gets `PointerLeave` when the
pointer exits the OS window. A page would keep its hover state; so does a button
at the window edge today. Translate the event and leave the hovered chain.

**W4 — Cursor applies without a move (`HultUI`). Shipped in step 2.** The window reads the cursor
only on a pointer move. The page's cursor answers a move we forwarded, so it
arrives just after that move was handled and would show one move late. The
window re-reads the hovered element's cursor after a style pass that changed it.
Also: `CursorType.None` maps to the arrow today (hidden cursor missing), and a
`Progress` kind is worth adding — SDL has both.

**W5 — Key re-entry. Shipped in step 2, in `HultUI.WebView` rather than `HultUI`:**
the element dispatches the offered key from itself, so no window method was needed. With the page focused on Windows, keys go to
Chromium, and `AcceleratorKeyPressed` hands back the ones an app might bind. They
must re-enter HultUI as a `KeyDownEvent` on the focused element's ancestors. The
WinForms host already translates virtual keys through scan codes to `KeyCode`
(`KeyMap`); the missing piece is an internal `Window.DispatchKey(...)` the
package can call.

**W6 — Raw scan code on key events (`HultUI.Core`).** For Linux engines that take
forwarded keys. `KeyCode` is physical but maps the keypad, F13+, media and
international keys to `Unknown`; CEF's key event wants the native key code beside
the Windows one (what exactly is question 5 of the Linux spike). Add `KeyDownEvent.ScanCode`/`KeyUpEvent.ScanCode`.
**Spike L0 answered it:** CEF's `native_key_code` is the XKB keycode (evdev + 8), and it is
what fills `KeyboardEvent.code` (empty without it); `key` and `keyCode` come from the
Windows code. So `ScanCode` carries SDL's scancode (the USB HID usage), and the engine maps it
through one table generated from Chromium's `dom_code_data.inc` (USB, evdev and XKB per key).

**W7 — Per-source interop images on Vulkan (`HultUI.Platform.SDL3`,
`HultUI.Rendering.Skia`). Done (2026-09-30).** The Vulkan surface lent *one* interop
image and refused a second lend in a frame, so a second GPU source in a window drew
nothing. Now `IExternalGLSurface.TryAcquireFrameTarget` and `ReleaseFrameTarget` take
an owner (the `GpuFrameSink`), and `IVulkanSurfaceHost.LendInteropImage(owner, …)` keeps
a slot per owner: its image, GL objects, and its own GL→Vulkan and Vulkan→GL semaphore
pair. One barrier submit hands every slot drawn in the frame back to Vulkan, and the
present signals every slot's release semaphore. `ForgetInteropImage(owner)` may be
called from any thread; the slot is destroyed at the next frame, after a device wait.
A sink holds its slot until it is disposed — with its controller or player, not when its
element leaves the tree. Proven by
`VulkanProbeTests.TwoGpuSources_InOneVulkanWindow_BothReachTheScreen`: two pages side by side, captured from the swapchain, and one
disposed afterwards gives its image back. With the second lend refused again, the
right page's half was empty (seen). A single video on Vulkan was checked by capture.
**Open:** the Linux fd path, which has not run with more than one slot.

**W8 — Foreign accessibility subtree (`HultUI.Core`, both bridges).** Neither
bridge could host a subtree it did not build. **Windows half done (step 4):**
`Element.NativeAccessibleContent` (internal; a web view overrides it) →
`AccessibilityNode.NativeContent`, a `NativeAccessibleContent(Window, HitTestWindow)`.
The differ reports a change to it as `AccessibilityProperties.NativeContent`, and an
element says when it changed with `NotifyAccessibilityPropertyChanged` — the tree is
only rebuilt for a reason, and nothing else changes when an engine finds its windows.
The UIA bridge does the rest as described under *Accessibility* above: HWND override on
the root, the node as override provider, a stand-in provider for hit tests, and a
`ChildrenInvalidated` on the node and the root when content arrives or leaves. The
engine's side is `IWebEngineView.AccessibleContent` plus
`WebEngineEvent.AccessibleContentChanged`.
**Linux half open, and different now.** With WebKit it would have been a socket onto a
tree on someone else's bus. CEF off-screen gives the embedder a tree of plain data
(`cef_accessibility_handler_t`), so the web view's node grows real children that our
AT-SPI bridge serves like any others; `NativeContent` is the Windows shape and need not be
reused. Designed in step 5, from what the spike's question 9 shows. **Question 9 showed**
(2026-10-01): CEF hands over Chromium's `AXTreeUpdate` as data (nodes with role, name, states,
attributes, children, device-pixel bounds), full once and incremental after (a small form 32
nodes, a Wikipedia article ~5,100 nodes and 5 MB); nothing reaches AT-SPI by itself; CEF cannot
act on a node, but its node ids are DevTools' `backendDOMNodeId`, so actions go through the
DevTools session the engine already has for messages.

Not needed, checked: pixel-density math (`ScreenContentBox × Window.DisplayScale`
is device pixels; use it rather than `LocalPosition`, which ignores scrolling),
pointer capture (`CapturePointer`, released on up), wake from another thread
(`RequestRepaint`'s thread-pool hop, reused as is), IME area
(`TextInputHost.SetInputArea`, for the Linux engine).

**Drag and drop: open, and a platform decision.** In visual hosting the host is the
page's drop target: WebView2 wants `CompositionController3.DragEnter/DragOver/DragLeave/Drop`
with the real `IDataObject`, for files dropped from outside *and* for drags that start inside
the page (HTML5 drag and drop — a sortable list), whose OLE drag lands on our window like any
other. Our window's drop target is SDL's, and SDL (read from its source, 2026-09-30) reads
the data only in `Drop`; `DragEnter` and `DragOver` send a position and nothing else. So:

- during a hover nobody in HultUI knows what is being dragged — `DragData` is empty until
  the drop, for every element, not only the web view;
- the page cannot be offered the data while the pointer moves, so its `dragover` handlers
  cannot accept, and without an accepted `dragover` the browser does not deliver the drop;
- an in-page drag reaches SDL as an outside drop and loses everything but files and text.

Three ways on. **Chosen (2026-09-30): the second.**

1. **An engine-local wrapper.** The WebView2 engine replaces the window's drop target with
   its own, which forwards to the page's `CompositionController3` while the pointer is over
   a page and to SDL's target everywhere else. SDL's target is found through the window
   property OLE keeps it under (`OleDropTargetInterface`, undocumented but unchanged since
   Windows 95). Nothing changes for apps without a web view.
2. **HultUI's own drop target on Windows**, replacing SDL's in the platform layer: the data
   is read on `DragEnter`, so every element's `DragData` is filled during the hover, and a
   hosted engine can ask for the raw data object. The right shape, and a change to every
   app's drops (Vesper Commander's among them).
3. **Leave it** until an app needs it.

**Built.** `WindowsDropTarget` (in `HultUI.Platform.SDL3`) replaces SDL's target per window:
it revokes SDL's registration and registers its own, a hand-built COM object, on the window's
thread; a thread that cannot host OLE keeps SDL's. It reads files and text on `DragEnter`,
raises `DropBeginInput(nativeData)`, the files and text, and positions, and answers OLE on
each step from `DropFeedback` — whether an element accepted — which the window fills in
while it handles the input. A drag that leaves is `DropCancelInput`, a leave and no drop; the
old path took SDL's leave for a drop, so a zone that accepted anything got a drop with nothing
in it. `DragData.NativeData` carries the
`IDataObject*`. The web view element forwards drags from outside (platform data, files or
text — not a HultUI-internal drag) as `IWebEngineView.SendDrag`, and WebView2 forwards them to
`CompositionController3`, with a data object made from the files and text when the platform
gave none (`DropData`).

**Proven, by playing the drag through the registered target as OLE would, without a mouse**
(`OsDropTests`): an element knows the files during the hover and its answer reaches OLE (and
with SDL's target it heard nothing at all until the next pump — seen); a page gets
`dragenter` with the `Files` type and its acceptance reaches OLE.

**The drop into the page: proven by hand** (the owner, 2026-09-30, the sample with a real
mouse): files dragged from File Explorer onto the page's drop zone arrive by name; a
sortable list inside the page reorders by dragging (HTML5 drag and drop, the case WebView2's
tracker calls broken in composition hosting); a page item dragged onto another application
(the Run dialog) arrives as its text; and Vesper Commander's own drops work as before, now
with the copy, move and no-entry cursors during the hover.

**The late drop zone** (found by hand: a file flicked into the middle of a zone lit it half a
second late; moved slowly to its edge, at once; Edge at once either way). Our side is 7 ms.
WebView2 takes about 120 ms to tell the page a drag entered, and every `DragOver` it is given
meanwhile holds that up: from `DragEnter` to the page's acceptance, 4 s with every call passed
on (a fast mouse makes hundreds a second), 0.5 s at one per 50 ms, 0.25 s with a quiet 150 ms
and then one per 25 ms. The engine now passes hovers on at that pace and answers in between
with the last answer. Edge pays none of this: its drop target is in the browser's own process.
A first guess — that WebView2 re-asks the page only when the point moves — was wrong; it
came from a test flooding it with calls.

What the synthetic test could not do, for the record: played without a real `DoDragDrop`, the
browser never finishes the drag —
`drop` and `dragleave` never reach the page, and its `dragover` events keep coming after
`Drop` and `DragLeave` have been called and returned success. Two readings. The test plays a
drag with no `DoDragDrop` behind it, which a real one always has; or WebView2's visual
hosting cannot finish a drag, which its own tracker records for drags that start inside a
page (WebView2Feedback #5237, #5486: open, "never worked" in composition hosting). The real
drag settled it: the first. So `OsDropTests` pins the hover and the acceptance, and the drop
is a hand check.


**Known limits to document:** a web view inside a `ScrollView` loses the middle
button to the ancestor's autoscroll, and scroll chaining out of the page does not
exist — give it its own region. Moving an element between windows is unsupported
framework-wide; the element re-reads its window at render and re-parents the
engine if it changed, but nothing more.

## Implementation order

0. **Spikes.** W0 (WebView2 capture) — **done 2026-09-30, the design holds.** L0
   (CEF rendering one page off-screen on Fedora: ten questions, in
   `WEBVIEW-LINUX-BRIEF.md`) runs on the Linux machine before step 5. Nothing is built on an assumption either spike
   can break.
1. **`HultUI.WebView`** — **done 2026-09-30**: contract, controller, element,
   `FakeWebEngine`, seams W1 and W2, 33 headless tests (`tests/HultUI.WebView.Tests`),
   and `samples/HultUI.Samples.WebView` on the fake with 4 more
   (`tests/HultUI.Samples.WebView.Tests`), both in the cross-platform filter. The
   sample's dropdown test found that a press which closes a menu over the page leaves
   its release to land on the page, so the element forwards a release only for a
   button whose press it forwarded.
2. **WebView2, pixels** — **engine done 2026-09-30** (`src/HultUI.WebView.WebView2`):
   environment per user data folder, composition controller, capture into the sink,
   navigation, script, messaging, pointer and cursor, focus hand-off both ways,
   runtime detection. Every COM and WinRT call is a raw vtable call whose slot comes
   from `scripts/generate-webview2-interop.py`, which reads the C vtable structs in
   `WebView2.h` and the Windows SDK headers (`Interop/ComSlots.g.cs`); callbacks are
   hand-built COM objects (`ComHandler`). Proven by 5 real-window tests
   (`HultUI.UiTests/WebView2Tests`: pixels reach the sink *and* the window's front
   buffer, script and messages both ways, a refused navigation, clicks landing on the
   half they aimed at), 22 mapping tests that run anywhere
   (`HultUI.WebView.Engines.Tests`), and the sample's `--check`, which passes under
   NativeAOT in about 380 ms from launch with no trim or AOT warnings.
   Seams W3, W4 and W5 followed the same day, which **completes step 2**: the pointer
   leaving the window leaves the hover chain; a cursor changed by a style pass shows
   without another move (and `CursorType.None` now hides the cursor, beside a new
   `Progress`); and `AcceleratorKeyPressed` offers shortcuts — Ctrl or Alt held, or
   F1–F12, never typing, arrows or Escape — to the app, dispatched from the element
   through its ancestors; a handler that stops one keeps it from the page. A real-window
   test posts F2 to WebView2's focus window in-process and sees the app's handler run
   (and fails with the subscription removed). W5 needed no `Window` seam after all: the
   element dispatches the key itself. The hover fix found the same bug as `IsFocused`
   in `IsHovered` and `IsPressed`, fixed the same way.
3. **Windows zero-copy** — **done 2026-09-30, GL and Vulkan.** `GpuFrameSink` and
   `IGpuFrameRenderer` moved from Media to `HultUI.Core.Rendering` (the mode enum renamed
   `GpuSinkMode`), and the engine registers a renderer as mpv does. In GPU mode the
   capture thread only asks for a repaint; inside the frame the UI thread takes the
   newest frame, copies it on the GPU into a D3D11 texture registered with GL through
   `WGL_NV_DX_interop2`, locks it and blits it into the sink's framebuffer — or, on
   Vulkan, into the lent interop image through the hidden GL context. Measured with the
   sample's `--bench` (the spike's animated page, 1650×1072, 60 frames a second, five-second
   windows, noisy): **62–106 ms/s on GL and 19–94 on Vulkan** across JIT and NativeAOT
   runs, against 291–334 for the step-1 pixel path and 219 for the upload fallback the
   renderer keeps for drivers without the interop (`HULTUI_WEBVIEW2_INTEROP=0`). The AOT
   build publishes with no trim or AOT warnings and passes `--check` in ~360 ms. An encoded picture into Vulkan's linear-light target
   needs no conversion: the frame reports `ReferenceWhiteNits = 0` and the renderer
   composites it as encoded, which a swapchain capture confirmed. The element now draws
   before the first frame, as the video element does — that visit decides the mode.
   W7 turned out not to be needed for this: zero-copy on Vulkan worked with the one lent
   image as long as a window had one GPU source. A video and a page in one Vulkan window
   conflicted until W7 was done, after step 6 (see the seam).
4. **Windows accessibility — done.** The page's UIA tree hangs under the web view element,
   a point over the page resolves to the page's element, focus in the page is reported as
   the page's element, and all of it follows the window when it moves. Seam W8's Windows
   half, `ParentWindowWatch`, and a fix to the bridge's `ProviderOptions` values. Proven
   against real WebView2 by `WebViewAccessibilityTests` in the real-window suite (placement,
   hit test and the move each seen failing first; focus worked before and is pinned), and
   at the provider interfaces by
   `NativeContentUiaTests`. What a reader still meets that is not ours: the embedded
   browser's own views between the element and the document, as in any WebView2 app.
   One new contract member beside the two above: `IWebViewHost.Post`, because an engine
   that has to look again later is on a timer thread and its objects are not.
5. **Linux engine: CEF**, in the order `WEBVIEW-LINUX-BRIEF.md` gives: find and start
   libcef, pixels, input and IME (seam W6), navigation and messages, zero-copy,
   accessibility (seam W8's Linux half), then the bundle for Debian and Ubuntu.
6. **Polish — done.** Menus, dialogs, downloads, permissions, and drag and drop (below).
   Four requests, each answered exactly once because the engine or the page's stopped
   script waits: `ContextMenuRequested` (a HultUI context menu of the page's editing and
   copying entries, none of a browser's; the app may change them or take the menu over),
   `ScriptDialogRequested` (a HultUI dialog naming the page's host), `DownloadRequested`
   (cancelled unless the app says where) and `PermissionRequested` (denied unless the app
   allows). WebView2's own menu, dialogs, download flyout and permission prompt never show.
   One framework seam, W9: `Window.OpenContextMenu(items, point, onClosed)`, a menu whose
   entries arrive after the click, which tells its owner it closed only after the chosen
   item's action has run. The keyboard is taken from the page while a menu or dialog is open
   and given back after. Found: WebView2 reports the menu's location in CSS pixels, not
   device pixels; a page that starts a second download without a click is stopped by the
   browser's guard against automatic downloads; `Element.Focus()` does nothing inside an
   overlay (overlay content has no focus manager), so the dialog focuses its field through
   the window's. **Drag and drop**: option 2 below, chosen by the owner.

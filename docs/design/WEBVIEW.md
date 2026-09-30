# HultUI.WebView — Design

A web page as an element, shipped as packages an application opts into, so an
application that never shows a web page never carries an engine, a loader, or
the interop for one.

Written 2026-09-30 from a read of the framework as it stands. The Windows path
was spiked the same day (W0, `spikes/WebView2Capture`, results below); the Linux
path has not been. Where a claim still needs confirming on real hardware it says
**verify**. Three decisions were put to the owner and are
marked **DECIDED** (2026-09-30).

This reverses an earlier call. `docs/plans/10-capability-roadmap.md` lists
WebView as out of spirit; the external review of 2026-08-12 recommended a
satellite package if it ever shipped. That is the shape here, and the roadmap's
line changes when this lands.

## What an application gets

```csharp
IWebEngine engine = OperatingSystem.IsWindows() ? new WebView2Engine() : new WpeEngine();
var web = new WebViewController(engine, new WebViewOptions
{
    UserDataFolder = Path.Combine(appData, "WebView"),
});
web.Navigate(new Uri("https://example.org"));

Box.New().Children(
    Toolbar(web),                        // web.Title, web.CanGoBack, web.IsLoading are signals
    WebViewElement.New(web).FlexGrow(1));

web.MessageReceived += m => ...;          // window.chrome.webview.postMessage / its WPE twin
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
  HultUI.WebView.Wpe/        Linux engine: WPE WebKit through its WPEPlatform API, hand P/Invoke
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

**Accessibility.** `ICoreWebView2CompositionController2.AutomationProvider`
returns the page's UIA root. **W0: it is an *override* provider**
(`ProviderOptions` = ServerSide | OverrideProvider | UseComThreading), with no
parent and a client-relative rectangle, and
`ICoreWebView2Environment4.GetAutomationProviderForWindow(Chrome_WidgetWin_0)`
returns the same object. The IDL says why: hosts are expected to implement
`IRawElementProviderHwndOverride` and answer for WebView2's focus window with
it. So on Windows the bridge's window provider gains that interface (seam W8)
rather than splicing a fragment into our tree. Until W8 lands the element is
announced as a named `Group` with nothing inside it.

**NavigationStarting has no deferral** (the IDL offers none), so the answer must be
given before the handler returns. It is: WebView2 raises it on the UI thread, where the
controller answers synchronously. An engine that raises elsewhere (WPE) keeps the
request's continuation instead.

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

## Linux engines

**DECIDED (Linux engine): WPE WebKit.** The comparison it was chosen from:

| | WPE WebKit (WPEPlatform) | CEF off-screen | WebKitGTK | None for now |
|---|---|---|---|---|
| Engine | WebKit (Safari-class) | Chromium (same as WebView2) | WebKit | — |
| How it gets into our window | We implement a `WPEDisplay`/`WPEView`; frames arrive as `WPEBuffer` (dmabuf, or shared memory without a DRM device) through `render_buffer` | `OnPaint` gives BGRA with dirty rects; `OnAcceleratedPaint` gives dmabuf planes | It cannot: a GTK widget, no foreign embedding on Wayland, XEmbed is GTK3/X11 only | A placeholder; OAuth goes through the system browser and a loopback redirect |
| Frame path phase 1 | shm / mapped buffer → `PixelFrameSink` | `OnPaint` → `PixelFrameSink` | — | — |
| Zero-copy | dmabuf → EGLImage (GL) / `VK_EXT_external_memory_dma_buf` (Vulkan) | dmabuf, but CEF's Linux shared-texture path breaks on NVIDIA today (cef#4237, open) | — | — |
| Input | Everything forwarded as `WPEEvent` — keyboard too; IME through a `WebKitInputMethodContext` we feed from SDL's text-editing events | Everything forwarded (`SendMouseEvent`, `SendKeyEvent`, IME composition calls) | — | — |
| Popups, menus, dialogs, file chooser | Signals to the embedder: we draw `<select>` and context menus with HultUI's Dropdown/ContextMenu and use HultUI's file dialogs | Popup widget painted into a second buffer we composite; dialogs via handlers | — | — |
| Accessibility | WebKit speaks AT-SPI over D-Bus itself; our bridge references its root as the element's child (**verify** the embed API) | `CefAccessibilityHandler` sends raw tree updates; we would build the whole AT-SPI tree ourselves | — | — |
| What the app ships | Nothing: a distro package (`libwpewebkit-2.0`) | libcef + resources + a helper executable: ~110 MB compressed, ~350 MB installed (Arch's package), plus sandbox setup | — | Nothing |
| Security updates | The distribution's | Ours, every Chromium release | — | — |
| Maturity risk | WPEPlatform became stable and default only in 2.54 (Sept 2026); the libwpe API is now deprecated. Debian testing has 2.54, Debian stable 2.48; Ubuntu 24.04 (our CI runner) is older; Fedora **verify** | Mature; the heavy parts are packaging and the Linux GPU path | — | None |
| Rendering parity with Windows | Different engine: pages may differ from WebView2 | Same engine as WebView2 | — | — |

**Why WPE WebKit.** It is the only engine built for exactly this
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

**Threading on Linux (WPE).** WPE runs on a GLib main context. It gets a
dedicated thread with its own `GMainLoop`; controller calls marshal onto it and
events marshal back with `InvokeOnMainThread`. Frames arrive on that thread and go
into the sink like `FrameArrived` on Windows.

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
handler returns; WPE, on its own thread, waits for the continuation.

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
(`SetVirtualHostNameToFolderMapping` / a WPE URI scheme handler) under a fixed
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
zero elsewhere; WPE needs none, since we implement its display ourselves). Core
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
international keys to `Unknown`; WPE wants the hardware keycode (evdev) and
derives the keysym itself. Add `KeyDownEvent.ScanCode`/`KeyUpEvent.ScanCode`.

**W7 — Per-source interop images on Vulkan (`HultUI.Platform.SDL3`,
`HultUI.Rendering.Skia`).** The Vulkan surface lends *one* interop image and
reallocates it when the requested size changes, so two GPU sources in one window
of different sizes — a web view and a video, or two videos today — fight over it.
Lend per source. Needed for zero-copy on Vulkan (phase 3), not before.

**W8 — Foreign accessibility subtree (`HultUI.Core`, both bridges).** Neither
bridge can host a subtree it did not build. On Windows W0 showed WebView2's
provider is an HWND *override* provider for its focus window, so the Windows half
is `IRawElementProviderHwndOverride` on the window provider, returning
`GetAutomationProviderForWindow(hwnd)`; where that lands in the tree relative to
the element is for the real-window suite to show. The general splice below is
what AT-SPI needs. UIA today: `UiaFragment.Navigate` returns
only our fragments, `GetEmbeddedFragmentRoots` is null, hit testing and focus
only know our snapshot, and `NativeProvider.LogicOf` is undefined for a foreign
pointer. AT-SPI: children are local paths on our bus only, and no node offers a
`Socket`. The seam: an `AccessibilityNode` slot for an external root, set from an
element hook in `AccessibilityTreeBuilder`; on Windows the host fragment returns
the AddRef'd foreign provider as its child and delegates point and focus queries
into it; on Linux a child may be a `(bus, path)` pair and the host node
implements `Socket.Embedded`. Phase 4, after the page works.

Not needed, checked: pixel-density math (`ScreenContentBox × Window.DisplayScale`
is device pixels; use it rather than `LocalPosition`, which ignores scrolling),
pointer capture (`CapturePointer`, released on up), wake from another thread
(`RequestRepaint`'s thread-pool hop, reused as is), IME area
(`TextInputHost.SetInputArea`, for the Linux engine).

**Not in the first version:** files dropped onto a page. SDL owns the OLE drop
target and `DragData` carries only files and text, while WebView2's
`CompositionController3.Drop` wants the `IDataObject`. That is a platform seam of
its own, for later.

**Known limits to document:** a web view inside a `ScrollView` loses the middle
button to the ancestor's autoscroll, and scroll chaining out of the page does not
exist — give it its own region. Moving an element between windows is unsupported
framework-wide; the element re-reads its window at render and re-parents the
engine if it changed, but nothing more.

## Implementation order

0. **Spikes.** W0 (WebView2 capture) — **done 2026-09-30, the design holds.** L0
   (WPE rendering one page into our window on Wayland) needs a Linux machine with
   WPE 2.54 and runs before step 5. Nothing is built on an assumption either spike
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
   **W7 is not done** and turned out not to be needed for this: zero-copy on Vulkan works
   with the one lent image as long as a window has one GPU source. A video and a page in
   one Vulkan window still conflict (the second lend is refused and draws nothing); fixing
   it means generalising the host's single image — its semaphores, release bookkeeping
   and GL objects, hardened in plan 15 and verified on Linux — to one per source.
4. **Windows accessibility**: `IRawElementProviderHwndOverride` on the UIA
   bridge's window provider for WebView2's focus window; proven in the real-window
   suite.
5. **Linux engine**, same order: pixels, input, zero-copy, accessibility.
6. **Polish**: HultUI context menu and script dialogs over the engine's, downloads,
   drag and drop into the page.

# The web view on Linux: a brief for a Claude Code session there

Steps 1 to 4 of `docs/design/WEBVIEW.md` are built and verified **on Windows only**: the
contract and element (`src/HultUI.WebView`), the WebView2 engine
(`src/HultUI.WebView.WebView2`), zero-copy on GL and Vulkan, and Windows accessibility. Step 5
is the Linux engine. This file is what a session on the Linux machine needs to do it without
any other context: the decision, the machine, a spike with ten questions in order, and then the
build.

Nothing here has run on Linux. Where a line says **verify**, it is a belief, not a finding.
**Write what you find into this file** (results under each question) and into
`docs/design/WEBVIEW.md` where it changes the design. The Windows session's memory is not on
your machine; the documents are the only thing both sides share.

## The decision, and why it changed

**DECIDED 2026-09-30: the Linux engine is CEF (Chromium Embedded Framework), rendered
off-screen and drawn by HultUI.** The earlier choice, WPE WebKit, is withdrawn.

The owner's rule: an app must run on **Fedora with only Fedora's packages, RPM Fusion and
official COPRs**, and on **Debian and Ubuntu** as well. Checked the same day:

- **WPE WebKit is not in Fedora.** `packages.fedoraproject.org` has no `wpewebkit` for
  Fedora 43 to 45, Rawhide or EPEL. It exists only in personal COPRs (`philn/wpewebkit`).
- **CEF is.** `cef` and `cef-devel`, maintained (ngompa, dherrera), in Fedora 43, 44, 45 and
  Rawhide; 146.0.11 on the stable releases, 151 in Rawhide on 2026-09-30. Arch has it too.
- **WebKitGTK 6.0** is in Fedora but is a GTK widget with no off-screen mode; it cannot be
  composited. **Qt WebEngine** is in Fedora but is C++ only and brings Qt's event loop; it
  would need a native shim we build and ship.

So CEF is the one engine Fedora ships that can hand its pixels to an embedder. It was
rejected before on size (about 350 MB installed) and on a bug in its Linux zero-copy path on
NVIDIA. On Fedora the size is the distribution's, not the app's. The bug is still open
(cef#4237: shared-texture OSR asks for CPU-mappable buffers, which NVIDIA cannot render into)
and costs only zero-copy there, not the page.

**Debian and Ubuntu do not package CEF** (not re-checked on 2026-09-30; **verify** with
`apt-cache search cef`). There the app carries it: see *Where libcef comes from*.

What this costs against the WPE plan, so nobody is surprised:

- **Accessibility is ours to build.** WebKit speaks AT-SPI itself. CEF off-screen hands the
  embedder raw tree updates (`cef_accessibility_handler_t`), and the AT-SPI nodes are ours.
- **The ABI moves.** Fedora bumps CEF's major version inside a stable release (144 to 146 on
  Fedora 43 during 2026). CEF's versioned API (below) is what makes that survivable.
- **A helper process.** Chromium is multi-process; something has to be the renderer's
  executable.
- **Security updates on Debian and Ubuntu are ours**, with every Chromium release.

## What is fixed, and what is yours to decide

Fixed by the design, and by what Windows already ships:

- **Composited.** HultUI draws the page; nothing is a child window. Every popup in HultUI is
  an in-canvas overlay, and a native child would cover them.
- **The contract.** `IWebEngine`, `IWebEngineView`, `IWebViewHost`, `WebEngineEvent`
  (`src/HultUI.WebView/IWebEngine.cs`, `WebEngineEvent.cs`). The controller, the element and
  the 37 headless tests do not know which engine they have. Read `WebView2View.cs` as the
  worked example of an engine, not as something to mirror line by line.
- **The package.** `src/HultUI.WebView.Cef`, engine class `CefEngine`, `Name` = `"CEF"`.
  `net10.0`, in `HultUI.CrossPlatform.slnf`, and it must publish under NativeAOT with no
  warnings: every app here does.
- **No C++.** The engine talks to `libcef.so` through CEF's **C API**
  (`/usr/include/cef/include/capi/`), by hand, the way WebView2 is talked to through COM
  vtables. CEF's structs are the same idea — a ref-counted base followed by function
  pointers — only inline instead of behind a vtable pointer.

Yours to decide, from what the spike shows: the message loop model, the helper process, how
script results and page messages travel, which Ozone platform to ask for, and the shape of the
accessibility seam on Linux.

## The machine

A **Proxmox container** (LXC) on a 12700K, with the **integrated GPU** (Intel UHD 770) passed
in, and a **fresh install of the latest Fedora**. On 2026-09-30 that is Fedora 44; Fedora 45
is in beta with its release planned for 20 October 2026. Either carries CEF 146.

Three things about a container that a desktop does not have. All three are **verify**:

- **No monitor.** HultUI needs a Wayland (or X11) display to open a window, and so does any
  zero-copy path. Run a headless compositor that renders on the GPU. A starting point, all
  from Fedora's packages:

  ```bash
  sudo dnf install sway wayvnc grim
  export XDG_RUNTIME_DIR=/run/user/$(id -u)
  WLR_BACKENDS=headless WLR_LIBINPUT_NO_DEVICES=1 LIBSEAT_BACKEND=noop \
  WLR_RENDERER=gles2 WLR_RENDER_DRM_DEVICE=/dev/dri/renderD128 sway &
  export WAYLAND_DISPLAY=wayland-1
  wayvnc 0.0.0.0 5900 &          # look at it from another machine
  grim shot.png                  # what the compositor shows
  ```

- **The GPU is a device file.** The host has to pass the render node into the container
  (Proxmox 8.2 or later: `pct set <id> -dev0 /dev/dri/renderD128,gid=<gid of "render" in the
  container>`, and the same for `/dev/dri/card0` with the `video` group). Inside, check
  before anything else: `ls -l /dev/dri`, `eglinfo -B`, `vainfo`. If the render node is
  missing, stop and say so: that is the owner's to fix on the host.
- **Chromium's sandbox wants user namespaces.** An unprivileged container may refuse them
  unless it has `nesting=1`. Fedora ships `/usr/lib64/cef/chrome-sandbox` as the other route.
  Question 1 below finds out. `no_sandbox` is acceptable **for the spike only**, and the
  result must say which of the three it was.

Set up:

```bash
sudo dnf install cef cef-devel dotnet-sdk-10.0 clang zlib-devel mesa-dri-drivers \
                 mesa-libEGL-devel egl-utils libva-utils git
git clone git@github.com:fredrikhu/hului-revamped.git && cd hului-revamped
git checkout claude/webview-component-repo-62ba8b
dotnet build HultUI.CrossPlatform.slnf
dotnet test tests/HultUI.WebView.Tests          # 37 pass on the fake engine, no browser
dotnet run --project samples/HultUI.Samples.WebView -- --fake   # a HultUI window in the compositor
```

If the last line does not put a window on the compositor, fix that first; nothing below
means anything until HultUI itself draws there. `dotnet-sdk-10.0` from Fedora is **verify**
(`dnf search dotnet-sdk`).

What Fedora's package holds (Fedora 44, read from the package index):

```
/usr/lib64/cef/libcef.so            chrome-sandbox      icudtl.dat
               resources.pak        chrome_100_percent.pak  chrome_200_percent.pak
               v8_context_snapshot.bin   locales/*.pak
               libEGL.so  libGLESv2.so  libvk_swiftshader.so  libvulkan.so.1
/usr/include/cef/include/capi/*.h   include/cef_api_hash.h   include/cef_api_versions.h
/usr/src/cef-<version>/libcef_dll/  (the C++ wrapper's sources; not used)
```

There is **no helper executable** in it and no pkg-config file. RPM provides `cef(abi)` and
`cef(api)`.

## Rules that carry over from Windows

Each of these was a bug first.

- **Copy struct layouts from the headers, never from memory.** One wrong slot in WebView2's
  vtables cost an afternoon. The Windows engine generates its slots from the C headers
  (`scripts/generate-webview2-interop.py` → `Interop/ComSlots.g.cs`). Do the same for CEF's
  `capi` headers once the spike has settled which structs are needed; for the spike,
  hand-written structs checked against the header are fine.
- **Never call into the engine from inside a draw.** The element reports placement after the
  frame (`WebViewElement.FlushPlacement`).
- **A thread the frame waits for must never post to the UI thread directly.**
  `IWebViewHost.RequestRepaint` is safe from any thread because the controller hops through
  the thread pool; use it, and `IWebViewHost.Post` for work that must run on the UI thread.
- **The UI thread must never block on the engine's thread**, if the engine has one. The
  reverse is allowed and is how a cancellable navigation gets its answer.
- **The element draws before the first frame.** That visit decides whether the sink is in
  pixel or GPU mode; an engine that waits for a draw to start producing frames deadlocks.
- **The accessibility tree is rebuilt only when something says it is stale.** Raise
  `WebEngineEvent.AccessibleContentChanged`; the element turns it into
  `NotifyAccessibilityPropertyChanged`.
- **In a test, wait for the page to say it is ready** (a message it posts), not for the first
  frame. A frame only proves the capture started.
- **One `dotnet test` at a time**, and never build while one runs: they share `bin/`.
- **Code with backslashes goes through the editor's write tool, not a shell heredoc.**

## Spike L0: ten questions, cheapest first

A console program under `spikes/CefOsr/`, results in its `README.md` as a table, the way
`spikes/WebView2Capture/README.md` reports W0. No HultUI until question 7. Stop and report if
1 or 2 fails: everything else stands on them.

**1. Does Fedora's libcef load and start, windowless, from a .NET program?**
`dlopen("/usr/lib64/cef/libcef.so")`, then `cef_api_hash(version, 0)` **first**, then
`cef_initialize` with `windowless_rendering_enabled`. Find out:
- whether it finds `icudtl.dat`, the snapshot and the `.pak` files beside itself, or needs
  `resources_dir_path` / `locales_dir_path`, or the working directory changed;
- what the helper process is: the same executable started again with `--type=…` (then
  `cef_execute_process` is the first thing `Main` does), or a separate small executable named
  in `browser_subprocess_path`. Prefer whichever asks nothing of the app's `Main`;
- the sandbox in this container: works, works with `nesting=1`, works with `chrome-sandbox`,
  or only with `no_sandbox`;
- which Ozone platform it starts on with no flags, and with `--ozone-platform=wayland`,
  `x11` and `headless`.

**2. Does the versioned API hold?** Since M133 a client names the API version it was written
against (`CEF_API_VERSION=XXXZZ`; the list is `cef_api_versions.h`) in the first call to
`cef_api_hash`, and a library that still supports that version keeps its ABI for it. A
mismatch is a fatal error, not a return code. Find out which version Fedora's 146 offers, and
then **run the same binary against a newer libcef**: download the official minimal
distribution of the current release for `linux64` from `cef-builds.spotifycdn.com`, unpack it
under `~/cef-new`, and point the spike at it. This one test stands for both Fedora bumping a
major version and an app bundling CEF on Debian. If it fails, the engine has to be built per
CEF version and the plan below changes.

**3. Pixels.** One browser with `windowless_rendering_enabled`, a render handler whose
`get_view_rect` and `get_screen_info` answer 800×600 at scale 1.5, a `data:` page. Does
`on_paint` deliver BGRA at 1200×900? Save a PNG and look at it. Time from `create_browser` to
the first frame. Does a still page stop painting, and an animating one paint at
`windowless_frame_rate`?

**4. The message loop.** Two candidates: `multi_threaded_message_loop` (CEF gets its own UI
thread; callbacks arrive there) and `external_message_pump` (`cef_do_message_loop_work` on
HultUI's thread when `on_schedule_message_pump_work` asks). Which threads do `on_paint`,
`on_title_change` and `on_before_browse` arrive on in each? Does each shut down cleanly, with
no zombie processes (`pgrep -f type=`)? The contract allows either: `IWebEngineView.Event`
may be raised from any thread.

**5. Input and focus.** `send_mouse_move_event`, `send_mouse_click_event`,
`send_mouse_wheel_event`; `send_key_event` for a letter, Enter, Tab and an arrow; `set_focus`.
What does `cef_key_event_t` need to make a page's `keydown` and a text field both right:
`windows_key_code`, `native_key_code`, `character`? That answers seam W6 (the raw scan code
HultUI's key events do not carry yet). Then a `<select>`: CEF paints its list as a second
buffer (`PET_POPUP`, with `on_popup_show` and `on_popup_size`); confirm, and that it can be
composited over the page at the rectangle given. Then `ime_set_composition` and
`ime_commit_text`, and `on_ime_composition_range_changed` for where the candidates go.

**6. Script results and page messages without code in the renderer.** CEF's
`execute_java_script` returns nothing, and its message router is C++ only. Try the DevTools
protocol from the browser process: `execute_dev_tools_method` with `Runtime.evaluate`
(`returnByValue`) for `ExecuteScriptAsync`, and `Runtime.addBinding` plus the
`Runtime.bindingCalled` event (through `add_dev_tools_message_observer`) for
`window.chrome.webview.postMessage`'s twin. If that works, the helper process needs no code of
ours at all. Also needed: the posting frame's origin, for `WebViewOptions`' allowed origins.
If it does not work, the fallback is a render process handler in the helper that adds the
binding in `on_context_created` and sends a process message.

**7. Policy hooks.** `on_before_browse` returning 1 cancels a navigation, synchronously;
`on_before_popup` returning 1 refuses a new window and tells us the URL;
`on_render_process_terminated` fires when the renderer is killed; a scheme handler factory
serves `https://app.local/` from a folder.

**8. Zero-copy.** `shared_texture_enabled` on the window info, and `on_accelerated_paint`
hands over dmabuf planes (fd, stride, offset, modifier, format). On the Intel GPU: does it
arrive at all, and on which Ozone platform? Import it in a GL context with
`EGL_EXT_image_dma_buf_import` and draw it; then the same through
`VK_EXT_external_memory_dma_buf`. Measure CPU with an animating page (the one in
`samples/HultUI.Samples.WebView/Program.cs`, `AnimatedPage`) at 1650×1072 and 60 frames a
second, against `on_paint`. Windows for comparison: about 300 ms/s copying pixels, 62–106 on
GL and 19–94 on Vulkan zero-copy. Who owns the fds, and for how long is a frame valid?

**9. Accessibility.** `set_accessibility_state(STATE_ENABLED)` and a render handler that
returns an accessibility handler. What does `on_accessibility_tree_change` carry (node ids,
roles, names, child lists, as a `cef_value_t` tree), how large is it for a real page, are the
updates incremental, and what does `on_accessibility_location_change` add? Is there any way to
act on a node (focus, click) by id? Check with `accerciser` or `busctl` whether Chromium puts
anything on the AT-SPI bus by itself when windowless. **Expect not.**

**10. Two pages and the way out.** Two browsers on one `cef_initialize`; close one; close
both; `cef_shutdown`. What must be released, in what order, for the process to exit by itself?

## Where libcef comes from

The engine looks for a CEF directory (one holding `libcef.so` and its resources) in this
order, the way the mpv backend looks for libmpv and the Windows engine for its loader:

1. `HULTUI_CEF_DIR`.
2. `<app directory>/cef/`.
3. `<app directory>/runtimes/<rid>/native/cef/`.
4. The distribution's: `/usr/lib64/cef` (Fedora), `/usr/lib/cef` (Arch).

`CefEngine.Availability` reports a missing CEF with the package to install
(`sudo dnf install cef`), and never throws.

On **Fedora** an app declares a dependency on `cef` and ships none of it. On **Debian and
Ubuntu** the app ships it, from the official builds (`cef-builds.spotifycdn.com`, minimal
distribution, `linux64` and `linuxarm64`), about 110 MB compressed. Whether that is a NuGet
package per RID (`HultUI.WebView.Cef.Natives.linux-x64`), built by a workflow as the mpv
natives are, or a download the app's packaging does, is decided after question 2: it only
works at all if one engine binary runs against more than one CEF.

Two things to write down for whoever ships an app, not to solve: the official builds have no
H.264 or AAC, while Fedora's use the system's ffmpeg (RPM Fusion's gives the full set); and a
bundled CEF needs its licence files shipped beside it.

## Step 5, after the spike

In this order; each ends with its tests green and the step written into
`docs/design/WEBVIEW.md`'s implementation order as steps 1 to 4 are.

1. **`HultUI.WebView.Cef`: the directory search, `Availability`, initialise and shut down.**
   Struct layouts generated from the `capi` headers. Tests in
   `tests/HultUI.WebView.Engines.Tests`, skipped when no CEF is found (the CI runner is
   Ubuntu and has none).
2. **Pixels.** `on_paint` into `IWebViewHost.Pixels`. `SetPlacement` gives device pixels and a
   scale: the view rectangle is pixels divided by scale. The sample's `--check` passes on
   Linux, JIT and NativeAOT.
3. **Input, focus, IME.** `Capabilities` does **not** include `NativeKeyboard`: the element
   then forwards keys and text through `SendKey` and `SendText`, a path only the fake engine
   has exercised so far. Seam W6 lands here. Shortcuts are simpler than on Windows: the
   element sees every key first and can offer it to the app before sending it on. The
   `<select>` popup is composited by the engine into the same sink. The IME's candidate
   position goes to `TextInputHost.SetInputArea`.
4. **Navigation, script, messages, policy.** Everything in `WebEngineEvent`, by whichever
   route question 6 chose. That includes the four requests Windows added in step 6, each
   answered exactly once and each with a default that asks nothing of the app:
   `ContextMenuRequested` (CEF's context menu handler hands over its model and a callback;
   the element draws the menu), `ScriptDialogRequested` (the JS dialog handler, and the
   before-unload one), `DownloadRequested` (the download handler: cancelled unless the app
   says where) and `PermissionRequested` (the permission handler: denied unless the app
   allows). The handler names are from memory; take them from the `capi` headers. CEF's
   menu point is worth checking against the click, in which units, as WebView2's needed.
   **Drags** reach the element as HultUI drag events and go to the engine through
   `IWebEngineView.SendDrag` (CEF: the browser host's drag-target calls, with a drag data
   object built from the files and text). On Windows the window's drop target is HultUI's own
   and fills `DragData` while the drag hovers; on Linux it is still SDL's, which learns the
   files only on the drop, so a page's drop zone cannot accept during the hover there. Fixing
   that is the Linux twin of `WindowsDropTarget` (XDND and the Wayland data device under
   SDL), a platform change of its own; note it, do not start it unasked. A drag that starts in
   the page comes to the host through CEF's start-dragging callback, which the host must turn
   into a real drag.
5. **Zero-copy.** A `IGpuFrameRenderer` registered through `IWebViewHost.UseGpuRenderer`, as
   the Windows engine's `CaptureRenderer` and the mpv backend do. Seam W7 is done: on
   Vulkan each GPU source is lent a shared image of its own, with its own semaphores.
   It ran on Windows only, so the fd export of those semaphores is unproven: once CEF
   draws, show two pages, or a page and a video, in one Vulkan window and check a
   `HULTUI_VULKAN_CAPTURE` of it, as `VulkanProbeTests` does with two WebView2 pages.
6. **Accessibility.** The Linux half of seam W8 is not what `WEBVIEW.md` sketched for
   WebKit (a socket onto someone else's tree). CEF gives a tree of plain data, so the web
   view's node grows real children that our AT-SPI bridge serves like any others. Design it
   from question 9's answer; `AccessibilityNode.NativeContent` is the Windows shape and need
   not be reused.
7. **Debian and Ubuntu.** The natives package or download, and a run on each.

## When you stop

Commit on `claude/webview-component-repo-62ba8b` with the results in the documents, and push
if the owner has said to. The Windows side is done: step 6 (polish) and seam W7.

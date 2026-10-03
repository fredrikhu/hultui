# The web view on Linux: a brief for a Claude Code session there

Steps 1 to 4 and 6 of `docs/design/WEBVIEW.md`, and seam W7, are built and verified **on
Windows only**, and are on `main` since 2026-09-30: the contract and element
(`src/HultUI.WebView`), the WebView2 engine (`src/HultUI.WebView.WebView2`), zero-copy on GL
and Vulkan, Windows accessibility, and the page's menus, dialogs, downloads, permissions and
drags. Step 5 is the Linux engine. This file is what a session on the Linux machine needs to do it without
any other context: the decision, the machine, a spike with ten questions in order, and then the
build.

**Spike L0 ran on 2026-10-01** on the machine below. Every question has its result under it
(and `spikes/CefOsr/README.md` has the detail and raw output). The decisions it informs are
recommended in *Spike L0: recommendations*, for the owner to settle before step 5 starts.
Where a line still says **verify**, it is a belief, not a finding.
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
  the headless tests in `tests/HultUI.WebView.Tests` do not know which engine they have. Read `WebView2View.cs` as the
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
  container>`). **Only the render node is passed in**: the headless compositor and Chromium
  both draw through it, and the host has no `card0` (its display node is numbered otherwise),
  so nothing here may assume a `/dev/dri/card*`. Inside, check before anything else:
  `ls -l /dev/dri`, `eglinfo -B`, `vainfo`. If the render node is missing, stop and say so:
  that is the owner's to fix on the host.
  **Found (2026-10-01):** `renderD128` (group `render`) and a `card1` (group `video`) are
  both there. `eglinfo -B`: Mesa 26.2.3 on the UHD 770 through GBM, surfaceless and device
  platforms; `vulkaninfo`: the UHD 770 through Mesa's Intel driver, and llvmpipe. `vainfo`
  finds no driver (`intel-media-driver` is RPM Fusion's), so video decode is software.
  Sway headless on `renderD128` with GLES2 works as written above.
- **Chromium's sandbox wants user namespaces.** An unprivileged container may refuse them
  unless it has `nesting=1`. Fedora ships `/usr/lib64/cef/chrome-sandbox` as the other route.
  Question 1 below finds out. `no_sandbox` is acceptable **for the spike only**, and the
  result must say which of the three it was.

Set up:

```bash
sudo dnf install cef cef-devel dotnet-sdk-10.0 clang zlib-devel mesa-dri-drivers \
                 mesa-libEGL-devel egl-utils libva-utils git \
                 mesa-vulkan-drivers vulkan-loader vulkan-tools
vulkaninfo --summary     # the Intel GPU through Mesa's driver, for the Vulkan renderer and question 8
git clone git@github.com:fredrikhu/hului-revamped.git && cd hului-revamped
git checkout claude/webview-component-repo-62ba8b
dotnet build HultUI.CrossPlatform.slnf
dotnet test tests/HultUI.WebView.Tests          # 64 pass on the fake engine, no browser
dotnet run --project samples/HultUI.Samples.WebView -- --fake   # a HultUI window in the compositor
```

If the last line does not put a window on the compositor, fix that first; nothing below
means anything until HultUI itself draws there. `dotnet-sdk-10.0` from Fedora is **verify**
(`dnf search dotnet-sdk`).

**Found (2026-10-01):** all of it holds on Fedora 44. `dotnet-sdk-10.0` is Fedora's
(10.0.112), sway 1.11 and wayvnc 0.9.1 likewise. The build has no errors; the fake-engine suite
passes 64 of 64; `--fake` opens the window in sway through SDL's Wayland driver with GL on the
UHD 770 (`gfx: video=wayland renderer="Mesa Intel(R) UHD Graphics 770"`), drawn. Sway's
headless output starts at 1280×720; `swaymsg output HEADLESS-1 resolution 1920x1080` makes
room for question 8's 1650×1072 page.

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

> **Result (2026-10-01): yes, but the helper must be native.** Details and raw output:
> `spikes/CefOsr/README.md`, question 1.
> - `cef_api_hash(14600, 0)` first returns the Linux hash `cef_api_versions.h` lists;
>   `cef_initialize` = 1, context ready ~200 ms after process start; a `data:` page painted
>   85–100 ms after `create_browser_sync`. Fedora's 146 accepts API versions 13300–14600, and
>   its RPM provides `cef(api) = 13300 … 14600`: an app package can require the API version.
> - **Resources** are found beside `libcef.so`, no `resources_dir_path`, any working directory.
> - **Helper: neither shape works with .NET.** Chromium's zygote has to be single-threaded to
>   enter the sandbox, and cannot fork a .NET process even unsandboxed. The app run again,
>   a NativeAOT helper (2 threads at `Main`: its finalizer) and a JIT helper (7) all fail:
>   sandbox on, the zygote never reports (`FATAL zygote_host_impl_linux.cc:221`); sandbox off,
>   the GPU process never launches. Only `--no-sandbox --no-zygote` gets a .NET helper running.
>   **A 20-line C helper** (`dlopen`, `cef_api_hash`, `cef_execute_process`) in
>   `browser_subprocess_path` works with the sandbox on and asks nothing of `Main`. It finds
>   libcef through `HULTUI_CEF_DIR`, set with libc `setenv` before `cef_initialize`. It is the
>   engine's one piece of native code.
> - **Sandbox: works, namespace sandbox** (renderers in their own user, PID and network
>   namespaces, seccomp-bpf). The setuid `chrome-sandbox` beside `libcef.so` also works, found
>   without `CHROME_DEVEL_SANDBOX`. `no_sandbox` was not needed and no later run uses it.
>   Whether the host set `nesting=1` cannot be seen from inside; `unshare -U` works.
> - **Ozone: ask for it outright** (which one: questions 3 and 8 settle it: the window's own,
>   `wayland` with `--disable-features=WaylandFractionalScaleV1`, or `x11`). No flag means X11, which fails here (no X server);
>   `--ozone-platform-hint=auto` follows `XDG_SESSION_TYPE`, unset in this shell. `wayland`
>   works with the GPU: WebGL is ANGLE on GL ES 3.2 on Mesa, UHD 770. `headless` paints but
>   has no WebGL. `x11` fails for want of a server. CEF passes
>   `--render-node-override=/dev/dri/renderD128` to its children itself.

**2. Does the versioned API hold?** Since M133 a client names the API version it was written
against (`CEF_API_VERSION=XXXZZ`; the list is `cef_api_versions.h`) in the first call to
`cef_api_hash`, and a library that still supports that version keeps its ABI for it. A
mismatch is a fatal error, not a return code. Find out which version Fedora's 146 offers, and
then **run the same binary against a newer libcef**: download the official minimal
distribution of the current release for `linux64` from `cef-builds.spotifycdn.com`, unpack it
under `~/cef-new`, and point the spike at it. This one test stands for both Fedora bumping a
major version and an app bundling CEF on Debian. If it fails, the engine has to be built per
CEF version and the plan below changes.

> **Result (2026-10-01): it holds.** Fedora's 146 offers API versions 13300–14600. The spike,
> generated from 146's headers at **14600**, ran unchanged (same binary, same C helper) on the
> official **CEF 154.0.32** (Chromium 154.0.8037.58, current stable), which offers
> 13300–15400. The hash matched, `cef_initialize` = 1, the page painted 59 ms after
> `create_browser`, WebGL ran on the GPU, the sandbox was on, and every reference came back.
> Generating from 154's headers at 14600 gives the identical structs, offsets and exports
> (six new error-code values aside). So one engine binary serves Fedora across major bumps and
> a bundled CEF; the plan below stands. Two things a bundle needs: **a flat directory**
> (`Resources/` beside `libcef.so`; `resources_dir_path` does not cover `icudtl.dat`, and CEF
> dies on ICU without it), and **`strip`** (the official `libcef.so` is 1.45 GB with debug
> sections, 272 MB stripped; everything stripped compresses to 115 MB with xz).

**3. Pixels.** One browser with `windowless_rendering_enabled`, a render handler whose
`get_view_rect` and `get_screen_info` answer 800×600 at scale 1.5, a `data:` page. Does
`on_paint` deliver BGRA at 1200×900? Save a PNG and look at it. Time from `create_browser` to
the first frame. Does a still page stop painting, and an animating one paint at
`windowless_frame_rate`?

> **Result (2026-10-01): yes on `headless`, not on `wayland`.** With
> `--ozone-platform=headless --use-angle=gl-egl` an 800×600 view at 1.5 paints **1200×900**
> BGRA (top-down, `width×4` rows, opaque alpha). A 100-CSS-pixel box is 150 pixels, and the
> GPU does the work: WebGL is ANGLE on GL ES 3.2 on Mesa. On `--ozone-platform=wayland` the
> same answers paint **800×600 at scale 1**. Nothing changed that:
> `notify_screen_info_changed`, `--force-device-scale-factor`, or sway's output scale. Plain
> `headless` falls back to SwiftShader, which CEF 146 refuses (no WebGL); `--use-angle=vulkan`
> works too. Headless never connects to the display, so it is the same under Wayland and X11.
> ~~The engine asks for `headless` + `gl-egl`.~~ **Revised by question 8:** headless gives no
> shared textures, and Wayland's scale needs only `--disable-features=WaylandFractionalScaleV1`
> (X11's needs nothing), so the engine asks for the window's platform. First frame 85–93 ms after
> `create_browser`. A still page stops painting (0 in 2 s; `invalidate` asks for one). An
> animating page paints at exactly `windowless_frame_rate`, changeable live: 60, 30, 120 and
> 1 measured, each the whole view for the bench page. Base64 `data:` URLs need
> `;charset=utf-8`. PNGs: `spikes/CefOsr/results/q3-*.png`.

**4. The message loop.** Two candidates: `multi_threaded_message_loop` (CEF gets its own UI
thread; callbacks arrive there) and `external_message_pump` (`cef_do_message_loop_work` on
HultUI's thread when `on_schedule_message_pump_work` asks). Which threads do `on_paint`,
`on_title_change` and `on_before_browse` arrive on in each? Does each shut down cleanly, with
no zombie processes (`pgrep -f type=`)? The contract allows either: `IWebEngineView.Event`
may be raised from any thread.

> **Result (2026-10-01): both work; multi-threaded is the fit.** With
> `multi_threaded_message_loop` every callback (`on_paint`, `on_title_change`,
> `on_before_browse`, life span, load) arrives on **`CrBrowserMain`**, CEF's thread in our
> process, and HultUI's thread does no CEF work. `create_browser_sync` returns null off that
> thread: create asynchronously and take the browser from `on_after_created`, and post
> whatever must run there with `cef_post_task`. With `external_message_pump` the callbacks
> arrive on our thread, but **only with a fallback timer**. CEF asks for work once
> (`cef_initialize`) and then stalls unless `cef_do_message_loop_work` also runs at least
> every 33 ms, as cefclient does. With the timer, CEF asks from its IO and pool threads,
> always with delay 0 (~1/s idle, 186/s animating). The cost is the same in all three: our
> process ~18 ms/s idle, ~50 ms/s animating at 60; CEF's ~155 ms/s. Shutdown is clean in all
> three: every reference returned, `cef_shutdown` in 16–25 ms, and nothing left once our
> process exits. But three zygotes and two zombie renderers outlive `cef_shutdown` for as long
> as the process lives (question 10). Details: `spikes/CefOsr/README.md`, question 4.

**5. Input and focus.** `send_mouse_move_event`, `send_mouse_click_event`,
`send_mouse_wheel_event`; `send_key_event` for a letter, Enter, Tab and an arrow; `set_focus`.
What does `cef_key_event_t` need to make a page's `keydown` and a text field both right:
`windows_key_code`, `native_key_code`, `character`? That answers seam W6 (the raw scan code
HultUI's key events do not carry yet). Then a `<select>`: CEF paints its list as a second
buffer (`PET_POPUP`, with `on_popup_show` and `on_popup_size`); confirm, and that it can be
composited over the page at the rectangle given. Then `ime_set_composition` and
`ime_commit_text`, and `on_ime_composition_range_changed` for where the candidates go.

> **Result (2026-10-01): all of it works, from any thread.**
> - **Mouse:** coordinates are **view coordinates (DIP)**: device pixels ÷ scale. Left,
>   right (`contextmenu`) and `click_count` 2 (`dblclick`) work. Wheel `delta_y` −120 is one
>   notch down (120 CSS px, smooth). Right-click logs that no default menu exists off-screen,
>   so the context-menu handler of 5.4 is required.
> - **Keys:** `windows_key_code` gives `key` and `keyCode`, `native_key_code` (**the XKB
>   keycode, evdev + 8**) gives `code` (empty without it), and `character` gives the text.
>   A key that types: `RAWKEYDOWN`, then `CHAR` **carrying the key's own codes** and the
>   character, then `KEYUP`, with modifier flags. That is exactly a browser's
>   keydown/keypress/input/keyup. A `CHAR` with the character as its code gives `keypress`
>   the wrong key ('c' → "3", VK_NUMPAD3). Text with no key goes through `ime_commit_text`
>   (an `insertText` input, no keypress). Keys the page leaves unhandled come back in
>   `on_key_event`.
> - **W6:** the XKB keycode. A `KeyCode` → XKB table covers the keys HultUI names. For the
>   rest, `KeyDownEvent.ScanCode` = SDL's scancode (the USB HID usage), mapped through
>   Chromium's `dom_code_data.inc` table (USB, evdev, XKB per key).
> - **Focus:** `set_focus(0/1)` blurs and focuses. Tab past the last element raises
>   `on_take_focus(next)`.
> - **`<select>`:** confirmed. `on_popup_show`, then `on_popup_size` in DIP (200×106 under the
>   field), then `PET_POPUP` paints at device scale (300×159). Clicks inside the rectangle
>   choose an option.
> - **IME:** composition events and commit as in a browser.
>   `on_ime_composition_range_changed` gives one rectangle per character in DIP.
>
> `spikes/CefOsr/README.md`, question 5; the popup's PNG in `results/`.

**6. Script results and page messages without code in the renderer.** CEF's
`execute_java_script` returns nothing, and its message router is C++ only. Try the DevTools
protocol from the browser process: `execute_dev_tools_method` with `Runtime.evaluate`
(`returnByValue`) for `ExecuteScriptAsync`, and `Runtime.addBinding` plus the
`Runtime.bindingCalled` event (through `add_dev_tools_message_observer`) for
`window.chrome.webview.postMessage`'s twin. If that works, the helper process needs no code of
ours at all. Also needed: the posting frame's origin, for `WebViewOptions`' allowed origins.
If it does not work, the fallback is a render process handler in the helper that adds the
binding in `on_context_created` and sends a process message.

> **Result (2026-10-01): it works; the helper needs no code of ours.**
> - **`Runtime.evaluate`** (`returnByValue`, `awaitPromise`) returns values, objects, awaited
>   promises and `exceptionDetails` as JSON, in **0.29 ms** median. Unserialisable results
>   (`window`) come back as a method error.
> - **`Runtime.addBinding`** works without `Runtime.enable`. `Runtime.bindingCalled` carries
>   `{name, payload, executionContextId}` and the binding survives navigation. A shim from
>   `Page.addScriptToEvaluateOnNewDocument` builds `window.chrome.webview` (`postMessage`,
>   `addEventListener('message')`) on it, **but only after `Page.enable`**. Host → page is
>   `Runtime.evaluate` of a delivery function.
> - **Origin:** `Runtime.enable`'s `executionContextCreated` gives each context's id, origin
>   and frame, so a post's `executionContextId` gives its origin. `data:` is opaque (`://`).
> - **Cross-origin iframes** are other targets. `Target.setAutoAttach(flatten)` gives a
>   session each, and the same setup per session makes their posts arrive. Session replies
>   come only through the raw `on_dev_tools_message`.
> - Send on CEF's UI thread: from any other thread `send_dev_tools_message` still sends, but
>   returns 0.
> - **Found:** a `srcdoc` iframe in a `data:` page crashes the renderer (146 and 154, with or
>   without DevTools). `NavigateToString` should be served by the scheme handler, not as
>   `data:`.

**7. Policy hooks.** `on_before_browse` returning 1 cancels a navigation, synchronously;
`on_before_popup` returning 1 refuses a new window and tells us the URL;
`on_render_process_terminated` fires when the renderer is killed; a scheme handler factory
serves `https://app.local/` from a folder.

> **Result (2026-10-01): all four hold.**
> - **`on_before_browse`:** called on `CrBrowserMain` before any request. Returning 1 cancels
>   (`ERR_ABORTED`; the page and its title stay put). CEF's thread can wait for HultUI's
>   thread to answer: 30 ms here, as the `WebNavigationRequest` continuation will.
> - **`on_before_popup`:** returning 1 refuses (no browser is created) and gives the URL, for
>   `target=_blank` links and `window.open` on a user gesture. Without a gesture, the popup
>   blocker drops them before the handler is called.
> - **`on_render_process_terminated`:** on SIGKILL, `TS_PROCESS_WAS_KILLED` (9), and
>   `reload` brings the page back.
> - **Scheme handler:** `cef_register_scheme_handler_factory("https", "app.local")` serves a
>   folder (handlers created on `Chrome_IOThread`). The page is a secure context with origin
>   `https://app.local`, with subresources, `fetch`, 404s, and every handler released.
>   **`NavigateToString` served through it** (`https://app.local/__string/<n>`) keeps UTF-8,
>   has a real origin, and does not crash on `srcdoc`.

**8. Zero-copy.** `shared_texture_enabled` on the window info, and `on_accelerated_paint`
hands over dmabuf planes (fd, stride, offset, modifier, format). On the Intel GPU: does it
arrive at all, and on which Ozone platform? Import it in a GL context with
`EGL_EXT_image_dma_buf_import` and draw it; then the same through
`VK_EXT_external_memory_dma_buf`. Measure CPU with an animating page (the one in
`samples/HultUI.Samples.WebView/Program.cs`, `AnimatedPage`) at 1650×1072 and 60 frames a
second, against `on_paint`. Windows for comparison: about 300 ms/s copying pixels, 62–106 on
GL and 19–94 on Vulkan zero-copy. Who owns the fds, and for how long is a frame valid?

> **Result (2026-10-01): it works, on `wayland` and `x11`; not on `headless`.**
> - **Where:** `on_accelerated_paint` arrives under Wayland and under X11 (Xwayland). On
>   `headless` nothing paints at all ("Unable to allocate frame"). **Wayland's scale works
>   with `--disable-features=WaylandFractionalScaleV1`**: exact and live (1.5 → 2.0 → 1.5
>   repaints in ~50 ms). X11's needs no flag. So the engine asks for the window's platform.
> - **What:** one plane, BGRA (`DRM_FORMAT_ARGB8888`), **modifier 0 (linear)**, stride 4864
>   at 1200×900, from a pool of three buffers.
> - **GL:** EGLImage (`EGL_EXT_image_dma_buf_import`, surfaceless EGL), blit into our own
>   texture, fence: **0.81 ms**.
> - **Vulkan:** `VK_EXT_external_memory_dma_buf` + `VK_EXT_image_drm_format_modifier`
>   (explicit plane layout, import a `dup`'d fd, acquire from `VK_QUEUE_FAMILY_EXTERNAL`),
>   copy, fence: **0.73 ms**. Both read back pixel-exact.
> - **CPU** at 1650×1072, 60 frames a second, our process: **33–34 ms/s** with GL or Vulkan,
>   against 108–113 for copying `on_paint` and 147–152 with the upload. CEF's processes drop
>   from ~190 to ~130.
> - **The fds are CEF's.** CEF documents them as valid only inside the callback, and the
>   buffer goes back to the pool on return. Seen: the fd usually stays open past the
>   callback, sometimes not. So copy on CEF's thread before returning; never close the fd,
>   never keep it; `dup` only for an import that takes ownership.
> - **Not checked:** `PET_POPUP` as a shared texture, and NVIDIA (cef#4237).
>
> `spikes/CefOsr/README.md`, question 8.

**9. Accessibility.** `set_accessibility_state(STATE_ENABLED)` and a render handler that
returns an accessibility handler. What does `on_accessibility_tree_change` carry (node ids,
roles, names, child lists, as a `cef_value_t` tree), how large is it for a real page, are the
updates incremental, and what does `on_accessibility_location_change` add? Is there any way to
act on a node (focus, click) by id? Check with `accerciser` or `busctl` whether Chromium puts
anything on the AT-SPI bus by itself when windowless. **Expect not.**

> **Result (2026-10-01): plain data, incremental, nothing on AT-SPI. Acting on nodes goes
> through DevTools.**
> - **What arrives:** `on_accessibility_tree_change` (on `CrBrowserMain`) carries Chromium's
>   `AXTreeUpdate` as a value tree: `{ax_tree_id, events: [{event_type, id}], updates:
>   [{root_id, tree_data (focus, selection), node_id_to_clear, nodes: [{id, role, state,
>   attributes (name, value, checkedState, htmlId, …), child_ids, actions, location
>   (device pixels, relative to offset_container_id)}]}]}`.
> - **Size and cost:** a small form is 32 nodes (14 KB). Wikipedia's "Web browser" article is
>   ~5,100 nodes (5 MB), and reading it through `cef_value_t` took 226 ms.
> - **Incremental:** yes. A changed heading and checkbox send 4 nodes and a
>   `checkedStateChanged` event. `on_accessibility_location_change` is a list of `{id,
>   new_location: {bounds, offset_container_id}}`; scrolling 2000 px sent 42 KB.
> - **Acting by id:** not through CEF; its handler is read-only. But CEF's node ids equal
>   DevTools' AX `nodeId` and `backendDOMNodeId`, so `DOM.focus` and
>   `DOM.resolveNode` + `Runtime.callFunctionOn("this.click()")` act on a node by id.
> - **AT-SPI:** the registry shows nothing new from a windowless browser, as CEF's header
>   says (TreeOnly mode).
> - **For W8's Linux half:** mirror the tree from the updates, serve its nodes as the web
>   view's children through our bridge, map actions to DevTools, and enable accessibility
>   only when a screen reader is on.

**10. Two pages and the way out.** Two browsers on one `cef_initialize`; close one; close
both; `cef_shutdown`. What must be released, in what order, for the process to exit by itself?

> **Result (2026-10-01): clean, and forgiving.**
> - Two browsers paint at 60 each.
> - `close_browser` → `on_before_close` (5–10 ms) → release ends one browser and its renderer;
>   the other keeps painting.
> - **The documented order** (each browser closed, `on_before_close` seen, our reference
>   released; then `cef_shutdown` on the initialising thread): `cef_shutdown` takes 15 ms,
>   every reference of ours comes back, the process exits ~35 ms after `Main` returns, and no
>   CEF process is left 0.3 s later.
> - **Three zygotes and two zombie renderers outlive `cef_shutdown`** for as long as the
>   process lives. Chromium never tears its zygotes down; they go when our end of their socket
>   closes. So `cef_shutdown` belongs at application exit only.
> - **Release builds forgive mistakes.** A held browser reference, a browser left open (which
>   `cef_shutdown` then closes itself), no `cef_shutdown` at all, or SIGKILL with both open:
>   the process exits and nothing is orphaned. A debug `libcef` would assert on the first two;
>   the engine keeps the documented order.

## Spike L0: recommendations, decided 2026-10-02

Each is the spike's answer to one of "yours to decide". The owner decided them on
2026-10-02, after the Windows side's review below; where the review changed one, the decision
says so. None is built yet.

1. **The helper process: a separate C executable**, about 20 lines (`spikes/CefOsr/Helper/helper.c`):
   `dlopen` libcef, `cef_api_hash` with the engine's API version, `cef_execute_process`. No
   .NET executable can be the zygote with the sandbox on (question 1). It asks nothing of the
   app's `Main`, does not depend on the CEF release (only on the API version), and finds
   libcef through `HULTUI_CEF_DIR`, which the engine `setenv`s before `cef_initialize`. Ship it
   per RID in `HultUI.WebView.Cef`'s `runtimes/<rid>/native`, built by a workflow as the mpv
   natives are. It is the one exception to "no native code of ours". **DECIDED: ship it.**
2. **The message loop: `multi_threaded_message_loop`** (question 4). Callbacks arrive on
   `CrBrowserMain`, HultUI's thread does no CEF work, and the costs equal the external pump's,
   which would need a 33 ms timer and run CEF's work on the thread that draws.
   `on_before_browse` waits on CEF's thread for the controller's answer. Calls that must run
   on CEF's UI thread go through `cef_post_task`. **DECIDED**, with the review's two cautions.
3. **Script and messages: the DevTools protocol, nothing in the renderer** (question 6).
   `Runtime.evaluate` for `ExecuteScriptAsync`. `Runtime.addBinding` plus a shim registered
   with `Page.addScriptToEvaluateOnNewDocument` (after `Page.enable`) for
   `window.chrome.webview`. `Runtime.enable`'s execution contexts give the origin. Messages
   are parsed raw (`on_dev_tools_message`), so child sessions (`Target.setAutoAttach`,
   cross-origin iframes) work if iframe messages are wanted. **DECIDED.** `NavigateToString`
   is served by a scheme handler, not a `data:` URL (question 7), **but from a host of its own
   with a `Content-Security-Policy: sandbox` header that leaves out `allow-same-origin`, so
   its origin stays opaque**, as the contract says; not from `app.local` (the review says why).
4. **Ozone: the window's own platform, asked for outright** (questions 1, 3, 8):
   `--ozone-platform=wayland --disable-features=WaylandFractionalScaleV1` under Wayland, and
   `--ozone-platform=x11` under X11. Both give the GPU, the exact scale (changeable live) and
   shared textures. `headless` (+ `--use-angle=gl-egl`) is only a fallback with no display:
   it has the scale but no zero-copy. **DECIDED.**
5. **Zero-copy: an EGL import on CEF's thread, inside `on_accelerated_paint`**, into a
   texture the engine owns, fenced before returning (question 8). HultUI's frame then draws
   the newest, as Windows draws its D3D11 copy. GL serves both renderers through the
   existing interop. How the engine's context shares with HultUI's (one share group, or the
   copy target exported to it) is step 5.5's first question. Expect ~33 ms/s against ~110–150
   for pixels at 1650×1072, 60 frames a second. **DECIDED: on Wayland only.** HultUI's GL
   context is GLX under X11, which cannot take an `EGLImage`; there the page takes the pixel
   path. The review prefers exporting the engine's copy targets over a share group.
6. **Accessibility on Linux: a mirrored tree, served by our bridge, acted on through DevTools**
   (question 9). The web view's node keeps a mirror of CEF's `AXTreeUpdate`s and exposes
   their nodes as its children to the AT-SPI bridge. Actions go to DevTools by
   `backendDOMNodeId`, which is CEF's node id. Accessibility is enabled only while a screen
   reader is on (`org.a11y.Status`), because a big page costs hundreds of ms to read.
   `AccessibilityNode.NativeContent` is not reused; the bridge needs a way to host data
   children, which is the seam to design. **DECIDED.**
7. **Shutdown: `cef_shutdown` at application exit only**, after every browser is closed and
   released (question 10). The zygotes stay until then. **DECIDED**, with the engine a
   per-process singleton (the review).

### Review from the Windows side (2026-10-02)

Read against the contract and the Windows engine. The spike's findings stand; these are about
the recommendations built on them. The owner took the review's side on all three choices it
raised (helper, string pages, X11).

- **1, the helper: agree.** The table in question 1 rules out every .NET shape, and the "no C++"
  rule was about not needing CEF's C++ wrapper to talk to the engine: a 20-line C launcher is
  not interop. Build and ship it as the mpv natives are.
- **3, `NavigateToString` from `app.local`: no, as written.** The contract says a string
  document's origin is **opaque** and its messages arrive with a null origin
  (`IWebEngine.NavigateToString`, `WebEngineEvent.MessageReceived`), which is what WebView2
  gives. Served from `https://app.local/__string/…` it would be **same-origin with the app's
  own content**: an untrusted string (a mail body, rendered Markdown) could `fetch` the app's
  files and share their storage, which it cannot on Windows. Keep the scheme handler, which
  avoids the `data:` crash, but serve strings from a **host of their own** (not `app.local`)
  with the response header `Content-Security-Policy: sandbox allow-scripts allow-forms
  allow-popups allow-modals` (everything but `allow-same-origin`): that makes the document's
  origin opaque, as the contract says. **Verify** that the `srcdoc` case still works that way,
  and that `executionContextCreated` then reports the opaque origin, which the engine maps to
  null.
- **2, the message loop: agree, with two cautions.** HultUI's thread must never wait for
  `CrBrowserMain` (browser creation is asynchronous already, through `on_after_created`, which
  the controller's held-until-ready queue fits). And before `cef_shutdown` the engine must
  answer every `on_before_browse` still waiting for the UI thread, or the shutdown, made on that
  thread, waits for a CEF thread that waits for it.
- **5, zero-copy: refine.** HultUI's GL context is **EGL on Wayland but GLX on X11** (SDL's
  default; see `SDL3Window`'s `GetProcAddress`), and an `EGLImage` cannot be imported into a
  GLX context. Two ways, cheapest first:
  - **Zero-copy on Wayland only**, X11 on the pixel path. Fedora's GNOME and KDE are
    Wayland-only now, so X11 is the uncommon case.
  - Or a hand-over that both accept: the engine's ring of copy targets allocated as exportable
    memory and imported with `GL_EXT_memory_object_fd`, which Mesa offers under GLX and EGL
    alike. That is what the Vulkan interop already does, in reverse.

  On EGL, prefer **exporting** the engine's copy targets (`EGL_MESA_image_dma_buf_export`, once
  per slot, imported once on HultUI's side) over a share group: a context shared with SDL's
  has to be created against it on HultUI's thread and then used on CEF's. Per frame: copy and
  fence on CEF's thread inside `on_accelerated_paint` (the spike's 0.8 ms), publish the newest
  slot, and let HultUI's frame blit it into the sink's target, as Windows does with its D3D11
  copy. Three slots, so the one HultUI reads is never the one CEF writes.
- **7, shutdown: agree, and it shapes the engine's lifetime.** CEF initialises once per
  process. An app, or a test run, that creates a second `CefEngine` after disposing the first
  must not fail: the engine is a per-process singleton (created on first use, shut down once,
  on the thread that initialised it, when the application ends). `WebView2Engine` has no such
  limit, so this is the Linux engine's to hide. The engine tests share one engine across the
  run.
- **Worth trying in step 5.2: `external_begin_frame_enabled`.** CEF then paints only when told
  to (`send_external_begin_frame`). Driven from HultUI's frame loop, the page's frames line up
  with the window's instead of running on a second 60 Hz clock, and a still page costs nothing.
  Question 4 measured 17 ms/s in our process with the page idle; **verify** whether this
  removes it.
- **4, Ozone: agree.** The engine needs to know whether HultUI's windows are on Wayland or X11
  before `cef_initialize`; that is a small seam (the SDL video driver's name), to be listed
  beside W1 to W9.
- **6, accessibility: agree.** Give tests a way to force it on: the Linux UI tests will run
  with no screen reader. The AT-SPI bridge reads neither `org.a11y.Status` property today (it
  serves its tree always), so the engine is the first to watch it: decide whether
  `IsEnabled` (any assistive technology) or only `ScreenReaderEnabled` counts.

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
works at all if one engine binary runs against more than one CEF. **Question 2 says it does**
(146 and 154, one binary). Whichever it is, the directory it produces is flat (the
distribution's `Release/` and `Resources/` merged) and its `libcef.so` stripped.

Two things to write down for whoever ships an app, not to solve: the official builds have no
H.264 or AAC, while Fedora's use the system's ffmpeg (RPM Fusion's gives the full set); and a
bundled CEF needs its licence files shipped beside it.

## Step 5, after the spike

In this order; each ends with its tests green and the step written into
`docs/design/WEBVIEW.md`'s implementation order as steps 1 to 4 are.

1. **`HultUI.WebView.Cef`: the directory search, `Availability`, initialise and shut down.**
   Struct layouts generated from the `capi` headers. Tests in
   `tests/HultUI.WebView.Engines.Tests`, skipped when no CEF is found (the CI runner is
   Ubuntu and has none). **Done 2026-10-02**; `WEBVIEW.md`'s implementation order has what was
   built and found.
2. **Pixels.** `on_paint` into `IWebViewHost.Pixels`. `SetPlacement` gives device pixels and a
   scale: the view rectangle is pixels divided by scale. The sample's `--check` passes on
   Linux, JIT and NativeAOT. **Done 2026-10-02**, with what `--check` needs of 5.4 (script,
   messages, string documents) built on the decided routes; the review's two "verify"s are
   answered there (the CSP sandbox holds; external begin frames are not needed).
3. **Input, focus, IME.** `Capabilities` does **not** include `NativeKeyboard`: the element
   then forwards keys and text through `SendKey` and `SendText`, a path only the fake engine
   has exercised so far. Seam W6 lands here. Shortcuts are simpler than on Windows: the
   element sees every key first and can offer it to the app before sending it on. The
   `<select>` popup is composited by the engine into the same sink. The IME's candidate
   position goes to `TextInputHost.SetInputArea`. **Done 2026-10-02**; see `WEBVIEW.md`.
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
   into a real drag. **Done 2026-10-02**, with two differences from this text, in
   `WEBVIEW.md`: off-screen CEF denies every permission but the camera and microphone without
   asking, and a page's drag is carried inside the page, not turned into a real drag (no
   drag source on the Linux side), so it cannot be dropped on another application.
5. **Zero-copy.** A `IGpuFrameRenderer` registered through `IWebViewHost.UseGpuRenderer`, as
   the Windows engine's `CaptureRenderer` and the mpv backend do. Seam W7 is done: on
   Vulkan each GPU source is lent a shared image of its own, with its own semaphores.
   It ran on Windows only, so the fd export of those semaphores is unproven: once CEF
   draws, show two pages, or a page and a video, in one Vulkan window and check a
   `HULTUI_VULKAN_CAPTURE` of it, as `VulkanProbeTests` does with two WebView2 pages.
   **On Wayland only** (decided 2026-10-02): under X11 the page takes the pixel path.
   **Done 2026-10-03** (`WEBVIEW.md` has it): copy and fence inside `on_accelerated_paint` into
   a ring of three exported slots, imported once on HultUI's side and blitted on GL and Vulkan;
   the popup arrives as a shared texture; the W7 check passes with two CEF pages in one
   Vulkan window. On the way it found a crash since 5.3 (disposing a view with its `<select>`
   list open), fixed.
6. **Accessibility.** The Linux half of seam W8 is not what `WEBVIEW.md` sketched for
   WebKit (a socket onto someone else's tree). CEF gives a tree of plain data, so the web
   view's node grows real children that our AT-SPI bridge serves like any others. Design it
   from question 9's answer; `AccessibilityNode.NativeContent` is the Windows shape and need
   not be reused.
   **Done 2026-10-03**, through seam W11 (data children, in Core, so both bridges could host
   them); `WEBVIEW.md` has it. One finding: CEF does not pass scroll offsets on, so the page is
   asked for its scroll.
7. **Debian and Ubuntu.** The natives package or download, and a run on each.
   **Designed, built and proved on this machine (2026-10-03); the runs on Debian and Ubuntu
   remain.**

   **The route: a natives package per RID, built by a workflow, as mpv's are.** Weighed against
   a download made by the application's packaging:
   - *A package* (`HultUI.WebView.Cef.Natives.linux-x64`, `.linux-arm64`): the application adds
     one `PackageReference`, and the build and the publish both put CEF where the engine looks.
     The CEF is pinned by version and SHA-1, the engine tests ran on exactly that tree before it
     was packed, and a restore works offline from the package cache. The cost is size: 154 MB
     for linux-x64 (zip, as NuGet packs; arm64's download is 30% larger, so about 200 MB), under
     nuget.org's 250 MB limit but heavy for every restore and feed. That is why there is one RID
     per package: no application pays for the other architecture.
   - *A download by the application's packaging* (a script or an MSBuild target fetching CEF's
     archive from Spotify's CDN, checking it, flattening and stripping it): no large package
     to host, and the application picks its CEF. But every application's build needs the
     network, `strip` for the target architecture, and the CDN's continued existence, and each
     application repeats work the engine's maintainers could do once. The flattening and
     stripping are exactly what question 2 showed easy to get wrong (ICU beside `libcef.so`; a
     1.45 GB `libcef.so`).

   The package wins for the same reasons mpv's did. `native/cef/build.sh` is reusable by an
   application that wants the other route anyway: it takes a tarball, or downloads one, and
   leaves the flat stripped tree.

   **Built:**
   - `native/cef/` holds `versions.env` (CEF 154.0.32 and both SHA-1s, from the CDN's
     `index.json`) and `build.sh <rid>`, which fetches, checks, flattens, strips with `strip`,
     `llvm-strip` or `aarch64-linux-gnu-strip`, and leaves out `chrome-sandbox` (see AppArmor
     below).
   - `src/HultUI.WebView.Cef.Natives` packs one RID (`-p:Rid=`) under `cef/` with a
     `buildTransitive` targets file that copies the tree to `cef/` beside the application, on
     build and on publish. **Not as native assets, found when consumed:** a RID-specific publish
     flattens `runtimes/<rid>/native/**` into the application's directory, `locales/*.pak`
     included, and the engine then found no CEF there.
   - The `cef natives` workflow builds, proves (the engine suite on the packaged tree), packs
     and publishes both, on a `cef-natives-v*` tag.

   **Proved here:**
   - The engine suite, 119 tests, passed on the bundled, stripped CEF 154 through
     `HULTUI_CEF_DIR` (twice), as on Fedora's 146.
   - The sample's `--check` passed on 154, zero-copy on GL and Vulkan.
   - A scratch application referencing the packed `.nupkg` from a local feed, built
     framework-dependent and published `-r linux-x64`, found CEF 154 in its own `cef/` before
     Fedora's and ran a page ("Chrome/154.0.0.0").
   - Found with 154: the zero-copy harness clicked a `<select>` without giving the page focus,
     and 154 opens one only in a focused page (146 did either way). A web view always has
     focus when clicked, so the harness now focuses first. 154's minimal build also has no
     `libEGL.so` or `libGLESv2.so` beside `libcef.so` (Fedora's 146 has both), and nothing
     missed them.

   **Ubuntu's AppArmor limit on user namespaces, for a bundled CEF (not built).** Since 23.10
   (so 24.04 LTS) Ubuntu sets `kernel.apparmor_restrict_unprivileged_userns=1`: an unconfined
   program may not make a user namespace. Chromium's sandbox is made of them: the browser
   process starts the zygote in a new user namespace (on Fedora, question 1 found exactly this
   sandbox running). On Ubuntu that fails and, with no setuid `chrome-sandbox` to fall back on,
   Chromium stops with "No usable sandbox!". The process that makes the namespace is the
   application's own executable, not the helper. That matters for a profile, and the user of a
   `dotnet app.dll` launch would be `dotnet` itself. Options, and what each asks of the person
   installing the app:
   1. **An AppArmor profile, installed by the app's package** (recommended; what Chrome,
      VS Code and the Electron apps do). A file in `/etc/apparmor.d/` naming the app's
      executable, `flags=(unconfined)` with `userns,`, loaded by the package's post-install
      (`apparmor_parser -r`). Asks: installing from a `.deb` (root, which installing already
      is). Nothing for a tarball or a `dotnet run` (see 4). The natives package can carry the
      template; the path in it is the application's to fill.
   2. **The setuid `chrome-sandbox`**, owned by root, mode 4755, beside `libcef.so`, where CEF
      finds it. Asks: root at install, to set the bit (a package cannot carry it, which is why
      the natives leave it out). Works without AppArmor's blessing, but a setuid binary is attack
      surface, and Chromium treats this sandbox as the legacy one.
   3. **No sandbox** (`--no-sandbox`). Asks nothing, and leaves every renderer unsandboxed:
      acceptable only for an application that shows its own, trusted content. If offered at
      all, as an explicit option an application turns on, never as the engine's fallback.
   4. **The sysctl set to 0** (`kernel.apparmor_restrict_unprivileged_userns=0`). Asks root
      and a system-wide security decision of the user, for one application. Documentable for
      a developer machine, not requirable of users.
   5. **Snap or Flatpak.** A snap with the `browser-support` plug gets user namespaces. Flatpak
      allows none, and Chromium there uses `zypak`, which CEF would need integrating. Asks:
      installing from that store, and more engineering per format.

   Whatever the route, the engine should **say so instead of dying**: read
   `/proc/sys/kernel/apparmor_restrict_unprivileged_userns`, and when it is 1 and no profile
   covers the executable (a test `unshare(CLONE_NEWUSER)` in a forked child fails), report the
   engine unavailable with a reason that names option 1, rather than let CEF end the process.
   To build and confirm on Ubuntu 24.04.

   **Run on Debian 13 and Ubuntu 24.04 (2026-10-03).** The sample, published NativeAOT for
   linux-x64 here with `HultUI.WebView.Cef.Natives.linux-x64` (379 MB without symbols), copied
   to a Debian 13 container with the Intel GPU's render node and to an Ubuntu 24.04 VM with no
   GPU, each running sway headless.
   - **Debian:** `--check` passed on GL and on Vulkan, **zero-copy under sway** (1916×973), with
     **the sandbox on**: renderers and a utility process in their own user namespace, the
     zygote's, and seccomp filters on every CEF process. (A container's kernel is the Proxmox
     host's, so the AppArmor sysctl is 0 there.)
   - **Found on the fresh accounts: the profile went nowhere.** With no `~/.local/share` yet,
     .NET's `LocalApplicationData` is "" on Linux, the default profile became a path relative
     to the working directory, and CEF would not start ("Could not find a part of the path
     …/HultUI.Samples.WebView/CEF", the executable's own name). Fixed with
     `SpecialFolderOption.DoNotVerify`; the folder is made when CEF starts. A test runs the
     engine in a process whose `HOME` is an empty directory; it fails without the fix.
   - **What the application needs installed besides itself.** Read from both, not guessed:
     `ldd` over every binary in the tree, the libraries SDL and CEF name for loading at run
     time, and the libraries actually mapped by the running application and every CEF process
     on both renderers (`/proc/<pid>/maps`, then `dpkg -S`). The closure is 108 packages; what
     an application's `.deb` declares is what its binaries link or load directly, and apt
     brings the rest. Names as Debian 13 and Ubuntu 24.04 both have them (the `t64` ones are
     the 64-bit-time renames; an older release has `libasound2` and so on, so an alternative
     `libasound2t64 | libasound2` covers both):

     ```
     Depends: libc6, libgcc-s1, libstdc++6,
      libx11-6, libx11-xcb1, libxcb1, libxcomposite1, libxdamage1, libxext6, libxfixes3,
      libxrandr2, libxcursor1, libxi6, libxkbcommon0,
      libwayland-client0, libwayland-cursor0, libwayland-egl1,
      libegl1, libgl1, libgbm1, libvulkan1,
      libasound2t64 | libasound2, libatk1.0-0t64 | libatk1.0-0,
      libatk-bridge2.0-0t64 | libatk-bridge2.0-0, libatspi2.0-0t64 | libatspi2.0-0,
      libcups2t64 | libcups2, libglib2.0-0t64 | libglib2.0-0,
      libcairo2, libpango-1.0-0, libdbus-1-3, libexpat1, libfontconfig1, libfreetype6,
      libnspr4, libnss3, libudev1
     Recommends: libdecor-0-0, libegl-mesa0, libgl1-mesa-dri, mesa-vulkan-drivers
     ```

     CEF links NSS, ATK/AT-SPI, CUPS, ALSA, X11 and GBM whether or not it uses them, so a
     Wayland-only desktop still needs them. A desktop has nearly all of these already; the
     Debian container lacked NSS, ATK/AT-SPI, CUPS and four X libraries. Ubuntu's server-like VM
     lacked the same but NSS. Both lacked `libwayland-egl1`, without which SDL's Wayland backend
     is missing and SDL says "No available video device". `libdecor-0-0` gives windows their
     title bars on a compositor without server-side decorations (GNOME), so it is a
     recommendation, not a requirement. The Mesa drivers come in through a desktop; a machine
     with another vendor's driver has its own.
   - **Ubuntu: the limit stops CEF.** Without a profile CEF ended the process: `FATAL
     zygote_host_impl_linux.cc:129 No usable sandbox! If you are running on Ubuntu 23.10+ …`,
     SIGTRAP, exit 133, before the application could do anything. **The engine now says so
     first** (`CefSandbox`): before `cef_initialize`, and in `CefEngine.Availability`, it reads
     the AppArmor sysctl and the process's own label (`/proc/self/attr/current`), and the two
     sysctls that turn user namespaces off outright. Restricted and unconfined, it fails with a
     reason that names the executable needing a profile (and, under `dotnet app.dll`, says that
     an application with a bundled CEF ships as its own executable). Root, or a setuid
     `chrome-sandbox` beside `libcef.so`, is let through. On the Ubuntu VM the sample now reports
     "CEF: Chromium's sandbox cannot start: … /home/dev/sample/HultUI.Samples.WebView has none …"
     and goes on with its fake engine. No probe by `unshare`: a child process is judged by its
     own profile, not the application's, and forking a .NET process is not safe.
   - **The profile, tested on the Ubuntu VM.** The shape Ubuntu's own profiles for Chrome,
     VS Code and Edge have: `profile <app> /opt/<app>/<executable> flags=(unconfined) { userns,
     include if exists <local/<app>> }`. **It must cover the application's executable, and
     that is enough.** With it, `--check` passed with the sandbox fully on (renderers and a
     utility process in their own user namespace, seccomp filters), and every CEF process
     carried the application's label (`hultui-sample (unconfined)`): an unconfined-mode
     profile is inherited across `exec`, so the helper runs under it. A profile on the helper
     alone, with the application unconfined (the engine's check off, `HULTUI_CEF_SANDBOX_CHECK=0`,
     new for exactly this), still died "No usable sandbox!": the browser process makes the
     namespace. So the check judges the right process, and an application launched as
     `dotnet app.dll` would need a profile on `dotnet`: **a bundled-CEF application ships as its
     own executable** (NativeAOT or apphost).
   - **How the application's `.deb` installs it, tested end to end.** The profile is a conffile
     in `/etc/apparmor.d/<app>`. `postinst configure` makes the empty
     `/etc/apparmor.d/local/<app>` and, when `aa-enabled` says AppArmor is on, loads the profile
     (`apparmor_parser --replace --write-cache --skip-read-cache`), as debhelper's
     `dh_apparmor` snippet does. `prerm remove` unloads it, and `postrm purge` removes the local
     file. `native/cef/deb/build-sample-deb.sh` builds the sample's package that way: 128 MB,
     with the `Depends` above. On Ubuntu `apt install` resolved the dependencies, loaded the
     profile, and `hultui-sample --check` passed. `apt remove` unloaded it and kept the conffile,
     and `purge` removed both. On Debian the same package installed (no AppArmor tools there,
     so the scripts did nothing) and passed zero-copy on GL and Vulkan.
   - **Not tested: arm64.** The package and the workflow build linux-arm64, and nothing has run
     on it. Also not run: a desktop session (GNOME, KDE) on either distribution; both machines
     ran sway headless.

## Review of steps 5.1 and 5.2 (2026-10-02)

Read on the Windows side against the contract, with the whole solution built and tested on
Windows. **The engine's shape stands:** `CefRuntime` as the per-process singleton on a thread
of its own, CEF's own UI thread for every callback, every call posted, and seam W10 in the
placement. Fix these before 5.3; each is small.

1. **String pages pile up.** `StringPages` keeps every `NavigateToString` body until its view
   is disposed. An application that renders one mail after another into the same view grows
   without bound. Keep a bounded number per view (the newest few, enough for back and
   forward), and serve the older ones the "no longer available" page.
2. **A resize during start-up is lost.** A placement that changes between `create_browser`
   and `Ready` posts no `Resize` (the phase is `Starting`), and CEF read the old size at
   creation, so a window maximised while the page starts keeps a page the old size. Apply the
   current placement once when the view becomes ready.
3. **The profile lock's host check fails on a host name with a dot.** On Linux,
   `Environment.MachineName` stops at the first `.`; Chromium's `SingletonLock` holds the
   whole name (`host.example.com-1234`). A second instance on such a machine then takes the
   held profile and CEF fails. Compare up to the first dot on both sides, or read the full
   name with `gethostname`.
4. **Every `[UnmanagedCallersOnly]` callback catches.** An exception that leaves one ends the
   process. `OnBeforeCommandLine` has `try`/`finally` and no `catch`; `StringPages.Headers`,
   `Read` and `Skip` have no `try` at all.
5. **`SDL3Window.WindowSystem` asks SDL every time**, and the binding allocates a string per
   call; the element computes its placement on every layout. One video driver per process:
   read it once.
6. **The mpv backend misses its natives under Fedora's .NET**, as you found (`Mpv.cs`, the
   `runtimes/<RuntimeIdentifier>/native` probe). Add the portable RID as
   `CefLibrary` does, and check it on this machine with Fedora's SDK.
7. **The helper workflow should use the release transport.** Artifact storage is already
   full: the `docs` job's upload failed on that on 2026-10-01. Do as `mpv-natives.yml` does
   (`native/mpv/release-transport.sh`; move it somewhere shared rather than copy it).
8. **Already fixed on the Windows side** (pull before you start):
   - the two Skia failures here were the two rows of
     `AJapaneseSentence_ReadWithAnEnglishUi_IsOneFace`, a theory the Han-font gate did not
     cover. It is now `[HanFontsTheory]` and skips without a CJK font;
   - three `CefEngineTests` failed on Windows, which runs the whole solution in CI: two expect
     `/` in `Path.Combine`'s results, and the lock test creates symbolic links, which Windows
     allows only with a privilege. They are `[LinuxFact]` now and still run here. Item 3
     changes the lock test;
   - `ProceduralVideoTests.TheFrameRate_SetsHowOftenFramesArePublished`, the one that failed
     here once, failed on Windows too (24 frames, at least 25 wanted): a sleep rounds up to
     the 15.6 ms timer tick there. Its lower bound is 12 now.

**Decided by the owner, 2026-10-02:**

- **`WebViewOptions.UserDataFolder`: the first folder rules.** The first page's folder (or
  the application's default when it names none) becomes the process's profile,
  `root_cache_path`. A page naming the same folder shares it; a folder inside it gets a
  request context of its own (CEF's rule for `cache_path`); any other folder makes that page
  fail (`WebEngineEvent.Failed`, `InitializationFailed`) with a message naming the rule and
  the folder in use. A second instance's temporary profile applies to the root as now.
- **The helper workflow uses the release transport** (item 7): artifact storage is full, so
  there was no real choice.

Also write into `docs/WEBVIEW.md`'s limits what an application meets on Linux: the profile
rule above; a second instance runs on a temporary profile (no shared cookies, unlike
WebView2); and pages under X11 take the pixel path.

## Review of steps 5.3 and 5.4 (2026-10-02)

Read on the Windows side, with the whole solution built and tested on Windows. **It stands.**
Checked in particular: seam W6 from SDL's scancode through `KeyInput`, the key events and
`WebKeyInput`; `InputAreaChanged` to the window's input method; the navigation wait (CEF's
thread waits one hop to HultUI's, since a `WebNavigationRequest` cannot be deferred, and every
answer still owed gets its default at disposal and exit); the move of the release transport to
`scripts/`, whose callers were all updated and which finds its Python half from its own
directory. The limits found while building (permissions, drags out, the context menu) are
the engine's, written down where an application will read them, and accepted.

1. **The signal test cannot see a replaced handler.** It compares which signals are caught
   (`SigCgt`) before and after CEF. Chromium taking `SIGCHLD` was exactly such a replacement:
   caught before and after, by someone else. Compare each signal's handler (the
   `sa_sigaction` address, the first field of the saved `sigaction`) for every signal .NET
   had caught before CEF. That also covers what the two behaviour checks do not: `SIGRTMIN`,
   with which .NET suspends threads for the garbage collector, and `SIGTERM`, `SIGINT` and
   `SIGQUIT`, through which it shuts down cleanly.
   **Done 2026-10-03, and it found one:** Chromium had taken SIGINT and SIGTERM too, so an
   application's own handlers for them never ran. Every signal is restored now; see
   `WEBVIEW.md`.
2. **The process tests' one timeout.** Three tests that start a process timed out together
   once in about ten runs and never again. Too little to act on; if it comes back, have the
   test print what is still running (`pgrep -af type=`) and the child's standard error
   before it fails, so the next one says why.
   **It came back during 5.5 and said why (2026-10-03):** the child was a zombie and the test
   host's SIGCHLD was at the default. .NET installs its handler at the first `Process.Start`,
   and that one came while CEF started, so Chromium reset it to the value saved before. Fixed
   in the engine, which matters to applications too; see `WEBVIEW.md`, step 5.5.
3. **Fixed on the Windows side** (pull before you start): `docs/ENVIRONMENT.md` had none of
   the web view's variables, the WebView2 ones included; it has a section now, with
   `HULTUI_CEF_SWITCHES` marked as not a supported way to configure an application.
   `KeyInput`, `WebKeyInput` and the headless `KeyDown`/`KeyUp` documented only their new
   scan code, which made the compiler warn about the rest; all documented now. And
   `MpvLoaderTests.UnderADistributionsOwnRid_ThePortableRidsFolderIsSearchedToo` failed on
   Windows, which runs it too, for expecting `/` from `Path.Combine`: it builds its expected
   paths the same way now. **Rule for tests here: a test that runs on Windows builds its
   expected paths with `Path.Combine`; one about Linux's paths or processes is
   `[LinuxFact]`.** Windows CI runs the whole solution, so this is the second time it would
   have gone red at the next merge.

## Review of step 5.5 (2026-10-03)

Read on the Windows side, with the whole solution built and tested there: every suite green,
the real-window GPU tests too (23, among them the Vulkan probe's two WebView2 pages after its
change), and no build warning the branch added. **It stands.** Checked in particular: the ring
(CEF writes neither the newest slot nor the one being read, and HultUI moves its read to
another slot only after the GPU has finished the last one, so with three slots a write never
waits and never overtakes a read); the late `UseGpuRenderer`, which works because the element
reads `Frames` on every draw; and the signal fix (every action saved, only Chromium's changes
put back, judged by `dladdr` against libcef, after .NET has installed its `SIGCHLD` handler).

1. **Signals Chromium sets after start-up would not be put back.** The restore runs once, at
   `on_context_initialized`, and the test compares handlers right after `Start`, before any
   page. If Chromium installs a handler when the first browser or its GPU process comes up,
   nothing restores it and the comparison does not see it; only the behaviour checks, which do
   run after a page, would. Run the restore again once the first page has drawn, and take the
   test's "after" after a page has drawn too.
2. **The page-and-video variant of the W7 check** is worth one run if a libmpv is to hand
   (Fedora's `mpv-libs`): it is the one place the mpv backend's hidden-context interop and
   CEF's share a Vulkan window. Not a blocker.

**Done 2026-10-03:** item 1 as asked (it found nothing to put back, on headless or Wayland);
item 2 not run, for want of a libmpv here. See `WEBVIEW.md`.

## Review of step 5.6 (2026-10-03)

Read on the Windows side, with the whole solution built and tested there: every suite green,
and all 31 real-window tests, among them the UIA tests and `WebViewAccessibilityTests`, which
WebView2's tree still passes now that the element is an `IAccessibleContentSource` with an
empty tree on Windows. **It stands.** Seam W11 in core rather than in a bridge is the better
place: it is the virtual items' pattern, so both bridges and the action router took it with
a few lines each.

1. **Fixed on the Windows side:** an ambiguous `cref` (`FromCef`, which has two overloads) in
   `CefAccessibleTree.cs`, the branch's one new build warning; the docs job builds with
   warnings as errors.
2. **The application's AT-SPI name** (empty, as you found): worth fixing while you are in the
   bridge, since it is what a screen reader announces for the application. The entry
   assembly's name is what the profile folder already uses.
   **Done 2026-10-03:** the application root is named so; busctl lists the sample as
   "HultUI.Samples.WebView", JIT and AOT.
3. **Not for you:** `ContentNodeId` keeps the owning element's id to 30 bits and
   `VirtualItemId` to 31, while element ids come from a counter every element ever built
   draws from, rebuilds included. A long-running application that rebuilds a lot would pass
   that and route actions to the wrong element. Older than this step (the virtual items have
   it), so it is a task of its own on the Windows side.
4. **5.5's page-and-video check** still wants a libmpv: the owner can install Fedora's
   `mpv-libs` as root in the container, and the check can run then. Not a blocker.
   **Done 2026-10-03** with `mpv-libs` installed: both halves show their colour, and the trace
   has two interop sources. It was mpv's first shared-image run on Mesa and Intel, and it worked;
   `WEBVIEW.md` (W7) and `HDR.md` have it.

## Review of the page-and-video check, the AT-SPI name and step 5.7's first part (2026-10-03)

Read on the Windows side, with the whole solution built and tested there (every suite green,
no new build warning) and the Vulkan probe's six tests run twice. **It stands**, and the
natives package per RID is the right route: one build that flattens, strips and checks the
SHA-1, instead of every application's packaging doing it.

1. **Fixed on the Windows side: the page-and-video test on Windows.** It failed there, as you
   expected: the bundled LGPL libmpv has no lavfi ("unrecognized file format"). The Windows
   test now writes twelve seconds of solid magenta as Y4M, which every build reads, and passes
   it with `--clip=`; it passes, so W7 with WebView2 and mpv in one Vulkan window is proven on
   Windows too. The Linux test can keep lavfi.
2. **Fixed on the Windows side: the natives' copy condition.** `Natives.targets` copied CEF
   for any build without a RID, so a cross-platform application built on Windows carried some
   360 MB of Linux CEF in every output folder. Now: a Linux RID, or no RID when the build runs
   on Linux. **Re-run your throwaway application** (a RID-less build and a `-r linux-x64`
   publish, both on this machine) to confirm it still lands.
   **Re-run 2026-10-03, with the package repacked:** the RID-less build and the
   `-r linux-x64` publish both found CEF 154 in their own `cef/` and ran a page; a
   `-r win-x64` build of the same application now has no `cef/`.
3. **Fixed on the Windows side: the natives README** now says what a page cannot play with
   the bundled CEF (H.264, AAC), as this brief asked application authors to be told.
4. **AppArmor: agree with (a), plus detection, and (c) only as an explicit opt-in.** Two
   things to settle on the Ubuntu machine: whether the process that needs the profile is the
   application alone or the helper too (Chromium's browser process starts the zygote in its
   namespace; the renderers make more inside it), and that the profile names the application's
   own executable, so an application started as `dotnet app.dll` would need one for `dotnet`.
   Say in the docs that a bundled-CEF application ships as its own executable.
5. **The `cef natives` workflow has never run.** Its job runs the engine suite on the
   self-hosted runner, which is Ubuntu, so the first dispatch is also the first Ubuntu run, in
   a container whose kernel is the Proxmox host's. Either answer, sandbox or "No usable
   sandbox!", is worth having before 5.7's machines: run it once by hand
   (`gh workflow run "cef natives" --ref claude/webview-component-repo-62ba8b`) and write
   down what it said.
   **Not run from here:** this machine has no `gh`, and the repository's API needs a token it
   does not have. The Ubuntu VM gives the same answer for a real Ubuntu kernel (step 7).
6. **The Ubuntu sandbox check needs a virtual machine, not a container.** In an LXC container
   the kernel and its AppArmor are the Proxmox host's, so whether Ubuntu's limit applies there
   says nothing about an Ubuntu desktop. Debian can be a container, as Fedora is.

## Review of step 5.7 on Debian and Ubuntu (2026-10-03)

Read on the Windows side, with the whole solution built and tested there. **It stands**: the
dependency list measured rather than guessed, the profile proven necessary and sufficient on
the application's executable alone, and a real `.deb` that installs, loads, removes and purges
it. The fresh-account bug was a good find.

1. **Root is not fine.** `CefSandbox.Judge` passes a process running as root, but Chromium
   refuses root without `--no-sandbox` and ends the process ("Running as root without
   --no-sandbox is not supported"), the very thing the check exists to prevent. Confirm with
   `sudo` on either machine, and report root the way the other cases are reported (unless the
   application chose to run without the sandbox).
   **Done 2026-10-03.** Confirmed with `sudo` on both machines: CEF logs "Running as root
   without --no-sandbox is not supported" and the process ends (exit 1) before the
   application's own code goes on. With `HULTUI_CEF_SWITCHES=no-sandbox` the same root run
   passed `--check` on Debian. `CefSandbox` now refuses root with a reason ("run the
   application as an ordinary user"), on any distribution and whether or not a setuid
   `chrome-sandbox` is there, unless the application chose no sandbox (`no-sandbox` among the
   switches), which also lifts the AppArmor case. Rule tests for both.
2. **Fixed on the Windows side:** `File.GetUnixFileMode` in `CefSandbox` was the branch's one
   new build warning (CA1416): the csproj's `<SupportedOSPlatform>linux</SupportedOSPlatform>`
   generates nothing for a plain `net10.0` project, so the analyzer treats the code as every
   platform's. It has an `OperatingSystem.IsWindows()` guard now. The property is inert:
   remove it, or keep it only as a comment, so nobody relies on it.
   **Done:** removed from `HultUI.WebView.Cef.csproj`, with a comment saying why it is not
   there and what guards the code instead. Three other projects carry the same property
   (`HultUI.Platform.Accessibility.Linux`, `HultUI.WebView.WebView2`,
   `HultUI.Platform.Accessibility.Windows`); not touched here.
3. **Fixed on the Windows side:** the natives README's paragraph on what a page cannot play
   (H.264, AAC) was lost in the rewrite; it is back.
4. **Not for you:** the empty data folder on a fresh account also hits Vesper Commander, whose
   settings path has no fallback (Reel, Aperture and Junction have one). A task of its own on
   the Windows side. **Done:** `ConfigDir` honours `XDG_DATA_HOME`, asks with `DoNotVerify` and
   falls back to `~/.local/share`, so it is never relative; elevation's job folder uses it too
   (a relative one sent the root worker, started in root's home, looking in the wrong place).
5. **Left open, and fine to leave:** arm64, GNOME and KDE sessions, and the first run of the
   `cef natives` workflow, which needs the owner's go-ahead because it publishes a prerelease.

## Developer tools on Linux (decided 2026-10-03)

**The owner's decision:** developer tools open in **a native window of their own**, CEF's
`show_dev_tools` with an ordinary window: not drawn by HultUI. F12 opens them on both
platforms when `WebViewOptions.DevTools` is on, and so does code.

**Done on the Windows side:**
- The contract: `IWebEngineView.OpenDevTools()` (a default that does nothing) and
  `WebViewController.OpenDevTools()`. The controller throws `InvalidOperationException` when the
  options leave developer tools off, and `NotSupportedException` on an engine without
  `WebEngineCapabilities.DevTools`; otherwise it holds the call until the engine is ready, as
  every command is.
- WebView2: `OpenDevToolsWindow`. F12 and Inspect were WebView2's own already.
- `WebView2Tests`: the call opens a DevTools window; F12 does with the option on and does
  not with it off. Found on the way: the DevTools window belongs to another `msedgewebview2`
  process than the page's focus window, so the tests find it by title and process name.
- The sample: `--devtools` turns them on and adds a toolbar button that calls
  `OpenDevTools()`.
- `docs/WEBVIEW.md`: a "Developer tools" section.

**Yours:**
1. **`CefView.OpenDevTools`:** on CEF's UI thread, `show_dev_tools` with a windowed
   `cef_window_info_t` (no parent: a top-level window), a client of its own for the DevTools
   browser, and no point to inspect. A second call shows the open one. The tools close with
   their page (`close_dev_tools` when the view goes) and are released before `cef_shutdown`.
2. **Find out first whether CEF can make that window on the Ozone platform the engine
   started**: Wayland, with fractional scaling off, or X11. The platform is CEF's for the
   whole process, so it cannot change for one window. If Wayland cannot, stop and report what
   does, with what it costs, before choosing.
3. **The capability:** add `WebEngineCapabilities.DevTools` to `CefView.Capabilities` once it
   works, and not before: the controller relies on it.
4. **F12** (and Ctrl+Shift+I, which WebView2 also answers): in `SendKey`, with the option on,
   the key opens the tools instead of reaching the page. The element forwards only what the
   app did not take, so an app's own F12 still wins.
5. **Inspect on the context menu:** CEF's menu has no such entry. Add one named
   `inspectElement` (the menu code keeps it under that name when the option is on) whose choice
   opens the tools at the menu's point (`inspect_element_at`).
6. **Tests** on sway: the call, F12 and Inspect each open a window titled like DevTools (sway's
   tree shows it), F12 opens nothing with the option off, and disposing the page closes it.
7. **Docs:** the Linux paragraph of `docs/WEBVIEW.md`'s "Developer tools", and take "there is
   no 'inspect' entry" out of the Linux limits.

**Done 2026-10-03, all seven.**
- **Point 2 first:** CEF made the window on both platforms the engine starts. Under Wayland
  (fractional scaling off) it is an xdg-shell window, "DevTools - about:blank", app id
  `chromium-browser`; under X11 (Xwayland here) an X11 window of the same title. On the headless
  platform (no window system, tests only) the tools open invisibly and do no harm. Nothing to
  stop for.
- **`CefView.OpenDevTools`** (`CefView.DevToolsWindow.cs`): on CEF's UI thread,
  `show_dev_tools` with a windowed, parentless `cef_window_info_t`, a client of its own (no
  handlers), and a point only for Inspect. A second call shows the open window. The page's
  disposal calls `close_dev_tools` before `close_browser`, and the client is released with the
  page's other objects, before `cef_shutdown`.
- **`WebEngineCapabilities.DevTools`** always: the capability is read before the platform is
  known, and every platform the engine starts works.
- **F12 and Ctrl+Shift+I** (no other modifier) open the tools in `SendKey` when the option is
  on; the page sees neither the press nor its release. The element offers keys to the app
  first, so an app's own F12 still wins.
- **Inspect:** with the option on, the page's menu ends with a separator and `inspectElement`
  ("Inspect", the first command id CEF leaves to the embedder). Chosen, it cancels CEF's menu
  and opens the tools at the menu's point (CSS pixels, as `show_dev_tools` takes them).
- **Tests** (`CefDevToolsTests`, on sway): each runs the engine in a process of its own on
  Wayland and asks sway's tree for that process's windows. The call, F12, Ctrl+Shift+I and
  Inspect each open a window titled "DevTools…" that is gone once the page is disposed; F12
  with the option off opens none. Plus the key rule. Not checked: which element Inspect
  selects inside the tools.

## Running without the sandbox (decided 2026-10-03)

**The owner's decision:** a proper option to run CEF without Chromium's sandbox, **enforced**:
without the sandbox, the engine loads only the application's own content, unless the
application says, with a second and separate setting, that it accepts untrusted content
without a sandbox. Today the only way is the diagnostic `HULTUI_CEF_SWITCHES=no-sandbox`.

**Why it is needed:** the sandbox cannot start as root, on Ubuntu 23.10 and later without the
AppArmor profile, or where user namespaces are off. In each case the engine reports itself
unavailable (`CefSandbox`). An application that shows only its own trusted content may prefer
to run there without the sandbox, and should be able to say so in code.

**The API** (Linux only: WebView2's sandbox always works on Windows, so it gets nothing):

```csharp
new CefEngine(new CefEngineOptions
{
    Sandbox = false,                              // default true
    AllowUntrustedContentWithoutSandbox = false,  // default false; ignored while Sandbox is on
});
```

Keep the parameterless constructor. `CefEngineOptions` is a public sealed record with init
properties, like `WebViewOptions`, and its documentation says plainly what running without the
sandbox means: a page that exploits a browser bug runs with the user's full rights.

**What to build:**

1. **Start CEF without its sandbox** (`cef_settings.no_sandbox = 1`) when the engine that starts
   CEF asks for it. CEF hands the switch on to its children, so the helper needs nothing.
2. **The first engine decides.** CEF starts once per process, so the engine whose page starts
   it fixes the mode for the process. A later engine asking for the other mode reports itself
   unavailable (`Availability`), and its pages fail with `InitializationFailed`, with a reason
   that names the conflict rather than silently running in the wrong mode.
3. **The sandbox check:** with the sandbox off, root and the AppArmor limit no longer make the
   engine unavailable. `CefSandbox.Facts.NoSandbox` already exists; feed it from the option.
   The diagnostic `HULTUI_CEF_SWITCHES=no-sandbox` keeps working and goes through the same
   path, **enforcement included**: one rule, no sandbox means only the application's own
   content, unless the API's second setting says otherwise.
4. **The enforcement.** With the sandbox off and untrusted content not allowed, every network
   request of the page, in every frame and of every kind (documents, iframes, scripts, styles,
   images, `fetch`, WebSockets), is cancelled unless its URL is the application's own:
   - `WebViewOptions.AppOrigin` (`https://app.local`);
   - the string pages' host (`StringPages.Host`);
   - `data:`, `blob:` and `about:blank`.

   Put it in the request handler (`get_resource_request_handler` and
   `on_before_resource_load` returning `RV_CANCEL`), so nothing slips past the navigation
   check. A refused main-frame navigation also completes as failed, with an error name that
   says why (pick one in the style of the existing WebView2 names), and each refusal is logged
   once with its URL (`CefLog`).

   Note: **nothing serves `app.local` today.** No folder mapping exists in `WebViewOptions`
   on either engine, so until one does, an application without the sandbox shows only its
   string pages. Allow it anyway, so it works the day a folder mapping exists; that is a
   separate decision for the owner. DevTools are untouched: their window has its own client.
5. **Never silent:** log a line when CEF starts without its sandbox, and give `CefEngine` a
   `bool? IsSandboxed` (null until CEF has started; then how it actually runs, whichever engine
   started it).

**Tests:**
- **Rule tests:** the option lifts the root and AppArmor cases; two engines asking for
  different modes conflict. The request rule: which URLs pass, for each kind of URL above.
- **Process tests:**
  - **Ubuntu VM, no profile:** the default reports unavailable; `Sandbox = false` draws a string
    page.
  - **Fedora:** with the option off, the renderer processes really run unsandboxed (no user
    namespace of their own, `Seccomp: 0` in `/proc/<pid>/status`), so the option is shown to
    do what it says rather than only skip the check.
  - **Enforcement, with the sandbox off:** a string page loads; a navigation to
    `https://example.org` is refused and reported; an `<img>`, a `<script>` and an `<iframe>`
    from the internet inside a string page do not load (the page can report each with
    `onerror`); with `AllowUntrustedContentWithoutSandbox`, the same page loads them.
- Every test that runs on Windows builds its expected paths with `Path.Combine` or is
  `[LinuxFact]`/`[CefFact]`.

**Docs:**
- `docs/WEBVIEW.md`: a "Running without the sandbox" part of the Linux text, with the warning
  and the enforcement.
- The natives README: option (c) of the Ubuntu list becomes this setting.
- `docs/ENVIRONMENT.md`: `HULTUI_CEF_SWITCHES=no-sandbox` is enforced the same way.

**Done 2026-10-03, as written.**
- **The API:** `CefEngineOptions` (a public sealed record; `Sandbox`, default true;
  `AllowUntrustedContentWithoutSandbox`, default false), `new CefEngine(options)` beside the
  parameterless constructor, and `CefEngine.IsSandboxed`.
- **The mode:** the engine whose page starts CEF passes its mode, and `cef_settings.no_sandbox` follows
  it. A later engine in the other mode gets `Availability` false and its pages
  `InitializationFailed`, both with a reason that names the conflict (`CefRuntime.ModeConflict`).
  The diagnostic `HULTUI_CEF_SWITCHES=no-sandbox` turns an engine's wish off through the same
  `CefRuntime.WantsSandbox`, so its pages are limited the same way. The sandbox check takes
  `NoSandbox` from the mode.
- **The enforcement** (`CefView.OwnContent.cs`): with the sandbox off and untrusted content not
  allowed, the request handler's `get_resource_request_handler` returns a resource handler whose
  `on_before_resource_load` cancels (`RV_CANCEL`) every URL that is not the application's own, and
  whose `on_resource_redirect` sends a redirect elsewhere to `data:,`. `on_before_browse` refuses a
  main-frame navigation first and completes it with "BlockedWithoutSandbox". Each refused URL is
  logged once per page. The rule (`CefView.IsOwnContent`) passes the string pages' origin,
  `AppOrigin` (scheme, host and port all), and `data:`, `blob:`, `about:blank`, `about:srcdoc`.
  It refuses the rest, the local network and `file:` included. The DevTools window has its own
  client and is not limited.
- **Never silent:** "CEF starts without Chromium's sandbox: …" on standard error at start.
- **Found when testing:** an `http` resource inside the `https` string page is mixed content,
  which Chromium refuses with or without the sandbox. So the enforcement test's foreign content
  is the internet's, over `https` (example.org, wikipedia.org, cdnjs). The test that needs it
  loaded returns early offline. On Fedora (an LXC container) every process carries the
  container's own seccomp filter, so "no seccomp" is read as "no filter Chromium added"
  (`Seccomp_filters` equal to the process's own). Chromium does not pass the environment on
  to its renderers, so they are found by parentage.
- **Tests:**
  - Rules: the option lifts root and AppArmor; the mode conflicts; 17 URLs through the own-content
    rule.
  - Processes on Fedora: the renderers really run without the sandbox (in the application's user
    namespace, no filter of Chromium's; with the sandbox, namespaces of their own and one filter
    more). Only the own content loads: image, script, fetch and frame refused, the navigation
    failed and named. The diagnostic switch is enforced the same way. Untrusted allowed loads
    all of it. A second engine in the other mode is unavailable, and its page fails.
  - On the Ubuntu VM, no profile, the same probe (self-contained, on the bundled CEF 154): the
    default engine is unavailable with the AppArmor reason. `Sandbox = false` draws the string
    page with the same refusals, and its renderers have `Seccomp_filters: 0` in the
    application's namespace. Untrusted, the conflict and the switch behave as on Fedora. As
    root, the default is refused and `Sandbox = false` draws.

## When you stop

Commit on `claude/webview-component-repo-62ba8b` with the results in the documents, and push
if the owner has said to. The Windows side is done: step 6 (polish) and seam W7, with `main` merged in on 2026-09-30.

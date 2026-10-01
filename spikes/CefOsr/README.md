# CEF off-screen spike — L0

Spike L0 of [`docs/design/WEBVIEW-LINUX-BRIEF.md`](../../docs/design/WEBVIEW-LINUX-BRIEF.md):
before any engine code, can Fedora's CEF render a page off-screen for a .NET program,
through CEF's C API alone, and what does each part of the engine have to look like? Ten
questions, cheapest first; the brief has the questions, this file the answers.

**Machine.** Proxmox LXC on an i7-12700K, Intel UHD 770 (ADL-S GT1) passed in as
`/dev/dri/renderD128` (a `card1` is present too), Fedora 44, Mesa 26.2.3, CEF
146.0.11+chromium-146.0.7680.177 from Fedora's `cef` package, .NET 10.0.112. No monitor: sway
1.x headless on the render node (`WLR_BACKENDS=headless WLR_RENDERER=gles2`), output 1920×1080.
VA-API has no driver here (`intel-media-driver` is RPM Fusion's), so video decode is software.
Raw output of every run is under [`results/`](results/), named by question.

## What it is

A console program, no HultUI until question 7.

- `gen-capi.py` generates `Capi.g.cs` from the headers: clang parses every `capi` header at
  `CEF_API_VERSION=14600` (the headers drop and add members by version), and a C program
  compiled from the same headers prints every struct's size, every field's offset and every
  enum's value. The C# structs are `LayoutKind.Explicit` with the compiler's offsets, and
  every function-pointer member is a typed `delegate* unmanaged<...>`, so a call with the wrong
  arguments or a callback with the wrong signature does not compile. 169 structs, 100 enums,
  174 exports. This is the generator step 5.1 asks for, ahead of time; it cost less than
  checking hand-written structs would have.
- `Native.cs`: loading, UTF-16 `cef_string_t`, and reference counting. CEF's rule, read from
  `libcef_dll/cpptoc/cpptoc_ref_counted.h` (Fedora ships the wrapper's sources in
  `/usr/src/cef-*/`): **every struct pointer that crosses the boundary, as an argument or a
  return value, carries one reference owned by the receiver; `self` carries none.** So we add a
  reference before handing CEF one of ours (including returning a handler from `get_*`), and
  release every struct CEF hands us. Our structs count their references, so a run can see
  whether CEF gave back everything it took.
- `Host.cs`: `cef_initialize` with the options, one client for every browser, handlers that
  raise events the probes subscribe to.
- `Helper/helper.c`: the subprocess executable (question 1 says why it is C).
  `Helper/Program.cs` is the .NET version that does not work.
- `Q1.cs` … one file per question.

## Running it

```bash
export XDG_RUNTIME_DIR=/run/user/$(id -u) WAYLAND_DISPLAY=wayland-1   # the headless sway
dotnet build spikes/CefOsr
clang -O2 -o spikes/CefOsr/bin/cefosr-helper-c spikes/CefOsr/Helper/helper.c   # used when present
spikes/CefOsr/bin/Debug/net10.0/CefOsr q1 --ozone=wayland
```

Options: `--cef-dir=` (default `/usr/lib64/cef`), `--ozone=`, `--sandbox=off`,
`--subprocess=` (`browser_subprocess_path`; default `bin/cefosr-helper-c` when built, `self`
runs this program again),
`--loop=work|external|mt`, `--resource-dirs`, `--cwd=`, `--switch=name[=value]`,
`--log=verbose|info|warning|error`.

## Answers

| # | Question | Answer |
|---|---|---|
| 1 | Does Fedora's libcef load and start, windowless, from a .NET program? | **Yes, with a native helper.** `cef_api_hash(14600)` first, then `cef_initialize`: context ready ~200 ms after process start, a `data:` page loaded and painted 85–100 ms after `create_browser_sync`. Resources are found beside `libcef.so` with no `resources_dir_path` and from any working directory. **The subprocess executable cannot be .NET**: Chromium's zygote must be single-threaded to sandbox itself, and cannot fork a .NET process even unsandboxed. A 20-line C helper in `browser_subprocess_path` works with the sandbox on. Sandbox: **the namespace sandbox works in this container**, and the setuid `chrome-sandbox` beside `libcef.so` works too; `no_sandbox` is not needed. Ozone: **Wayland**, with the GPU (WebGL on ANGLE → GL ES 3.2 → Mesa); X11 (the default) and `ozone-platform-hint=auto` without a session type fail here for want of an X server; `headless` runs with no WebGL (until question 3 gives it `--use-angle=gl-egl`). |
| 2 | Does the versioned API hold? | **Yes.** The same binary, written against Fedora's 146 at API 14600, runs unchanged on the official CEF **154.0.32** (Chromium 154.0.8037.58, the stable release on 2026-10-01): same hash, `cef_initialize` = 1, page painted 59 ms after `create_browser`, WebGL on the GPU, sandbox on, references balanced. 154 accepts API 13300–15400; generated from 154's headers at 14600, every struct, offset and export is identical to 146's. A bundled CEF must be laid out **flat** (`Resources/` beside `libcef.so`: `resources_dir_path` does not cover `icudtl.dat`) and **stripped** (the official `libcef.so` is 1.45 GB with debug sections, 272 MB without; ~115 MB compressed in all). |
| 3 | Pixels | **BGRA, top-down, opaque, at the frame rate asked for.** The scale: see question 8, which revises the platform: `wayland` with `--disable-features=WaylandFractionalScaleV1`, or `x11`, both exact. First found: An 800×600 view at scale 1.5 paints **1200×900** on `--ozone-platform=headless` (a 100-CSS-pixel box is 150 device pixels), and **800×600 at scale 1** on `wayland`, whatever `get_screen_info`, `--force-device-scale-factor` or the compositor's output scale say. `headless` with `--use-angle=gl-egl` has the GPU (WebGL on ANGLE → GL ES 3.2 → Mesa) and needs no display connection. First frame 85–93 ms after `create_browser`. A still page stops painting (0 paints in 2 s); an animating one paints at exactly `windowless_frame_rate`: 60, 30, 120 and 1 measured, each the whole view. |
| 4 | The message loop | **Both work; `multi_threaded_message_loop` is the simpler fit.** With it every callback (`on_paint`, `on_title_change`, `on_before_browse`, life span, load) arrives on CEF's own UI thread, `CrBrowserMain`, in our process, and our thread does nothing. `external_message_pump` delivers them on our thread, but **only with a fallback timer**: CEF asks for work once and then stalls unless the host also calls `cef_do_message_loop_work` at least every 33 ms (cefclient's rule); with it CEF asks ~1/s idle and 186/s animating, always "now". Costs are the same either way: ~18 ms/s idle and ~50 ms/s animating at 60 in our process, ~155 ms/s in CEF's. All three shut down with every reference returned and no process left once ours exits; until then zygotes and two zombie renderers outlive `cef_shutdown` (question 10). |
| 5 | Input and focus | **All of it works off-screen.** Mouse coordinates are **view coordinates (DIP)**; clicks, right-clicks, `click_count` 2 for a double click, the wheel (−120 = one notch down, 120 CSS px, smooth). Keys: **`RAWKEYDOWN`, `CHAR`, `KEYUP`, each carrying the key's Windows code and its XKB `native_key_code`**, give a page exactly what a browser does; `native_key_code` is what fills `KeyboardEvent.code` (seam W6). Text with no key goes through `ime_commit_text`. Tab past the last element raises `on_take_focus`. `<select>` paints a `PET_POPUP` buffer at the device scale with its rectangle in DIP, and clicks inside it choose. IME composition and commit work, with character bounds in DIP. |
| 6 | Script results and page messages without code in the renderer | **Yes, through the DevTools protocol; the helper stays 20 lines of C.** `Runtime.evaluate` (`returnByValue`, `awaitPromise`) answers `ExecuteScriptAsync` with JSON: values, objects, promises, exceptions; 0.29 ms median round trip. `Runtime.addBinding` + `Runtime.bindingCalled` carries the page's posts; a shim from `Page.addScriptToEvaluateOnNewDocument` (needs `Page.enable`) builds `window.chrome.webview` on it, on every new document. The origin: `Runtime.enable`'s `executionContextCreated` names each context's origin, and every post names its context. Cross-origin iframes are other targets: `Target.setAutoAttach(flatten)` and the same setup per session. Send on CEF's UI thread. |
| 7 | Policy hooks | **All four work.** `on_before_browse` returning 1 cancels (`ERR_ABORTED`, the page stays), asked on `CrBrowserMain`, which waited 33 ms for our thread's answer. `on_before_popup` returning 1 refuses and names the URL, for a link with `target=_blank` and for `window.open`, on a user gesture; without one Chromium's popup blocker drops it before asking. `on_render_process_terminated` (`TS_PROCESS_WAS_KILLED`, 9) on SIGKILL, and `reload` brings the page back. A scheme handler factory for `https` + `app.local` serves a folder as a secure origin (`https://app.local`, `isSecureContext`), with 404s, on `Chrome_IOThread`; and `NavigateToString` served through it gets a real origin and no `data:` crash. |
| 8 | Zero-copy | **Yes, on `wayland` and `x11`, not on `headless`.** `on_accelerated_paint` hands over one dmabuf plane, BGRA (`DRM_FORMAT_ARGB8888`), modifier 0 (linear), stride 4864 at 1200×900, from a pool of three buffers. Imported on CEF's thread inside the callback, as CEF requires, into GL (`EGL_EXT_image_dma_buf_import`, blit, fence: 0.81 ms) or Vulkan (`VK_EXT_external_memory_dma_buf` + DRM format modifier, copy, fence: 0.73 ms); pixels checked in both. At 1650×1072, 60 frames a second, our process: **33 ms/s** either way, against 108 for copying `on_paint`'s pixels and 150 with the upload; CEF's processes 130 against 190. **On Wayland the scale needs `--disable-features=WaylandFractionalScaleV1`**; then it is exact and live, like X11's. CEF owns the fds. |

### 1. Load, start, helper, sandbox, Ozone

**Load and start.** `NativeLibrary.Load("/usr/lib64/cef/libcef.so")`, then
`cef_api_hash(14600, 0)` as the first call: it returns `7cec7429…`, the Linux hash
`cef_api_versions.h` lists for 14600, and `cef_api_version()` answers 14600. Fedora's 146
accepts API versions **13300 to 14600** (`CEF_API_VERSION_MIN`/`LAST`), and the RPM says the
same: `Provides: cef(api) = 13300 … cef(api) = 14600`, with an RPM macro
`%_cef_api_requires 14600` → `Requires: cef(api) = 14600`. So an app's package can depend on
the API version the engine was generated against rather than on a CEF release.
`cef_initialize` with `windowless_rendering_enabled`, `root_cache_path` set (CEF warns without
it), returns 1; `on_context_initialized` arrives ~140 ms later on the calling thread.

**Resources.** `icudtl.dat`, `v8_context_snapshot.bin`, the `.pak` files and `locales/` are
found beside `libcef.so` without `resources_dir_path` or `locales_dir_path`, also with the
working directory at `/` (`results/q1-ozone-*.txt` all ran from `/`). Fedora's layout is flat;
the official distribution's is not (question 2).

**The helper process.** Neither shape in the brief works with a .NET executable
(`results/q1-helpers.txt`):

| Subprocess executable | Threads at `Main` | Sandbox on | Sandbox off | Off + `--no-zygote` |
|---|---|---|---|---|
| The app run again (`--type=…`) | 7 (JIT) | zygote never reports | GPU launch fails | works |
| Separate .NET helper, NativeAOT | 2 (main, `.NET Finalizer`) | zygote never reports | GPU launch fails | works |
| Separate .NET helper, JIT | 7 | zygote never reports | GPU launch fails | works |
| **Separate C helper** (`Helper/helper.c`) | 1 | **works** | works | works |

"Zygote never reports" is `FATAL zygote_host_impl_linux.cc:221`: the browser waits for the
zygote's boot message (`ReceiveFixedMessage(kZygoteBootMessage)`), and the zygote dies first:
entering the namespace sandbox needs a single-threaded process. "GPU launch fails" is
`GPU process launch failed: error_code=1002` six times and `GPU process isn't usable.
Goodbye.`; 1002 is `LAUNCH_RESULT_SUCCESS`, which the zygote path reports even when the fork
request returns no process: the unsandboxed zygote cannot fork a .NET process either. Every
.NET runtime has threads before `Main` (NativeAOT the fewest: its finalizer thread), so a .NET
zygote is out, and `--no-zygote` is allowed only with `--no-sandbox`.

So **the helper is a separate native executable**, about 20 lines of C: `dlopen` libcef,
`cef_api_hash` with the same version as the browser process, `cef_execute_process`. It asks
nothing of the app's `Main`. It finds libcef through `HULTUI_CEF_DIR`, which the browser
process sets in the native environment (`setenv`, not `Environment.SetEnvironmentVariable`,
whose changes .NET keeps to itself) before `cef_initialize`; Chromium passes children none of
our own switches. It is the engine's only native code; how it is built and shipped (beside the
natives of mpv, by a workflow, per RID) is a step 5.1 decision.

**The sandbox in this container.** It works, by the **namespace sandbox**: `unshare -U` works
here, renderers and the storage service run in their own user, PID and network namespaces
with seccomp-bpf (`Seccomp: 2`, `NoNewPrivs: 1`); the GPU process and the network service run
in ours, under seccomp. Whether the container has `nesting=1` is not visible from inside; the
kernel allows unprivileged user namespaces (`max_user_namespaces` 2147483647). With
`--disable-namespace-sandbox` the **setuid sandbox** works as well: Fedora's
`/usr/lib64/cef/chrome-sandbox` is setuid root and is found beside `libcef.so` without
`CHROME_DEVEL_SANDBOX` (`results/q1-setuid-*.txt`). `no_sandbox` was not needed; every later
run here has the sandbox on. For Debian and Ubuntu: Ubuntu 23.10+ restricts unprivileged user
namespaces through AppArmor, so a bundled CEF there needs an AppArmor profile or a setuid
`chrome-sandbox`, which an app cannot install without root. Question for step 5.7.

**Ozone** (C helper, sandbox on, working directory `/`):

| Flag | Result |
|---|---|
| none | X11, Chromium's default: `Missing X server or $DISPLAY`, `cef_initialize` = 0 |
| `--ozone-platform=x11` | the same: no X server in this container (no Xwayland) |
| `--ozone-platform=wayland` | **works.** GPU process maps `libgallium` (Mesa's Gallium driver), `libEGL_mesa`, `libwayland-client`; WebGL reports `ANGLE (Intel, Mesa Intel(R) UHD Graphics 770 (ADL-S GT1), OpenGL ES 3.2)` |
| `--ozone-platform=headless` | starts and paints, but **no WebGL**; the GPU process loads `libvulkan_intel` and no GL |
| `--ozone-platform-hint=auto` | X11 when `XDG_SESSION_TYPE` is unset (here); Wayland when it says `wayland` |

CEF passes `--render-node-override=/dev/dri/renderD128` to its children by itself and never
looks for a `card*`. A windowless browser still connects to the display server. So the engine
should ask for the platform outright, not leave it to a hint that depends on the session's
variables. **Question 3 revises which:** on Wayland the scale is ignored, so the engine asks
for `headless` with `--use-angle=gl-egl`, which has the GPU and the scale.

Also seen: `chrome://gpu` never loads in a windowless browser (`Timeout of new browser info
response` for its frame), so the GL was read through WebGL instead; and renderers and zygotes
outlive `cef_shutdown` for a while (question 10). Reference counts were all back to 0 after
shutdown.

### 2. The versioned API, against a newer CEF

The official minimal distribution of the current stable release for `linux64`,
`cef_binary_154.0.32+g682c378+chromium-154.0.8037.58_linux64_minimal.tar.bz2` (326 MB, SHA-1
checked against the CDN's), unpacked under `~/cef-new`. 154 supports API versions **13300 to
15400**; its hash for 14600 is the one 146 returns. The spike binary, built once against
Fedora's headers at 14600, and the same C helper, pointed at it with `--cef-dir`
(`results/q2-cef154.txt`):

- `cef_api_hash(14600)` = `7cec7429…`, `cef_api_version()` = 14600: the library serves the
  version we named, not its own.
- `cef_initialize` = 1 in 164 ms; the page loaded and painted 59 ms after
  `create_browser_sync`; the WebGL probe reports the same ANGLE on Mesa; renderers in their own
  namespaces; `cef_shutdown` with every reference given back.
- **Statically too:** `gen-capi.py --include <154> --api 14600` produces the same 169 structs
  with the same offsets and sizes and the same exports as from 146's headers; the only
  difference is six new `cef_errorcode_t` values (`results/q2-capi-146-vs-154-at-14600.diff`).
  At 15400 the headers add members, which is what the version number is for.

So **one engine binary serves Fedora's CEF across its major bumps and a bundled CEF on Debian
and Ubuntu**, as long as the library still lists the engine's version. Both 146 and 154 keep
13300 as the minimum, so the window has not yet moved in eight releases. The engine should
pin the newest version Fedora's stable releases all provide (14600 today) and check
`cef_api_hash` before anything else; an app's RPM requires `cef(api) = 14600`.

What a bundle needs, learned on the way:

- **Flat.** The distribution splits `Release/` (library, snapshot, sandbox) from `Resources/`
  (`icudtl.dat`, `.pak`, `locales/`). Pointed at `Release/`, CEF dies on ICU (`Invalid file
  descriptor to ICU data received`), and still does with `resources_dir_path` and
  `locales_dir_path` set to `Resources/`: those cover the `.pak` files, not `icudtl.dat`.
  Everything in one directory works, which is the layout CEF's own CMake produces. Fedora's
  package is already flat.
- **Stripped.** The minimal distribution's `libcef.so` is 1.45 GB, with debug sections;
  `strip --strip-unneeded` makes it 272 MB (Fedora's: 269 MB). Stripped library plus the rest,
  `xz -6`: **115 MB**, the brief's estimate.
- 154 links ANGLE into `libcef.so` (no `libEGL.so`/`libGLESv2.so` beside it, as 146 has), and
  its `chrome-sandbox` is not setuid out of the tarball: on Debian and Ubuntu the namespace
  sandbox is the route, and Ubuntu's AppArmor restriction on it is step 5.7's question.

### 3. Pixels

`get_view_rect` answers 800×600, `get_screen_info` scale 1.5 (CEF asks three times while
creating the browser, struct size 56, as generated). The page: a red 100×100 CSS-pixel box at
the top-left, a blue one at the bottom-right, a line of text (`results/q3-*.txt`, the PNGs
beside them).

| Ozone | `on_paint` | Red box | WebGL |
|---|---|---|---|
| `wayland` | **800×600** | 100 px | ANGLE → GL ES 3.2 → Mesa |
| `headless` | 1200×900 | 150 px | none |
| **`headless` + `--use-angle=gl-egl`** | **1200×900** | **150 px** | **ANGLE → GL ES 3.2 → Mesa** |
| `headless` + `--use-angle=vulkan` | (not run) | | ANGLE → Vulkan → Mesa's Intel driver |

**On Wayland the scale we give is ignored.** Neither `notify_screen_info_changed` plus
`was_resized`, nor `--force-device-scale-factor=1.5`, nor sway's output scale at 1.5 or 2
changed it: the frame stayed 800×600 with the page laid out at 1:1. **On `headless` the scale
is exact.** Headless also has the GPU once ANGLE is told to use GL (`gl-egl`) or Vulkan (its
default on headless is SwiftShader, which CEF 146 refuses without
`--enable-unsafe-swiftshader`, hence "no WebGL" in question 1), never connects to the
display, and so does not care whether HultUI's window is on Wayland or X11. **So the engine
asks for `--ozone-platform=headless --use-angle=gl-egl`**, not for the window's platform;
question 1's advice to follow the window is withdrawn. Question 8 says whether shared
textures arrive on headless too.

**Revised by question 8:** they do not, and the Wayland scale was one feature flag away.
With `--disable-features=WaylandFractionalScaleV1` the Wayland platform takes the scale from
`get_screen_info` exactly (1200×900, the 150-pixel box) and changes it live; X11 does so with
no flag. Both have the GPU and shared textures, so **the engine follows HultUI's window:
`wayland` plus that flag, or `x11`.** `headless` remains the fallback for a host with no
display.

- **Format.** BGRA, top-down, rows of `width × 4` bytes, alpha `FF` on an opaque page:
  `(10,10)` is `00 00 FF FF` (red), the bottom-right corner `FF 00 00 FF` (blue). What
  `PixelFrameSink` takes.
- **Time to the first frame:** 85–93 ms from `create_browser_sync` returning, with load end
  already seen; the browser exists ~20 ms after the call starts.
- **A still page stops.** No paints in 2 s once settled. `invalidate(PET_VIEW)` brings one
  full-view paint back on demand.
- **An animating page paints at `windowless_frame_rate`**, and `set_windowless_frame_rate`
  changes it live: 60.0, 30.0, 120.0 and 0.7–1.0 paints a second measured over 3 s. Each paint
  of the sample's bench page has one dirty rectangle, the whole view (its background moves).
  A HultUI window's refresh rate is the rate to ask for; nothing caps it at 60.
- **Thread:** with `cef_do_message_loop_work` on the main thread, `on_paint` arrives on the
  main thread (question 4 compares the loop models).
- **`data:` URLs need a charset.** Base64 without `;charset=utf-8` is read as Latin-1 ("Â·").
  `NavigateToString` must say UTF-8.

### 4. The message loop

Three runs, identical but for the loop, on `headless` + `gl-egl`: create a browser
(asynchronously: `create_browser_sync` only works on CEF's UI thread, and returns null, with
no error, from any other), navigate twice, 5 s still, 3 s animating at 60, close, shut down
(`results/q4-*.txt`).

| | `multi_threaded_message_loop` | `external_message_pump` + 33 ms timer | polled `cef_do_message_loop_work` (reference) |
|---|---|---|---|
| Callbacks arrive on | **`CrBrowserMain`, CEF's thread in our process** | our main thread | our main thread |
| `on_schedule_message_pump_work` | not called | from `Chrome_IOThread`, `ThreadPoolForeground`, `CacheThread`; always delay 0 | not called |
| Pump runs | none of ours | 31/s idle (the timer), 186/s animating | 480/s, always |
| Our process, idle | 17 ms/s | 18 ms/s | 21 ms/s |
| Our process, animating at 60 | 48 ms/s | 56 ms/s | 56 ms/s |
| CEF's processes, animating | 157 ms/s | 153 ms/s | 163 ms/s |
| `cef_shutdown` | 16 ms, on main | 25 ms | 18 ms |

**The external pump stalls without a timer** (`results/q4-external-no-timer.txt`): CEF asks
once, during `cef_initialize`, and never again; the first page took 15 s to fail with
`ERR_ABORTED`, and nothing animated. Its pump (`libcef/browser/browser_message_loop.cc`)
asks only when the UI thread goes from idle to busy and never reports delayed work it
already knew of, so the host must come back by itself. cefclient
(`main_message_loop_external_pump.cc`) calls `cef_do_message_loop_work` at least every
1000/30 ms for this reason. Iterating glib's default context instead does not help. With the
timer: HultUI's loop would have to wake 30 times a second forever, and each wake runs up to
10 ms of CEF's work (its time slice) on the thread that draws.

**Multi-threaded** keeps CEF's work off HultUI's thread entirely: CEF runs its UI thread
itself, and our thread only calls in. What that asks of the engine:

- every event is raised on `CrBrowserMain`, which `IWebEngineView.Event` allows; the
  controller marshals;
- `on_before_browse` is answered on CEF's thread, which waits while HultUI's thread decides:
  the allowed direction ("the reverse is allowed"), and the continuation
  `WebNavigationRequest` already has;
- calls that must be on CEF's UI thread (`create_browser_sync`, and whichever others the
  headers say) are posted with `cef_post_task(TID_UI, …)`; the browser itself comes back in
  `on_after_created`. `cef_initialize` and `cef_shutdown` are made on our main thread, the
  same one, as CEF requires.

**Shutdown.** In all three: close the browser, `on_before_close` 2–5 ms later, release our
reference, `cef_shutdown` returns in 16–25 ms, and every reference CEF took of ours has been
returned. But **three zygotes and two zombie renderers are still there 3 s after
`cef_shutdown`**, the zombies being the zygote's unreaped children. They go when our process
exits: `pgrep` finds no helper and `ps` no zombie, at once and 2.5 s later, in every mode.
For an app, which shuts CEF down only on its way out, that is clean; question 10 looks at why
they outlive `cef_shutdown`.

### 5. Input and focus

A page with a text field, a button and a `<select>` at known CSS positions logs every event it
receives through `console.log`; the spike prints them after each step (`results/q5.txt`). Run
with `--loop=mt`, so every call here comes from our thread, not CEF's: CEF accepts input from
any thread.

**Mouse.** `send_mouse_move_event` / `send_mouse_click_event` / `send_mouse_wheel_event` take
**view coordinates**, the DIP of `get_view_rect`: (60, 85) arrives as `clientX 60, clientY 85`
with `devicePixelRatio` 1.5. So the engine divides the element's device pixels by the scale.
Left, right (`contextmenu`, `auxclick`) and `click_count` 2 (`dblclick`, `detail 2`) all
arrive. The button flags in `modifiers` (`EVENTFLAG_LEFT_MOUSE_BUTTON`) make `buttons`
right. **Wheel:** `delta_y` −120 is one notch *down* (`deltaY` +120, `deltaMode` 0), the
page scrolls 120 CSS px, animated over ~10 frames. The sign is Windows' `WHEEL_DELTA`
convention. Right-click with no context-menu handler logs `Default context menu
implementation is not available`: step 5.4's handler is needed, as on Windows. The cursor
comes back through `on_cursor_change` (`CT_POINTER`, `CT_IBEAM`).

**Keys: what `cef_key_event_t` needs.**

| Sent | `keydown` | `keypress` | Text |
|---|---|---|---|
| `RAWKEYDOWN`, `CHAR`, `KEYUP`, all with wkc `0x41`, native 38, character `a` (cefclient's way) | `"a"`, `KeyA`, 65 | `"a"`, `KeyA`, 97 | `a` |
| the same with `native_key_code` 0 | `"b"`, **`code` ""**, 66 | `"b"`, `""` | `b` |
| `RAWKEYDOWN` (wkc `0x43`, native 54, no character), then `CHAR` with wkc = the character `c` | `"c"`, `KeyC` | **`"3"`**: wkc `0x63` read as VK_NUMPAD3 | `c` |
| `RAWKEYDOWN`/`CHAR`/`KEYUP`, the `CHAR` carrying the key's wkc `0x44` and native 40 | `"d"`, `KeyD`, 68 | `"d"`, `KeyD`, 100 | `d` |
| the same for E with `EVENTFLAG_SHIFT_DOWN` | `"E"`, `KeyE` | `"E"` | `E` |
| `CHAR` only, wkc 0 or the character, no native | — | **`"\u0000"`** | `é`, `日` |
| `ime_commit_text("ü")` with no composition | — | — | `ü`, `insertText` |
| ArrowLeft (`0x25`/113), Enter (`0x0D`/36, `\r`), Tab (`0x09`/23) | `ArrowLeft`, `Enter`, `Tab` with their codes | Enter only | caret moves; Tab moves focus |

So: **`windows_key_code` gives `key` and `keyCode`; `native_key_code`, the XKB keycode
(evdev + 8), gives `code`; `character` gives the text.** A key that types sends
`RAWKEYDOWN`, then `CHAR` with *the key's* codes and the character, then `KEYUP`, with the
modifier flags; HultUI's `KeyDownEvent` and the `TextInputEvent` SDL sends right after it make
that pair. Text that came from no key (an IME commit outside composition, a dead key, an
on-screen keyboard) goes through `ime_commit_text`, which inserts without a `keypress`, as a
browser does; a bare `CHAR` would give the page a `keypress` with key `"\u0000"`.

**Seam W6, answered:** the engine needs the XKB keycode. For every key HultUI's `KeyCode`
names, a table from `KeyCode` to XKB is enough; for the keys it maps to `Unknown` (keypad,
F13+, media, international) the raw scan code is needed. SDL's scancode is the USB HID usage,
and Chromium's `ui/events/keycodes/dom/dom_code_data.inc` lists USB usage, evdev and XKB for
every key, so `KeyDownEvent.ScanCode` carrying SDL's scancode and one generated table cover
it. **Keys come back.** Every key the page did not consume arrives in the keyboard handler's
`on_key_event` (`RAWKEYDOWN` and `KEYUP`), on CEF's thread: a second chance for app
shortcuts, beside the element seeing every key first.

**Focus.** A windowless browser reports `document.hasFocus()` true from the start;
`set_focus(0)` blurs the page (`blur` on the field and the window), `set_focus(1)` restores it.
Tab moves through the page's own order; **Tab past the last element blurs the window and
raises `on_take_focus(next = 1)`** on `CrBrowserMain`, which is `FocusLeaving(forward)`.
Shift+Tab sent after that comes back in at the last element.

**`<select>`.** A click on it raises `on_popup_show(1)` and `on_popup_size` with the list's
rectangle in **view coordinates** (20, 150, 200×106: just below the field), then `on_paint`
with `PET_POPUP` and a **300×159** buffer, the rectangle times 1.5
(`results/q5-select-popup.png`: the native list, first row highlighted). The engine
composites it over the page at the rectangle × scale. Mouse events sent at view coordinates
inside the rectangle go to the list: a click at row 3 chose "three" (`change`), closed it
(`on_popup_show(0)`), and the page saw nothing else.

**IME.** In the focused field, `ime_set_composition("にほん", underline 0–3, caret 3)` gives
`compositionstart`, `compositionupdate "にほん"` and an `input` with `insertCompositionText`;
**`on_ime_composition_range_changed` reports the composed range (6–9) and one rectangle per
character in view coordinates** (where `TextInputHost.SetInputArea` puts the candidate window,
after × scale and the element's offset). `ime_commit_text("日本")` ends it with
`compositionend "日本"`. This container has no CJK font, so those characters drew as 7 px
boxes; the geometry, not the glyphs, is what was checked.

### 6. Script results and page messages, with no code in the renderer

All through `send_dev_tools_message` (raw UTF-8 JSON `{id, method, params}`) and one
`cef_dev_tools_message_observer_t` registered with `add_dev_tools_message_observer` (keep the
returned registration; releasing it unregisters). No DevTools window, no remote-debugging
port. `results/q6.txt`.

**Threading.** `send_dev_tools_message` reports whether it took the message only on CEF's UI
thread; from any other thread it still sends it and returns 0. With
`multi_threaded_message_loop` the engine posts the call to `CrBrowserMain`
(`cef_post_task(TID_UI)`), where every result and event also arrives.

**`ExecuteScriptAsync` → `Runtime.evaluate`** with `returnByValue: true, awaitPromise: true`:

| Expression | Result |
|---|---|
| `1 + 2` | `{"result":{"type":"number","value":3,…}}` |
| `({ a: [1, 'two', null], b: { c: true } })` | `{"result":{"type":"object","value":{"a":[1,"two",null],"b":{"c":true}}}}` |
| `undefined` | `{"result":{"type":"undefined"}}` |
| a promise resolving in 50 ms to `'later'` | `"later"`, after 50 ms |
| `throw new Error('boom')` | the error object plus `exceptionDetails` |
| `window` | method error `-32000 Object reference chain is too long` |

So the engine returns `JSON.stringify` of `result.value` (`"null"` for `undefined`, as
WebView2 does), faults the task on `exceptionDetails` or a method error. **Round trip: 0.29
ms median, 0.46 max** over 50 calls, from the send on CEF's thread to the result callback.

**`postMessage` → `Runtime.addBinding({name})`.** It works **without** `Runtime.enable`: a
page calling the binding raises `Runtime.bindingCalled {name, payload, executionContextId}` on
`CrBrowserMain`. The binding survives navigations (it is added to every new context). The
payload is a string, so the page side stringifies.

**`window.chrome.webview`** comes from a shim registered once with
`Page.addScriptToEvaluateOnNewDocument` (`runImmediately` for the current document): it
wraps the binding in `postMessage(m)` (→ `JSON.stringify`) and `addEventListener('message')`.
**The script runs only while the Page domain is enabled**: without `Page.enable` it never
ran (no log, no `chrome.webview`); with it, it runs before the page's own scripts on every
navigation. Host → page is `Runtime.evaluate("chrome.webview.__deliver(<json string>)")`,
which dispatches a `MessageEvent`; the page's listener got `{"hello":"from host"}`.

**The posting frame's origin.** With `Runtime.enable`, `Runtime.executionContextCreated`
names each context's id, origin and frame (`id 1, origin https://example.com, frame …,
default true`), and `executionContextsCleared` says when to forget them; every
`bindingCalled` names its `executionContextId`. So: keep id → origin, check it against
`WebViewOptions`' allowed origins, raise `MessageReceived(json, origin)`. A `data:` page's
origin is opaque (`://`). (`Runtime.enable` is the domain pages can notice a debugger by;
only the mapping needs it.)

**Cross-origin iframes are out of process** (site isolation is on): an `https://example.org`
iframe in `https://example.com` produced no context in our session, so neither binding nor
shim reached it. **`Target.setAutoAttach {autoAttach, flatten: true}`** attaches it as its own
`iframe` target with a `sessionId`; messages sent with that `sessionId` run in it (`typeof
chrome.webview` → `"undefined"` until set up), and the same `addBinding` + shim there made its
post arrive as `bindingCalled` tagged with the session. **Replies and events of a child
session come only through `on_dev_tools_message`** (raw), never `on_dev_tools_method_result`
or `on_dev_tools_event`, so the engine should parse raw messages itself and return 1. Whether
iframes post at all is a policy choice (WebView2 gives them their own frame object); the
mechanism is there.

**Found on the way: a `srcdoc` iframe inside a `data:` page crashes the renderer**
(`TS_PROCESS_CRASHED`), DevTools or not, on 146 and on 154; in an https page it works
(`results/q6-srcdoc-in-data-url.txt`). So `NavigateToString` should not be a `data:` URL:
serve the string from the scheme handler of question 7 instead, which also gives it a real
origin.

**So the helper needs no code of ours**, and stays the 20 lines of C from question 1. The
render-process-handler fallback is not needed.

### 7. Policy hooks

`--loop=mt`; `results/q7.txt`.

**`on_before_browse` cancels, synchronously, and can wait for HultUI's thread.** It is
called on `CrBrowserMain` for every main-frame navigation (the first load, link clicks,
`load_url`, `reload`), before any request. The spike's handler queued the request and blocked
CEF's thread until our main thread answered, 30 ms later, as the controller's
`WebNavigationRequest` continuation will: returning 1 for `/blocked.html` left the page where
it was, its title unchanged, with `on_load_error ERR_ABORTED` for the URL and no request to
the scheme handler; `/other.html` was allowed and loaded. `user_gesture` is 0 for
`element.click()` from script and for `load_url`; `is_main` tells frames apart.

**`on_before_popup` refuses, and names the URL.** On a real mouse click (`send_mouse_click_event`)
on a `target=_blank` link and on a button calling `window.open(url)`, it was called on
`CrBrowserMain` with the URL, `CEF_WOD_NEW_FOREGROUND_TAB` and `user_gesture` 1; returning 1
created no browser (`on_after_created` never fired). From script without a gesture, neither
reached the handler: Chromium's popup blocker drops them first, as in Chrome.
`NewWindowRequested` therefore sees the popups a user asked for, which is what it is for.

**`on_render_process_terminated`.** SIGKILL to the renderer processes:
`TS_PROCESS_WAS_KILLED`, error code 9, on `CrBrowserMain`, at once. `browser->reload`
afterwards started a new renderer and loaded the page again (through `on_before_browse`
and the scheme handler), so `ProcessFailed(message, canReload: true)` holds.

**`https://app.local/` from a folder.** `cef_register_scheme_handler_factory("https",
"app.local", factory)`, on CEF's UI thread after context initialisation, returns 1; no custom
scheme registration is needed for `https`. The factory's `create` is called on
`Chrome_IOThread` for every request, including subresources and `fetch`, and returns a new
`cef_resource_handler_t`: `open` (`handle_request = 1`: answered now), `get_response_headers`
(status, MIME type, charset, length), `read` (copy, return 0 at the end), `skip`, `cancel`.
The page is a secure context with origin `https://app.local`; its CSS applied
(`rgb(200, 30, 40)`), `fetch('/data.json')` returned the file, `/missing.txt` and
`/favicon.ico` got 404. Paths are resolved against the folder and anything that leaves it is
a 404. All 12 handlers were released by CEF (reference counts back to 0).

**`NavigateToString` through the handler.** A reserved path (`https://app.local/__string/1`)
served from memory: title right, `héllo · 日本` intact (UTF-8 from the response's charset),
origin `https://app.local`, and **the `srcdoc` iframe that crashes a `data:` page works**.
That is the engine's `NavigateToString`.

### 8. Zero-copy

`shared_texture_enabled = 1` in the window info; the animated bench page
(`results/q8-*.txt`).

**Where it arrives** (3 s animating, 800×600 at the scale asked):

| Ozone | `on_accelerated_paint` | Frame |
|---|---|---|
| `wayland` | 177 in 3 s | 800×600: the scale ignored, as in question 3 |
| `wayland` + `--disable-features=WaylandFractionalScaleV1` | 177 in 3 s | **1200×900**, exact |
| `x11` (sway's Xwayland, `DISPLAY=:0`) | 179 in 3 s | **1200×900**, exact, no flag |
| `headless` (+ `gl-egl` or `vulkan`) | **none, and no `on_paint` either**: `Unable to allocate frame for first frame capture: OOM?` | — |

The flag is what fixes question 3's Wayland scale: with `WaylandFractionalScaleV1` off,
Chromium's Wayland screen stops overriding the embedder's `device_scale_factor`, and
`get_screen_info` decides. Changing it live works on both: scale 1.5 → 2.0 with
`notify_screen_info_changed` + `was_resized` repainted at 1600×1200 within ~50 ms, and back.
WebGL is on the GPU on both (ANGLE → GL ES 3.2 on Wayland, GL 4.6 on X11). So **the engine
asks for the window's platform**: `--ozone-platform=wayland
--disable-features=WaylandFractionalScaleV1` under a Wayland HultUI window, `x11` under X11.

**What arrives.** One plane; `CEF_COLOR_TYPE_BGRA_8888`, i.e. `DRM_FORMAT_ARGB8888`;
**modifier 0, linear**; stride 4864 for 1200 pixels (rows padded to 1216), offset 0, size
4 377 600; `coded_size` = `visible_rect` = `content_rect` = the frame; `timestamp` in µs
(16 666 apart at 60). The pool is **three buffers** (distinct dmabuf inodes; two at
800×600). On the Intel GPU through Mesa, the modifier is linear, so any importer that takes a
pitch can take it; NVIDIA, where cef#4237 is, was not available here.

**Who owns the fds, and for how long.** CEF does. Its documentation says the handle "cannot be
cached and cannot be accessed outside of this callback … the contents should be copied to a
texture owned by the client application", and the buffer goes back to the pool on return.
Seen: five distinct fd numbers in 118 frames; the previous callback's fd was still open at the
next callback 113 times and closed 4 times. So: never close it, never keep it; `dup` it only
for an import that takes ownership (Vulkan's), and finish the copy before returning.

**Import and copy, on `CrBrowserMain`, inside the callback** (`Q8Gl.cs`, `Q8Vk.cs`):

- **GL**: an EGL context on the surfaceless platform (`EGL_EXT_image_dma_buf_import` and
  `_modifiers` both present), `eglCreateImageKHR(EGL_LINUX_DMA_BUF_EXT)` with fourcc
  `AR24`, offset, pitch and modifier; `glEGLImageTargetTexture2DOES`; blit into a texture of
  ours; `glFenceSync` + `glClientWaitSync`; destroy the image. **0.81 ms average, 1.15 max**
  per 1200×900 frame. Read back: the frame exactly, the right way up
  (`results/q8-gl-imported.png`).
- **Vulkan**: `VK_KHR_external_memory_fd`, `VK_EXT_external_memory_dma_buf`,
  `VK_EXT_image_drm_format_modifier`; an image with `DRM_FORMAT_MODIFIER_EXT` tiling and an
  explicit plane layout (offset, row pitch); `vkGetMemoryFdPropertiesKHR`; a dedicated
  allocation importing a `dup` of the fd; a barrier acquiring it from
  `VK_QUEUE_FAMILY_EXTERNAL` (layout `GENERAL`, so its contents are kept) and releasing it
  after; `vkCmdCopyImage` into an image of ours; a fence. **0.73 ms average, 1.11 max**, no
  failures in 148 frames. Read back: the same frame (`results/q8-vk-imported.png`).

No synchronisation from CEF was needed: the content was complete when the callback came.

**Cost** at 1650×1072 (scale 1), the bench page animating at 60, two runs of 5 s each
(`results/q8-bench.txt`):

| Path | Frames/s | Our process | CEF's processes |
|---|---|---|---|
| `on_paint`, copied into a buffer (what `PixelFrameSink` takes) | 60.0 | 108–113 ms/s | 188–190 ms/s |
| `on_paint`, copied and uploaded to a GL texture (Skia's re-upload) | 60.0–60.2 | 147–152 ms/s | 188 ms/s |
| **shared texture, GL import + blit + fence** | 60.0 | **33–34 ms/s** | 130–132 ms/s |
| **shared texture, Vulkan import + copy + fence** | 60.0–60.2 | **33–34 ms/s** | 126–136 ms/s |

Windows, for comparison (whole HultUI sample, so with its own drawing): ~300 ms/s for
pixels, 62–106 on GL and 19–94 on Vulkan zero-copy. Here zero-copy costs a quarter of the
pixel path in our process, and CEF's processes save ~60 ms/s as well (no readback).

**What it means for step 5.5.** The copy has to happen on CEF's thread, during the callback,
into an image the engine owns; HultUI's frame then draws the newest of those, as Windows does
with its D3D11 texture. GL is the natural importer: the GPU sources here already draw
through GL, into the window's context on the GL surface and into the lent interop image on
the Vulkan one, so one EGL import serves both renderers. That needs the engine's EGL context
to share with HultUI's (or the copy target to be exported to it), which the next step
settles. A direct Vulkan import works as well and costs the same, if a Vulkan-only route is
ever wanted. Not checked here: `<select>`'s `PET_POPUP` as a shared texture, and NVIDIA.

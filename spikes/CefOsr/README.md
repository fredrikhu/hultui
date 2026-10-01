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
| 3 | Pixels | **BGRA, top-down, opaque, at the frame rate asked for; but the scale only takes on `headless`.** An 800×600 view at scale 1.5 paints **1200×900** on `--ozone-platform=headless` (a 100-CSS-pixel box is 150 device pixels), and **800×600 at scale 1** on `wayland`, whatever `get_screen_info`, `--force-device-scale-factor` or the compositor's output scale say. `headless` with `--use-angle=gl-egl` has the GPU (WebGL on ANGLE → GL ES 3.2 → Mesa) and needs no display connection. First frame 85–93 ms after `create_browser`. A still page stops painting (0 paints in 2 s); an animating one paints at exactly `windowless_frame_rate`: 60, 30, 120 and 1 measured, each the whole view. |
| 4 | The message loop | **Both work; `multi_threaded_message_loop` is the simpler fit.** With it every callback (`on_paint`, `on_title_change`, `on_before_browse`, life span, load) arrives on CEF's own UI thread, `CrBrowserMain`, in our process, and our thread does nothing. `external_message_pump` delivers them on our thread, but **only with a fallback timer**: CEF asks for work once and then stalls unless the host also calls `cef_do_message_loop_work` at least every 33 ms (cefclient's rule); with it CEF asks ~1/s idle and 186/s animating, always "now". Costs are the same either way: ~18 ms/s idle and ~50 ms/s animating at 60 in our process, ~155 ms/s in CEF's. All three shut down with every reference returned and no process left once ours exits; until then zygotes and two zombie renderers outlive `cef_shutdown` (question 10). |
| 5 | Input and focus | **All of it works off-screen.** Mouse coordinates are **view coordinates (DIP)**; clicks, right-clicks, `click_count` 2 for a double click, the wheel (−120 = one notch down, 120 CSS px, smooth). Keys: **`RAWKEYDOWN`, `CHAR`, `KEYUP`, each carrying the key's Windows code and its XKB `native_key_code`**, give a page exactly what a browser does; `native_key_code` is what fills `KeyboardEvent.code` (seam W6). Text with no key goes through `ime_commit_text`. Tab past the last element raises `on_take_focus`. `<select>` paints a `PET_POPUP` buffer at the device scale with its rectangle in DIP, and clicks inside it choose. IME composition and commit work, with character bounds in DIP. |

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

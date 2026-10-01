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
clang -O2 -o /tmp/cefosr-helper-c spikes/CefOsr/Helper/helper.c
dotnet build spikes/CefOsr
spikes/CefOsr/bin/Debug/net10.0/CefOsr q1 --ozone=wayland --subprocess=/tmp/cefosr-helper-c
```

Options: `--cef-dir=` (default `/usr/lib64/cef`), `--ozone=`, `--sandbox=off`,
`--subprocess=` (`browser_subprocess_path`; without it the program runs itself again),
`--loop=work|external|mt`, `--resource-dirs`, `--cwd=`, `--switch=name[=value]`,
`--log=verbose|info|warning|error`.

## Answers

| # | Question | Answer |
|---|---|---|
| 1 | Does Fedora's libcef load and start, windowless, from a .NET program? | **Yes, with a native helper.** `cef_api_hash(14600)` first, then `cef_initialize`: context ready ~200 ms after process start, a `data:` page loaded and painted 85–100 ms after `create_browser_sync`. Resources are found beside `libcef.so` with no `resources_dir_path` and from any working directory. **The subprocess executable cannot be .NET**: Chromium's zygote must be single-threaded to sandbox itself, and cannot fork a .NET process even unsandboxed. A 20-line C helper in `browser_subprocess_path` works with the sandbox on. Sandbox: **the namespace sandbox works in this container**, and the setuid `chrome-sandbox` beside `libcef.so` works too; `no_sandbox` is not needed. Ozone: **Wayland**, with the GPU (WebGL on ANGLE → GL ES 3.2 → Mesa); X11 (the default) and `ozone-platform-hint=auto` without a session type fail here for want of an X server; `headless` runs with no WebGL. |
| 2 | Does the versioned API hold? | **Yes.** The same binary, written against Fedora's 146 at API 14600, runs unchanged on the official CEF **154.0.32** (Chromium 154.0.8037.58, the stable release on 2026-10-01): same hash, `cef_initialize` = 1, page painted 59 ms after `create_browser`, WebGL on the GPU, sandbox on, references balanced. 154 accepts API 13300–15400; generated from 154's headers at 14600, every struct, offset and export is identical to 146's. A bundled CEF must be laid out **flat** (`Resources/` beside `libcef.so`: `resources_dir_path` does not cover `icudtl.dat`) and **stripped** (the official `libcef.so` is 1.45 GB with debug sections, 272 MB without; ~115 MB compressed in all). |

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
should ask for the platform outright (`--ozone-platform=wayland` when HultUI's window is on
Wayland, `x11` when on X11), not leave it to a hint that depends on the session's variables.

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

# NativeAOT

HultUI publishes to a **single, self-contained native executable** with
[NativeAOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/). The
whole managed framework — layout, reconciliation, reactivity, text pipeline,
rendering, event routing — is ahead-of-time compiled into the binary. No .NET
runtime needs to be installed on the target machine, and there is no JIT warmup.

## Why the framework is AOT-clean

HultUI is AOT-compatible **by design**, not by luck:

- **No reflection for app logic.** The `[Reactive]` source generator emits
  Signal-backed properties at compile time — the job a reflection-based
  framework would do at runtime. `typeof(TEvent)` / `GetType()` are used only as
  dictionary keys and for type-equality in reconciliation, neither of which
  needs runtime metadata.
- **Source-generated P/Invoke.** All native interop (`user32`, `UIAutomationCore`,
  `oleaut32`, SDL3, Skia) uses `[LibraryImport]`, which the marshalling source
  generator turns into blittable stubs — no `Marshal`-based runtime marshalling.
- **Hand-built COM for accessibility.** The Windows UI Automation provider
  (`NativeProvider`) builds genuine native COM objects out of
  `[UnmanagedCallersOnly]` vtable thunks instead of relying on the built-in COM
  marshaller or `ComWrappers`. This keeps the a11y bridge AOT-friendly (and, as a
  bonus, dodges a UIAutomationCore access-violation with `StrategyBasedComWrappers`
  CCWs on released .NET 10).
- **Hot reload is compiled out.** The `MetadataUpdateHandler` (the one place that
  touches `System.Reflection.Metadata`) is behind `#if !NATIVE_AOT`.

Every project sets `<IsAotCompatible>true</IsAotCompatible>`, so the trim/AOT
analyzers run on every build. The framework currently compiles with **zero
IL2xxx/IL3xxx warnings**.

## Building the AOT target

A sample opts in with two properties (see
`samples/HultUI.Samples.HelloWorld/HultUI.Samples.HelloWorld.csproj`):

```xml
<PublishAot>true</PublishAot>
<InvariantGlobalization>true</InvariantGlobalization>
```

`PublishAot` only engages on `dotnet publish`; ordinary `dotnet build`/`dotnet run`
are unaffected apart from the analyzers being enabled.

### Publish

```
dotnet publish samples/HultUI.Samples.HelloWorld -r win-x64 -c Release
```

The output (`bin/.../win-x64/publish/`) contains the native `.exe` plus the three
native libraries (`SDL3.dll`, `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`) and no
managed `.dll`s — everything managed is inside the executable. The `.pdb` files
are debug symbols and are not part of the shipping payload.

### Toolchain requirements (Windows)

NativeAOT links with the MSVC toolchain, so you need the **Visual Studio C++
build tools** (the `VC.Tools.x86.x64` component; a full VS install or the
standalone Build Tools both work).

The ILC targets shell out to `vswhere.exe` to locate the linker, and `vswhere`
is **not** on `PATH` in a plain shell. The reliable options:

- **Best:** run the publish from a **"x64 Native Tools Command Prompt for VS"** /
  **"Developer PowerShell for VS"**, which sets up `PATH`, `LIB`, and `INCLUDE`.
- **From a plain shell:** call `vcvars64.bat` first, and add the VS Installer
  directory (which contains `vswhere.exe`) to `PATH`:

  ```bat
  call "<VS>\VC\Auxiliary\Build\vcvars64.bat"
  set "PATH=C:\Program Files (x86)\Microsoft Visual Studio\Installer;%PATH%"
  dotnet publish samples\HultUI.Samples.HelloWorld -r win-x64 -c Release
  ```

Symptom of a missing setup: an MSB3073 link error whose command text contains
`'vswhere.exe' is not recognized` — the linker path resolved, but `vswhere`'s
error text was captured into the link command.

## Measured results (HultUI.Samples.HelloWorld, win-x64)

| | NativeAOT | Framework-dependent | Self-contained (no AOT) |
|---|---|---|---|
| Shipping payload (excl. `.pdb`) | **~18 MB** | ~17 MB | ~90 MB+ |
| .NET runtime required on target | **None** | .NET 10 (~85 MB shared) | Bundled (in the 90 MB) |
| Managed code form | Native, in the `.exe` | Loose IL `.dll`s, JIT'd | Loose IL `.dll`s, JIT'd |
| JIT warmup | None | Yes | Yes |
| Native `.exe` size | 2.5 MB | 0.16 MB apphost stub | 0.16 MB apphost stub |

The three native libraries (Skia ~11.6 MB, SDL3 ~2.7 MB, HarfBuzz ~1.8 MB)
dominate the on-disk size in every configuration, which is why AOT and
framework-dependent look similar on disk. The decisive differences are
qualitative: AOT needs **no runtime installed** and has **no JIT warmup**, and
against the honest "no runtime needed" alternative — a self-contained non-AOT
build that bundles the whole runtime — AOT is roughly **5× smaller**.

### Startup, measured

Set `HULTUI_STARTUP=1` (stderr) or `HULTUI_STARTUP=<path>` (a file, which is what
a GUI-subsystem binary needs) and any HultUI app prints its start-up phases, timed
from when the OS started the process — so the runtime's own start, which no
in-process stopwatch can see, is included. Junction, win-x64, warm runs on one
Windows 11 machine (RTX 4080), time in ms since process start:

| phase | AOT | JIT (Release) |
|---|---|---|
| `SDL_Init` returned | 31 | 59 |
| `SDL_CreateWindow` returned | 179 | 212 |
| GL context current | 181 | 214 |
| Skia surface built | 186 | 229 |
| platform window ready | 195 | 246 |
| application ready | 196 | 251 |
| **first frame presented** | **224** | **391** |

AOT is worth ~165 ms here, and essentially all of it is JIT: the app's own stretch
— building the element tree, the first layout, the first paint, revealing the
window — falls from 139 ms to **29 ms**. Everything else the framework does is
already close to free (the Skia surface 5 ms, the `Window` constructor 1 ms).

What is left is not the framework's to spend. The ~150 ms inside the first
`SDL_CreateWindow` is SDL loading the GL driver — `opengl32.dll` plus the vendor
ICD plus the WGL extension probe — which it does lazily for the first window that
asks for OpenGL. Creating the GL context afterwards is 2 ms and Skia's `GRContext`
about 11 ms; the cost is the library load. It cannot be moved off the critical
path by any arrangement of app code, because no window can exist until it
finishes, and a window created without `SDL_WINDOW_OPENGL` cannot be upgraded
later (SDL refuses, and Win32 sets a pixel format once per `HWND`). So an AOT
start-up is roughly: 30 ms runtime + `SDL_Init`, 150 ms driver load, 17 ms
context/surface/DPI plumbing, 29 ms app — within a few ms of the floor for a GL
window on this machine.

**Watch the stretch between `SDL_CreateWindow` and the GL context.** It is the place a
display query lands, and one did: re-measured on 2026-10-06, the context came 150 ms after
the window. Frame pacing (2026-09-20) reads the display's refresh rate as the window is
sized, and it asked SDL for the *current* display mode — which makes SDL list every mode
the display offers first, the first time for each display: 115–150 ms on a 4K HDR monitor.
The desktop mode answers the same question for a window, which HultUI's fullscreen always
is, without the list. Six AOT launches each way, first frame 419–531 ms (median ~445) →
259–408 ms (median ~276); the context is current 5 ms after the window again.

The very first run after a driver update pays more: the first frame compiles
Skia's shader programs (~115 ms here rather than ~20 ms), after which the GPU
driver's own on-disk program cache serves them. That cache is the driver's, not
Skia's, and needs nothing from the app.

## AOT vs JIT throughput

A common expectation is that "native code" means faster throughput. For a
GC'd, managed framework that is **not** what AOT buys you — NativeAOT keeps the
full runtime, including the **same garbage collector**. Its wins are startup,
memory, and deployment, not steady-state CPU throughput. At steady state RyuJIT
plus dynamic profile-guided optimization (PGO) recompiles hot paths with real
runtime profiles and typically matches AOT, sometimes edging ahead.

`scripts/aot-vs-jit-benchmark.ps1` runs the js-framework-benchmark suite
(`HultUI.Samples.Benchmark --auto`) under both a JIT'd Release build and the
NativeAOT binary and tabulates the median per-operation latency. A representative
run (win-x64; single run, expect noise):

| operation | JIT (ms) | AOT (ms) | AOT/JIT |
|---|---|---|---|
| create 1,000 | 28.89 | 29.42 | 102% |
| update every 10th | 5.88 | 7.54 | 128% |
| select row | 0.78 | 0.70 | 90% |
| swap rows | 4.62 | 6.11 | 132% |
| remove row | 4.82 | 6.82 | 141% |
| create 10,000 | 374.28 | 423.76 | 113% |
| append 1,000 to 1,000 | 43.43 | 48.28 | 111% |
| clear 1,000 | 15.07 | 15.51 | 103% |

Warm JIT and AOT are a **wash**, with JIT slightly *ahead* on most operations —
dynamic PGO recompiling hot paths with real profiles is doing exactly what it is
supposed to. There is no steady-state throughput win from AOT.

**A cautionary tale about warmup.** An earlier version of this table (before the
benchmark had a global pre-warm) looked very different: `create 1,000` showed
JIT 64 ms vs AOT 30 ms, a seeming 2× AOT win. That was **not** throughput — it
was JIT tier-up leaking into the first measured metric, because the HultUI
assemblies start at Tier-0 and two per-metric warmup passes at process start were
not enough to promote the shared style/layout/text pipeline. The fix was a global
pre-warm in the suite (see the benchmark README); with it, `create 1,000` drops
to ~28 ms — the AOT figure — and the "win" evaporates. The lesson: **AOT's real
advantage is the cold path** (startup, warmup, first frames); at steady state it
keeps the same GC and the same performance envelope as a warmed JIT.

To isolate the managed slice from the GPU work, run the benchmark with
`--profile`, which breaks each operation into build / style / layout / render /
present — build+style+layout is the only part a backend swap can affect.

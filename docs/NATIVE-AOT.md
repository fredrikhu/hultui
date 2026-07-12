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

Startup opens the window in a couple of seconds cold on a dev machine, but that
figure is dominated by SDL3 window creation, Skia GPU context init, font loading,
and the first render — not by runtime start. AOT's contribution is removing JIT
warmup and eliminating assembly load, which matters most for the managed
work on the first few frames.

# WinForms interop — Design

For applications migrating from WinForms to HultUI a page at a time: open a HultUI
window from a WinForms app, and mount HultUI as a `System.Windows.Forms.Control`
inside an existing form.

Everything below was spiked and measured on 2026-09-03 before this document was
written. Where something was *not* measured it says so.

Measurement environment: Windows 11 26200, .NET 10, RTX 4080 (NVIDIA 610.62),
PerMonitorV2 at 1.5× scale, **Debug** build. Treat absolute numbers as an upper
bound and the ratios as the finding.

## What was proven

A throwaway WinForms host, WinForms owning the message loop throughout:

**A — a HultUI top-level window from WinForms.** `Application` and its SDL window
created on the WinForms UI thread and frame-stepped from a WinForms `Timer`. Four
dirty-driven frames, host responsive (203 pump ticks against 16 heartbeats over
~4 s). Synthetic `WM_MOUSEMOVE` / `WM_LBUTTONDOWN` / `WM_LBUTTONUP` **posted into
the thread's own queue** reached SDL's window procedure through the WinForms loop,
were translated, hit-tested, and ran a HultUI `OnClick` handler twice. The input
path crosses the loop boundary for real.

**B — SDL adopting an existing HWND.** `SDL_CreateWindowWithProperties` with
`SDL_PROP_WINDOW_CREATE_WIN32_HWND_POINTER` produced a GL 3.3 core context on a
bare child HWND under a WinForms `Panel`; 191 frames swapped.

**C — a HultUI tree inside a WinForms `Panel`, over that adopted HWND.** ~230 lines
of `IWindowingPlatform` / `IPlatformWindow` written entirely outside the framework,
plus the public `SkiaGLRenderSurface`. Rendered, laid out, hit-tested, clicked.

**D — the same thing with no SDL anywhere.** WGL directly on the WinForms control's
**own** HWND, `SkiaGLRenderSurface` over it, input from the control's ordinary
WinForms events. Rendered at 1.5× DPI, and a posted click became a WinForms
`MouseDown` which HultUI hit-tested and handled.

**E — four HultUI surfaces in one form**, each its own control and GL context,
sharing one `Application`. All four rendered crisply and independently.

The headline: **C and D both needed zero framework changes.**
`IWindowingPlatform`, `IPlatformWindow`, `IRenderSurface`, `SkiaGLRenderSurface`
and `Application(IWindowingPlatform, …)` are already public.

## Is SDL necessary for the control? No.

Experiment D settles it. Plain `wglCreateContext` on the control's HWND gave a
4.6.0 compatibility context that Skia accepted without complaint (the SDL path
asks for 3.3 core; Skia is happy with either), and every platform service the
embedded case needs has a better WinForms-native answer than an SDL-bridged one:

| Concern | Via adopted SDL window | Via WGL on the control |
|---|---|---|
| Mouse / wheel | SDL events, bridged | `Control` events, native |
| Focus & tab order | bridged out of SDL, two-way | WinForms owns it |
| IME / text input | SDL's IMM/TSF host | `ITextInputHost` over IMM32 |
| Clipboard, file dialogs | SDL's | `WF.Clipboard`, `OpenFileDialog` |
| Drag & drop | SDL's | `AllowDrop` / `DoDragDrop` |
| UIA | free (SDL's HWND path) | needs a framework seam (below) |
| Window procedure | SDL subclasses it — must use a separate child HWND | untouched |

**The design therefore drops SDL from the control entirely.** That removes the
child-HWND trick, removes the WndProc contention with WinForms, and removes the
two-way focus bridge — a HultUI control becomes an ordinary WinForms control that
happens to paint with GL. The one thing it costs is UIA, which needs a small
framework change rather than coming free.

SDL stays for the *top-level* scenario (scenario A), where it already is the
platform and nothing is gained by replacing it.

Two traps found in the WGL path:

- **`wglGetProcAddress` returns null for GL 1.1 entry points** (and can return the
  documented bogus values 1/2/3/-1). Skia asks for those, so the resolver must
  fall back to `GetProcAddress(opengl32.dll, …)`. Not defensive coding — without
  it `GRContext` creation fails.
- **The pixel format can be set only once per HWND, for its lifetime.** WinForms
  recreates control handles on many property changes, so the control must
  recreate its context on `OnHandleCreated` / `OnHandleDestroyed`.

## What does the Nth control cost? Almost nothing.

Only the first surface pays the driver load. Per-surface mount cost, four surfaces
in one form:

| Surface | GL context | Skia surface | Total |
|---|---|---|---|
| 0 | 125.5 ms | 13.5 ms | **150.2 ms** |
| 1 | 4.3 ms | 0.9 ms | **5.3 ms** |
| 2 | 3.6 ms | 0.8 ms | **4.5 ms** |
| 3 | 4.2 ms | 0.9 ms | **5.1 ms** |

The ~130 ms in surface 0's context is `opengl32.dll` plus the vendor ICD — the same
cost [the start-up breakdown](../NATIVE-AOT.md) already attributes to driver load
in a normal HultUI app (`HULTUI_STARTUP=1`), and it is paid **once per process**,
not once per control. Every later surface is ~5 ms — a 30× drop, small enough that
mounting a control lazily on first tab activation is a nicety rather than a
necessity.

Steady-state rendering scales as expected — forced render of every surface:

| Surfaces | min | median |
|---|---|---|
| 1 | 0.15 ms | 0.26 ms |
| 4 | 0.70 ms | 1.11 ms |

**Sharing buys nothing; do not build it.** Sharing GL display lists
(`wglShareLists`) and handing every surface one `SkiaTextServices` was measured
and made no difference — per-surface cost stayed ~5 ms and the forced-render
median got marginally *worse* (1.43 ms vs 1.21 ms for four surfaces). There is
nothing left to share: after the first, a `SkiaTextServices` costs about a
millisecond because the font collection is already warm. So `SkiaGLRenderSurface`
does not need a "bring your own `GRContext`" overload, which was on the table
before this was measured.

**One real trap: vsync.** Before calling `wglSwapIntervalEXT(0)`, the median
forced render of a *single* surface was 8.3 ms — `SwapBuffers` blocking on the
compositor. With N surfaces that serialises into N frame intervals. SDL sets swap
interval 0 for exactly this reason; the WGL path must do it explicitly, and it is
the difference between 0.26 ms and 8.3 ms per frame.

## What is wrong today

One thing blocks both scenarios:

**`Application.Run()` is the only public way to drive frames.** The per-frame steps
— `ProcessPendingResizes`, `ProcessPendingActivations`, `DrainMainThreadQueue`,
`RenderDirtyWindows`, `CleanupClosedWindows` — are `internal`, with
`InternalsVisibleTo` only for `HultUI.Headless`. A host owning its own message
loop cannot call `Run()`, so all five spikes reached those methods by reflection.

And one thing degrades the embedded case:

**`AccessibilityBridgeFactory` type-tests the concrete `SDL3Window`**
([AccessibilityBridgeFactory.cs:35](../../src/HultUI/AccessibilityBridgeFactory.cs)),
so a WGL-backed platform window falls back to `NullAccessibilityBridge` — no UIA,
and a screen reader sees an empty region where the HultUI content is.

## The design

Everything WinForms-shaped lands in a new **`src/HultUI.WinForms`**. The framework
gains two small seams, neither of which mentions WinForms.

### Framework changes (the whole list)

**1. A public frame step.**

```csharp
/// <summary>
/// Runs one frame: pumps native events, drains posted work and await
/// continuations, ticks animations, renders every window that needs it.
/// Returns false once no application window is open. For hosts that own
/// their own message loop; Run() is a loop over this.
/// </summary>
public bool RunFrame(TimeSpan delta);
```

`Run()` becomes a loop over `RunFrame` plus its idle `WaitForEvents` — which a
host must *not* inherit, since parking the thread is the host loop's business.
`HeadlessSession.PumpFrame` collapses into the same call, so the two frame
sequences can no longer drift apart. Strictly required.

**2. HWND discovery for accessibility.** A minimal interface in
`HultUI.Core/Platform`, discovered by type test the way `IWindowChrome` already
is:

```csharp
/// <summary>Implemented by platform windows backed by a real Win32 HWND, so
/// window-handle services (UIA) work regardless of which backend created it.</summary>
public interface IWin32WindowHandle { nint Hwnd { get; } }
```

`SDL3Window` implements it by returning `GetWin32Hwnd()`; `AccessibilityBridgeFactory`
switches its test from `is not SDL3Window` to `is not IWin32WindowHandle`. About
fifteen lines, and it is the right shape independently of WinForms — the factory
should not have known a concrete backend type in the first place.

**3. Nothing else.** `Window`, `Element`, layout, styling, the reactive graph, the
render pipeline and `SkiaGLRenderSurface` are untouched. In particular the
`SDL3Window.Adopt` and `WindowStyle.Embedded` changes an earlier draft of this
document proposed are **not needed** — experiment D removed the reason for them.

### `src/HultUI.WinForms`

A new package, `net10.0-windows` with `UseWindowsForms`, referencing `HultUI`. The
only Windows-Desktop-flavoured project in the repo; the framework's own `net10.0`
targets stay clean and nothing else in the solution depends on it.

```
src/HultUI.WinForms/
  HultUIControl.cs              the Control: HWND + GL lifetime, sizing, DPI, cursor, focus
  HultUIApplicationHost.cs      owns the one Application; the pump; RunFrame scheduling
  WinFormsWindowingPlatform.cs  IWindowingPlatform; binds each Window to a control
  WglPlatformWindow.cs          IPlatformWindow over the control's GL context
  Interop/Wgl.cs                pixel format, context, swap interval, proc resolver
  Input/WinFormsInputTranslator.cs   Control events → InputEvent
  Input/WinFormsTextInputHost.cs     ITextInputHost over IMM32
  Services/WinFormsClipboard.cs      IClipboard over WF.Clipboard
  Services/WinFormsFileDialogs.cs    IFileDialogs over the WinForms dialogs
  HultUIWindow.cs               "open a HultUI window from a WinForms app" (SDL-backed)
```

**`HultUIApplicationHost`** owns the single `Application` for the process
(`Application.Current`, `InvokeOnMainThread` and `UIThread.Bind` make it a de-facto
singleton — several controls in one form share it, which the existing multi-window
support already handles) and drives `RunFrame` from the WinForms loop.

The pump is a `System.Windows.Forms.Timer`, not `Application.Idle`. The spike
measured `Idle` firing 19 times in four seconds against 234 timer ticks: `Idle`
fires when the queue drains, which is not a frame clock. HultUI's own dirty
tracking decides whether a frame is actually produced — a static UI rendered 2–4
frames across a whole run — so the host needs no throttle of its own.

**`HultUIControl`** is the mount point. It creates its GL context in
`OnHandleCreated`, tears it down in `OnHandleDestroyed`, calls
`wglSwapIntervalEXT(0)` once current, resizes the surface from `OnResize`, and
takes `DisplayScale` from `DeviceDpi` (handling `OnDpiChangedAfterParent`). It sets
`Opaque | AllPaintingInWmPaint | UserPaint` and overrides `OnPaintBackground` to
nothing, so WinForms never erases pixels GL owns.

### Airspace — accepted, not solved

A GL-backed control is an airspace island: WinForms controls cannot be drawn over
it, and HultUI popups, dropdowns and context menus are clipped at the control's
edge rather than escaping it. This is accepted as a constraint rather than
designed around, on the expectation that adoption units are large composed regions
— a whole tab page, a whole panel — where popups fit inside the region anyway.

A raster backend (`SkiaRasterRenderSurface` blitted in `OnPaint`) would remove the
constraint at CPU cost. It is **not** in the plan; it is recorded here as the known
escape hatch if a real migration hits a case where overlap matters.

### Passing the host control through `CreateWindow`

`Application.CreateWindow(title, width, height, style)` gives the platform no way
to say *which* control to mount into. `WinFormsWindowingPlatform` carries a
`PendingControl` field set immediately before the call — the same technique
`HeadlessWindowingPlatform.NextWindowScale` already uses in-repo, and it keeps
`Application`'s API unchanged. If that grows uncomfortable, an
`Application.CreateWindow(Func<IWindowingPlatform, IPlatformWindow>)` overload is
the escape hatch; not needed for the first cut.

## Staging

1. **`Application.RunFrame`** plus a docs section on host-owned loops, and
   `HeadlessSession.PumpFrame` rewired through it. Unblocks "open a HultUI window
   from a WinForms app" completely, with no new project. (~half a day)
2. **`IWin32WindowHandle`** and the accessibility factory switch. (~1 hour)
3. **`HultUI.WinForms` with `HultUIControl`**: HWND/GL lifetime, sizing, DPI,
   cursor, mouse input, the pump. This is the spike's experiment D promoted to
   production. (~2 days)
4. **Keyboard, focus and IME**: `ITextInputHost` over IMM32, `ProcessDialogKey` /
   `IsInputKey` so Tab and arrows reach HultUI's `FocusManager` and traversal can
   also leave the control. (~1–2 days)
5. **Clipboard, file dialogs, drag & drop** over their WinForms equivalents.
   (~1 day)
6. **A sample** under `samples/` — a form with a menu strip, a `DataGridView`, and
   a `HultUIControl` filling a tab page, which is the actual migration shape.

Step 1 is worth doing regardless of whether the control ships: a public frame step
serves any host loop — a game loop, a plugin inside another application, a test
harness that is not `HeadlessSession`.

## Open questions

- **UIA on a WGL control is unverified.** Step 2 makes the bridge *reachable*;
  whether a screen reader reads the region correctly when the HWND is a WinForms
  control rather than an SDL window has not been tested. It should work — the
  bridge only needs an HWND and subclasses its window procedure — but WinForms
  also has opinions about `AccessibleObject` on its controls, and the two may
  need reconciling.
- **Multiple `Application` instances silently rebind `Application.Current`.** The
  design says one per process, which is correct, but the failure mode for a caller
  who constructs a second is a silent rebind rather than an exception. A guard is
  probably right, and is a framework change not listed above.
- **Non-NVIDIA drivers.** All numbers here are one GPU. The ~130 ms first-context
  cost is driver-dependent and worth re-measuring on Intel integrated graphics
  before quoting it to anyone.
- Nothing here addresses WPF. The same two framework seams would serve an
  `HwndHost`-based WPF control and the airspace analysis is identical; separate
  document if it is ever wanted.

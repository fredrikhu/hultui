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

**F — a real UI Automation client against the WGL control.** FlaUI (UIA3, the
same interface a screen reader uses) attached from a separate process and read the
HultUI tree off the WinForms control's HWND. See below — it works.

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
| UIA | free (SDL's HWND path) | needs a framework seam — **measured working**, below |
| Window procedure | SDL subclasses it — must use a separate child HWND | untouched |

**The design therefore drops SDL from the control entirely.** That removes the
child-HWND trick, removes the WndProc contention with WinForms, and removes the
two-way focus bridge — a HultUI control becomes an ordinary WinForms control that
happens to paint with GL. The one thing it costs is that UIA needs a small
framework change instead of coming free, and experiment F confirmed that change is
all it needs.

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

## Does UIA survive on a WinForms control? Yes.

The one risk that could have flipped the decision back to SDL adoption, spiked
before committing. Experiment F reproduced exactly what
`AccessibilityBridgeFactory`'s Windows branch does — action router, UIA host
bridge, `WindowsAccessibilityBridge` over the HWND — but against the WinForms
control's handle, then attached FlaUI (UIA3) from a *separate process* and read
the tree back.

**The HultUI element tree is exposed.** Looked up by `AutomationId`, across the
process boundary:

```
heading-0 : found - type=Text   name='WGL surface 0'
button-0  : found - type=Button name='Click me'
count-0   : found - type=Text   name='clicks: 0'
```

**Patterns work, not just properties.** `IInvokeProvider.Invoke()` on `button-0`
reached the HultUI click handler (`D: surface 0 click handler ran (1)`) and the
count element's Name changed from `clicks: 0` to `clicks: 1` — so the action
router marshals an AT-thread call onto the UI thread's frame drain correctly on
this path too.

**Bounds are right.** The bridge converts with `ClientToScreen` against whatever
HWND it was handed, so the control's client origin is the correct origin and the
rects land where a screen reader would draw its highlight, at 1.5× scale:

```
window rect     : X=177 Y=177 W=600 H=507
host panel rect : X=185 Y=517 W=585 H=160
button-0 rect   : X=203 Y=601 W=81  H=45     ← inside the hosting control
```

**No duplicate subtree.** The concern was that WinForms' own `AccessibleObject`
on the control would answer alongside HultUI's provider and a screen reader would
see the region twice. It does not:

```
Pane  id='embedPanel'                (the WinForms Panel)
  Pane                               (the TableLayoutPanel)
    Window id='2230124'              (the HultUI control — WinForms' own node)
      Pane (HultUI fragment root)    (HultUI's tree hangs here)
```

One WinForms wrapper node, then HultUI's fragment root under it. The content is
not doubled.

Two cosmetic things for `HultUIControl` to fix, neither structural:

- The control surfaces as `ControlType.Window` with the HWND value as its
  AutomationId, because a bare `Control` has no name. Set `AccessibleName` /
  `AccessibleRole` on it so the wrapper node reads sensibly.
- `Sdl3UiaHostBridge` is misnamed — it carries nothing SDL-specific, which
  experiment F proved by running it in a process where SDL was never initialised.
  Rename it `UiaHostBridge` when the factory changes.

Caveat on method: the bridge was attached eagerly by hand, whereas the factory
builds it at `Window` construction and leaves it inert until an AT probes. Same
code, different moment — but the lazy-activation path on a WinForms control has
not been separately exercised.

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
and a screen reader sees an empty region where the HultUI content is. This is a
type test, not a real dependency: experiment F wired the bridge up by hand against
a WinForms control's HWND and everything worked (below).

## The design

Everything WinForms-shaped lands in a new **`src/HultUI.WinForms`**. The framework
gains two small seams, neither of which mentions WinForms.

### Framework changes (the whole list)

**1. A public frame step. — Implemented.**

```csharp
/// Whether the application still has work to do: no quit requested and at least
/// one of its own windows open. A host watches this to know when to stop.
public bool IsRunning { get; }

/// Runs one frame — pump, resizes, activations, posted work, animations, render.
/// Returns true when any window rendered. Nothing here waits: parking the thread
/// until the next event is the caller's policy.
public bool RunFrame(TimeSpan delta, bool forceRender = false);
```

Two members rather than the one this document first proposed. Liveness and
"did anything render" are different questions and a host needs both — the first
to decide when to stop, the second to decide whether to idle — so folding them
into one return value would have forced a struct or an out parameter for no gain.

`forceRender` was not in the original sketch either. A host needs it: after
occlusion a `WM_PAINT` arrives with the GL back buffer's contents undefined, and
it must be redrawn even though nothing in the tree is dirty.

`Run()` is now a loop over `RunFrame` plus its idle `WaitForEvents`, and
`HeadlessSession.PumpFrame` calls the same method instead of assembling its own
copy of the sequence — so a test frame and a real one can no longer drift apart.
With Headless no longer assembling frames, `ProcessPendingResizes`,
`ProcessPendingActivations`, `DrainMainThreadQueue` and `CleanupClosedWindows`
stopped needing to be `internal` and are now private.

One thing a host must *not* do: call `InstallSynchronizationContext`. It stays
internal deliberately. WinForms installs its own context on the UI thread, and
replacing it would route the host's own `await`s through HultUI's frame queue —
working, but surprising, and broken the moment the pump stops.
`Application.InvokeOnMainThread` works either way, because `RunFrame` drains that
queue itself.

**2. HWND discovery for accessibility. — Implemented.** A minimal interface in
`HultUI.Core/Platform`, discovered by type test the way `IWindowChrome` already
is:

```csharp
/// <summary>Implemented by platform windows backed by a real Win32 HWND, so
/// window-handle services (UIA) work regardless of which backend created it.</summary>
public interface IWin32WindowHandle { nint Hwnd { get; } }
```

`SDL3Window` implements it by returning `GetWin32Hwnd()`, and
`AccessibilityBridgeFactory` keys the Windows branch on the interface instead of on
`SDL3Window`. It is the right shape independently of WinForms — the factory should
not have known a concrete backend type in the first place.

The Linux branch deliberately keeps its `SDL3Window` test. AT-SPI has no
handle-shaped seam to key on — that bridge registers the application on the D-Bus
session rather than attaching to a window — so there is nothing for a backend to
implement, and "is this a real desktop window" is still the question being asked.
Generalising it without a second Linux backend to generalise *over* would be
guessing.

`Sdl3UiaHostBridge` was renamed `UiaHostBridge` in the same change: it reads the
window's scale and tree and talks to the router, none of which knows what created
the HWND, and experiment F ran it unchanged in a process where SDL was never
initialised.

The spike then had its hand-wiring deleted — the platform window implements the one
property and the factory does the rest — and the UI Automation probe still passes
end to end. **Implementing `IWin32WindowHandle` is now the whole of what an embedded
backend must do to be readable by a screen reader.**

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

1. ~~**`Application.RunFrame`** plus `HeadlessSession.PumpFrame` rewired through
   it.~~ **Done.** `IsRunning` + `RunFrame(delta, forceRender)`, eight tests in
   `HultUI.Headless.Tests/RunFrameTests.cs`, and the spike re-run on the public API
   with the reflection deleted — every experiment behaves identically, and the host
   no longer touches SDL at all, because `RunFrame` pumps the platform itself.
2. ~~**`IWin32WindowHandle`** and the accessibility factory switch, plus the
   `Sdl3UiaHostBridge` → `UiaHostBridge` rename.~~ **Done.** Four tests in
   `HultUI.Headless.Tests/AccessibilityBridgeSelectionTests.cs` pin the selection
   from both sides (a non-SDL window with a handle gets the live bridge; no handle,
   and headless, stay on the Null one), and the gated real-window UIA suite still
   passes on the SDL path. The *end-to-end* FlaUI test still lives in the spike:
   promoting it into `tests/HultUI.UiTests` needs a WinForms sample to point at, so
   it moves to step 6 with the sample it depends on.
3. ~~**`HultUI.WinForms` with `HultUIControl`**: HWND/GL lifetime, sizing, DPI,
   cursor, mouse input, the pump.~~ **Done.** `src/HultUI.WinForms` and
   `samples/HultUI.Samples.WinFormsHost`, a form with a menu strip, a `DataGridView`
   and two `HultUIControl`s sharing one application. Verified by driving the sample
   with a real UI Automation client: the tree reads, `Invoke` reaches the handler,
   element bounds land exactly where the button is drawn, and everything renders
   crisply at 144 DPI.

   Two things worth knowing about the shape it took. `RootFactory` is a
   `Func<Element>` rather than an element, because WinForms recreates a control's
   handle on its own schedule and a new handle means a new GL context, surface and
   framework window — the tree is rebuilt for the new window rather than moved to it.
   And the platform window is disposed synchronously in `OnHandleDestroyed` rather
   than left for the framework to reap a frame later, because by then the HWND its GL
   context is bound to is gone.
4. ~~**Keyboard, focus and IME**~~ **Done.** `IsInputKey` claims arrows and Enter,
   `ProcessDialogKey` owns the Tab boundary, `WM_CHAR` becomes text input (holding
   back a lone high surrogate for its partner), and `WM_IME_COMPOSITION` is taken over
   so the framework renders the preedit itself and Windows does not draw a second one
   over the top. `WinFormsTextInputHost` arms and disarms an IMM32 context on focus and
   puts the candidate window at the caret.

   **This step needed a framework change the design had not anticipated.**
   `FocusTraversal` wrapped unconditionally, so Tab inside a hosted tree could never
   leave it — "traversal can also leave the control" was not implementable as written.
   `FindNext`/`FindPrevious` and `FocusManager.MoveFocusNext`/`MoveFocusPrevious` gained
   a `wrap` flag and a bool return, and `Window` gained public `MoveFocus(forward,
   wrap)`, `FocusEdge(first)` and `ClearFocus()`. A window still wraps, because Tab has
   nowhere else to go there; a host passes `wrap: false` and reads the false return as
   its cue to move on. Six tests in `FocusBoundaryTests` pin both behaviours.
5. ~~**Clipboard, file dialogs, drag & drop** over their WinForms equivalents.~~
   **Done.** Clipboard and dialogs landed with step 3, because `IPlatformWindow`
   requires them; this step is drag and drop, in both directions and through WinForms
   rather than SDL. Inbound: `AllowDrop` is on by default (matching SDL, where the OS
   delivers every drop and the tree decides), and `OnDragEnter` announces the payload
   *before* the first position — the position is what runs the hit test and tells the
   element a drag has arrived, so a target inspecting the data any earlier would see an
   empty payload. A drag that leaves without dropping ends at a position outside every
   element, so no `Drop` is dispatched but the hovered element still gets its
   `DragLeave`. Outbound: `WglPlatformWindow` implements `INativeDragSource` over
   `Control.DoDragDrop`, which the framework discovers by type test exactly as it does
   for SDL.

   Effects are mapped member by member rather than cast. The two enums share their bit
   values today, and a cast would keep working until one of them gained a member — at
   which point a Move reported as a Copy would leave a source holding a file it had
   promised to delete.

   **An actual drop is not automated, and cannot be.** OLE drag-and-drop runs a nested
   modal loop driven by the physical mouse; posted messages do not enter it, which is
   what every other interaction here relies on. The test covers what survives that
   limit — the capability is genuinely present (a mistake in the `INativeDragSource`
   type test reports "no" rather than failing loudly) and a lazily created tab page
   mounts — and the drop path itself is exercised by hand.
6. ~~**A sample**, and experiment F's FlaUI probe promoted into
   `tests/HultUI.UiTests`.~~ **Done.** `HultUI.Samples.WinFormsHost` is a form with a
   menu strip and a `DataGridView` beside a `TabControl` whose first two pages are
   `HultUIControl`s and whose third is untouched WinForms — a page at a time, which is
   the shape a migration actually takes. The palette page also covers the lazy case: a
   `TabControl` does not create a page's handle until it is first shown, so that control
   mounts long after the first.

   `WinFormsHostSampleTests` (7 tests, behind the same `HULTUI_UITESTS` gate as the
   other real-window suites) drives it as a UI Automation client: the tree is readable,
   `Invoke` reaches the handler, bounds are real screen coordinates, the host's own
   `DataGridView` and `TabControl` are still in the tree beside it, typing lands,
   Backspace takes the key path rather than the text path, and Tab moves through the
   tree and then out of it.

   Input is **posted** to the control's handle rather than synthesised: a posted message
   is dispatched by the application's own loop, which is the path a real keystroke takes,
   without moving the developer's mouse or needing to win a race for the physical input
   queue. The handle comes from UI Automation — HultUI's own providers report no native
   handle, so the first ancestor that does is the hosting control.

   **The suite earned its place immediately.** It found that clicking a `HultUIControl`
   never gave it Win32 focus: WinForms grants focus on mouse-down by deferring to
   `DefWindowProc`, which does not focus a custom-painted child window. Everything looked
   fine — the control rendered, clicks worked, typing worked, because a posted `WM_CHAR`
   reaches the window procedure regardless — and only Tab was dead, because the form's
   dialog-key routing starts at the focused control and never reached
   `ProcessDialogKey`. `HultUIControl.OnMouseDown` now focuses explicitly.

Step 1 is worth doing regardless of whether the control ships: a public frame step
serves any host loop — a game loop, a plugin inside another application, a test
harness that is not `HeadlessSession`.

## Open questions

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

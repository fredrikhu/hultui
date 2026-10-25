# Round 3 survey: 4. .NET runtime

At ac5d415, JIT and NativeAOT; real windows never focused; an env-gated per-loop log probe (passes,
frames, waits, posts, wakes, input events, UI-thread bytes, GCs, heap, private bytes) and heap dumps.
Probes reverted; worktree clean. Probe code, logs and heap stats in the session scratchpad `r3rt/`
(probe/Program.cs, LoopLogProbe.cs, looplog-probe.patch, logs/, dumps/*.stat.txt, jit-1.json,
aot-1.json, cmp.py).

SATISFIED: no

## Review

- **UIA keeps closed windows alive (leak; windows closed during round 2 or before).** A real SDL window
  opened and closed, unfocused, with this machine's UIA client listening: managed heap after a full GC
  1.29 MB → 18.8–19.4 MB at 50 cycles → 36.7–37.7 MB at 100–300 cycles; each closed 60-row window kept
  ~357 KB; plateau at ~100 windows. Private bytes 112 → 165–179 MB. With HULTUI_A11Y=0 the heap stayed
  flat at 1.18 MB. Root path (heap dump): strong handle → UiaFragmentRoot → WindowsAccessibilityBridge →
  UiaHostBridge → Window (`_disposed`=1) → the whole element and a11y trees. Cause:
  `WindowsAccessibilityBridge.Dispose` (WindowsAccessibilityBridge.cs:765-788) releases only its own
  reference and never tells UIA the window is gone; `UiaDisconnectProvider` is declared at
  UiaInterop.cs:61 and never called. Prototype (reverted): `UiaReturnRawElementProvider(hwnd, 0, 0, NULL)`
  in Dispose, or `UiaDisconnectProvider` on the root and its wrappers, kept the heap flat at 1.29 MB over
  150 cycles, private 139–143 MB. Round 2's survey read this flat over 400 cycles; unclear whether a
  round 2 change caused it or a different client listens today. → a11y-uia-disconnect-on-close.
- **Notes for runtime-idle-wakeups (no objection).** Three round 2 additions are noticed only because
  the loop polls every 16 ms and must become deadlines or wakes: 444ba70's range-value trailing refresh,
  250 ms (Window.cs:827-828); 444ba70's AT-SPI `SetRangeValue` queues its action without a wake
  (AccessibilityActionRouter.cs:124); 2170222's AT-SPI status flip only sets a flag from the watcher
  thread (LinuxAccessibilityBridge.cs:131).
- **No allocation regressions.** Perf suite at ac5d415, JIT and AOT, 102 rows: no row outside round 2's
  captures by ≥10 % and ≥1 KB; AOT within 1 KB of JIT on 80 of 102 rows, the other 22 within 4 % or known
  AOT gaps (scroll/jump 6.3 vs 9.1). Caches bounded: 450 real-window mount/unmount cycles (unique 3,000-line
  JSON TextArea with spans, a 1,000-item `RenderWith` list with selection, gradients and shadows at moving
  sizes, z-indexed children, a 200-paragraph rich text editor) plateau at 15.9–16.3 MB (a11y off) or
  16.4–16.7 MB (a11y on) from cycle 150, within the text caches' 20 MB of bounds; handles flat. Thread
  safety holds: 240d468's versions are read on bridge threads, their lazy fields (`_plain`,
  `RichDocument._plainText`, `_starts`) idempotent, `ToPlainText` memoized per document (a UIA ValueValue
  read copies nothing new); 12d9742's worker touches no TextPipeline, takes the tokenizer only under the
  highlighter's `Gate`, its `CancellationTokenSource` has no registrations to dispose; c2bf858, 91efc25
  and bff6de2 are UI-thread only.

## Candidates

- **runtime-idle-wakeups** (re-ranked; first in this area). The idle loop waits for the next deadline it
  owns, or a wake, instead of `WaitForEvents(16)` (Application.cs:533). Idle: 59.8–61.9 loop passes/s in
  every real window (GL and Vulkan, JIT and AOT, the probe and all five apps; Junction and Reel also AOT),
  every pass a 16 ms timeout, 0 B per pass. Minimized: 37.4–38.5 passes/s (Windows coarsens timers).
  WinForms host ticks every 8 ms (HultUIApplicationHost.cs:34, :148). Deadlines needed: active animations
  incl. delay tweens (exact due time); a11y layout trailing refresh 100 ms (Window.cs:824-825); range
  trailing refresh 250 ms (Window.cs:827-828, new); resize throttle (Application.cs:590); inspector refresh
  (Application.cs:414); Vulkan colour poll only without colour events (SDL3Window.cs:1289-1293). Explicit
  wakes needed: `AccessibilityActionRouter` queues (AccessibilityActionRouter.cs:46-143); AT-SPI status
  flip (LinuxAccessibilityBridge.cs:131); AT-SPI attach. Already wake: WM_GETOBJECT, `InvokeOnMainThread`,
  `SynchronizationContext.Post`, SDL events. Idle bar: ~60 → ≤1 wake/s idle in all five apps; minimized
  38 → 0; ~10/s only for a Vulkan window without colour events. Medium; no API or pixel impact.
- **a11y-uia-disconnect-on-close** (owner 7; raised by 4). Tell UIA a window's providers are gone when
  its bridge is disposed: `UiaReturnRawElementProvider(hwnd, 0, 0, NULL)`, optionally
  `UiaDisconnectProvider` for the root and its wrappers. 150 open/close cycles: managed 36.7 → 1.29 MB,
  private 175 → 140 MB; each closed window kept its whole tree (TextElement 656 B each) until ~100 more had
  closed. Retained bar (a leak closed); hits the DevTools inspector, WinForms hosts (each handle
  recreation is a new window), multi-window apps. Small; a reader sees the same events.
- **runtime-pointer-events-on-demand** (owner 4; touches Events, layout's lead "enter/leave allocates per
  move"). Create PointerMove, PointerEnter and PointerLeave event objects only when an element on the
  path has a handler of that type; interaction-style hover (`SetInteractionHovered`) needs none. Real
  windows: 152 B per input event JIT with no hover change; 189 B JIT and AOT with hover changes; posted
  moves give 3 input events each, so 456–568 B per hover frame. Every app's hover frame is 568 B on GL
  (JIT and AOT), ~2.5 KB on Vulkan; the render pass is 0 B. Breakdown: `MouseMoveInput` 40 B
  (EventTranslator.cs:52) + `PointerMoveEvent` 64 B (Window.cs:1360) per move, plus Enter and Leave 56 B
  each per hover change (PointerState.cs:50, :58); ~48 B per event unattributed. Suite input/hover over
  1,000 rows 0.2 KB/op; a 1 kHz mouse makes ~100–150 KB/s of garbage. Churn on input/hover −50…−75 %;
  real-window hover frame 568 → ~150 B. `MouseMoveInput` stays (public `IPlatformWindow.InputReceived`
  contract). Small; no API change.
- **apps-tooling-generic-framelog** (4; build with runtime-idle-wakeups as its measurement). The env-gated
  per-loop log used in this survey: ~100 lines + 9 hook lines in Application and Window (patch:
  scratchpad r3rt/looplog-probe.patch, LoopLogProbe.cs). No app changes, JIT and AOT; logs passes, frames,
  idle and paced waits with early returns, posts, wakes, input events, UI-thread bytes per
  pass/frame/input, GCs, heap and private bytes. Tooling. Small.
- **tooling-textbench-warmup** (4; re-ranked lower). Warm until `JitInfo.GetCompiledMethodCount` stops
  rising; print the methods compiled while measuring. At ac5d415 TextBench compiled 990 methods during
  measurement (1,288 before), despite its 3 s time-based warm-up (Program.cs:46-55). Tooling. Small.
- **test-frame-pacing-event-precondition** (4; last). Make the storm thread's cadence independent of
  `Sleep(1)` granularity. At 120 Hz the precondition needs >363 events and the storm raises ~510; it
  fails only on displays ≥~170 Hz (FramePacingTests.cs:36-37, :116-119). Test reliability. Small.

## Examined, not worth it

- Idle passes and steady frames hold in real windows: GL idle 0 B per pass, JIT and AOT; a spinner draws
  120 frames/s at 2.1–2.3 passes per frame, 0 B per frame, 0 GCs, JIT and AOT; all five apps 0 B per idle
  pass. Vulkan frames 2,138 B (JIT), 2,158 B (AOT); Aperture and Reel hover frames ~1.95–2.0 KB above GL —
  confirms vk-swapchain-target-per-frame.
- Vulkan display-colour poll: 10.6–10.9 KB/s JIT, 13.0 KB/s AOT per idle Vulkan window without colour
  events (`FindTarget` arrays and string marshalling 2–3 times per poll, WindowsDisplayColor.cs:153-154,
  SDL3Window.cs:1277-1294). Aperture and Reel use events (0 B). Only `HULTUI_RENDERER=vulkan`; becomes a
  100 ms deadline under idle-wakeups.
- cfe2691 `ServiceUndrawnSources`: whole-tree walk every pass while a minimized window is dirty
  (Window.cs:860-886), O(n) at ~38 passes/s, 0 B; can fold into idle-wakeups (also gl-vk-undrawn-sources-walk).
- `GpuFrameSink.TryRenderFrame` closures: a display class, an `Action` and a `GetProcAddress` delegate per
  drawn video frame, ~190 B (GpuFrameSink.cs:78-107); <1 % of Reel's playback bytes per frame.
- 1d61775 gradient cache keyed by snapped rect: a gradient whose size changes every frame misses every
  time and now also copies its stops (+40–56 B per miss). No app animates a gradient's size.
- 898a65e `LruCache.Counted` (MeasurementCache.cs:227) counts an entry at most at half the bound; two
  layouts over 2 MB could outlive the 4 MB layout bound until the next wrapped layouts evict them. Needs a
  single label over 2 MB; none in the apps.
- bff6de2 keyed item rows build a new `_retained` dictionary per selection move; small next to the row's
  25.7 KB/op.
- 12d9742 keeps the last response (text, lines, starts, spans) after its tab closes, as before; bounded at
  1–2 bodies.
- Minimized-window passes: 37–38/s, 0 B, GL; Vulkan adds 266–272 B per pass from the colour poll.
- Live-repo Junction "idle": 2.2–2.6 posts/s from other sessions' git activity (GitWatcher), ~440–860 KB
  per rebuild frame — the known whole-app rebuild, not idle.

## Needs the user

None.

SATISFIED: no

All numbers are counts and bytes at 440ba012 (.NET 11.0.100), from NativeAOT probe hosts, headless and in real windows. The probe was reversed with `git apply -R`, the worktree is clean, and the build servers are shut down. **One switch reproduces "before":** `LayoutProbe.NoSpread` turns d43b3518's new branch off at run time, alternated in one process and across launches over the same states.

REVIEW:
- **d43b3518 (content-based row base): no objection. Fewer measure calls, equal bytes.** Spread vs not, same states, headless AOT, Junction 1440×900:
  - **Start + History:** MeasureSize 946 → 624 (−34 %), its cache hits 332 → 181, leaf misses 555 → 553, leaf hits 790 → 619 (−22 %), UI 1,951.6 → 1,932.0 KB.
  - **History Down, same commits:** MeasureSize 5.17 → 2.58 and 11.5 → 5.75 a key (−50 %), leaf misses equal, UI 27.0 → 26.2 and 38.5 → 37.6 KB.
  - **History resize, 4 px:** MeasureSize 354.5 → 236.3 (−33 %), leaf hits 307 → 235, UI 26.9 → 27.3 KB (noise).
  - **View switches:** MeasureSize 228–259 → 210–228; first visits 645 → 612; UI ±1 %.
  - **Wrapped diff:** 574 → 571, 72.3 = 72.3 KB.
  - **Real window, same counts:** view switch MeasureSize 295–323 → 241–243, UI 328–334 vs 322–334 KB.
  - **Vesper:** start 1,935.6 → 1,935.2 KB; navigation equal; Detail resize 1,354 → 1,351; wrapped reader 222 → 219.
  - **Meridian (start, workspace, 600-op spec):** MeasureSize 623 → 592, UI 2,375.6 → 2,368.5 / 2,370.7 KB.
  - **New work, all of it 0 B in the layout phase:**
    - Junction start: 270 spread asks (173 computed), 590 max-content computes, of which 357 are unbounded label measures.
    - A History resize step: 70 asks, 16 computes, 9.5 label measures.
    - 4–18 max-content computes a step are thrown away (wider than the room).
  - **Expert 8's +1.3 % UI KB:** none of it is from d43b3518 (±1 % in all three apps, mostly down).
- **7c0a3b79 (`OnLayoutChanged`): no objection.**
  - **Nobody listening:** one static read per box written (Junction: ~650 a view switch, ~89 a History key, ~433 a resize step) and one field read a frame.
  - **With `LayoutWatch.On` and a listener (prototype on the root):**
    - each box write adds a thread-static read and a handler scan;
    - each unmount adds a `ConditionalWeakTable.Remove` (29 a History key, 220–226 a Junction view switch, 150 a Vesper navigation);
    - UI KB equal in every series (Junction wrapped diff 72.2 / 72.0 vs 72.3 / 71.8; view switch 297 / 313 vs 301 / 312), with 1 event a step when the listener's box moves. Below noise.
- **e28d72f6, 2603955a, 7a79fc19: no objection.**
  - e28d72f6: one closure per flyout opened, loops O(menu depth).
  - 2603955a: two flag reads a pass a window.
  - 7a79fc19: an `IsShownInWindow` parent walk a frame, only while a tooltip owner is set. O(depth), 0 B.
- **Round 15, all hold except b71c57de, which was not measured:**
  - **29f1ddc0:** Vesper's wrapped reader makes 0 builds a resize step, 12.5–17 KB headless, 12.6 KB real, 1 frame.
  - **7f3c9fd0:** Junction's wrapped diff makes 1 build a 4 px step, 62–73 KB, 1 frame.
  - **b71c57de:** not measured (it needs a GitLab review open).
  - **4047ce4d:** code read only.
- **c32c4b2e (expert 8's lead): mechanism confirmed, but it does not reach a user's drag.**
  - **The cause:** the size message's live frame (`RenderLiveResizeFrame` → `OnWindowResized` → `RenderFrame`) runs inside the posted action's batch, so it draws before the build; the batch's flush then builds and draws again.
  - **Junction wrapped diff, real window, steps posted with `InvokeOnMainThread`:** 2.0–2.2 frames a step, every step a frame inside the batch. The 16 ms drag draws 2.1 frames a step, 109–112 vs 96–99 KB.
  - **The same `SetWindowPos` from a window message on the UI thread:** 1.0 a step at ±120 px, ±4 px and in a 16 ms drag.
  - **Why a user's drag is unaffected:** its size messages come from `PumpEvents`, which runs before the drain and outside every batch.
  - **What it does reach:** resizes made inside posted work, and inside event handlers. Handlers were batched before c32c4b2e too: Aperture's `SetFullscreen` and Reel's `ToggleFullscreen` when called from one.
  - **For owner 6:** `RenderLiveResizeFrame` could set aside and flush open batches as `RunFrame` does. Harnesses that post `SetWindowPos` count twice the frames a user gets.

CANDIDATES:
- id: layout-wrapped-first-open-in-frame
  what: A wrapped view's first open fits its rows to the body's real width before the frame is drawn, by moving the width check from a post-frame callback into the body's `OnLayoutChanged` (7c0a3b79), instead of a `MarkDirty` after the frame.
  evidence:
    - **The cause:**
      - On a first open the body has no box, so `BodyWidth` (CommanderApp.cs:3659) falls back to `_window.LayoutWidth`.
      - `RebuildViewerHeights` (CommanderApp.Viewer.cs:366) then measures every line at that width.
      - `SettleWrappedRowWidths` (CommanderApp.cs:3588) finds the real width after the frame and rebuilds. `_viewerSettledWidth` reads 1206 after the first open (window 1240).
      - Junction is the same: `SettleDiffScrollbar` (JunctionApp.Diff.cs:741), `_diffSettledWidth` 808.
    - **Vesper, headless, TODO.md (1,023 lines):** first wrapped open 14 builds, 2,272.5 KB UI, 3–4 frames. A first unwrapped open is 11 builds, 1,187.0 KB, 3 frames. Later opens are 11 builds, 658–751 KB.
    - **Real window:** 14 vs 11 builds, 2,783 vs 1,017–1,090 KB.
    - **Junction Commit view, first open:** 7 builds and 2,213 KB wrapped against 3 builds and 1,674 KB unwrapped; real window 2,424.5 KB.
  real app:
    - **Vesper:** the first Shift+F3/F3 (and compare view) of a session with "wrap" on, a saved setting.
    - **Junction:** the first Commit or Changes view open with "Wrap long lines" on.
    - **How often:** once a session a view. The wasted measure pass is O(document), so it grows with the file (the 602k-character archive pays a whole extra `RebuildViewerHeights`).
  expected:
    - **Churn bar on that open:** Vesper's 1,085 KB and 3 builds of excess (−48 %), Junction's 539 KB and 4 builds (−24 %).
    - **Frames:** the frame drawn with wrong heights goes (Vesper drew 4 frames, not 3, in 1 of 3 runs).
    - **What is sure:** the hook alone removes the rebuild. The wasted measure at the window's width also goes only if the first measure waits for the box (fit in place, as `FitWrappedRowsInPlace` does on a resize). Not prototyped.
  cost: small–medium, app code (Vesper's `SettleWrappedRowWidths`, Junction's `SettleDiffScrollbar`). No API change. Pixels: only the wrong first frame goes.

REAL WINDOW:
- **Setup:**
  - Monitors: the 4K at 3840×2160, 150 %, 120 Hz (primary), where both windows opened at window DPI 144; the 1440p at 2560×1440, 100 %, 120 Hz, at (−2560, 314).
  - Window sizes: Junction 2160×1350 client (1440×900 logical), Vesper 1860×1230 (1240×820). GL.
  - 3 launches in 2 holds: r1 (Junction) waited 0.0 s, held 189.0 s; r2 (Vesper, Junction) waited 82.4 s, held 199.0 s.
  - 0 foreground takes, 0 activations refused, 0 `nvlddmkm` events.
  - Idle gate: r1 failed (another session's testhost), r2 passed (CPU 2 %). Counts and bytes only.
- **Grow steps from another thread draw twice; shrinks draw once.** `SetWindowPos` from the driver thread: grows 2.0 frames a step at 4 px and at 120 px, shrinks 1.0, in Junction and in Vesper's reader. This is round 15's 160 px lead: only its jump-back steps grew.
- **A no-draw pass per resize step:** 1.92 frames a 4 px step with 1 drawn (the extra pass draws nothing; likely the accessibility bounds rest). Headless: 1.0.
- **View switches:** 1.29–1.57 drawn a switch (one of 7 drew 3) against 1.0–1.14 headless; UI 322–334 vs 296–313 KB.
- **d43b3518:** the same measure counts as headless.

WAITING, RE-RANKED:
- **layout-wide-step-two-frames:** close it. It is the harness's cross-thread grow step (2.0 a step at any size), not the step's size and not c32c4b2e's cause. On the window's own thread, outside a batch, every step draws 1.0 in both apps: ±120 px, ±4 px and the 16 ms drag.
- **after-layout-callback (question 51):** closed by 7c0a3b79. `OnLayoutChanged` is that hook. The apps' after-frame width checks can use it (the candidate above).
- **dropdown-follow-control-on-resize:** partly settled by 2603955a.
  - All four apps use the custom frame, and a press on its edge now closes light-dismiss popups.
  - But `FramePressedPending` is read only at the next pass, after the size loop ends, so the list still floats at its opening point during an edge drag.
  - Keyboard snaps and app-made resizes keep it open.
  - Correctness, low.
- **controls-menu-max-height-scroll:** e28d72f6 adds flyouts at any depth that flip to the parent's left (`FlipRightEdge`), but still no maximum height or scrolling. Not perf.
- **layout-virtuallist-spacer-stale:** VirtualListElement was touched only by the screen-reader fix. Still invisible; low.
- **vesper-brief-filter-clear-reveal:** CommanderApp.Views.cs is untouched since round 14. 7c0a3b79 now gives the sideways reveal the after-layout form it lacked: `OnLayoutChanged` on Brief's grid. Small, not perf.

EXAMINED, NOT WORTH IT:
- **d43b3518's MeasureCache float:** +8 B an element (the app team's note): Junction 368 elements ≈ 2.9 KB, Vesper 471 ≈ 3.8 KB. Below the retained bar.
- **7c0a3b79's on-state costs:** a thread-static read and a handler scan a box write, plus a `ConditionalWeakTable.Remove` an unmount. 0 B a frame, sub-µs a switch.
- **d43b3518's discarded max-content computes:** 4–18 a step, 0 B.

NEEDS THE USER: none.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\b6ef64d9-4ed1-4793-9955-6a986d74de79\scratchpad\r16-layout\`.
- **Summary:** `r16-summary.txt`.
- **Probe:** `probe-r16.patch`, plus `LayoutProbe.r16.cs`, which goes in as `src\HultUI.Core\DevTools\LayoutProbe.cs`.
- **Host:** `rw\` (`src\Program.cs`, `src\R16.cs` with the X/P/P0/M step modes and the window subclass; `pub.ps1`, `head.ps1`, `batch.ps1`, `hold.ps1`; AOT builds in `aot-Junction\`, `aot-Vesper\`, `aot-Meridian\`).
- **Results:** `out\` (`head-Junction-h1..h3`, `o1..o3`; `head-Vesper-h1..h4`, `o1..o3`; `head-Meridian-m1..m4`; `real-Junction-r1`, `real-Junction-r2`, `real-Vesper-r2`).
- **Logs:** `holds.log`, `launches.log`, `idle-gate.log`.

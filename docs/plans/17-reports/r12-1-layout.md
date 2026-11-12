SATISFIED: yes

I measured at f9c44628 (.NET 11.0.100) and report counts and bytes only. I re-applied round 11's counter probe (`probe-r11.patch`) and added counters for root passes and the real window's resize path: SDL events, live-resize frames and loop resizes. A NativeAOT host per app (Junction, Vesper, Meridian) runs the same script in two modes: headless in a HeadlessSession, or in a real window. Every probe edit was reversed; the worktree is clean at f9c44628 and the build server is shut down.

REVIEW: none.
- **a72ed576 (`ScrollsDown`):**
  - **Meridian map click:** at the tip, with 1c7dc920 landed too:
    - MeasureSize 107 / 107 / 109.9 at 60 / 600 / 2,000 endpoints (round 11 before step 4: 869 / 941 / 943.9).
    - ComputeLayout 20.2 / 20 / 24.
    - KB 13.6 / 13.6 / 22.1.
    - All 24 pixel and geometry hashes after the verify clicks are identical to round 11's.
  - **App walks:** round 11's 21 headless walks give the same counts as round 11's prototype on every row; KB moves by 0.1 at most.
  - **Is the rule safe:** I read it again and found nothing it misses.
    - Scrolling on the main axis forces unbounded main distribution (LayoutEngine.cs:702–706), so wrap lines and children's percentage heights never read the forced height.
    - Layout reserves no scrollbar gutter, so the width cannot depend on the height.
- **9fcf3696 (the layout audit):** off, it costs one static readonly read per outermost `PerformLayout` and nothing per element.
- **1c7dc920 (the map list draws its selection):** a draw-side change only. A uniform width still takes the one `DrawRoundedRect` call. Its layout effect is the click's ComputeLayout 72.2 → 20.2.

CANDIDATES: none.

REAL WINDOW:
- **Setup:**
  - **Monitor:** the 1440p (2560×1440) at 100 %, 200 Hz (SDL reports 200.01), on the RTX 4080 (GL 3.3, driver 610.88), with the default frame (16×39 px).
  - **Launches:** 9 real-window launches in 3 holds of `Global\HultUI-r12-desktop`:

    | Hold | Waited | Held |
    |---|---|---|
    | r1 | 90.8 s | 301.3 s (1.3 s over the 5-minute cap) |
    | r2 | 246.6 s | 162.8 s |
    | r3 | 94.7 s | 191.2 s |

    A fourth batch (the paint-skip prototype below) was stopped while it waited for the lock; it never opened a window. 9 headless AOT launches and 6 headless JIT launches ran outside the lock.
  - **Guards:** 0 foreground takes. The in-process guard never fired and the CBT hook refused 0 activations. The driver set `WS_EX_NOACTIVATE` again on 7–8 windows per launch. 0 `nvlddmkm` events.
- **Resize steps made on the window's own thread** (as a drag's own size loop makes them): layout work per step equals headless exactly for every step type, in all three apps. Root passes, ComputeLayout, MeasureSize and MeasureLeaf (both with hits) are the same. For a 4 px step:

  | App | ComputeLayout | MeasureSize | MeasureLeaf | Grid layouts |
  |---|---|---|---|---|
  | Junction History | 240.13 | 335.3 | 422.65 | – |
  | Vesper | 396 | 1,354 | 1,970 | 62 |
  | Meridian map | 127 | 382 | 520 | – |

  - **Frames:** 1.03 per step against 1 headless; 1.1–1.13 a step in an 8 ms-paced fast series.
  - **Fonts and DPI:** real system fonts change no count. Only the 100 % monitor is attached, so I ran headless at scale 1.5: every count in every phase of all three apps equals scale 1.0.
- **Resize steps from another thread don't match a real drag.** This is the `SetWindowPos` the brief names, made from a driver thread.
  - **Growing:** each step gets a WM_PAINT for the area it exposed after the live frame's `ValidateRect` (counted: 1 EXPOSED with data1 0 a step). That gives 3 frames a step: 2 resize frames, which double RenderVisits (Vesper 439 → 878, Junction 285 → 578, Meridian 200 → 400), plus one extra clean root pass and one pass with nothing to draw.
  - **Shrinking:** 1 frame a step.
  - **Known trap:** this is round 5's harness trap (r5-3-gl-vulkan.md). With the same steps made on the UI thread it disappears (1.03 frames a step), so a real drag does not pay it.
  - **For the guidelines and future briefs:** make resize steps on the window's own thread (`InvokeOnMainThread` → `SetWindowPos`).
- **Accessibility bytes per resize step** (UIA bridge live in a real window; expert 7's area):
  - 1.65–3.0 KB a step over the first 40 steps of a run, then 0.07–0.14 KB; headless 0.
  - Junction's compact-breakpoint crossing adds 69.7 KB narrowing and 75.7 KB widening.
- **Junction compact-breakpoint crossing** (1340 ↔ 1440 px): 2 frames against 1 headless.
  - RenderVisits 552 vs 276 narrowing, 642 vs 321 widening.
  - Cause: the hover re-hits the remade row under the cursor, which was resting over the window (HitTests 1, HitVisits 22, one live redraw for a dirty window).
  - Layout counts are equal: 18 root passes; ComputeLayout 465 narrowing, 508 widening.
- **Mouse moves posted to the HWND** are taken without focus: 1 hit test a move.

  | App | HitVisits real | HitVisits headless |
  |---|---|---|
  | Junction | 30.95 | 29.5 (3 of 40 moves drew 2–3 frames) |
  | Vesper | 39.48 | 40.5 |
  | Meridian | 38.48 | 39.5 |

  - **1 px moves on the same row:** 30 / 41 / 41 visits, equal to headless.
  - **The user's cursor sat over the probe window** at all 81 posted moves of each run, so no WM_MOUSELEAVE followed them. Headless, a move followed by a leave draws 2 frames a move (RenderVisits 642 / 878 / 400).
- **Fast series from another thread** (40 steps 8 ms apart, shrinking): 45 frames for 40 steps; root passes and every layout count equal headless.

WAITING, RE-RANKED:
- **vesper-brief-filter-clear-reveal (filter-clear half):** still open. `ScrollBriefToCursor` is now at CommanderApp.Views.cs:891; b3906200 only moved it and the logic is unchanged. Not perf; no bar.
- **controls-menu-max-height-scroll:** `ContextMenuElement` had 0 commits in 5731ee58..b0dd1954. Not perf.
- **layout-virtuallist-spacer-stale:** round 11 changed `VirtualListElement` only in `DrawHighlight` (1c7dc920) and the off-screen labels (4cced183); the spacer code is unchanged. Invisible and 0 B. Not perf.
- **dropdown-follow-control-on-resize:** no dropdown or picker was open during the real-window resizes, so it was not exercised. Not perf.

EXAMINED, NOT WORTH IT:
- **Skipping the second draw of a growing step** (a WM_PAINT at a size already presented): I built it behind `PROBE_SKIPPAINT` in SDL3Window's event watcher but did not run it. The doubled frame occurs only when the step comes from another thread, so a fix has no real-app case.
- **Junction History's compact breakpoint:**
  - **Cost per crossing:** 17 rows remade and 18 root passes; 300.6 KB narrowing and 343.5 KB widening headless, 370–421 KB real.
  - **Why not:** it happens only when the content width crosses 1140 (a 1406 px window with the sidebar), for example on maximize or restore. The rows really change at that point (the ref chips hide).
- **Vesper resized between two widths** (the maximize/restore pattern): 716 / 685 MeasureSize with about 137 hits a step, against 1,354 with 128 at new widths. Grid rows hold one exact width (examined in round 5), and the case is rare.
- **Folding the hover settle into the resize frame:** it would save 1 frame per breakpoint crossing, and only with the pointer over the list. Rare.

NEEDS THE USER: none.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\0bab04ae-e7a9-44cb-b4da-96d849a107f6\scratchpad\r12-layout\`.
- **Probe code:**
  - `probe-r12-full.patch`: every edit — round 11's probe, the root-pass counter, the resize-path counters and the `PROBE_SKIPPAINT` prototype.
  - `LayoutProbe.r12.cs`: goes in `src/HultUI.Core/DevTools/`.
- **Real-window host:** in `rw\`.
  - Source: `src\Program.cs`, `Rw.props`, and `Junction\`, `Vesper\`, `Meridian\`.
  - Scripts: `pub.ps1` (AOT publish) and `batch.ps1` (guarded launches under the lock).
  - Builds: `aot-<App>\` and `jit-<App>\`.
- **Fixtures and drivers:** `fx\` (Meridian specs), `work-<App>\`, `mer\` and `appdrv\` (with `-bin`).
- **Scripts:** `cmprw.py`.
- **Logs:** `holds.log`, `launches.log`.
- **Outputs** (in `out\`):
  - `real-<App>-r1/r2/r3same.txt`, `headless-<App>-h1/h2.txt`, `jithead-<App>-s1/s1.5.txt`
  - `mer-r12.txt`, `drv-r12.txt`, `*.framelog`

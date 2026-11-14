SATISFIED: no

I measured at e6f7b5aa (.NET 11.0.100) on NativeAOT probe hosts, headless and in real windows, and report counts and bytes only. The probe is reversed with `git apply -R`, the worktree is clean, and the build servers are shut down.

REVIEW: none.
- **0da9f80e: no hover look misses its repaint.**
  - Rechecked at e6f7b5aa: only `VirtualListElement` reads `IsHovered` while painting (counted by `DrawsChildHover`).
  - TableView headers, Expander headers and buttons use `AddHoverStyle`/InteractionStyles, which mark their own frames. VideoPlayer's buttons mark paint in their own enter and leave handlers.
  - A menu entry's reader selection (`ResolveIsSelected` → `IsHovered`) goes into `_a11yOwnSelection`, which `NeedsAccessibilityFrame` counts, so it still gets its frame.
  - In real windows a wheel notch with a resting pointer drew 1 frame in every app.
  - Older and not caused by 0da9f80e: hover is not hit-tested again after a scroll (see NEEDS THE USER).
- **154a7ca3 (`LeadWrappedBodyWidth`) holds on the 4K at 150 %.**
  - 1 drawn frame per step at 4 logical px, 1 physical px and 3 physical px steps, where the logical widths are fractional (1205.333 and so on).
  - 0 rows of 300 with a stale height after any of the 60 steps, and 0 after a DPI round trip.
  - Left over: a height-only resize with wrap on still builds the whole shell (64 PatchFroms, 62–65 KB a step against ~8 KB with wrap off). This is folded into candidate 1.
- **8f9f2683 / d413e001: frames per Junction event.**
  - A save over History: 0 builds and 0 frames, headless and real. Good.
  - A save over Changes: 2.25 builds and 2.25 frames. The first frame draws the pixels already on screen (candidate 2). This is not a regression: the Changes path is unchanged by these commits.
- **d73b1439:** the walk-only frame comes only when a click handler is added outside a build, and no app does that. `CopyAppHandlersFrom` gains two bool ORs per handler (O(handlers), 0 B). No walk gained a frame.

CANDIDATES:
- id: layout-junction-wrap-resize-lead
  what: Give Junction's wrapped diff the width lead Vesper got in 154a7ca3, and on a resize build only what needs it.
    - A window resize hands the build the body's laid-out width plus the window's change; a drag of the file-list splitter hands it the width minus the splitter's change.
    - A resize that crosses no breakpoint and changes no scale rebuilds only the view (`RerenderView`), not the chrome.
    - A resize that leaves the width alone builds nothing.
  evidence: Commit view, wrap on, a 200-line prose file. Prototype switches in the same binary, alternated; numbers per step.
    - **Code today:** JunctionApp.cs:498 calls `Rerender()` (chrome and view) on every step while a wrapped diff is shown. `LeadDiffWidth` (JunctionApp.Diff.cs:756) returns when wrapped. `SettleDiffScrollbar` (:675) corrects the width after the frame, which is the second build. The splitter path is JunctionApp.Changes.cs:515 → `MarkViewDirty`.
    - **Headless, 4 px steps:** 2 → 1 drawn frames. PatchFroms 291.7 → 145.7 with the lead, 51.2 with the lead plus view-only builds. Bytes 542 → 378 → 279 KB (−30 % / −49 %). Render visits 744 → 372.
    - **Real window, 4K at 150 %, 120 Hz:**

      | Step | Drawn frames | PatchFroms | KB |
      |---|---|---|---|
      | 4 px logical | 2 (+1 a11y-only) → 1 | 292 → 146 | 546 → 379 |
      | 1 px physical | 2 → 1 | 334 → 167 | 576 → 399 |
      | 3 px physical | 2 → 1 | 323 → 161 | 567 → 392 |

      The measured width equals the laid-out width after every step.
    - **Splitter drag, 4 px steps (real and headless the same):** 2 → 1 drawn frames, PatchFroms 216 → 49, 446–451 → 266 KB (−41 %). Today's second frame repeats the first frame's pixels in 16 of 16 steps.
    - **Height-only steps:** 167 PatchFroms and 180 KB → 0 and 3.2 KB.
    - **Pixels:** 18 of 18 led steps (12 width steps including 120 px jumps, 6 height steps) equal a full render.
  real app: Junction's Commit and Changes views with "Wrap long lines" on (a saved setting). Every resize, maximize or snap step, and every drag step of the file-list splitter, which users do more often than resizing.
  expected: time bar (frames per step 2 → 1) and churn bar (−30 to −49 % a step; −98 % on a height-only step).
  cost: small, app code only, the same pattern as 154a7ca3. No API. Settled pixels unchanged, and the first frame of a step shows correct heights instead of stale ones.
- id: junction-changes-save-same-frame (layout + reactivity; owner 6 if the fix goes in `Reload`, 1 if in `PatchFrom`)
  what: A save in the working tree while Changes is on screen draws 2 frames, and the first one draws the pixels already on screen.
  evidence:
    - **Headless:** 4 of 4 saves hash as "same, new".
    - **Frame trace, identical in the real window:**
      - At 315 ms the watcher's view build patches 30 kept elements (13 Box, 6 Text, 6 Image, 2 VirtualList, 2 Button, 1 splitter) while the Changes load is still running.
      - `Element.PatchFrom` marks paint and style on every patch, changed or not (Element.cs:1796, :1802). So frame 1 runs style 31, layout 0, render 552 visits.
      - At 561 ms the load lands: layout 443, measure 1,392, text 185, 37 rows. This is frame 2.
    - **Per save:** 2.25 builds and 2.25 frames; 958 KB on all threads (real), 923 KB (headless).
  real app: Junction beside an editor with Changes open, its main view while coding: every save.
  expected: time bar: 1 of the 2 frames per save, a full-window render of 552 visits.
  cost: two options, no API either way.
    - (a) small to medium, app code: `Reload` starts the Changes view's own loads without a build and lets the landing build, as it already does for `SummaryFor`/`WorktreesFor`.
    - (b) large, framework: `PatchFrom` marks paint and style only when something it copied changed. That touches 35 overrides and risks stale pixels.
- id: layout-hover-removed-row-no-frame
  what: Stop counting a removed row's leave as a drawn hover change. Today `PointerState.LeaveRemoved` → `HoverChanged` asks for a frame for a tint whose list is gone or redraws anyway.
  evidence:
    - Meridian with the pointer resting on the map list, switching tabs with Digit1/Digit3. Each switch to Requests draws 2 frames, and the second is pixel-identical (4 of 4 headless).
    - Prototype `PROBE_NOREMOVEDCOUNT`: 2 → 1 frame on those switches (4 of 4). Render visits 263 → 231.5 averaged over all switches. Real window 2 → 1.5 frames a switch. 0 of 8 stale against a full render.
    - Junction view switches: 0 same-pixel frames either way, because its loads land in the second frame anyway.
  real app: Meridian, switching tabs from the keyboard away from the map with the mouse parked on the list. One small frame each time (63 render visits).
  expected: time bar on that action (one frame of two). Small.
  cost: small, one line (PointerState.cs:100). No API, no pixel change.

REAL WINDOW:
- **Setup:**
  - Monitors: the 4K at 3840×2160, 150 %, 120 Hz (primary; every window opened there) and the 1440p at 2560×1440, 100 %, 120 Hz, at (−2560, 314).
  - Windows: Junction and Meridian 2160×1350 client (1440×900 logical), Vesper 1860×1230.
  - 3 launches in 2 holds: r1 waited 439.2 s, held 255.8 s; r2 waited 0.0 s, held 198.1 s.
  - 0 foreground takes, 0 activations refused, 0 `nvlddmkm` events.
  - Input in process (wheel, keys, moves, a press and drag on a splitter). Resizes and DPI moves by `SetWindowPos` on the window's own thread. The system's UIA listener was present.
- **Wheel:** 1 drawn frame per notch in every app, as headless, plus 0.5 accessibility-only frames per notch (the bounds rest; 0 headless).
  - A touchpad-like wheel (8 events of 1/8 notch, 4 ms apart) draws 4.6–4.7 frames per notch at 120 Hz, against 8 headless (one per event).
  - Each event's work is small: a hit test of 27–38 visits. 8–14 KB per notch either way.
- **Junction's commit walk, first visit of a file:** 2.75 frames per key (1.92 drawn, 0.83 accessibility-only), against 1 headless at a fast pace.
  - A headless walk at a reader's pace reproduces it: the key's frame, then the syntax colours landing 95–100 ms later.
  - That landing remakes the 34–37 rows on screen, because the diff's rows are unkeyed (style 349, layout 342, text 137, 312 KB).
  - All threads: 7.0 MB per first-visit key against 454 KB per revisit. Tokenizing a 300-line C# file allocates 6.4–10.5 MB off the UI thread. This is a lead for experts 5 and 8.
- **DPI change** (to the 100 % monitor and back, `SWP_NOSIZE`): Junction with the wrapped diff 4 drawn frames, ~780 PatchFroms and 1.2 MB per move; Vesper with the wrapped viewer 3 drawn, 192, 593–701 KB. Heights correct afterwards.
- **Hover after a scroll:** after 4 of 4 wheel series in Junction (headless and real) and 3 of 4 in Vesper (real), hover sits on a row that is no longer under the pointer.

WAITING, RE-RANKED:
- **junction-commit-first-visit-frames (with expert 8):** close it from the layout side. The second frame is the colours landing, which is by design; the accessibility-only frame is the bounds rest (expert 7). Keying the rows was prototyped and is not worth it (see below). The real cost is the 6.4–10.5 MB tokenize, which is expert 5's.
- **after-layout-callback:** still not needed. Junction's wrapped diff is another reader of a laid-out width, but a lead fixes it without a hook (candidate 1). Round 13 added no `RequestPostFrameCallback` calls.
- **dropdown-follow-control-on-resize, controls-menu-max-height-scroll, vesper-brief-filter-clear-reveal:** their code was not touched in round 13 (Dropdown, DatePicker, ContextMenu and CommanderApp.Views.cs last changed before 8f01b6fd). Still not perf.
- **layout-virtuallist-spacer-stale:** round 13 changed VirtualListElement only for labels (505123d7). Still invisible; keep it low.

EXAMINED, NOT WORTH IT:
- **Keying Junction's diff rows so the colours landing patches them** (prototype `ProbeJKey`): text measures 137 → 4 and layouts 342 → 77, but patches go 0 → 299 and bytes 312 → 320 KB (+2.5 %). It is a time-only bet on monospace measures that are already arithmetic. 0 stale.
- **Wheel scrolling:** 1 frame and 8–14 KB per notch, mostly render. A touchpad's events are already coalesced to the display rate.
- **DPI change:** 3–4 frames and 0.6–1.2 MB, once per move between monitors. Rare.
- **Same-pixel audit of everyday actions** (headless, a frame hash after every drawn frame):
  - Clean: Vesper's keys, navigation and Tab; Junction's History, Branches and Tags keys and its view switches.
  - Same-pixel frames appear only in candidates 1, 2 and 3, plus 1 in 20 of today's wrapped resize steps.

NEEDS THE USER:
- **Should hover follow the pointer when content scrolls under it?** As in browsers, the list tint and hover styles would move to the row now under a resting pointer.
  - Today it stays on the row that scrolled away until the pointer moves (Junction 4 of 4, Vesper 3 of 4).
  - If built: one hit test per scroll frame (27–38 visits), done before the frame draws, so no extra frame.
  - It changes where the tint is drawn during a scroll. Before and after images can be made from headless frames after a wheel series.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\41943a9b-f657-4483-971a-a2b346b967f7\scratchpad\r14-layout\`.
- **Probe:** `probe-r14-full.patch`. It is round 13's probe adapted by `mkpatch.py`, plus `PROBE_JLEAD`/`ProbeJView`/`ProbeJKey` in Junction, `PROBE_NOREMOVEDCOUNT`, and the `LayoutProbe.OnFrame` hook. `LayoutProbe.r14.cs` goes in `src/HultUI.Core/DevTools/`.
- **Host:** `rw\` (`src\Program.cs` with phases r14 and r14act, `RW_SAMECHECK`, `RW_ACTIONS`; `pub.ps1`, `head.ps1`, `batch.ps1`; builds in `aot-<App>\`).
- **Summary:** `r14-summary.txt`.
- **Logs:** `holds.log`, `launches.log`, `batch-r1.log`, `batch-r2.log`, and `out\` (`real-*-r1/r2.txt`, `head-*-h*/s*.txt`).
- **Fixtures:** `fx\`, `work-<App>\`.

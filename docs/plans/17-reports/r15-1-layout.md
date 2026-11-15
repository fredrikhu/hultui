SATISFIED: no

All numbers are counts and bytes, measured at 5a212f3e (.NET 11.0.100) on NativeAOT probe hosts, headless and in real windows. The probe was reversed with `git apply -R`, the worktree is clean, and the build servers are shut down.

REVIEW: none.
- **b62e22cf (hover follows a scroll):** cost is small and no extra frame is drawn.
  - The follow's own hit test per wheel notch, in visits: Junction History 26.1, Commit file list 37.1, diff 34.7; Vesper Detail 37.7, wrapped reader 25–30. That doubles the wheel's own hit test. It allocates 0 B.
  - The Commit file list is a virtual list, so 432 files still cost only 37 visits.
  - Every series drew 1 frame per notch, and after every series the hover sat on the row under the pointer. The real window showed the same.
  - The longest plain (non-virtual) scroll list in the apps is Junction's reflog, capped at 200 rows (`GetReflog(maxCount = 200)`). So the follow costs at most about 200 visits per scroll frame anywhere.
  - `ContentMoves` counts across all windows. A scroll in one window costs every other window one hit test on its next drawn frame. Harmless.
- **27ee5bda / 835adab9 / b3355310 (Junction wrapped diff):** they hold.
  - A 4 px width step is 1 frame and 286–292 KB on the UI thread headless; step 4 recorded 279 KB.
  - Each width step still remakes every visible row (`InvalidateRows`, JunctionApp.Diff.cs:548–549). See candidate 2.
- **a3ebdfca, 6a4c778d, ae8429d3, 1b019826:** no layout objection. The code read clean and none of the frames I measured was affected. I did not re-measure them; their counts belong to experts 6 and 1 in round 14.

CANDIDATES:
- id: layout-vesper-wrap-resize-no-build (closes Waiting apps-vesper-wrapped-drag-bytes)
  what: With wrap on in Vesper's reader (and its compare view, which has the same code), a resize builds nothing:
    - A width step keeps the visible rows and hands the list new heights from the resize handler.
    - A height-only step falls through to the ordinary resize path.
  evidence:
    - **Today's code:**
      - CommanderApp.Viewer.cs:538–545 sets `remake = true` whenever the row shape (wrap, pan, width) moves. `RemakeForFind` then calls `InvalidateRows`, so all ~37 visible rows are made again.
      - When wrapped, the rows do not read the width. `BuildViewerRow`/`ViewerTextCell` read only the gutter, the wrap flag and the colour spans; the pan is used only unwrapped.
      - VirtualList's `KeepUnchangedRows` (VirtualListElement.cs:1631–1641) already re-lays kept rows out when heights move.
      - CommanderApp.cs:3526–3531 calls `MarkDirty` (a whole-window build, 64 PatchFroms) on every wrapped resize step, height-only steps included.
      - The compare view has the same pattern (CommanderApp.Diff.cs:743–750).
    - **Prototype:** switched at run time, alternated ×2. "Keep" skips the remake on a width-only change. "No build" also runs `RebuildViewerHeights` + `InvalidateHeights` + `SettleWrappedRowWidths` from `OnWindowResized`.
    - **Headless, AOT, 1240×820 at scale 1, UI KB per step:**

      | Step | Today | Keep | No build |
      |---|---|---|---|
      | TODO.md (1,023 lines), width 4 px | 275–289 | 73.7 | 12.7 (−95 %) |
      | aaa-long.txt (300 long lines), width 4 px | 310–314 | 157 | 95.6 (−69 %) |
      | Height-only | 63.7–65.6 | – | 2.55–2.69 (−96 %) |

      - On TODO.md, rows made per step 37 → 0, style resolves 252 → 0, leaf measures 597 → 264, text measures 84 → 10, PatchFroms 64 → 0.
      - What remains on aaa-long.txt is real work: `RebuildViewerHeights`' 300 `MeasureWrapped` calls, and the kept rows wrapping again (38 text measures).
    - **Correctness:**
      - 0 rows with stale heights in 328 headless and 40 real steps.
      - Every headless frame equals a full repaint of its tree.
      - Kept rows equal rows made fresh at the same width: 168 of 168 comparisons, jumps of 160 px included.
      - 1 drawn frame per step.
  real app: Vesper's reader (Shift+F3) or compare view with wrap on. Wrap is a saved setting shared with the diff. It applies to every resize, maximize or snap step.
    - Real window, all-thread KB per step:

      | Step | Today | Keep | No build |
      |---|---|---|---|
      | TODO.md, width 4 px | 287–289 | 78 | 18.5–20.9 |
      | TODO.md, width 1 px physical | 275–278 | 78 | 18.9–19.5 |
      | aaa-long.txt, width 4 px | 302–307 | 154–160 | 93.6–98.6 |
      | Height-only | 62.7–63.4 | – | 2.7–4.0 |

    - The accessibility bridge costs 0.07 KB per step in every mode. Expert 8's 248–269 KB per step is fully explained by the row remakes plus the window build.
  expected: churn bar, −69 to −95 % per width step and −94 to −96 % per height step. Likely the time bar too (times come from the build phase).
  cost: small to medium, app code only. No API and no pixel change.
    - Keeping the rows is one condition.
    - The no-build path mirrors `FitGridsInPlace`. The after-frame settle still corrects a wrong lead.
    - The compare view needs the same change.
    - With a Brief or Thumbnails grid behind the full-shell view, keep today's `MarkDirty`.
- id: layout-junction-wrap-keep-rows
  what: Junction's wrapped diff keeps its visible rows on a width-only change, instead of calling `InvalidateRows` at JunctionApp.Diff.cs:548–549.
  evidence: Commit view, docs/aaa-long.md wrapped, 16 steps of 4 px, alternated ×2.
    - **Headless, AOT, 1440×900:** 286–292 → 156.6 KB per step on the UI thread (−45 %). Rows made 18 → 0, style resolves 192 → 54, leaf measures 687 → 394, text measures 152 → 99. PatchFroms 52.5 either way. 1 frame per step.
    - **Pixels:** kept rows equal rows made fresh at the same width in 52 of 52 comparisons, 160 px jumps included. The measured width equals the laid-out width after every step.
    - **Caveat:** since 835adab9, a row's flex basis is the code width it was made at. A kept row keeps its old basis and is first measured at it. During a 16-step drag that measure is a cache hit, but a long drag could push it out of the 4 MB layout cache. Count over a 400 px drag before building, or keep rows only while the basis is within a few pixels of the new width.
  real app: Junction's Commit and Changes views with wrap on: every resize step, and every step of the file-list splitter drag (it goes through the same line).
    - Real window, all-thread KB per step: 280.7–289.0 → 154.7–154.8.
  expected: churn bar, −45 % per step.
  cost: small, one condition. No API and no pixel change.

REAL WINDOW:
- **Setup:**
  - Monitors: the 4K at 3840×2160, 150 %, 120 Hz (primary), where every window opened, and the 1440p at 2560×1440, 100 %, 120 Hz, at (−2560, 314). Window DPI 144.
  - Window sizes: Vesper 1860×1230 client (1240×820 logical), Junction 2160×1350 (1440×900 logical).
  - 2 launches in 2 holds: r1 (Vesper) waited 0.0 s, held 226.7 s; r2 (Junction) waited 541.7 s, held 67.9 s.
  - 0 foreground takes, 0 `nvlddmkm` events.
  - The idle gate failed before both holds (CPU 59 % and 10 %), so only counts and bytes are reported.
- **The user's pointer:** the real cursor entered Vesper's window twice. Two series picked up 2,940 and 644 hit-test visits and steps of 3–8 frames from it. Every other series drew 1 frame per step.
- **Large width jumps (real only):** a 160 px step drew 2 frames in both apps, today's code included (Junction 1x7, 2x3 per 10 steps). Headless drew 1. Steps of 4 px and 1 px physical drew 1. This is a lead, not chased.
- **Wheel:** 1–2 frames per notch, of which 1 is drawn (as in round 14); the second frame is accessibility-only (the bounds rest). The follow's visits matched headless exactly.

WAITING, RE-RANKED:
- **apps-vesper-wrapped-drag-bytes:** close it. It becomes candidate 1. The bytes are row remakes plus a whole-window build per step; none of it is the bridge.
- **junction-review-cards-resize-stale:** still stands; correctness, small.
  - `DiffWrapNeedsRemeasure` (JunctionApp.cs:540–541) covers wrap only. `LeadWrappedDiffSplit` (JunctionApp.Diff.cs:817) also returns early unless lines wrap.
  - So with review cards on and wrap off, a window resize never re-measures the cards. A splitter drag builds at the old width, and the after-frame check corrects it with a second build.
  - Fix: treat `ReviewRowsActive` (Commit view only) like wrap in both places. It costs one view build per resize step, only while reviewing.
  - Not measured live: it needs a GitLab review open.
- **dropdown-follow-control-on-resize, controls-menu-max-height-scroll, vesper-brief-filter-clear-reveal:** no change in round 14 (Dropdown*, DatePicker*, ContextMenu* and CommanderApp.Views.cs untouched in 06d4db59..5a212f3e). Still not perf.
- **layout-virtuallist-spacer-stale:** b62e22cf touched VirtualListElement only to route offsets through `SetOffsets`. Still invisible; keep it low.
- **after-layout-callback:** still not needed. Round 14 added no `RequestPostFrameCallback` calls, and candidate 1 fixes another width reader without a hook.

EXAMINED, NOT WORTH IT:
- **The follow's hit test on long lists:** every long list in the apps is a virtual list (26–38 visits per notch). The longest plain one is the reflog, capped at 200 rows, in a rarely opened panel.
- **Wheel frames:** 1 drawn per notch in all 7 series, headless and real.

NEEDS THE USER: none.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\8e8ba33b-db33-43fa-885a-cb6b82b7253f\scratchpad\r15-layout\`.
- **Probe:** `probe-r15-full.patch` (made with `mkpatch15.py`, which writes `probe-r15.patch`; plus the Vesper and Junction switches). `LayoutProbe.r15.cs` goes in as `src\HultUI.Core\DevTools\LayoutProbe.cs`.
- **Host:** `rw\` (`src\Program.cs`, `src\R15.cs`, `pub.ps1`, `head.ps1`, `batch.ps1`; builds in `aot-Vesper\`, `aot-Junction\`).
- **Summary:** `r15-summary.txt`.
- **Logs:** `holds.log`, `launches.log`, `batch-r1.log`, `batch-r2.log`, `idle-gate.log`.
- **Results:** `out\` (`head-Vesper-h1/h2/h3/h3c/h4c.txt`, `head-Junction-h1/h2/h2c.txt`, `real-Vesper-r1.txt`, `real-Junction-r2.txt`).

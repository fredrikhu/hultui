# Round 3 survey: 8. App workloads

All five apps published NativeAOT at fc807ec (round 2's "after" baseline) and 5d9be15 (same src/apps
as 0951288, the released builds). Real windows that never took focus, a11y on unless noted, before
and after alternated, two runs each; bytes and counts identical across repeats. Shares were taken
under eight concurrent surveys sharing the GPU, so flush and present shares are inflated. The round 2
probe needed two hand merges (VirtualListElement after 545d1b6, Application after 65cdae0) and a new
wrap for `TextPipeline.MeasureWidth` (b23b203). Probe (with minimize, clickat and per-Build byte marks),
scripts and CSVs in the session scratchpad `r3apps/`; worktree reverted and clean at 5d9be15.

SATISFIED: no

## Review of round 2

**0912a5e (a11y-virtual-item-selected): objection.** A selection move now forces a full a11y rebuild
on the next frame: `VirtualListElement.cs:78` `NotifyAccessibilityPropertyChanged(IsSelected)` →
`Window.cs:3301` `_a11yPropertyDirty`, which skips the 100 ms coalescer. Junction History walk (40 keys
at 60 ms, UIA client listening): a11y rebuilds 25 (all "layout due") → 41 (40 "property"), 1,110 nodes
each; a11y bytes per key 217 → 294 KB (+36 %); a11y share of the walk's frame time 7.1–7.6 % →
30.0–31.1 %; a11y time 2.8× in alternated runs. Branches walk: 15–16 → 20 rebuilds per 20 keys. The
suite row reported +2–3 % only because it already rebuilt the tree every step.

**Did round 2 help? Yes**, except that History a11y cost. Shares after [before]; bytes before → after:

| App, session | Render (realize, text) | Flush | a11y | Present | Layout | Style | Bytes/frame | UI-thread per session |
|---|---|---|---|---|---|---|---|---|
| Junction, a11y on | 48–49 % (7–8, 9) [53–54 (10–11, 11)] | 26 % [26] | 10.0–11.5 % [5.2–5.6] | 6 % [6–8] | 7 % [6–7] | 0.8 % | 235–238 → 186–187 KB (−21 %) | 104 → 82 MB (−21 %) |
| Junction, a11y off | 55 % (9, 11) [55 (11, 12)] | 29 % [28] | — | 7 % | 7.5 % [8.7] | 1.0 % | 214 → 112 KB (−48 %) | 84 → 62 MB (−27 %) |
| Vesper panes + viewer | 39 % (10, 3) [41–50 (13–19, 3)] | 48–49 % [35–46] | 2.1–2.2 % | 5–6 % | 4 % | 0.6 % | 214–215 → 179–180 KB (−16 %) | 57.5 → 50.9 MB (−12 %) |
| Meridian response + typing | 25–45 % (text 10–22) [68–74 (text 45–47)] | 47–70 % [20–25] | 0.9–1.4 % | 2–3 % | 2–3 % | 0.1–0.2 % | 912–1,176 → 150–152 KB (−84…87 %) | 87 → 18 MB (−79 %) |
| Aperture library + item page | 28–36 % (4–4.5, 1.2) [32–35 (3–3.6, 1.2)] | 29–35 % [32] | 3.2–3.5 % | 22.5–25 % | 2 % | 0.2–0.3 % | 57.5 → 54–57 KB (flat) | 16.7–17.3 → 15.8–16.6 MB |
| Reel playback | 44–46 % [45–47] | 13–14 % | 0.5–0.6 % | 32–34 % | 1.1 % | 0.2 % | 3.1 → 3.2–3.3 KB | 73–96 → 8.5 MB (−88…91 %) |

Per interaction (per key, frames plus input handling), before → after:

- Junction History walk 708 → 551 KB (−22 %); a11y off 483 → 257 KB (−47 %); rows realized per key
  18.4 → 2.6, realize bytes 251 → 41.5 KB. Files walk 872 → 512 KB (−41 %); `TODO.md` open frame
  8.6 → 2.8 MB. History hover sweep 16.2 → 0 KB/frame; wheel 125–128 → 91–94 KB/frame. Commit walk
  676–684 → 635 KB. Branches walk 672–674 → 667 KB (flat).
- Vesper: first code folder 5,071 → 412 KB in one frame (realize 89 → 18 % of it); viewer PageDown
  270 → 212 KB; pane walk 579 → 570 KB (flat).
- Meridian: the 18,884-line response frame 25.1 → 7.5 MB (−70 %); URL keystroke 2,078 → 311 KB (−85 %),
  text share 47.5 → 5.2–5.5 %.
- Aperture poster walk 232 → 228–229 KB (flat).
- Reel: VideoPlayer rebuilds per 12 s 1,796–2,380 → 165–166; loop passes 6,509–8,999 → 1,174–1,194.
  Minimized 6.5 s (1080p24 H.264): loop passes 108,724 → 489; video now served undrawn (437 passes).
  Before, Vulkan never drew again after the restore.

Working set and private bytes at the end, after a full GC: Junction 252–257 → 252–259 MB, Vesper
144–146 → 146 MB, Meridian 150–151 → 146–153 MB (flat); Meridian managed heap 23.4 → 17.5 MB (−25 %);
Aperture private 257 → 250–253 MB (−2 %); Reel working set 300–304 → 289–290 MB (−4 %).

Start-up unchanged: GL driver load (`SDL_CreateWindow`) 51–68 % of time to first frame; Aperture still
builds 3 swapchains; the Vulkan first frame takes 175–220 ms after "application ready", 30–32 % of time
to first frame. Idle: 61 loop passes/s (65 before), 0 frames.

## Candidates

- **a11y-selection-move-patch** (7). Mark IsSelected on the two item nodes of the applied tree and raise
  their events, as `MoveA11yFocus` (`Window.cs:676`) does for focus, instead of forcing a full rebuild.
  Evidence: the 0912a5e objection; with a11y off the History walk frames allocate 1.68 MB, with a11y on
  a11y adds 11.7 MB (87 % of the walk's frame bytes). Churn and time on the History walk: a11y rebuilds
  40 → ≤25 per 40 keys, a11y bytes ≥ −26 % per key, a11y share 30 % → ≤8 %; Branches alike. Small–medium;
  no API change; events unchanged.
- **a11y-item-label-fallback** (7). `VirtualListElement.cs:149` names unlabelled items with
  `item?.ToString()` — for records a compiler-generated dump, built for every item on every a11y rebuild.
  Junction Changes view, typing a commit summary with a 146-line diff open: a11y 798 KB per keystroke
  (321 nodes, 2.5 KB each), 22.7 % of the typing frames. Readers hear `'DiffRow { Type = Unified, Index
  = 1, LineIndex = -1, Review = , Thread = , … }'` for each of 195 items (`JunctionApp.Diff.cs:517`, no
  `AccessibleItemLabel`). Vesper's diff is the same kind of record (`CommanderApp.Diff.cs:738`); viewer
  and thumbnail lists are named "0", "1", … (`CommanderApp.Viewer.cs:542`, `CommanderApp.Views.cs:100`).
  Correctness, plus churn ~−700 KB per keystroke (−35 % of 2.0 MB). App follow-up: `AccessibleItemLabel`
  on those four lists. Small; changes what a reader hears, not pixels.
- **apps-junction-diff-flatten-per-build** (8, app; guideline). Every Junction build re-flattens the
  shown diff: new DiffRow records, `IntralineDiff.Compute` per changed pair, merged spans
  (`JunctionApp.Diff.cs:479`, `:748`); the list then re-templates all visible rows (`:520`, one-argument
  `RenderWith`). Changes view typing, a11y off: Build 740 KB per keystroke, the view 588 KB of it (81 %);
  realize adds 441 KB (39 rows × 11.3 KB); total 1.19 MB per keystroke. A no-op key in the Commit view
  still costs 253 KB Build + 247 KB realize, layout 29 % of the frame (213 text layouts). Runs on every
  rebuild (keystrokes, toast frames, spinner frames). Scaling (O(diff lines) per rebuild goes) and churn
  ~−1.0 MB per keystroke (−85 %): memoize rows by (diff, split, highlight), two-argument `RenderWith`.
  Small, app only.
- **reconcile-toasthost-fade-rebuild** (6; = reactivity-toasthost-fade-in-place). `ToastHost` calls the
  owner's `MarkDirty` every fade step (`src/HultUI.Core/Builders/ToastHost.cs:75`, `:100`); Junction
  (`JunctionApp.cs:423`), Aperture and Meridian rebuild the whole app every drawn fade frame. One Meridian
  toast: 27 whole-app rebuilds at 256 KB (6.9 MB), 25 a11y rebuilds, 7.3 MB on the UI thread for a 2.9 s
  message; Junction's rebuild 261 KB. Rebuilds per toast 27 → ~3, bytes −90 %: set the mounted toast's
  `Opacity` from the tween. Small, internal.
- **media-videoplayer-fade-rebuild** (3). The bar fade writes `_barOpacity` (`VideoPlayer.cs:195`,
  `:217`), which `Build` reads (`:293`), so every drawn fade frame rebuilds the player and every pointer
  move reveals and later fades the bar. Reel 12 s: 165–166 VideoPlayer rebuilds at 38.1 KB = 6.3 MB of the
  session's 8.5 MB UI-thread allocation (74 %); the sweep-and-fade segment alone 93 rebuilds over 109
  frames. Fade frames 41 → ~3 KB; Reel session −60 % or more. Small; no API change.
- **vk-swapchain-target-per-frame** (3; re-ranked up). Every Vulkan frame allocates exactly 2,048 B
  outside style, layout, render and a11y (surface begin and end); GL 0 B. A quiet Reel playback frame is
  2,416 B, 2,048 (85 %) of it this; Aperture's quiet frames 2,048 of 2,048. 2.0 → ≤0.1 KB per frame.
  Medium; touches HDR.md's "never cache a wrapped swapchain target".
- **layout-adopt-item-rows-branches** (1; next for adopt-item-rows). Junction Branches
  (`JunctionApp.Branches.cs:282`) to the two-argument `RenderWith`: 19.9 rows re-realized per key at
  20.4 KB, 435 KB per key, realize 18–30 % of the walk's frame time, both builds; ~−380 KB per key (−56 %
  of 667 KB). Then by impact: (1) Vesper panes (`CommanderApp.Pane.cs:744`): 35–38 rows per key,
  312–345 KB, realize 12–24 % of the walk, the most-used walk; (2) Junction's diff list (above);
  (3) Aperture poster grid: 2.5 rows per key at 49 KB, 125 KB per key, realize 9–10 %. Not Junction Commit
  (its 28 rows per key are a new file's diff rows). Small per list (shape key + tests).
- **runtime-idle-wakeups** (4/3; re-ranked up now round 2's items 1–2 landed). An idle window wakes 61
  times a second (`Application.cs:533` `WaitForEvents(16)`). Junction and Meridian idle 10 s: 611 loop
  passes, 0 frames (Junction 649 before). Idle bar: ~1 wake/s. Medium.
- **text-open-measures-every-line** (5). The first draw of a TextArea measures every hard line's width.
  Junction `TODO.md` open frame (98.5 KB, 1,023 lines): 2.8 MB, 1,223 text calls, text 84 % of the frame,
  27–30 % of the Files walk. Meridian's first response frame: 7.5 MB, 24,194 text calls, text 90 %. Time
  ≥5 % on the open frame if only visible lines are measured and the extent estimated. Medium.
- **apps-tooling-generic-framelog** (8, built by 4; re-ranked up). Commit an environment-gated frame log
  (`HULTUI_BENCH` / `HULTUI_FRAMELOG`) to the framework. This round's probe needed two conflict merges and
  a new hook; source in `r3apps/probe/` (`BenchHook.cs`, `BenchCounters.cs`, `after-full.diff`,
  `marks.py`). Tooling; every app survey depends on it. Small.
- **Suite coverage gaps** (8):
  - apps-scenario-selection-walk-real-loop-a11y: keys every 60 ms over a list plus a detail pane in the
    real loop (virtual clock), bridge on, counting a11y rebuilds per key. Would have caught 0912a5e; the
    one-frame-per-step a11y rows cannot.
  - apps-scenario-type-beside-diff: typing beside a 150-row list of unlabelled records whose rows are
    derived in Build.
  - apps-scenario-toast: one `ToastHost` toast over a 300-element root, counting rebuilds per toast.
  - apps-scenario-video-bar-fade: VideoPlayer with a fake backend, counting rebuilds per fade frame.
  - Vulkan steady-frame bytes: a Windows-only real-window row.
  - Still missing from round 2: rich-row walk (~400 chrome elements plus 14-element rows),
    document-switch, image-grid, loop-wakes.

## Examined, not worth it

- test-vesper-diffview-escape-flake: 0 failures in 25 loops of the 56 DiffView tests (1,400 runs) under
  the eight-survey load; CI on 37861109308 and 38072271269 green first attempt. Keep watching; the race is
  the "Comparing" assert at `DiffViewTests.cs:980` against a 400-line thread-pool diff.
- apps-tokenize-on-ui-thread: only GitLab merge-request markdown code fences
  (`JunctionApp.Markdown.cs:490-496`, 256-entry cache), one-time ~5 MB. Not reachable without GitLab;
  rank last.
- apps-whole-app-tween-rebuilds: the framework parts became candidates (ToastHost, VideoPlayer).
  Junction's spinner loop (`JunctionApp.Spinner.cs:51`, a 261 KB rebuild per drawn frame while busy)
  stays a guideline item; no spinner ran in these sessions (0 animation ticks).
- Aperture leads (poster-as-hero, hero-scrim-seam, tmdb-size-fallback) are not perf. The item page at
  150 %: 23.6 KB/frame, flush and present dominated. Aperture flat overall: a11y 7–8 %, realize 9–10 % in
  the poster walk.
- Meridian leads (pane-choice-not-persisted, min-width-wraps) are not perf.
- Reel minimized: 75 loop passes/s, 67 undrawn services/s, VideoPlayer and a11y rebuilt 8/s from
  position events, ~340 KB/s. Playing, not idle; small.
- Style ≤1.8 % in every walk. Layout ≤11 % except the Commit walk (20–26 %), the no-op rebuild covered by
  the diff candidate.
- Flush rose to 48 % in Vesper and 47–70 % in Meridian: time under a shared GPU; 61edb19 showed the driver
  wait only moves.
- Working set and start-up: no new lever. GL driver load and Vulkan first-frame shader work examined in
  round 1 (no program cache).

## Needs the user

1. **Re-open "split Junction into components" (declined 2026-10-06)?** Round 2 removed most other
   per-key costs, so the whole-app Build is now most of what remains. Junction History key, a11y off:
   Build and reconcile 84 % of the bytes (215 of 257 KB); titlebar, sidebar and toolbar 70 % of the Build
   bytes (132 KB per key) and do not change on a move. Meridian URL keystroke: Build 92 % of the bytes
   (286 of 311 KB), the request tree 46 % of it. Vesper: both panes rebuilt every key (79 % of 219 KB).
   Persistent child components (field-held, rebuilt only on their own signals) would skip these parts.
   Allow these app changes, or keep it a guideline only?
2. **Aperture device registrations:** the 6 Aperture runs signed in again as device "aperture-survey" on
   jellyfin.home. Remove those devices and revoke the API key when the work is done.

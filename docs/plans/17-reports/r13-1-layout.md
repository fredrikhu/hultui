SATISFIED: no

I measured at 2ced4e26 (.NET 11.0.100) and report counts and bytes only. I used the round 12 layout probe plus new counters for why each move draws a frame and for frames that actually draw. The probe was reversed with `git apply -R`; the worktree is clean at 2ced4e26 and the build servers are shut down.

REVIEW: none.
- **c0ce9387 (`DrawsChildHover`, `DrawnHoverChanges`): no hover look misses its repaint.**
  - **Code check:** only `VirtualListElement` draws from a child's `IsHovered` (covered by `DrawsChildHover`). The only app read is Aperture's card check in ApertureApp.Library.cs:439, which is data, not drawing. Every Core enter/leave handler marks its own element: SplitterHandle, Calendar, ContextMenu, Slider, NumericUpDown, Terminal links, and MenuBar (an ordinary handler, so it is counted). InteractionStyles.Recompute marks its owner. A menu entry's reader selection gets its own accessibility frame.
  - **Pixels:** headless, after each of 72 keys under a resting pointer (Junction commit walk with the pointer on the diff and on the files, History walk; Vesper panes), the shown frame equals a fresh full render: 0 stale.
  - **Real window:** in Junction's commit walk the settle ran 2 times a key and drew no frame of its own.
- **3f93d54c (`LeadDiffWidth`):** O(1) per resize. A wrong guess (a breakpoint crossing or a scale change) is corrected by the after-frame check, as before. Real window, steps on the window's own thread: 1 drawn frame per size change.
- **67fc0884 (appends told to a reader):** one `Array.Resize` of the kept labels to the item count per append past the window (VirtualListElement.cs:1659), only while a reader keeps labels. That is 24 B per item (40 B for Vesper's `PaneRowItem`). `GetItemLabel` already made the same resize at the next walk, so the order of work is unchanged. No objection.

CANDIDATES:
- id: layout-hover-move-frames (Waiting: hover-move-frames-nothing-shows)
  what: Give an input's hover clause (Window.cs:2828) the rule the settle already uses (Window.cs:3198): a changed hover chain asks for a frame only when `DrawnHoverChanges` moved or DevTools is open.
  evidence:
    - What still asks for a frame: hover styles, owned controls, scrollbars and tooltips mark themselves; ordinary enter/leave handlers are counted by the handler clause.
    - Prototype `PROBE_NOHOVERCLAUSE`, headless (JIT and AOT give the same counts), sweeps every 8 px down and 12 px across:

      | App | Moves | Frames now → prototype | Change |
      |---|---|---|---|
      | Junction | 761 | 297 → 109 | −63 % |
      | Vesper | 292 | 161 → 32 | −80 % |
      | Meridian | 956 | 303 → 127 | −58 % |

    - 498 of today's 761 frames draw exactly the pixels already on screen.
    - Safety: after each of the 2,009 moves, and in 160 move-then-leave checks, the shown frame equals a full render (0 stale).
    - Real windows, NativeAOT, drawn frames:

      | App | Drawn frames now → prototype | Moves that drew |
      |---|---|---|
      | Junction | 244 → 129 | 202 → 73 |
      | Vesper | 108 → 38 | 94 → 10 |
      | Meridian | 180 → 140 | 166 → 112 (its map list's tint is real) |

  real app:
    - Moving the mouse over Junction's diff (42 → 3 frames a sweep), its History list (48 → 17) and sidebar, over Vesper's panes (44 → 5 moves drawing), and over Meridian's map. Users do this constantly.
    - Each removed frame is a full-window repaint: 320–544 render visits in Junction, 439 in Vesper, 200 in Meridian.
  expected: time bar, by a wide margin: 42–65 % fewer frames while the pointer moves.
  cost: small. One condition plus one snapshot int, tests like `HoverAfterRebuildTests` for moves, and an ELEMENTS.md line. No API change; pixels unchanged (0 stale).
- id: layout-vesper-wrap-resize-lead
  what: Vesper's wrapped viewer and diff measure row heights at the body's last laid-out width, then correct it after the frame (SettleWrappedRowWidths, CommanderApp.cs:3323, triggered by OnWindowResized at :3274). So each size change builds twice and draws twice, and the first frame has stale heights. Leading the width by the window's delta, as Junction's `LeadDiffWidth` does, removes the second build.
  evidence: viewer on 300 long lines, 20 resize steps of 4 px, per step:

    | Run | Drawn frames | Builds (PatchFroms) | RenderVisits | KB |
    |---|---|---|---|---|
    | Headless, wrap on | 2 | 128 | 394 | 373 |
    | Headless, wrap off | 1 | 0 | 277 | 39 |
    | Headless, lead prototype `PROBE_VLEAD` | 1 | 64 | 197 | 310 (−17 %) |
    | Real window, wrap on | 2 (+1 accessibility-only) | – | – | 377 |

  real app: Vesper's viewer (Shift+F3) or diff (F3) with wrap (W) on, while the window is resized, maximized or snapped. Occasional; −50 % frames and builds per step there.
  expected: time bar (half the frames per step) and churn (−17 %).
  cost: small, app code in Vesper (the misuse LAYOUT.md now warns about since 3f93d54c). The transient first frame shows correct heights instead of stale ones. Settled pixels were not compared.

REAL WINDOW:
- **Setup:**
  - The 4K at 150 % (client 2160×1350 for a 1440×900 layout), 120 Hz, with a UIA client listening.
  - 4 launches in 2 holds of `Global\HultUI-r13-desktop`: r1 waited 620.2 s and held 73.4 s; r2 waited 308.4 s and held 207.4 s.
  - 0 foreground takes, CBT hook refused 0, 0 `nvlddmkm` events.
- **The user's cursor rested outside the window at all 761 posted moves (in round 12 it was inside).**
  - So SDL followed every posted `WM_MOUSEMOVE` with a leave: 2 hit tests per move and a frame every move. Junction drew 528 frames; the prototype drew 272.
  - Run r2 therefore sent the moves in-process.
  - For future briefs: posted moves are representative only while the real cursor is inside the window.
- **Junction's commit walk, pointer on the diff:** the first visit of a file draws 3 frames a key in a real window against 1 headless.
  - 2 drawn: the key's build and the async diff load's build. The headless loader lands before the frame.
  - 1 accessibility-only frame.
  - Revisits draw 1 frame. This is for expert 8.
- **Every discrete size change ends with an accessibility-only frame 100 ms later:** this is a6bfb1fc's rest. It draws nothing: 14–16 such frames per 18 steps.
- **Hover transitions draw at 120 Hz:** a sweep across the window drew 22–23 frames for 8–9 moves that drew.
- **Opening Vesper's viewer:** 2 frames per open, as headless, plus 420 KB of accessibility bytes per open in the real window (0 headless). This is for expert 7.

WAITING, RE-RANKED:
- **hover-move-frames-nothing-shows:** now the candidate layout-hover-move-frames above.
- **size-change-restore-second-frame:** close it; it is a harness artifact. With `SetWindowPos` from another thread each restore draws 2 frames (one exposed paint each). From the window's own thread, where the OS makes drags, maximize, restore and snap, it draws 1. Over 18 steps that is 20 against 18 drawn frames; headless 18.
- **after-layout-callback:** yes, another app needs a laid-out value: Vesper's wrapped viewer and diff (above). But a width lead fixes it without a hook (prototype numbers above), so the public hook is still not needed.
  - Other after-frame reads are reveals or focus once rows exist, not width guesses: Aperture (ApertureApp.Library.cs:139, Cursor.cs:299) and Junction (JunctionApp.cs:1081/1087, History.cs:652/674/1215).
- **layout-virtuallist-spacer-stale, dropdown-follow-control-on-resize, controls-menu-max-height-scroll, vesper-brief-filter-clear-reveal:** round 12 changed only c0ce9387 and 67fc0884 in my area, neither touching this code. Still not perf.

EXAMINED, NOT WORTH IT:
- **Bytes per pointer move that draws nothing:** 0.15–0.85 KB on all threads in real windows (0.23–0.59 KB headless), with no frame. Below every bar.
- **The label resize in 67fc0884:** 24–40 B per item, once per append, only with a reader. Below every bar.

NEEDS THE USER: none.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\17c11f5e-9de0-4477-8f58-0d0a5e006605\scratchpad\r13-layout\`.
- **Probe:** `probe-r13-full.patch` (the round 12 probe plus the move-reason counters, `PROBE_NOHOVERCLAUSE` and `PROBE_VLEAD`) and `LayoutProbe.r13.cs` (goes in `src/HultUI.Core/DevTools/`).
- **Summary:** `r13-summary.txt`.
- **Host:** `rw\` (`src\Program.cs`, `Rw.props`, `pub.ps1`, `batch.ps1`, `head.ps1`, builds `aot-<App>\` and `jit-<App>\`).
- **Fixtures:** `fx\`, `work-<App>\`.
- **Logs:** `holds.log`, `launches.log`, `batch-r1.log`, `batch-r2.log`, and `out\` (`head-*`, `headless-*-ah1/ah2`, `real-*-r1/r2`).

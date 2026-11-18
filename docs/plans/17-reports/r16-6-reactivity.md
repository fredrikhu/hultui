SATISFIED: no. One lead (Junction's wrapped diff fitted in place) may clear the churn bar on an occasional action. The live-frame fix is a correctness and harness item and moves no real-app frames today.

REVIEW:
- **c32c4b2e (expert 8's reactivity-resize-batch-two-frames): confirmed in the harness, but not a real-app regression for drags.** I agree with expert 1.
  - **Cause.** The harnesses post `SetWindowPos` with `InvokeOnMainThread`, so the call now runs inside the drain's batch. Windows sends the size inside the call, so `RenderLiveResizeFrame` → `OnWindowResized` → `RenderFrame` also runs inside the batch.
  - Junction's resize handler writes `_revision.Value++` (`RerenderView` / `Rerender`, JunctionApp.cs:512–514), and the batch holds that write. The size message's frame draws with no build. The flush then builds, and the same pass draws a second frame.
  - A probe Resized handler, subscribed after the app's, found the write still pending in 20 of 20 steps.
- **Real window, Junction (hold 1, one launch).** jrepo's 8th commit in the Commit view, wrap on, 20 steps of 6 px each way (expert 8's scene). "Frames a step" counts the drag's no-op first step, as expert 8 did.

  | How each step was made | Frames a step | Frames | Write held |
  |---|---|---|---|
  | Posted (expert 8's harness) | 1.90 | 20 × 7.5 KB with no build, plus 20 × 156 KB build frames | 20/20 |
  | `SetWindowPos` from a Win32 timer callback, which `SDL_PumpEvents` dispatches as a real drag's modal loop does | 0.95 | 20 × 162 KB, build inside the size message | 0/20 |
  | Posted, inside `SuspendBatching` | 0.95 | — | 0/20 |
  | Posted, with the fix below | 0.95 | 161 KB a frame | 0/20 |

  - **160 px steps** (7 each way, crossing the compact-chrome breakpoint, `Rerender` from `OnResize`): posted 14 frames in 7 steps (7 stale 7.7 KB + 7 × 156 KB); timer 7; fix 7.
- **Which user actions pay it.**
  - **None of the user's own resizes.** Edge drags, snaps, Win+arrow and DPI hops all reach the live frame from `PumpEvents`, with no batch open. So does the custom frame's maximize button: `WindowsCustomFrame.Maximize` posts `WM_SYSCOMMAND`. Window placement is set before `Run`.
  - **Fullscreen toggles from a key or click handler** (handlers were batched before c32c4b2e too):
    - Aperture's `SetFullscreen` (ApertureApp.Player.cs:261–268) and Reel's `ToggleFullscreen` (ReelApp.cs:210–215) call `_window.SetFullscreen` first, then write `_videoPlayer.IsFullscreen` and call `Rerender()`.
    - The custom frame calls `SetWindowPos` synchronously (WindowsCustomFrame.cs:238), then `SDL3Window` sets `ResizePending = true` (SDL3Window.cs:1238). The next pass's `ProcessPendingResizes` raises Resized again and marks paint dirty.
    - So a toggle is **2 frames with or without the fix**: frame 1 in the size message, then frame 2 next pass with the writes. Frame 1 is stale in both apps: Aperture builds the view through `MarkViewDirty` with the player's old flag; Reel does no build and draws the old controls at the new size.
    - The fix alone changes neither frame, because both writes come after the call. The fix plus moving the writes before `SetFullscreen` makes frame 1 right; frame 2 is still drawn.
    - I counted this from the code, not in a real window: a toggle covers the user's monitor. A user toggles a few times a session.
  - **App resizes from posted work:** only Reel's single-instance `Listen` (`reel.Open` then `window.Raise()`), and only if `Raise` restores a minimised window inside the call. Not verified.
- **Measurement trap.** Every harness that posts `SetWindowPos` (start-x `resize`/`drag`/`dragrel`, r15-layout `SameThread`) counts double frames since c32c4b2e for any app whose Resized handler writes a signal: Junction's wrapped diff and breakpoints, Meridian's breakpoint, Aperture's scale change. Its "step KB", measured around `SetWindowPos`, also misses the build: 7.5 KB instead of 162 KB.
- **layout-wide-step-two-frames (expert 1):** at 440ba012 in Junction it is this batch: posted 160 px steps draw 2 frames, timer steps 1.
  - Vesper (hold 2) draws 1 frame per 160 px step whichever way the step is made: panels 82 KB a step, wrapped reader 10–19 KB. Vesper's handler writes no signal (`MarkDirty`, `FitWrappedRowsInPlace`), so round 15's Vesper case was a different cause. Expert 1 has since closed it as the cross-thread grow-step artifact, which fits.
- **033a900c: no extra mounts or rebuilds in the apps.** A probe counter in `TreeDiffer.CanPatch` found 0 host-against-host calls (0 refusals, 0 same) in all five app suites: Junction 1,161 tests, Vesper 951, Meridian 680, Aperture 368, Reel 52, all passing.
  - The control, the commit's own ForeignHostPatchTests, recorded 4 refusals and 1 same, so the counter works.
  - Why the apps never reach it: a kept host is moved into the new tree by `AddChild` and put back by `TryRestoreMoved` or the keyed path. It never meets a different host at the same position.
  - The key counter is one string per host, as before. Expert 8's equal builds over 48 walks agree.
- **ae3ac740, ff3c12e6: none.** No app subscribes to `PreviewKeyDown` or `InputActivity`, so the cost is one null check per input. `TargetTakesText` adds one bool per key. Expert 8: Meridian typing 10.0 → 10.1 KB a character, 1 frame; held Shift 0 frames.
- **f08647ed: docs only** (Component.cs comments, DECISIONS.md).
- **fdd795bd, cea60a27, 56da4346 in the real Junction (hold 3):** History 20 Downs 1.00 frame a key, 21.7 KB; Commit file list 1.00, 77.4 KB a key; saves after the first 1 frame, 128 KB on the UI thread, about 2.3 MB on all threads; the first save 4 frames, 515 KB. This matches expert 8 (7 processes a save, 128–130 KB). No objection.
- **dae30194, 59609522, c32c4b2e in the real Junction (hold 3, drain counters):** over start-up with the preload, History, Commit, Changes and 3 saves:
  - 109 passes ran posted work: 110 actions, including the harness's own 41 posted inputs.
  - At most 2 actions in a pass; 0 passes stopped by the budget; 33 batches had writes to flush.
  - Neither cost nor gain in Junction's own work. The win is for streaming producers.

CANDIDATES:
- id: reactivity-live-frame-sets-aside-batch
  what: `RenderLiveResizeFrame` sets aside the open batches and flushes what they held, then restores them, as `RunFrameCore`'s nested pass already does.
  evidence:
    - Real window: posted steps 1.90 → 0.95 frames a step; 160 px steps 2 → 1; the build moves into the size message.
    - Headless probe test, a posted action or a handler's batch that resizes and runs the live frame, Resized writing a signal: without the fix the live frame builds 0 and draws 1, then 1 build and 1 frame follow; with the fix the live frame builds 1 and draws 1, then nothing follows.
    - PostedWorkTests, LiveFrameRequestTests, FrameRobustnessTests, OverlayResizeTests and A11yLiveResizeTests pass with it, 34/34. The posted-burst PerfSuite rows never run a live frame, so step 5's wins are untouched by construction.
  real app: 0 frames today. User resizes run with no batch open; the fullscreen toggles write after the call. It gives a correct first frame for app code that resizes from a handler or posted work, and it makes posted-`SetWindowPos` harnesses truthful.
  expected: no real-app perf bar. It is correctness plus honest measurement. With the apps' fullscreen writes moved before `SetFullscreen` (app side), frame 1 of a toggle shows the right player state; the toggle still draws 2 frames because of `ResizePending`.
  cost: small, about 6 lines, using internal `SetAsideBatches`/`RestoreBatches`. When no batch is open, the cost is one int read per live frame. No API. Pixels: it removes a stale frame. Side effect: a build that throws while the batch flushes now surfaces in the SDL event watcher, which logs and swallows it, as it already does for any live-frame build. Fix the harnesses anyway: steps from a Win32 timer callback (my `dragrelT`) or inside `SuspendBatching` (`dragrelS`).
- id: reactivity-junction-wrap-resize-in-place (lead, with expert 1)
  what: Junction's wrapped diff builds the view on every width step. It could hand the new row heights to the list without a build, as Vesper's `FitWrappedRowsInPlace` does.
  evidence: real window, real-drag context (hold 1, timer steps): 156–164 KB and 1 view build per 6 px or 160 px step. Vesper's wrapped reader (hold 2): 10–16 KB a step with no build.
  real app: resizing or snapping Junction with wrap on, or with a review open. Occasional; a drag is tens of steps.
  expected: churn bar, about −80–90 % a step if the heights can be fitted in place. Not prototyped.
  cost: medium. The rows carry a flex basis (`RebaseCodeLabels`), plus split mode and review cards. No API. Check expert 1's survey first.

REAL WINDOW:
- **Monitor:** the 4K, 3840×2160 at 150 % (window DPI 144), 120 Hz, primary. The window log reports it in DPI-virtualised units as 2560×1440. Junction's client area 2160×1350 (1440×900 logical); Vesper 1860×1230. GL, a11y on, timer resolution 1 ms (Steam).
- **Holds:** 3 holds, 3 launches (h1 Junction 27.1 s, h2 Vesper 24.6 s, h3 Junction 24.5 s; waits 0, 0 and 184.8 s). 0 foreground takes, 0 nvlddmkm events. Idle gate: h1 failed (71 % CPU), h2 passed (1 %), h3 failed (27 %); only counts and bytes are reported.
- **What only the real window shows:** the live frame runs synchronously inside `SetWindowPos`, so its build depends on which batch is open at the call. Headless platforms never call the live frame themselves.
- **Hold 2 (Vesper):** write held 0 in every variant. Panels 160 px steps 7/7 frames posted and timer; reader 6 px steps 20/21 both ways.

WAITING, RE-RANKED:
- junction-changes-leads: leave plan 17 as reader behaviour, as expert 7 says. Unchanged at 440ba012: a reader's select is `ActivateItem` and toggles a folder. No perf number since round 8.
- junction-review-old-path: an app bug, not perf. Unchanged: no commit in 75f2288a..3038f82b touches `OldPath`. Move it to the app backlog.
- junction-summary-overtaken-at-start: keep as a low lead. `AsyncLoader` and the summary path are untouched since round 15. 56da4346 shares a save's `status`, not the summary's remote, for-each-ref, rev-parse and describe.
- vesper-local-waiting-text: wording, the user's call. Unchanged; not perf.
- junction-review-focus-colours: unchanged; small correctness, not perf. Move it to the app backlog.
- junction-chromeparttests-load: **close.** No recurrence in rounds 13–16. Junction.Tests passed 1,161/1,161 twice today under other experts' load. Main's last 30 CI runs: 22 green, 7 cancelled, 1 red (SingleInstanceTests).
- junction-gitwatcher-burst-test: **closed by f5bb3133.** The debounce now runs on a ManualClock, and `Forget()` drops the warm-up callback, which covers both guessed causes. App side: `TickCount64` became `TimeProvider.System` elapsed time, one timestamp read per event, 0 B.
- junction-leads (remaining): unchanged (no test names `SelectConflict`). Not perf; leave plan 17.

EXAMINED, NOT WORTH IT:
- The drain's budget in the real Junction: 0 budget stops in 109 passes, at most 2 actions in a pass. Nothing to tune.
- Vesper's 160 px steps: 1 frame a step whichever way the step is made.
- Changing 033a900c: 0 host-against-host reconciles in the apps.

NEEDS THE USER:
- Optional, app side, with reactivity-live-frame-sets-aside-batch: write the player's fullscreen flag and `Rerender()` before `_window.SetFullscreen` in Aperture and Reel, so the first frame of a toggle shows the right controls. This changes the pixels of one frame per toggle; no frame count changes. Before/after captures could be made in-process with a Reel clip, on a monitor the user can spare.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\b6ef64d9-4ed1-4793-9955-6a986d74de79\scratchpad\r16-reactivity\`
- `probe16.patch`: the probe, made at 440ba012 and reverted in my worktree. It holds the live-frame set-aside behind `HULTUI_PROBE_LIVE_SETASIDE`, the drain counters and the `CanPatch` counter (`HULTUI_PROBE_CANPATCH_LOG`).
- `ProbeLiveBatchTests.cs`, `livebatch.txt`, `test-live-0.log`, `test-live-1.log`, `cp-control.36916.txt`, `test2-*.log`.
- `start\`: the AOT host. `src\Script.cs` adds the `dragrelT`, `dragrelS`, `fix:` and `drain` steps and the held-write probe.
- `rest\`: `common6.ps1`, `batch6a.ps1`–`batch6c.ps1`, `drain6.py`, `frames14.py`, the driver files; runs in `rest\runs\` (h1-jwrap6, h2-vwide6, h3-jdrain6); `runs-h1.log`–`runs-h3.log`.
- `aotp\Junction`, `aotp\Vesper` (the hosts), `fx\jrepo` (a copy), `locks.log`, `gate.log`.

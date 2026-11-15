SATISFIED: no

Setup: worktree detached at 5a212f3e, SDK 11.0.100, git 2.53. I applied r14b6's `probe.patch` plus my own counters and env-gated prototypes, all saved in `final15.patch`. At the end I ran `git apply -R`: the worktree is clean at 5a212f3e, the build server is shut down, no probe process is left and the fixture clones are deleted. I report counts and bytes only.

REVIEW:
- **6a4c778d (a save over Changes builds once).** It holds only for a save that keeps the diff's lines. The colours that `ReloadChangesView` asks for early (JunctionApp.cs:894–902) cover the old diff's lines. When a save adds a line inside a hunk, the new rows are not covered (Highlight.cs:185–188), so the file is tokenized a second time and builds a second time. Fixture: CheckboxElement.cs on a jrepo clone with LF endings (a 7-line hunk), alternated ×2.
  - Headless: an in-place save is 1 frame, 1 build, 1 tokenize, 167–168 KB. A save that adds a line is 2 frames 15–16 ms apart, 2 builds, 2 tokenizes, 323–384 KB UI and 2.37–2.43 MB all-thread. → candidate 1.
- **Watcher (21cf206c, a4bad904, 3a859d8e, 05f19d27, e0263b8f): no objection.**
  - The 53-operation probe matches round 14 step 3's after-run. Files: 29/29 ref moves heard as refs, 4 stale (linked worktrees), 0 regressions, 5 extra refs reloads. Reftable: 29/29, 5 stale, 0 regressions, 6 extra.
  - Staging is heard: `git add`, `restore --staged` and a staged submodule bump are fixed against round 13.
  - An outside switch, pull, stash push or stash pop gives one callback each, with no extra reload.
  - Ignored folders at scale: headless Junction (Changes) on a clone of HultUI at 5a212f3e while dotnet builds in it.
    - Junction.csproj, cold (about 15 projects): 1,985 events, 1,347 dropped, 4 `check-ignore` runs (131 folders), 1 callback.
    - Touch a Core file, then build: 1 callback, the touch itself. The build adds none.
    - All of HultUI.slnx, cold (103 projects): 29,383 events, 20,429 dropped, 40 `check-ignore` runs (1,145 folders). 971 events were told because one window hit the 256-folder limit (GitWatcher.cs:90), which gave 1 callback (12 git runs).
    - The solution again with no change: 0 callbacks. Touch, then the solution: 1.
- **c7dd2365 / 8b994a41 (per-file patch loads): no objection.** Superseded reads keep running, since AsyncLoader never cancels a producer. Held key at 33 ms over c655b411 (333 rows):
  - Headless JIT: 21 git runs for 20 keys, 16–18 of them dropped.
  - Real window AOT: 0–2 of 17–18 dropped. Not worth a change (Examined).
  - Ctrl+D's kept-file read is confirmed (see Waiting).
- **ae8429d3 / 1b019826 / 6ea0db3b (Vesper): no objection.**
  - A listing that throws is rare: access errors return a Denied listing (FileSystem.cs:178–182).
  - The two timers per navigation (the 50 ms move, the 370 ms nudge) post to the UI thread but build and draw nothing once the rows have landed.

CANDIDATES:
- id: reactivity-junction-reveal-in-frame
  what: Junction's Commit tab and Changes scroll the caret into view in a callback after the frame (JunctionApp.cs:1202, :1208). Every key that moves the caret past the list's edge draws a second frame with no build, and the key's own frame shows the caret's row outside the list. History and the Files tree already scroll in the same frame.
  evidence:
  - A frame log at key 5 of a 400 ms walk: the key's build and frame at 3 ms, then `ScrollCommitCursorIntoView` → `ScrollRangeIntoView` → `MoveOffsetY` marks paint and draws frame 2 at 17 ms.
  - Prototype (`R15_SCROLLNOW`): the same call also in OnAfterBuild, with the after-frame call kept as the check (a no-op when the first was right).
  - Headless c655b411, frames per key: 400 ms 2.90 → 1.90, 100 ms 3.00 → 2.00, 33 ms 1.85–2.05 → 1.15. Builds and UI KB unchanged.
  - Headless Changes with 61 changed files: 2.40 → 1.60 frames per key at the edge, ×2 identical.
  real app: Junction, walking a big commit's files or a long Changes list with the arrow keys. This happens on every key once the caret reaches the bottom of the list (after about 30 rows), many times a minute while reviewing.
  - Real window, 10 keys 400 ms apart: 43 → 30 frames (4.3 → 3.0 a key); the frame 6 ms after each key is gone.
  - Real window, 20 held keys: 49 → 34 frames.
  expected: Time bar. One frame of the two or three a key draws goes away (−30 % frames a key in a real window), and the key's own frame shows the caret in view.
  cost: small, app only (call the reveal before layout as well). The settled pixels are the same; a transient frame goes. No API change.
- id: reactivity-junction-save-colours-cover
  what: Widen the new side of the early colours ask in `ReloadChangesView` by a few lines, so a save that adds lines inside a hunk is still covered when its rows land.
  evidence:
  - Prototype `R15_WIDEASK=8`, headless ×2: a save that adds a line goes 2 → 1 frames, 2 → 1 builds, 2 → 1 tokenizes, 323–384 → 199–229 KB UI (−38…−40 %), 2.37–2.43 → 1.93–1.98 MB all-thread (−18 %).
  - In-place saves are unchanged (167–168 KB).
  - The tokenizer walks from the file's start to the last shown line anyway, so the cost is 8 more lines tokenized.
  real app: Junction with Changes on screen while you edit in an IDE. This is every save that adds or removes a line inside a hunk, which is a large share of saves.
  - Real window: a save that adds a line goes 3 → 1 frames (the rows, their colours 16 ms later, and an a11y trailing frame about 100 ms later all collapse into one), 2 → 1 builds, 309–358 → 182–213 KB UI (−41 %), 2.38–2.46 → 1.98–2.00 MB all-thread.
  expected: Clears the memory churn bar (−40 % UI KB a save) and removes 2 frames a save.
  cost: small, app only. No pixel or API change: the final frame is the same.

REAL WINDOW:
- **Setup.** Two holds of `Global\HultUI-r15-desktop`, logged in `rw\holds.log`:
  - h1: waited 209.5 s, held 118.2 s, 5 launches.
  - h2: waited 198.7 s, held 94.9 s, 4 launches.
  - Idle gate before each hold. 0 foreground takes, 0 refused activations, 0 nvlddmkm events.
- **Monitors.** NativeAOT probe host on GL, on the 4K at 150 % (window scale 1.5, 120 Hz), layout 1440×900. Both monitors attached, the 1440p at 100 % to the left.
- **h1's bytes do not count.** My out-of-build mark logging was on in h1 and inflated its UI bytes (stack traces). Only h1's counts are used; h2's bytes are clean.
- **What the real window showed that headless did not:**
  - A layout-driven a11y trailing frame about 102 ms after every key in the Commit tab, and after each save that adds a line. That is the 4th frame of 4.3 a key; it is not in headless. For expert 7 (Window.cs:1706).
  - Per-file reads keep up with a 33 ms key repeat: 0–2 dropped of 17–18 on AOT, against 16–18 dropped headless on JIT.
  - Junction.csproj built beside the real window: over History, 1 callback (the touch), 4 git runs and 0 builds. Over Changes, 1 callback, 11 git runs, 1 build, 1 frame. The build itself adds none.

WAITING, RE-RANKED:
- junction-files-head-key: not reachable; move to Examined. The Files view browses `_selectedCommitHash ?? "HEAD"` (Files.cs:92), and History selects a hash as soon as the log lands (History.cs:428–436).
  - Walk: at mount the reference is HEAD; once the log lands it is f0f86d0c. After an outside commit both the reference and the body are unchanged, which is correct for that commit.
  - HEAD is read only before the log lands.
- junction-summary-overtaken-at-start: unchanged when the writer is outside an ignored folder, so keep it, low.
  - A log at the repository root appended every 400 ms: the first summary lands at 798 and 1,641 ms (2 and 5 loads dropped), and at 1,257 ms on 2 cores. remote, for-each-ref and rev-parse ran 3–5 times instead of once.
  - The same log under TestResults/ (ignored): 0 callbacks, 0 loads dropped, the summary at 970 ms. Round 14 removed the build case.
- junction-ctrl-d-kept-patch: confirmed; correctness, small. After End onto an unread file of c655b411, `CurrentDiffTarget` returns ACCESSIBILITY-PLAN.md while the open file is KeptRowTests.cs.
  - Ctrl+D's binary refusal (Diff.cs:1581) reads the kept file; the launch opens `_selectedFilePath`.
  - Fix: check `CommitDiffFor(detail, _selectedFilePath)` instead.
- vesper-local-waiting-text: unchanged (CommanderApp.Pane.cs:1187); your call.
- junction-changes-leads: unchanged.
- junction-chromeparttests-load: no recurrence. 6a4c778d's two new cases pump deterministic loads. Keep watching.
- junction-review-old-path: still reproduces by reading (GitDiffLine.cs:31/44; Review.cs:1182 `d.OldPath ?? c.File`).
- junction-leads (remaining): there is still no test of `SelectConflict` (Changes.cs:1480).

EXAMINED, NOT WORTH IT:
- **Watcher per-window limits (MaxAsk 256, MaxWaiting 4,096).** They cost 1 reload per cold build of the whole 103-project solution, and nothing on incremental builds (above). I did not run the prototypes that would lift them (`R15_ASKANYWAY`, `R15_BIGLIMITS`).
- **Per-file reads that let the read in flight land, then load the newest (`R15_FILECOALESCE`).** Headless at a 33 ms key repeat: 21 → 14–15 git runs, 0 dropped, −24…−30 % all-thread KB a key.
  - In the real window only 0–2 reads were dropped.
  - The naive version held an open file behind a read-ahead: 12 of 20 keys at 100 ms showed the kept diff on their first frame.
- **An untracked log appended every 400 ms.** Each append is one callback (status and worktree, 2 git runs over History). That is legitimate: Changes shows untracked content.

NEEDS THE USER:
- none

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\8e8ba33b-db33-43fa-885a-cb6b82b7253f\scratchpad\r15-reactivity\`
- `final15.patch`: the probe (r14b6) with watcher counters, the mark log and the prototypes `R15_SCROLLNOW`, `R15_WIDEASK`, `R15_FILECOALESCE`, `R15_ASKANYWAY`, `R15_BIGLIMITS`. Apply it from a worktree with its full path.
- Watcher probe: `fsw\` (FswProbe.csproj, Stubs.cs, runboth.ps1); logs `fsw-files.log`, `fsw-reftable.log`.
- Headless walks: `walk\src\R15.cs`; runners `run.ps1`, `bigbuild.ps1`, `walks2.ps1`–`walks5.ps1`.
  - Results: `bigbuild-A.txt`, `bigbuild-S-res.txt`, `walks2-res.txt`–`walks5-res.txt`; full outputs in `runs\`.
- Real window: `rw\src\Program.cs`, `rw\src\Script15.cs`, `rw\proj\`, `rw\pub.ps1`, `rw\rwbatch15.ps1`, `rw\summ15.py`.
  - Logs: `rw\runs\h1-*.log`, `rw\runs\h2\`, `rw\summ-h1.txt`, `rw\summ-h2.txt`, `rw\holds.log`, `rw\rwbatch.log`, `rw\gate.log`.

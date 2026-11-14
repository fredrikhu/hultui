SATISFIED: no

Setup: worktree detached at e6f7b5aa, SDK 11.0.100. I applied r13b4's `probe.patch` plus my own counters and env-gated prototypes (all saved in `final14.patch`). Real git 2.53 ran on private clones of round 12's jrepo only. I report counts and bytes, no timings. At the end I ran `git apply -R final14.patch`, so the worktree is clean at e6f7b5aa. The build server is shut down, no probe process is left, and the fixture clones are deleted.

REVIEW: no objection to any round 13 commit.
- **15f97351, 013e1f1b.** I reran the watcher probe (53 operations plus 5 extras) on both backends. The results match round 13 step 1's after-run exactly:
  - Files backend: 29/29 ref moves heard as refs, 0 regressions, 7 stale (3 index-only, 4 linked-worktree), 5 extra refs reloads in 53 operations.
  - Reftable backend: 29/29 heard, 0 regressions, 8 stale.
- **8f9f2683.** No new stale case. Every load that an event must re-key is keyed on DataGen or RefsGen, and loaders land reactively. The only disk read a build makes outside a loader is the issue-rules file, and the event handler checks it. In a real window History made 0 builds and 0 frames over 8 build-driven callbacks.
  - Separate lead, older than this commit and unconfirmed: the Files view's file body and blame at HEAD are keyed `{repo}|HEAD|{path}` with no generation (JunctionApp.Files.cs:677–680, Blame.cs:87–93). An open file may keep showing old content after a commit. My headless check read the wrong loader, so it proves nothing either way.
- **da72e01b.** A Load with the same key was already a no-op, and retries happen inside the loader. Junction commit-files went from 132.0 to 130.3 KB a key.
- **d413e001.** Forget cannot lose an outside change:
  - Anything heard before Forget is on disk before the reload's loads start, because they start at the next Build.
  - RefreshAll moves both generations and rebuilds, so it re-keys everything a dropped callback would have.
  - Anything heard after Forget is told as before.
  - The probe results are identical with it.
- **63e005f3.** The test fix is sound. The review path is safe because it sits behind a loaded merge request. The summary itself can still be overtaken:
  - AsyncLoader drops a superseded result, so a working-tree callback during the first full summary (5 git runs) restarts it.
  - Measured: Junction opened while a log file is appended every 0.4 s or 1 s. The first summary landed at 0.8–1.3 s on all cores and 1.3–2.1 s on 2 cores (affinity 0x3).
  - Before landing, 2–4 summary loads were dropped. The full summary's remote, for-each-ref, rev-parse and describe ran 3–4 times instead of once.
  - It lands every time here. On a slower runner it can be overtaken for as long as callbacks keep coming, which is the CI failure. Until then the branch label and remotes wait.
- **Sweep of 17 walks (25 rows) against round 13's b-s1 outputs.**
  - 0 empty builds everywhere, and equal counts except one improvement: Meridian tab switch 199.7 → 119.1 KB (−40 %; patched 112 → 90, made 115 → 92).
  - One Junction commit-files run showed 2 extra `git show` runs (+344 KB on all threads a key). The rerun was equal to round 13. The cause was an outside event on the shared r12 jrepo: walks on shared fixtures hear other experts' git activity.

CANDIDATES:
- id: reactivity-junction-watch-ignored-paths
  what: A working-tree event under a gitignored folder (a build's bin/obj, .vs, node_modules) reloads the repository, although git never shows those files.
  evidence:
  - Setup: a small project committed into a jrepo clone (jrepo's .gitignore ignores bin/ and obj/), built in about 1.7 s. Each build makes 91–114 events, which debounce into 2–4 callbacks.
  - Headless, with the loads run on the thread pool: History 4–8 git runs a build. Changes 22–33 git runs (diff 12–18, status 4–6, worktree, log, ls-files), 4–6 builds and frames, 3.2–4.4 MB on all threads.
  - Prototype that drops bin/obj events (`R14_SKIPBINOBJ`): a rebuild gives 0 callbacks, 0 git runs, 0 frames. A first build gives 1 callback. A source touch gives 1, as it should.
  real app: Junction beside an IDE or terminal that builds or tests the repository: every build. Callbacks grow with the build's quiet gaps of 300 ms or more. Each one runs a status (History) or the Changes load (about 11 git runs). Real window, History, three builds: today 3/3/2 callbacks, 6/6/4 git runs and 534/483/422 KB on all threads; with the prototype 1/0/0 callbacks, 2/0/0 runs, 288/182/196 KB.
  expected: Scaling bar: reloads that grow with a build's output phases go to 0. On Changes, a build saves 22–33 git runs, 4–6 frames and 3–4 MB.
  cost: medium. Ask git which directories it ignores, for example `git check-ignore --stdin` on a debounce window's new directories, cached and refreshed when .gitignore or info/exclude changes. A tracked file force-added inside an ignored folder must still be heard; check-ignore answers per path. No API or pixel change.
- id: reactivity-vesper-nav-on-arrival (vesper-enter-two-frames, re-ranked with the mechanism found)
  what: Vesper's NavigateTo builds the pane over the old rows straight away (PaneDirty builds the parts and the view synchronously), then builds again when the listing lands. When a frame falls between the two, it shows the new title over the old listing with the cursor at row 0.
  evidence:
  - Per navigation: 3 top builds (CommanderApp twice, from NavigateTo and from the listing's MarkViewDirty, plus the footer part again), 5 part builds, 74 patched, 183 made, 300.6 KB UI in a real window.
  - Frames per navigation: 1.00 headless, 1.42 headless with loads on the thread pool, 1.70 in a real window (14 of 20 navigations drew 2 frames).
  - A half-prototype that defers only PaneDirty (`R14_NAVLEAD`) made it worse, 1.42 → 1.92. The key handler's own writes (cursor to row 0, marks cleared) still build and draw.
  real app: Every Enter, Backspace, Back and Forward in Vesper, many times a minute.
  expected: 1.70 → 1.00 frames and 3 → 2 top builds per navigation, with no frame that mixes old and new content.
  cost: medium, app only. Apply the whole navigation (folder, cursor, marks, history, PaneDirty) when a local listing lands, or at the 350 ms connecting delay for a slow folder. Behaviour: the pane changes all at once. No API.
- id: reactivity-junction-watch-index-staging (correctness; see NEEDS THE USER)
  what: Staging from a terminal is never heard: `git add`, `restore --staged` and a staged submodule bump. The watcher ignores index renames on the belief that `git status` rewrites the index after every save, and that belief does not hold for real edits.
  evidence:
  - Optional-locks probe on a jrepo clone (2,726 files): after a content save, status wrote the index 0 times in 8 saves. Git writes it only after a file is rewritten with the same bytes: once, 332 KB, then clean.
  - Variant watcher that also hears index renames and answers each callback with a status (locks on): fixes 3 of 53 operations (4 of 60) on both backends, 0 regressions.
  - Echo cost: operations that write the index (switch, pull, stash, checkout --, the aborts) now give 2–3 callbacks instead of 1. The app's own status refreshes entries git marked racily clean and is heard back once or twice.
  real app: Changes stays stale after terminal staging until the next working-tree event.
  expected: Correctness, 3 operations. Cost: 1–2 extra reloads per outside checkout, pull or stash (2 git runs each on History, about 11 on Changes).
  cost: small (one GitWatcher line plus a test).

REAL WINDOW:
- **Setup.** One hold: waited 256.5 s, held 61.5 s. Three launches of the NativeAOT probe host on GL: Junction base, Junction with bin/obj events dropped, Vesper.
- **Monitors.** Both attached. I measured on the primary 4K at 150 % and 120 Hz; the 1440p is to its left. Logical layout 1440×900 (Junction) and 1240×820 (Vesper).
- **Guards.** 0 foreground takes, 0 activations refused, 0 nvlddmkm events.
- **Not seen headless:**
  - Vesper draws 1.70 frames a navigation against 1.00 headless (1.42 with thread-pool loads): the real window shows the stale frame that headless hides.
  - Junction during a build: History makes 0 builds and 0 frames, but runs 4–6 git commands and allocates 0.4–0.5 MB on other threads per build.
- **Same as round 13.** Junction start: 20 top builds (4 empty), 29 git runs, 11 frames.

WAITING, RE-RANKED:
- junction-git-optional-locks: measured, not worth it. Move to Examined.
  - The per-save index write it assumed does not happen for content saves (0 of 8). Status takes `index.lock` 1–2 times a save, only for its refresh.
  - With `GIT_OPTIONAL_LOCKS=0`, files rewritten with the same bytes stay stale: 192 entries, 2.6 MB re-hashed by every summary status until something writes the index.
  - Changes' `git diff --name-status` takes the lock and writes the index anyway (1 of 7 runs).
  - The staging miss is fixed without it (candidate 3).
- junction-changes-leads: unchanged; round 13 touched no selection pattern. Reader behaviour, experts 7 and 8.
- junction-review-old-path: still reproduces by code reading. `GitFileDiff.OldPath` is never set (GitDiffLine.cs:31; :44 says it stays null). App bug, not perf.
- junction-chromeparttests-load: no recurrence. 8f9f2683 rewrote 64 lines of ChromePartTests. Round 13's CI red was ReviewTests, and 63e005f3's `TempRepo.Settle()` removes that watcher race. Keep watching at the full suite.
- vesper-enter-two-frames: now candidate reactivity-vesper-nav-on-arrival.

EXAMINED, NOT WORTH IT:
- AsyncLoader coalescing (let the in-flight result land, then start the newest key). Changes under a 0.4 s churn for 10 s:
  - Today: 75 loads, 23 dropped as superseded (31 %), 266 git runs, 30 builds, 26 frames, 1.8 MB UI.
  - Coalescing: 0 dropped and 235 git runs (−12 %), but 78 builds, 68 frames and 8.1 MB UI. Worse: dropping superseded results is the cheaper rule.
  - History under the same churn drops 0 of 50.
- Vesper half-prototype `R14_NAVLEAD`: 1.42 → 1.92 frames (see candidate 2).

NEEDS THE USER:
- reactivity-junction-watch-index-staging: should Junction hear terminal and IDE staging (Changes follows it) at the cost of 1–2 extra working-tree reloads after each outside checkout, pull or stash?

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\41943a9b-f657-4483-971a-a2b346b967f7\scratchpad\r14-reactivity\`
- `final14.patch`: r13b4 probe, AsyncLoader counters, `R14_COALESCE`, `R14_SKIPBINOBJ`, `R14_NAVLEAD`. Apply from the worktree with the full path.
- Watcher probes:
  - `fsw\` with logs `fsw-files.log`, `fsw-reftable.log`.
  - `fswidx\` (the index-rename variant) with logs `fswidx-files.log`, `fswidx-reftable.log`.
- Optional-locks probe: `olk\Program.cs`, results in `olk.log`.
- Headless walks: `walk\src\R14.cs` (vesper-nav, junction-churn, junction-build, junction-startchurn, junction-fileshead), `run.ps1`, `sweep.ps1`, `compare.py`; outputs in `runs\` (`sweep-s1.txt`, `*-r14.out.txt`).
- Real window: `rw\src\Program.cs`, `rw\proj\`, `rw\pub.ps1`, `rw\rwbatch.ps1`, `rw\summ14.py`; logs in `rw\runs\h1-*.log`, `rw\holds.log`, `rw\rwbatch.log`.

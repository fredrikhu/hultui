SATISFIED: no

Setup: worktree detached at 2ced4e26, SDK 11.0.100. I applied round 12 step 2's `probe.patch` and added my own counters. The main new one counts empty builds: top-level builds whose reconcile marks nothing and makes no element (the base `PatchFrom`'s unconditional paint mark is set aside). I also added env-gated prototypes. Probes ran on real git 2.53 against fixture repos only, never the user's. I report counts and bytes, no timings. At the end I ran `git apply -R final13.patch`: the worktree is clean at 2ced4e26, the build server is shut down, the fixture server is stopped, and no probe process is left.

REVIEW: one objection, to b6c6eb6f: on a reftable repository it reads ref moves as working-tree-only.
- **What happens.** In a reftable repo, HEAD and `refs/heads` are stubs and every ref lives in `.git/reftable`, which no watcher sees. So a checkout, pull, reset, stash or rebase made outside the app only reaches the working-tree watcher. That event now bumps DataGen only, and the log, the branch label and the branch list keep what they had.
- **Watcher probe, 53 real git operations on fixtures (`fsw\`).**
  - Files backend: no regressions. 6 operations the old watcher missed are heard now (same-commit switch, detach, back, remote add, set-url, branch -u).
  - Reftable (`--ref-format=reftable`): 10 regressions where the old watcher fired and the new one says working tree: switch ×2, pull --ff-only, stash push/pop, merge --abort, rebase with a conflict, rebase --abort, cherry-pick --abort, reset --hard.
  - Reftable, 16 more ref moves neither watcher hears (branch and tag create/delete, fetch, push, update-ref, notes, bisect…). The old code healed them at the next editor save. Now they wait for a refs event such as a commit or a config write.
- **Headless Junction, 9 outside operations, each checked after the op and again after an editor save.**
  - HEAD: 8 of 9 stale both times (branch shown "main" while git says "other"; the log's HEAD row; the branch list).
  - Pre-b6c6eb6f behaviour (`R13_OLDWATCH`): 2 stale (branch create, fetch), both fresh after the save.
  - With a watcher on `.git/reftable`: 0 stale. Files backend at HEAD: 0 stale.
- **Why it matters later.** Git's plan for 3.0 makes reftable the default for new repositories.
- **053a35ef:** no objection. `SameAs` compares all nine summary fields and `GitWorktree` equality covers every field. One leftover: another repo's event still builds the view (candidate 4).
- **3912f391:** no objection. The title bar and rail show nothing of the active tab, and no window title follows it.

CANDIDATES:
- id: reactivity-junction-watch-reftable
  what: When `.git/reftable` exists, GitWatcher also watches it and treats the `tables.list` rename (the only event a committed ref transaction makes) as a ref move.
  evidence: The fixed probe over the 53 operations: 0 regressions and 0 ref moves unheard. The headless app check goes from 8/9 stale to 0/9.
  real app: Any reftable repository (opt-in today with `git init --ref-format=reftable` or `git refs migrate`). Every outside checkout, pull, reset, stash or rebase leaves Junction showing the old branch and log until a commit.
  expected: Fixes round 12's regression. A correctness fix, not a perf bar.
  cost: small, one watcher. No API or pixel change.
- id: reactivity-junction-watch-lock-echo
  what: A ref transaction that changes nothing still creates and deletes a lock. Every `git fetch` (git 2.53) takes `refs/remotes/<r>/HEAD.lock`; reftable takes `tables.list.lock`. The refs watcher reads that as a ref move. So Junction's background fetch reloads everything 300 ms after its own fingerprint check (RemoteOps.cs:219–224) decided not to. Fix: a lock file alone, or a folder's own Changed, counts as a working-tree event. For reftable only the `tables.list` rename counts as a ref move. Reflog keys on DataGen, because reflog-only moves such as merge --abort's reset entry arrive as lock events only.
  evidence:
  - Raw event dump of a no-op `fetch --all --prune`: Created and Deleted on `HEAD.lock` plus the folder's Changed; no rename.
  - Headless background sweep, one repo, nothing new: 17 → 5 git runs, UI 253.2 → 24.0 KB, all threads 824.3 → 181.5 KB, 2 → 1 builds and frames. Together with candidate 4: 3.4 KB UI and 0 builds or frames. No watcher at all gives 3 runs and 1.5 KB.
  - Toolbar Fetch, nothing new: 28 → 17 runs, UI 1,305.8 → 1,013.0 KB (−22 %), all threads 2,267 → 1,591 KB (−30 %).
  - Watcher probe with the variant: 0 regressions against HEAD on both backends over 53 operations. Simply dropping the lock events lost 4 (merge and cherry-pick --abort, reflog expire, worktree add): measured, rejected.
  - This predates b6c6eb6f: the old watcher fired on it too.
  real app: Auto-fetch (off by default; every 5 minutes, per workspace repo in "all open" mode), the toolbar Fetch, any outside or IDE background fetch. Real window, per no-op fetch on the jrepo clone: 14 git runs, 4,463.6 KB all threads, 514.2 KB UI, 2 builds, 2 frames.
  expected: Churn −90 % per sweep and −99 % with candidate 4; 12 fewer git runs per fetch event.
  cost: small (GitWatcher plus one load key). No API or pixel change.
- id: reactivity-junction-own-action-echo
  what: An in-app action that moves a ref reloads twice: once from its own `RefreshAll`, and again 300 ms later from the watcher hearing the same change.
  evidence: Headless palette checkout, other and main alternated, compared with no watcher (the upper bound): 28 → 14 git runs, UI 1,030.0 → 796.3 KB (−23 %), all threads 2,019.6 → 1,267.5 KB (−37 %), builds 16.6 → 15.0. The same applies to commit, pull, push, branch, tag and stash from the app.
  real app: Every Junction action that writes refs, several times a session.
  expected: Churn bar, −23 % UI and −37 % all threads per action, −14 git runs.
  cost: medium, not prototyped. Option one: read a refs/HEAD/config fingerprint (1–2 git runs) before the own reload starts, and let a refs event reload only when it moved; reading it first keeps an outside change made meanwhile safe. Option two: pause the watcher around the app's own commands. No API change.
- id: reactivity-junction-watch-view-skip
  what: A watcher event rebuilds the view (`RerenderView`) even when no view load re-keys. Two cases: (a) the event's repo is not the one on screen; (b) a working-tree event while History, Branches, Tags, Files, Commit or Overview is shown (only Changes, working-tree diffs and Submodules key on DataGen). Prototype: `DataGen()` notes reads made during `JunctionApp.Build`, with the summary and worktree loads read apart because `Reload` starts them itself. The rebuild is skipped when the last view build read none.
  evidence: per event, headless:
  - (a) Other repo, file written/deleted: 2 → 1 builds, 84.7 → 68.8 KB (−19 %), 2 → 1 frames. Touched with the same content: 18.4 → 2.4 KB (−87 %), 1 → 0 builds and frames.
  - (b) History, file written/deleted: 167.4 → 151.5 KB (−9.5 %), 5 → 4 builds, 2 → 1 frames; the empty build is gone (1.00 a event, 18.1 KB). Touched: 20.5 → 2.4 KB (−88 %), 1 → 0.
  - Changes view unchanged: 304.1 KB and 7 builds both ways.
  - Final frames: History PNG identical (382B69ED…, the same hash as round 12). Changes a11y tree identical; its pixels differ between two base runs as well.
  real app: Junction beside an editor, in a multi-repo workspace: every save in any repo. Real window: 186.1 → 168.0 KB UI, 5 → 4 builds and 2 → 1 frames a save; touched 21.9 → 1.6 KB, 1 → 0 builds and frames; empty builds 13 → 4 a launch.
  expected: Churn −19…−88 % per event, one frame fewer.
  cost: small for (a), small–medium for (b). No API or pixel change.
- id: junction-git-optional-locks (lead, not measured)
  what: Run Junction's read-only git with `GIT_OPTIONAL_LOCKS=0` (git's setting for background readers).
  evidence: Today `git status` takes `index.lock` and rewrites the index (332 KB on jrepo's 2,726 files) whenever it refreshes stat data, that is, on the first status after every save. Two consequences:
  - Lock contention with the user's own git: the ledger archive's 2026-11-09 log has a test's `git commit --amend` hitting `index.lock` held by the app's background git.
  - Because status rewrites the index, the watcher must ignore index renames. So a terminal `git add`, `restore --staged` or staged submodule bump is never heard (0 events, both watchers, both backends).
  real app: Junction open while the user works in a terminal.
  expected: Removes one index write per save, the lock contention, and the staging miss. Not measured.
  cost: small code. Behaviour change: background reads no longer refresh git's stat cache.

REAL WINDOW:
- **Setup.** One hold: waited 218.5 s, held 80.1 s. Two launches of a NativeAOT probe host (base, and proto = view-skip + locks dropped + reftable watcher), GL, on the 4K at scale 1.5 and 120 Hz (window 2160×1350 px). Junction on a local clone of jrepo with a local bare origin, History on screen, a posted WM_MOUSEMOVE resting in the window.
- **Guards.** 0 foreground takes, 0 activations refused, guard silent, 0 `nvlddmkm` events.
- **Same as headless.** Builds and frames per event match: working-tree event 5/2, touch 1/1, and with the prototype 4/1 and 0/0. UI bytes about 10 % higher with the pointer (186 vs 167 KB).
- **Not seen headless:**
  - On a real-size repo the no-op fetch echo costs 14 git runs and 4.46 MB on all threads (0.82 MB on the small headless fixture). With locks dropped: 0 runs, 81.7 KB, 0 builds, 0 frames.
  - An outside checkout: 16 git runs, 6.1 MB, 8 builds, 5 frames, 1 of the builds empty. With the prototype: 7 builds, 4 frames.

WAITING, RE-RANKED:
- junction-changes-leads: no perf number, and nothing in my area changed. Reader behaviour; stays with experts 7 and 8.
- vesper-brief-filter-clear-reveal: the scrollbar half is done (round 10 step 15: `BriefScrolled`, CommanderApp.Views.cs:616–625; `GridCursorNoBuildTests.InBrief_AKeyThatScrolls_LeavesNoBlankColumn`). The filter-clear reveal is a correctness item, not perf, and I did not re-measure it. Keep with expert 1.
- aperture-cursor-guard-textarea: ApertureApp.cs:554 still tests `is not TextInputElement`. The only TextArea (the problem report over the player) is caught earlier at :500, which tests both types. 0 B and no reachable case: close it as perf.
- junction-chromeparttests-load: no recurrence in rounds 11–12 (round 12's red CI attempt was `HighlighterConcurrencyTests`). Since b6c6eb6f, one ChromePartTests case (remote added) arrives through the real FileSystemWatcher alone, a new load-sensitive point. Keep watching.

EXAMINED, NOT WORTH IT:
- Common user actions in all five apps, 31 headless walks (Vesper 7, Meridian 3, Junction 8 including the changes300 set, Aperture 3, Reel 2): 0 empty builds in every walk. Vesper brief and viewer, Junction wheel and difffind, Aperture detail and grid, and Reel playback build 0–0.01 times a key.
- Misses both watchers share, old and new: index-only changes and a linked worktree's own switch, remove or add. They are data-only and heal at the next event.
- Refs reloads for nothing: pack-refs, gc and config edits. Rare.

NEEDS THE USER: none.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\17c11f5e-9de0-4477-8f58-0d0a5e006605\scratchpad\r13-reactivity\`
- `final13.patch`: probe and prototypes (`R13_OLDWATCH`, `R13_REFTABLE`, `R13_NOLOCK`=1/2, `R13_VIEWACTIVE`, `R13_VIEWDG`, `R13_NOWATCH`, `R13_MARKSITES`). Apply from the worktree with the full path.
- Watcher probe: `fsw\` (Program.cs, the old and new watchers); results in `fsw-files.log`, `fsw-reftable.log`, `fsw-reftable-fix.log`, `fsw-*-nolock.log`, `fsw-*-lockwt.log`; raw event dumps in `fx\fsw-dump-*.txt`.
- Headless walks: `walk\src\R13.cs` (watch-other, gitops, own) and `Program.cs` (the EMPTY line), `run.ps1`, `sweep.ps1`; outputs in `runs\` (`ops`, `own1.txt`, `viewdg.txt`, `watch1.txt`, `sweep-s1.txt`, `*.out.txt`); images in `png\`.
- Real window: `rw\src\Program.cs`, `rw\proj\`, `rw\pub.ps1`, `rw\rwbatch.ps1`, `rw\summ13.py`; logs `rw\runs\h1-junction-{base,proto}.log`, `rw\holds.log`, `rw\rwbatch.log`.

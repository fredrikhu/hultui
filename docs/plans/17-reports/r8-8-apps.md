# Round 8 survey, expert 8: App workloads

SATISFIED: no

Method: I re-ran round 7's 47 headless walks as a NativeAOT publish of round 7's walk host on 27020cdb, with round 7's probe. The probe's VirtualList hunk no longer applied cleanly, so I re-placed that one line by hand. Two reps, bytes and counts only. The baseline is round 7's after rows (57e6547c, the start of round 7). UI bytes per key agree between reps to 0.1 %.

I added three walks: Aperture Discover, a Commit-view file walk that starts from the list's first row, and that walk's second stretch. For a fair check of the Commit view I also rebuilt base trees (57e6547c, fc7dc90d, 3d54d779, 604d5e7e) and ran them against 27020cdb, one host at a time. No real windows, no Jellyfin sign-ins (Aperture ran on `FakeMediaLibrary` / `FakeRequestServer`). The worktree is clean at 27020cdb (probe removed by `git apply -R` plus a reverse edit) and the 127.0.0.1:8767 fixture server is stopped.

REVIEW: none. Round 7 helped the real apps, not only suite rows, and no walk got worse on the UI thread.

- **Totals, UI KB per key:**
  - Round 6's 33 walks: 2,373 → 2,029 (−14 %), geometric-mean ratio 0.72.
  - All 47 walks: 4,894 → 4,229 (−14 %), geometric mean 0.60.
  - No walk worse by more than 2 %.
- **Landed rows hold in the apps:**
  - Aperture Home Right/Down 64.6/65.3 → 0.4 (−99.4 %), 0 builds.
  - Junction summary typing 15.1 → 2.0 (with a reader 15.7 → 2.5).
  - Meridian URL typing 68.5 → 8.9 (0.05 builds a character).
  - Meridian find Enter 60.3 → 12.7; Junction diff find Enter 87.8 → 31.6 (0 builds).
  - Vesper type-to-find 111.8 → 61.0 (−45 %).
  - Junction Changes 300 wheel with a reader 25.2 → 16.3.
  - Meridian find typing 75.1 → 51.1; its search rest 24.4 → 0.4.
  - Aperture grid Down with a reader 111.9 → 84.9 (trees 0.99 → 0).
  - Meridian History Down 68.7 → 52.8 (rows 2.5 → 0.5).
  - Vesper Detail 26.1 → 20.9, Brief 34.0 → 28.8, Thumbnails −13 %, partner −13 %, Insert −11 %, Tab −5 %.
  - Junction History 60 ms 33.8 → 30.2; all threads 1,644 → 30.2; GCs per 100 keys 12/9/4 → 0/0/0.
  - Junction History 500 ms with a reader: all threads 5,195 → 35.1.
  - Changes 300 Down 313 → 295 (with a reader 358 → 312); session walk −7 % (with a reader −18 %).
  - Rich text editor (review composer) 3.7 → 3.3 (with a reader 4.3 → 3.9).
  - Meridian palette with a reader 36.8 → 31.9.
- **Flat (within 1.5 %):** Tags, Files, big files, Vesper folder in/out and viewer page, Meridian History search, body typing and method pick, Aperture grid Down/Right/resize, Reel 0.8 KB a frame.
- **f64e4674 (app-support merge):** no app uses its new APIs (`GrowsWithContent`, `DismissPopups`, `Window.CloseOverlays`, `SaveFrame`). Its per-frame additions allocate nothing: `DrawingFrame` is a struct, and `RestoreToCount` runs once a frame. Where the walks themselves didn't change, layout visits and measure misses are unchanged (Aperture resize 75/19, method pick 208.3), so 39e60064's stretched-margin measure moved no counted work. Aperture's walk processes now exit 0; in round 7 every rep ended with 0xC0000409 at exit.
- **Checked and not an objection — the Commit view's all-thread bytes (3d54d779):**
  - `commit-files-100ms` read 295 → 1,909 KB a key on all threads, GCs 1/0/0 → 7/3/1, UI 222.5 → 159.6.
  - Cause one: the walk's start changed. With 3d54d779 History loads only the commit it rests on (2 detail loads, not 95), so `PickInitialFile` opens git's first file. That file, `.git-blame-ignore-revs`, is row 437 of 438 in the tree, so the walk's 24 Down keys sit at the end of the list (77 KB, 0 layout each).
  - From the first row (Home), same walk on both trees: UI 288.6 → 291.4. All threads: fc7dc90d 460, 3d54d779 1,737, 27020cdb 1,768.
  - Cause two: first use of the syntax grammars. The next 60 files, after 60 unmeasured keys, cost 285.6 → 290–296 KB, GCs 1/0/0 both. At walk start 27020cdb had 1 grammar loaded and fc7dc90d had 2 (History's per-move prefetch had loaded one more).
  - So the grammar loads and first tokenizes moved from History's prefetch (for a tab nobody had opened) to the first Commit-tab walk; they were not added.
- **Notes:**
  - Junction Files: all threads 127.4 → 137.0 KB a key (+7.5 %), identical in both reps, git counts equal. It is off the UI thread and under the 10 % bar; not traced.
  - Big files at 33 ms: all threads 1,783 → 3,148. This is timing: git runs vary 0.0–0.9 a key, and round 7's base rep 2 read 2,981.

Shares of UI bytes on 27020cdb:
- **Junction:** History build 48 %, render 44 %. Tags build 59 % (one JunctionApp build a key). Files build 62 % (1.23 builds and 149 measure misses a key). Changes 300 Down render 83 % (26.2 diff rows a key), build 12 %. Diff find Enter render 97 %.
- **Vesper:** Detail build 72 %. Tab build 98 %. Folder in/out render 57 %, build 41 %.
- **Meridian:** History Down build 80 %; find typing build 92 % (one response Part a character).
- **Aperture:** grid Down render 59 %, build 38 %.
- **CPU:** headless render (CPU raster) is 78–98 % of UI cycles on every walk.

CANDIDATES:
- id: apps-aperture-discover-cursor-no-build (owner 6; app)
  what: a cursor key on Discover moves the ring on the live cards, as Home has since 31e1aaa7, instead of rebuilding every shelf.
  evidence:
    - New walk `discover-right/down-100ms`: `FakeRequestServer`, 5 shelves of 20 titles, 35 cards and 364 elements on screen.
    - 334.2 / 337.6 KB a key; build 320.3 (96 %); 0.99 ApertureApp builds a key.
    - `MoveRingWithoutBuild` returns false off Home (`ApertureApp.Cursor.cs:170`), so the key calls `RerenderView` (`:128`, `:139`).
    - `BuildDiscoverView` (`ApertureApp.Discover.cs:25`) rebuilds every row, its LINQ lists and the `string.Join` cursor key (`:77`) on each key.
  expected: bytes bar, about 334 → 0.4 KB a key (−99 %), as Home's five shelves went 255.7 → 0.3; builds a key 1 → 0.
  cost: small–medium, app only. Reuses `KeptElements` and `MoveRingWithoutBuild`. No pixel change if done as Home was.
- id: apps-meridian-history-archive-part (owner 6; app)
  what: a History key moves the list's selection through its ref and builds only the archived-send panel, as its own Part; the window does not build.
  evidence:
    - History Down 52.8 KB a key; build 42.4 (80 %); one MeridianApp build a key.
    - Measured alone, `BuildHistoryRealView` costs 40.7 KB a call (archive panel 14.9, list 3.2, filter bar and footer about 22.6). The chrome is only about 1.7 KB.
    - The archive is built inside the view (`MeridianApp.History.cs:188-191`). This is ledger step 10's "Left open".
    - History search typing (47.4 KB a character, build 63 %) also rebuilds the archive for a selection that did not move.
  expected: bytes bar. Down about 52.8 → 24 KB (−55 %); search typing about −30 %.
  cost: medium, app only. No pixel change.

WAITING, RE-RANKED:
- **Suite scenarios:**
  - Document switch: still worth a suite row. Junction Files is unchanged at 85.0 KB a key: view build 62 %, 1.23 builds a key (the file's text lands and builds again), 149 measure misses.
  - Held selection with all-thread bytes: drop. `CommitPrefetchTests.AHeldArrow_LoadsOnlyTheCommitItStopsOn` now counts detail loads and pins 3d54d779.
  - New: none needed for Discover; the existing `component/shelf walk` rows pin the pattern, and an Aperture test like `HomeCursorTests` would pin the app.
- **junction-tags-test-flake:** close. I ran `TagsViewTests`, `BranchRootMemoryTests`, `NewWorktreeBranchTests` and `CommitPrefetchTests` (29 tests) 6 times while the walk batch loaded the CPU: 0 failures. Add round 7's two full suites and CI 41691075588 (df45b4a2), all green since 63467f8e.
- **junction-changes-leads:** not worth it from the app side. A caret key at 300 files is 295.4 KB; 83 % is render from 26.2 rows a key, which are the reached file's diff rows and are needed (rows can't follow their item across files). Build is 36.7 (12 %); with a reader a11y is 19.1 KB, 1.03 applies, 0 trees. The reader-side items (a stage's tree, multi-selection members, folder "select") stay with expert 7, not measured here.
- **junction-leads (remaining):**
  - ChangesView lists during a stage: closed by 95c86c97 (kept in flight, rows 62 → 5).
  - Big files gen 2: 1 per 40 keys, the same as round 7's base rep 2; not worth it.
  - Old-side convergence: expert 5's, not measured.
  - `SelectConflict` tests: not perf.
- **aperture-leads (remaining):**
  - Discover: measured → apps-aperture-discover-cursor-no-build.
  - Toasts: still not measured.
  - The cursor-focus comment: a bug, not perf.
  - Grid Down 81.4 KB (build 38 %, render 59 %) and Right 48.0 (build 61 %): onto `KeptElements` later, per the user.
- **vesper-coalesce-markdirty:** still 3 CommanderApp and 5 Part builds per folder change; build is 127.3 of 311.7 KB (41 %). Two app builds are about 30 KB (−10 %), at the bar. I agree with expert 6: drop.

EXAMINED, NOT WORTH IT:
- **Commit view's all-thread rise after 3d54d779:** the first use of the syntax grammars moved from History's prefetch to the Commit tab. The second stretch is 285.6 → 293 KB on all threads, GCs equal (bisected: fc7dc90d 460, 3d54d779 1,737).
- **`commit-files-100ms` against round 7:** not comparable. The start file changed, so 24 of its 60 keys sit at the end of the list. Use the from-the-first-row variant: 288.6 → 291.4 UI.
- **Meridian method pick:** 146.9 KB, two whole-window builds a pick (open, pick), 208 measure misses. Once per action.
- **Meridian History search alone:** after the archive Part, the chrome left to save is about 1.7 KB.
- **Junction Files all-thread +9.6 KB a key:** off the UI thread, 7.5 %, under the bar.

NEEDS THE USER:
- No new decisions.
- Reminder: no Jellyfin sign-ins this round. The "aperture-survey" devices and the API key are still to be removed and revoked when the work is done.

FILES: scratchpad `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\64e7e0e2-bc1c-4069-9d42-9311fee04c9e\scratchpad\r8-apps\`:
- Walk host: `walk\src\Program.cs` (round 7's host plus `ApertureDiscover`, `commit-files-home`, `commit-files-second`, the Commit view diagnostic and the Meridian History build-cost probe), `walk\Walk.props`, `walk\after\`, `walk\base\`, `walk\bis*\WalkHost.csproj`.
- Scripts: `run.ps1`, `batch8.ps1`, `aotpub8.ps1`, `homepair.ps1`, `bistree.ps1`, `bisseq.ps1`, `second.ps1`, `tagsflake.ps1`, `merge.py`, `cmp.py`, `sum.py`.
- Data: `runs\walks.jsonl` (round 8, 2 reps), `runs\cmp-in.jsonl` (merged with round 7), `cmp-all.txt`, `runs\home.jsonl` (Commit view pairs and bisect), `runs\discover.jsonl`, `runs\after4-aot-hc-meridian.out.txt` (the HISTCOST line), `tagsflake.log`.
- Trees: `base-tree` (57e6547c with the probe), `bis-*` (bisect extracts), `fx`, `www`.

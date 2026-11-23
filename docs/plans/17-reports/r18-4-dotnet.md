SATISFIED: no

Set-up: the worktree was detached at aa3a3696 (SDK 11.0.100). I report bytes, counts and complexity only. One prototype (static selection handlers in `TextElement`) was undone by a reverse edit, so the worktree is clean. Build servers are shut down and nothing is left running.

REVIEW: no objection to round 17's commits or to main's. One lead about b1423e6e goes to candidate 1: the collection never runs after a big walk that finishes.
- **b1423e6e (one aggressive collection at rest).**
  - **When it fires:** headless JIT on bigrepo, counting `HighlightIdleCollections`:
    - 6 Downs at 400 ms, then rest: 1 collection. Committed 49.5 → 20.9 MB, ws 123.4 → 90.1 MB.
    - 4 Downs, then History while a walk is running: 1 collection (`DropHighlightPrefetch` cancels the walk, the texts are kept, the collection follows).
    - 6 Downs at 2.5 s: 0 while the keys come, 1 at rest. No key gap reached 2 s of idle.
    - jrepo (HultUI's own history), History and Commit file walks: nothing kept, 0 collections.
  - **Pauses:** 8.2–16.9 ms from `GCMemoryInfo` (JIT, information only); round 17 measured 7.6–12 ms in a real window. Only at rest.
  - **Allocations:** every landing while the flag is set schedules one `Task.Delay` with its continuation (a few hundred bytes). Nothing else allocates.
  - **What defeats it:** only `Dispose` and a scheme change stop a pending collection. The gap is a walk that finishes, where nothing is kept, so nothing collects (candidate 1).
- **a787c194 (`UnsafeAccessor` into TextMateSharp).** Clean on NativeAOT.
  - Expert 8's publishes of all five apps at aa3a3696 show 0 IL2xxx/IL3xxx warnings.
  - The accessor binds at compile time. Round 17's AOT real-window bytes (293.7 → 168.5 MB) show the lines are taken on AOT.
  - What is kept: `LinesLeft` 40 B plus 9 B a line, counted. The ~13 B a line of state objects is still not counted (known). The 8 B a line index goes with the call.
  - Finished walks leave the `ConditionalWeakTable`.
- **3ea96588 (a `Computed<bool>` per view tab).** Seven, made lazily once per `JunctionApp` and kept for the app's life: about 2 KB in all.
  - A recompute unsubscribes and re-subscribes through HashSet operations (0 B).
  - `NotifySubscribers` rents from `ArrayPool` (0 B once warm).
  - No leak.
- **e790d321 (size-only ranges).**
  - `WrapRange` grows 24 → 32 B. A size-only range weighs 300 + 4 B a character, inside the same 4,096-entry / 16 MB bound.
  - `LineSizes` (24 B) is reused. The path checks `KeepsWrapRanges` and `ReuseAcrossWidths` at entry.
- **Element sizes at aa3a3696 against 0df5e3ea: unchanged.** BoxElement 600, TextElement 672, ButtonElement 640, Window 1,032.
  - `WebViewElement` 632 → 688 (e66ead9f; one per web view).
  - `Aperture.Core.MediaItem` 264 → 336 B (ee044d36/93f8a26d: five new fields). That is +0.7 MB for 10,000 items.
  - `HighlightTexts` 48 → 72 B.
- **0f9c8477 (Aperture history): bounded, no leak.** Headless over `FakeMediaLibrary`, 160 films, by keyboard:
  - A page in history is a `Visit` (40 B), a `Route` (40 B) and its strings; the list is capped at 100 (150 forward navigations leave 100).
  - Heap after a full GC: open/back ×300 between two films 1.32 → 1.35 MB.
  - 150 different films: 1.32 → 1.84 MB, falling back to 1.62 MB (the text caches filling).
  - `_arrivalFocus` is a strong ref that is never cleared. After Back it still holds the last item page (50 elements), but something else holds that page too: clearing the ref frees 0.0 KB. It is one page, bounded.
  - `Pressable` uses one static delegate. The `ShowsFocusVisual` copy costs 0 B.
- **Main's other commits.**
  - 78a69ee5: the right-to-left run cache grows lazily (0 B in apps without RTL text) and is bounded at 16 MB / 32,768 entries.
  - 52acb2f2: `ShaperCalls++` is a plain counter.
  - 0a7bb324: `OnlyPaints` walks a subtree only at opacity 0, the same order as the render it skips; 0 B.
  - Nothing else changes allocations.

CANDIDATES:
- id: dotnet-junction-collect-after-big-walk
  what: Run b1423e6e's one aggressive collection at rest after a big diff walk that finished, not only after one whose texts were kept.
  evidence:
  - Real window, NativeAOT (expert 8's shared JunctionHost at aa3a3696), bigrepo, 2 launches equal within 0.5 MB.
  - After the Commit open's walk finished, rested 5–10 s: no collection (gc 11/2/1). Committed 33.5 MB, ws 134.5–134.8 MB, private 145 MB.
  - After 3 first visits at 4 s, each walk finished, rested 5 s: committed 41.0, ws 144.7, private 154.4–155.1.
  - Then one cancelled walk made the existing collection fire (gen 2 +1): committed 13.6, ws 119.3–119.8, private 128.3–129.7. A forced compacting full GC adds nothing (119.5–120.1).
  - Headless JIT: 6 revisits at 4 s leave heap 40.9 / committed 46.9 / ws 118.9 MB at rest, against 20.8–27.6 / 20.9–27.6 / 90–101 after a collection.
  - Code: `HighlightLanded` (JunctionApp.Highlight.cs:568) sets `_collectWhenIdle` only through `KeepHighlightTexts`.
  real app: Junction, opening a big diff in Commit or Changes and reading it while its colours land. Nothing at rest collects after that, so the window keeps 15–25 MB of working set until the next walk. This happens on every big file a reviewer stops on.
  expected: retained bar. Working set at rest 144.7 → ~120 MB (−17 %) after finished walks, 134.5 → ~120 (−11 %) after just the Commit open. Committed 41.0 → 13.6 MB. Cost: one 8–12 ms UI-thread pause per rest after a big walk.
  cost: small, Junction only, no API, no pixels.
  - Also set `_collectWhenIdle` when a landed walk was big: lines walked ≥ ~2,000, or bytes the worker allocated for the walk ≥ ~32 MB (`GC.GetAllocatedBytesForCurrentThread` around `Tokenize`).
  - Small files must not trigger it: jrepo's walks (275 lines over 10 files) must keep 0 collections.
  - Guard: extend `HighlightResumeTests`' idle-collection test to cover a walk that finished.

REAL WINDOW:
- One hold of `Global\HultUI-r18-desktop`, 10:59:51–11:01:03 local: waited 241.7 s, held 72.2 s.
  - The idle gate failed just before it (last 13.9 %, mean 26.5 % CPU); bytes and counts are unaffected.
  - 2 launches of expert 8's NativeAOT JunctionHost on fresh bigrepo copies. Both exit 0 on WM_CLOSE; 0 foreground takes, 0 nvlddmkm events, 0 screen reads.
  - Timer resolution 1 ms (Steam).
  - Desk: windows on the 4K (DISPLAY2, primary, 3840×2160 at 150 %, DPI 144, 120 Hz), 2160×1350 px, GL, HDR display on. The 1440p (100 %, 120 Hz) is to its left.
- **What headless did not show:**
  - The idle collection fires on AOT with the real clock and vsync: gen 2 +1, committed 41.7 → 13.6 MB within 6 s of the cancelled walk.
  - After walks that finish, committed stays 2.5–3× higher at rest (33.5–41.0 MB) for as long as the window rests. That is candidate 1.
- No Aperture real-window run of mine: only expert 8 signs in, and expert 8's hold covers the keyboard walk and history.

WAITING, RE-RANKED:
- **elements-selectable-label-lazy-wiring:** I own it (4, allocations and element size). Below the bar; keep it as a nit for when `TextElement` is next touched.
  - Measured: a plain label 672 B, a selectable label 1,552 B. The +880 B is six owned-handler delegates, the blur closure, the context-menu delegate, and handler arrays of 2, then 4, then 8 slots.
  - Prototype: static delegates that read `CurrentTarget`, plus one 6-slot array. A selectable label drops to 976 B (−576 B, −37 %).
  - Junction Commit file Down, 27 selectable labels on screen: 279.3 → 267.3 KB a key (−4.3 %).
  - Vesper reader wheel step, 41 on screen: 25.9 → 24.8 KB (−4 %).
  - Fully lazy wiring (−880 B) would reach at most about −6 %.
- **findbar-countonscreen-foreach:** still a nit. FindBar.cs:212 still loops `foreach` over `bar.Children`; no commit in the range touched FindBar.
- **textarea-chunked-document:** stays parked. No TextArea or Meridian change in the range, and the user's 256k condition is unmet.
- **fedora-local-dotnet:** keep waiting. On 2026-11-22 `updates` still has 11.0.100~rc.3 and `updates-testing` has the final 11.0.100-1.fc44. The installed rpm is still dotnet-sdk-10.0-10.0.112, and both links still point to /usr/local/share/dotnet (11.0.100). WSL was terminated after the check.

EXAMINED, NOT WORTH IT:
- **Aperture history retention** (0f9c8477): bounded as in REVIEW. The `_arrivalFocus` ref frees 0.0 KB when cleared.
- **Aperture private-byte growth in my art-mode probe:** an artefact of my fixture, which answers every picture with a 600×400 PNG, under PosterCache's 400-picture cap. Heroes are bounded at 4 (`HeroesKept`), and real poster sizes have been set by display scale since round 1's ebefa29.
- **Aperture per-key bytes:** a Home-row Right is 10.6 KB and a grid Right 0.4–31.9 KB. The 31.9 KB comes from the cursor crossing into rows not yet made (0.2 builds, 1.3 rows a key). Nothing allocates per frame.
- **Aperture Back to the grid** (a lead for experts 6/8, not my fix): 323 KB (1 build, 6 grid rows made again, 69.6 KB of it in input handling). Opening an item page is 135–189 KB. The grid's rows are not kept across an item page.
- **`MediaItem` +72 B:** 0.7 MB per 10,000 items.

NEEDS THE USER: none.

FILES (in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\00f54f03-b511-4949-9273-30fe719e1bbb\scratchpad\r18-dotnet\):
- `results.txt`: every number above, with how each was measured.
- `sizes\`: round 16's probe re-pointed; `sizes-aa3a3696.tsv` against `sizes-0df5e3ea.tsv`. Run `diff.py` with `.` as its third argument (the third argument is a filter).
- `aperture\` (ApProbe: `src\Program.cs`, `proj\ApProbe.csproj`, assembly named Aperture.Tests for the internals): `run-noart2.txt`, `run-noart-a11y0.txt`, `run-osc300.txt`, `run-new150.txt`, `run-art-osc100.txt`.
- `labels\` (`src\Program.cs`, `proj\Labels.csproj`): the micro, Junction file-walk and Vesper reader scenes. The prototype's numbers are in `results.txt`; the edit is undone.
- `jgc\` (`src\Program.cs`, `proj\JGc.csproj`, `big1.txt`, `history1.txt`): when the idle collection fires.
- `rw\` (`common.ps1` is expert 8's `common18.ps1` re-pointed, `batch-gc.ps1`, the copied `Driver*.cs`, `batch-g1.txt`, `runs\g1-*`), plus `locks.log` and `gate.log`.
- `fx\` (jrepo clone, bigrepo copies, viewer), `fedora-check.sh`, `fedora-check.txt`.

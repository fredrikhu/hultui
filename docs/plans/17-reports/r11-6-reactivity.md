SATISFIED: no

Setup: my worktree on dee48fbb, SDK 11.0.100, .NET 11.0.0 JIT Release, headless. I applied round 7's walk probe (`r11u\apps\probe-r7.patch`) and added counters of my own:
- a rebuild request the running build drops, by phase (before / Build / reconcile / OnAfterBuild) and by call site;
- what started each top-level build;
- each effect's dependency reads;
- VirtualList row layouts and skips.

I ran all 67 walks of the r11u walk host once (`batch.ps1`), on copied fixtures. Only counts and bytes are reported, no timings. The r2/r3 passes also record a stack trace for each top-level build. That inflates their KB, so I compare those passes only against each other. Afterwards I reversed everything with `git apply -R final11.patch`; the worktree is clean and the build server is shut down.

REVIEW: none.
- **Reactivity code in f4d6050d..dee48fbb:** only b2fdcf52 touches it. A kept key still costs one 24 B object; see EXAMINED for its finalizer.
- **2f7ab168 and Vesper's `UserScrolled` handler:** the delegate is cached (CommanderApp.Views.cs:612), so no build allocates one.
- **The .NET 11 upgrade:**
  - Source generators: 37/37 generator tests pass on SDK 11. A probe with 18 `[Reactive]` property types builds on both net11.0 and net10.0 at WarningLevel 9999 / AnalysisLevel latest-all, with 0 warnings in the emitted `P.Reactive.g.cs`.
  - Generator allocations after tier-1 warm-up (12 rounds × 40 calls, 120 ms sleeps): .NET 11.0.0 and 10.0.14 give the same bytes. A read, a write, a same-value write and a write that re-runs an Effect cost 0 B each for int, int?, bool, double, float, long, string, string?, enum, enum?, record struct, record struct?, (int, string), DateTime, Guid and object?.
  - Only structs without `IEquatable` box: 48 B a write on both runtimes (plain struct, `KeyValuePair<int,int>`). No signal type in the apps is such a struct: Color and Size are record structs.
  - Suite captures c177a16c, .NET 10.0.14 against 11.0.0, the 107 `component/`, `a11y/` and `input/` rows: allocKB 0 up and 7 down on JIT; 0 up and 0 down on AOT.

CANDIDATES:
- id: reactivity-meridian-map-list-draws-selection (absorbs Waiting virtuallist-selection-relayout; with expert 1)
  what: Meridian's Discovery map sets `.SelectionHighlight(Azimuth.Raised, Azimuth.Radius1)`, so the list draws the selection and a click makes no row again.
  evidence: disc-map-click, prototype `R11_MAPHL` (MeridianApp.Discovery.cs:598), probe on in both runs. Per click:

  | | 600 endpoints, before | 600 endpoints, after | 60 endpoints, before → after |
  |---|---|---|---|
  | KB a click | 30.8 | 22.2 (−28 %) | 26.4 → 19.6 (−26 %) |
  | Render KB | 8.4 | 0.0 | |
  | Rows made | 2 | 0 | |
  | Row layouts | 4 (35 rows skipped as clean) | 0 (list not laid out) | |
  | Layout visits | 70.2 | 20.3 | 57.8 → 16.2 |
  | Measure misses | 69 | 61 | |

  - Unchanged: the endpoint detail part, 1.0 build a click (build 13.3 + reconcile 7.8 KB, about 61 misses). That is new content and is needed.
  - Probe-free run (r1): 23.5 KB a click, so the same −8.6 KB is about −37 %.
  - Step 11's "869 measure calls" counted cache hits. Per click the list itself lays out only the 2 rows it remade (4 layouts, 8 misses) and skips the other 35. The framework relayout is cheap, so no framework change is needed.
  expected: churn −26…−37 % a map click, the same at 60 and 600 endpoints.
  cost: small, app only. Pixel impact: a selected row draws a 2 px accent on its left edge only (EndpointRow, `BorderWidth(2,0,0,0)`). `RowHighlight` has one border width for all sides, and `SelectionOverlay` sits at the row's end, so neither can draw it. The hover tint moves to `.HoverHighlight`. Readers are unaffected: the rows are not composite, so the list publishes the selection → NEEDS THE USER.

WAITING, RE-RANKED:
- **ondafterbuild-markdirty-dropped:** document it; make it work only if the user wants the behaviour change. It costs no builds today.
  - **Cause:** `OnAfterBuild` runs inside the build effect (ComponentHost.cs:60). `MarkDirty` → `Execute(force)` returns at Effect.cs:45, because the effect is still executing. A signal written there that `Build` read is also lost: `NotifyDirty` is ignored (Effect.cs:100), and the version snapshot is taken after the write.
  - **Measured:** 67 walks in 15 processes, all five apps, set-up included. 0 requests came from `OnAfterBuild`. All 17 drop sites are inside `Build`.
  - **Prototype `R11_AFTERDEFER`:** queues an `OnAfterBuild` MarkDirty like a mid-reconcile one. ForceRebuild's own drain runs it, or the frame's drain before layout (Window.cs:1729) does. It adds 0 builds in Vesper's and Meridian's walks.
  - **Semantics probe (`afterprobe`):**
    - a bounded retry of 3 gives 4 builds, as the code intends;
    - a signal written in `OnAfterBuild` is still dropped (that needs an Effect change);
    - an unconditional MarkDirty in `OnAfterBuild` gives 102 builds at once and +101 every frame. That runaway is hidden today and would need a per-frame cap.
  - **Vesper's `RestorePaneScroll` retry (CommanderApp.cs:305) has been dead code since 00b8fc79.** An offset set before layout now reads back, so `settled` is true. Once the content is measured the clamp is real, so a working retry would only add up to 3 useless view builds. Delete it.
  - **Fix cost:** one bool per host and one check per MarkDirty.
  - **Never extend the fix to MarkDirty during `Build`: Junction depends on that drop.** `TextSearch.Bind` → `Rerun` → `FindBar.SearchChanged` → `MarkViewDirty` runs inside every diff build. Drops a key: Changes 300 caret 0.95, big files 1.00, Files 0.45, session 0.30, commit files 0.10. Honoured, each diff build would ask for the next, up to DrainDeferred's guard of 100. Meridian does the same through a signal write (`ResponseDirty`), ignored 1.00 times a pretty/raw key.
  - **Docs:** REACTIVITY.md's lifecycle table should say that MarkDirty or a write to a signal the build read, from `OnBeforeBuild`, `Build` or `OnAfterBuild`, is dropped. Use `RequestPostFrameCallback` or `InvokeOnMainThread` instead.
- **virtuallist-selection-relayout:** not a framework candidate (35 of 39 realized rows skip). The app route above removes the rest.
- **aperture-cursor-guard-textarea:** not perf, 0 B either way, and harmless today.
  - The issue reply's view registers an empty cursor list (`SetCursorList("issue:"+id, [], 1)`, Issues.cs:339), so `HandleCursorKey` returns false.
  - The report message is behind the player branch, which tests both field types (ApertureApp.cs:499).
  - Fix with the next Aperture keyboard change: `and not TextAreaElement` at ApertureApp.cs:553.
- **junction-changes-leads:** still reader behaviour with no perf number. No round 10 Junction commit touched multi-selection or folder select, and the Changes list sets no `CanSelectMultiple`. It stays with expert 7.

EXAMINED, NOT WORTH IT:
- **`WeakElementRef` (b2fdcf52) derives from `WeakReference`:** as far as I know, the runtime skips the finalizer only for the exact `WeakReference` type. So each kept key is one finalizable object, which costs one finalizer run and one extra GC survival when the key is pruned. That is once a key (Sync at 3,000 files is 2,000 keys a rescan), not per build.
- **Dependency lists keep duplicate reads:** per build, Junction reads 10 sources (3 distinct, max 10), Vesper 3.8 (2.4 distinct, max 7), the Meridian map 1.0. The lists are reused, so the cost is 0 B.
- **Vesper folder in/out builds 4 times a key in one frame (CommanderApp 3, Part 1):**
  - Triggers: the navigation; the listing landing (CommanderApp.cs:667); a part built when a loader lands; `NudgeWhenConnectingIsDue` (:631), about 1.0 a key at the walk's 300 ms pace.
  - The first three are chained: the preview load starts in the build the listing triggers. Wrapping `DrainMainThreadQueue` in one batch would not merge them.
  - Lead for expert 8, cause not checked: the nudge tests only `Loader(side).IsLoading`, not that it is still the load it was set for. Fast repeated navigation may pay one view build (about 30–34 KB of 280–313 a key). Check it in a real window.
- **`AsyncLoader.Start` inside Aperture's build (`EnsureGridPage`):** writes its state signal during the build, once per process. Harmless, because the result triggers its own build.

NEEDS THE USER:
- **Meridian map selection drawn by the list (−26…−37 % a click):** choose between (a) a full 2 px accent border all round the selected row (pixels change), (b) no accent, only the Raised fill (pixels change), or (c) a new public `RowHighlight` with a border width per side (keeps the left-only accent). Before/after images: headless map at 600 endpoints with row 3 selected and hovered, base against `R11_MAPHL` (reapply the prototype line from `final11.patch`).
- **OnAfterBuild MarkDirty:** (a) documentation only, deleting Vesper's dead retry; or (b) honour it: deferred to the frame's drain, with a per-host cap per frame, never for `Build`. Either way it costs 0 builds in today's apps.
- **Possible stale find-bar count (not perf; read in the code, not reproduced):** Meridian builds `_responseFind.Build()` before the body binds the search (Response.cs:384, before `ResponseBody`). Junction does the same: Diff.cs:76 runs before `BindDiffSearch`. The re-run's "changed" is dropped, as measured above. So with the bar open, a pretty/raw switch or a new file may show the old "n of m" until something else builds. My one headless attempt, opening the bar through reflection, did not mount it, so this is still unconfirmed. Decide whether to check and fix it in FindBar (read the count after the bind, or put the bar after the body).

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\ec6d577c-f9ba-4479-acbd-908e67c0ea0a\scratchpad\r11-reactivity\`
- `final11.patch`: every probe edit (round 7's patch, drop/phase/trigger/dependency/row counters, `R11_AFTERDEFER`, `R11_MAPHL`). Apply it from the worktree with the full path.
- `walk\` (`src\Program.cs` adds the DROPS/TRIG/ROWS lines; `src\R9.cs` adds the `findstale` mode; `after\WalkHost.csproj`), `run.ps1`, `batch.ps1`, `batch2.ps1`, `batch3.ps1`, `summ.py`, `fx\` (copied fixtures).
- Outputs: `runs\after-jit-r1-*.out.txt` (67 walks), `runs\after-jit-t2-*` (map 600/60, folder in/out, Changes, `R11_AFTERDEFER` on Vesper and Meridian), `runs\after-jit-t3-*` (`R11_MAPHL`, findstale), `batch-r1.log`, `batch2.log`, `batch3.log`.
- Generator: `genprobe\src\Program.cs`, `genprobe\g11\GenProbe11.csproj`, `genprobe\g10\GenProbe10.csproj`, `run11.txt`, `run10.txt`, `build11.log`, `build10.log`, `gentests.log`.
- `afterprobe\Program.cs` and `afterprobe\AfterProbe.csproj` (OnAfterBuild semantics); `cmpcaps.py` (suite allocKB, .NET 10 against .NET 11).

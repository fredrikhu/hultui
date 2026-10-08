# Plan 17: The performance team

A team of eight performance experts works through HultUI in rounds. Each round they
look for time and memory wins in their area, the best ones are built and measured one at
a time, and the round ends with a review. When a whole round finds nothing worth doing,
the team writes `docs/PERFORMANCE.md`: guidelines for writing fast apps with the framework.

This file is the brief. The state of the work lives in
[`17-performance-team-ledger.md`](17-performance-team-ledger.md). The orchestrator (the
session running `/loop`) reads both on every tick, does the next step, and records it.

## The team

Every expert owns one area. A finding that crosses areas belongs to the expert whose code
changes; the others may comment on it in their review.

### 1. Layout

- **Owns:** style resolution, measure and arrange (flex, grid, text measure *calls*,
  measure caches), dirty propagation, `VirtualList` / `TableView` realization, scrolling,
  sticky and z-order, hit-testing.
- **Code:** `src/HultUI.Core/{Layout,Elements,Styling,Events}`.
- **Read first:** `docs/LAYOUT.md`, `docs/GRID.md`, `docs/DECISIONS.md`; memory
  `layout-leaf-measure-cache`, `layout-change-audit`, `hultui-grid-vs-flex-perf`,
  `junction-frame-profile`, `perf-p4-p5-measured`.
- **Known leads:** hit-testing a 1,000-child column is linear (~5 µs per move) and
  enter/leave allocates ~1 KB per move; `VirtualList` realizes rows inside the render pass.

### 2. SkiaSharp: painting

- **Owns:** `SkiaRenderContext` and the paint path, `SaveLayer`/clip use, reuse of
  `SKPaint`/`SKFont`/`SKPath`/`SKShader`, drawing text blobs, picture and layer caching,
  image decode and caching, rich text paint, the cost of each effect (shadow, blur,
  gradient, transform).
- **Code:** `src/HultUI.Rendering.Skia` (not the text services), `src/HultUI.Core/Rendering`.
- **Read first:** `docs/DECISIONS.md` (Rendering, damage rects); memory
  `opacity-layer-bounds`, `hultui-pixel-snapping`, `golden-images`,
  `junction-frame-profile`.
- **Known leads:** Render is 40 % of a Junction frame and only about half of that is
  painting.
- **Constraint:** golden images must stay byte-identical unless the change is meant to
  alter pixels; say so in the commit if it does.

### 3. GL and Vulkan

- **Owns:** the SDL3 window and GL context, `SkiaGLRenderSurface`,
  `SkiaVulkanRenderSurface`, `SdlVulkanHost`, `GRContext` resource limits,
  flush/submit/present, swap interval and frame pacing, the resize path, F16/HDR
  swapchains, video interop (mpv), start-up, GPU memory.
- **Code:** `src/HultUI.Platform.SDL3`, the surfaces in `src/HultUI.Rendering.Skia`,
  `src/HultUI.Media*`.
- **Read first:** `docs/design/HDR.md`, `docs/MEDIA.md`, `docs/ENVIRONMENT.md`; memory
  `vulkan-renderer`, `vulkan-default-decision`, `f16-swapchain-is-the-cost`,
  `swapchain-maintenance1`, `resize-stretch-fix`, `hultui-frame-pacing`,
  `startup-time-breakdown`, `hultui-wake-watcher-deadlock`, `mpv-sync-set-deadlock`.
- **Known leads:** Flush is 19 % of a Junction frame; loading the GL driver is ~132 ms
  of ~400 ms to the first frame.
- **Measures in a real window** (the headless suite rasterizes on the CPU):
  `SDL_WINDOW_ACTIVATE_WHEN_SHOWN=0` so the window never takes focus, `HULTUI_TRACE=<path>`
  for a frame trace (Flush = GPU stalls), `HULTUI_STARTUP=1` for start-up phases.

### 4. .NET runtime

- **Owns:** allocations per frame and per interaction, GC behaviour (gen-0 placement,
  LOH), boxing, closures, LINQ and iterators in hot paths, collection choice, struct
  layout and element size, NativeAOT and trimming, JIT tiering, P/Invoke and COM
  marshalling, UI-thread dispatch and wake-ups, leaks (handlers, subscriptions, unbounded
  caches). Reviews every other expert's change for allocations.
- **Code:** all of `src`; especially `src/HultUI.Core/Threading`, the interop files.
- **Read first:** `docs/NATIVE-AOT.md`, `docs/THREADING.md`; memory
  `perf-p4-p5-measured`, `perf-suite` (allocation traps), `hultui-twin-mount-leak`.

### 5. Text pipeline

- **Owns:** itemization, shaping (HarfBuzz), BiDi, ICU line and grapheme breaking, word
  and measure caches, font fallback, truncation and ellipsis, `TextArea` line layout,
  rich text layout, `TextMeasure`.
- **Code:** `src/HultUI.Text`; `SkiaTextShaper`, `SkiaTextServices`,
  `SkiaMeasureContext`, `SkiaFontCollection` in `src/HultUI.Rendering.Skia`; the layout
  side of `src/HultUI.Core/RichText` and `TextArea`.
- **Read first:** `docs/plans/12-rich-text-spans.md`, `14-bidi.md`,
  `16-rich-text-editor.md`, `samples/HultUI.Samples.TextBench/README.md`; memory
  `text-pipeline-perf`, `hultui-textarea-scroll-wrap`, `cjk-fallback-test-chars`,
  `rich-text-editor`, `junction-frame-profile`.
- **Known leads:** opening Junction's `TODO.md` still costs ~20 ms, mostly cold
  measuring of 89k characters; `TextArea` computes line starts in Render. The plain
  (non-BiDi) path has never run surrogates, emoji, CJK or ZWJ in production: widening it
  needs a BiDi-vs-plain equivalence corpus first.

### 6. Reactivity and reconcile

- **Owns:** components, `[Reactive]`, signals and effects, `Build`, `TreeDiffer`
  (keyed and unkeyed), mount, unmount and patch, persistent child components, the code
  the source generators emit.
- **Code:** `src/HultUI.Core/{Components,Reactivity}`, `TreeDiffer`,
  `src/HultUI.SourceGenerators`.
- **Read first:** `docs/REACTIVITY.md`; memory `hultui-keyed-siblings-gotcha`,
  `hultui-twin-mount-leak`, `hultui-dispatch-handler-pinning`, `junction-frame-profile`
  (whole-app rebuild per selection), `perf-p4-p5-measured` (P5a).
- **Known leads:** `MarkDirty` rebuilds synchronously, so several signal writes in one
  handler may rebuild several times; Junction rebuilds the whole app per selection.

### 7. Accessibility bridges

- **Owns:** the per-frame `AccessibilityNode` tree build and diff, the UIA provider
  (COM, events) and AT-SPI (D-Bus), coalescing, wrapper upkeep, property-change raising.
- **Code:** `src/HultUI.Core/Accessibility`, `src/HultUI.Platform.Accessibility.*`.
- **Read first:** `docs/ACCESSIBILITY-PLAN.md`; memory `uia-bridge`, `a11y-owner-slots`,
  `uia-provider-repositioning`, `junction-frame-profile`; the perf suite's `a11y/` group.
- **Known leads:** a11y is 20 % of a Junction frame. Open levers from memory `uia-bridge`:
  reference-keyed dictionaries over fresh nodes (`RuntimeHelpers.GetHashCode` 9.6 % of
  samples), wrappers built eagerly per diff, PropertyChanged raises not coalesced.
- **Constraint:** a screen reader must see the same tree and events. A UIA client is
  always listening on this machine; measure with the bridge on and with `HULTUI_A11Y=0`.

### 8. App workloads

- **Owns:** no code. Measures the real apps end to end on NativeAOT publishes —
  Junction (`JUNCTION_BENCH`, `JUNCTION_FRAMELOG` on branch `claude/render-probe`),
  Vesper Commander, Aperture, Meridian, Reel, the rich text editor — and splits each
  app's frame time, start-up and working set by area.
- **Job:** hands leads to the owning expert (a lead is a candidate with that expert as
  owner); checks that landed commits helped real apps and not only suite rows; proposes
  perf-suite scenarios for app patterns the suite does not cover.
- **Read first:** memory `junction-frame-profile`, `junction-branch-perf`,
  `startup-time-breakdown`, `benchmarking-workflow`; `scripts/aot-vs-jit-benchmark.ps1`;
  the `run-hultui-app` skill.
- **Reports shares, not times, in a survey:** the other surveys load the CPU, so it
  gives each area's share of the frame and counts; absolute times come from the build
  phase.

Memory files live in
`C:\Users\fredr\.claude\projects\C--Users-fredr-source-repos-fredrikhu-hului-revamped\memory\`.
They record what was true when written; check a named file, flag or commit still exists
before relying on it.

## What counts as a reasonable improvement

A candidate is worth building when it is **measurable** and clears **one** bar:

- **Time:** ≥5 % lower min on a perf-suite row, a Benchmark op or an app frame metric
  that stands for a real workload, seen in two idle-gated runs.
- **Memory churn:** ≥10 % lower allocKB per op on such a row, or a per-frame allocation
  removed from a steady-state frame (target: 0.1 KB per frame).
- **Retained memory:** ≥5 % lower managed heap or working set for a realistic tree, or
  GPU memory, or a leak closed (mount/unmount or open/close cycles that retain bytes).
- **Scaling:** removes O(n) work per frame or per event from a hot path, even if today's
  scenarios are small.
- **Start-up:** ≥10 ms off the time to the first presented frame.
- **Idle:** an idle window (nothing dirty, no animation, no pending deadline) wakes the
  CPU less often; target about 1 wake per second or fewer beyond the deadlines it owns.

It is **not** reasonable when it:

- saves less than the noise (see `perf-p4-p5-measured`: 5 ns per event is noise);
- breaks public API, changes behaviour, or alters pixels without a reason the user would
  accept — those go to **Needs the user** in the ledger;
- adds complexity out of proportion to the win, or a new native dependency;
- re-opens a decision in `docs/DECISIONS.md` or memory without new evidence. Declined so
  far: damage rects, handler bitmask and TreeDiffer/LRU pools (P4/P5), Vulkan as the
  default renderer, the net481 port, splitting Junction into components.

App code (`apps/*`) is in scope only where an app misuses the framework in a way the
guidelines should warn about. Fix the framework first.

## A round

The ledger's **Phase** field says where the round is.

### Phase `survey` (all experts in parallel)

Spawn the eight experts at once, each with `isolation: "worktree"` so their builds never
share `bin/` with each other or with this worktree. Each gets this brief, its own
section, the ledger, and the list of commits since the last survey.

Each expert:

1. Reviews the commits landed since the last survey, in all areas, for regressions its
   area would see (time, allocations, GPU work). Objections become candidates.
2. Looks for new candidates in its area. Profiles before guessing: count calls, measure
   allocations, read traces. Skips anything listed in the ledger as landed, declined or
   examined unless it has new evidence.
3. May build and run anything in its own worktree, but **reports only allocation
   numbers, call counts and complexity** from this phase. Eight surveys run at once, so
   their timings are noise; timings come from the build phase.
4. Returns, in this shape:

   ```
   SATISFIED: yes | no
   REVIEW: <objections to landed commits, or "none">
   CANDIDATES:
   - id: <area>-<short-slug>
     what: <one sentence>
     evidence: <numbers, file:line, scenario>
     expected: <which bar it clears, and by how much>
     cost: <small | medium | large>, <API or pixel impact, if any>
   EXAMINED, NOT WORTH IT:
   - <what>: <why, with the number>
   NEEDS THE USER:
   - <what>: <the decision needed>
   ```

   `SATISFIED: yes` means: no candidate in its area clears a bar.

The orchestrator merges the reports into the ledger, folds duplicates into one
candidate with one owner, ranks the candidates by expected win over cost, and sets Phase
`build`. If **every expert** said `SATISFIED: yes` and none raised a review objection, it
sets Phase `guidelines` instead.

The team may change. When a survey shows an area nobody owns, or one expert's area is
too big to cover in a round, the orchestrator adds or splits a role in this brief and
says so in the ledger's log.

### Phase `build` (one candidate at a time)

Take up to twelve candidates, best first; the rest stay queued for the next survey to
re-rank. For each, spawn **one** agent (the owning expert,
no worktree isolation, in this worktree) and wait for it before the next. Never build
or test here while it runs.

The agent:

1. **Baseline.** Builds Release, runs `scripts/perf/idle-gate.ps1`, then captures the
   rows that matter (perf suite `--json` with `--filter`, Benchmark `--profile`, a
   frame trace, or a purpose-built probe). Uses `--no-build` after the gate so the build
   is not inside the quiet window.
2. **Change.** Implements it in the style of the surrounding code.
3. **Tests.** Adds or updates a test that pins the behaviour and, where possible, the
   win (a call count, an allocation count, a cache hit). Checks the test fails with the
   change reverted by a reverse edit — never `git checkout --` — then restores it. Runs
   the affected test projects.
4. **After.** Idle-gated capture of the same rows, then the full perf suite against the
   baseline with `--compare`. Runs before and after alternately at least twice.
5. **Decides.** Keeps the change if it clears its bar and no row got slower beyond
   noise; otherwise reverts only the files it changed and records the numbers as
   "examined, not worth it".
6. **Commits** a kept change on this branch: one change per commit, subject
   `<Area>: <what it does>`, body with before → after numbers and how they were taken,
   ending with the co-author line from the session. Commits the captures as
   `samples/HultUI.Samples.PerfSuite/captures/<date>-<sha>-<slug>-{before,after}.json`.
   Updates `docs/DECISIONS.md` for anything declined with numbers, and the matching
   guide in `docs/` if the change alters what an app author should do.
7. Returns: kept or reverted, commit, numbers, anything learned that the guidelines
   should say.

After the last candidate, run the whole test suite once (`dotnet test HultUI.slnx -c
Release`; see the `testing` skill). A failure that appeared this round is fixed or its
commit reverted before the round closes. Then set Phase `release`.

### Phase `release` (the user asked for this on 2026-10-08)

Every round ends on main, with CI green, before the next round starts.

1. **Merge into main and push**, only after the full suite passed. Follow memory
   `merge-race-shared-checkout`: merge in this worktree, never by running git in the
   primary checkout. If main is checked out elsewhere: `git fetch`, `git switch --detach
   origin/main`, `git merge --no-ff -m "Merge branch '<branch>'" <branch>`. **Gate the
   push:** if `origin/main` moved after the full suite ran (another session pushed), the
   merged tree is not what was tested — build and run the suite on the merge first. Then
   `git push origin HEAD:main`, fast-forward the primary checkout only behind the guard in
   that memory, and `git switch` back to the branch. No tags.
2. **Wait for CI** on the pushed commit (`gh run list --branch main --commit <sha>`; memory
   `gh-cli-available`, `ci-red-findings`). Pace the checks to how long the workflows take;
   do not poll in a tight loop. The next round's survey starts only when every run for that
   commit has finished.
3. **CI red:** tell a known flake (memory `ci-red-findings`) from a real failure by
   re-running the failed job once. A real failure is fixed on the branch, re-tested, merged
   and pushed the same way, and CI waited for again. Never start the next round on red.
4. Record the merge commit and the CI result in the ledger, increment **Round**, set Phase
   `survey`.

### Phase `guidelines`

1. Spawn every expert in parallel (read-only, no builds). Each returns its section
   of `docs/PERFORMANCE.md` as text.
2. The orchestrator assembles the document. Shape:
   - **Measure first:** the perf suite, Benchmark, TextBench, `HULTUI_TRACE`,
     `HULTUI_STARTUP`, the DevTools FPS meter, `HULTUI_A11Y=0`, AOT vs JIT, the
     idle gate and P-core pinning.
   - One section per area: rules an app author can follow, each with the reason and a
     number from a perf-suite row, a commit or a capture. Short code samples that use the
     real API.
   - **Traps:** things that look cheap and are not, from the ledger and memory.
   - **Costs at a glance:** a table of common operations and what they cost.
3. Spawn every expert again to review the whole document for errors, missing rules and
   claims without a number. Apply their changes. Repeat until all approve.
4. Link it from `docs/toc.yml` (Guides) and `docs/index.md`. Commit.
5. Set Phase `done`.

### Phase `done`

Stop the loop (`ScheduleWakeup` with `stop: true`) and report to the user: the commits
with their numbers, what was examined and declined, the **Needs the user** list, and the
last merge into main with its CI result. The guidelines commit is merged and pushed like a
round, with CI waited for.

## Rules for everyone

- **Measure on an idle machine.** `scripts/perf/idle-gate.ps1` before every timed run;
  the user games on this machine and other sessions run tests. Allocation numbers do not
  drift with load.
- **Pin and warm.** The perf suite and Benchmark pin to P-cores and warm up by time
  (≥5 s). An external process: `$p.ProcessorAffinity = [IntPtr]0xFFFF`.
- **Compare mins** between captures; the median says the min was not luck.
- **Know the traps** in memory `perf-suite`, `benchmarking-workflow` and
  `layout-change-audit` (gen-0 GC placement, JIT object stack allocation moving allocKB,
  measure-cache hits from resizing between two widths, alternating two ops in one series).
- **Apps on AOT.** Measure app frames on a NativeAOT publish; JIT first visits cost
  5–10× more. AOT also allocates differently (`Type.Name` builds a new string every call;
  apps allocate ~2.2× more per frame than the JIT suite shows), so a change aimed at
  per-frame allocation checks its bytes on an AOT build too.
- **a11y state:** pin it with `HULTUI_A11Y=0` when comparing across OSes; measure it on
  when the change is in the a11y path.
- **Never take focus** from the user's desktop: no `SendKeys`, no foreground windows,
  no clicks in Junction; drive apps headlessly or through `JUNCTION_BENCH`
  (branch `claude/render-probe`).
- **One build at a time per worktree** (memory `concurrent-dotnet-test-trap`).
- **Create files with the Write tool**, not shell heredocs (they hang).
- **Commit on this branch only.** Agents never push, merge or tag. The orchestrator merges
  into main and pushes once per round, in Phase `release`; never tags.
- **Plain language** in commits and docs, as in the rest of `docs/`.

## When it ends

There is no round limit: the loop runs until every expert is satisfied in the same
survey. The one other way out is **a blocker only the user can clear**: when every
remaining candidate is in Needs the user, stop and ask; resume when the user answers.

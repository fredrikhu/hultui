# Plan 17: The performance team

A team of four performance experts works through HultUI in rounds. Each round they
look for time and memory wins in their area, the best ones are built and measured one at
a time, and the round ends with a review. When a whole round finds nothing worth doing,
the team writes `docs/PERFORMANCE.md`: guidelines for writing fast apps with the framework.

This file is the brief. The state of the work lives in
[`17-performance-team-ledger.md`](17-performance-team-ledger.md). The orchestrator (the
session running `/loop`) reads both on every tick, does the next step, and records it.

## The team

Every expert owns one area. A finding that crosses areas belongs to the expert whose code
changes; the others may comment on it in their review.

### 1. Layout and tree pipeline

- **Owns:** build and reconcile (`TreeDiffer`, components, signals), style resolution,
  measure and arrange (flex, grid, text measure calls, measure caches), dirty propagation,
  `VirtualList` / `TableView` realization, scrolling, sticky and z-order, hit-testing.
- **Code:** `src/HultUI.Core/{Layout,Elements,Components,Reactivity,Styling,Events}`.
- **Read first:** `docs/LAYOUT.md`, `docs/GRID.md`, `docs/REACTIVITY.md`,
  `docs/DECISIONS.md`; memory `layout-leaf-measure-cache`, `layout-change-audit`,
  `hultui-grid-vs-flex-perf`, `junction-frame-profile`, `perf-p4-p5-measured`.
- **Known leads:** hit-testing a 1,000-child column is linear (~5 µs per move) and
  enter/leave allocates ~1 KB per move; `VirtualList` realizes rows inside the render pass.

### 2. SkiaSharp: painting and text

- **Owns:** `SkiaRenderContext` and the paint path, `SaveLayer`/clip use, reuse of
  `SKPaint`/`SKFont`/`SKPath`, picture and layer caching, image decode and caching, text
  shaping and glyph caches (HarfBuzz, ICU), rich text paint, the cost of each effect
  (shadow, blur, gradient, transform).
- **Code:** `src/HultUI.Rendering.Skia`, `src/HultUI.Text`, `src/HultUI.Core/Rendering`,
  `src/HultUI.Core/RichText`.
- **Read first:** `docs/DECISIONS.md` (Text Rendering, Rendering, damage rects);
  memory `opacity-layer-bounds`, `text-pipeline-perf`, `hultui-pixel-snapping`,
  `golden-images`, `junction-frame-profile`.
- **Known leads:** Render is 40 % of a Junction frame and only about half of that is
  painting; `TextArea` computes line starts in Render.
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
  marshalling, UI-thread dispatch and wake-ups, the managed cost of the accessibility
  bridges (UIA, AT-SPI), leaks (handlers, subscriptions, unbounded caches).
- **Code:** all of `src`; especially `src/HultUI.Core/{Threading,Accessibility}`,
  `src/HultUI.Platform.Accessibility.*`, the interop files.
- **Read first:** `docs/NATIVE-AOT.md`, `docs/THREADING.md`; memory
  `perf-p4-p5-measured`, `uia-bridge`, `a11y-owner-slots`, `perf-suite` (allocation
  traps), `hultui-twin-mount-leak`.
- **Known leads:** a11y is 20 % of a Junction frame; the bridge's open levers are listed
  in memory `uia-bridge` (reference-keyed dictionaries over fresh nodes, eager wrappers,
  uncoalesced PropertyChanged).

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

### Phase `survey` (four experts in parallel)

Spawn the four experts at once, each with `isolation: "worktree"` so their builds never
share `bin/` with each other or with this worktree. Each gets this brief, its own
section, the ledger, and the list of commits since the last survey.

Each expert:

1. Reviews the commits landed since the last survey, in all areas, for regressions its
   area would see (time, allocations, GPU work). Objections become candidates.
2. Looks for new candidates in its area. Profiles before guessing: count calls, measure
   allocations, read traces. Skips anything listed in the ledger as landed, declined or
   examined unless it has new evidence.
3. May build and run anything in its own worktree, but **reports only allocation
   numbers, call counts and complexity** from this phase. Four surveys run at once, so
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

The orchestrator merges the four reports into the ledger, ranks the candidates by
expected win over cost, and sets Phase `build`. If **all four** said `SATISFIED: yes`
and none raised a review objection, it sets Phase `guidelines` instead.

### Phase `build` (one candidate at a time)

Take up to six candidates, best first. For each, spawn **one** agent (the owning expert,
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
commit reverted before the round closes. Then increment **Round**, set Phase `survey`.

### Phase `guidelines`

1. Spawn the four experts in parallel (read-only, no builds). Each returns its section
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
3. Spawn the four again to review the whole document for errors, missing rules and
   claims without a number. Apply their changes. Repeat until all four approve.
4. Link it from `docs/toc.yml` (Guides) and `docs/index.md`. Commit.
5. Set Phase `done`.

### Phase `done`

Stop the loop (`ScheduleWakeup` with `stop: true`) and report to the user: the commits
with their numbers, what was examined and declined, the **Needs the user** list, and that
nothing is pushed or merged yet.

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
  5–10× more.
- **a11y state:** pin it with `HULTUI_A11Y=0` when comparing across OSes; measure it on
  when the change is in the a11y path.
- **Never take focus** from the user's desktop: no `SendKeys`, no foreground windows,
  no clicks in Junction; drive apps headlessly or through `JUNCTION_BENCH`
  (branch `claude/render-probe`).
- **One build at a time per worktree** (memory `concurrent-dotnet-test-trap`).
- **Create files with the Write tool**, not shell heredocs (they hang).
- **Commit on this branch only.** No pushes, merges or tags; the user decides at the end.
- **Plain language** in commits and docs, as in the rest of `docs/`.

## Stopping early

- **Round 10** without the team satisfied: write the guidelines anyway, list the
  remaining candidates under **Open** in the ledger, and stop.
- **A blocker only the user can clear** (every remaining candidate is in Needs the user):
  go to `guidelines`, then stop and ask.

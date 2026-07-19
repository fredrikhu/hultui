# Session handoff — headless mode (2026-07-18)

Temporary document. Delete once absorbed.

## Status

All work is pushed. `main` @ `da62e1b`, working tree clean, nothing local left behind.

```
da62e1b Bring TODO.md up to date with the headless work
d01e12c Move CI actions off the deprecated Node 20 runtime
7783411 Build the shared graph once on the Linux CI leg
8d918d8 Guard the two Windows-only call sites the analyzer flags
ff7e6ff Fix snapshot baseline comparison against a CRLF checkout
eb20693 Document display-list snapshot testing in HEADLESS.md
daf48dd Stop scroll view content collapsing instead of scrolling
4e2652a Pin down layout bounds vs painted output
804271e Display-list snapshot testing via a recording render context
94d187f CI: Linux headless test leg; headless docs
bbe2612 Headless query API over accessibility-tree snapshots
9aa21f2 Headless input driver: clicks, keys, text, wheel, drag
+ 5 more (M1–M4: platform abstraction, raster surface, session/pump)
```

Local verification at that commit: 602 tests green (496 Core + 1 skipped, 42 Headless, 31 Text, 20 Skia, 7 TodoList, 6 SourceGen), plus 14 FlaUI tests on Windows. Build clean apart from one pre-existing `CS9191`.

## Check this first

Two CI runs were queued behind the last pushes and **had not reported when the session ended**:

1. **Windows leg** — should now be green. It was failing on `RenderLogTests.DisplayList_MatchesBaseline`, fixed in `ff7e6ff`.
2. **Linux leg** — `7783411` swapped it to `dotnet test HultUI.CrossPlatform.slnf`. That structural workflow change has **never been exercised by a real run**. The `.slnf` was verified on Windows (one shared build instead of six, `UiTests` excluded), and paths use forward slashes so they are safe on Linux — but the run itself is unconfirmed.

If the Linux leg fails, the `.slnf` is the first suspect. Fallback is the previous form: six `dotnet test tests/<project>` lines, which is known-good.

## What exists now

See `docs/HEADLESS.md` (usage) and `TODO.md` (what is done / next / deferred). In brief: `HultUI.Headless` runs apps with no window, GL, or display — raster Skia surface, manually pumped frame loop, virtual clock, input injection through the real event funnel, queries over accessibility-tree snapshots, and display-list snapshot recording.

## Decisions and dead ends not recorded in the repo

**Do not change the `FlexShrink = 1` default.** Fixed-height items shrinking in a bounded overflowing column is correct CSS — that is why `flex-shrink: 0` is a common idiom. The bug that was fixed was narrower: a scroll container's indefinite main axis not propagating to nested containers.

**The min-content clamp was attempted and reverted.** Do not approximate CSS `min-height:auto` from the flex Phase 1 measurement. For a container that measurement is its full content extent, not its min-content size, so an ancestor row wrapping a scroll view gets pinned to the entire scrollable height — measured at 14,640px tall and 1007 draw calls instead of 47, collapsing the viewport and defeating culling. All 497 unit tests passed with that bug present; only the benchmark caught it. A correct implementation needs a real recursive min-content pass where scroll containers contribute zero. `FlexShrinkTests` has a skipped case describing it.

**Add a shrink-heavy benchmark before attempting that.** The current suite barely exercises the shrink path (`_body` has `FlexShrink(0)` and sits in an unbounded axis), so it will not measure the cost of a min-content pass.

**Golden images were deferred deliberately**, in favour of display-list snapshots — readable diffs, git-friendly baselines, and no font-determinism problem. The raster surface exposes `Snapshot`/`ReadPixels`, so pixel goldens are unblocked whenever wanted; they need an embedded pinned font first.

**Existing `FlexShrink(0)` calls are now redundant** wherever they were working around the scroll collapse (the Benchmark sample's `_body`, likely Junction too). Harmless, but misleading to read.

## Traps hit this session

- **Baselines in raw string literals** carry the *source file's* line endings. With `autocrlf` that is CRLF on Windows and LF on Linux, so a baseline can pass on one CI leg and fail on the other. Normalise with `ReplaceLineEndings("\n")`. `RenderLog.ToString()` and the a11y dump always emit `\n`.
- **`TimeSpan.FromSeconds` rounds to whole milliseconds** — use `FromTicks` for frame deltas.
- Inside the `HultUI.*` namespace hierarchy, bare **`Text` binds to the `HultUI.Text` namespace**, not the builder. Alias `using Text = HultUI.Core.Builders.Text;` *inside* the namespace declaration; at the top of the file it does not win.
- **`git stash push` with an untracked file in the pathspec fails entirely**, stashing nothing — an A/B benchmark then silently measures the same code twice. Stash only tracked files.
- `grep "warning CS"` **does not match `warning CA1416`.** Two platform-analyzer warnings were visible on every local build and went unnoticed all session because of that.

## Benchmarking (how the layout bug was caught)

`samples/HultUI.Samples.Benchmark --auto` runs a js-framework-benchmark suite and exits; `--profile` gives a per-pass breakdown (build/style/layout/render/present). **Any layout-engine change should be A/B'd with it** — unit tests did not catch a viewport-collapsing regression.

Compare **within a single session only**; cross-session numbers are noise. On Windows, `SDL_WINDOW_ACTIVATE_WHEN_SHOWN=0` opens the window without stealing focus. On Linux this needs a display (or an offscreen SDL video driver) — the benchmark is not headless.

## On Linux specifically

- `tests/HultUI.UiTests` (FlaUI) is `net10.0-windows` and cannot run. It is excluded from CI on both legs; the headless suite is what gates interaction coverage.
- `dotnet test HultUI.CrossPlatform.slnf -c Release` is the full cross-platform suite.
- The headless suite needs no display, GPU, or X11/Wayland, and provably never loads the SDL3 native library (there is a test asserting it).
- Skia and HarfBuzz Linux native assets are already referenced and confirmed working on `ubuntu-24.04`.
- System fonts differ from Windows, so never assert pixel-exact or text-dependent absolute positions across platforms. `HeadlessOptions.DeterministicRecording()` uses `FixedMetricsMeasureContext` to make snapshot baselines OS-independent.

## Suggested next steps

1. Confirm both CI legs are green (see above).
2. **Linux AT-SPI2 per-object D-Bus export** — the top open item in `TODO.md`, and now much more tractable: `HeadlessWindow.InstallAccessibilityBridge()` turns on the real per-frame build/diff path and records the `TreeDiff`s a bridge consumes, so `ApplyChanges`' diff → signal mapping is unit-testable with no bus and no desktop. You are about to be on Linux, which is also where `accerciser`/`orca` verification has to happen.
3. Optional cleanups: remove now-redundant `FlexShrink(0)` calls; the pre-existing `CS9191` in `SkiaRenderContext.cs:541`; the `// TODO: The other units need to be able to be resolved?` in `Dimension.cs:37,47`.

## Memory that does not transfer

Assistant memory lives under `~/.claude/projects/<path-derived-key>/memory/` and is keyed by project path, so the Windows notes will not be visible from Linux. The durable facts are reproduced above and in `TODO.md`. The Windows-side files, if you want to copy them manually, are:

`benchmarking-workflow.md`, `headless-testing.md`, `flex-shrink-scroll-collapse.md`, `snap-layouts-caption-band.md`, `dialog-followups.md`, `junction-redesign.md`, indexed by `MEMORY.md`.

The implementation plan is at `~/.claude/plans/sprightly-puzzling-bear.md` (Windows-side only).

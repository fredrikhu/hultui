# Round 10 survey, expert 2: SkiaSharp painting

SATISFIED: no

I measured at 3462f18e, headless and JIT, counting calls and bytes only (no timings). I opened no real windows, so no
monitor was involved. Round 8's paint probe applied again: `probe-r8-tracked.patch` plus `PaintProbe.cs`, with the
three hunks that collide with round 9's line cull re-applied by hand and a count added in `TryDrawImage`. I also tried
one env-gated prototype, `PROBE_LINESEEK`. The whole diff is saved as `probe-r10.patch`, then undone with
`git apply -R`; PaintProbe.cs was deleted and the worktree is clean. Round 7's app fixtures are gone (`r7-apps\fx` is
empty), so I made new Vesper fixtures in `r10-painting\fx`. Meridian's coll25 is lost, so the Request tab was not
re-driven; the 2,000-line suite row stands in for it.

REVIEW: none. Round 9's paint commits hold:
- **Suite, all 214 rows shared with round 9:** every paint counter matches round 9's `suite-textcull.txt` run within
  5 % (`cmp.py`, `ab-r9-r10.txt`).
  - The 7 new rows:
    - `text/wheel over a 2,000-line plain label`: 1,999 lines visited a step, 1,939 culled, 60 drawn, 3.0 KB.
    - `paint/400 faded icons`: 400 images drawn, 400 faded leaves, 0 layers, 0 B.
    - The kept image grid: 19.2 images and 0 layers a step; the own-row variant allocates 185.9 KB, the kept one
      23.5 KB.
    - The viewer drag rows: 0.06 / 0.38 KB.
    - The minified TextArea row: 49 lines, 38 KB (its first draw).
- **b4f182b5 / 3a08f828:** both `DrawText` overloads skip off-clip lines before reading their runs. The probe counts
  1,836 lines a step whose runs are never made.
- **f9a7cc74 (faded images), checked with vdrv at 1240×820:**
  - Detail, every entry cut: 26.6 images fade themselves, 0 layers (round 9: 26.6 layers). Images drawn 79, as when
    nothing is cut.
  - Brief: 61 → 0 layers, images drawn 111 either way.
  - Code check:
    - A GPU or pixel source returns false and keeps its layer.
    - A non-Skia source draws nothing, as `DrawImage` does.
    - The alpha byte is computed as `PushOpacity` computes it (`(byte)(opacity * 255)`).
    - A transform is popped on the early return (ElementRenderer.cs:73-78).
- **8a6d7bb7 (Sync's Equal rows):** with Show equal on, 0 layers on Down/Up, wheel and hover (round 9: 26.2 / 23.1 /
  20). The colours are set at build time, so render is 0 B on Down/Up and hover. The wheel's 0.43 KB a frame is rows
  being realized.
- **Cut thumbnails:** 24 layers for 24 tiles, as the user decided (question 22).
- **58aeb965:** a frame with nothing to release reads one volatile count (`AnyToRelease`). `WebViewElement.Render` adds
  one `NeedsShowing` read. No paint cost.
- **ae32c9d8 / bf74fbca:** same-colour tokens are still merged into one span (SyntaxHighlighter.cs:604-608). Only 2,335
  of 1.75 M corpus lines change colour (Dark+/Light+). No change in the number of colour stretches to draw is expected.
- **Other commits:** only b4f182b5, f9a7cc74, 58aeb965 and 7e964aeb touch `src/HultUI.Rendering.Skia` or
  `src/HultUI.Core/Rendering`; 7e964aeb (the held-resize post) is expert 3's area. No commit touches HultUI.Terminal.

CANDIDATES:
- id: paint-text-lines-seek
  what: `DrawText` (both overloads) and `DrawParagraph` find the first line the clip can reach by binary search on the
  line tops, and stop at the first line wholly below the clip, instead of testing every line of a label on every frame.
  evidence: Prototype `PROBE_LINESEEK` (`__Seek` in `probe-r10.patch`):
  - Rule: start at the first line whose top is at least clip.Top − 2 × the layout's tallest line; stop at a top past
    clip.Bottom + the tallest line.
  - It is exact. `LineOutsideClip` (SkiaRenderContext.cs:989) already counts every skipped line as outside.
  - A layout whose line tops are not sorted falls back to the scan.
  - `text/wheel over a 2,000-line plain label in a ScrollView`: line visits a step 1,999 → 60, plus 11 search probes;
    lines culled one by one 1,939 → 0.
  - `richtext/type in a paragraph of 600 spans`: 77 → 41 visits, plus 7 probes.
  - Meridian's Request tab with the 89k body (round 9's counts): 1,906 lines visited a frame for about 190 on screen,
    so about 200 with the seek.
  - Rows of 2–10-line labels show 22–56 search probes a step with no visit saved. The build should only search labels
    of about 32 lines or more.
  - Prototype artefact: it keeps the tallest line in a `ConditionalWeakTable`, which allocates on each new layout
    (bidi wrap +0.28 KB, table wheel +0.59 KB). Stored on `TextLayout` / `PipelineTextParagraph` it allocates nothing.
  - Pixels: `HultUI.Rendering.Skia.Tests` pass 387/387 with the seek on, including LabelLinesCullTests' 27 glyph-count
    and byte-for-byte pixel cases.
  expected: Scaling. A long label's per-frame line work goes from O(lines) to O(log lines + visible). Each skipped visit
  is a cheap compare, so the time win on today's rows is likely small; the build phase should time it with
  `step-ab.ps1` on the 2,000-line row. No allocation change.
  cost: small, about 30 lines plus an internal float and bool on `TextLayout` and `PipelineTextParagraph` (+8 B a
  cached layout). No API change; pixel-identical by construction.

WAITING, RE-RANKED:
- terminal-noop-scrollback-wheel: unchanged, and no HultUI.Terminal commit this round.
  - Re-measured with R8Probe: a no-op notch still draws one whole frame, 5,440 glyphs at the bottom and 5,682 at the
    top, 836 / 882 draws, 112 B.
  - The repaint comes from `ScrollViewport` (TerminalElement.cs:566) even when the offset is clamped.
  - Only the terminal sample uses HultUI.Terminal. Keep low.
- terminal-row-strings-by-content: unchanged. A streamed line costs 10,276 B of render, all in `TerminalElement`, with
  55 draws and 1 blob made. Same reason; keep low.

EXAMINED, NOT WORTH IT:
- **Steady-state render bytes in the suite:**
  - The large ones are all VirtualList realization inside the render pass, which is by design: `TableView 10k sort`
    234 KB, typing beside a 150-line diff 249 KB, `list 100k replace` 143 KB.
  - Each is `~UpdateContentSize:VirtualListElement` (the probe's by-type breakdown).
- **Effects in the suite:**
  - Shadow and gradient tiles: 0 blur or shader cache misses, 0 B.
  - Layers appear only in scenarios built for them or in transient fades: toasts and the tween row 18 a step over
    19–20 frames, the video bar fade 28, `paint/tiles transform + opacity` 72.
  - Rounded clips are at most 24 a frame (spinners, Aperture-style cards).
- **Image memory:**
  - Vesper decodes thumbnails at their target size (ImageDecode.cs:91-99).
  - `SkiaImageSource` makes its bitmap immutable, so `SKImage.FromBitmap` shares the pixels instead of copying them
    (SkiaImageSource.cs:45-57).
  - No retained-memory lever.
- **TextArea:** it draws only the visible rows, from scroll offset divided by line height (TextAreaElement.cs:2008), so
  it has no line scan.
- **Thumbnails cut tiles:** 24 layers, declined by the user (question 22); no new evidence.

NEEDS THE USER: none.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\3484c586-c631-4af0-957d-95e7a03cac41\scratchpad\r10-painting\`
- Probe:
  - `probe-r10.patch`: round 8's probe re-applied to 3462f18e, the `seekProbes` counter, and the `PROBE_LINESEEK`
    prototype. Copy `PaintProbe.cs` to `src/HultUI.Core/Rendering/` first, then `git apply` the patch from the
    worktree.
  - `PaintProbe.cs`.
- Scripts: `cmp.py`, `newrows.py`, `rawdiff.py` (round 9's), `top.py` (ranks suite rows by one counter),
  `run-drivers.ps1`.
- Drivers, retargeted to this worktree:
  - `probe\`: R8Probe, for the terminal wheel and stream.
  - `vdrv\`: Vesper, now on the `fx\` fixtures.
  - `mdrv\`: Meridian; its coll25 fixture is gone, so it was not run.
- Fixtures: `fx\` (Core's Elements folder, docs, 24 PNGs).
- Outputs:
  - Suite: `suite-probe-r10.txt`, `ab-r9-r10.txt`, `suite-seek-off.txt`, `suite-seek-on.txt`.
  - Tests with the seek on: `skia-tests-seek.txt`.
  - Probe runs: `terminal-wheel.txt`, `terminal-stream.txt`, `vesper-sync.txt`, `vesper-views.txt`.
  - Build logs and `commits.txt`.

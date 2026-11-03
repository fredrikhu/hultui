# Round 7 survey, expert 2: SkiaSharp painting

SATISFIED: no

Measured at 57e6547c, headless on JIT; counts and bytes only. A render-pass probe (round 6's, ported) over all 190
suite rows, compared with round 6's output. R7Probe modes: `table` (the RichTextTable sample's 300×10 document per
frame at each depth), `walk` (long wheel walks), `wheel` and `stream` (terminal). An emulation of Skia's GPU text-blob
cache, counted per draw, with three models: the blob map as it is (one blob per face, size and text); the same with
the baseline's device y snapped to a whole pixel; one blob per run object (e624b076's keying). Prototype `PROTO_SNAP=1`
switches the snap on in the build. Probe patch reverse-applied; worktree clean.

## REVIEW

None. All 43 rows that differ from round 6's probe output went down in bytes.

- **8b6c3950 (blob key by words' text):** no row composes a glyph array to look a blob up (0 on all 190 rows).
  text/wrap 397 → 67 KB/op together with step 14 (render 72.7 → 14.1 KB); scroll/jump render 10.5 → 3.0 KB. Glyph
  compares remain only on BiDi rows (right-to-left runs are not marked, by design): styled bidi wrap 1,937 and styled
  bidi ellipsis 1,206 per op, 0 B.
- **afa7116d / 61b538c3 (lazy line runs):** runs moved to first draw, as recorded. Render bytes rose where runs are now
  made — styled wrap 8.1 → 17.1 KB, styled ellipsis 12.0 → 24.1, multi-font 33.0 → 36.2 — while their totals fell
  −48 %, −47 % and −81 %. The table wheel's 0.93 KB a notch is 6 lines' runs made on first draw (each suite round is a
  fresh session, so revealed rows are new). Steady redraw rows 0 B. Runs made by `TextPaintBounds`: 0 on every row;
  runs made for off-clip lines in `DrawText`: 1 per op, on text/wrap only.
- **49634ea5 (table grid lines):** 31–33 rectangles a frame at every depth.
- **eac5f19b, 9947adaf, d8e28f93, 3a8e6c9e:** 0 B in steady frames. Find Enter 0.27 KB render bytes. Sticky rows
  +0.37 KB a wheel step (20.16 vs 19.79).

## CANDIDATES

- id: paint-text-baseline-whole-device-pixel
  what: draw each text blob and sub-blob at a baseline whose device y is a whole pixel when the canvas matrix has no
  skew or perspective, so Skia's GPU text-blob cache stops keeping one entry per fractional y a shared blob was ever
  drawn at, and scanning those entries on every draw.
  evidence:
  - **Mechanism (from Skia's source as recalled; the build should confirm):** `TextBlobRedrawCoordinator` keys a
    direct-mask GPU text blob by (SkTextBlob id, fractional device origin); `BlobIDCacheEntry::findBlobIndex` scans
    each id's list linearly; an entry dies only when its SkTextBlob is released or the cache's budget purges it.
  - **Why lists grow in HultUI:** the run-blob map shares one SkTextBlob per (face, size, text) (since b688db76) and
    keeps every blob drawn in the last two frames, so a text drawn at many fractional y positions collects an entry
    per position: TextArea lines, rich text blocks, wrapped lines, table cells.
  - **It explains richtext-table-mid-depth-hump.** Emulated cache entries scanned per frame in the table at 1.5×
    (step 7d's window scale), depths visited 0→25→50→75→100 as that probe did:

    | Depth | 0 % | 25 % | 50 % | 75 % | 100 % |
    |---|---|---|---|---|---|
    | Entries scanned per frame | 421 | 1,613 | 2,337 | 2,733 | 1,081 |
    | Step 7d render, mean of 3 tip runs (ms) | 0.139 | 0.164 | 0.182 | 0.186 | 0.156 |

    Extra render time over extra entries scanned: 21, 22, 20 and 26 ns an entry. Everything else flat by depth:
    paragraphs 190–210 (they track rows on screen), draws 206–221, 0 B and 0 blob misses a frame, rectangles 31–33.
    With y snapped: 253 / 290 / 316 / 315 / 276 entries scanned (~1.3 a draw); cache entries kept 1,031 → 342.
  - **Long walks**, 200 notches down at 1.5×:

    | Walk | Draws a frame | As it is: scanned a frame | New GPU blobs a frame | Entries kept | Snapped: scanned | Snapped: new | Snapped: kept |
    |---|---|---|---|---|---|---|---|
    | Table | ~210 | 819 → 5,815 | ~10 | 2,206 | 285–310 | 3.2 → 0.5 | 440 |
    | JSON-like TextArea, 13 px monospace | 52 | 170 → 1,217 | 2.6 | 581 | 51 | 1.4 (the blob misses) | 320 |

    The JSON walk gives the same numbers at 1.0× (the line height itself is fractional). A VirtualList with whole-pixel
    rows (height 26, at 1.25× and 1.5×) scans 1 entry a draw and does not grow: typical lists are unaffected.
  - **Keying by run identity would not fix it:** layout copies share runs, so that model also grows, 607 → 3,742 a
    frame on the table walk. Why e624b076 was flat (likely): its map was keyed by run identity and emptied whole at
    2,048 entries; the HUD's changing text filled it, so every GPU entry was dropped regularly.
  - **Prototype (`PROTO_SNAP`, ~6 lines in SkiaRenderContext):** at the blob and sub-blob draw sites in `DrawRun` and
    `DrawRunPart`, `y = (MathF.Floor(m.ScaleY*y + m.TransY + 0.5f) - m.TransY) / m.ScaleY` with
    `m = Canvas.TotalMatrix`, only when SkewX/SkewY/Persp are 0. Pixel-identical because Skia places direct-mask glyphs
    at floor(device y + 0.5) anyway; subpixel positioning is along x only. Perf-suite frames (190 rows × 7 frames,
    512 MB glyph cache) at 1.0× and 1.5×: A/A 0 of 1,330 differ, A/B 0 of 1,330, both scales; BiDi, emoji, span,
    terminal and rich text rows included.
  expected: scaling bar — text-blob cache work per frame that grows with the distance scrolled becomes ~1 entry a draw.
  Time bar on a real GPU window frame: the table at 75 % ~0.186 → ~0.14 ms render, the whole hump (~−20 % render,
  ~−11 % frame at that rate). Long TextArea sessions gain ~0.12 µs a frame per notch scrolled, until Skia's budget caps
  the cache (4 MB by default, as recalled). Fewer GPU blob rebuilds per scroll frame (table 10 → ≤ 3, JSON 2.6 → 1.4).
  Retained native memory: cache entries kept −45…−80 %.
  cost: small, no API change, pixels identical in the raster A/B. The build must A/B in-process GL and Vulkan captures
  (A/A first), and time the RichTextTable sample at each depth and a TextArea wheel walk in a real window (expert 3's
  guarded harness). Read the matrix once per line or track it managed-side (the prototype read `Canvas.TotalMatrix`
  on every draw, ~206 P/Invokes a frame in the table). Glyphs with fractional y offsets (shaped marks) could move
  1 px; none did in the suite.

## WAITING, RE-RANKED

- richtext-table-mid-depth-hump: explained and folded into paint-text-baseline-whole-device-pixel. Not 49634ea5's grid
  lines (31–33 rectangles at every depth), not the per-paragraph clip test (flat by depth).
- terminal-noop-scrollback-wheel: unchanged, 1.00 frame a no-op notch — a whole terminal frame (5,440–5,682 glyphs,
  836–882 draws, 12 listings). Low: only the terminal sample uses HultUI.Terminal.
- terminal-row-strings-by-content: unchanged, 10,812 B render bytes a streamed line (was 10,276), all in
  TerminalElement. Low, same reason.

## EXAMINED, NOT WORTH IT

- `Canvas.LocalClipBounds` per `DrawParagraph` (b688db76's clip test): 190–210 P/Invokes a frame in the table, flat
  by depth — a few µs, a few percent of render.
- Steady render bytes: 0 B on hover, redraw, paint/ and terminal forced frames; 0.24 KB a frame on media rows is the
  known video path.
- Layers: opacity only (toast, fade, tween, tiles, dialog). The new `a11y/type in a rich text editor inside a
  VirtualList row` rows draw 1 layer per keystroke, bounded.
- Rounded clips: 21 per step on the image grid, 24 on spinners; GPU work.

## NEEDS THE USER

None, unless the build's GPU A/B shows a pixel difference.

## FILES

Scratchpad `r7-painting\` of session 855f4ddd: `probe-r7.patch` (all probe edits plus `PROTO_SNAP`; creates
`PaintProbe.cs`); `apply_probe7.py`, `apply_probe7b.py`, `apply_probe7c.py` (Ganesh emulation), `apply_probe7d.py`
(snapped model), `apply_proto_snap.py`; `probe\` (R7Probe) and `pix\` (frame-hash probe) with `pixruns.ps1`,
`pixcmp.py`; outputs `suite-probe-r7.txt`, `ab-r6-r7.txt`, `suite-gpu.txt`, `table-tip.txt`, `table-gpu-1.5.txt`,
`table-gpu-1.5-snap.txt`, `walk-{table,json,list}-1.5.txt`, `walk-json-1.txt`, `walk-list-1.25.txt`,
`walk-{json,table}-1.5-snap.txt`, `terminal-wheel.txt`, `terminal-stream.txt`, `pix-{all,s15}-{b0,snap}-{1,2}.txt`.

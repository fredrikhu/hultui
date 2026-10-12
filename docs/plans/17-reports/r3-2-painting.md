# Round 3 survey: 2. SkiaSharp painting

Round 2 re-measured with a render-pass probe (bytes per element type; glyph, blob, layer and path
counters), JIT and AOT, at 5d9be15. Probe reverted; worktree clean. Probe patch, driver and scripts in
the session scratchpad `r3paint/`.

SATISFIED: no

REVIEW: none.

- Steady and hover frames allocate 0 B in the render pass, JIT and AOT: Junction diff (Changes: 22
  spanned lines, 31 paragraphs), Vesper reader, Aperture item page on the real library (Eternals: hero,
  2 cached scrims, 30 images), Junction History. A whole hover frame is 0.22 KB (input events).
- Wheel frames are list realization (layout's): Junction diff 12.2 KB/frame (AOT 12.0), realization
  12.2 KB, 1 new paragraph 0.31 KB; Vesper reader 16.8 KB/frame (AOT 16.4), realization 16.25 KB,
  1.65 paragraphs 0.53 KB.
- 8d30d31: 0 shader or blur misses after the first frame.
- 103f30e: correct as landed. AOT still pays 56 B more per new blob than JIT, as before 103f30e (see
  paint-raw-run-buffer).
- bf5adcc: 0 paragraphs laid out per hover frame.
- ca59c6d / 545d1b6: paint side unchanged. The item-row walk does 34 text draws, 17 images and 2 new
  blobs per step; its 18.6 KB/step is 18.2 KB of realization.
- 6cf4fe6 / 17e94b0: the hero draws at ~1.0 source px per device px, so Skia builds no mipmaps.

## Candidates

- **paint-spanned-line-sub-blobs** (re-ranks waiting paint-spanned-line-glyph-work; part (b),
  prototyped). Draw each colour stretch of a spanned line from a kept sub-blob holding only the glyphs
  whose ink can reach that stretch's clip. `DrawLineSegment` (SkiaRenderContext.cs:917-940) draws every
  overlapping run once per stretch (:932-937). The prototype finds the glyph range by binary search on
  glyph X with the face's bounding box ± 1 unit, keeps one sub-blob per (run, g0, g1), and draws the
  whole run when X goes backwards or the range is the whole run. Glyphs handed to Skia per frame:
  Junction diff hover 24,527 → 3,420 (−86 %), wheel 26,918 → 3,550; Vesper reader hover 10,881 → 2,637
  (−76 %), wheel 14,240 → 3,033; text/selectable viewer hover 22,587 → 4,230 (−81 %), wheel 23,498 →
  4,330; text/spans recolor 68,647 → 7,392 (−89 %); text/spans bold 29,900 → 5,398 (−82 %); JSON wheel
  3,883 → 1,497 (−61 %); Terminal keystroke frame with 6 Powerline lines 3,330 → 948. Glyphs hashed per
  frame (SameGlyphs, :83-94) fall with it (selectable viewer hover 22,587 → 77). Pixels byte-identical
  on 8 headless frames (Junction diff and Vesper reader, scale 1 and 1.5, before and after a wheel);
  Terminal and emoji pages at 1.5 identical too. Cost with managed SKTextBlob sub-blobs: selectable
  viewer wheel 3.3 → 14.2 KB/op JIT (AOT 4.0 → 14.5), richtext wheel 0.6 → 3.4, richtext 600 spans
  +4 %; other text rows and all hover rows unchanged. With native handles the selectable viewer wheel
  reads 9.8 KB/op; the rest is harness cache-map growth (fresh session each round), removed by presized
  maps. Expected time ≥5 % on selectable viewer hover/wheel, spans recolor/bold, JSON wheel, Junction
  diff and Vesper reader frames (not timed). Medium; no API impact; pixel-identical by construction.
  Still needed: a pixel test that does not draw text as outlines (the goldens do), a GPU readback check,
  a fallback to advance or em bounds for faces with an empty bounding box (colour emoji).
- **paint-native-blob-handles.** Keep run blobs and sub-blobs as native handles
  (`sk_textblob_builder_make`, `sk_canvas_draw_text_blob`, `sk_textblob_unref`, all exported by
  libSkiaSharp 3.119.2) instead of SKTextBlob wrappers. A new blob costs 128 B JIT, 184 B AOT (wrapper,
  WeakReference registration, and on AOT the run-buffer class). Prototype allocKB per op:

  | Row | JIT before → after | AOT before → after |
  |---|---|---|
  | scroll/jump | 6.3 → 2.4 (−62 %) | 9.1 → 2.4 (−74 %) |
  | scroll/wheel | 1.8 → 1.2 | 2.3 → 1.2 |
  | scroll/sticky | 1.9 → 1.2 | 2.3 → 1.2 |
  | scroll/nested | 2.2 → 1.2 | 3.0 → 1.2 |
  | selectable viewer wheel | 3.3 → 2.2 | 4.0 → 2.2 |
  | richtext wheel | not run | 1.0 → 0.2 |

  What remains in scroll/jump is cache-map growth per fresh session. Pixels identical (same native
  draw call; 8-frame A/B). Churn bar on 6 rows JIT and AOT; makes sub-blobs cost no managed bytes.
  Medium; the first direct P/Invoke into SkiaSharp's C API, blob lifetimes by hand (Needs the user).
- **paint-raw-run-buffer.** Build blobs with `AllocateRawPositionedRun` (struct) instead of
  `AllocatePositionedRun` (class) at SkiaRenderContext.cs:1501. NativeAOT heap-allocates the 56 B
  SKPositionedRunBuffer per new blob; JIT keeps it on the stack. AOT allocKB per op: scroll/jump
  9.1 → 6.3 (−31 %); scroll/nested 3.0 → 2.2 (−27 %); scroll/wheel 2.3 → 1.8 (−22 %); selectable viewer
  wheel 4.0 → 3.3 (−18 %); scroll/sticky 2.3 → 1.9 (−17 %); richtext wheel 1.0 → 0.6 (−40 %). JIT
  unchanged; same native call. Native handles would cover this too. Small.
- **paint-polygon-pooled-path.** `FillPolygon` and `DrawPolyline` reuse the pooled SKPath (Rewind)
  instead of a new SKPath per call (SkiaRenderContext.cs:1335-1366, :1362); HultUI.Terminal
  `BoxDrawing.HalfEllipse` (BoxDrawing.cs:189-194) fills a reused span instead of a new Point[25] per
  call. Every Powerline separator on screen is redrawn every terminal frame (BoxDrawing.cs:157).
  Keystroke frame, 1 Powerline line: 2.31 → 1.38 KB render (path change alone); 6 lines: 12.49 → 1.62
  KB/frame (72 paths, 24 arrays gone); a whole keystroke 12.8 → 1.9 KB. Pixels identical at 1.5. Rich
  text task ticks also use DrawPolyline (RichTextEditorElement.Render.cs:250). Churn −87 % render bytes
  per terminal frame with a Powerline prompt, growing with prompt lines on screen. Small.
- **paint-cluster-line-measurer.** A spanned or selected line whose shaping merged clusters (emoji, ZWJ,
  combining marks, ligatures, Indic/Thai) builds a prefix-measurer closure per line and again per colour
  stretch (SkiaRenderContext.cs:827, :923; `MeasurerFor` at :1296 captures the 40 B
  ResolvedInheritedStyle struct, ~128 B per closure) and cuts a substring per boundary (TextTypes.cs:272).
  Vesper reader on DemoShell.cs lines 401-438 (2 such lines on screen), hover frame: 2.16 KB per frame
  from 12 closures and 8 substrings; the measure calls allocate 0; every other steady frame measured is
  0 B. Caching the closure by reference fails (struct style): 2.9 KB. Fix: one measurer per line, cached
  by value; text side a span-based prefix measure (e.g. `MeasureTextWidth(ReadOnlySpan<char>)` from
  b23b203). Removes ~1.1 KB per such line per steady frame → 0. Small; the TextTypes part is expert 5's.

## Examined, not worth it

- Part (a) of paint-spanned-line-glyph-work, an identity-first blob lookup: a reference-keyed side map
  adds 47 B per new blob (scroll/jump 6.3 → 8.7 KB/op JIT, +38 %; AOT 9.1 → 11.5). Sub-blobs already
  remove the hashing on spanned lines; plain-text frames hash at most 4.5k glyphs (dense text 4,496).
- 1 SaveLayer per frame on Aperture's item page: the disabled cast pager "Previous" button (opacity
  0.4, ApertureApp.Cards.cs:515), a box with one icon. Junction and Vesper hover frames have 0 layers;
  layers bounded since round 1.
- Gradient cache during a resize: a scrim's rect changes every resize frame, so it misses once per frame
  and keeps at most 256 shaders. No steady cost.
- GPU memory: no round 2 change uploads textures; not measured.
- paint-drawspannedline-overlap (stays waiting, lowest; could close with a Debug assert): the contract
  is sorted, non-overlapping spans (TextSpan.cs:9, TextElement.cs:84); every producer flattens its spans
  (TextSearch.Decorate, Junction MergeHighlight JunctionApp.Diff.cs:768, MdSpans); Junction Files'
  overlapping list goes to a TextArea that draws one line at a time, so the search starts at offset 0.
- aperture-poster-as-hero (stays waiting): not reproducible (jellyfin.home: 0 of 17 films and 0 of 10
  series lack a backdrop). Code unchanged; the fallback (ApertureApp.Cards.cs:428-429) asks by width,
  2048 px at 150 %, decoding a 2000×3000 poster to ~22.9 MB with ~20 % of rows shown. Fix: crop at
  decode.
- aperture-tmdb-size-fallback (stays waiting, not perf): a HEAD request gets 200 for w1920, 400 for
  w1600 and w2000.

## Needs the user

1. **aperture-hero-scrim-seam:** re-confirmed on the real Eternals page. Luma jumps 144 → 169 (row 80)
   and 135 → 154 (row 150) across x 998/999, where the horizontal scrim ends at 15 % alpha
   (Halide.cs:90). End it at 0 %? Changes Aperture's pixels on purpose; not a perf change.
2. **paint-native-blob-handles:** allow HultUI.Rendering.Skia to call three libSkiaSharp C functions
   directly, bypassing SKTextBlob? Not a new dependency, but ties the renderer to SkiaSharp's native C
   API at the pinned 3.119.2.

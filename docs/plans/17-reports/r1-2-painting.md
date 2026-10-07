# Round 1 survey: 2. SkiaSharp: painting

Measured with a temporary paint probe (render-pass allocation, per-element-type allocation,
draw/glyph/path/shader/layer counts, image bytes copied) in worktree
agent-a720f5935ea37cf7e at d47f737, then reverted. The probe patch, `PaintProbe.cs` and
`probedriver.cs` are in the orchestrator's scratchpad as `painting-probes.patch` (session
dc3c745c).

SATISFIED: no
REVIEW: none (round 1)

## Candidates

- **paint-text-zero-alloc.** Index loops instead of `foreach` over `TextLayout.Lines` / `TextLine.Runs`; move `PrefixMeasurer`'s lambda into its own method (closure ~72 B allocated at method entry on every call); pooled `SKPath` for rounded-border corner triangles.
  - Evidence: enumerators at SkiaRenderContext.cs:526, 537, 565, 624, 702, 810, 1161; closure at :1163; TextTypes.cs:166 (`VisualRanges`); 4 new `SKPath` per rounded border at :405. Experimental fix measured: 72 B per `DrawText` → 0. Render-pass KB/frame: paint/dense text 17.5 → 0.21; text/spans recolor 89.8 → 0.21; input/type 44.1 → 1.7; input/hover 5.7 → 0.21; anim/hover 28.3 → 0.14; controls/expander 54.3 → 0.07. allocKB/op: input/type 44.8 → 2.5; layout/deep nesting 51.3 → 4.8; text/spans recolor 161 → 71.5; input/hover 6.8 → 1.3; grid/resize 16.8 → 3.1; richtext/wheel 9.9 → 1.8; richtext/select all 12.0 → 2.0; overlay/tooltip 14.8 → 4.3; controls/expander 144 → 90. None worse. Junction Changes diff: 83.9 → 10.7 KB per hover frame, 92.7 → 18.9 per wheel frame; History labels 10.2 KB → 0. Vesper ~10.9 → 0.34 KB per hover frame.
  - Expected: −10…−94 % allocKB on ~40 rows; steady text frames ~0.2 KB.
  - Cost: small; no API or pixel change.
- **paint-image-zero-copy.** `SKImage.FromBitmap` copies a mutable bitmap (SkiaImageSource.cs:26), so every image is held twice. `SetImmutable` before wrapping, or keep only the `SKImage`.
  - Evidence: 40 × 1000×1500 BGRA: mutable +229 MB and a different pixel address; immutable +0 MB, same address. `SKBitmap.Decode` output is mutable. Vesper thumbnails 14 PNGs copied 1,362 KB (cache up to 512 → ~50 MB duplicated). Junction icons 18.8 KB. Aperture `PosterCache` up to 400 × ~0.96 MB → ~375 MB duplicated once drawn. All producers finish drawing before handing over; `SkiaImageSource` owns and disposes.
  - Expected: −50 % of decoded image memory.
  - Cost: small; no pixel change. A caller mutating a bitmap after handing it over would break (none does).
- **paint-selectable-label-paragraph.** Keep a selectable/linked label's paragraph between frames while text, style, width, spans, MaxLines and Overflow are unchanged; draw with `ctx.DrawParagraph`. Today `TextElement.Render` builds a new `PipelineTextParagraph` every frame (TextElement.cs:551; allocations PipelineTextParagraph.cs:31, 38–40, ~320 B) and `DrawText` looks the layout up again.
  - Evidence: Junction diff rows all selectable (JunctionApp.Diff.cs:972, 1035), so are Vesper viewer lines. Junction diff: 31–32 paragraphs per frame = 9.9 of the 10.7 KB left after the fix above; 263 lookups for 231 labels.
  - Expected: Junction diff render-pass ~10.7 → ~0.8 KB/frame.
  - Cost: small–medium; prove pixel identity against goldens. Code in Core/Elements.
- **paint-spanned-line-glyph-work.** (a) Look a run's blob up by object identity first, content-keyed dictionary only on a miss. (b) Optionally draw each colour stretch from a sub-blob of glyphs whose ink can reach that clip.
  - Evidence: blob comparer hashes every glyph per `DrawRun` (SkiaRenderContext.cs:77–88, lookup :1360); `DrawLineSegment` redraws every overlapping run whole per colour stretch (:806–814). Glyphs hashed per frame: Junction diff hover 23,045 (231 DrawText → 522 DrawRun); text/spans recolor 68,647 (~139-glyph line drawn ~13.7×); richtext/select all 5,749; paint/dense text 4,496; Junction History 1,932.
  - Expected: O(stretches × glyphs) → O(glyphs) per frame; matters for syntax-highlighted text. Time unmeasured.
  - Cost: (a) small, (b) medium; both pixel-identical by construction.
- **junction-lane-palette** (app). `LaneColor` reads `LanePalette`, which builds a new `Color[6]` three times per call (JunctionApp.History.cs:21, 1287–1288). 183–1,018 B per graph canvas per frame → 2.6–14.3 KB per History hover frame. Framework canvas context allocates 0 B. Guideline example: no allocating properties in paint callbacks. Small.
- **text-shaper-per-call-objects** (owner: text pipeline). `SkiaTextShaper.Shape` creates an unused `SKPaint` plus an `SKFont` per call (SkiaTextShaper.cs:20–21): 0.84–1.0 KB per call; text/create 2,000 labels 1,815 of 7,866 KB/op; Junction start-up 874 calls, 733 KB. Cold path only.
- **textarea-render-substrings** (owner: text pipeline). `TextArea.Render` allocates a substring per visible line per frame (TextAreaElement.cs:1402) and a span list per spanned line (:1489); after an edit `GetLineStarts` re-measures every line in Render. input/type 2,000-line TextArea: 41 substrings, 2,042 layout lookups, 269 KB render-pass per keystroke; wrapped 900-line row 241 KB.

## Examined, not worth it

- Shadow, gradient, dash objects per draw (SkiaRenderContext.cs:166, 200, 433): 104 B/shadow, 168 B/gradient; apps draw 0–1 shadows, no gradients or dashes. Fold a mask-filter cache in only if free.
- Run blob cache bound (clears at 2,048): 0 clears anywhere, ≤ ~78 new blobs per frame.
- Layout lookup per label per frame in Render: allocation-free, O(text length) hash, tens of µs per Junction frame.
- SaveLayer and clips: 0 SaveLayers per hover frame in Junction and Vesper; ~11 saves per frame.
- Picture/layer caching of static subtrees: would reopen full-redraw/damage-rect decisions; Junction full repaint ~1 ms CPU raster.
- Canvas drawing context: 0 B per draw.
- Mipmapped sampling for ~1:1 images (`DownscaleSampling`, SkiaRenderContext.cs:850): up to +33 % GPU texture memory; GL expert to check `GRContext` usage.

## Needs the user

- **Aperture poster decode size** (ApertureApp.Cards.cs:114): posters fetched at card width × 2 whatever the display scale — 4× the drawn pixels at 100 %, 1.78× at 150 %, over a 400-poster cache. Width × display scale cuts retained poster memory 75 % at 100 %, 44 % at 150 %; posters look soft briefly after moving to a higher-DPI monitor until refetched. Worth up to ~280 MB?

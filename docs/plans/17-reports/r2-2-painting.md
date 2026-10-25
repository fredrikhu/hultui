# Round 2 survey: 2. SkiaSharp: painting

Paint probes (render-pass bytes per element type; paragraph, blob, glyph, stretch, shader,
mask-filter counters) JIT and AOT on 6ba7e2d, reverted; a real GL window for the image-memory
lead. Probes in the session scratchpad `r2paint/`.

SATISFIED: no

REVIEW: none. a2f6051's pooled corner SKPath is safe (Skia's clip copies the path); steady
paint frames allocate 0 B in the render pass on JIT and AOT. 615d75d: GL texture bytes per
photo unchanged (35.16 MB plain, 46.88 MB mipped for 3840×2400). Aperture artwork (ebefa29,
a9b6970, e18b689) is drawn at 0.9–1.0 device px per source px, so Skia builds no mips.
7e6fd57 Vulkan-only; 4315c4b keeps the stencil. 81be6ca's narrow-label check: 0–1 cached
measures per frame. 4030012 slider: same pieces, same order, 0 B.

## Candidates

- **paint-selectable-label-paragraph.** Keep a selectable/linked label's `PipelineTextParagraph` between frames while text, style, width, spans, MaxLines and Overflow are unchanged; draw with `DrawParagraph`. TextElement.cs:554 builds one per frame (~320 B: PipelineTextParagraph.cs:19, 31, 38, 40), then draws via DrawText (:562/:564). Junction diff hover 10.0 KB render-pass per frame (9.7 KB = 31 paragraphs); diff wheel 25.8 KB (10.0 paragraphs); Vesper reader hover 10.9 KB (all 35 paragraphs), wheel 33.4 (10.6). No suite row has selectable labels. Junction diff hover → ~0.3 KB, Vesper reader → 0. Small–medium; prove pixels with goldens.
- **paint-spanned-line-glyph-work.** (a) identity-first blob lookup (SameGlyphs.GetHashCode hashes every glyph per DrawRun, SkiaRenderContext.cs:77-88); (b) per colour stretch draw a cached sub-blob of the glyphs whose ink can reach that clip (DrawLineSegment redraws every overlapping run per stretch, :814-837). Per frame: Junction diff hover 22 lines, 354 stretches, 553 run draws, 26,210 glyphs (16× each line); Vesper reader 10,963 (6×); JSON wheel 3,996 (3.8×); text/spans recolor 68,647 (13.7×). O(stretches × glyphs) → O(glyphs + stretches): (a) 26,210 hashes → ~0; (b) Skia glyph work 26,200 → ~4,000 (Junction), 11,000 → 2,500 (Vesper), 4,000 → 1,200 (JSON). Time unmeasured. (a) small, (b) medium; pixel-identical by construction.
- **paint-effect-object-cache.** One `SKMaskFilter` per blur sigma and one gradient `SKShader` per gradient + snapped rect, bounded maps (DrawBoxShadow :166; DrawGradientRect :200-221). 104 B per shadow, 168 B per gradient (AOT 149 / 212). Aperture item page steady frame 0.33 of 0.56 KB is its two scrims (ApertureApp.Detail.cs:108, :116); every popover/menu/dropdown frame 104 B (PopoverTheme.cs:19), dialogs 104 B (Dialog.cs:130), Meridian rule fades. Suite: paint/tiles shadow 7.35 KB/op → 0, gradient 11.85 → 0 (−100 %). Round 1 declined it; with the frame floor at 0 these are what is left in real steady frames. Small; pixel-identical.
- **paint-blob-builder-reuse.** One reused `SKTextBlobBuilder` instead of a new one per cache miss (SkiaRenderContext.cs:1395): 216 → 136 B per new blob; 208 B per miss in the suite (AOT 306). scroll/jump 5,000 rows: 50.8 new blobs per step = 10.35 of ~10 KB/op → ~6.3 (−39 %); create rows −4…−12 KB (< 1 %). Small.
- **junction-lane-palette** (app). Palette built once per theme instead of a new Color[6] per read (JunctionApp.History.cs:21, :1287-1288); index loops over edge lists (:1263-1281). History hover in this repo 1.97 KB/frame → 0 (14 canvases); round 1 saw up to 14.3 KB on a many-lane repo. Guideline example. Small.

## Examined, not worth it

- **gl-image-retained-after-dispose: declined — it is NVIDIA's GL driver pool, not Skia.** Real GL window, eight 3840×2400 photos (281 MB decoded), drawn then disposed: private +417.8 MB beyond the pixels, DXGI 344.7 MB, nvidia-smi +347 MiB; Skia's cache 234.8 MB (5 mipped textures). `PurgeUnlockedResources` took Skia to 0.4 MB but DXGI stayed 344.8 MB, nvidia-smi +347, private 491 → 504 MB. One photo at a time: DXGI 151 MB either way. A second pass after a purge added only +48 MB (the driver reuses its pool). Drawn 1:1: DXGI 291 MB retained. A high-water mark no Skia call returns; the lever is uploading fewer pixels (decode at the drawn size) — guideline. AMD, Intel, Vulkan not measured.
- paint-cover-half-pixel-crop: quality fix that changes pixels on purpose (Cover source offset unsnapped, SkiaRenderContext.cs:1071); no perf bar; Aperture's cards avoid it. Quality list.
- Skipping mips for near-1:1 downscales: Skia already does (plain at 0.95× and 0.8×, mipped at 0.6×).
- A framework decode-at-size helper: four apps already scale their own pictures; Junction's image diff decodes full size for 1:1 zoom. Guideline.
- Run blob cache bound: 0 clears across all 94 scenarios and app runs.
- Render-pass bytes owned by others: list realization (virtual/list 100k jump 215 KB/step, TableView sort 233 KB, Junction diff wheel ~16 KB/frame, Vesper Down 240 of 470 KB) → layout-virtuallist-realize-in-render; JSON wheel 14.1 KB/frame in TextAreaElement.Render → text-textarea-measure-lines.
- SaveLayer and clips: 0 SaveLayers on app hover frames; 1 on a dialog.

## Needs the user

None.

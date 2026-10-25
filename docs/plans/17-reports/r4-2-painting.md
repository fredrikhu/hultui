# Round 4 survey: 2. SkiaSharp painting

At ba817e8; render-pass probe on all 122 suite rows (JIT), headless probes with env-gated
prototypes. Probes reverse-applied; worktree clean. Probe files in the session scratchpad `r4paint/`
(`probe-r4.patch` with `PROTO_RUNINK`, `PROTO_ROWSTR`, `PROTO_VIEWFRAME`, `PROTO_MARGIN`; `probe/`
R4Probe modes faces, varab, term, termtrace, tasks, viewer, fontcache; `suite-probe-jit.txt`).

SATISFIED: no

REVIEW: none.

- Render-pass bytes 0 on every steady or hover row that draws text (122 rows, JIT).
- Effect caches: blur and gradient misses 0 after the first frame.
- 27024f6 (native blobs): the finalizer touches only plain dictionaries and Skia's thread-safe unref.
- 6c70dbe (kept path): every user of the kept SKPath finishes inside one call.
- 5cc9892, b183aa0, b3e00b0 (sub-blobs): the map stays bounded (Examined).
- 12c64b0 (colour emoji): emoji runs are 1–3 glyphs, drawn whole as designed.
- 58f78dd (image textures): interaction with the idle loop bounded (Examined).

## Candidates

- **paint-subblob-variable-fonts** (re-ranks the waiting item). For an outline face whose bounding box
  Skia marks as a guess (variable fonts), take the run's own glyph bounds once per run (one
  `SKFont.GetGlyphWidths` call with spans into kept buffers, kept per run in a presized map bounded like
  the sub-blob map, same slack), so its colour stretches draw sub-blobs too. Faces affected here:
  Cascadia Mono and Cascadia Code (Windows' `monospace`, the Terminal's default), Segoe UI Variable Text
  and Display, Bahnschrift (whole runs today, SkiaRenderContext.cs:1016, flag at :1100); Calibri and
  Courier New Regular stay out (EBDT bitmap strikes). Glyphs per frame (prototype):

  | Screen | Before | After |
  |---|---|---|
  | text/selectable viewer hover | 22,587 | 3,702 (−84 %) |
  | text/selectable viewer wheel | 23,498 | 3,784 (−84 %) |
  | text/wheel highlighted JSON | 3,883 | 1,255 (−68 %) |
  | Diff-like viewer, 46 lines, ~10 stretches per line, Cascadia Mono | 29,856 | 3,780 (−87 %) |
  | Same viewer, Segoe UI Variable Text | 29,856 | 4,343 (−85 %) |
  | Same viewer, Consolas (already covered) | 4,218 | 4,218 |
  | Terminal, `ls --color`-like screen | 42,445 | 3,549 (−92 %) |
  | Terminal, Powerline page | 1,775 | 659 (−63 %) |

  Glyphs hashed in blob lookups fall alike (viewer 29,856 → 83, terminal 42,445 → 25); richtext/type in
  a 600-span paragraph 81 whole-run draws per frame → 0. Pixels: 80 of 80 frames byte-identical (four
  variable faces, scales 1/1.25/1.5/2, no transform / 3° rotation / 0.8 scale / skew / fractional
  translate, 9–40 px, bold and italic); SpannedLineSubBlobTests 18/18 with the prototype on. Bytes 0 per
  steady frame; the new map grows once (+2 KB/op on the viewer wheel row) unless presized (~57 KB once
  per render context). Expected time ≥5 % on selectable viewer hover/wheel and highlighted JSON wheel
  (round 3's comparable glyph cut: Junction diff hover −15/−17 %, spans recolor −65 %); the same win for
  Junction/Vesper diffs with Cascadia as code font, HultUI.Terminal on Windows, Fedora's variable Noto
  Sans. Scaling: a spanned line O(glyphs) instead of O(stretches × run glyphs). Small–medium; no API
  change; pixel-identical by construction. The build must re-run the pixel A/B on WSL Fedora and include
  an A/A control (Examined).
- **terminal-row-strings** (with **terminal-textframe-record**). Keep each terminal row's drawn string
  and reuse it while the row's text is unchanged (compare the StringBuilder); make the grid's
  `TextFrame` record only when a value changed. TerminalElement.Render.cs:350 cuts a new string per drawn
  row per frame; TerminalElement.Accessibility.cs:56 makes a 32 B `TextFrame` every Render. Per forced
  frame (1200×900, prototype): Powerline page 1,648 B → 0; coloured screen 6,048 B → 0 (all
  `System.String`). Churn: 1.6–6.0 KB removed from every terminal frame. Small; no API or pixel impact.
  No suite row covers the terminal — expert 8 to add one (coloured screen, forced frame).
- **richtext-viewframe-record.** Make the `ViewFrame` record in `NoteViewFrame` only when X or Y
  changed (RichTextEditorElement.Accessibility.cs:80, 24 B every editor Render; 40 ticked tasks forced
  frame 24 B → 0, the editor's only render-pass allocation). Small; bundle with terminal-row-strings.
- **aperture-hero-scrim-seam** (approved by the user 2026-10-11). Halide.cs:90 is still
  `Gradient.Horizontal(Alpha(Bg, 0.96), Alpha(Bg, 0.15))`, used by the item-page hero
  (ApertureApp.Detail.cs:116) and Discover (ApertureApp.Discover.cs:753); end it at 0 %. No perf bar;
  removes a visible seam (luma 144 → 169 and 135 → 154 across x 998/999 on Eternals). Small; Aperture
  pixels change on purpose.
- **paint-menu-rows-outside-window** (code in Elements → owner expert 1). `ContextMenuElement.Render`
  (ContextMenuElement.cs:320-405) measures and draws every item every frame, including items below the
  window; skip rows outside `ctx.ClipBounds`. overlay/dropdown open 200 items: 278 text items per frame,
  173 of them (3,184 glyphs) outside the window, 200 `MeasureText` calls per frame. The popup is laid out
  200 rows tall below its anchor, so rows past the window bottom also look unreachable by mouse (real
  case: Junction's merge-request target-branch dropdown, JunctionApp.CreateMergeRequest.cs:305, in a
  repo with many branches). Scaling: per hover frame of an open menu O(items) → O(visible). Small for
  the cull; a maximum height with scrolling is the real fix for reachability.

## Examined, not worth it

- No cull above the first clip (`RenderTree`): off-window lines are suite artefacts from columns that
  overflow the window without a scroll view (deep nesting 594 of 661 lines, type in TextInput 528 of
  602, gallery mount 156 of 225, splitter drag 78, slider drag 42 per frame). Apps keep long content in
  scroll views; overlays covered by paint-menu-rows-outside-window.
- Line cull inside a multi-line DrawText: 1 of 36 lines off the clip on text/wrap.
- Skia glyph cache limit (2 MB default): realistic screens use 0.03–0.34 MB; worst case 480 distinct
  emoji at 20 px, 2× = 1.29 MB.
- **Testing trap:** DirectWrite text is not byte-identical between two draws in one process. An A/A test
  (whole runs drawn twice) differed in 4–5 of 120 frames (3–46 bytes, max delta 35) on Consolas after
  many strikes with the default 2 MB cache; with a 512 MB cache 0 of 120 — strike purges regenerate some
  glyph masks differently. Explains round 3's unexplained 1–4 pixel difference (max 51) and 12c64b0's
  first-render note. Any byte-identity A/B on system fonts needs an A/A control or a raised cache limit.
- Pixel-path video frames (`DrawPixelFrame`): 248 B per frame on media/* rows (SKPixmap and SKImage
  wrappers); production GL keeps the wrapper (0 B); Vulkan's per-frame wrap (~530 B) is expert 3's
  surface code; only raster and non-interop Vulkan take this path, and removing it needs more native C
  functions than the four approved.
- 58f78dd with the idle loop (1521f47): images disposed in the last second before idle wait for the next
  frame — at most ~27 posters (~4–10 MB of spare textures) at Aperture's rate; same-size spares are
  reused by the next upload. Bounded.
- Sub-blob map: keys can hold up to 2,048 stale runs after re-layouts; the 600-span paragraph makes ~224
  new sub-blobs per keystroke and the map clears about every 9 keystrokes (~0.5 MB managed plus native);
  presize 74 KB once per context.
- Blob-lookup glyph hashing on plain text (dense text 4,496 glyphs per frame): identity-first lookup
  declined in round 3 (+38 % bytes); a cached hash on ShapedRun costs +8 B per run on every layout.
- paint-drawspannedline-overlap: no new evidence (terminal spans built in order, TrimSpans keeps them
  apart); stays lowest.
- Remaining render-pass bytes belong to other areas: TextArea line starts (text/open long document
  1,373 KB; typing 6.8 KB), list realization in render (virtual/, table/), rich text layout inside paint,
  map growth per fresh session (~1.1 KB per step on scroll/ rows).

## Needs the user

None.

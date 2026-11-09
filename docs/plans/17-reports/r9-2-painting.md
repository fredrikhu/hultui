SATISFIED: no

Measured at ea3e57ab, headless, JIT. Counts and bytes only, no timings. I reused round 8's paint probe (`probe-r8-tracked.patch` + `PaintProbe.cs`, which applied cleanly) and ran it over all 214 suite rows, round 8's R8Probe modes, the Meridian History driver, and two new drivers: Vesper layers and the Meridian Request tab. I also tried two env-gated prototypes, `PROBE_TEXTCULL` and `PROBE_IMGFADE`. Afterwards I undid every edit by reverse edit plus `git apply -R`; the worktree is clean.

REVIEW:
- **967ce1ac** (main's Meridian Request tab, merged in 3a15b5b1): this is an objection, carried as candidate paint-label-lines-cull below.
  - The sent body is one plain `Text` (`Ui.Mono(...)`, MeridianApp.Response.cs:646) inside a ScrollView.
  - A plain label paints through `SkiaRenderContext.DrawText` (SkiaRenderContext.cs:815/861), which draws every line every frame. Only `DrawParagraph` (:924) culls lines by the clip, so a selectable label culls and the same plain label does not.
  - Test: the coll25 "Notes Big" body is 89,000 chars, a label 23,785 px tall. The send fails, which still leaves a `SentRequest`.
  - Wheel over the tab: 1,898 lines and 1,903 text runs drawn a frame, 1,706 of the lines off the clip.
  - Opening the tab: 538 KB of render, 1,771 new text blobs, 3,783 KB for the step.
- **f3302209 holds.**
  - Clip probe: a canvas clipped to 1200×800 at save count 2 and handed over as the Vulkan surface does (`SetCanvas(canvas, 2)`) keeps its device clip through `BeginFrame`. `ClipBounds` is 801×535 layout px. Handed over at 1 (the old behaviour) it is 1601×1068.
  - A 200-row menu with 28 px rows (culled by `ClipBounds`, ContextMenuElement.cs:393) draws rows 0–19 instead of 0–38.
  - `SetCanvas` callers: GL and raster pass 1, Vulkan passes the surface's count. Nothing else swaps the canvas.
- **307ee07c holds.** Meridian History, 1440×900:
  - Layers a frame went from 29.0 / 29.4 / 30.0 to 0 (Down/Up, wheel, hover).
  - All other draws are unchanged: 230.8 runs, 48 rounded rects, 205 text lines.
  - One more element is rendered (317, was 316): the archive part's group from 62a67257.
  - On Down/Up, render bytes went from 0 to 0.38 KB a frame. That is one 13 KB `TextElement` layout every ~33 frames (the archive panel's new text); the key totals 18.7 KB.
- **80f31170 `Element.Restyle` and the restyled cursor rings:** paint only.
  - `Recompute` ends in `MarkPaintDirty` (InteractionStyles.cs:105). A restyle repaints one frame, like any change (no damage rects, by decision).
  - Vesper's `ShowCursor` sets only border and background colours; transparent ones draw nothing (BoxElement.cs:143).
  - `VisualState` copies the shadow list only when there are shadows (:210, :227). Vesper's cells have none.
- **180d2579 (web preview):**
  - `WebViewElement.Render` makes one `DrawImage` and one clip a frame.
  - Capture frames arrive only when the page changes (VisualCapture.cs remarks), and placement is posted after the frame.
  - It cannot run headless without an engine. No objection.
- **Suite, 214 rows against round 8's 213:** no paint counter rose.
  - Render KB a step fell on rich text (step 3): open 3,145 → 2,245, enter 40.3 → 0.9, resize 652 → 464.
  - Rich text typing rows rose by 48 B a key (424 → 472; 0 → 48 inside a VirtualList row). bc007ae8's `TextLineBox[]` is now made on first read, in render; step 3's net is still lower.
  - The worker tick rows rose 0.12 → 0.18 KB of render, but their total bytes (55 / 193) are unchanged, so that is attribution only.
- **Others:** e7d10c63 and 4fc94836 touch no paint code. No paint objection to the rest of round 8.

CANDIDATES:
- id: paint-label-lines-cull
  what: `SkiaRenderContext.DrawText` (both overloads) skips lines wholly above or below `Canvas.LocalClipBounds`, with the same line-height slack `DrawParagraph` uses (:924-935). It reads the clip only for labels of more than one line.
  evidence: prototype `PROBE_TEXTCULL` on the Meridian Request tab (mdrv `request`):
  - Wheel: runs drawn a frame 1,903 → 194 (−90 %), 1,706 lines culled; render 0 → 0.35 KB a frame (runs made for lines scrolling in, once each).
  - Opening the tab: render 538.5 → 13.8 KB (−97 %), blobs made 1,771 → 61, step 3,783 → 3,258 KB (−14 %).
  - Pixels: 0 channels differ outside the header's elapsed-time label, which differs run to run (an A/A run shows the same 48 px box).
  - Suite with the prototype: no row changed (`cmp.py`). No suite row has a multi-line plain label inside a clip. Its off-clip lines (layout/deep nesting 594 of 661, TextInput typing 528 of 602) are top-level overflow, which the root never culls by design (ElementRenderer.cs:16-19).
  expected: Scaling (per-frame text drawing goes from all lines to the visible lines). Memory churn: −14 % on opening the tab, −97 % of its render. The build phase needs a suite row, for example a wheel over a 2,000-line plain label in a ScrollView.
  cost: small, framework only. No API change; pixel-identical (only lines wholly outside the clip are skipped). The rest of the open step (3.26 MB) is building and laying out one 89k-character label. The app could show the sent body in a read-only TextArea, as the response body does (expert 8's or the app's call).
- id: paint-image-fades-itself
  what: a lone `ImageElement` with `Opacity` < 1 draws its still image once with the alpha `PushOpacity` would use, instead of in an offscreen layer, as a plain Box already does (BoxElement.cs:198, ElementRenderer.cs:73). GPU and pixel-stream sources keep their layer.
  evidence:
  - Vesper ghosts each cut row's icon with `icon.Opacity = 0.45f` (CommanderApp.Pane.cs:991, Views.cs:666). Icons are `Image` leaves (`Icons.Draw`, `FileIcons.Draw`), so each cut row is one layer.
  - vdrv, 1240×820, every entry cut, layers a frame: Detail (61 entries) 26.6, Brief 61, Thumbnails 29.5 (whole tiles: Views.cs:419). The same views uncut: 0.
  - Prototype `PROBE_IMGFADE` (an internal interface; probe-only IVT from Core to HultUI.Rendering.Skia): Detail 26.6 → 0, Brief 61 → 0.
  - Pixels, A/B frame dumps: Detail at 1× has 3,363 icon pixels that differ, ≤2/255. Brief at 1.5× has 18,310, ≤2/255. Detail at 1.5× and Brief at 1× are identical (it depends on where an icon falls on the pixel grid).
  - Cut rows stay on screen while the user pastes in the other pane, so this is a lasting state, not a moment.
  expected: like step 9b. Round 8 step 9 measured ~8–10 µs CPU a layer and 20–32 % of a frame for 29 row layers. These are 16–24 px layers, so the GPU fill is smaller, but each layer still splits the render pass. A real-window GL/Vulkan timing (step 9's harness) decides whether it clears 5 %. It also covers Vesper's dimmed thumbnail placeholder (Views.cs:401).
  cost: small to medium. Needs a Core→Skia internal seam (IVT or an internal interface; no public API). Pixels change by ≤2/255 on faded images.
- id: apps-vesper-faded-rows
  what: Vesper fades the colours of Sync's Equal rows (Sync.cs:313, `row.Opacity = 0.55f` on a 7-cell Grid row) and of cut thumbnail tiles (Views.cs:419), not the whole row or tile, as 307ee07c did for Meridian History.
  evidence: vdrv, 67-row sync of two copies with "Show equal" on, layers a frame: Down/Up 26.2, wheel 23.1, hover 20 (0 with Show equal off, the default). Thumbnails with every tile cut: 29.5.
  expected: like step 9b (20–30 row- or tile-sized layers a frame). Only while "Show equal" is on or a cut is pending. Below the two framework candidates.
  cost: small, app only. Pixels change: glyph edges, plus wherever a tile's badge or mark chip overlaps its picture. Needs the user, as question 13 did.

WAITING, RE-RANKED:
- terminal-noop-scrollback-wheel: unchanged. A no-op notch still draws a whole frame (1.00 a notch): 5,440 / 5,682 glyphs, 836 / 882 draws, 112 B a notch with 12 listings. Only the terminal sample (and the PerfSuite) reference HultUI.Terminal. Keep low.
- terminal-row-strings-by-content: unchanged, 10,276 B of render a streamed line, all in TerminalElement. Same reason; keep low.
- controls-menu-max-height-scroll: the Vulkan-drag half is gone since f3302209 (the menu now culls at the window, rows 0–19 instead of 0–38; see REVIEW). What remains, no maximum height or scrolling, is not perf and is expert 1's.

EXAMINED, NOT WORTH IT:
- Other row-level `Opacity` in the apps, all transient or a few layers:
  - Junction's resolved review cards (Review.cs:892): one layer per resolved card on screen; the recording has 1 resolved thread of 3, and round 8's walks drew 0 layers.
  - Junction's clone list while it waits (CloneFromGitLab.cs:107) and its dragged repo row (RepoOrder.cs:39).
  - Meridian's options locked rows (Palette.cs:194), its sending and stale response (Response.cs:137, 402) and its runner row (Runner.cs:174).
  - Aperture's disabled pager button and disabled buttons in general (ButtonElement.cs:258; 0 in walks).
- Top-level overflow labels in suite scenarios draw off the window: the root has no cull by design, and apps keep overflow in scroll views.
- Web preview paint: one image a frame, new frames only on page changes (see REVIEW).
- Rich text +48 B a key, History +0.38 KB on Down/Up, worker tick +0.06 KB: attribution moves or one-offs (see REVIEW).

NEEDS THE USER:
- paint-image-fades-itself: may faded images change by ≤2/255 (often 0) to drop one layer per faded image? Vesper's cut icons are 26.6–61 layers a frame. Ask once the build phase's GPU timing shows it clears 5 %.
- apps-vesper-faded-rows: may Vesper's Equal sync rows and cut tiles fade their colours instead of the whole row or tile? The pixel change is like question 13's, larger where a badge overlaps a tile's picture.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\c484f5ac-8f22-48dd-b669-13c24b1e7889\scratchpad\r9-painting\`
- Probe:
  - `probe-r9-full.patch`: round 8's probe plus both prototypes, behind `PROBE_TEXTCULL` / `PROBE_IMGFADE`; apply it from the worktree to rebuild the drivers.
  - `proto-image-fade.patch`: the image-fade half only.
  - `PaintProbe.cs`.
- Scripts: `cmp.py`, `newrows.py`, `rawdiff.py`.
- Drivers:
  - `probe\`: R8Probe; the clip mode now takes a base count.
  - `mdrv\`: Meridian History, and `request` for the Request tab, with `MDRV_DUMP`.
  - `vdrv\`: Vesper Sync, Detail, Brief and Thumbnails, cut or plain, with `VDRV_SCALE` and `VDRV_DUMP`.
  - `walk\`: retargeted, not run.
- Outputs:
  - Suite: `suite-probe-r9.txt`, `suite-textcull.txt`, `ab-r8-r9.txt`, `ab-textcull.txt`.
  - Probe runs: `clip.txt`, `terminal-wheel.txt`, `terminal-stream.txt`, `meridian-history-1.txt`, `meridian-request.txt`, `meridian-request-cull.txt`, `vesper-layers.txt`, `imgfade.txt`.
  - Frame dumps: `dump-*.raw`.

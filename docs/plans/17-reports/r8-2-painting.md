# Round 8 survey, expert 2: SkiaSharp painting

SATISFIED: no

Measured at 27020cdb, headless, JIT. Counts and bytes only, no timings. I ported round 7's paint probe (the Ganesh text-blob cache emulation, behind `PROBE_GPU=1`) and ran it over all 213 suite rows, the R7 probe modes and 41 app walks (expert 8's round 7 walk host with a stand-in `FrameProbe` and a `PAINT` line). The probe was reverse-applied afterwards and the worktree is clean.

REVIEW:
- **d04464f6** (main's frame-robustness change, merged in c655b411): `SkiaRenderContext.BeginFrame` now calls `Canvas.RestoreToCount(1)` (SkiaRenderContext.cs:312). That also removes the window clip that the Vulkan surface sets on an over-allocated canvas just before it: the 8-bit kept target's `ClipTo` (SkiaVulkanRenderSurface.cs:61-72, 680; from 6c627f27) and the F16 offscreen clip (:870-883; from 1b11aeda). The order is surface `BeginFrame` (Window.cs:1856, sets the clip), then context `BeginFrame` (:1862, drops it).
  - Probe (clip.txt): the surface clip is 1200×800 at save count 2. After `BeginFrame` the save count is 1, the device clip is 2400×1600 and `ClipBounds` is 1601×1068 layout px instead of 800×533.
  - The cached `_clip` then skips putting the clip back until the size changes, and the next `BeginFrame` drops it again. So no Vulkan drag frame has been clipped since c655b411.
  - No shown pixel changes. The clear and any draws that hang over the edge now cover the allocated size: up to 255 px more each way (256 px buckets, SdlVulkanHost.cs:702), and more on a drag back inside a grown swapchain. A long menu draws its rows down to the allocated edge (it culls by `ClipBounds`, ContextMenuElement.cs:384-389).
  - 6c627f27 measured a variant without the clip as no faster, so this clears no time bar. It is a landed behaviour undone, so I list it as a candidate below.
- **18fe58a0 holds.** Walks of 200 notches down and back at 1.5×, with the emulation on the real build:
  - Table: 207–222 draws a frame, entries scanned flat at 285–311 a frame (before: 819 → 5,815), new GPU blobs 3.2 → 0.5 → 0, entries kept 440 (before: 2,206).
  - JSON TextArea: 52 draws, 51 scanned a frame, flat (before: 170 → 1,217), kept 320 (before: 581).
  - VirtualList: about 1 scan a draw.
  - This is exactly round 7's snapped model.
  - Matrix reads: 1 a frame on these walks, 1–7 a frame across the 41 app walks (7 at 412 draws), 1–4 on suite rows.
  - Draws not snapped: 0, except Junction commit files at 0.67 of 129.
  - With the emulation in the apps: Junction Changes wheel scans 416.7 entries for 412 draws (0.36 new a frame); Meridian History scans 233 for 231.
- **Suite, 190 rows shared with round 7's probe output:** only 6 differ, all down. Render KB a step:
  - text/open highlighted JSON 268.4 → 86.3 (d748b316)
  - richtext/open 3,364 → 3,145 (73819ebd)
  - richtext/resize 696 → 652
  - palette filter: overlay 101.2 → 84.9, elements drawn 211 → 199; also the UIA and AT-SPI palette-typing rows (8bd2a2a2)
- **The 23 new rows:** render 0–1.4 KB a step, apart from:
  - the "carry their place" twin rows (74–78 KB a step; these are the before-twins)
  - find Enter beside 3,000 rows (7.4 KB)
- **List-drawn tints** (ae3c5e70, c66d1bbb, 3ecbedfa, 5a9f8a54): `RenderUnderContent` allocates 0 B in every suite row and walk; palette Down renders 0 B.
- **The other f64e4674 paint commits** (efaf947b, 94d27324, e7965a2d, 534dfcf8): nothing added per frame.
- **4fcc9663:** paint code untouched.

CANDIDATES:
- id: paint-beginframe-keeps-surface-clip
  what: `BeginFrame` restores to the save count the surface handed the canvas over at, not to 1. For example, an internal `SetCanvas(canvas, baseSaveCount)`, with the Vulkan surface passing 2 while it clips. A throwing frame's pushes are still undone, and the drag clip stays.
  evidence: the review item above; clip.txt; SkiaRenderContext.cs:298-317, SkiaVulkanRenderSurface.cs:493-504.
  expected: restores 6c627f27's and 1b11aeda's clip (culling and clear at window size during Vulkan drags). It is a correctness fix of a landed change; no time bar is expected.
  cost: small; internal only, no pixel change. Tests:
  - a clipped canvas keeps its `DeviceClipBounds` through `BeginFrame`
  - FrameRobustnessTests still pass
  - the VulkanProbe check with a clip that is deliberately too narrow (6c627f27) shows a difference again
  Shared with expert 3 (the surface).
- id: paint-meridian-history-faded-row-layers
  what: Meridian History fades every send older than today with `row.Opacity(0.6)` (MeridianApp.History.cs:382). Each such row has children, so every visible row becomes a bounded offscreen group (SaveLayer) on every History frame.
  evidence:
  - Driver mdrv (coll25 fixture, 1440×900), layers a frame: Down/Up 29.0, wheel 29.4, hover 30.0. Walk host `history-down-60ms`: 29 a frame (231 text draws, 48 rounded rects); `history-search` 1.6.
  - Managed bytes: 0.
  - Mechanism: ElementRenderer.cs:71-85. Only a childless plain Box fades itself (BoxElement.cs:198-212).
  - Other walks: Junction 0 in 17 walks, Vesper 0 in 11, Aperture 0–1, Meridian's other views 0–1.6.
  - In Ganesh each layer is its own render target and render pass, and the parent's pass is split around it.
  expected: 29 layers → 0 a History frame (about 59 → 1 render passes). The time bar is unknown until a real GL/Vulkan History wheel frame is timed (build phase; there is no suite row with faded rows yet). Drop it if the layers come to under 5 % of the frame.
  cost: small in the app (Azimuth colours at 60 % alpha on each text and on the chip). Pixels change: glyph edges by ±1–2/255, and the chip text over its tint by up to about 0.24 × (tint − backdrop). The framework variant (a plain Text fades itself, as a plain Box does) is medium and still leaves the method chip as a layer.

WAITING, RE-RANKED:
- terminal-noop-scrollback-wheel: unchanged. A no-op notch still draws a whole frame (1.00 frame a notch): 5,440 / 5,682 glyphs, 836 / 882 draws, 112 B a notch with 12 listings. Still only the terminal sample uses HultUI.Terminal (no app project references it). Keep it low.
- terminal-row-strings-by-content: 10,276 B of render a streamed line (round 7: 10,812), all in TerminalElement. Same reason; keep it low.
- controls-menu-max-height-scroll: still no maximum height or scrolling. `ContextMenuElement.Render` walks every item each frame (arithmetic only, 0 B) and draws only the items inside `ClipBounds`. Not perf; it is expert 1's. With the review bug above, a long menu draws to the allocated edge during a Vulkan drag.

EXAMINED, NOT WORTH IT:
- Most render-phase bytes in the app walks are VirtualList row realization, which runs inside the render pass by design (`UpdateContentSize`, VirtualListElement.cs:965-981), not painting. KB a frame: Vesper Insert 17.5, partner 17.2, type-to-find 18.8, folder in/out 179, reader page 242.5; Aperture grid Down 47.9; Junction History 13.0, Commit files 50.4, Changes 300 Down 152.4, big files 496. Paint's own share is the text of newly shown or changed lines: reader page 16.8 KB for 32.7 paragraphs, big files 20.7 for 31, TextInput / TextArea / rich text 1–7.5 KB. This is for experts 1 and 8's phase splits.
- Horizontal fractions: GPU blobs are still keyed by the fraction of x. Wheel steps are whole layout pixels, so only touchpad or scrollbar pans make new fractions. Snapping x would move subpixel glyphs, which changes pixels.
- Junction History's commit affordance gradient: the shader cache keys by rectangle, so each Down makes a new shader (about 0.2–0.3 KB a key, 0.7 % of the 30.5 KB key).
- Palette Down: 3.7 KB of overlay render a step, from 5 TextElement draws laying out the new row's text (the text pipeline, expert 5's).
- Canvas text and halo text (94d27324): one native blob or path per call; only HelloWorld draws canvas text.
- Disabled buttons fade through a group (ButtonElement.cs:258), but the Junction, Vesper and Aperture walks draw 0 layers.
- Paint mix over the 41 walks: shadows 0, gradients at most 1 a frame (cached), layers 0 except Meridian History (29) and Aperture Home (1), rounded clips 21 a frame on Aperture's grid (GPU-side, as in round 7).

NEEDS THE USER:
- paint-meridian-history-faded-row-layers: whether the faded History rows may change pixels (edges ±1–2/255, chip text over its tint) to drop 29 layers a frame. Only worth asking once the build phase's GPU timing shows the layers clear 5 % of the frame.

FILES: everything is in `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\64e7e0e2-bc1c-4069-9d42-9311fee04c9e\scratchpad\r8-painting\`
- Probe: `probe-r8-tracked.patch` and `PaintProbe.cs` (round 7's probe ported to 27020cdb; the emulation runs with `PROBE_GPU=1`; render attribution in ElementRenderer)
- Comparison scripts: `cmp.py`, `newrows.py`
- Probe programs: `probe\` (R8Probe: table, walk, wheel, stream, clip), `mdrv\` (Meridian History counts), `walk\` (walk host with `src\Stub.cs`)
- Suite outputs: `suite-probe-r8.txt`, `suite-gpu-r8.txt`, `ab-r7-r8.txt`
- Walk and probe outputs: `walk-table-1.5.txt`, `walk-json-1.5.txt`, `walk-list-1.5.txt`, `terminal-wheel.txt`, `terminal-stream.txt`, `clip.txt`, `meridian-history-1.txt`, `walk-junction.txt`, `walk-vesper.txt`, `walk-aperture.txt`, `walk-meridian.txt`, `walk-junction-gpu.txt`

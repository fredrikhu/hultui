# Round 19 survey, expert 2: SkiaSharp painting

SATISFIED: no

**Setup.** My worktree was detached at 56d9a3f6. The probe was saved as `probe-r19.patch` plus `LayerProbe.cs`, then
reverse-applied; `git status` is clean, the build server is shut down, and no probe processes are running. Headless runs
used JIT and real windows used NativeAOT. The numbers are counts and bytes only.

**Junction fixture.** After the orchestrator's note I re-ran every Junction number on my own copy of jrepo
(`git reset --hard HEAD` first; the shared original was not touched).

- Headless paint numbers came out the same within 1 %.
- The real-window Junction run was redone in hold 2. Start private is now 98.2 MB; it was 973.5 MB on the dirty shared
  copy.

REVIEW: none in paint. Notes per commit:

- **d335159e (a pixel frame uploaded once per version): no objection.**
  - On this desk, Reel's and Aperture's video never takes this path. With NVIDIA's GL interop on, GL and Vulkan both draw
    video through `DrawGpuFrame`. Meridian's WebView2 preview uses the GPU sink too.
  - The path is reached only when interop is off (`HULTUI_VULKAN_INTEROP=0`, or a driver without GL interop), by the
    WebView's upload fallback, and by CPU frame sources.
  - Measured on that path with Reel driving `ProceduralBackend` frames (480×270, 30 fps) through `PixelFrameSink`:
    - Playing: one upload per new version (Vulkan 91 frames / 91 uploads; GL 79 / 79).
    - Paused with 30 pointer moves: 30 frames, 0 borrows, 0 uploads, 30 texture reuses. Before this commit each of those
      30 frames uploaded the whole picture.
    - Skia's cache stayed flat while playing (Vulkan 6.57 MB; GL 9.21 MB). The scratch pool is reused; it does not grow
      by one texture per version.
    - DXGI local stayed flat (27.9 / 23.5 MB).
  - What it keeps: one texture per drawn source, released at the end of the first frame that does not draw it.
  - Per frame it costs a dictionary lookup and a loop over an empty map, with no allocation. The upload simply moves from
    flush to draw time, with the same bytes.
- **bd3605ff (canvas text lines): no objection.**
  - No app uses `DrawTextLine` or `ScrollbarElement`. Measured on the suite's 60×160 canvas with 4 spans a line:
    - Scrolling a line makes about 3 native blobs a frame; the 59 kept lines reuse theirs. Render costs 2.1 KB a frame.
    - A jump makes 360 blobs a frame (6 a line, all text shaped cold) and 224 KB render a frame; that cost is text
      shaping.
  - Lines go through the label pipeline's layout cache and the sub-blob map, as labels do.
- **1951019d (desktop light/dark preference): no objection.**
  - It only reads SDL's value and raises an event; no app subscribes to it.
  - A theme switch itself (Junction, Options > Appearance, measured in a real GL window, 10 switches):
    - Each switch draws 1–2 frames and makes 0 new text blobs (glyph runs carry no colour).
    - Its 39 Phosphor icon rasters are re-cut each time. The previous batch is retired and disposed at the next switch,
      so the cache stays at 39 + 39.
    - After 10 switches Skia is +0.02 MB and DXGI local is flat at 26.2 MB.
  - About 200 KB of render-phase bytes per switch frame are the VirtualList re-making its rows inside
    `UpdateContentSize`, not painting.
- **0ec5182c (NumericUpDown paint bounds): no objection.**
  - `ValueStyle` is a struct `with`, so it allocates nothing. The bounds cost one `TextPaintBounds` and one `MeasureText`
    per field per faded frame, both cache hits.
- **1ddec166 (the drag ghost's own layer): no objection.**
  - The source subtree is walked once per drag frame; Junction's sidebar row is small.
- Main's other commits have no paint cost. Mnemonic underlines draw through spans only while Alt is held, and no app uses
  `UseMnemonic`. No new `Opacity` was added anywhere since 49bd5d81.

CANDIDATES:

- id: painting-aperture-hero-band
  what: Aperture decodes each item-page hero at the full height of the asked width, but draws it `Cover` into a box at
  most 420 logical px tall. Crop the decoded picture to the vertical band its box can ever show (the 420-px maximum at
  the current scale, plus a margin for the width step and filtering) before it becomes a `SkiaImageSource`. Then neither
  the kept pixels nor the GPU texture hold the rows that are never shown.
  evidence:
  - Real window, Vulkan, the fixture library, 1440×900 at 100 %, two launches in two holds with the same result:
    - The first item page adds +11.48 MB of picture textures; the largest single texture is 11.34 MB.
    - That texture is exactly the decoded 1408×2112 picture (1408×2112×4 B = 11.34 MiB, so no mip chain).
    - It is drawn into a 1376×396 box (scale 0.98/0.19, `Cover`): 18 % of its rows show.
  - The code's own numbers, in `ApertureApp.Cards.cs:651-659` with `HeroAsk`/`HeroPixels` and `HeroHeightAt` =
    clamp(0.44·h, 280, 420):
    - A 16:9 backdrop at 150 % in a 1440×900 window is 2176×1224 (10.65 MB) and shows 594 rows (49 %). Cropped to the
      420 band it is about 2176×670 ≈ 5.8 MB.
    - Maximized on the 4K at 150 % it is 3840×2160 (33.2 MB) and shows 630 rows (29 %); cropped about 10.3 MB.
    - "A film whose poster is its hero" (2:3) shows 18 %, as the fixture does.
  real app: Aperture item pages: every film or series page opened, and each Back to a kept hero. At least 2 heroes are
  kept (`HeroesAlwaysKept`), each held twice: decoded pixels plus a texture that Skia keeps while the picture lives.
  - 1440×900 at 150 % (r18b6's desk), arithmetic: about −4.9 MB per hero × 2 heroes × (pixels + GPU) ≈ −19 MB. Back at
    Home that is about −6 % of private (302.5 MB measured after 678a786c).
  - Maximized on the 4K: about −23 MB per hero × 4 ≈ −90 MB.
  - Not measured on the real server: only expert 8 signs in. Expert 8 should confirm the backdrop shapes on real items.
  expected: the retained-memory bar (≥5 % working set or GPU memory) on Aperture at 150 %. Far more in a maximized window
  or with portrait-poster heroes.
  cost: medium, Aperture only (the hero decode path in `PosterCache` and the band passed with the hero's ask), no public
  API.
  - Pixels: none intended. `Cover` is centred, so an integer-aligned band with a margin of 1 px or more should draw
    byte-identical frames. A headless fixture comparison of hero frames would guard it.
  - Decision for the build: with smaller heroes, the 24 MB `HeroBytesKept` bound would keep 4 heroes instead of 2 at
    150 %. Either keep the count and take the memory, or keep more heroes for Back.

REAL WINDOW:

- **Desk:** the 1440p only, 2560×1440 at 100 %, 200 Hz, primary. Steam was running; timer resolution 1 / 0.5 / 15.625
  ms.
- **Holds:** 2 holds, 11 launches, every window closed by WM_CLOSE. 0 foreground takes, 0 refused activations, 0
  nvlddmkm events.
  - h1: waited 139 s, held 123 s, gate failed (mean CPU 35 %).
  - h2: waited 584 s, held 97 s, gate passed.
  - h1's Junction launch was on the dirty fixture and is replaced by h2's. h1's Reel launches played nothing (Reel opens
    only a file that exists) and are replaced by h2's.
- **Aperture rest purge (Vulkan, fixture, Home after 6 item pages):**
  - Before: Skia 49.3 MB in 116 resources: pictures 31.8 MB (24.9 MB purgeable), upload staging 12.2 MB (all
    purgeable), stencil 1.2 MB, atlas and other scratch. DXGI local 74.7 MB, non-local 28.9 MB.
  - `PurgeUnusedResources(ms reaching back to just before the last frame)` (3,342 ms here): pictures go 31.8 → 6.9 MB and
    DXGI local 74.7 → 51.3 MB (−31 %). A second time-based purge frees nothing more.
  - A scratch-only purge then frees the staging buffers: 12.2 → 0.08 MB, DXGI non-local 28.9 → 16.9 MB, private 184.6 →
    161.2 MB (−12.7 %).
  - A paint-only frame after the purges re-makes 0 pictures and +0.08 MB of buffers.
  - Re-opening the last page re-uploads its pictures (+11.5 MB local, +12 MB staging) in the same frame: 1 frame on
    either side, no blank frame.
  - Identical in both holds, with the sides alternated.
- **The same purge on GL:** Skia's cache drops 52.3 → 16.0 MB but DXGI local stays 73.6 → 73.5 MB. NVIDIA GL keeps freed
  texture memory, so a purge buys nothing there.
- **Junction (GL, clean jrepo):** Skia 9.3–9.4 MB throughout. That is the 4 MB glyph atlas, a window-sized stencil and
  render target, and 0.03 MB of icons. Opening a commit, the wheel and 10 theme switches add 0.09 MB. The rest purge
  frees 0.06 MB.
- **Layers:** no unbounded layer in any mark of any app; 1–19 bounded layers a phase.

WAITING, RE-RANKED:

- **painting-controls-paint-bounds:** still not worth it.
  - All 9 `Opacity` calls in `apps/*` at 56d9a3f6 were re-checked, and none is new since round 18. None fades a
    Checkbox, Toggle, Dropdown, picker, Spinner, ProgressBar, Canvas or `Fit.None` image.
  - Meridian's response fade covers a TextArea or WebView; both are bounded.
  - Every real-window and headless walk drew 0 unbounded layers.
  - It stays a candidate only when an app fades one of these controls.
- **skia-rest-purge-vulkan (expert 3's), comment:** safe for the paint path on Vulkan, with three conditions.
  1. The time handed to Skia must reach back no later than the start of the last presented frame, as
     `SizeRestPurge.TakeDue` does. Then nothing on screen is re-uploaded (measured: 0).
  2. Every picture is raster-backed (`SkiaImageSource` keeps its pixels). GPU frames and d335159e's kept textures are
     held references that a purge never frees. So a purged picture re-uploads from its kept pixels in the frame it
     returns: no decode and no blank frame. That frame does pay the upload (+11.5 MB for a fixture page).
  3. It needs a scratch purge as well. Upload staging (12 MB here, 12 MB in r18b6) becomes purgeable only when Skia
     notices the GPU finished, which happens at the purge itself, so the time-based purge alone never frees it.
  - Gain on the fixture: −23.4 MB DXGI local (−31 %) and −12 MB non-local.
  - Not on GL: Skia's cache drops 36 MB but DXGI local does not move.
  - A smaller budget is not a safe substitute. Skia evicts by least recent use at every flush, during active frames. A
    budget below one frame's working set (a 4K page: a 33 MB hero, 66 MB F16 offscreen, posters, staging) re-uploads
    that frame's own pictures every frame. A budget large enough to avoid that saves less than a rest purge.

EXAMINED, NOT WORTH IT:

- **Text blob reuse while scrolling:** Junction's Commit diff wheel makes 5.6 blobs a frame going down (rows coming in)
  and 0 going back up. Vesper's reader: 2 then 0. Nothing is re-made for rows already drawn.
- **Hero mipmaps:** the hero is drawn at 0.98 scale and its texture equals the decoded bytes, so there is no mip chain
  (Skia disables mipmaps near 1:1). Posters are 0.69 MB each, also at 1:1.
- **Render-phase bytes on rebuild frames** (Junction commit open 213 KB a frame, theme switch 200 KB): element `Render`
  accounts for 137 KB of 970 KB on an open. The rest is the VirtualList realizing rows inside `UpdateContentSize`, which
  is layout and reconcile work for experts 1/6, not paint.
- **Phosphor icon cache:** 39–54 rasters (44–62 KB) in Junction, bounded by retire-then-dispose across theme switches. No
  app animates an icon's colour.

NEEDS THE USER:

- none

FILES (in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\e91920b9-3793-496a-9a73-f017250d8772\scratchpad\r19-painting\`):

- **Probe:** `probe-r19.patch` and `LayerProbe.cs` (apply at 56d9a3f6). Switches: `PAINT_BYTYPE`, `PAINT_OWNERS`.
- **Hosts:** `headless\` (JIT `LayerHeadless19`; walks theme, open, wheel, canvas-line, canvas-jump, apages, vrename)
  and `rw\` (AOT `PaintRW19`: Aperture rest purge with `PAINT_PURGE`, Junction, Reel procedural; Skia cache by kind,
  largest picture textures, on-screen picture scales). Shared fixture in `shared\Fixture.cs`.
- **Clean Junction fixture:** `fx\jrepo`.
- **Scripts:** `headless.ps1`, `pub.ps1`, `batch.ps1`.
- **Logs:** `holds.log`, `gate.log`, `runs\h1-*.out.txt`, `runs\h2-*.out.txt`, `runs\hl-*.out.txt`.

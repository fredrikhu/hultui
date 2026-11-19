SATISFIED: no

**Setup.** Worktree at 0df5e3ea, now clean: the probe was saved as a patch and reverse-applied with `git apply -R`. `dotnet build-server shutdown` has run. No probe process is left and no watcher. Numbers are counts and bytes only. Headless runs used JIT with every frame hashed where pixels mattered. Real windows used NativeAOT.

REVIEW:
- **96d00ab3: no objection.**
  - Headless at 2160×1350 (scale 1.5): the palette in Junction, Vesper and Meridian draws 14 bounded layers an open (0.58, 0.53 and 0.50 Mpx a frame) and 0 unbounded.
  - `Restyle` on each tween step allocates nothing. The +0.4 KB of closures is paid once an open.
  - No app's dialog, palette, popover, menu or toast still hits a window-sized layer.
  - **The one window-sized layer left in the apps is the video player's control bar** (candidate 1).
- **89732bec: no objection.**
  - Junction History headless, 20 keys at 1.5: 2 shader misses, 0.020 KB a frame in the render phase (round 16: 0.02).
  - Only three apps draw gradients: Junction's History affordance, Meridian's welcome rules (static) and Aperture's hero scrims (static except on resize). Reel and Vesper draw none.
  - Aperture makes 2 shaders per new window size (about 0.7 KB), only while resizing. No moving gradient is left.
- **Main's 8 commits: no objection in paint.**
  - 38ec9773 and b8a3dcf5 change no paint code; the list cursor draws through the app's own template.
  - Render-phase bytes per frame: Junction History keys 0.020 KB, Vesper list keys 0.000 KB, Vesper wheel 0.012 KB.

CANDIDATES:
- id: painting-player-bar-fade-layer (framework: `SliderElement.cs:216`, `VideoPlayer.cs:833–870`)
  what: The player's control bar fades by its `Opacity` (out over 0.3 s, 2.5 s after the pointer rests; back in over 0.15 s). The bar holds two Sliders and three Canvas glyphs (pause, chevrons, speaker) whose `PaintBounds` is null, so every fade frame draws the bar through a window-sized layer, and Skia keeps that texture for the window's life (the dialog fade's old problem).
  evidence:
    - **Headless** (Reel at 1280×760, scale 1.5, 120 Hz):
      - Each fade frame's layer is 2.19 Mpx (1920×1140, the whole window). The bar itself is 1280×52.
      - The probe names the layer's owner `BoxElement[video-controls]`, and a `CanvasElement` glyph as the first element with unknown bounds; the Sliders are unbounded too.
      - Prototype: the Slider's bounds are its content box grown by ThumbRadius + 4 (the thumb, a 3 px ring and its 1 px stroke); each glyph is a private `CanvasElement` subclass that reports its border box grown by 2. The layer drops to 0.152 Mpx (−93 %).
    - **Real window** (NativeAOT; Reel itself on `ProceduralBackend`, as no clip or mpv is set up here; first fade, then 6 reveal-and-fade cycles):

      | Renderer, window | First fade, DXGI local | First fade, private | DXGI local at end | Private at end |
      |---|---|---|---|---|
      | Vulkan, 1920×1140 | +12.0 → +0.0 MB | +13.5 → +2.6 MB | 67.5 → 55.5 MB (−18 %) | 126.0 → 108.8 MB (−14 %) |
      | Vulkan, client 3778×2044 | +32.0 → +2.0 MB | +22.5 → −6.5 MB | 194.4 → 164.4 MB (−15 %) | 162.2 → 136.1 MB (−16 %) |
      | GL, 1920×1140 | +12.0 → +2.0 MB | +23.2 → +2.0 MB | 49.0 → 39.0 MB (−20 %) | not compared (see below) |

      - GL private at the end is not compared: the first GL launch started 54 MB higher, NVIDIA's shader-cache miss.
      - Layer area: 7.72 → 0.298 Mpx a fade frame at 3778×2044.
      - The first fade drew 54 layered frames (28 at a visible alpha); the 6 cycles drew 418.
      - +32 MB is Skia's approx-fit 4096×2048 at 4 B a pixel. The 4K fullscreen would be 4096×3072: about 48 MB on 8-bit, 96 MB on an F16 HDR-video swapchain. **That is arithmetic, not measured.**
  real app: Reel and Aperture while playing. The bar fades after every pointer rest and comes back on every move, so anyone who watches a video and touches the mouse pays it. The first fade in each window commits the texture.
  expected: retained-memory bar (GPU memory).
    - DXGI local −12 MB at Reel's default size (−18 %) and −30 MB at 3800×2100 (−15 %); private −14 to −16 %.
    - Fill work on each fade frame: 0.15–0.30 Mpx cleared and composited instead of 2.2–7.7 Mpx.
  cost: small. No public API: it overrides an existing virtual, plus a private subclass in HultUI.Media. **It changes pixels slightly** (see NEEDS THE USER).
    - Headless pixel check (fake backend), 2 cycles: 61 of 111 frames are byte-identical. The other 50 differ by 1–2 pixels at most 1/255, all on the full-screen icon (a Phosphor raster resampled in a layer at another origin).
    - The frames before the fade, after it and while hidden are identical.
    - **Pixel-identical add-on:** skip a subtree whose `RenderOpacity` is 0 in `ElementRenderer`. While playing, the hidden bar is still drawn on every video frame at alpha 0 (Skia drops the draws). Elements rendered per hidden frame drop from 24 to 8 (−67 %), and 220 of 220 frames are byte-identical. Render bytes are unchanged.
    - **Wider scope:** Checkbox, RadioButton, ToggleSwitch, Dropdown, NumericUpDown, ProgressBar, Spinner, the date and time pickers, Calendar, Expander's part and Canvas also return null bounds. No app fades a subtree holding one today. Giving each its known reach would close the ledger's "still through a window-sized layer" note on 96d00ab3.

REAL WINDOW:
- **Setup:**
  - The 4K is primary at 3840×2160, 120 Hz, 144 dpi; the 1440p is at 2560×1440, 120 Hz, 96 dpi. The window was at dpi 144.
  - Vulkan ran B8G8R8A8 with Mailbox present, `hdrDisplay=on`, SDR white 240 nits.
  - Timer resolution 1 / 0.5 / 15.625 ms (Steam holds 1 ms).
- **Safety:**
  - 1 hold of 224.5 s, after a 244 s wait. The idle gate failed first (mean CPU 38 % over 60 s).
  - 6 launches, alternated by side: Vulkan 0/1, Vulkan at 3800×2100 1/0, GL 0/1.
  - 0 foreground takes, 0 refused activations, 0 nvlddmkm events.
  - The 3800×2100 window covered the user's resting cursor; its frame counts matched the small window's within 1 %.
  - **Script bug:** the first launch waited 70 s for marks my script never writes, then closed by timeout. The data is complete.
- **What only the real window showed:**
  - Skia keeps the bar's window-sized layer texture after the first fade (+12 / +32 MB DXGI local), and NVIDIA commits private bytes along with it.
  - Headless, the CPU layer is freed every frame, so only the area shows (2.19 → 0.152 Mpx).
- **Probe overhead:** render-phase bytes in real-window frames that drew a layer include the probe's own key strings. I read bytes only from frames with no layer: 0.249 KB a frame while playing with the bar shown.

WAITING, RE-RANKED:
- **junction-colour-landing-remakes-rows: the repaint cost is not mine.**
  - When colours land, `DrawSpannedLine` clips the same glyphs per colour stretch, and the new stretches' sub-run blobs are made whether rows are remade or recoloured in place.
  - Blobs are kept per ShapedRun, so a remade row with the same layout hits the cache.
  - Headless Commit open: one frame after the open had 264 blob misses (native blobs, 0 KB managed in DrawText). Its 378 KB of render-phase bytes are mostly rows realized in `UpdateContentSize`: 781 KB over 3 frames.
  - Rank it on build and measure (experts 6 and 5).

EXAMINED, NOT WORTH IT (or not mine):
- **Render-phase bytes that are row realization, not paint (for experts 1 and 6).** VirtualList realizes rows in `ScrollViewElement.UpdateContentSize`, which runs inside the render pass:
  - Vesper's viewer PageDown/PageUp: 27.6 / 25.5 KB a frame (531 KB over 4 calls).
  - Junction's palette typing: 45.9 KB a frame (394 KB over 5 calls).
  - Junction's Commit open: 781 KB over 3 frames.
  - Paint itself in those frames: 0.05 KB of text runs.
- **Meridian typing in an 89 KB body: 9.4 KB a frame (for expert 5).**
  - 3.7 KB is shaping the edited line (the pipeline's Layout) and 0.8 KB is runs; 2.3 native blobs a key.
  - About 5 KB is the TextArea's own line bookkeeping in `Render`, already incremental.
  - Text work for a changed line, not a steady frame.
- **Reel video frame: 0.242 KB a frame, all in VideoElement.** That is `TryWrapPixelFrame`'s `SKImage.FromPixels` per frame, on the pixel path only (the procedural backend, or software decode). mpv's GPU path keeps one wrapper per image (`_keptFrames`); not measured, since there is no clip on this machine.
- **Meridian greys its response at 0.45 while a send is in flight.** The layer is bounded by the TextArea when its scrollbar is inside, and Meridian has no animation during a send, so the layer is drawn only on frames that happen anyway.
- **Aperture's disabled page buttons and Junction's remote-op icon** fade Phosphor images, which are bounded or fade themselves.

NEEDS THE USER:
- **painting-player-bar-fade-layer: 1–2 pixels one level off during the bar's fade.**
  - Before/after numbers: −12 to −30 MB GPU memory and −14 to −16 % private per playing window.
  - Pixels: during the 0.3 s fade, 1–2 pixels of the full-screen icon are one level off (at most 1/255) in 50 of 111 frames. All other frames are identical.
  - It is the same kind of one-level change as question 68 (approved for the gradients).
  - Choices: (a) keep today's fade; (b) bounds with the one-level change; (b) plus the pixel-identical opacity-0 skip.
  - Images: `headless.ps1 -App Reel -Walk reveal -Extra "PAINT_FAKE=1;PAINT_HASH=1;PAINT_CYCLES=2;PAINT_DUMPDIR=<dir>"` with `PAINT_SLIDER_BOUNDS=0` and then `=1`, then `cmpdump.py <dir0> <dir1>`.

FILES (all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\35903ed7-ff10-416d-84e9-b6c09675ab6b\scratchpad\r17-painting\`):
- **Probe:** `probe-r17.patch` (apply at 0df5e3ea) and `LayerProbe.cs` (copy to `src\HultUI.Core\Rendering\`).
  - Counts layers bounded and unbounded, with their device area and owner.
  - Counts gradient misses, blob misses, text layout and run bytes, render-phase bytes, render bytes by element type, and elements rendered.
  - Switches: `PAINT_SLIDER_BOUNDS`, `PAINT_SKIP0`, `PAINT_BYTYPE`, `PAINT_NOOWN`.
- **Hosts:** `headless\` (JIT, `LayerHeadless`; walks `keys2`, `wheel`, `pal`, `f3`, `pgdn`, `type`, `reveal`, `open`, `click:<id>`, `focus:<id>`) and `rw\` (NativeAOT `ReelPaint`, guarded, DXGI marks).
- **Scripts:** `headless.ps1`, `pub.ps1`, `batch1.ps1`, `cmpdump.py`.
- **Logs:** `holds.log`, `gate.log`, `runs\vk-0`, `vk-1`, `vkbig-0`, `vkbig-1`, `gl-0`, `gl-1` (`.out.txt`), `runs\hl-*.out.txt`.

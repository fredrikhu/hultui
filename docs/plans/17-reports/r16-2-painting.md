SATISFIED: no

**Setup.** Worktree clean at 440ba012. The probe was saved and reverse-applied. `dotnet build-server shutdown` has run, and no PaintHost or PaintHeadless process is left. Numbers below are counts and bytes only. Real windows ran NativeAOT with in-process input; headless runs ran on JIT, every frame hashed.

REVIEW:
- **043f5b38 (SkiaSharp 4.156): no objection in the paint path.**
  - **Tests:** at 440ba012, 64 Rendering.Skia tests pass (Allocation, NativePath, Polygon, PaintEffectCache, GlyphOutlines, NativeTextBlob), and 45 Headless tests (FrameAllocationTests and the goldens).
  - **Real window, AOT:** render-phase bytes are 0.0 KB a frame for hover, wheel and palette frames in Junction, and for every phase in Vesper and Meridian. The only steady paint allocation is the History gradient miss (candidate 2), which is older than this commit.
  - **Pinned outlines:** `GlyphOutlines` / `TextOutlinePath` run only under `TextAsOutlines`, and only Headless `PinnedFonts` turns that on. Outline paths made: 0 in every app walk, headless (6 apps) and real window.
  - **Canvas and paths:** Junction's commit graph opens 14 canvas contexts a History frame (Béziers through `NativePath`) at 0 B. No app draws polygons.
  - **Gradients in the apps:**
    - Junction History's "Details →" affordance: 1 a frame while History shows.
    - Meridian's welcome rules: 2, static.
    - Aperture's Detail and Discover hero scrims: 2 each, static except on resize.
    - m156's +6 % gradient cost touches one rect a History frame.
- **9895efd6: no objection.** 0 dashed or dotted draws in all walks.
- **41e2775e: no objection.** No app uses `CursorType.Image`. A system cursor change costs one more compare. An image cursor is one `SDL_CreateColorCursor` per new instance, with up to 32 kept by instance.
- **5af9df10 and c39c9478: no objection.**
  - **Frames:** a software Junction walk drew the same frames as GL (wheel 40 for 40 notches, 1 per Down, 14 per palette open) with the same render bytes.
  - **Allocation:** 0 B a frame for the target. `PixelTarget` is a record struct, and the SKSurface is rebuilt only when SDL's surface moves or resizes.
  - **Cost is complexity, not allocation:** every frame rasterizes the full window on the CPU and BitBlts 2160×1350×4 = 11.7 MB. Each dialog-fade frame also needs a full-window CPU layer (11.7 MB, transient).
  - **Memory:** private 43.2 MB at the end of the walk, against 178–233 MB on GL.
- **00ee728d: no objection.** It copies `Decoration` into a span it already makes; 0 B.
- **Round 15's frames work holds** (headless):
  - Junction: 0 frames for 5 Shift, 5 Ctrl and a held Shift with 15 repeats; right-click releases 0 of 6; a History row press 0.
  - Meridian: click releases draw 1 frame in 6, 0 of them identical.
  - The numbered-label cache is in place (FrameAllocationTests pass).
- **ff3c12e6 and ae3ac740: no frames added.** `PreviewKeyDown` and `InputActivity` are null-checked, and no app subscribes.
- **The screen-reader focus event per move adds no frame:** real windows with the UIA bridge on drew 20 frames for 20 Downs in Junction, Vesper and Meridian.

CANDIDATES:
- id: painting-dialog-fade-layer (framework: Dialog.cs:98–100)
  what: `Dialog.Show` fades in the full-window scrim with `Animate.Opacity(scrim, 0, 1)`, so each of the 12–15 fade frames draws through a window-sized layer, and on GL Skia keeps that layer's texture for the life of the window.
  evidence: Real window, GL, the palette opened in-process with `TogglePalette`. This is expert 8's "first palette open commits 37–55 MB private".

  | App | Layers per open | Texture kept | DXGI local | Private |
  |---|---|---|---|---|
  | Junction (2160×1350) | 12 × 2.92 Mpx | 3072×1536 RGBA, 18.0 MB | 46.8 → 64.8 MB | +55.3 MB (still +42 at rest; WS +0.6) |
  | Meridian | 14 | 18.0 MB | 45.9 → 63.9 MB | +38.0 MB |
  | Vesper (1860×1230) | 14 | 2048×1536, 12.0 MB | 39.1 → 51.1 MB | +30–31 MB |

    - A second open adds 0. Two Junction launches gave the same figures (+55.4 and +55.3 MB).
    - Prototype "split" (the scrim's colour tweened with `Animate.Background`, the panel's own opacity in a layer of its paint bounds): Junction 0.62 Mpx a layer, a 4.0 MB texture, DXGI +8.0 MB, private +10.3 MB; Meridian 1024×1024 (4.0 MB), DXGI +8.0, private +13.7.
    - Prototype "none": 1 frame per open, +0 MB GPU, private +0.3 MB.
    - The fade predates the round, so the cost is on every build, as expert 8 found.
  real app: The first open of any Dialog in a GL app: the command palette in Junction, Meridian and Vesper, and Confirm and Alert dialogs. Once a window session; the memory stays until the window closes.
  expected: retained-memory bar (GPU memory).
    - No fade: DXGI local −18 MB of 64.8 (−28 %), private −42 to −55 MB.
    - Split: −10 MB (−15 %), private −27 to −45 MB.
  cost: small, but it changes pixels during the fade.
    - Split, headless at 2160×1350, palette frames: mid-fade, the panel's area (21 % of the window) differs by up to 35 of 255, with at most 105k pixels off by more than 8. The scrim area differs by at most 3. The last frame is identical.
    - A pixel-identical option on the GL side for expert 3: purge Skia's scratch textures once an overlay's opacity tween ends. The next open allocates the 18 MB again.
- id: painting-gradient-shader-by-size (framework: SkiaRenderContext.cs:602–631)
  what: The gradient shader cache is keyed by the rect in canvas coordinates, so a gradient that moves makes a new SKShader at each new place; key it by size and draw it translated.
  evidence:
    - The moving gradient is Junction History's affordance, which follows the focused row.
    - Headless History, 10 Downs and 10 Ups: 10 misses, about 350 B each (4.1 KB over 12 misses), 0.17 KB a frame.
    - Prototype (shader over (0,0,w,h), `Canvas.Translate` around the draw): 12 → 3 misses over two walks, and 0 of 44 frame hashes differ from today's.
    - Real window AOT: 20 of 20 History Downs miss, 0.2 KB a frame. That is the only render-phase allocation of a History key frame.
    - Each miss also makes a native shader. The cache clears all 256 entries at once when full.
  real app: Browsing History with the keys: every Down or Up onto a row not yet visited.
  expected: memory-churn bar, a per-frame allocation removed: 0.2–0.35 KB → 0 on History key frames.
  cost: small. No API change; pixels identical in the 44 frames compared. PaintEffectCacheTests' count expectations may need updating.

REAL WINDOW:
- **Setup:** the 4K was primary at 150 % (the window said 120 Hz; WMI said 119). The 1440p was attached to its left at 100 %. The UIA bridge was on (a11y=1). Junction and Meridian windows were 1440×900 logical (2160×1350 px); Vesper's was 1240×820.
- **Safety:** 2 holds: 107 s after a 77 s wait, and 134 s after a 146 s wait. 9 launches, 0 foreground takes, 0 refused activations, 0 nvlddmkm events. The idle gate timed out both times (CPU averaged 10.5–12.8 %, peaks 22–26 %). Counts only. No Aperture: expert 8 found Jellyfin sign-in failing.
- **Only the real window showed the palette's layer texture kept:** Skia's GL cache holds it, and NVIDIA commits 2.1–3.1× its size in private bytes while the working set barely moves. Headless CPU layers are freed every frame.
- **Expert 8's ~40 MB jumps (for expert 3): not paint.**
  - Junction's and Vesper's first launches had +40 and +38 MB from the first frame (149.6 against 110.6 MB, and 139.8 against 101.5 MB). Meridian had none, and the second launches had none.
  - Nothing in paint is behind it. Skia's cache was 15.33 MB at the first frame in both launches. No texture or render buffer was made, and the NVIDIA thread count stayed at 3.
  - Skia links the same programs at the same frames in every launch:
    - Junction: 5 at the first present, 6 at the fourth, 2 at the 12th Down (expert 8's "13th Down"), 1 at the palette's first frame.
    - Vesper: 11 at the first present.
    - Meridian: 6, then 1.
  - NVIDIA's GL disk-cache file for the exe was rewritten only after the first Junction launch.
  - **Conclusion:** the first program NVIDIA has to compile because its disk cache lacks an m156 program costs a process about 40 MB, kept, plus the long flush. Base never showed it because m119's programs were cached by earlier rounds.
  - **Check:** run a scene twice per exe; the second run shows nothing.
  - SkiaSharp 4.156 has no persistent program cache (plan 18), so there is nothing to fix in HultUI. It is a one-time cost per program per machine after a Skia or driver update.

WAITING, RE-RANKED: none named for me.

EXAMINED, NOT WORTH IT:
- **Keys in an empty Junction Commit tab:** 16 of 20 Down/Up frames identical (Style and Paint marks on 3 Commit-tab elements, headless). Same class as round 15's declined "keys the apps ignore"; rare.
- **Vesper:** PageDown/PageUp at the list end 10 of 10 identical, and F3 on a folder. **Meridian:** map keys 20 of 20. Both known.
- **A press on selectable Commit-view text** still draws an identical frame (the focus clause). Round 14's verdict stands.
- **Halo text** allocates a path each draw (`GetTextPath`), but no app draws halo text.
- **The 11.12 + 11.12 MB at the first frame** in Skia's dump (a RenderTarget and a StencilAttachment) are the wrapped default framebuffer.

NEEDS THE USER:
- **painting-dialog-fade-layer: which fade?**
  - (a) Keep today's single fade of the whole dialog: 18 MB of GPU memory and 38–55 MB private kept.
  - (b) Split: the scrim's colour and the panel fade separately. During the 0.12 s fade the panel's area is up to 35 of 255 darker; the last frame is the same.
  - (c) No fade at all.
  - (d) Keep (a)'s pixels and have expert 3 purge the texture after the fade.
  - Before/after images: the headless host's `pal` step with `PAINT_DUMPDIR` dumps every fade frame. Pairs at frames 2, 5 and 8 show it.

FILES: in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\b6ef64d9-4ed1-4793-9955-6a986d74de79\scratchpad\r16-painting\`
- **Probe:** `probe-r16.patch` (apply at 440ba012), plus `PaintProbe.cs` (copy to `src\HultUI.Core\Rendering\`) and `GlHookProbe.cs` (copy to `src\HultUI.Rendering.Skia\`).
  - The patch is round 15's probe re-based, plus the switches `PAINT_GRAD_LOCAL` and `PAINT_DIALOG_FADE=split|none`.
  - `PAINT_GLHOOK` wraps Skia's GL proc lookup: program links and compiles, texture, render-buffer and buffer allocations, and private bytes at each present.
- **Hosts:**
  - `headless\` (JIT): walk steps `keys2` and `pal`, plus `PAINT_HASHLOG` and `PAINT_DUMPDIR`.
  - `host\` (AOT `host\aot`): `PAINT_SCRIPT=r16`, with NVIDIA thread counts.
- **Scripts:** `batch16.ps1`, `idlegate.ps1`, `headless.ps1`, `pub.ps1`, `sum16.py`, `cmpfade.ps1`, `take_ours.py`.
- **Logs:** `holds.log`, `idlegate.log`, `batch-h1.log`, `batch-h2.log`, `test-alloc.log`, `test-frame.log`.
- **Outputs:** `runs\{Junction,Vesper,MeridianMap}-gl`, `Junction-sw`, `Vesper-gl2`, `Junction-{gl2,split,nofade}`, `MeridianMap-split` (`.out.txt`); summaries `sum-h1-*.txt`, `sum-h2-*.txt`; headless `runs\hl-*`; `runs\cmpfade.txt`.

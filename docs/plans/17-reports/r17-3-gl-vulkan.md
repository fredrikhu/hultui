SATISFIED: no

**Method.** Worktree detached at 0df5e3ea (SDK 11.0.100). I used my own NativeAOT publishes of round 16's scripted host (r16b10's `Script.cs`), built at 0df5e3ea for all five apps (`pub\aot17`). The host gained one probe, `HOST_SWAP=<n>`: after the first frame it calls `SDL_GL_SetSwapInterval(n)` on the UI thread and logs the interval it actually got. The driver is round 16's: per-thread context switches, memory by class, and DXGI `QueryVideoMemoryInfo` plus Skia's cache at each `gpu` step. All numbers below are bytes and counts; MB means MiB as DXGI reports it.

REVIEW: none. No commit in either range regresses GL or Vulkan.
- **96d00ab3 (dialog fade):** checked in every app that calls it. The only callers are the command palettes of Junction, Vesper and Meridian; apps' own modals have no fade, and Aperture and Reel have no palette. First Ctrl+P, DXGI local:
  - Vesper GL 1860×1230: +8.0 MB (39.14 → 47.14).
  - Vesper Vulkan: +5.0 MB.
  - Meridian Vulkan: +4.07 MB.
  - Near-maximized at 3778×2044: Junction Vulkan +4.0 MB, Vesper GL +8.0 MB.
  - A second open adds +0 local on all of them.
  - So the layer is the panel's own size in every app and at every size.
- **5766a137 (Aperture/Reel fullscreen order):** counted from code; nothing was toggled. A toggle draws 2 frames and rebuilds the swapchain once. The second frame's `ResizePending` reaches `SkiaVulkanRenderSurface.Resize`, which returns early when the size is unchanged (SkiaVulkanRenderSurface.cs:499). This is the same as before the commit; Aperture builds once instead of twice.
- **23b552d8 (app id):** no app calls `Application.SetAppMetadata`, so start-up is untouched. It adds one static bool set in the window constructor; HULTUI_STARTUP shows no new phase.
- **Main's other 7 commits:** none touch my area. No commit in 32c85e81..b131ae85 touches SdlVulkanHost, SkiaVulkanRenderSurface or SkiaGLRenderSurface.

CANDIDATES:
- id: media-bar-fade-window-layer (owner 3 with 2)
  - what: The video player's transport bar fades by its own `Opacity` (VideoPlayer.cs:275 `SetBarOpacity`). The bar holds the scrubber, a `SliderElement`, whose `PaintBounds` is null (SliderElement.cs:216). The bar's subtree therefore has unknown bounds, and every fade frame draws through a window-sized offscreen layer that Skia then keeps. This is round 16's dialog-fade problem in the media apps.
  - evidence (Reel on Vulkan, bands.y4m playing with real mpv, 1920×1140 px):
    - The bar auto-hides about 2.5 s after start. Skia's cache goes 4.27 → 16.27 MB at that point (+12.00, one 2048×1536 RGBA8 approximate-fit texture). DXGI local goes 272.21 → 284.21 MB.
    - Both stay there for the rest of the minute, through later show/hide fades (the texture is reused).
    - At 3778×2044, Vulkan's size-rest purge logged "skia cache 36.3 MB → 4.1": the 32 MB 4096×2048 layer.
    - By Skia's approximate-fit rule, a 3840×2160 fullscreen window would get 4096×3072 = 48 MB. That figure is not measured.
    - On GL nothing purges it; on Vulkan only a size change followed by a rest does.
  - real app: Reel and Aperture, every playback.
    - The bar fades out 2.5 s after the pointer stops (300 ms, about 36 frames at 120 Hz) and back in on each move (150 ms, about 18 frames).
    - Each of those frames clears and composites a window-sized layer just to fade a bar about 150 px tall.
    - Memory kept: +12 MB at the default window (4 % of Reel's 284 MB local while playing); about 48 MB in 4K fullscreen (about 12 %, estimated).
  - expected: GPU-memory bar in fullscreen (−44 MB estimated); at the default window it is below 5 %. The per-fade-frame fill also drops from the whole window to the bar; that is a time bar for the build phase.
  - cost: small.
    - Fix: give `SliderElement` exact bounds: its content box joined with the thumb's ring square (thumbX ± (r+o+1), cy ± (r+o+1)). Everything it draws lies inside that (SliderElement.cs:222–296).
    - No new API (an existing virtual's return value changes).
    - It also covers the remaining Dialog case 96d00ab3 left open: a panel holding a slider.
    - Pixels: the same content, but the layer's origin moves, so antialiased edges in the bar may differ by a few levels during fade frames only (the known layer-origin effect). See NEEDS THE USER.
- id: vk-swapchain-two-images (lead, owner 3; measure before building)
  - what: A windowed Vulkan window on this NVIDIA card costs about 19.5 bytes of DXGI local per pixel; GL costs about 13.
  - evidence: two up/down size cycles, each settling at the same number, so there is no leak.
    - Junction Vulkan: 68.61 ↔ 160.86 MB.
    - Reel Vulkan: 286.77 ↔ 375.02–379.02 MB.
    - Junction GL: 46.16 ↔ 107.47 MB.
    - On GL, Skia's cache counts the default framebuffer's D24S8 stencil (11.1 / 29.5 MB).
    - Vulkan's share is 3 swapchain images (`minImageCount + 1`, SdlVulkanHost.cs:835) plus about 7 B/px held by the driver. Our SDR path renders straight into the swapchain (no offscreen surface).
  - real app: Aperture and Reel in 4K fullscreen hold 3 × 31.6 MB of swapchain. Two images would save about 32 MB, about 8 % of local memory while playing.
  - expected: the GPU-memory bar, if frame pacing holds.
  - cost: small code but medium risk. Mailbox with 2 images can block on acquire, and HDR.md:484 records an image-count hazard. The build would have to count frames and acquire waits in Reel playback and in a drag first.

REAL WINDOW:
- **Setup:** every window on the 4K (DISPLAY2, primary, 3840×2160, DPI 144 = 150 %, 120 Hz); the 1440p was at 120 Hz to its left. Machine timer 1/0.5/15.625 ms in every hold (Steam). The user's real cursor was inside the window rectangle in most launches ("cursor IN"), outside it in Reel's.
- **Safety:** 4 holds of 168.6 / 183.3 / 200.3 / 220.5 s, after waits of 0 / 33.0 / 58.7 / 285.6 s. 28 launches, 0 foreground takes, 0 nvlddmkm events, no stall, the screen never read. The idle gate failed in all 4 holds (testhost, or CPU at 31 %), so I report counts only.
- **Rest wakes per second** (8 s, bounded by in-host marks, 0 frames in every rest):
  - GL: Junction 11.1, Vesper 11.5, Meridian 11.9, Aperture 10.5, Reel 12.0. NVIDIA's GL worker accounts for 10.0–11.2 of each; UI threads 0.2–1.4.
  - Vulkan: Aperture 112.1 and Reel 112.1 (NVIDIA's 4 threads 96 + 10 + 4 + 1; UI 0.8 / 0.4).
  - Junction, Vesper and Meridian forced onto Vulkan: 129.6 / 124.8 / 124.8. Their UI thread wakes 13.1–15.5/s from the 100 ms display-colour poll ("display colour polled" in HULTUI_STARTUP); Aperture and Reel turn on display-colour events and do not pay it.
- **Start-up (NativeAOT, warm shader cache):**
  - At the first frame: 3.7–5.5 MB allocated; 18 threads on GL (23 for Junction), 25–30 on Vulkan.
  - Phase order is as in rounds 11–16: on GL the driver loads inside `SDL_CreateWindow`; on Vulkan the instance and device are made before "present scaling".
  - Nothing new at 0df5e3ea.
- **GPU memory after a minute of normal use** (DXGI local / nonlocal, start → end):
  - Junction GL: 46.6/19.4 → 54.9/29.4. Skia's cache 53 → 121 resources, 15.5 → 19.8 MB.
  - Vesper GL: 39.1/16.5 → 47.1/28.5.
  - Meridian GL: 45.8/17.9 → 53.9/21.4.
  - Vesper Vulkan: 62.1/22.2 → 67.1/22.2.
  - Aperture (sign-in page): flat at 69.0/24.6.
  - Reel playing: 272.2/103.6 → 284.2/111.6 (+12 is the bar layer).
  - Local growth on GL is the palette panel's own layer (+8; +5 on Vulkan); nonlocal growth is NVIDIA's pool, which levels off. Process working set: Junction +48 MB (managed heap 23 MB), Vesper +60 MB, Meridian +12 MB.
- **5da967d2's lever, swap interval 1 (`got=1` confirmed), 50 scrollbar visits, swap 1 against swap 0:**
  - Vesper GL: nonlocal 16.03 → 28.52 at visit 5, then flat to visit 50 and through the rest, on both sides. A new 12.0 MB write-combined allocation appears at visit 5 on both.
  - Junction GL: 20.38 → 29.36 on both sides.
  - Frames per 5 visits: 90–94 on both, so pacing is not halved. The lever changes nothing.
- **Only the real window showed:** the bar fade's kept window-sized texture, Vulkan's per-pixel cost against GL's, the pool's indifference to swap interval, the 100 ms poll's wakes, and NVIDIA cache writes (Aperture and Reel on GL, the first palette open in Vesper, Meridian on Vulkan).

WAITING, RE-RANKED:
- **5da967d2's "not tried" lever (swap interval 1 during tweens):** closed by the probe above; no effect on the pool or on frames.
- **gl-threaded-optimization-profile:** information only, unchanged. NVIDIA's GL worker is still the only thread awake at rest, 10.0–11.2/s in all five apps.
- **vk-instance-extensions-once:** keep low. `HasInstanceExtension` (SdlVulkanHost.cs:532) still enumerates per call, 8 times at Init; the code is untouched in both ranges. It affects Aperture and Reel start-up only and stays below the 10 ms bar.
- **f16-held-drag-post-stall:** not run; it needs the user's approval. No Vulkan host or surface change in either range. Keep low.
- **Open: 8a38ef4 and 4315c4b on AMD/Intel and on Linux:** unchanged, and this desk has neither GPU. Nothing in either range touches the destroy-first path (SdlVulkanHost.cs:845) or the depth/stencil request. Suggest moving them to "needs other hardware" so surveys stop carrying them.

EXAMINED, NOT WORTH IT:
- **A Vulkan swapchain "leftover" after a resize:** a first reading looked like a spare 35 MB swapchain. Two up/down cycles return exactly (68.61 MB both times); it is the per-pixel cost above, not a leak.
- **The 100 ms display-colour poll on Vulkan without events:** 13–15 UI wakes/s, but only when a GL-default app is forced onto Vulkan by environment variable. The apps that ship on Vulkan (Aperture, Reel) use events. No real-app case.
- **The palette layer kept after the fade:** 8 MB on GL (a Skia purge does not return NVIDIA's memory, round 16), 4–5 MB on Vulkan, only in GL-default apps. Below the bar.
- **Reel's ~270 MB local while playing:** the CUDA/GL interop, already examined (mpv-cuda-context-at-rest, media-interop-context-release).
- **23b552d8:** nothing at start-up; there is no caller.

NEEDS THE USER:
- **media-bar-fade-window-layer pixels:** with exact slider bounds the bar's content is unchanged, but the layer's new origin may move antialiased edges by up to a few levels (Linux up to 8), and only during the 0.45 s of fade frames. The frames before and after a fade are untouched. Accept that (recommended), or ask for before/after images: a headless capture of Reel's bar at α = 0.5 on both builds. Decide this only if the build finds any difference.

FILES (all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\35903ed7-ff10-416d-84e9-b6c09675ab6b\scratchpad\r17-gl-vulkan\`):
- **Host:** `start\src\Script.cs` (r16b10's, plus `HOST_SWAP`), `start\src\Program.cs`, `start\Start.props`, `start\pub.ps1`, `start\pub-run.log`; publishes in `pub\aot17\<App>\`.
- **Driver:** `rw\common.ps1` (lock `Global\HultUI-r17-desktop`), `rw\hold.ps1`, `rw\Driver.cs`, `rw\Driver13.cs`, `rw\Mem.cs`, `rw\Desk.cs`, `rw\TimerRes.cs`, `idle-gate.ps1`.
- **Batches:**
  - `rw\batch1.ps1`: rest, start-up and palette.
  - `rw\batch2.ps1`: a minute of use on GL.
  - `rw\batch3.ps1`: up/down resize, plus a minute of use on Vulkan.
  - `rw\batch4.ps1`: swap interval.
- **Analysis:** `rw\an17.py` (`python -I an17.py <tag> [label] --wakes r0 r1 --frames`).
- **Data:** `rw\runs\h1..h4.jsonl`, `*.frames.csv`, `*.startup.txt`, `*.out.txt`, `h*-windows.log`; `locks.log`, `gate.log`, `rw\h1..h4.log`.
- **Fixtures:** `fx\jrepo`, `fx\coll25`, `fx\bands.y4m` (copies).
- **State:** no repo file changed (the worktree is clean at 0df5e3ea); build servers are shut down; nothing of mine is running.
- **For PERFORMANCE.md:** an element that fades by `Opacity` gets a window-sized layer whenever any descendant has null `PaintBounds` (Slider, Toggle, Checkbox, Dropdown and the rest). Check Skia's cache for a texture the window's approximate-fit size after the first fade.

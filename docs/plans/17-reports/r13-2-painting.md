SATISFIED: no

One candidate (dead-size stencils on Vulkan). The worktree is clean at 2ced4e26: the probe was reverse-applied and PaintProbe.cs deleted, and `dotnet build-server shutdown` has run. No PaintHost or PaintHeadless process is left. Counts and bytes only, no timings.

REVIEW: none.
- **c0ce9387 (the hover settle draws only when something shows hover):** no control misses its frame.
  - **What reads hover at paint time:** only `VirtualListElement.RenderUnderContent` and `RenderOverStickyRow` (both covered by `DrawsChildHover`), DevTools (covered), and the a11y menu-entry IsSelected (it notifies a11y itself, no frame needed).
  - **Hover styles:** they go through `InteractionStyles.Recompute`, which always calls `owner.MarkPaintDirty()`.
  - **Owned enter handlers:** in a settle, only `SplitterHandleElement` has one, and it marks.
  - **Owned leave handlers:** in a settle they reach only removed elements (`LeaveRemoved` up to the deepest shown element). The ones that could reach shown elements on a move all mark: ContextMenu `Hover(-1)`, Calendar, Slider, Terminal links.
  - **Hover drawn from a move's position** (menus, calendar days, slider fill, links): it needs a move event either way, so the commit changes nothing there.
  - **Dynamic check:** headless twin, pointer in the window. After every settle that asked for no frame, I forced one more frame and compared the pixels with SHA-256. Junction: 7 such settles checked, 0 differ. Vesper and Meridian have no settles in this script.
  - **Real window:** Junction's settle counts equal headless (12 declined, 3 marked). Draws stay 1 frame a key.
- **2d18a361 (8-bit until an HDR video):** paint path unchanged. Aperture signed in, real window, every phase:
  - Frames, draws, SaveLayers and layer pixels are identical on 8-bit, F16 and GL. Example: wheel ×40 is 28 frames, 885 text runs, 47 SaveLayers, 0.114 Mpx in all three; hover ×30 is 10 frames, 201 text runs, 10 SaveLayers.
  - Reel is identical on 8-bit and F16.
  - Glyph atlas: one 4 MB A8 page on all three.
  - Image textures: 42, 8.57 MB on all three.
  - Skia's budgeted GPU cache (GR budget), settled: Aperture 13.2 MB on 8-bit against 35.7 on F16 (F16 draws through a 22.8 MB budgeted half-float offscreen; 8-bit draws straight to the swapchain). Reel 4.2 against 20.9.
  - Skia's memory dump, which also counts the wrapped swapchain images: Aperture 46.5 MB against 102.4; Reel 20.9 against 54.3.
  - Per frame the commit adds two type tests and a field compare, 0 B.
- **Perf suite with the probe, JIT, 232 rows against round 12's:**
  - No draw counter moved ≥5 %.
  - Render/overlay KB: 10 rows up by 0.2–1.6 KB, 1 down.
  - The largest relative move: palette typing overlay 3.17 → 3.37 KB (+6.3 %). The TextInputElement fill grew 1,820.8 → 2,025.6 B a step: 7a753337's word-cache entry (48 → 56 B) while a fresh pipeline fills. Steady state 0. Below the bar.

CANDIDATES:
- id: painting-vk-dead-size-stencils (code in `SkiaVulkanRenderSurface`; expert 3's file, Skia cache policy mine; the orchestrator assigns)
  - what: a Vulkan window keeps one Skia stencil attachment for every window size it has drawn at, and nothing frees the old ones:
    - Skia shares stencils by a unique key, so they are not scratch resources;
    - the 8-bit path never purges;
    - the F16 path's `PurgeUnlockedResources(scratchResourcesOnly: true)` skips unique-keyed resources.
  - evidence: Aperture signed in, NativeAOT, real window, 4K at 150 %, 120 Hz, 2160×1350:
    - **Settled:** 1 stencil, 2.78 MB.
    - **After 18 size changes through 9 sizes** (SetWindowPos, SWP_NOACTIVATE): 9 stencils, 2,122–2,847 KB each, 21.77 MB in all, 19.0 MB of them for sizes no longer shown. GR budget 13.2 → 36.6 MB, purgeable 25.4 MB.
    - **F16:** the same 9 stencils, 21.77 MB.
    - **GL:** flat. One stencil (FBO 0's), budget 23.9 MB before and after.
    - **Reel's empty player:** draws nothing that needs a stencil (0).
    - **Prototype** (host calls on the UI thread after the resizes):
      - `PurgeUnlockedResources(true)`: 9 stencils stay (budget 34.6).
      - `PurgeUnusedResources(1000)`: 1 stencil, budget 11.2 MB.
      - That call also dropped 17 of 41 image textures. The next wheel pass re-made 7 of them from their CPU copies (0 decodes, 0 blob misses).
      - End state: budget 13.8 MB against 36.6 without the purge; process private 163.7 MB against 188.3.
  - real app: Aperture, every maximize or restore, snap or monitor move. Each new size adds W×H bytes for the whole session (8.3 MB for a maximized 4K window), capped only by the 256 MB budget.
  - expected:
    - Retained GPU memory: −19 MB after 9 sizes, about 27 % of round 12's 69 MB DXGI local at rest on 8-bit.
    - Scaling: removes growth that is O(distinct sizes).
  - cost: small. One deferred purge once a size change has rested: drop what has not been used since the change, keeping images drawn since. No API or pixel change.

REAL WINDOW:
- **Disturbance from my batch (reported by the coordinator):**
  - The first batch3.ps1 dot-sourced `driver.ps1.inc`. The name does not end in .ps1, so PowerShell handed it to the shell and it opened in Visual Studio on the user's desktop (devenv, "driver.ps1.inc - Microsoft Visual Studio"). The user saw it and closed it.
  - I cannot tell whether it took the foreground. A file opened through the shell usually activates its editor, so assume it did.
  - That run then failed before any launch (a bad run-spec parse). It took the lock for 0.1 s and started no PaintHost: no process, no out file.
  - Fixed: the include is now `driver.inc.ps1`. The batch checks that `[PaintDriver]` loaded and that every run spec is valid before taking the lock (`-DryRun` tested). Logged in holds-notes.log.
- **Launches and holds:**
  - 9 launches, all on the 4K (3840×2160) at 150 %, 120 Hz, HDR display on; RTX 4080, driver 610.88.
  - Holds: h1 0.1 s, no window (after a 336 s wait); h2 23:49:44–23:51:38, 114 s (waited 687 s); h3 00:01:31–00:04:24, 173 s (waited 461 s); h4 00:07:55–00:08:38, 43 s (waited 0 s).
  - The host took the foreground 0 times and refused 0 activations; the guard never fired. 0 nvlddmkm events. Input was in process only. Aperture signed in under the existing device id "aperture-r12", so no new device on the server.
- **Draws per interaction: real equals headless at 1.5 scale.**
  - Equal in every hover, key, click and wheel phase in Junction, Vesper and Meridian, frame for frame (one Junction wheel phase: 42 frames against 40).
  - Resize frames: Junction 16 against 11, Vesper 10 against 9. Both come from SetWindowPos made from another thread (known trap).
  - Settles equal headless.
- **Glyph atlas at 1.5 scale:** still one 4 MB A8 page on GL, 8-bit and F16 in all five apps, never a second. Meridian adds one 0.13 MB texture.
- **GL GR budget:** 13.0–15.6 MB in Junction, Vesper and Meridian, flat over 18 resizes. It includes FBO 0's stencil, 11.12 MB at 2160×1350 (4 B a pixel): accounting only.
- **GL programs:** Junction 5 → 12, Meridian 6 → 7, Vesper 11 → 11, all compiled by the first key.
- **Display-rate animations:** Meridian's Toast fades draw 27 frames with 24 bounded SaveLayers (0.576 Mpx) at 120 Hz. Round 12 saw 40 and 37 at 200 Hz; headless draws 7 and 5.

WAITING, RE-RANKED:
- **meridian-hover-frames-real:** explained, not a bug. Close it.
  - After the script's click opened the endpoint pane, the map list narrowed to x 312–880; its rows end at 868. The script's pointer column, x = 876, is then in the list's scrollbar band.
  - Real and headless agree that 29 of 30 moves change no hover: the hovered element is the list itself (Diag in both).
  - The first move puts the pointer on the bar. The bar's 0.12 s expand tween (`Scrollbar.ExpandDuration`) draws at the display's rate: 16 frames at 120 Hz (move log: 15 frames in the 130 ms after the first move, then none), 25 at 200 Hz in round 12, 3 with headless 40 ms pumps.
  - The import settle is the Toast's 0.12 s fade-in and 0.18 s fade-out (see REAL WINDOW).
  - Every one of these frames is a full-window repaint (100 text runs, 46 rrects).
- **hover-move-frames-nothing-shows (expert 1's item), paint side:**
  - A move that changes hover where nothing shows it (no style mark, no tint row, no handler) draws a frame with the same pixels every time. Headless SHA-256 check:
    - Junction: 62 of 62 such frames identical (13 + 29 + 10 + 10).
    - Vesper: 77 of 77 identical (28 + 28 + 1 + 10 + 10).
    - Tint frames (a list row's hover changed): 12 of 13 changed; the one unchanged was a header row that takes no tint.
  - Real window, such frames out of the hover pass's frames, and what each draws:

    | App and view | Hover-only / all frames | Draws per frame |
    |---|---|---|
    | Vesper file panel | 28 / 28 | 217 text runs, 79 images, 32 rrects |
    | Junction History | 13 / 26 | not split out |
    | Junction commit diff | 29 / 29 | 117 text runs, 33 paragraphs, 43 images, 50 rrects |
    | Aperture | 9 / 10 | 20 text runs, 25 rrects, 32 images, 1 SaveLayer |
    | Reel | 7 / 7 | 2 text runs, 1 rrect, 1 image |
    | Meridian map | 0 / 30 (all tint) | – |

  - Junction's and Vesper's click-to-select passes are 10 of 19–20 and 10 of 30 such frames.
  - Still worth it. The fix belongs in the window's input net, like c0ce9387: count the hover change as a reason only when `DrawnHoverChanges` moved or DevTools is open. Handlers are already their own clause. Every owned leave handler marks what it changes.

EXAMINED, NOT WORTH IT:
- **Capping short UI tweens below the display's rate:** frames scale with refresh (a 0.12 s tween is 15 frames at 120 Hz, 24 at 200 Hz). A cap would change smoothness on the user's high-refresh displays; with damage rects declined, each of these frames is simply a full frame. No candidate.
- **8-bit against F16 paint cost:** identical draws and layers. F16 adds only the 22.8 MB offscreen and twice-as-large swapchain images. Nothing left to do.
- **GL stencil accounting:** FBO 0's stencil counted in Skia's budget, flat; nothing to purge on GL.

NEEDS THE USER: none.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\17c11f5e-9de0-4477-8f58-0d0a5e006605\scratchpad\r13-painting\`
- **Probe:** `probe-r13.patch` is round 12's probe plus this round's additions: move classes, settle counters, an OS-move count, the Vulkan presents count and surface path, and a `PAINT_OLD_SETTLE` switch. `PaintProbe.cs` must be copied to `src\HultUI.Core\Rendering\` first, then the patch applied from the worktree.
- **Hosts:**
  - `host\` (PaintHost: Junction, Vesper, Meridian, Aperture signed in, Reel; Diag; the `PAINT_PURGE` prototype; AOT build in `host\aot`).
  - `headless\` (the twin, with the pixel-hash checker and the declined-settle check).
- **Scripts:** `batch3.ps1` with `driver.inc.ps1`, `headless.ps1`, `pub.ps1`, `tab13.py`, `cmp.py`, `kbdiff.py`.
- **Logs:** `holds.log`, `holds-notes.log`, `batch-h*.log`.
- **Outputs:**
  - `runs\*-b1`, `Aperture-vk8/f16/gl/vk8purge`, `Reel-vk8/f16` (real windows).
  - `runs\*-hjit-new/old/diag` (headless).
  - `tab-h2.txt`, `tab-h3-aperture.txt`, `tab-h3-reel.txt`, `tab-h4.txt`.
  - `suite-probe-r13.txt`, `cmp-r12-r13.txt`.

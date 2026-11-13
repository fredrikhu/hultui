SATISFIED: no

Two candidates: Vesper's release frame and the scrollbar tween's identical tail frames. The worktree is clean at e6f7b5aa: the probe was saved, reverse-applied and PaintProbe.cs deleted. `dotnet build-server shutdown` has run, and no PaintHost or PaintHeadless process is left. Everything below is counts and bytes, no timings.

REVIEW: none
- **4fdb56cb (stencil purge after a size rests): sound, and no on-screen texture is purged.**
  - **Setup:** Aperture signed in, NativeAOT, real window, 8-bit, 2182×1406. A wheel pass, then 18 SetWindowPos on the window's own thread through 9 sizes.
  - **Before the purge:** 104 resources, 34.71 MB in Skia's budget, 9 stencils (21.77 MB).
  - **The purge,** 1 s after the last change: 59 resources, 11.11 MB. It dropped:
    - 8 stencils, 18.99 MB;
    - 11 image textures, 2.58 MB;
    - 9 scratch buffers, 2.00 MB;
    - 17 small scratch surfaces, 0.03 MB.
  - **Nothing on screen had to come back:**
    - A forced full repaint of the same content right after made 0 textures, 0 stencils and 0 buffers; the only new entries were swapchain images wrapped again, which allocate nothing.
    - A second repaint made 2 buffers (0.06 MB); a hover pass made nothing.
    - The next wheel pass uploaded 7 images again (2.54 MB): the ones scrolled back into view.
  - **A second run of size changes** after that wheel pass dropped those same 7 images (2.54 MB); the budget went back to 11.11 MB.
  - **GPU memory (DXGI local / non-local):** 109.8 / 31.6 → 85.0 / 24.6 MB.
  - **Reel's empty player:** the purge drops 2 scratch buffers (0.09 MB; 4.21 → 4.11 MB, 9 → 7 resources). Its one on-screen image stays, and a repaint uploads nothing.
  - **No stray purges:** none at start-up or at rest.
- **0da9f80e (hover moves ask for no frame): no frame that should draw is lost.**
  - **Headless check:** serpentine sweeps over the whole window, every 20 px down and 36 px across, with a pointer leave every other row. Six apps: Junction, Vesper, Meridian's map, Fluent, Aperture signed in, Reel.
  - **Result:** 9,150 inputs drew no frame. Among them were 938 hover-only moves (the kind the commit stopped drawing) and 104 leaves. After each one I forced a frame, and all 9,150 were byte-identical to the frame on screen (SHA-256).
  - **Real windows, hover-only moves:** Junction diff 29 moves → 0 frames, Vesper 28 → 0, Reel 7 → 0, Aperture 9 moves → 1 frame.
- **2d18a361's 8-bit path after round 13: intact.**
  - Every Aperture and Reel frame goes straight to the swapchain as B8G8R8A8 (0x2c).
  - At rest after the purge: Aperture 11.11 MB, Reel 4.11 MB.
  - Presents equal drawn frames.
- **d73b1439's walk-only frames: they draw nothing.**
  - They return at Window.cs `if (!draws) return false`, before `Surface.BeginFrame`.
  - In 8 real-window launches, presents equalled drawn frames in every phase while walk-only frames ran: Aperture wheel 13 for 28 drawn, Junction wheel 19–20 for 40–42, Reel and Meridian resizes 18 for 18.
  - Those walk-only frames are the coalesced layout refresh (the desktop UIA listener had the bridge active in every launch), not d73b1439's. Its walk was asked once in all phases (Aperture's start).
  - Its click-handler notices, 0 walks each, for expert 7: Junction 226–233 per 20 Commit-view keys (about 11 a key); Aperture 44 and Vesper 162 per 18 resizes; Meridian 171 per 10 clicks.

CANDIDATES:
- id: painting-vesper-pointer-up-frame (app code: Vesper)
  what: Vesper's root `On<PointerUpEvent>` (CommanderApp.cs:776) is an ordinary handler, so every button release in the window asks for a frame, though it changes nothing visible:
    - on a right-button release, `CancelContextHold` stops a timer and `EndRightMark` clears a set;
    - otherwise it only clears `_findResizing`.
  evidence:
    - **Headless, 20 row clicks:** 20 release frames, all 20 byte-identical to the frame before.
    - **Prototype `root.OnPassive(...)`:** 0 release frames; the 20 press frames are unchanged.
    - **Real window** (AOT, 4K at 150 %, 120 Hz), 10 clicks: 20 frames, 10 with the prototype. The same 20 in both holds. Each frame draws 448 elements, 213 text runs, 79 images and 32 rrects.
  real app: every click in Vesper (selecting a file, buttons, tabs, a scrollbar drag's end), many times a minute.
  expected: time bar on an app frame metric, frames per click 2 → 1 (−50 % of a click's frame cost).
  cost: small (one line, OnPassive). No API or pixel change.
- id: painting-scrollbar-tween-tail (framework: `Scrollbar.SetHover`'s tween)
  what: the 0.12 s expand and collapse ease-out tween marks its owner on every step. Its last steps move the bar less than half a device pixel and redraw the whole window with the pixels already on screen.
  evidence: headless, pointer on and off the list's vertical bar, frames at the display interval, hashed. Same-pixel frames:

    | Case | Expand | Collapse |
    |---|---|---|
    | Junction and Meridian, 120 Hz at 1.5 | 2 of 15 (both at the tail) | 6 of 15 (all at the tail) |
    | Junction, 200 Hz at 1.0 | 6 of 24 (3 at the tail) | 11 of 24 (10 at the tail) |
    | Vesper, 120 Hz at 1.5 | 2 of 15 | 0 of 15 |

    - Real window at 120 Hz: every enter or leave draws 15 frames (16 the first time) in Vesper, Junction and Meridian. Each is a full repaint: Vesper 448 elements and 213 runs, Junction 329 and 136, Meridian 200 and 91.
    - These tail frames are also the only identical frames left in the hover sweeps: 24–40 a sweep, with the list marked while the pointer is in its right-edge band.
  real app: the pointer entering or leaving any list's scrollbar band: crossing Vesper's panes, reaching for a scrollbar in Junction's History or Meridian's map. Several times a minute.
  expected: time bar on an app frame metric, frames per bar visit −27 % in Junction and Meridian (8 of 30 at 120 Hz, 13 of 48 at 200 Hz). Vesper about 2 of 30.
  cost: small. End the tween at the target once the remaining travel is under half a device pixel (one final mark). No API change, no change to any settled frame or golden. Verify with this harness that every frame still drawn hashes the same as now.

REAL WINDOW:
- **What only the real window showed:**
  - Walk-only frames (headless has no UIA listener).
  - Scrollbar tweens at the display's rate: 15 frames at 120 Hz, against 3 with headless 40–50 ms pumps.
  - Junction's 10 clicks: 9–10 frames, all presses on Commit-view message text. Headless, 20 of 20 of these press frames are pixel-identical; they are not worth fixing (see EXAMINED).
  - GL non-local GPU memory steps up by 3–4 MB to about 29 MB in the first 25 s (Junction 19.4 → 29.4, Vesper 16.5 → 28.5), then stays flat, while Skia's budget stays at 15.6 and 13.2 MB. That is the driver's.
- **Launches and holds:**
  - 8 launches in 2 holds:
    - h1 22:42:10–22:44:34, 144.7 s, waited 0 s;
    - h2 22:58:24–22:59:53, 88.9 s, waited 171 s.
  - All on the 4K, primary, at 150 % (DPI 144): the window reported 120 Hz, the WMI adapter 119 Hz. The 1440p was attached at 100 %; its refresh rate was not read.
  - 0 foreground takes, 0 refused activations, 0 nvlddmkm events. Aperture signed in under the existing device "aperture-r12".

WAITING, RE-RANKED: none of mine.

EXAMINED, NOT WORTH IT:
- **Press on selectable read-only text** (Junction's commit message): 20 of 20 press frames are identical. Two causes: `TextElement.OnPointerDown` marks unconditionally (TextElement.cs:354), and the focus moving to the pressed text asks the input net for a frame. Cutting it needs the focus clause to know the text draws no focus, which is not worth it for that click target.
- **Junction's render-phase bytes on key passes** (6.9–16.8 KB a frame):
  - 97–99 % is `VirtualListElement.UpdateContentSize` realizing rows inside render, 134–257 KB per 20 keys in 3–4 calls. That is row realization (experts 1 and 6).
  - The paint calls themselves are 0.1–0.2 KB a frame.
  - Vesper's and Meridian's key passes allocate 0.0 KB in render.
- **GPU memory at rest:**
  - Aperture after the purge: 11.1 MB in Skia's budget (1 stencil 2.78, 24 images 4.17, the glyph atlas 4.0), plus 3 wrapped swapchain images of 11.1 MB each, which are not Skia's.
  - Junction on GL: 15.6 MB, of which FBO 0's stencil is 11.12 (accounting only).
  - The glyph atlas is still one 4 MB page everywhere. Nothing left to purge.
- **Hover-style frames with identical pixels:** Meridian's rail 4 in a 1,668-move sweep; Fluent 28, a sample only.

NEEDS THE USER: none.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\41943a9b-f657-4483-971a-a2b346b967f7\scratchpad\r14-painting\`
- **Probe:** `probe-r14.patch` and `PaintProbe.cs`. Round 13's probe plus walk-only, click-notice and purge counters, the `PAINT_WHY` dirty-path frame log, and the `PAINT_PASSIVE_UP` Vesper switch. Copy `PaintProbe.cs` to `src\HultUI.Core\Rendering\` first, then apply the patch from a worktree.
- **Hosts:**
  - `host\` (PaintHost, AOT in `host\aot`; scripts `purge`, `bar` and round 13's);
  - `headless\` (PaintHeadless JIT in `headless\bin-jit`; modes: sweep, `PAINT_CLICKS`, `PAINT_BAR`, `PAINT_KEYS`).
- **Scripts:** `batch14.ps1`, `driver.inc.ps1`, `headless.ps1`, `pub.ps1`.
- **Logs:** `holds.log`, `holds-notes.log`, `batch-h1.log`, `batch-h2.log`, `headless-*.log`.
- **Outputs:** `runs\*.out.txt`; real windows are `Aperture-vk8`, `Reel-vk8`, `Junction-gl`, `Vesper-gl`, `*-bar`, `Vesper-barpassive`; headless runs are `hl-*`.

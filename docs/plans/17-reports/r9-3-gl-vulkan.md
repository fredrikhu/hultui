SATISFIED: no

I measured at ea3e57ab (detached; `src` and `apps` are the same as 10acc8c0) with `HULTUI_A11Y=0`. I report counts only.

**Monitor change:** the 4K monitor at 150 % is now attached and primary (system DPI 144). So every window this round was at scale 1.5: the 800×533 probe window is 1200×800 px.

**Real windows: 30 launches.** Each one had the default frame, SDL's no-activate hint set in process, the CBT hook plus `WS_EX_NOACTIVATE`, and the 5 ms foreground guard. This round the guard also counts new `msedgewebview2` processes as ours. Each window closed with a posted WM_CLOSE.
- Guard fired 0 times and the hook refused 0 activations.
- The screen was never read.
- No device lost and no stall over 1 s. The only steps over 100 ms were the first F16 drag step, 316 and 586 ms (the known first F16 swapchain build).
- The last `nvlddmkm` event is still 2026-10-25. The F16 kept-target drag was not run.
- Worktree clean: the prototypes were undone by reverse edits.

REVIEW: none.

- **f3302209:**
  - Steady frame: 0.0 B on GL, Vulkan 8-bit and F16, JIT and AOT (least of 5 rounds of 120 frames).
  - 8 s idle after a drag: 0 frames, 0 loop passes, 0 wakes on all six.
  - The clip holds. Each launch drew 83–84 drag frames inside WM_ENTERSIZEMOVE (Vulkan: 8-bit kept target, F16 offscreen). In none did the root's `ClipBounds` exceed the window; the worst excess was +1.3 layout px, Skia's outset. A lost clip would show up to 255 px.
  - Bytes a drag frame, JIT/AOT: GL 2,080/1,984, Vulkan 8-bit 136/40, F16 2,136/2,040; 1 frame a step.
  - Per frame the commit adds one int write in `SetCanvas`. `SaveCount` is read only when the clip changes.
- **307ee07c (Meridian History, 29 layers a frame → 0):** holds as step 9b measured it, so I did not re-run it. `git diff 307ee07c 10acc8c0 -- src apps samples tests` is empty, and no `.Opacity(` is left in the History files.
- **180d2579 (Meridian web preview):** I built a probe of the same code path in a guarded 1440×900 window. It uses `WebView2Engine`, the CSP string page, `MessageOrigins = []`, refuses navigation away from the document, and puts a `WebViewElement` in the lower pane (page 2160×779 px).
  - Steady frame with the page on screen: GL 0.0 B (JIT and AOT); Vulkan 24 B (candidate 2).
  - 8 s idle, shown and hidden, GL and Vulkan: 0 frames, 0 page frames, 0 passes, 0 wakes.
  - GPU memory: candidate 3, and the Meridian question under Needs the user.
  - 5 open/close cycles: memory stays flat after the first.
- **e7d10c63 (`Window.cs`, a selection dragged past a view's edge scrolls it):**
  - Per frame: one `TimeProvider.GetUtcNow` and some field tests.
  - It asks for frames only while a selection is held past an edge the view can still scroll toward, and the button's release clears it. No idle cost.
- **Others (4fc94836 PaneSplitter, rich text, a11y, apps):** they touch no GL, Vulkan, present or resize code.

CANDIDATES:
- id: gl-vk-live-resize-stuck-post
  what: While a resize is held, `RenderLiveResizeFrame` draws a clean frame on every live-resize event whenever the main-thread queue is non-empty (Application.cs:573, :583–586). A post that cannot run until the drag ends therefore redraws the same picture on every SDL timer tick. Examples: `WebViewElement`'s placement flush (WebViewElement.cs:198–202, queued at the first step and stuck until the end), or any `InvokeOnMainThread` or await continuation. Fix: draw for posted work only when something was posted since the last such frame, for example by comparing `_mainThreadQueue.Count` with its value at that frame, reset when the queue drains.
  evidence:
    - Method: a guarded real window, a synthetic drag (SetWindowPos with NOACTIVATE inside WM_ENTERSIZEMOVE), then 1 s of only pumping messages, which is an edge held still.
    - GL plain window: 0 frames in the held second, 1.0 frame a step.
    - One no-op post a step: 64–65 frames in the held second, 2.0 frames a step. Bytes a step stay 2,080, so the extra frame is a clean redraw.
    - Meridian's preview on screen: held before any step 0 frames, after the steps 65 (GL) and 64 (Vulkan 8-bit); 64 frames for 32 steps, against 32 with the view removed.
    - Side effect: 0 page frames arrive during the drag, so the page keeps its old size until the drag ends.
  expected: Time bar, as frames drawn during a resize. Held edge with a post waiting: 64–65 frames a second → 0–1 (−98 %). Each drag step: 2 frames → 1 (−50 % of resize frames). This applies to any app with a web view on screen or a continuation pending. Timings are for the build step.
  cost: small. No API change; no pixel change, since the dropped frames redraw the same picture. A new post still draws once, so a GPU source that signals by posting keeps its rate.
- id: vk-interop-check-allocates
  what: `SdlVulkanHost.TryEnableInterop` (SdlVulkanHost.Interop.cs:143–188) creates its uuid lambda's closure (`<>c__DisplayClass193_0`, field `byte[] vkUuid`, 24 B) on entry to every call, including the cached early return. `SkiaVulkanRenderSurface.SupportsExternalGL` (:281) calls it on every draw of a GPU source (SkiaRenderContext.cs:1484 and :1693) and from `ServiceUndrawnSources` (Window.cs:1580).
  evidence:
    - Web view on screen, forced repaints: Vulkan 8-bit 24.0 B a frame (JIT and AOT), F16 24.0, GL 0.0, no view 0.0.
    - 20,000 frames: 24.8 B a frame, and every GC allocation tick was that closure type.
    - Prototype with a loop instead of the lambda: 0.0 B on 8-bit and F16, 0 ticks.
    - By the code, a Vulkan mpv video frame takes the same path.
  expected: removes a per-frame allocation from a steady frame: 24 B → 0 per GPU source per Vulkan frame.
  cost: small; no API or pixel impact.
- id: webview-hidden-keeps-interop
  what: A web view whose element leaves the screen only gets `put_IsVisible(false)` (WebView2View.cs:259–264). It keeps the capture's D3D11 interop texture, its `WGL_NV_DX_interop2` registration (VisualCapture.cs:343–398) and the sink's own GL texture. Meridian keeps its preview controller for the app's whole life (`_preview ??=`), so after one HTML preview this memory stays while the user is on pretty, raw or a JSON response. Fix: on hide, release the interop and the sink's texture in a frame with their GL context current; recreate them, or restart the capture, when the page is shown again.
  evidence:
    - DXGI `QueryVideoMemoryInfo`, this process, local; GL: no engine 45.3 MB; shown 90.7; hidden 90.7 (all of it kept); controller disposed 45.3.
    - Prototype that also closes the frame pool and session on hide: still 90.7. The capture's own buffers are not in this process's figure.
    - Prototype that also releases the interop texture and its GL registration: hidden 68.4 MB (−22.3 MB, −25 %). Shown again, the page frame arrives and memory is back at 90.7.
    - Vulkan 8-bit with the same prototype: hidden 139.1–146.5 → 124.2 MB.
    - Scaling: a 2160×300 px page adds 25.3 MB, a 2160×779 page adds 45.4. That fits about 4.9 page-sized buffers plus 12.7 MB fixed (the view's D3D11 device and compositor); the interop is about 3.3 of those buffers.
  expected: retained GPU memory −22 MB (−25 % of the process) while a preview is hidden at this size; about −29 MB (−32 %) with the sink's texture too (one page buffer is 6.7 MB; not prototyped).
  cost: medium, because the GL names need their context current at hide time. No API change. A static page sends no new frame on show, so releasing the sink's texture means restarting the capture on show; the prototype did that and its page frame arrived.

WAITING, RE-RANKED:
- **gl-threaded-optimization-profile: still information only.**
  - The walk fires once per GL launch at start-up (JIT 1/1, AOT 1/1).
  - Later burst, `SwitchToThread` per paced frame: mean 357 (JIT) and 344 (AOT), max 456 and 578. Round 8: mean 340–387, max ≤ 986.
  - It is the user's NVIDIA driver setting; there is no framework lever.
- **F16 kept drag targets: stays deferred; nothing new justifies running it.**
  - The only new number: an F16 drag frame allocates 2,136 B (JIT) / 2,040 B (AOT), against 8-bit's 136/40. That is the per-size swapchain wrap kept targets would remove.
  - That is drag-only, not steady: about 0.4 MB/s at 200 Hz.
  - Round 5's `nvlddmkm` reason is unchanged. Not run.
- **Open items:**
  - f9bceb6d's window checks are now possible (see Needs the user).
  - 8a38ef4 and 4315c4b need other GPUs; there is none here, so they stay open.

EXAMINED, NOT WORTH IT:
- **RGBA16F interop image for an 8-bit page on Vulkan:** 13.5 MB against 6.7 MB for RGBA8 at 2160×779 px. That is 4.6 % of the process's 146.5 MB, Vulkan is opt-in, and round 5's verdict stands.
- **Closing the WGC frame pool and session on hide:** frees 0 MB in this process's figure.
- **GL drag frame, 2,080 B:** the FBO 0 re-wrap; round 5 found it cannot be kept.
- **GL drag private commit:** 89.9 → 336 MB during a drag, back to 94.6 after the idle, working set flat. NVIDIA's GL driver, as round 2 found.
- **Web view open/close cycles:**
  - After the first cycle, GL keeps +7.5 MB (about 1.1 page buffers). It stays flat over 4 more cycles and after the engine is disposed: bounded, not a leak.
  - Vulkan stays flat at 78.9 MB.
  - The browser processes exit when the last controller goes (6 → 0), even while the engine lives.

NEEDS THE USER:
- **Meridian: dispose the web preview when it leaves the screen?**
  - Gain: all 45.4 MB of this process's GPU memory (90.7 → 45.3). Also the 6 `msedgewebview2` processes (283–315 MB working set, 140–198 MB private, shown or hidden), which all exit at dispose. nvidia-smi for the whole GPU is too noisy (±100 MiB) to add a figure.
  - Cost: the next "web" toggle starts the browser again; the first page frame took 395–673 ms after `NavigateToString` in 5 runs.
  - This is app code. [Recommend: dispose after about 60 s hidden, so a quick toggle back stays instant. Candidate 3 then covers the minute before that.]
- **f9bceb6d's window checks can now run.** The 4K monitor at 150 % is attached and was primary at sign-in (system DPI 144); the 2560×1440 at 96 DPI is on its left.
  - `WindowPlacementTests` and the Borderless `WindowStateActivationTests` (`HULTUI_UITESTS=1`) show maximized and full-screen windows over the desktop. I did not run them because you were active (Discord in the foreground, input seconds earlier).
  - Say go, or have them run while you are away. The Snap Layouts hover stays by hand.

FILES: `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\c484f5ac-8f22-48dd-b669-13c24b1e7889\scratchpad\r9-gl-vulkan\`
- Probe host: `host\Program.cs` (round 8's R8GlHost, now with an STA `Main` and a guard that also watches new WebView2 processes), `host\R9.cs` (modes `drag` and `webview`), `host\ImportCounter.cs`, `host\R9GlHost.csproj` (AOT with `-p:R9Aot=1`).
- `host\R9.cs` switches: `PROBE_DRAG_POST` (a no-op post a step), and DXGI/nvidia-smi readings, an allocation-tick listener, and `PROBE_TOP`, `PROBE_WEBDRAG`, `PROBE_WEBDRAG_HIDDEN`, `PROBE_ALLOC_FRAMES`, `PROBE_SHORT`.
- AOT build: `aot\R9GlHost.exe`.
- Runner: `run.ps1` (also prints `nvlddmkm` events since start); monitor list: `monitors.ps1`.
- `runs\`: stdout and stderr of all 30 launches. `drag-*` and `aot-drag-*` are the steady, drag and idle runs; `drag-gl-post` and `drag-gl-hold*` the held-edge counts; `walkrun-gl-1` and `aot-walkrun-gl-1` the thread walk; `webview-*` and `aot-webview-*` memory, idle and steady bytes; `webview-*pause*` the hide-release prototypes; `webview-vk*-alloc` and `-noclosure` the 24 B hunt and its fix; `webdrag-*` the drags with the page on screen.
- Diffs read: `diff-f3302209.txt`, `diff-180d2579.txt`, `diff-e7d10c63-window.txt`.
- Build logs: `build*.log`, `publish*.log`.

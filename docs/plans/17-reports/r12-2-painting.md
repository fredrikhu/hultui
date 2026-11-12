SATISFIED: yes (painting). One cross-area candidate for the Junction owner is listed below; it is not paint code.

Setup: worktree at f9c44628, SDK 11.0.100. Round 11's probe (probe-r11.patch) applied cleanly. I added layer pixels, the GR context, presents and GL object counts on top. Everything was reverse-applied and PaintProbe.cs deleted; `git status` is clean and `dotnet build-server shutdown` has run. Counts and bytes only, no timings.

REVIEW: none.
- **Suite, JIT, 230 rows shared with round 11:**
  - No paint counter moved ≥5 % (`cmp.py`).
  - Strict KB diff: 46 rows down (the word cache), 5 up.
  - Up rows: `richtext/open 5,000 blocks` +2.6 KB (first open); `virtual/section walk` ×2 +1.2 KB, which is 7d823b34's table doubling while it fills (known).
  - `a11y/command palette typing` UIA/AT-SPI went 1.94 → 3.17 KB overlay render: TextInputElement 560 → 1,820.8 B a step, stable over 5 and 20 rounds. Bisected to 7d823b34 by reverse patch. It is the same fill effect, because each round has a fresh pipeline: with the table made at 8,192 slots the row reads 480 B, below round 11's 560. Not an objection.
- **91e5559c (merged theme):** 0 paint cost.
  - Dark+ spans before/after on 8 files (Window.cs, SkiaRenderContext.cs, JunctionApp.cs, a 3,692-line JSON, CSS, JS, Python, Markdown): span counts identical (13,942 / 8,917 / 4,359 / 6,461 / 996 / 6,588 / 707 / 1,151). Only the colours change (hashes differ).
  - 0 touching same-colour spans either way, so no sub-draw is added or merged.
  - The suite's JSON rows use hard-coded spans and are byte-identical.
- **1c7dc920 + 1879ba78 (the per-side ring):** headless Meridian map, 600 endpoints, the rows drawing the selection (1879ba78 reversed) against the list drawing it.
  - Every phase's draws are identical: click10 911 text runs, 409 rrects, 54 borders, 9 frames either way.
  - The rounded per-side ring is 1 fill + 3 save/clip/stroke pairs (1 rect clip, 2 antialiased triangle clips), the same calls the row's own box made.
  - Render bytes are down: 812 → 759.5 KB per 10 clicks, 872.6 → 809.5.
  - On the GPU, the GL program count did not move on map clicks (9 before, 9 after).

CANDIDATES:
- id: apps-junction-diff-width-follow-same-frame (owner: Junction/HorizontalPan, expert 8 or 1; not paint code)
  what: In Junction's diff view, 7ca2b3db's after-frame width follow (`FollowDiffWidthAfterFrame`, JunctionApp.Diff.cs:724) draws a second full frame after every window size change; the thumb could be given its width before that frame renders.
  evidence:
  - Real window: Junction after a commit click, 9 size changes 200 ms apart (SetWindowPos, SWP_NOACTIVATE), twice. 20 frames each time (1,693 text runs). Vesper and Meridian draw 10 for the same steps.
  - Headless with real time between steps gives the same 20.
  - With the follow switched off (probe env, reverse-edited after): 14 frames, 1,225 text runs (−6 frames, −28 % text runs).
  - Round 11's survey had assumed the second frame merges into the next move's frame. It does only when sizes arrive faster than frames.
  real app: Junction, maximize/restore, snap, a monitor move or a slow edge drag in History or Changes with a diff shown: a few times a session. One extra full frame each (~85 text runs, ~43 rrects).
  expected: time bar on that action, about −30 % of the frames per discrete size change. Fast drags gain nothing. Low rank. The other 5 extra frames (14 vs Vesper's 9–10) are not attributed.
  cost: small to medium; no API or pixel change.

REAL WINDOW:
- **Setup:**
  - 1440p (2560×1440) at 200.01 Hz, scale 1, RTX 4080, GL 3.3 NVIDIA 610.88 (depth 0, stencil 8). Aperture on Vulkan F16 Mailbox.
  - NativeAOT host with the probe: Junction (round 11's jrepo fixture), Vesper (fixture folders), Meridian (coll-r9 workspace + spec600 import, in-memory keyring) and Aperture (sign-in page).
  - Same script in each: hover ×30, wheel ×40, click, Down/Up ×20, all twice; 10 row clicks ×2; 9 resizes ×2; idle. Input went in process to the window's own input handler.
  - An AOT headless twin ran the same script (CPU raster).
- **Launches and holds:**
  - 4 launches in 1 hold: 08:49:29–08:52:02, 152.9 s, after an 802.7 s wait.
  - Two earlier batches were stopped while still waiting for the lock (no window opened). The second was stopped because my clicks used button 1, which is middle; left is 0.
  - 0 foreground takes: the guard never fired and 0 activations were refused. 0 nvlddmkm events.
- **Draws per interaction: real equals headless.**
  - Hover, keys and clicks match in frames and every draw counter. Example: Vesper hover, 28 frames and 6,076 text runs both ways.
  - Junction wheel draws 42–43 frames for 40 notches against 40 headless.
  - 0 paths made, 0 blur or shadow misses in any app. Junction and Vesper use 0 SaveLayers.
- **What the real window adds:**
  - **Start-up:** frames arrive as loads land in real time. Junction 13 frames and 1,527 text runs to settle against 5 and 661 headless; Vesper 5 against 1. The first real frame is lighter (Junction 44 text runs vs 117), because it is shown before the repository arrives.
  - **Animations run at the display's 200 Hz:** Meridian's settle after the import is 40 full-window frames with 37 bounded SaveLayers (0.41 Mpx, ~11,000 px each). Headless at 50 ms pumps draws 7 frames and 5 layers.
  - **Unexplained:** Meridian's second hover pass is 25 frames real against 3 headless.
- **GPU side (GL):**
  - Skia's budgeted cache stays flat: Junction 9.13 → 9.35 MB, Vesper 8.12 → 8.28, Meridian 9.18 → 9.34, against a 256 MB limit. Nothing grew over 18 resizes, 0 purge calls, and none needed.
  - Part of each budget is the stencil buffer Skia attaches to the window's own back buffer (4.94 MB at 1440×900; Skia counts it, the driver holds it).
  - **Glyph atlas:** one 2048² A8 page (4 MB) in every GL app, never a second. Meridian adds one 0.13 MB texture.
  - **Text blob cache:** 0.03 → 0.28 MB. CPU glyph cache 30–155 KB.
  - **Images:** each is uploaded once (Junction textures 33 → 55 as icons appear). 1,066 image draws over 26 hover frames added 0 textures.
  - **First-use shader work:** GL programs at the first frame → final: Junction 5 → 13, Vesper 11 → 13, Meridian 6 → 9. All are compiled by the first wheel or key; 0 after.
- **UI-thread bytes:** real equals headless plus my probe's own memory dump (~1 KB per GPU resource, 30–97 KB a snapshot). No hidden per-frame allocation in the GL present path.
- **Vulkan (Aperture, for expert 3):**
  - Memory dump 24.0 MB (9 surfaces) at the first frame → 43.9 MB (13) settled → 33.9 MB (11) after resizes. The resize scratch purge works.
  - Budgeted cache stays 14.1–14.2 MB.

WAITING, RE-RANKED:
- **terminal-noop-scrollback-wheel:** not worth it; drop from Waiting.
  - No commit to `src/HultUI.Terminal*` or the terminal sample since round 10 except the target framework line, so round 11's numbers stand (5,440–5,682 glyphs, 112 B a no-op notch).
  - No app's project references HultUI.Terminal. Under "real apps first" it has no case.
- **terminal-row-strings-by-content:** not worth it; drop. 10,276 B a streamed line, same reason.

EXAMINED, NOT WORTH IT:
- **GL resource-cache purging:** flat 8–9.4 MB budgeted over every interaction and 18 resizes; nothing to purge.
- **A persistent program cache:** 8 lazy program compiles a Junction session, all before the first key. SkiaSharp 3.119 has no persistent-cache API (round 1).
- **Hover repaints the whole window** (Vesper 217 text runs a move): this is the damage-rects decision.
- **Meridian map clicks render 76–81 KB a click:** all of it is `~UpdateContentSize:VirtualListElement` in render, at 0 blob misses in the second pass. That is the list realizing rows inside the render pass (expert 1, by design).
- **Junction wheel render 9.6–17 KB a frame:** the same list realization.

NEEDS THE USER: none.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\0bab04ae-e7a9-44cb-b4da-96d849a107f6\scratchpad\r12-painting\`
- **Probe:** `probe-r12.patch` (round 11's probe + r12: layer pixels, GR context, presents) and `PaintProbe.cs`. Copy PaintProbe.cs to src/HultUI.Core/Rendering/ first, then apply the patch from the worktree.
- **Real-window host:** `host\` (PaintHost.csproj, src\Program.cs; AOT publishes in `host\aot`, `host\aot2`), `batch2.ps1` (guarded, takes the lock; batch.ps1 is the first version), `pub.ps1`.
- **Headless twin:** `headless\` (PaintHeadless, AOT in `headless\aot`), `headless.ps1`.
- **Logs:** `holds.log`, `holds-notes.log`.
- **Outputs (`runs\`):** `<App>-b3.out.txt` (real window), `<App>-haot3.out.txt` (headless AOT), `*-hjit*.out.txt`. Variants: `MeridianMap-hjit2-rowsel` (rows draw the selection), `Junction-hjit-rsettle(-nopan)` (resize with real time between steps, the width follow off).
- **Suite:** `suite-probe-r12.txt`, `palette-r5/r20/nowc/fullsize.txt`.
- **Spans probe:** `spans\`, `spans-before.txt`, `spans-after.txt`.
- **Scripts and patches:** `tab.py`, `cmp.py`, `kbdiff.py`, `wordcache-7d823b34.patch`, `merged-theme-91e5559c.patch`, `map-1879ba78.patch`, `pan-7ca2b3db.patch`.

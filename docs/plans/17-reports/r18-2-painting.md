SATISFIED: no

**Setup.** Worktree detached at aa3a3696 and clean again: the probe was saved as `probe-r18.patch` and reverse-applied. `dotnet build-server shutdown` has run, and no probe process or watcher is left. Numbers are counts and bytes only. Headless runs used JIT. Real windows used NativeAOT (`PaintRW`, an in-process host that drives input through `Window.HandleInputEvent` on the UI thread).

REVIEW:
- **0a7bb324: no objection.**
  - Skia already drops a layer whose paint is at alpha 0 (`SkCanvas::saveLayer`: nothingToDraw → save plus an empty clip). So the skip saves CPU only (24 → 7 elements a hidden frame, as the commit says) and no GPU memory. The commit claims none.
  - `OnlyPaints` walks the hidden subtree once a frame and stops at the first element that does more than paint. That is trivial.
- **c23edfb9: no objection.** The Slider's bounds cover the track, the thumb and the ring at the value's place. No app fades a Slider other than the player's bar (checked every `Opacity` in `apps/*`).
- **Correction to my round 17 report.** It said "no app fades a subtree holding one" of the controls with null bounds. That was wrong: Vesper's multi-rename fades a row of NumericUpDowns by default (candidate 2).
- **Main's commits: no objection in paint.**
  - Since 0df5e3ea only 0a7bb324 and e790d321 (text) touch `HultUI.Rendering.Skia` or `Core/Rendering`.
  - The keyboard focus ring added in 0f9c8477 is two stroked rounded rects (`Window.cs:4719`).
  - Aperture by keyboard: 1 frame a key, 0.21–0.56 KB render a frame, only bounded layers (numbers under REAL WINDOW).

CANDIDATES:
- id: painting-drag-ghost-layer-bounds
  what: The in-window drag ghost is drawn through `ctx.PushOpacity(0.7f)` (and 0.8f for a custom `DragVisual`) with no bounds (`Window.cs:4083`, `:4094`). Every drag frame is therefore a window-sized layer, and on GL Skia keeps that texture for the window's life. Fix: pass the source's subtree paint bounds (ElementRenderer's `SubtreePaintBounds`, exposed as internal).
  evidence:
    - **Headless** (Junction, 1440×900 at 1.5): 29 ghost frames per 30-move drag. Each draws a 2.916 Mpx unbounded layer (the whole window); with bounds it is 0.026 Mpx (−99 %).
    - **Real window** (GL, client 2160×1350, 2 holds, sides alternated, identical results):
      - The first drag adds +18.25 MB to Skia's cache (15.53 → 33.78) and +18.3 MB of DXGI local (46.6 → 64.9). Private grows +45.5 / +47.4 MB.
      - With bounds: Skia +0.50, DXGI local +0.5, private +7.8 / +7.9.
      - Drags 2 and 3 add 0 on either side.
      - At the end: DXGI local 64.9 → 47.1 MB (−27 %); start-adjusted private −38 to −40 MB.
    - Maximized on the 4K the layer would be 4096×3072 × 4 B ≈ 48 MB. **That is arithmetic, not measured.**
  real app: Junction's sidebar repository reorder (`RepoOrder.cs:42`). It is the only in-window drag in any app: Vesper's file drag goes to the OS (no ghost), and no app sets `DragVisual`. Users drag rarely (while arranging the Ctrl+1… order), but one drag commits the texture until the window closes (GL has no purge, and NVIDIA keeps its own pool).
  expected: retained-memory bar (GPU): −18 MB DXGI local at the default size (−27 %), about −48 MB maximized on the 4K.
  cost: small, no public API (`InternalsVisibleTo HultUI` exists). **Changes pixels:** 23 of 29 drag frames differ in 2–5 pixels by up to 6/255 at the ghost's edges (see NEEDS THE USER).

- id: painting-numericupdown-paint-bounds
  what: `NumericUpDownElement.PaintBounds` returns null (`NumericUpDownElement.cs:342`, because the value can be drawn past a box laid out short). Vesper's multi-rename counter row holds three of them and sits at `Opacity(0.45f)` while no mask has a [C] (`CommanderApp.Rename.cs:140`; that is the default). So every frame of the dialog draws a layer the size of the card's clip, and Skia keeps it. Fix: border box ∪ the centred value line, grown by 1.
  evidence:
    - **Headless** (Vesper, 1240×820 at 1.5): open plus 12 typed keys = 13 frames, each with a 1.198 Mpx layer (the card's clip, which has `Overflow.Hidden`). With bounds: 0.041 Mpx (−97 %).
    - **Pixels:** 12 of 12 typing frames, the open frame and the close frame are byte-identical.
    - **Real window** (GL, client 1860×1230, 2 holds alternated):
      - The first open adds +9.13 MB to Skia's cache (13.14 → 22.27) and +9.1 MB DXGI local (39.1 → 48.2). With bounds: +0.50 each.
      - First-open private: +26.4 / +27.0 MB unbounded, +1.7 / +2.4 bounded.
      - Start-to-end private over 3 open/type/close cycles: +35.7 / +50.8 unbounded, +21.0 / +20.7 bounded.
      - DXGI local at the end: 48.2 → 39.6 MB (−18 %). Later opens add 0.
  real app: Vesper's Multi-rename (Ctrl+M, also in the palette). Every key typed into a mask draws through the layer, and the texture stays after the first open in the window.
  expected: retained-memory bar (GPU): −8.6 MB DXGI local (−18 %), plus about −17 to −30 MB private per Vesper window that ever opened Multi-rename.
  cost: small; overrides an existing virtual; pixel-identical in the probe. **Wider scope, optional:** Dropdown has the same reason; Checkbox, RadioButton and ToggleSwitch (focus ring 3–4 px out, label overflow), ProgressBar and Spinner (centred when the box is smaller), the pickers, Calendar and Expander's part return null too, but no app fades one today. Checked: every `Opacity` in `apps/*`, `ButtonElement`'s disabled opacity, the toasts, the palettes, and Core's `Dialog` fade, which no app uses.

REAL WINDOW:
- **Desk** (logged before each hold):
  - The 4K is primary at 3840×2160, 120 Hz, 144 dpi (150 %); the 1440p is to its left at 2560×1440, 120 Hz, 100 %. Every window was on the 4K at dpi 144.
  - Steam was running; timer resolution 1 / 0.5 / 15.625 ms.
- **Holds:** 3 holds, 9 launches, all closed by WM_CLOSE. 0 foreground takes, 0 refused activations, 0 nvlddmkm events.
  - h1: a script bug (the `$runs` folder variable overwrote the `$Runs` parameter, since PowerShell names ignore case), so nothing launched. Waited 205 s, held 0 s, gate passed.
  - h2: 5 launches, 67.6 s held after a 104 s wait. The gate failed (mean CPU 32.9 %).
  - h3: 4 launches, 45.3 s held after a 714 s wait. The gate passed.
- **What only the real window showed:** the textures Skia keeps after the first unbounded layer (+18.25 MB Junction drag, +9.13 MB Vesper rename), and NVIDIA committing private bytes along with them. Headless, the CPU layer is freed every frame, so only the area shows.
- **Aperture** (Vulkan, client 2160×1350; fixture library and request server copied from the run-hultui-app driver, with posters served as generated JPEGs at the size asked for):
  - Home by keyboard: 20 arrow keys = 20 frames, 0.34 KB render a frame, 152 elements a frame. Layers are bounded only: the disabled page buttons, 0.003 Mpx each.
  - 10 Tabs = 10 frames, 0.53 KB a frame.
  - F5 (the New seasons row and its toast): 16 frames, all bounded (toast at most 0.026 Mpx).
  - Skia's cache: 24.02 → 30.19 MB in the first key walk (+12 resources, posters scrolled in and uploaded once); F5 +1.06 MB / +5 resources; the second walk +0.06 MB.
  - DXGI local 86 → 90 MB in the first walk, then flat. Nothing window-sized, nothing kept per key.
- **Lead, not paint (for experts 4, 6, 8):** my Junction host on the r14 jrepo was at 747–790 MB private and 724–778 MB working set about 3 s after start, with 43 gen-0 GCs, on AOT and GL. Round 15 saw 130 MB for Junction. The host builds `JunctionSettingsData` in-process and skips `Program.cs`, so check against the real app before trusting it.

WAITING, RE-RANKED:
- No Waiting item is mine. Round 17's "wider scope" note (the controls with null bounds) now has its one real-app case: candidate 2.

EXAMINED, NOT WORTH IT:
- **Aperture's keyboard walk, focus rings and New seasons row:** numbers under REAL WINDOW. Bounded layers only, each poster uploaded once, about 0.1–0.6 KB render a frame. Nothing to change.
- **Aperture's disabled page buttons at 0.4:** bounded 48×48 px layers, 1–3 a frame. Below every bar.
- **Opacity-0 layers left after 0a7bb324:** Skia draws nothing for them (alpha-0 saveLayer is a save plus an empty clip), so they hold no GPU memory.
- **Junction's dragged row dimmed at 0.4:** bounded, 0.026 Mpx a frame.
- **Lead for the next survey (not measured on real artwork):** `PosterCache` keeps each poster as a decoded raster (`SKBitmap`), and Skia uploads a GPU texture of it too. Both copies live as long as the LRU (cap 400) holds the poster. The fixture walk shows Skia +7.3 MB and private +19 MB for 28 small posters. Expert 8's real Jellyfin walk could count posters and bytes before anyone judges this.

NEEDS THE USER:
- **painting-drag-ghost-layer-bounds changes pixels slightly.**
  - The gain: −18 MB GPU memory (−27 % DXGI local) at Junction's default size, about −48 MB maximized on the 4K, after any repository drag.
  - The cost: during a drag, 23 of 29 frames differ in 2–5 pixels at the ghost's left and right edges, by up to 6/255. Frames before and after the drag are identical.
  - Growing the bounds by 2 px changes nothing, so this is not clipping. It is the layer origin moving (the same kind of change question 69 accepted for the player bar, at up to 6 levels instead of 1–2).
  - Choices: (a) keep today's ghost; (b) bounds with this change.
  - Images: `headless.ps1 -App Junction -Walk jdrag -Extra "PAINT_HASH=1;PAINT_DUMPDIR=<dir>"`, run once plain and once with `;PAINT_GHOST_BOUNDS=1` added, then `cmpdump.py <dir0> <dir1>`. Copies are in `dump\g0` and `dump\g1`.

FILES (all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\00f54f03-b511-4949-9273-30fe719e1bbb\scratchpad\r18-painting\`):
- **Probe:** `probe-r18.patch` (apply at aa3a3696; adds `LayerProbe.cs`). Switches: `PAINT_GHOST_BOUNDS`, `PAINT_GHOST_PAD`, `PAINT_NUD_BOUNDS`.
- **Hosts:** `headless\` (JIT `LayerHeadless`; new walks `jdrag`, `vrename`, `akeys`, `atab`, `af5`, plus Aperture), `rw\` (AOT `PaintRW`, guarded, with Skia-cache and DXGI marks) and `shared\Fixture.cs` (Aperture fixture and poster handler).
- **Scripts:** `headless.ps1`, `pub.ps1`, `batch.ps1`, `cmpdump.py`.
- **Logs:** `holds.log`, `gate.log`, `runs\h2-*.out.txt`, `runs\h3-*.out.txt`, `runs\hl-*.out.txt`, `dump\g0`, `dump\g1`, `dump\g2`.

SATISFIED: no

I found four candidates and one review objection. The objection is to b62e22cf: a click on a row in Meridian's map now draws 2 frames, and the second one has the same pixels as the first.
- **Worktree:** clean at 5a212f3e. The probe was saved, reverse-applied and PaintProbe.cs deleted.
- **Cleanup:** `dotnet build-server shutdown` has run. No PaintHost or PaintHeadless process is left.
- **Numbers:** counts and bytes only, no timings.
- **Method:** headless runs pump at 120 Hz × 1.5 and hash every drawn frame (SHA-256) against the frame before. Where an input drew no frame, a frame is forced, and it must hash as the frame on screen; a mismatch is a "miss".

REVIEW:
- **b62e22cf (objection): the release clause draws same-pixel frames on Meridian clicks.**
  - **The clause:** "content moved under a held press → `_root.MarkPaintDirty()` on release" (Window.cs, end of `HandleInputEvent`).
  - **Meridian's map, headless:** a row click's release drew a frame 10 of 10 times, 9 of them the same pixels. The probe shows this clause fired each time: a scroll view moved in the press's frame while the button was held.
  - **Real window:** 10 clicks drew 10 release frames.
  - So a Meridian row click is 2 frames, and the second shows nothing new. This clause is new in round 14 (b62e22cf). Fix: candidate painting-release-settle-now.
  - **The rest of b62e22cf is clean.**
    - A wheel notch under a resting pointer draws 1 frame in Junction, Vesper, Meridian and Aperture.
    - 0 of those frames are identical, and 0 misses.
    - Pointer in and pointer out draw the same element count (Junction 318.1 a frame). The pointer adds 1 rrect (the tint) and 0.00 KB a frame once warm. The first pass's 10.4 KB a frame is row realization, in either order.
- **64c127a2: the right click's release frame is all repeated pixels.**
  - Vesper headless: 6 of 6 right-click release frames are identical to the frame before.
  - Cause: the `_hoverMayBeStale` clause. The mark's rebuild under the held button replaced the hovered row, so the release asks for a frame only to settle hover. Same fix as above.
  - Not run in a real window: Vesper's 400 ms context-hold timer would open the real shell menu if a release came late.
- **545d5d89: no objection.** Identical frames per bar visit (on and off):

  | Rate and scale | Frames | Identical |
  |---|---|---|
  | 200 Hz × 1.0 | 28 | 4 (3 expanding, 1 collapsing) |
  | 120 Hz × 1.0 | 18 | 1 |
  | 120 Hz × 1.5 | 18 | 0 |

  Same in Junction, Meridian and Vesper. See EXAMINED.
- **835adab9 and b3355310: no objection.**
  - In the Commit view at 1.5, all 8 sign cells found ("+" and "-") are 16.00 px wide, and the code starts at x 618.00 in every row.
  - b3355310's own byte check (216 of 216 identical with 1cb9c4a0) stands.
- **c7dd2365 and 8b994a41: no objection.** In Junction's Commit view, 20 file keys drew 20 frames, 0 identical and 0 misses.
  - Headless loads run synchronously, so this does not see a read-ahead landing frame.
  - The code says a read-ahead landing redraws only for the selected path.
- **Not walked:** 6a4c778d and ae8429d3 (the watcher's reload and Vesper's slow folder).

CANDIDATES:
- id: painting-modifier-key-frames (apps: Junction, Meridian, Aperture, Reel, Vesper)
  what: Each press of a modifier key on its own, and each auto-repeat while it is held, draws a full frame of the same pixels in all five apps.
  evidence:
    - **The cause:**
      - Junction, Meridian, Aperture and Reel register an ordinary root `On<KeyDownEvent>`: JunctionApp.cs:485, MeridianApp.cs:229, ApertureApp.cs:135, ReelApp.cs:89. The input net counts it, and the probe named exactly these handlers.
      - Vesper's handler is passive, but it marks for every key that is not a plain waited navigation (`OnKeyDown`, CommanderApp.cs:506–511).
    - **Headless, every app:** Shift 5 of 5 identical; Ctrl 5 of 5; a held Shift with 15 repeats, 16 of 16.
    - **Prototype:** the net skips `KeyChord.IsModifierKey` key-downs, and Vesper skips its mark for them. All of these go to 0 frames, 0 misses over 27 forced checks per app.
    - **Inside the rich text editor:** modifiers draw nothing, because the cause is the apps' handlers.
    - SDL passes key repeat through unfiltered (EventTranslator.cs:164).
  real app: The frames these presses draw today:
    - Ctrl+shortcut (Ctrl+S, Ctrl+F, Ctrl+Enter): 2 frames, 1 identical.
    - A capital letter typed in Meridian or Junction: 1 more identical frame.
    - Shift or Ctrl held for a range or multi-select (Vesper Ctrl+click, Junction Shift+click): about 30 identical frames a second after the repeat delay.
    - Real window: 5 Shift → 5 frames, 5 Ctrl → 5, a held Shift (1 + 30 repeats) → 31, in every app. The prototype makes all of them 0 (Junction, Meridian, Vesper).
    - Each frame is a full window: Junction 329 elements, Vesper 448, Meridian 200, Aperture 135, Reel 6.
  expected: time bar on an app frame metric: a shortcut 2 → 1 frames (−50 %), a held modifier about 30 → 0 frames a second.
  cost: small. Two ways to build it:
    - **App level (keeps the framework's rules):** each root handler goes passive and marks unless `KeyChord.IsModifierKey(e.Key)`, Vesper's pattern. One line in Vesper, a wrapper in the other four. No API or pixel change.
    - **Framework level:** one clause in the net. Any app that draws something on a bare modifier without marking would lose that frame. None of the five apps does (grep; MenuBar's Alt handler is framework-owned).
- id: painting-release-settle-now (framework: Window.cs, the two release clauses after the input net)
  what: When nothing is dirty at a release, settle hover (`SettleHover`) or follow the scroll (`FollowScrollWithHover`) at once, and ask for a frame only when that changes something drawn: a list tint, a handler run, or DevTools. Today the release always asks for a frame.
  evidence: A prototype behind a switch:
    - it runs the settle or follow at the release when the root and all overlays are clean;
    - it marks only on a handler run, a `DrawnHoverChanges` change or DevTools.

    | Case | Today | Prototype |
    |---|---|---|
    | Vesper right-click release, headless | 6 frames, 6 identical | 0 |
    | Meridian left click (walk), headless | 12 frames, 11 identical | 1 |
    | Meridian row click (rows walk), headless | 10 frames, 9 identical | 1 |
    | Meridian, real window, 10 row clicks | 10 release frames | 1 |

    0 misses in 81 forced checks across the five apps. Hover styles and the cursor already mark themselves, as after a move since 0da9f80e.
  real app: Clicking a request in Meridian's map, Meridian's most common action: 2 → 1 frames a click, 217 elements each. A right-click mark in Vesper: 2 → 1 frames, 449 elements.
  expected: time bar on an app frame metric, −50 % frames per click in both.
  cost: small to medium, framework-internal, no API or pixel change.
    - It runs hover enter and leave handlers at input time instead of after a present.
    - Run HoverFollowsScrollTests and HoverAfterRebuildTests under reverse edits, on Linux too.
- id: painting-junction-row-press-frames (app code: Junction, plus Meridian's Files rows)
  what: The right-click-only `row.On<PointerDownEvent>` handlers are ordinary, so every left press on such a row draws a same-pixel frame before the click's frame.
  evidence:
    - **Where:** History.cs:1288, Changes.cs:959 and 1023, Conflicts.cs:129, Branches.cs:523, Tags.cs:322, RefTree.cs:225, MergeRequests.cs:493, Sidebar.cs:138 and 196, SidebarWorktrees.cs:123, RepoModalView.cs:393, and Meridian Files.cs:392 (`RowContextMenu`). Each one acts only on button 2, and each marks when it acts (MarkDirty, RerenderView, or an overlay opening).
    - **Headless, 10 History row clicks:** 10 press frames, all 10 identical. Prototype with History's handler `OnPassive`: 0 press frames, 0 misses; release frames unchanged (10).
    - **Real window, same 10 clicks:** press frames 10 → 0; 20 → 10 frames for the 10 clicks.
  real app: Selecting a commit in History, a file in Changes (the staging workflow), branches, tags, repositories. Many times a minute. A click 2 → 1 frames, each 329 elements.
  expected: time bar on an app frame metric, −50 % frames per row click.
  cost: small (`On` → `OnPassive` on 14 handlers). No API or pixel change. Could be one build step with painting-modifier-key-frames.
- id: painting-rte-number-labels (framework: RichTextEditorElement.Render.cs:232)
  what: Every frame builds each visible numbered item's label (`Number.ToString() + "."`, about 31 B each), the only steady-state allocation of an editor frame.
  evidence: Headless, the editor built as Junction's composer builds it (Markdown features), a document with 25 numbered items. Prototype: a static label cache.
    - Click and caret frames: 0.78 → 0.00 KB a frame.
    - Typing frames: 3.15 → 2.37 KB a frame (−25 %).
    - 6, 20 and 40 frames measured.
  real app: Junction's review comments and merge request descriptions with numbered lists: every keystroke, caret move and selection frame.
  expected: memory-churn bar, a per-frame allocation removed (0.78 KB → 0 a frame).
  cost: small. No API or pixel change.

REAL WINDOW:
- **Setup:** NativeAOT PaintHost with the probe and prototype switches, input injected in-process. Every launch ran on the 4K, primary, 3840×2160 at 150 % (the window said 120 Hz, scale 1.5). The 1440p was attached at 100 %, 120 Hz. The desktop UIA listener was active in every launch (a11y=1).
- **Same counts as headless:** in every phase, presents equal drawn frames, with 0 walk-only frames. The real window adds the GPU flush and present to each repeated frame; Aperture and Reel present through Vulkan.
- **Counts:** the per-candidate numbers are above:
  - modifiers: 5, 5 and 31 frames in all five apps, 0 with the prototype in Junction, Meridian and Vesper;
  - Junction History press frames 10 → 0;
  - Meridian release frames 10 → 1;
  - Vesper's left-click releases were already 0 (64c127a2 holds).
- **What only the real window could show:** held-modifier repeats arrive at the OS repeat rate, one frame each.
- **The scrollbar tween at 200 Hz:** no monitor ran at 200 Hz today, so it was measured headless only.
- **Launches and holds:**
  - 8 launches in 1 hold: 23:30:45–23:33:15, 149.8 s, after waiting 334 s for the lock.
  - Idle gate before the hold: 4 % CPU for 10 s.
  - 0 foreground takes, 0 refused activations, 0 nvlddmkm events.
  - Aperture signed in under the existing device "aperture-r12".

WAITING, RE-RANKED: none of mine.

EXAMINED, NOT WORTH IT:
- **The scrollbar tween's mid-curve identical frames:**
  - Of a visit's frames: 4 of 28 identical at 200 Hz × 1.0, 1 of 18 at 120 × 1.0, 0 at 120 × 1.5.
  - The snapped rect stays put while the unsnapped radius moves. Telling those frames apart without drawing them would also drop frames that do change pixels, which changes how the tween looks.
  - Not worth it for 0–14 % of the frames of one bar visit.
- **Keys the apps ignore:** Meridian's Down on the map 20 of 20 identical, Reel's empty player 20 of 20, Vesper's viewer PageDown at the end 10 of 10. Same root-handler cause, but rare in real use. Only the modifier case is a candidate.
- **A press on selectable Commit-view text** (the focus clause): still 5 of 5 identical; round 14's verdict stands.
- **Rich text editor:** no caret blink and no idle frames.
- **Aperture's poster grid, wheel:** 1 frame a notch, 0 identical, 0.05 KB a frame.

NEEDS THE USER:
- **painting-modifier-key-frames:** fix it in each app (no framework change) or in the input net (one place, but an app that draws on a bare modifier without marking would lose that frame)? My recommendation is in each app.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\8e8ba33b-db33-43fa-885a-cb6b82b7253f\scratchpad\r15-painting\`
- **Probe:** `probe-r15.patch` and `PaintProbe.cs` (copy it to `src\HultUI.Core\Rendering\` first, then apply the patch at 5a212f3e). Round 14's probe plus:
  - the names of ordinary handlers that ran and which clause of the input net fired;
  - the switches `PAINT_SETTLE_NOW`, `PAINT_MOD_NOFRAME`, `PAINT_ROWPRESS_PASSIVE` (Junction History) and `PAINT_NUMCACHE`.
- **Hosts:**
  - `headless\` (PaintHeadless, JIT, in `headless\bin-jit`): `PAINT_WALK` steps (hover, wheel, wheelout, lclick, rclick, rows, keys, mods, type, open, files, signs, f3, pgdn) and the `Editor` app;
  - `host\` (PaintHost, AOT in `host\aot`): `PAINT_SCRIPT=r15`.
- **Scripts:** `headless.ps1`, `batch15.ps1`, `driver.inc.ps1`, `pub.ps1`, `summary.ps1`.
- **Logs:** `holds.log`, `batch-h1.log`, `idlegate.log`, `build-headless.log`, `publish-PaintHost.log`.
- **Outputs** (`runs\`):
  - headless: `hl-*-{walk1,mods,why,base,proto,base2,proto2,rows,rowsP,signs,files,viewer,type,num0,num1,bar200x1,bar120x1,bar120x15}.out.txt`;
  - real window: `{Junction,MeridianMap,Vesper}-{base,proto}.out.txt`, `Aperture-base.out.txt` and `Reel-base.out.txt`.

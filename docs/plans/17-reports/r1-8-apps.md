# Round 1 survey: 8. App workloads

NativeAOT publishes in real windows that never took focus (a11y on unless noted) and
headless hosting, with a temporary frame-log probe (Window, Application, ComponentHost,
ElementRenderer, TextPipeline + BenchHook), since reverted. Shares of frame time; CPU was
shared with the other surveys.

SATISFIED: no
REVIEW: none (round 1)

## Where frames go

| App, interaction | Render (realize, text) | Flush | a11y | Present | Layout | Style | Bytes/frame |
|---|---|---|---|---|---|---|---|
| Junction session, a11y off | 56 % (11, 11) | 27 % | — | 6 % | 8.5 % | 1 % | 287 KB |
| Junction session, a11y on | 45 % | 28 % | 14 % (84 rebuilds) | 6.5 % | 4.5 % | 0.6 % | 223 KB |
| Vesper | 48 % (realize 18) | 24 % | 16 % | 6 % | 4 % | — | 316 KB |
| Reel playback | 49 % | 13 % | — | 31 % | 1 % | — | 8 KB |

Meridian and Aperture (headless): the start-up frame is 64–73 % layout, of which text is
42–51 %. Junction input handling including builds: 10 % on top. Build 80 % / reconcile 20 %
of rebuild time.

## Candidates

- **a11y-eager-virtual-items.** Lists up to 2,000 items get one a11y node per item, off-screen included, on every rebuild; selection moves trigger the coalesced layout refresh.
  - Evidence: Junction History key walk (40 Down): a11y 30.3 % of frame time; 24 rebuilds × 1,110 nodes; 212 KB/frame. Vesper viewer PageDown: a11y 54.9 %, 10 rebuilds × 684 nodes, 231 KB/frame. HULTUI_A11Y=0: 41 frames instead of 58 for the same walk. AccessibilityTreeBuilder.cs:18 (MaxEagerVirtualItems = 2000), :198, :216–240; Window.cs:836–846.
  - Expected: scaling; ≥5 % on History key walk; ~190 B per item node per rebuild.
  - Cost: medium; screen reader must see the same tree — on-demand item provisioning (the >2,000 path) at a lower threshold, or diff item nodes by index.
- **layout-virtuallist-realize-in-render.** VirtualList re-realizes rows inside render (ElementRenderer.cs:94 → VirtualListElement.cs:386). Realize share: Junction History walk 11.7 % (a11y on) / 16.7 % (off), 167–242 KB/frame; Commit file walk 19.2 %, 131 KB; Branches/Tags switch 17–27 %; Vesper pane walk 15.9 %, 266 KB; **Vesper opening a 58-file folder 84 % of a 20 ms frame, 5.2 MB**; Aperture library Right key 20.6 %, 108 KB. Rows that only change selection should patch. Medium–large.
- **runtime-drawtext-boxed-enumerators** (= paint-text-zero-alloc). 72 B per DrawText; spanned lines 611 B (Vesper viewer 61.7 KB per render pass); Junction hover 41 KB/frame AOT (143 DrawText ≈ 10.3 KB); Vesper wheel 27.8 KB (159 ≈ 11.4 KB). ~25–40 % off render-only app frames.
- **text-textarea-short-lines-open.** First render measures every hard line (Substring + full Layout). Meridian 312 KB pretty JSON (~22,500 lines, mono, token spans): 24,165 TextPipeline calls in render, 30.3 MB, text layout 90 % of the frame. Junction Files walk: text layout 29–37 % of frame time, 434–469 KB/frame. TextAreaElement.cs:340 → :422 → :634. Lines provably narrower than the wrap width (mono advance, prefix-width table) could skip shaping. Medium.
- **reactivity-tween-rebuild-per-loop-iteration.** AnimationManager.Tick runs every main-loop iteration (Application.cs:384, :427), so a tween writing a signal read in Build rebuilds on every wake. Reel controls-bar fade: 32 frames each with 22–62 rebuilds between them; 713 rebuilds in the first 3 s, 943 during close (VideoPlayer.cs:195, :217; mpv wakes the loop). Mechanism inferred from counts. Tick only when a frame is due, or coalesce signal-driven rebuilds to one per frame. Small–medium.
- **gpu-aperture-startup-swapchain-x3.** Aperture creates its Vulkan swapchain 3× before the first frame (2160×1350 → 1395 → 1440); ~10 ms. Borderline. Small.
- **apps-scenario-virtual-selection-walk-a11y.** New perf-suite scenario: VirtualList of ~1,000 two-line rows (icon + two texts), selection in a signal the parent Build reads, Down repeatedly, bridge on. The most common Junction/Vesper interaction; no scenario covers it. Small.
- **apps-scenario-textarea-highlighted-json.** New scenario: open a 20,000-line pretty JSON with token spans in a read-only mono TextArea (Meridian). Small.
- **apps-tooling-generic-framelog.** Framework-level env-gated `HULTUI_BENCH` / `HULTUI_FRAMELOG` so every app can be scripted and logged on AOT (columns: a11y, realize, text time by pass, allocations by pass, a11y rebuild reason and node count, build/reconcile per input, memory row). Small.

## Examined, not worth it

- Build vs reconcile: no reconcile lead; Junction split already declined.
- Style 0.6–1.5 %, layout outside text and realize ≤8 %.
- Idle main loop: 64 B per RunFrame; Junction idle 127 KB over 15 s.
- Render-only bursts (~80 frames at 120 Hz in one a11y-on run) did not recur; likely the user's real pointer crossing the unfocused window. Check before blaming a11y.
- Junction history graph: 1,048 B per row per frame in the app's DrawGraph (foreach over IReadOnlyList, JunctionApp.History.cs:1258); framework canvas 0 B. Guideline item.
- Start-up (clean AOT): Junction first frame 343–350 ms, GL driver load ~68 %, app 12–14 %; Vulkan apps 131–138 ms from ready to first frame (known FP16 first-present). No new lever.
- Junction start-up allocation 110 MB in 4 s, 90 % off the UI thread (git loading, app code).
- Retained memory idle at 6 s (working set / private): Junction 164–167 / 180–183 MB; Vesper 103 / 117; Meridian 91 / 119 (managed ~3 MB); Reel 115 / 141 (275–284 / 356–369 playing); Aperture 118 / 150.
- **Junction heap growth:** GC heap 41 → 75 MB over a 30 s session (working set 165 → 252 MB), not attributed (no gcdump tool here). A heap census in the build phase could turn it into a candidate.
- Not measured: Junction GitLab and rich-text panels.

## Needs the user

- **Aperture against jellyfin.home:** only the offline fixture library was measured. A real-library run (poster grid, artwork decode, a11y on a large library) needs an API key file or permission to use the stored Jellyfin session.

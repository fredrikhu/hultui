# Round 2 survey: 8. App workloads

All five apps published with NativeAOT at 520002e (before round 1) and 6ba7e2d (after), real
windows that never took focus, a11y on unless noted, before/after alternated 2 runs each, a
rewritten frame-log probe driving scripted input (round 1's was lost with its worktree).
Aperture signed in to jellyfin.home with the key file read in-process (never printed); this
registers a device named "aperture-survey" on the server. Probe, scripts and CSVs in the
session scratchpad `r2apps/`. Shares and bytes only.

SATISFIED: no

REVIEW: none. No app metric got worse. View-switch frames dropped bytes (Commit 1,538 →
1,417 KB, Branches 1,243 → 950 KB).

## Did round 1 help? Yes in bytes and a11y; realize and text untouched (not built yet)

Shares after round 1 [round 1's table]; bytes before → after in the same A/B.

| App, interaction | Render (realize, text) | Flush | a11y | Present | Layout | Style | Bytes/frame |
|---|---|---|---|---|---|---|---|
| Junction session, a11y on | 51.5 % (8.8, 9.0) [45] | 28.4 % [28] | 9.5 → 6.0 % [14] | 6.4 % | 6.3 % [4.5] | 0.8 % | 350 → 246 KB (−30 %) |
| Junction session, a11y off | 56.0 % (10.0, 10.6) [56] | 27.9 % | — | 6.5 % | 7.9 % [8.5] | 0.9 % | 293 → 217 KB (−26 %) |
| Vesper panes + viewer | 52.5 % (18.6, 4.4) [48] | 30.6 % [24] | 13.6 → 3.9 % [16] | 6.6 % | 4.9 % | 0.8 % | 330 → 254 KB (−23 %) |
| Reel playback | 34–42 % [49] | 14 % [13] | 0.4 % | 37–47 % [31] | 1–1.4 % | 0.2 % | 7.7 → 2.9 KB (−62 %) |
| Meridian 312 KB JSON + wheel | 70.6 % (text 55.8) | 23 % | 0.8 % | 3 % | 2.2 % | 0.1 % | 970 → 773 KB (−20 %) |
| Aperture real library | 28.4 % (4.9, 1.4) | 35 % | 4.0 % | 25.7 % | 2.6 % | 0.4 % | 111 → 75 KB (−33 %) |

- Junction: History key walk a11y 17–18 → 8–11 %, 461 → 329 KB/frame; History wheel a11y 13 → 4 %, 210 → 131 KB; hover sweep 40 → 15 KB; Files walk 549 → 371 KB.
- Vesper: viewer PageDown a11y 49 → 6.7 % (57f09d6), 810 → 568 KB; pane walk a11y 12 → 3.7 %, 367 → 290 KB; pane wheel 30 → 0 KB.
- Meridian: response frame 15.4 → 12.6 MB (TextPipeline calls still ~24,200); response wheel 28 → 4.6 KB.
- UI-thread allocation per session: Junction 140 → 111 MB (−21 %), Vesper 79 → 65 (−17 %), Meridian 51 → 44 (−13 %), Aperture 17.8 → 14.4 (−19 %), Reel 165 → 132–147.
- Working set / private: Junction 241/233 → 227/221 MB; Vesper 159/159 → 155/147; Meridian 138/137 → 131/124; Aperture 204/258 → 158/190 (−23 %/−27 %; ebefa29 + 615d75d); Reel flat ~319/661.
- Retained managed heap after a full GC unchanged (Junction 55 MB at 34 s). Start-up unchanged (GL driver load 52–68 % of time to first frame; Vulkan FP16 first present 36–38 %; Aperture still 3 swapchains).
- Realize bytes did not move: Junction History walk realize 167–174 KB/frame.

## Candidates

- **reactivity-tween-rebuild-per-loop-iteration.** Reel 11 s playback: 14,829 loop iterations (1,350/s), a tween active in 86 %; VideoPlayer rebuilt 4,222 times (~13 per presented frame, 40.8 KB each = 172 MB on the UI thread, a gen-0 GC about once a second); only 50 position events (throttled at MpvBackend.cs:323-328) — the rebuilds are tween ticks (VideoPlayer.cs:195/:206/:217, ticked every iteration at Application.cs:387). Aperture uses the same player. > 10× less churn.
- **layout-virtuallist-realize-in-render.** Junction History walk realize 16 % of frame time (~18 rows per key, 12.9 KB and 13 text calls per row; 52 % template, 40 % layout); Commit 9 %; Branches 15 % (19.5 KB per row); Vesper pane walk 16–17 % (37 rows per key, 7.3 KB per row); Aperture poster walk 8–9 % (124 KB/frame). VirtualListElement.cs:621-622 → :537 → :425 from ElementRenderer.cs:92. **Correction:** round 1's Vesper "folder open 84 %, 5.2 MB" was the one-time syntax registry load (next item); later folder opens 12–14 % realize, 320 KB.
- **runtime-syntax-registry-first-use.** The first file list showing a code file loads TextMateSharp's registry on the UI thread (FileIcons.cs:54 → SyntaxHighlighter.cs:113-131 → EnsureRegistry :255): Vesper's first code folder 10.1 ms and 4.6–5.3 MB in one frame (realize 79 %); later folders 0 KB. Answer CanHighlight from a static extension table, or warm the registry on a worker. Small.
- **text-textarea-measure-lines.** Meridian JSON response: 24,195 TextPipeline layouts and 25.7 MB in one frame, text 91 % of it. Junction Files walk: text 28–29 % of frame time, 370 KB/frame.
- **apps-tooling-generic-framelog** (owner 8, built by 4). Env-gated framework frame log (HULTUI_BENCH script + HULTUI_FRAMELOG CSV); round 1's probe was lost and had to be rewritten (two files, 7 insertion points; trap: InputEvent left button is 0, SDL's is 1). Source in `r2apps/probe/`. Small.
- **runtime-idle-wakeups.** Idle Junction and Meridian 64.8 loop iterations/s (Application.cs:459), Junction 5.7 KB/s.
- **meridian-response-format-on-ui-thread** (app). The response handler spends 15.3 ms on the UI thread before the render frame (Prettify, Split, line starts, TokenizeLines at MeridianApp.Response.cs:366-379, :398, :434); the 22,500-line result exceeds SyntaxHighlighter.cs:99 MaxLineCount and is drawn uncoloured. Move to the request worker: ~30 % of a large response's UI-thread time. Small; guideline item.
- **gl-vk-one-submit-per-frame** (3): flush is the second-largest share in every app: Junction 28 %, Vesper 31 %, Aperture 33–36 %, Meridian 23 %, Reel 14 %.
- **a11y-eager-virtual-items** (7), re-ranked down: Junction History walk a11y 8–11 % (25 rebuilds × 1,110 nodes, 145 KB/frame); Vesper viewer PageDown 6.6 % (was 49 %).
- **gpu-aperture-startup-swapchain-x3** (3): unchanged, ~10 ms, borderline.
- **Suite coverage gaps:** apps-scenario-tween-signal-rebuild (Build reads a Signal<float> set by a 300 ms tween while a worker posts at ~1 kHz; rebuilds per frame); apps-scenario-rich-row-walk (14-element Junction-like rows); apps-scenario-document-switch (switch a highlighted read-only TextArea among 25 files, 24-entry LRU); apps-scenario-image-grid (17–400 decoded posters, Right-key walk and wheel); apps-scenario-loop-wakes (real-loop passes per second idle and during a tween — Benchmark sample or a real-loop test); a test that the first code-file icon does not load the TextMate registry on the UI thread.
- **Waiting items re-ranked:** apps-tooling-generic-framelog now a candidate; aperture-postercache-trim-thread (correctness — fix before Aperture memory work); aperture-poster-as-hero not reproduced (100 % scale, no item page); aperture-tmdb-size-fallback not exercised; junction-narrow-gitlab-glyphs not perf; meridian items not perf; apps-junction-heap-growth closed.

## Examined, not worth it

- **apps-junction-heap-growth: not a leak.** Live heap after full compacting GCs: 33.0 MB at start, 35.8 after History/Commit/Changes/Branches/Tags, 43.1 after a 25-file Files walk, 45.4 at the end; a second Files walk +0.0, a 200-commit walk +0.3. Files growth is the bounded 24-entry content cache (JunctionApp.Files.cs:68-71). Round 1's 41 → 75 MB was heap size at the last GC, garbage included. `_highlights` (JunctionApp.Highlight.cs:25) unbounded but +0.3 MB over 200 commits. Working set 164 → 190–212 MB is native.
- Junction Overview switch build 3.9–5.5 ms in two runs vs 0.24–0.31 in two others, same bytes: noise.
- Style 0.1–0.8 %, layout outside text ≤ 5 % in every app.
- Reel retained memory flat; GPU interop dominates its private bytes.
- Aperture large-library cost cannot be measured: the user's library is 17 films and 3 shows; a11y 3.7–4.1 %.

## Needs the user

None.

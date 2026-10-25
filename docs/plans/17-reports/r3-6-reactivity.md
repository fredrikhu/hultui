# Round 3 survey: 6. Reactivity and reconcile

Release JIT, headless, per-phase counters and `GetAllocatedBytesForCurrentThread`, Junction on a
1,456-commit clone, at ac5d415. Probes and captures in the session scratchpad
`probes/reactivity/r3/`; worktree clean.

SATISFIED: no

REVIEW: none. cc842ac, 883943f/e5d626e/b10d022, c2bf858, bff6de2/73b1c10, 7dc7e0e, 61aca67,
e877766 hold: signal write → effect 0 B; real-loop tweens 1.00 rebuild per drawn frame (with and
without 1 kHz posts); item rows 3 rows per step. **Note for runtime-idle-wakeups:**
`AnimationManager.Tick` steps a tween started this pass by the whole gap since the last step
(Application.cs:485-491, AnimationManager.cs:38-63); today ≤ one interval because idle passes keep
`lastTick` fresh — a loop that blocks until its next deadline would make a hover transition after
5 s idle jump to its end.

Re-measured: component edit keyed 5,281 KB/op (6,003 unchanged re-resolves per step, 3,000 only
because Text has an ApplyTheme; 44,004 style down-visits); signal tween 340 KB/op at 1.0 rebuild per
frame; selection walk item rows 26 KB/op (103 of 118 resolves are unchanged re-resolves of kept
rows); Junction History per Down: a11y off 455 KB in the press frame + 236 KB later, 2.05 rebuilds of
194 KB; a11y on 736 + 535 KB; 396 resolves (363 unchanged).

## Candidates

- **apps-junction-prefetch-rebuild** (app). The highlight prefetch's completion `MarkDirty()`s the whole app even when the view shows no highlights (JunctionApp.Commit.cs:281 → JunctionApp.Highlight.cs:103-108; `HighlightForCurrent` is null outside Commit/Changes). Every History Down rebuilds twice. Prototype (rebuild only in Commit/Changes): 2.05 → 1.0 rebuilds per press; a11y off press frame 455 → 251 KB (−45 %), later frames 236 → 49 (−79 %); a11y on 736 → 543 (−26 %). Small. Guideline: a background result needs a rebuild only if the current build reads it.
- **reactivity-pointer-rebuild-per-frame.** Pointer-move-driven rebuilds happen per move, not per drawn frame: PaneSplitter (PaneSplitter.cs:106-109, :128-137), HorizontalPan (:86, :172), Slider hover/drag (SliderElement.cs:106-113), VideoPlayer scrubber `_previewAt` (VideoPlayer.cs:458, read at :347). Real loop, a move every ms: synthetic 120-row splitter app 16.1 rebuilds/frame, 10.2 MB/frame (vs 1.0 and 0.66 MB with one move per frame); Junction Commit splitter 8.1/frame at 329 KB, 3.06 vs 0.69 MB/frame; VideoPlayer scrubber hover 15.7/frame, 639 vs 42 KB/frame. −78…−94 % bytes per drag/hover frame. Medium; behaviour change (Needs the user): (a) window delivers pointer moves at most once per frame that may be drawn (Chromium's rAF-aligned input) or (b) only splitter/pan/slider callbacks coalesce.
- **reactivity-videoplayer-position-in-place.** `Bar()` reads `player.Position` (VideoPlayer.cs:420), so every position tick (10/s, MpvBackend.cs:323-327) rebuilds all 22 elements: 41.2 KB, 22 resolves, 14 layout flag sets per tick, bar shown or hidden. Prototype (an Effect writes the elapsed label and slider value; Build reads position untracked): 248 B per tick, 0 rebuilds. ~412 KB/s → 2.5 KB/s (−99 %) in Reel and Aperture playback. Small–medium; expert 3 to review.
- **reactivity-toasthost-fade-in-place.** ToastHost calls `_onChanged` per fade step (ToastHost.cs:75, :100); all three apps pass `MarkDirty`. Junction, one 2 s toast: 22 whole-app rebuilds = 4.40 MB, 7,417 resolves. Prototype (Ref + Opacity write): 2 rebuilds, 0.43 MB (−90 %), same 20 frames. Small; callback cadence changes (Needs the user).
- **apps-whole-app-tween-rebuilds** (re-ranked). Junction remote-op and spinner tweens `MarkDirty()` per step (JunctionApp.RemoteOps.cs:51, JunctionApp.Spinner.cs:51): a fetch on the real loop 1.02 rebuilds per frame at 195 KB, 12.1 MB per 61 frames (a spinner element allocates 0). Write Transform/Opacity on Ref'd live elements or use a leaf spinner component. Small–medium, app only.
- **reconcile-restyle-on-change** (moved up). Every patch marks its whole subtree style-dirty (Element.cs:1329, :1034-1040): 7.3–7.5 down-visits per element per rebuild; item rows' kept rows re-resolved (103 of 118); Junction 363 of 396 resolves per press unchanged. Themed controls flip-flop layout: ButtonElement.ApplyTheme writes Padding/BorderWidth/Width/Height/MinWidth/MinHeight the twin lacks (ButtonElement.cs:176-192), so PatchFrom reports a layout change (Element.cs:1326) for Button, Kbd, TextInput, Toast on every rebuild (Junction: 7 Buttons, 1 Kbd, 1 TextInput), each reaching the root's measure cache. Latent bug: Text SelectionColor overwritten by ApplyTheme (TextElement.cs:277 vs :529). component/edit 6,003 → ~6 resolves; Junction ~−90 % resolves, −9 layout marks per rebuild. Medium–large; 32 ApplyTheme overrides to audit.
- **reactivity-animation-deadline.** AnimationManager reports when the earliest active tween can next change anything; ToastHost's hold becomes a `.Delay` (today a no-op tween stepping every 16 ms: a 2 s toast runs 118 steps for 20 drawn frames; VideoPlayer auto-hide and tooltip delays alike). With runtime-idle-wakeups: ~60 → 1 wake per delay. Small.
- **layout-virtuallist-stale-rows-restyled** (owner 1; evidence for virtuallist-row-style-inheritance). In a one-argument list, rows made in render are resolved under theme defaults (VirtualListElement.cs:533); the next style pass re-resolves them under the list with different metrics before render replaces them. Vesper, 400 files, one Down: 205 resolves change metrics, 228 layout flag sets; realization 363 KB of a 571 KB press.

## Examined, not worth it

- reconcile-array-enumerator-boxing: none left (cc842ac removed the last); two snapshot arrays per ReconcileChildren are 4.6–7 % of rebuild bytes — under the bar; P5a stands.
- Fading the VideoPlayer bar in place: ~0.7 MB per rare fade.
- MarkStyleDirtyDown O(n·depth) alone: 0 B; folded into restyle-on-change.
- c2bf858: a continuation running synchronously inside an effect would subscribe it; no code does this.
- Notes: Junction History rebuilds the a11y tree twice per press (expert 7). Reel's and Aperture's chapter panels read `Position` in the app Build (ReelApp.Chapters.cs:126, ApertureApp.Chapters.cs:149) → whole-app rebuild 10×/s while open (guideline example).

## Needs the user

1. **reactivity-pointer-rebuild-per-frame:** pointer-move handlers run at most once per frame that may be drawn, with the frame's last position (as browsers do). A drawing app would lose intermediate points without a coalesced-points API. (a) all pointer moves, or (b) only the splitter, pan and slider callbacks?
2. **reactivity-toasthost-fade-in-place:** ToastHost's `onChanged` would run on appear/change/go only, not per fade step (its doc says "fades" today). OK?
3. **reconcile-restyle-on-change, SelectionColor:** an app-set `TextElement.SelectionColor` would start to show (today the theme overwrites it). No app sets it. OK?
4. **reactivity-animation-deadline** (only with runtime-idle-wakeups): a tween started between frames starts its clock at its first step; frames shift by at most one interval. OK?

# Round 5 survey: 8. App workloads

**Setup.** Compared 969f3308 (after round 4) with 653971d3 (before; same code as beed97f). Both were NativeAOT publishes with the same probe: round 4's, plus counts of laid-out elements, MeasureSize calls, content measures, resolves and a11y patches, plus `clickid:`, `hoverid:` and `size:` script steps. Covered: all five apps, and the Fluent sample, which is the only host of the rich text editor that needs no GitLab.

**Windows.** 62 real windows. Each had the SDL hint as an env var and in process, a CBT hook on the UI thread (`WS_EX_NOACTIVATE` at creation, every activation refused), `WS_EX_NOACTIVATE` set again on the window, and a 5 ms foreground guard. All closed by script. No OS input: keys, moves, wheel and three clicks went in process through `Window.HandleInputEvent`; clicks used button 0, and only after a hit test confirmed the target.
- **Foreground-guard events: 0. CBT refusals: 0.**

**Method.** Before and after ran alternately (ABBA). Shares were taken while eight surveys ran. Bytes and counts were identical across repeats.

**Extra builds.**
- r5m: byte marks inside Junction's Commit view, plus per-job byte counters on its worker threads.
- r5t: a tally of which elements get laid out (Junction, Meridian).

**Where things are.** Probe code, scripts, CSVs and logs are in the scratchpad `r5apps/`: `probe5.py` (applies and reverse-edits at either commit), `probe/BenchHook.cs`, `probe/BenchCounters.cs`, `jmarks.py`, `ltally.py`, `fluentaot.py`, `iv.py`, `an4.py`, `tally.py`, `topframes.py`, `st.py`, `memrows.py`, `*-session.ps1`, `runs/`.
- The worktree is clean at 969f3308.
- The scratch repo's README is restored.
- The local HTTP server for Meridian is stopped.
- The user's own Junction process was left alone.

SATISFIED: no

## Review: did round 4 help the real apps?

Yes in all five apps. Nothing got worse. Shares below are UIA listening, before → after.

| App, session | Shares before → after | Bytes |
|---|---|---|
| Junction | render 49.6–51.9 → 49.5–49.8 %; flush 28.7–30.6 → 30.6–33.0; layout 6.4–6.7 → 4.4–4.6; style 1.1–1.2 → 0.3; a11y 5.5–5.6 → 5.4–6.0; present 8.2–9.5 → 9.7–11.8 | session UI thread 44.85 → 26.94 MB (−40 %); a11y off 39.54 → 21.89 (−45 %); KB/input 115.1 → 40.8 |
| Vesper (round 4 script) | render 43.1–43.5 → 42.8–45.2; flush 35.1–35.6 → 33.8; layout ~2.7 flat; frames 191 → 94 (key-ups, no-op wheels, idle moves) | 25.5 → 24.9 MB (−2 %: this script misses the partner and grid paths, see below) |
| Meridian | render 24.6–42.2 → 14.4–15.0; text 11.4–21.3 → 1.1; flush 44–65 → 76 | 22.82 → 16.22 MB (−29 %); KB/frame 85–91 → 17 |
| Aperture | render 29.9 → 31.9–33.3; flush 40.1 → 32.9–35.8; a11y 3.2 → 6.4–6.9 (37 frames instead of 97: no-op wheels gone; a11y bytes flat) | 9.38 → 7.94 MB (−15 %) |
| Reel, 1080p24 | render 61.7–64.5 → 66.1–67.9; flush ~10 → ~7 | KB/frame 0.8 → 0.3; 12 s 0.80 → 0.59 MB (−26 %); a11y trees 39 → 18; loop passes 2,413 → 1,370 |

Per key or action, UI-thread bytes, before → after:

**Junction**
- **Chrome parts.** Title bar + sidebar + toolbar marks per key: 128.6 KB → 0.
- **Walks:**
  - History at 60 ms: 323.8 → 186.4 KB (−42 %).
  - History at 500 ms: 379.1 → 241.7 KB (−36 %).
  - Branches: 244.2 → 106.7 KB (−56 %).
  - Files: 498.7 → 222.2 KB (−55 %; render 173.7 → 95.8).
- **Tags once.** Builds per key 2 → 1; 430.0 → 115.3 KB (−73 %).
- **Themed patch keeps layout.** Commit no-op walk 290 → 116 KB (−60 %); MeasureSize 3,485 → 21 per key; a11y trees 0.85 → 0.05 per key.
- **Restyle on change.** Resolves per key: History 413 → 70, Branches 610 → 77, Tags 616 → 84, Commit 433 → 83, Changes typing 601 → 61.
- **Changes typing.** 266.8 → 122.2 KB per keystroke (−54 %); a11y off 234.8 → 94.3.
- **Key-ups.** Frames per press 2 → 1 (Junction and Vesper).
- **Review rows.** Not measurable in the real app without a GitLab merge request; the headless numbers stand.

**Vesper**
- **Partner walk** (both panes on one 59-file folder):
  - 60 ms: 576.4 → 296.1 KB (−49 %); rows realized 35.3 → 3.9 per key.
  - 500 ms: 596 → 297 KB (−50 %).
- **Brief:** Down 344.6 → 237.5 KB (−31 %), Right 337.9 → 241.2 (−29 %).
- **Thumbnails:** 343.7 → 216.5 KB (−37 %); a11y trees 0.85 → 0 per key.
- **Resize steps:** Brief 199.7 → 66.9 KB (−67 %), builds 0.6 → 0.2; Thumbnails 208.7 → 106.4 (−49 %).
- **Passive moves.** 60 idle moves: 60 → 3 frames.
- **No-op wheels.** 20 notches: 20 → 0 frames.

**Meridian**
- **18,884-line response frame.** 7.36 MB → 534 KB; 24,260 → 251 text calls (mono-ASCII fits). Its render share fell from 35–42 % to 1.9 % of the frame, but flush is now 98 % of it (candidate 6).
- **URL typing.** 312.1 → 309.2 KB per keystroke (flat).
  - Laid out 31.4 → 10.5 per keystroke, not 0 as in the headless suite (candidate 2).
  - Resolves 235 → 235: every element is patched; restyle part (b) is still the route.
- **Retained memory.** Managed heap after full GC 27.5 → 21.9 MB.

**Aperture**
- Posters walk flat: 157 KB/key; resolves 239 → 111.
- First library open: 2.23 → 1.72 MB.
- Item page now opened by an in-process click on the card: about 620 KB, flat.
- The hero scrim change is visual only. RefreshPreview for trickplay was not exercised.

**Reel**
- **Scrubber hover with card:** 30.1 → 3.4 KB per move (−89 %); VideoPlayer builds 0.69 → 0.02 per move.
- **After stills landed:** 42.8 → 3.5 KB per move (−92 %).
- **Second file (N):** 33.0 → 5.0 KB per move. Before, the second file had no stills; now it does.
- **Pointer resting while stills land** (7.5 s): 343 → 210 KB; a11y trees 22 → 7, with 15 patches.
- **1080p60 HEVC playback:** 495 → 297 KB per 6.7 s; render 156 → 10 KB.
- **Not measured:** the modal-loop video rate and the Vulkan drag. Both need a real window drag, which means OS input.

**Rich text editor (Fluent sample)**
- 60 ms caret walk: a11y trees 1.0 → 0.68 per key, with 0.9 patches per key.
- At 500 ms: still 1.0 tree per key and 0 patches. The layout-due refresh runs first (candidate 7).
- The sample rebuilds the whole app on every caret move (230 KB), so bytes per key barely move: 245.7 → 241.4 KB.

**Idle, start-up, memory**
- **Idle (10 s):** 0.1 loop passes/s in Junction, Vesper, Meridian and Fluent, UIA listening.
- **Start-up:** GL apps spend 61–69 % in SDL_CreateWindow (driver load) and 12–20 % on the first frame. Vulkan composition is unchanged; Aperture still makes 3 swapchains.
- **Working set after full GC:**
  - Junction: flat (live managed 68.0 vs 68.1 MB).
  - Vesper: 138 → 134 MB.
  - Meridian: 151 → 145 MB.
  - Reel: flat.

## Candidates

- **apps-junction-commit-tree-virtualize** (owner 8 for the app, 1 for VirtualList; root cause of junction-commit-file-switch)
  - **What:** the Commit view's file tree is a plain ScrollView with one row element per file or folder, and every key builds all of it (JunctionApp.Commit.cs:486). The Changes file list has the same shape (JunctionApp.Changes.cs:526).
  - **Evidence:** walking a 382-row commit costs 2,616 KB/key.
    - Commit view build 1,870 KB/key, of which the tree mark is 1,720.7 KB (66 %).
    - Reconcile 181 KB; a11y 1.05 trees/key (212 KB), because every row is in the tree; realize 310 KB (22.9 diff rows); 2.1 frames/key.
    - The highlight landing calls `MarkDirty`, not `MarkViewDirty` (JunctionApp.Highlight.cs:126): 0.45 chrome builds per key (20.9 KB).
  - **Expected:** O(files) becomes O(visible rows) per key (scaling bar); −60…−70 % per key on this commit. The Files tab already has this pattern (`FileTreeRowItem` item rows).
  - **Cost:** medium, app only. Guideline: a list that can reach hundreds of rows is a VirtualList.

- **layout-patch-keeps-layout-more-kinds** (owner 1; 5 for TextArea)
  - **What:** TextArea, Checkbox (and RadioButton, ToggleSwitch, RichTextEditor) do not opt into `PatchMarksOwnLayout`. Every rebuild that patches one marks layout up to the root (Element.cs:1654; TextAreaElement.cs:2062 has no override).
  - **Evidence:** laid-out tally in the real apps.
    - Meridian URL and header keystrokes lay out `response-body` (the 18,884-line TextArea) plus 9.5 ancestors including the root: 10.5 per keystroke, 365 MeasureSize calls.
    - Junction Changes summary typing lays out a TextArea, 2 Checkboxes and 7 ancestors: 10 per keystroke, 1,982 MeasureSize calls.
    - Layout is 8.5–10 % of those typing frames.
    - With a reader, each such frame also schedules an a11y refresh: 0.86–0.87 trees/key, 20–27 KB/key.
  - **Expected:** laid out per keystroke → 0–1. Typing frames −8…−10 % (time bar). With a reader, −20…−27 KB/key (Junction Changes about −22 %).
  - **Cost:** small–medium: each kind checks what it copies, as in a3aeabb2. No API or pixel change.

- **apps-vesper-components** (owner 6/8; was Waiting, covered by "one app at a time")
  - **Evidence:** in the partner walk (296 KB/key), the CommanderApp build is 237.7 KB per key (80 %).
    - Panes 169.4 KB: both panes on every key, 71 % of the build.
    - Chrome 30.7 KB, rail 14.6 KB, reconcile 21.4 KB.
    - Brief and Thumbnails keys (216–241 KB) carry the same 190–205 KB build.
  - **Expected:** per-pane parts, −45…−55 % per key.
  - **Cost:** medium, app only.

- **apps-meridian-components** (owner 6/8)
  - **Evidence:** URL or header keystroke 309–312 KB. MeridianApp build is 260.8 KB per keystroke: request tree 120.2 (46 %), editor 67.7, response 33.9, shell 19.3, titlebar 17.6. Resolves 235/key.
  - **Expected:** −50…−60 % per keystroke.
  - **Cost:** medium, app only.

- **apps-junction-view-parts** (owner 6/8; continues apps-junction-components)
  - **Evidence:** the History build is 57.9 KB per key.
    - The include bar (21.0 KB) and footer (13.1 KB) do not change with a selection: 59 % of the build (JunctionApp.History.cs:91, :96).
    - `FilteredCommits` copies the whole commit list on every build with no query: `data.Commits.ToList()`, :278, 7.9 KB/key.
  - **Expected:** −20…−25 % per History key (about 186 → 145 KB).
  - **Cost:** small (the ChromePart pattern).

- **gl-first-use-flush** (owner 3; updates meridian-response-frame-flush)
  - **What:** a one-time GPU flush stall the first time a process draws a new kind of thing.
  - **Evidence:**
    - The response frame is now light on the CPU (render 1.9 % of the frame) but flush is 97.9 %. That one frame is 28–30 % of Meridian's session frame time.
    - Sending the same request a second time draws the same kind of frame with no such flush: not in the top 4, under 2.5 %.
    - The first command-palette open: 0 KB, flush 99.7 %, 21–27 % of session frame time. Later palette frames are about 2.5 %.
    - The first frame of every app: flush 76–88 % of that frame.
  - **Expected:** likely Skia GL program compiles or atlas creation on first use (round 1 looked only at start-up). Needs a GL trace (KHR_debug timestamps or Nsight). If it is programs: warm them at idle, or keep a program cache through the GL interface. Could clear the start-up bar.
  - **Cost:** unknown until traced.

- **a11y-layout-refresh-bounds-only** (owner 7; Waiting, re-rank up)
  - **Evidence:**
    - Junction History at a reader's pace: a11y is now 113.3 of 241.7 KB/key (47 %, up from 30 % because the build shrank).
    - Key-up session at 400 ms: one 1,110-node tree per key.
    - Fluent rich text caret at 500 ms: 1.0 tree per key and 0 caret patches; at 60 ms there are 0.9 patches per key.
  - **Expected:** −30…−47 % per key with a reader.

- **apps-junction-highlight-prefetch** (owner 5/8)
  - **What:** each History selection prefetches the commit detail and tokenizes the opening file, old and new, on a worker. Round 1 examined this as app design; the numbers below are new.
  - **Evidence** (worker bytes per job):
    - One start-up tokenize, the first commit's opening file pair: 92.8 MB.
    - History at 500 ms: 5.9 MB of tokenizing per key plus 0.6 MB of detail, against 0.24 MB on the UI thread (25×). That walk also triggered 6 gen-0 GCs per 16 keys, mostly from worker allocations.
    - Commit file walk: 4.0 MB per file key.
    - History at 60 ms: 2.1 MB of detail per key (40 loads).
    - JunctionApp.Highlight.cs:109, JunctionApp.Commit.cs:246.
  - **Expected:** −50…−90 % of all-thread bytes per History key if it tokenizes only up to the last line the hunks show, the old side only for deletions, or after the selection rests.
  - **Cost:** medium. A later prefetch is a small behaviour change (colours may arrive a moment later).

- **suite-coverage** (owner 8)
  - **document-switch:** Junction Files walk is now 222 KB/key (render 96 KB from first draws, text 11–12 %). It would pin the sub-blob first-draw cost.
  - **image-grid:** Aperture posters 157 KB/key. Per key: 0.9 whole-app builds, 54 KB realize, 0.9 a11y trees with no patch, because the cursor is an outline, not a list selection.
  - **New:** typing beside a large TextArea and checkboxes (pins candidate 2).

## Examined, not worth it

- **Junction LOH at session end, with UIA** (gcheap 93.0 vs 81.6 MB): live data is equal (68.0 vs 68.1 MB). All-thread allocations across the Commit switch are equal too (61.2 vs 61.3 MB off the UI thread). The difference is LOH free space, depending on GC timing. Not a leak.
- **Fluent's Rich text page first draw:** render bytes 21.6 → 169.4 KB (+15 % on the page open). This is b52ca178's accepted first-draw cost. Junction's Files walk render bytes fell 173.7 → 95.8.
- **Fluent sample rebuilding the whole app on every caret move** (`OnSelectionChanged(app.Refresh)`, 230 KB per caret key): sample code. Guideline material.
- **Junction History realize** (42.2 KB/key, about 16 KB per row): unchanged. The row template is app code; keep-rows was examined in round 2.
- **Vesper viewer PageDown:** 207 KB per page (18.3 new rows), unchanged.
- **Aperture's 3 swapchains and the Vulkan first present:** known (round 3).
- **Start-up and idle:** no new lever.
- **an4's "builds per key":** counts a build made inside an input handler twice. All build counts above come from the memory-probe counters.

## Needs the user

- **Vesper Detail resize (vesper-detail-resize-stale, not perf):** still open. e6a432fa added the path-trail comparison (`WindowFitChanged`/`PaneFit`), but only behind `if (!gridView) return;` (CommanderApp.cs:3121). In Detail, 10 resize steps cause 0 builds, so the trail stays stale. Running the same comparison in Detail would now be a small fix.
- **Other non-perf leads:** no round 4 commit touched junction-narrow-gitlab-glyphs, meridian-pane-choice-not-persisted, meridian-min-width-wraps, aperture-poster-as-hero or aperture-tmdb-size-fallback. Round 4 left the Meridian app untouched; in Aperture it changed only the scrim and trickplay `RefreshPreview`.
- **Reminder:** Aperture signed in to jellyfin.home 3 more times this round as device "aperture-survey". The API key was read from its file by path, never printed. Remove those devices and revoke the key when the work is done.

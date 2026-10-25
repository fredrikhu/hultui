# Round 4 survey: 8. App workloads

All five apps published NativeAOT at 0951288 and beed97f, same probe on both (round 3's probe plus
anchor edits). 73 real windows: SDL hint as env var and in process, a CBT hook on the UI thread
(`WS_EX_NOACTIVATE` at creation, activation refused), `WS_EX_NOACTIVATE` set again once the window
existed, a 5 ms foreground guard; graceful close; no clicks, no OS input. **Foreground-guard events:
0.** The CBT hook refused 17 activations — exactly one per custom-frame launch at 0951288, 0 at
beed97f (confirms 48527ac). Shares taken while eight surveys ran. Bytes and counts identical across
repeats. Probe code, scripts, CSVs and logs in the session scratchpad `r4apps/` (`probe/BenchHook.cs`,
`probe/BenchCounters.cs`, `appprobe.py`, `*-session.ps1`, `an4.py`, `chrome.py`, `undrawn.py`,
`keyup.py`, `lat.py`, `runs/`); scratch repo `r4apps/jrepo` (pushes disabled). The user's own Aperture
process (22836) was left alone. Worktree clean.

SATISFIED: no

## Review: did round 3 help the real apps?

None made worse. Yes in four of five; Meridian flat.

| App, session | Shares before → after | Bytes |
|---|---|---|
| Junction, UIA on | render 49–52 % → 41–52 % (realize 7–12 → 4–5, text 7–8 → 7–10); flush 26–28 → 31–35; a11y 7.3–9.8 → 4.2–6.2; layout 6.4–7.1 → 4.4–6.5; style ≤1.1; present 6 → 7 | KB/frame 186–188 → 63.7 (−66 %); UI thread over 27 s of walks 75.7 → 44.5 MB (−41 %) |
| Junction, a11y off | same pattern | KB/frame 116.8 → 48.0 (−59 %); UI thread 57.9 → 39.2 MB (−32 %) |
| Vesper | render 40–48 % → 31–36 % (realize 8–10 → 2); flush 37–47 → 47–52; a11y 1.8–2.1 → 2.3–2.7; layout 3.5–4 → 2.2–2.5 | KB/frame 131 → 27.6 (−79 %); UI thread 48.6 → 25.5 MB (−48 %) |
| Meridian | render 22–30 % → 17–20 %; flush 61–71 → 74–78 | KB/frame 90–91 → 89–90; UI thread 16.7 → 16.6 MB (flat) |
| Aperture | render 29–30 % → 30–32 %; flush 30–34 → 31–32; a11y 3.1–3.6 → 3.9–4.9; present 25–27 → 28–29 | KB/frame 61–73 → 33–37 (−50 %); UI thread 9.1–10.9 → 6.7–7.2 MB (−30 %) |
| Reel, 1080p24 H.264 | render 60 %, present 25 → 28, flush 9 → 10 | KB/frame 3.3 → 0.7–0.8; UI thread per 12 s 12.5–12.7 → 0.83–0.86 MB (−93 %) |

Per key or action (UI-thread bytes, before → after):

- Junction (UIA on): History walk (60 ms keys) 560 → 321 KB (−43 %), a11y 294 → 60 KB per key, 41 full
  rebuilds → 24 rebuilds + 39 patches per 40 keys (3730052 works); History at 500 ms builds per key 2 → 1
  (b985e74); Commit no-op walk 638 → 285 (−55 %); Branches 696 → 243 (−65 %); Files 671 → 486 (−27 %);
  wheel 101 → 46.5 KB per notch; Changes typing 1,964 → 267 KB per keystroke (−86 %; a11y off 1,219 → 235).
- Vesper: pane walk 571 → 262 KB (−54 %), rows made per key 35 → 0.7; first code folder 898 → 739 KB;
  viewer PageDown flat (212 → 211).
- Meridian: header typing flat (311 KB per keystroke); 18,884-line response frame 7.70 → 7.53 MB (still
  24,195 text calls); wheel 6.6 → 6.0 KB.
- Aperture: poster walk 252 → 158 KB (−38 %); first library open 2.1–2.9 → 1.6–1.7 MB.
- Reel: quiet playback 4 s 4,030 → 223 KB, VideoPlayer builds 91 → 1; pointer sweep 198 → 7.5 KB per
  move; seek 446 → 44 KB; minimized 6.5 s 2,281 → 165 KB, loop passes 77 → 31/s.

Idle (10 s): 61 loop passes/s → 0.1–0.3/s in Junction, Vesper and Meridian, UIA listening. Working set
after a full GC: Junction flat (235–252 → 236 MB; 78 MB managed both), Vesper 141 → 134, Meridian 141 →
137, Aperture flat, Reel 408 → 396. Start-up unchanged (GL driver load ~half of time to first frame;
Aperture 3 swapchains; Vulkan first frame 35–38 %).

## Candidates

- **events-noop-input-frames** (owner 1 or 4). Every input except mouse moves forces a full repaint
  today (Window.cs:2133-2138): any key-up, any wheel notch that scrolls nothing. Repaint only when an
  ordinary handler ran, hover changed, a drag is active, or something marked itself dirty. Key-down and
  key-up 70 ms apart, 20 keys in Junction History and the Vesper pane: 20 down frames + 20 up frames; up
  frames change nothing (0 B, no style/layout/a11y) but are full repaints; median up vs down frame
  Junction 0.88 vs 1.24 ms (a11y off 0.90 vs 1.18), Vesper 0.78 vs 0.93 — the up frame is 42–46 % of a key
  press's frame time; every typed character pays it too. Vesper wheel over a pane that cannot scroll: 20
  notches → 20 frames, 0 B. Frames per key press 2 → 1 (frame time per press −40…−46 %); no-op wheels →
  0 frames. Small–medium; audit owned handlers that act on key-up or wheel and might mutate without
  marking dirty. Suite row: key-down and key-up apart, frames per press.
- **apps-vesper-passive-move-handlers** (owner 8, app; re-measures vesper-root-move-handler). Vesper's
  root PointerMove handler (CommanderApp.cs:745) and each pane row's (CommanderApp.Views.cs:452) to
  `OnPassive`; Junction's review rows (JunctionApp.Review.cs:687) too. They act only during a find-resize,
  right-drag mark, context hold or review drag and must mark dirty themselves. 60 pointer moves along one
  row: 60 full frames at both builds, each repainting nothing (~1.0 KB UI-thread per move; render 47–50 %,
  flush 40–42 %). Frames per idle move 1 → 0; optionally make `OnPassive(handler, listening)` public
  (~104 → 40 B per move). Small, app only. Guideline: a move handler that usually does nothing must be
  passive.
- **a11y-layout-refresh-bounds-only** (owner 7; expert 1's layout-themed-patch-keeps-layout covers
  part). With a reader, every frame with layout dirt (Window.cs:1179) schedules a whole a11y tree
  rebuild; re-snapshot the applied tree's bounds instead, or rebuild only subtrees whose layout changed.
  beed97f, UIA listening: Junction History 60 ms keys — 24 layout-due rebuilds of 1,110 nodes (~100 KB
  each, 23 in undrawn a11y-only frames) = 2.29 of the walk's 2.41 MB a11y bytes, 57 KB per key (18 % of
  321); History at 500 ms (a reader's pace) — 16 rebuilds and 0 patches per 16 keys (the leading edge
  fires on every key, so 3730052's patch never runs), 113 KB per key (30 % of 379); Aperture posters
  18–19 rebuilds per 20 keys, 28 KB per key (18 %); Commit no-op and Branches 15–16 per 20 keys (~5 %);
  Vesper 27 per 45 keys (3 %). In History the details pane really moves. Churn −18 % / −30 % / −18 % per
  key with a reader; O(nodes) → O(moved nodes). Medium; events identical; a11y-property-patch family.
- **apps-junction-components** (approved; real-app evidence for expert 6's prototype). Unchanged chrome
  per build in the AOT app: titlebar 26.2 + sidebar 47.8 + toolbar 54.6 = 128.6 KB + 13–21 KB reconcile
  per key. History 69 % of Build, 40 % of UI-thread bytes with UIA (49 % off); Branches 75 % of Build,
  53–56 % of UI bytes; Files (1.44 builds/key) 185 KB = 69 % of Build, 37–40 %; Changes typing 131 KB
  per keystroke = 61 % of Build, 49 % (UIA) / 56 % (off). Matches −40…−75 % per key.
- **apps-meridian-components, apps-vesper-components** (next "one app at a time"). Meridian typing 311
  KB per keystroke at both releases, Build 260 KB (request tree 120 KB = 46 %, editor 67, response 34,
  shell 19, titlebar 18). Vesper: panes 78 % of Build bytes (13.8 of 17.8 MB per session; 163 KB per
  build, both panes every key), chrome 15 %, rail 7 %. Churn −40…−60 % per key expected. Medium each.
- **meridian-response-frame-flush** (owner 2/3; lead, needs a trace). Meridian's 18,884-line response
  frame: flush 86–152 ms vs render 25–32 ms in all four runs, both releases (72–85 % of that frame; every
  other Meridian frame flushes under 1 ms) — the worst single frame in the five apps. The CPU side is
  text-open-measures-every-line (expert 5). Unknown until traced.
- **junction-commit-file-switch** (owner 8, then 5/2; lead, needs a profile). Walking the files of a
  large commit: 2.72 MB per key at beed97f (4.84 at 0951288), 2.15 builds per key; the view's Build 1.84
  MB per key (each new file's diff flattened and highlighted); a11y 211 KB per key (one Children rebuild
  per file); realize 301 KB (23 rows). The most expensive key walk in any app, ~8× History.
- **Evidence for experts 3 and 7:** Reel quiet playback at beed97f, 35 s, UIA listening: 1.4 KB of
  UI-thread bytes per video frame — render 371 B (media-vulkan-video-frame-bytes), a11y 491 B, between
  frames 503 B; the a11y part is 3.0 full tree rebuilds per second (4.3 KB each) from the scrubber's
  range refresh (supports a11y-leaf-property-patch).
- **suite-coverage** (owner 8): key press with down and up apart (frames per press); the real-loop a11y
  walk at a reader's pace (500 ms; would show 16 rebuilds per 16 keys); Vulkan steady-frame bytes (Reel
  0.9 KB per frame + 0.5 KB between frames, unpinned); document-switch (Junction Files 486 KB per key,
  text 24–26 %); image-grid (Aperture posters 158 KB per key, a11y 18 %); a walk beside unchanged chrome
  (would pin apps-junction-components).

## Examined, not worth it

- junction-watcher-self-reload: 0 reloads in 44 real-app starts (16 sessions + 28 start-and-close loops,
  12 after touching a tracked file). The watcher only saw `index.lock` created (2 per start, 3–4 after a
  touch), never `index`, refs or work-tree events (the gitdir watcher is not subscribed to renames). App
  cost 0; the flake belongs to the test fixtures. If the tests need it: `GIT_OPTIONAL_LOCKS=0` for
  read-only git calls brings lock files per touched start 3–4 → 2.
- idle-wake-latency-p90: real Junction window, one key posted from a worker every 500 ms (40 per run, 2
  runs per build): post → handler median 0.03–0.04 ms, p90 0.05–0.11; post → frame median 1.59–1.69, p90
  1.78–1.99 — same on both builds, no rise in a real app (under load).
- reel-still-previews-rebuild (expert 6 has it): 90 s of real playback with the pointer elsewhere, 1
  ReelApp rebuild (37.9 KB), no still landings — costs only while stills are made.
- Commit no-op walk layout share 15–20 → 20–25 %: share only; layout per frame fell (median 0.36 → 0.20
  ms; text layout calls 213 → 132 per key); the rest is expert 1's themed-patch-keeps-layout.
- Meridian: flat; what remains is text-open-measures-every-line (expert 5) and the components follow-up.
- Vesper viewer PageDown: 211 KB per page = 18 newly shown rows.
- Aperture item page: not measured (Enter on the cursor card did not open it from the probe; round 3:
  flush and present dominated).
- Start-up and working set: no new lever.

## Needs the user

- Reminder: Aperture signed in to jellyfin.home 8 more times as device "aperture-survey" (API key read
  from the file by path, never printed). Remove those devices and revoke the key when the work is done.

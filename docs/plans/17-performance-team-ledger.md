# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 18
- **Phase:** build — round 18, step 13 next (text-bidi-wrap-ranges); step 12 (vk-swapchain-two-images) moved after 14, or sooner once the 4K is attached: its approved HDR drag run (question 74 (c)) needs the 4K, and only the 1440p has been attached since step 8. The survey ran 2026-11-22 in session
  00f54f03 (reports `17-reports/r18-*`; usage 1 → 8 % of the week, the 5-hour window 7 → 54 %). Questions 73–77 are answered (73 → (b), 74 → (c), 75 → (a), 76 → (a), 77 → (a) on
  2026-11-23, its commit riding with step 7). Round 18's files (the survey brief `brief-r18-survey.md`, build
  briefs `brief-r18b<N>.md`, `archive_r18.py`, `merge_r18.py`, `desk.ps1`, agent ids `r18-agents.txt`, probes
  `r18-<area>\` and `r18b<N>\`) are in session 00f54f03's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\00f54f03-b511-4949-9273-30fe719e1bbb\scratchpad\`. Round
  17's are in session 35903ed7's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\35903ed7-ff10-416d-84e9-b6c09675ab6b\scratchpad\`
  (`fullsuite-r17.sh` with `fedora-r17.sh` is the model for round 18's full suite, `brief-r17b<N>.md` for its build
  briefs). Older scratch publishes have lost their DLLs (r18-8): re-publish rather than reuse them. Round 17 RELEASED
  2026-11-20: main e799c23b, CI run 44653899561 green on the first attempt. The Jellyfin account to browse as is in
  memory `aperture-jellyfin` (never in committed docs). Read the desk at the start and before every hold. To resume in a
  fresh context, run in this worktree: `/loop Run the next step of the performance team: follow
  docs/plans/17-performance-team.md, with state in docs/plans/17-performance-team-ledger.md. Read both once per session
  (afterwards only the ledger's Phase and the part the step needs), do the step the ledger's Phase calls for, record it
  in the ledger, and commit.` Before resuming, `git fetch` and merge main if it moved; build steps use
  `scripts/perf/step-ab.ps1`. Older rounds are in [the archive](17-performance-team-ledger-archive.md) (search it, don't
  read it whole). History note: rewritten 2026-10-25 (every commit after 3353ce47 has a new ID; a pre-push hook refuses
  pushes built on the old history).
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`)
- **Review commits since:** round 18's survey reviews round 17's build, `git log --no-merges 0df5e3ea..e799c23b^2 ^45cbde7b^2` (the archive commit to the round 17 release; 41 commits not merges), with main's 13 commits outside plan 17 that 45cbde7b merged (781a857f, ba15d841, 78a69ee5, 52acb2f2 with their plan 19 notes and rows; 3490ba67; e66ead9f) and main's 5 that b28e9128 merged (fe8f4ecf, 0f9c8477, ee044d36, f67b25ec, 93f8a26d). Earlier: round 17's survey reviewed round 16's build, 32c85e81..b131ae85 (the last survey report to the round 16 release; 50 commits not merges), with main's 8 commits outside plan 17 that its merges 666e6486 and 5e180d17 brought in (266d6aee, 4917f40d, 23b552d8, 38ec9773, b8a3dcf5, d3e40ecb, 22c6e002, 791d1a44); main gained nothing after b131ae85. Earlier: round 16's survey reviewed round 15's build, 587f1767..75f2288a (the last survey report to the round 15 release; the one merge is round 15's own into main), and main's 41 commits outside plan 17 since that release, `git log --no-merges 75f2288a..3038f82b ^9550ecec` (42 with the ledger's cc093b50) (SkiaSharp 4 043f5b38, SDL 3.5.0 fd0cccb7, the app team's fixes; the screen-reader fix counts as reviewed). Earlier: round 15's survey reviewed round 14's build, 06d4db59..5b2985be (the last survey report to the round 14 release; the one merge is round 14's own into main). Earlier: round 14's survey reviewed round 13's build, 8f01b6fd..e432c224 (the last survey report to the round 13 release; its two merges are round 13's own into main). Round 13's survey reviewed round 12's build, 5f1744ca..afc1ed40 (the last survey report to the round 12 release; the one merge is round 12's own into main). Round 12's survey reviewed round 11's build, 5731ee58..b0dd1954 (the last survey report to the
  round 11 release; it includes the .NET 11 upgrade's step 0 only as already reviewed). Earlier: round 11's survey reviewed round 10's build and the .NET 11 upgrade, f4d6050d..868e2ffc
  (the last survey report to the upgrade's captures; round 10's merge into main cc55bdcc is in the range). Earlier: round 10's survey reviewed round 9's build, 4919ac40..facced69 (the last survey report
  to the round 9 release; 82 commits, 81 not merges; no merge from main this round). Round 9's survey reviewed
  eaa1ec29..10acc8c0 (round 8's build, with main's merge 3a15b5b1); round 8's survey reviewed 17d4220a..df45b4a2.
- **Probe code:** round 18 surveys in session 00f54f03's scratchpad `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\00f54f03-b511-4949-9273-30fe719e1bbb\scratchpad\r18-<area>\`; round 17 surveys in session 35903ed7's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\35903ed7-ff10-416d-84e9-b6c09675ab6b\scratchpad\r17-<area>\`
  (the common brief `brief-r17-survey.md` and `archive_r17.py` beside them); round 16 surveys and build probes in session
  b6ef64d9's scratchpad `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\b6ef64d9-4ed1-4793-9955-6a986d74de79\scratchpad\`
  (`r16-<area>\`, `r16b<N>\`); round 15 surveys in session 8e8ba33b's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\8e8ba33b-db33-43fa-885a-cb6b82b7253f\scratchpad\r15-<area>\`
  (the common brief `brief-r15-survey.md` and `archive_r15.py` beside them); round 14 surveys in session 41943a9b's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\41943a9b-f657-4483-971a-a2b346b967f7\scratchpad\r14-<area>\`
  (the common brief `brief-r14-survey.md` and `archive_r14.py` beside them); round 13 surveys in session 17c11f5e's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\17c11f5e-9de0-4477-8f58-0d0a5e006605\scratchpad\r13-<area>\`
  (the common brief `brief-r13-survey.md` and `archive_r13.py` beside them); round 12 surveys in session 0bab04ae's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\0bab04ae-e7a9-44cb-b4da-96d849a107f6\scratchpad\r12-<area>\`
  (the common brief `brief-r12-survey.md` and the archive script `archive_r12.py` beside them); round 11 surveys (and
  the upgrade's harness `r11u\`) in session ec6d577c's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\ec6d577c-f9ba-4479-acbd-908e67c0ea0a\scratchpad\r11-<area>\`
  (the common brief `brief-r11-survey.md` beside them); round 10 surveys in session 3484c586's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\3484c586-c631-4af0-957d-95e7a03cac41\scratchpad\r10-<area>\`
  (the common brief `brief-r10-survey.md` beside them); round 9's build-step probes `r9b<N>\` in session c484f5ac's
  scratchpad `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\c484f5ac-8f22-48dd-b669-13c24b1e7889\scratchpad\`;
  round 9 surveys in session c484f5ac's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\c484f5ac-8f22-48dd-b669-13c24b1e7889\scratchpad\r9-<area>\`
  (the common brief `brief-r9-survey.md` beside them); round 8's build-step probes in
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\64e7e0e2-bc1c-4069-9d42-9311fee04c9e\scratchpad\`; round 8 surveys in session 64e7e0e2's old scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\64e7e0e2-bc1c-4069-9d42-9311fee04c9e\scratchpad\r8-<area>\`;
  round 7 surveys (and builds) in session 855f4ddd's scratchpad `r7-<area>\`; round 6 surveys in session 855f4ddd's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\855f4ddd-feda-4f0a-a595-a83e07af08f8\scratchpad\r6-<area>\`
  (each report names its files). Rounds 4–5 in the scratchpad of session dc3c745c (a fresh context may have another
  scratchpad): `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\dc3c745c-a83d-4dc0-96ba-442a47e69937\scratchpad\`.
  Round 5 surveys `r5*/` (r5rt: R5Host walk/gc host; r5a11y: staleness audit; r5apps: app probe), round 5 builds
  `r5b11/`…`r5b14/`, the total comparison `total/` (part B: default-frame app hosts for both trees,
  walks.jsonl), round 4 `r4*/`.

**Repository move (user, 2026-10-24): HultUI moved to `github.com/fredrikhu/hultui`; the old
`hultui-revamped` will be deleted. Never push to the old URL again.** The new main had the same
history (d2bc157b). The shared `origin` was switched at round 4's release (2026-10-25: `git remote
set-url origin git@github.com:fredrikhu/hultui.git`, `git fetch --prune origin`). Open PRs/issues on
the old repo don't carry over. The repository is being prepared for open-sourcing: plan 17 adds only
HultUI and its apps, nothing outside them. **The new repo will be public and will
NOT get self-hosted runners (user, 2026-10-24: anyone could push code that runs on them); it uses
GitHub-hosted runners** (`ci/github-hosted-runners`, docs/CI.md; merged into main with round 4 at the
user's choice, 2026-10-25). Plan 17 never registers a runner. Hosted runners are slower (2-core while
the repo is private; Windows CI ~26 min) and differ in fonts and tooling: expect new CI precondition
failures like 187dbe1's, fixed in tests, not by adding runners.

**App releases:** cut only when the user asks (they asked after rounds 2 and 3; round 3's on
187dbe1: junction-v1.21.0, vesper-v1.21.0, meridian-v1.4.0, aperture-v1.20.0, reel-v1.5.0). Follow
memory `release-tagging` (≤3 tags per push).

## Verdicts

| Expert | Round | Satisfied | Review objections |
|---|---|---|---|
| 1. Layout | 18 | no | none (dd3ad712: every list is sized from outside; e790d321: no layout objection; 0f9c8477's ring and Tab stop only paint). Real window (1 hold, 2 launches): selectable labels' wiring and the VirtualList's children set → step 4 (Junction Commit −11.6 %, Vesper reader −15.5 % a wheel notch); found on the way, a text field's right-click menu acts on a detached copy after a rebuild → step 4 (a) |
| 2. SkiaSharp: painting | 18 | no | none (0a7bb324 saves CPU only: Skia drops alpha-0 layers itself; c23edfb9's bounds right). Real window (3 holds, 9 launches): Vesper's Multi-rename fades three NumericUpDowns through the card's layer (+9 MB GPU) → step 11 (a); Junction's drag ghost is a window-sized layer (+18 MB) → step 11 (b), question 73 |
| 3. GL and Vulkan | 18 | no | none (c23edfb9 and 0a7bb324 flat over a 50 s session with mpv; b1423e6e costs one late frame at most). Real windows (4 holds, 17 launches): two swapchain images −8…−180 MB DXGI local → step 12, question 74; a timer-step resize rebuilds Vulkan's swapchain every step, a modal-loop drag 1–2 times (vk-drag-steps-dropped closed) |
| 4. .NET runtime | 18 | no | none (a787c194 clean on AOT; 3ea96588 keeps ~2 KB; Aperture's history bounded at 100 visits; element sizes unchanged, `MediaItem` +72 B); b1423e6e never collects after a big walk that finished → step 10. Real window (1 hold, 2 launches) |
| 5. Text pipeline | 18 | no | e790d321: a size ask answered from `_cache` leaves the wrap range stale after a return to the opening width (Vesper zh drag +19 % characters itemized) → step 2; 781a857f proves ~125 HarfBuzz calls a mark on first frames (Junction first frame +13 % UI bytes, Meridian's open −4.6 %; deferring it is under the bar on AOT); 78a69ee5 a large win (Arabic reader open 39,963 → 3,871 calls). Right-to-left text has no wrap ranges → step 13. Real window (1 hold, 4 launches) |
| 6. Reactivity and reconcile | 18 | no | 0f9c8477: Back to a scrolled library grid lands at the top, against its own promise → step 3; Aperture's parts build on every load (step 7), each poster landing makes the cards again (step 8); keeping the page Back returns to → question 75. Real window (1 hold, 4 launches) |
| 7. Accessibility bridges | 18 | no | none on 13fee514; 0f9c8477: Escape to Home leaves the focus on nothing and Play does not name the film → question 76; round 17's 82.8 % "other" share was a frame-log trap (each row read the previous frame's total). With a real client each `UiaDisconnectProvider` is 0.56–0.73 ms on the UI thread → step 5. Real window (2 holds, 7 launches) |
| 8. App workloads | 18 | no | round 17 helped: Junction first file visits −22 %, view switches −4…−40 %, bigrepo working set at rest 202.4 → 134.9 MB, the player bar's first fade +12 → +2 MB; 45cbde7b: Junction start +7.7 % once a session, History Down +5 % (low). Aperture's picture cache keeps ~30 MB a page opened → step 6; a shelf turn builds all of Home → step 9. Real windows (4 holds, 30 launches); round 17's scratch publishes lost their DLLs |

## Queued candidates

Rounds 1–17's build orders all landed but round 6's step 8, round 10's step 14, round 11's step 1 (c), round 14's
step 8 and round 15's step 8 (a), measured and declined, round 11's steps 13 and 14, round 13's step 5 (b),
round 15's step 1 and round 16's step 10, measured with nothing to change, round 13's step 6 (c), not attempted
(back to Waiting), and round 17's steps 9 and 10, not taken, and step 11, stopped for question 72b (all three back
to Waiting) (rounds 5–17 are in [the archive](17-performance-team-ledger-archive.md#round-17-build-order), with each survey's
closed-from-Waiting note). Round 18's build order follows, from its survey.
A build step may bundle one expert's related small candidates as separate commits. Items marked **(user)** wait for a
decision in Needs the user; the rest of their step goes ahead.

### Round 18 build order

Ranked by expected effect in the real apps over cost (the brief's "Real apps first"); the decided item first, then the
two review objections. Each step names the report lines its brief quotes (`r18-k` = `17-reports/r18-k-<area>.md`) and
measures its real-app case in a guarded real window (the lock `Global\HultUI-r18-desktop`, the harnesses in session
00f54f03's scratchpad `r18-<area>\`) as well as by step-ab. Resize steps that stand for a user's drag are framed by
`WM_ENTERSIZEMOVE`/`WM_EXITSIZEMOVE` (r18-3's `dragrelM`); a bare timer step rebuilds a Vulkan swapchain every step.

1. **vesper-file-not-its-own-partner** (6; question 72b: (2), decided) — with both panes on one folder and the other
   cursor on ".." or a folder, F3 ("Compare files") pairs the cursor's file with itself ("Files are identical"), rings
   it and moves the ring with every key, so each key builds the window (headless 29.5 KB a key against 2.2; real window
   50.8 KB, r18-8). (2): F3 does nothing there, as on a folder, and nothing is ringed; `PartnerStays`
   (CommanderApp.cs:1517) and `DiffTargetPath` (the ring, Pane.cs:807/982) follow the same rule. Round 17's stopped
   brief is `brief-r17b11.md` with `r17b11-*` in session 35903ed7's scratchpad. Small; app; no API.
2. **text-wrapped-size-cache-hit-keeps-range** (5; r18-5 REVIEW and Candidate 1; the review objection to e790d321) — a
   size ask answered from the width-keyed `_cache` leaves the row's wrap range at the last drag's far end, so after the
   window returns to a width the view opened at, the next drag breaks every row again (Vesper zh drag: characters
   itemized 364,749 → 434,070, +19 %; prefix tables +26 %). Re-put the layout's range on a size-path hit only (on
   Layout's hits too it ping-pongs: the split view's first sweep 33.0 → 81.8 KB). Prototype `TEXT_SZRESTORE=2`
   (`r18-text\r18-probe.patch`), the second sweep: a359e426 split AOT 23.6 → 18.5 KB a step (−22 %; real window 23.6 →
   18.6), unified JIT 19.4 → 16.6, 574f43a7 split 45.6 → 22.8; re-breaks back to round 17's counts. Run round 15's
   real-document equivalence. Small; no API, no pixels.
3. **reactivity-aperture-back-lands-where-it-was** (6; r18-6 REVIEW and Candidate 1; the review objection to 0f9c8477) —
   Back, Forward, Escape or a rail reopen to a scrolled library grid lands at the top (`LetGoOfGrid` empties
   `_gridList`, so `RevealGridCursor` has no list), though 0f9c8477 and History.cs promise "back where you were
   scrolled"; the first key after costs 279.1 + 75.6 KB, 2 builds, 361 elements. Keep the grid's scroll offset in the
   `Visit` (mouse users too, as documented) and fall back to the cursor's row; make, ring and focus the cursor's card in
   the Back's own frame (PROBE_REVEAL: lands at 2674 with card-m070 focused, the next keys 0.4 KB; PROBE_SCROLLKEEP: the
   mouse case at 2400). Question 76 (a), if answered so, rides along as its own commit. Add a 300-film case to
   ApertureAppHostTests.History. Probe `r18-reactivity\probe18.patch`, `ProbeR18Tests.cs`. Small; app; no API.
4. **elements-context-menu-after-patch + elements-selectable-label-static-wiring + layout-list-children-without-set**
   (1, expert 4 reviews the AOT bytes; r18-1 Candidates 1–3, r18-4's re-rank of elements-selectable-label-lazy-wiring) —
   separate commits. (a) Correctness: `Element.PatchFrom` copies `ContextMenuItems` (Element.cs:1797), and for
   TextInput, TextArea, RichTextEditor and selectable labels that provider is bound to the build's fresh copy, so after
   one rebuild the right-click menu acts on a detached element (Cut, Copy and Paste off; Select All selects the copy).
   Keep an element-owned provider across a patch (a flag set by the wiring), keeping the public getter. (b)
   `WireSelection` (TextElement.cs:280): static handlers that read `e.CurrentTarget`, in one exact 6-entry array: 880 →
   304 B a label (1,552 → 976). (c) `ReplaceChildren` builds a `HashSet` when old × new > 64 (`SetOf`, Element.cs:1523),
   and `SyncVisibleItems` makes a new array at each window move (VirtualListElement.cs:1816): marks or a pooled set, and
   a kept array. Real window (NativeAOT, (b) and (c) together, UI KB a wheel notch): Junction Commit unified 16.7 → 14.7
   (−11.6 %), Vesper reader 15.3 → 12.9 (−15.5 %), compare −10.8 %, History −5.3 %; headless (b) alone −4…−6 % (r18-4: a
   Junction Commit Down −4.3 %), (c) alone −2.8…−5.4 %. Each commit its own numbers; (b) and (c) together clear the
   churn bar. Probe `r18-layout\probe-r18.patch`. Small; no API (a pooled set needs no new Element field).
5. **a11y-uia-disconnect-off-ui-thread** (7; r18-7 Candidate; closes Waiting a11y-client-frame-share) — with a real UIA
   client each `UiaDisconnectProvider` costs 0.56–0.73 ms on the UI thread (the first of a session 3.5–4.0 ms; headless
   never shows it), whatever the client holds. Run the disconnects (and `Detach`/`Release`) for the providers an apply
   lets go on a worker after the frame, drained on `Dispose` before `UiaReturnRawElementProvider(hwnd, 0, 0, 0)`; the
   tests' `ProviderDisconnected` needs a flush. Prototype `ZZ_DISC_ASYNC=1` (`r18-a11y\src18\`): Junction's palette
   close (13–14 disconnects) focus-only client 8.40 → 1.33 ms wall, reader-like 11.12 → 1.16; the first palette Down
   3.48 → 0.12 ms; Commit open −7…−16 %; the client logs show the same events. Aperture's Enter disconnects 19 (~10–14
   ms, extrapolated). The time bar: timed runs with a client asking (r18-7's FlaUI client, `--mode reader`) and step-ab
   `-Filter a11y/`. r18-7's frame-log trap: measure around `RenderFrame` inside `Application`, or by `FrameLog`.
   Small–medium; no API, no pixels.
6. **apps-aperture-artwork-retained** (4 with 2; r18-8 Candidate 1, r18-2's PosterCache lead, Waiting
   aperture-poster-as-hero's cost) — `PosterCache` is bounded by entries (400, PosterCache.cs:80), not bytes, and keeps
   each picture decoded (`SKBitmap`) and as a GPU texture. Real Jellyfin, after 6 item pages: 47 entries, 65.1 MB
   decoded (3 heroes at 2176×1224, 10.2 MB each; a 2000×3000 poster used as a hero, 22.9 MB), private 147 → 331 MB (313
   after a full GC, heap 5.1 MB), working set 144 → 252, DXGI local 81 → 140 MB; O(pages visited). A byte budget for big
   pictures (keep the last 2–3 heroes), and expert 2's answer whether the CPU bitmap can go once uploaded. Measure on
   the real server as the named user (r18-8's `aotp` with `apcache`); only this step's agent signs in. Small–medium;
   app; no API; an evicted hero loads again.
7. **reactivity-aperture-parts-build-on-change** (6; r18-6 Candidate 2) — Aperture's title bar and rail build on every
   `Rerender`, `MarkDirty` and build not marked view-only (17.6 + 31.3 = 49 KB each; Enter 2 + 2 part builds, a library
   first open 4 + 4). Build them only when what they read changes (PROBE_PARTSHAPE: Enter 167.4 → 100.7 KB, −40 %;
   library first open 784.6 → 620.6, −21 %; Forward 78.6 → 61.1, −22 %; part builds Enter 4 → 1), then per-button rail
   parts lit through a `Computed` as 3ea96588 (Enter about −56 %, Forward about −58 %, not prototyped). The prototype
   failed 13 of 120 PartTests and host tests (the oracle's forced full build goes through `MarkDirty`; 2 showed a stale
   rail after sign-out): keep the oracle green. Small–medium; app; no API.
8. **reactivity-aperture-poster-lands-in-place** (6; r18-6 Candidate 3 with r18-8's
   apps-aperture-artwork-landing-rebuilds-view; measure first) — each picture landing calls `RerenderView()`
   (ApertureApp.Cards.cs:339–344) and makes the cards again: a library first open with posters 1,263.0 KB, 12 builds,
   1,039 elements, against 787.6 KB and 4 builds with 404s (real window 1,237.6–1,638.0 KB, 14–21 builds); a failed
   fetch still costs a 32 KB build; an item page open has 6–8 whole-page builds of 92–107 KB (r18-8). Put a landed
   picture into the live cards that show it (as `RecolourKeptRows` does for colours): expected −45 % a first open,
   −45…−50 % a new row. Count data landings against picture landings first (r18-8). `ImageElement.Source` marks layout
   dirty: check the re-measure. Suite proposal (k) as its guard if it can be built. After step 7. Medium; app; no API;
   pixels equal once landed.
9. **apps-aperture-home-shelf-turn-builds-home** (6; r18-8 Candidate 3) — a Down or Up onto a shelf showing another page
   turns that shelf and builds all of Home (ApertureApp.Cursor.cs:243–245, Home.cs:140): real window 128–133 KB and
   2.25–3 frames a Down after walking Right past the first page (a Right within a page 0.7 KB). Each shelf's page as its
   own part: about −75 % a Down. Suite proposal (j) as its guard. After step 7. Small–medium; app; no API.
10. **dotnet-junction-collect-after-big-walk** (4; r18-4 Candidate) — b1423e6e's one aggressive collection at rest runs
    only after a walk whose texts were kept (`HighlightLanded`, JunctionApp.Highlight.cs:568, through
    `KeepHighlightTexts`), so after a big walk that finished nothing collects: real window bigrepo, after the Commit
    open committed 33.5 MB, working set 134.5; after 3 first visits committed 41.0, working set 144.7, private 155; a
    collection brings the working set to ~120 (−11…−17 %), committed 13.6. Also set `_collectWhenIdle` when a landed
    walk was big (≥ ~2,000 lines or ≥ ~32 MB allocated for it); jrepo's walks keep 0 collections. r18-3: the collection
    costs at most one late frame (a 6–8 ms pause). Extend `HighlightResumeTests`' idle-collection test. Small; app; no
    API.
11. **painting-numericupdown-paint-bounds** (2; r18-2 Candidate 2) + **painting-drag-ghost-layer-bounds** ((user),
    question 73; r18-2 Candidate 1) — separate commits. (a) `NumericUpDownElement.PaintBounds` returns null
    (NumericUpDownElement.cs:342); Vesper's Multi-rename counter row (three of them at `Opacity(0.45f)` by default,
    CommanderApp.Rename.cs:140) draws a layer of the card's clip every frame (1.198 → 0.041 Mpx): real window GL, first
    open Skia +9.13 → +0.50 MB, DXGI local +9.1 → +0.5 (at the end 48.2 → 39.6, −18 %), first-open private +26.4/+27.0 →
    +1.7/+2.4; pixels byte-identical. The border box ∪ the centred value line, grown by 1. (b) The in-window drag ghost
    (`Window.cs:4083`, `:4094`) is an unbounded layer: Junction's repository reorder, real window GL first drag Skia
    +18.25 → +0.50 MB, DXGI local at the end 64.9 → 47.1 (−27 %); 23 of 29 drag frames differ in 2–5 pixels by up to
    6/255 — only on question 73's (b). Probe `r18-painting\probe-r18.patch`. Small; no API.
12. **vk-swapchain-two-images** ((user), question 74; 3; r18-3 Candidate) — on Windows, `minImageCount` images (2 here)
    instead of `minImageCount + 1` (SdlVulkanHost.cs:835): DXGI local Reel SDR playing at 1920×1140 −8.4 MB (−3.1 %),
    maximized-sized −29.9 (−7.8 %); Aperture −16.8…−18.4 %; HDR (F16) −33.8 (−8.3 %) and −179.9 MB (−21.3 %, one pair);
    playback and fades 0 acquire waits over 1 ms; modal-loop drags while a video plays wait up to one refresh in ~40 %
    of frames (3 images: 2 of 57). Build as question 74 says, with `VulkanProbeTests` at 2 images under a validation
    layer proven loaded; keep `+1` off Windows. vk-instance-extensions-once may ride along as its own commit. Small
    code, medium risk.
13. **text-bidi-wrap-ranges** (5; r18-5 Candidate 2) — right-to-left paragraphs have no wrap range, so a drag step lays
    out every one of them again: Vesper's reader on the real README.ar.md, 477 BiDi layouts a step, 98.4–99.6 % with the
    same line starts as before, 90–92 % of the drag's UI bytes (ar 83.4 → ~9 KB a step at 1,440 and 120.7 → ~11 at 960,
    an upper bound; he 82–86 %); real window 97.3 % the same breaks. `ComputeWrappedBidi` tracks the widths its break
    decisions hold for; an equivalence run on realar, realhe and emoji as round 15's. Rare for this user; the scaling
    bar (O(paragraphs) a step). Medium; no API, no pixels.
14. **junction-diff-row-cells-one-label** (6; Waiting, r17b4's lead, r18-6's re-rank; measure first) — a diff row is
    8–10 elements; drawing its number and sign cells as one label each drops 3 elements, ~1.8 KB a made row: by
    arithmetic a first visit −11…−12 %, a revisit −16 %, a row scrolled in −19…−24 %. Measure first; the number columns'
    pixels must stay equal. After step 4. Small; app.

### Waiting

Re-ranked by the next survey.

- **live-reader check of menus** (user) — the six-step script in ACCESSIBILITY-PLAN.md (round 7 step 13) for Narrator,
  NVDA and Orca; a silent move would mean moving focus to the selected item (needs the user's approval).
  Round 16 (r16-7): settled for Narrator and NVDA by the screen-reader fix (`fixed4`: menu bar, context menu, a one-level submenu right in both); still unchecked: the dropdown step (A7), submenus deeper than one level (e28d72f6), light-dismiss (2603955a), Orca.
  Round 18 (r18-7): nothing changed (no menu or Windows-bridge commit since round 17).
- **junction-changes-leads** (6/7/8, reader behaviour, no perf number) — only the caret is told to a reader, not a
  multi-selection's other members; a reader's "select" on a folder row clicks it, which opens or closes the folder. (The
  perf parts were examined by round 8: below the bars.) Round 13: still so (round 12 touched no selection pattern).
  Round 16 (r16-6, r16-7): unchanged; reader behaviour with no perf number, outside plan 17 (surveys stop re-ranking it).
- **junction-review-old-path** (app bug, not perf; round 7 step 8a) — the review composer sends GitLab `old_path` = the
  new path for a renamed file, because `GitFileDiff.OldPath` is never set (JunctionApp.Changes.cs:122, 124;
  GitRepository.cs:496, 511; `RenamedFrom` holds git's old path). Decide whether review positions on renamed files
  should carry the old path. Reproduces in round 13.
  Round 16 (r16-6): unchanged (nothing in 75f2288a..3038f82b touches `OldPath`); an app bug outside plan 17 (surveys stop re-ranking it).
- **dropdown-follow-control-on-resize** (1, correctness, low) — a Dropdown list or DatePicker calendar stays at its
  opening point when a resize moves its control; anchoring changes how a press on the control behaves. Not perf.
  Round 16 (r16-1): partly settled by 2603955a (a press on the custom frame's edge closes light-dismiss popups), but `FramePressedPending` is read only after the size loop, so the list still floats during an edge drag; keyboard snaps and app-made resizes keep it open.
  Round 18 (r18-1): unchanged (no commit touches DropdownElement, DatePickerElement or `FramePressedPending`).
- **junction-leads (remaining)** (tests) — `SelectConflict` (JunctionApp.Changes.cs:1477) and a conflict row's
  right-click have no test of their own (not perf; still so in round 13).
  Round 16 (r16-6): unchanged; tests, outside plan 17 (surveys stop re-ranking it).
- **vesper-brief-filter-clear-reveal** (1, not perf; round 9 step 10) — clearing a filter with Backspace leaves Brief's
  cursor column out of view (the scroll is clamped to the narrower filtered layout; the sideways reveal has no
  apply-after-layout form like the vertical one). The scrollbar half was fixed by round 10 step 15 (`BriefScrolled`,
  CommanderApp.Views.cs:616–625; r13-6).
  Round 16 (r16-1): `OnLayoutChanged` (7c0a3b79) on Brief's grid now gives the sideways reveal the after-layout form it lacked; small, not perf.
  Round 18 (r18-1): unchanged (CommanderApp.Views.cs untouched).
- **findbar-countonscreen-foreach** (4, nit) — `CountOnScreen` loops `foreach` over `bar.Children` (FindBar.cs:212; 32 B
  on AOT, 0.4 % of an 8 KB Enter), and `RewriteCount` (:317–319) calls it once a switch with the bar open; fix when
  FindBar is next touched.
  Round 18 (r18-4): still a nit; FindBar untouched.
- **textarea-chunked-document** (5/4; the user chose option B on 2026-10-25) — parked: Meridian's 89k body typing is
  ~238–241 KB/key, most of it the TextArea's new string; the user's 256k-character condition is unmet (round 13: still).
  Round 18 (r18-4, r18-5): parked; no TextArea change.
- **a11y-served-items-followups** — a live Orca check only (no desktop Orca here; optional).
  Round 17 (r17-7): add step 7's late-attach check.
  Round 18 (r18-7): unchanged.
- **gl-threaded-optimization-profile** (3; the user's driver setting, information only) — the walk lands while the
  window rests; the setting would remove only the per-frame spin (round 9: mean 357 JIT / 344 AOT, max 456/578
  `SwitchToThread` a paced frame; round 10: mean 378/376, max 723/644).
  Round 18 (r18-3): unchanged, not measured again.
- **text-open-measures-every-line (full)** (5; user: not now, 2026-10-13). Round 13 (r13-5), stronger on real CJK text:
  a 1 px drag step re-breaks every wrapped row of the document (Japanese at 1440: 1,040 prefix tables, 119,899
  characters itemized and 84,495 fallback questions a frame; real window at 960 logical: 2,618 tables, 166,806
  characters) with ~40 rows on screen; step 3 removes the shaping and bytes, not this O(document) itemizing.
  Round 14 (r14-5): asked again as question 57 (zh at 1440 1,140 tables and 131k characters itemized a drag frame;
  the 154k-character file 2,178–5,288 tables and 9,900–13,550 HarfBuzz calls a frame; Arabic 1,846 BiDi probes a frame).
  Round 18 (r18-5): unchanged (Vesper zh open 211,248 characters itemized, 3,059 tables); the right-to-left part is step
  13.
- **controls-menu-max-height-scroll** (1; not perf: no maximum height or scrolling). Round 17 (r17-1): unchanged.
  (layout-virtuallist-spacer-stale closed by dd3ad712: no parent's measure reads the spacer any more.)
  Round 18 (r18-1): unchanged.
- **aperture-poster-as-hero**, **aperture-tmdb-size-fallback**, **junction-narrow-gitlab-glyphs**,
  **meridian-pane-choice-not-persisted**, **meridian-min-width-wraps** (apps; not perf). Round 13 (r13-8):
  meridian-pane-choice-not-persisted reproduces (MeridianApp.cs:148, flipped at :340, never saved); the min-width wrap
  at 760×540 stands on Windows (46fccd88 keeps 448 / 1320); the others' code is unchanged.
  Round 18 (r18-8): aperture-poster-as-hero now has a cost (a 2000×3000 poster as a hero is 22.9 MB decoded plus 22.9 MB
  GPU) → step 6; aperture-tmdb-size-fallback's code unchanged, not exercised; junction-narrow-gitlab-glyphs unchanged;
  both Meridian items still reproduce.
- **syntax-shared-registry-rest** (5; parked) — Typst, Julia, PHP, Razor and HTML still use the shared registry (round
  11: `.typ` +42,419 KB kept); the user (question 42): no Typst files; parked.
  Round 18 (r18-5): parked, unchanged.
- **fedora-local-dotnet** (environment) — once Fedora ships the final `dotnet-sdk-11.0`, remove
  `/usr/local/share/dotnet` and both links, `/usr/local/bin/dotnet` and `/usr/local/sbin/dotnet` (`which` picks the sbin
  one). Round 13 (r13-4): Fedora 44's updates have only 11.0.100~rc.3; `wsl-test.sh` follows the dotnet on PATH.
  Round 14 (r14-4): `updates` and `updates-testing` still have only 11.0.100~rc.3; both links still in place.
  Round 17 (r17-4): on 2026-11-19 `updates-testing` has the final 11.0.100-1.fc44, `updates` still rc.3; the installed rpm is dotnet-sdk-10.0; both links still in place. The user could take it from updates-testing now.
  Round 18 (r18-4): the same on 2026-11-22 (`updates` rc.3, `updates-testing` the final 11.0.100-1.fc44; the installed
  rpm dotnet-sdk-10.0; both links in place).
- **f16-held-drag-post-stall** (3; one survey stall, round 11 step 13's approved re-run clean) — cause unknown. Round 13
  (r13-3): the only F16 drag left is during HDR playback, where mpv posts every frame (the stall's precondition); a run
  needs an HDR clip playing on the 1440p at 200 Hz and a new approval from the user.
  Round 18 (r18-3): not run; it is now the gate for two images on F16 (question 74 (c)).
- **Suite scenarios** (8) — (a) an idle Vulkan window counting every thread's wakes: keep, low, only as a gated UiTests
  guard of round 12 step 5 (116 against 212 a second). (b) frames per key under a real loop and (c) an app-shaped shell
  resize: closed in round 13 (DiffFrameCountTests covers (b); (c) went with a11y-structural-walk-during-resize).
  Round 14 (r14-8) proposes: (d) a 75,000-character Chinese wrap resize, 1 px a frame, on real gettext prose at 960
  logical px past the word cache (guards 698c2758); (e) a11y insert above 1,000 item rows with a UIA client listening
  (UiaRig); (f) a gated UiTests guard for 4fdb56cb (a Vulkan window's DXGI local memory back within 2 s of 18 size
  changes).
  Round 15 (r15-8): make (d) fractional (1 physical px at 150 %, where step 1's objection shows) and add the
  154k-character realbig file for the past-the-cache case; (e) stands (a commit over History −52 % UI bytes with
  a11y off); (f) low.
  Round 16 (r16-8, r16-3, r16-4): (d) low; (e) medium, as step 3's guard; (f) low; new (g) a gated UiTests guard that a wrapped-row list resized from posted work draws 1 frame a step (with step 2), (h) an AsyncLoader per selection that allocates ~30 MB and is cancelled at 400 ms, all threads and gen counts on a real clock (with step 9), (i) a gated guard of DXGI local after the first `Dialog.Show` on a 2160×1350 GL window (with step 5).
  Round 17 (r17-8): (e), (g) and (i) closed (the closed note); (h) medium, as step 2's guard; (a), (d) and (f) low.
  Round 18 (r18-8): (h) closed (`HighlightResumeTests`' idle-collection test is b1423e6e's guard); (a), (d) and (f) low
  ((f): the sign-in page's DXGI local unchanged at 68.98 MB); new (j) an Aperture Home with 3 shelves of 30 cards,
  walked Right past the first page, then Down (step 9's guard); (k) an Aperture item page with N pictures landing one a
  frame, counting view builds a landing (step 8's guard).
- **findbar-count-not-announced** (7, reader behaviour, not perf) — a find bar's new "n of m" reaches the bridge as a
  Name change on the count label; a reader speaks it only if it moves there (no `Announce`, no live regions).
  Round 18 (r18-7): unchanged.
- **text-cjk-row-start-suffix** (5, lead; r13-5) — after step 3 the largest HarfBuzz source: a row-start word read off
  the line's word (Japanese 300 of 1,051 calls at 1000 px, 216 of 453 a drag; Chinese 201 of 1,165, 77 of 403). Needs
  the slice's own script guess and safe cuts at both ends. Medium–large; up to ~20–30 % of a new width's calls.
  Round 14 (r14-5): after step 1 (a) row-start segments are 88 of 951 calls (zh) and 118 of 681 (ja) at 1200;
  short words (under 8 characters) cut at need are the largest share (461 of 951); giving 2–7 character words their cuts
  in the tables halves a new width's calls but costs opening +248 KB (zh) / +121 KB (ja): a trade.
  Round 16 (r16-5): lower; the wrap ranges answer 88–94 % of rows (Vesper zh drag 39 calls a step at 1,440, 75 at 960; a new width 825).
  Round 18 (r18-5): low, unchanged (Vesper zh drag 39 / 75 HarfBuzz calls a step at 1,440 / 960).
- **junction-summary-overtaken-at-start** (6, lead; r14-6 REVIEW) — AsyncLoader drops a superseded result, so a
  working-tree callback during the first full summary restarts it: with a log appended every 0.4–1 s the first summary
  lands at 0.8–1.3 s (1.3–2.1 s on 2 cores) after 2–4 dropped loads, its remote/for-each-ref/rev-parse/describe run 3–4
  times; on a slower machine it can be overtaken as long as callbacks keep coming (round 13's CI failure). Coalescing
  measured worse (Examined).
  Round 18 (r18-6): unchanged (3490ba67 adds an index print to the shared status only).
- **vesper-local-waiting-text** (6, wording, optional; round 14 step 6, 1b019826) — a slow local folder's waiting block
  reads "Connecting to C:" (the share/server text); it could read "Loading <folder>" for a local folder, which also
  changes what the Ctrl+Shift+N review state shows and the test that checks it. The user's call; not perf.
  Round 18 (r18-6): unchanged.
- **vk-instance-extensions-once** (3; fix when Vulkan Init is next touched) — `HasInstanceExtension`
  (SdlVulkanHost.cs:532) enumerates on every call (8 `vkEnumerateInstanceExtensionProperties` calls at Init): one shared
  list saves 7.1–7.4 ms of start-up, below the 10 ms bar.
  Round 18 (r18-3): unchanged; may ride with step 12 as its own commit.
- **a11y-item-renames-unfetched** (7; question 64, with question 50's live check; r15-7 Candidate 2) — with UIA, an
  item node that has no wrapper yet raises no Name change (today `RaisePropertyChanges` → `TryWrapper` →
  `TryMakeItemWrapper` makes a wrapper and a native object only to raise): a Vesper navigation between two 1,000-file
  folders makes 1,003 Name raises and 13,052 provider calls every navigation (the first also keeps 1,000 wrappers and
  1,003 native objects, 372.4 KB); a Junction ref move with History shown 1,003 raises, 13,184 calls, 453.8 KB of
  1,426.8 UI KB (the old apps-junction-ref-move-a11y). UIA only: AT-SPI publishes every item. The renaming walk stays.
  Round 16 (r16-7): low once step 3 lands (at most 200 renames an event on Windows: Vesper 150 ↔ 250 files 154 raises, 2,028 calls, 42–65 KB a navigation); Linux needs Orca (AT-SPI clients cache every item).
  Round 17 (r17-7): low; UIA renames are bounded at 200 (a 150-item replace makes 150 raises, ~1,950 calls with a client); AT-SPI eager lists up to 2,000 (1,000 name signals a replace) need Orca.
  Round 18 (r18-7): low, unchanged.
- **junction-colours-before-rows-route-a** (6; question 70's (a), not built) — after step 4 (b3bb93bd) a first file visit
  in Junction's Commit view still costs 2 builds and 2 frames, 492–570 KB against a revisit's 362–393 KB; holding the
  first frame until short colours land would make it a revisit (−25…−32 %, 1 frame) with the diff shown later by the
  tokenize's time. Re-ask only if a survey shows the difference matters to a user.
  Round 18 (r18-6): no new number; not re-asked.
- **junction-files-first-open** (6, lead; r17-8, r17-6) — Junction's Files view first open: 3,806 KB, 3 frames, 3 builds
  (its loading states), once a session.
  Round 18 (r18-8): unchanged, once a session (3,841 KB, 5 frames).
- **aperture-signin-user-message** (app bug, not perf; r17-8) — with an API key on a server with several users,
  `ApertureFailure.UserMessage` drops the "name the user to browse as" detail the sign-in returns (question 71).
  Round 18 (r18-8): still reproduces (ApertureResult.cs:46: NotFound reads "That is no longer on <host>").
- **aperture-item-page-first-build** (6, lead; r18-8) — after 0f9c8477 an item page's first build is 244 → 278–294 KB
  (+14–20 %), a whole page open 1,444 → 1,535–1,706 KB (one base sample); likely the `Pressable` handlers and the
  arrival focus. Low; re-measure after steps 7 and 8.
- **painting-controls-paint-bounds** (2; r18-2, round 17 step 6's optional part) — Dropdown, Checkbox, RadioButton,
  ToggleSwitch, ProgressBar, Spinner, the pickers, Calendar and Expander's part still report no paint bounds, so a faded
  ancestor of one is window-sized; no app fades one (r18-2 checked every `Opacity` in `apps/*`; the NumericUpDown case
  is step 11 (a)). A candidate only when an app does.
- **meridian-runnerview-build-count-flake** (tests; r18b4; fix before round 18's full suite) — Meridian's RunnerView
  tests fail "N builds of the results for 15 results" in full-suite runs only: 4 of 20 runs at 56215ccc; with archived
  trees step 4's code 1 of 6 and the base 2 of 6; they pass alone. Results that land within one main-thread drain get
  one build, so the count is a timing race in the test, not a change of this round. CI may hit it. A fix agent makes the
  test deterministic (a gate or a count that does not depend on the drain) before the full suite.
- **skia-rest-purge-vulkan** (2 or 3, lead; r18b6) — Skia keeps the textures of every live picture up to its 256 MB
  budget: at Aperture's Home after 20 item pages 65 MB of textures of live pictures, 60 MB of them off screen, and 12 MB
  of upload staging; a rest-time `PurgeUnusedResources` on Vulkan would return about 70 MB of GPU memory and private.
  Framework-wide; needs its own real-window holds (frames after a rest that re-upload, GL and Vulkan). For the next
  survey.
- **aperture-postercache-slot-stall** (8 or 4, lead; r18b8) — `PosterCache` has 8 download slots, so 8 slow downloads
  stall every picture behind them (seen in step 8's real-window runs). Measure on the real server how often a slow fetch
  holds the queue and what a timeout or a separate hero lane would change. For the next survey.
- **reconcile-keyed-match-alloc** (6, framework lead; r18b8) — matching children by key allocates a Dictionary and a
  List (~0.3 KB) per patched container (seen in step 8 when a data rebuild matches waiting picture boxes by key). A
  pooled or small-count path could take it off every keyed patch. For the next survey.
- **release-merge-main-shelf-arrows** (release, fix at round 18's merge; r18b9) — main is 9 commits ahead (framework
  ContextMenu d9bf8e25, Button/Checkbox mnemonics, Dropdown/DatePicker, TableView sort, Application light/dark, a
  long-range scrollbar; Aperture 133bcfe6, merged 936b5520: Up and Down move shelf to shelf with no page turn and the
  cursor's card scrolls into view). Step 9 conflicts at the end of `MoveRingWithoutBuild`: resolve to `if (turned...)
  ... else FocusCursorCard(laidOut: true);` (`AfterHomeShelfBuild` already calls `FocusCursorCard()`, which on main also
  scrolls the card into view after the frame). Rewrite `HomeShelfTurnTests`' Down/Up steps and guard (j)'s Down for
  main's arrows (the turn is then Right/Left past a page end and Next/Previous). Check step 4's context-menu-after-patch
  against d9bf8e25. Run the full suite on the merged tree.
- **part-place-stale-double-build** (6, framework lead; r18b9) — `Part.Place(stale: true)` on a part with no host builds
  it twice (once in `Mount()`, again for the stale flag); Aperture's title bar and rail likely do this at start-up. For
  the next survey.
- **aperture-poster-card-cost** (6 or 8, lead; r18b9) — after step 9 a Home shelf turn is mostly the 7 poster cards it
  makes, about 5.6 KB each (headless ~50.6 KB a turn). A lighter card or cards kept across pages could take most of it.
  For the next survey.

Closed from Waiting by the round 18 survey (→ Examined unless named): text-wrap-ranges-per-width (r18-5: on top of round
17's steps 1 and 8 the 4-range memo moves 33.0/23.7 → 33.1/23.0 KB a step), vk-drag-steps-dropped (r18-3: a timer step
outside the modal loop rebuilds the swapchain every step, a user's drag 1–2 times a drag; timer steps also drop on GL
while a video plays; r18-8's ~30–45 updates a second in a user's drag is inferred, not measured),
window-setroot-component-not-mounted (r18-6: not perf; no root component overrides `OnMount`; a framework gap outside
plan 17), a11y-client-frame-share (→ step 5), elements-selectable-label-lazy-wiring (→ step 4 (b), static wiring
instead), junction-diff-row-cells-one-label (→ step 14), vk-swapchain-two-images (→ step 12),
vesper-file-not-its-own-partner (→ step 1), and Suite scenarios (h) (b1423e6e's test is the guard).

## Landed

Round 17 (the round round 18's survey reviews; it moves to the archive at round 19's survey). Rounds 1–16 are in
[the archive](17-performance-team-ledger-archive.md#landed-rounds-14) ([round 5](17-performance-team-ledger-archive.md#landed-round-5),
[round 6](17-performance-team-ledger-archive.md#landed-round-6), [round 7](17-performance-team-ledger-archive.md#landed-round-7),
[round 8](17-performance-team-ledger-archive.md#landed-round-8), [round 9](17-performance-team-ledger-archive.md#landed-round-9),
[round 10](17-performance-team-ledger-archive.md#landed-round-10), [round 11](17-performance-team-ledger-archive.md#landed-round-11),
[round 12](17-performance-team-ledger-archive.md#landed-round-12), [round 13](17-performance-team-ledger-archive.md#landed-round-13),
[round 14](17-performance-team-ledger-archive.md#landed-round-14), [round 15](17-performance-team-ledger-archive.md#landed-round-15),
[round 16](17-performance-team-ledger-archive.md#landed-round-16)).

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 17 | 8dbae799, dd3ad712, 4c890435 | perfsuite-columns-walk-focus (harness) + layout-virtuallist-measure-through-rows | (a) `ColumnsWalk` focuses its list: `virtual/columns cursor walk 100k item rows` renders 26.8 cells, 261.5 KB a step, 0 unrendered steps; a full suite run exits 0 again (`--quick`, all 248 scenarios, and step-ab's full JIT and AOT runs). (b) An internal `ScrollViewElement.MeasuredAsLeaf` (true for VirtualListElement), read by `MeasureSize` and the baseline probe: an ancestor measures a virtual list as a leaf, not through its realized rows, so no visible row is laid out at a width it is never drawn at. Headless (1440×900, 40 steps of 6 px, old and new in one process, alternated ×2), KB a step: resize Commit unwrapped 86.0/67.3 → 31.4/28.2 (asks 429 → 244), Changes unwrapped 77.7/71.1 → 29.5, Changes unwrapped split 145.6/142.3 → 66.5 (asks 835 → 358), Commit wrapped 103.7/95.5 → 63.3 (breaks 123.5 → 87.4), Changes wrapped split 270.7/265.1 → 195.4; splitter steps Commit unwrapped 33.5/28.5 → 13.9, Changes wrapped split 250.5/241.7 → 142.1; expert 5's a359e426 split 80.2/71.5 → 24.0 (re-breaks 3,919 → 1,396 per 40 steps, the rows' own 2,538 → 28); History, Vesper's panes, reader and compare view equal; 0 builds. Real window (NativeAOT, the 4K at 150 %, 2 holds, 0 foreground takes, 0 nvlddmkm; timer steps on the UI thread): Commit unwrapped resize 70.5–81.9 → 23.2–37.5 KB, 12.8–13.4 → 8.1–9.3 Mcycles, asks 497 → 317; Changes unwrapped split 143–145 → 60–73 KB, asks 880 → 417; Changes wrapped split splitter 233–236 → 126 KB (16 scenes in dd3ad712's body). Audit: 19 sites and the samples (CommandPalette, Vesper's Find and Rename set their height; TableView, InspectorView and the 16 app lists grow in columns whose siblings don't shrink): none needs the old content width; the layout audit over 9 suites, 4.75 M boxes, differs only in the new test and A11yUndrawnFrameTests (a 1 px counter no longer squeezed to 0.77 px by the list's rows: a fix). Pixels: boxes, scroll offsets and frame hashes equal the old rule's in 240 of 240 cases (1.0 and 1.5, wrap on and off, unified and split, 5 views, 3 widths, top and scrolled, caches emptied); goldens pass. step-ab (8dbae799) `-Pairs 2`: JIT 0 slower both pairs (pair 2 one faster, the partner walk −10 %), AOT 0/0 both pairs, A/A 0/0; the partner walk 36.2 → 33.6 KB allocKB on both runtimes. `VirtualListMeasuredAsLeafTests` (2) fail under a reverse edit; Windows `HultUI.slnx` 30 projects, 14,429 passed; Fedora the slnf plus Junction, Meridian, Aperture and Reel each in a private D-Bus session, 26 projects, 13,974 passed. LAYOUT.md, ELEMENTS.md (size a virtual list from outside: FlexGrow or a height, plus stretch or a width). After (b), expert 5's jcommit host: C# 574f43a7 re-breaks 1,129 → 185/142 (far misses 1,062 → 26), 71.8/79.7 → 42.2/39.0 KB (their 4-range memo reached 46.0); a359e426 split 4,394 → 1,579 (far 3,658 → 1,094), 99.0/85.2 → 49.1/34.9 KB (memo 52.9/38.3); 1,368 of the 1,396 re-breaks left are the heights pass's (step 8). |
| 17 | 34606bab, b1423e6e, b185a60c | apps-junction-changes-recolour-same-texts + dotnet-junction-kept-texts-working-set (the review objection to 80e94eeb) | (a) A working-tree event whose new generation reads the same texts keeps the diff's colours (a 64-bit hash of each text with the Working/Staged highlights) and carries the kept texts: headless bigrepo, 2 alternated pairs, an untracked file or another file's save with a big modified file on screen 288.7 → 9.4 MB all threads, 13,639 → 0 lines walked, the diff uncoloured 475–2,921 ms → never; git processes (8/9) and file reads (1) unchanged; without the Changes view's own load the event's colour bytes 284.6 → 5.9 MB (−98 %); a small file 42 → 0 lines; a real text change still recolours, spans equal a fresh walk's. Real window (AOT) 266 → 9.4 MB an event, uncoloured 2.8 s → 0 frames. (b) Once a walk that kept texts comes to rest, one `GC.Collect(2, GCCollectionMode.Aggressive)` 2 s later on the UI thread gives the memory back (pauses 7.6–12.0 ms, no frame or key in that window). Options in the real window (NativeAOT, bigrepo, expert 8's jbig scene, 2 holds alternated), end-of-scene ws │ after a forced full GC ws/committed/private │ revisit pass MB, gen-2s: kept (80e94eeb) 202.6 │ 181.6/71.3/192.4 │ 368.5, 0; none (f76cb440) 155.7 │ 146.2/35.8/154.8 │ 375.1, 4; **kept + one aggressive GC at rest 134.1 │ 134.7/24.4/144.6 │ 368.1, 0 (kept)**; a compacting gen-2 at rest 180.7 │ 181.3/71.2/191.2 (frees the heap, keeps committed); `GCConserveMemory=5` 175.6 │ 157.3/46.8/167.8 │ 367.6, 2; a 6 MB bound 162.0 │ 149.7/39.3/159.8 │ 374.0, 3; dropping the texts at rest 187.8 │ 170.6/60.2/181.8; UTF-8 texts 183.9 │ 155.6/45.1/165.3 │ 397.0, 2 (1 run). The committed code, hold 3: 134.0 MB at scene end (kept 202.5, none 154.6); after a second 12-key pass and its collection 141.7 (186.6, 163.6), that pass 384.0 MB with 0 gen-2s (none 392.6, 4). Pixels: settled coloured frames after two events byte-identical to a fresh load's. step-ab (66f46f11) `-Pairs 2`, all runs idle-gated: JIT 0/0 both pairs (pair 2 the rich-text a11y rows' allocKB 3.3 → 2.4, the A/A the other way), AOT 0/0 both pairs, A/A 0/0. `HighlightGenerationTests` (3) and an idle-collection test in `HighlightResumeTests` fail under every reverse edit; Windows Junction 1,171, Syntax 137, Headless 1,794; Fedora Junction 1,171 (private D-Bus). Real window 3 holds, 24 launches, 0 foreground takes, 0 nvlddmkm, the 4K at 150 %, 120 Hz. Suite proposal (h) not built (the suite runs no Junction; the Junction test is the guard). DECISIONS.md (the options table), NATIVE-AOT.md (only an aggressive collection gives committed memory back). |
| 17 | a787c194, 23dd0de4 | syntax-diff-skip-shared-stretches | `TokenizeDiff`'s old-version walk takes a line's end state and spans from the new version's walk when the line has the same text and starts from an equal state (TextMateSharp's `StateStack.Equals` ignores whether a rule's begin matched to the end of its line, which changes how `\G` matches next: the take also compares that, through an `UnsafeAccessor` to an internal member that a test pins); 9 B a line counted in Junction's kept-text `Bytes`. Equality: 0 lines differ, spans bit for bit against e77f210d and against each version's whole `TokenizeLines`, fresh and cancelled every 20 ms and resumed (1,326 cancels), at -U3 and -U12, on 379 pairs (the 9 survey pairs, 252 files of 1,000+ lines from the last 200 commits, 118 XML/YAML/PowerShell/shell); lines walked over the 200-commit corpus 916,266 → 621,679 (−32 %); bigrepo file00 13,626 → 7,453, 279.8 → 152.9 MB. Headless JIT (bigrepo, 2 idle-gated runs a side alternated): Commit open 297 → 171 MB all threads, GCs 18/2/1 → 10/2/1, colours at 3,042/3,045 → 1,812/1,799 ms; a stop on an unvisited file 300 → 172–174 MB, GCs 18/1/0 → 11/0/0, colours 2,783/2,833 → 1,635/1,587 ms; 12 keys at 400 ms unchanged (~31 MB a key); step 2's idle collection fires once on both sides. Real window (NativeAOT, 1 hold, base/new/base/new, 0 foreground takes, 0 nvlddmkm; **the 1440p only, 100 %, 200 Hz — the 4K was not attached**): Commit open 293.7 → 168.5 MB, GCs 20/4/2 → 11/2/1, colours 2,920/2,873 → 1,607/1,632 ms; a stop on an unvisited file 297.8 → 172.8 MB, colours 3,222 → 1,845/1,838 ms; the whole scene 1,358/1,370 → 1,138/1,134 MB (−17 %); end of scene after the idle collection ws 129.6 → 130.6 MB, heap 25.5 → 26.4 MB (the 9 B a line counted is 0.34 MB of it; ~13 B a line of distinct state objects the kept walks hold is not counted; five bigrepo pairs still fit the 16 MB bound at 13.92 MB counted). Pixels: 67 of 67 settled coloured frames byte-identical (bigrepo's first 3 files and a repo of 4 real pairs, every wheel position). step-ab (e77f210d) `-Pairs 2`, all 8 runs idle-gated: JIT and AOT 0 slower, 0 faster, no allocKB moved, A/A 0/0. 10 new tests fail under reverse edits (never taking 24, text alone 4, only the rules open 3, no end-of-line check 1, no records across a cancel 4; Junction's bytes test without the 9 B); some walked-line counts updated; Windows Syntax 147, Junction 1,172, Headless 1,794; Fedora Syntax 147, Junction 1,172 (private D-Bus). |
| 17 | b3bb93bd, ef113cef | reactivity-junction-colours-before-rows, route (b) (question 70: the recommendation) | When only a diff's highlight moved, `RecolourKeptRows` sets the new spans on the code labels on screen (each label finds its row and lane through its selection group and index) and `InvalidateRows` is skipped; a label whose spans would change its face (weight, slant, size, family) still has its row made again, and any other shape change still remakes every row; while reviewing, a landing keeps the woven rows as a caret move does (Waiting junction-review-focus-colours settled). Headless JIT, Commit view 1,440×900, alternated ×2: a C# first visit 746.6–824.5 → 492.0–570.3 KB (−31…−34 %), rows made 68 → 34, text measures 499 → 250, 2 builds and 2 frames either way; the landing alone 334.5–347.3 → 79.9–93.7 KB, 0 rows, 0 measures, render phase 272.5–282.8 → 12.6–22.6 KB; wrapped 746.4–945.1 → 519.4–718.9; markdown 763–799 → 507–544; big files 800–848 → 547–606; the first Commit open 1,117.8 → 852.4; Changes' first open 1,792.3 → 1,473.1; a review's landing 436–438 → 216 KB, rows 18 → 0; revisits unchanged (362–393 KB, 1 build, 1 frame). Real window (NativeAOT, 1 hold, 4 launches alternated, the 4K at 150 %, DPI 144, 120 Hz — re-attached during the step; 0 foreground takes, 0 nvlddmkm): first visits 733.1–868.2 → 479.8–613.4 KB (−29…−35 %), rows 68 → 34, render phase 593–718 → 335–461 KB; the first Commit open 1,193.8 → 928.8 / 945.1; Changes' open 1,090.4 → 784.8; revisits 361.4–397.5 unchanged. The ~7.8 KB a remade row is the row template, not paint or the list: 8–10 elements a row (Box 600 B, Text 672 B, a styled Box ~800 B), a selectable code label 1,552 B (six handlers and a menu delegate); `RenderDiffRow` alone 7.4–9.4 KB a row; a keyed row is made in full as a twin, then patched (leads in Waiting). Pixels: 48 of 48 frames byte-identical to the base's (plain and settled, 1.0 and 1.5, wrap on and off, unified and split, 3 stops each); every settled frame equals rows made afresh. step-ab (2b42bda0) `-Pairs 2`: JIT pair 1 0/0, pair 2 8 slower (component and a11y rows), A/A 0/0; AOT pair 1 0/0, pair 2 1 faster, A/A 10 slower; no allocKB moved; the suite's code is identical on both sides (`git diff 2b42bda0 b3bb93bd -- src samples` empty), and a JIT re-run moved other rows (3/6 and 1/0, A/A 2/10): noise. The four landing cases and the review-focus test fail under a reverse edit, the tab-width test under a recolour-whenever-the-highlight-moved mutant; Windows Junction 1,177, Headless 1,794; Fedora Junction 1,177 (private D-Bus), DiffRowKeepTests and ReviewTests 3 more runs of 3. REACTIVITY.md (recolour the rows on screen in place; don't make a row again for a colour). |
| 17 | 3ea96588, 6751a51a, b5ab4704 | reactivity-junction-view-tabs-part + reactivity-vesper-same-folder-down (r17-8's lead; partly) | (a) Each of Junction's seven view tabs is its own part, lit by a `Computed<bool>(() => View == view)`, so a view switch builds the two tabs whose light moved and not the toolbar (the survey's one-part prototype saved 18.4 KB; this form 2.5× that, with no `SetView` bump and no stale tab; a full chrome build +0.4 KB, +0.3 %; the tabs take no keyboard focus, so their element identity, hover kept, is pinned instead). Headless JIT, a fixed fixture, wide 1,440, KB a switch by key: Overview 101.4 → 54.2 (−46.5 %), History 175.3 → 128.1 (−26.9 %), Tags 141.5 → 94.4 (−33.3 %), Branches −20.6 %, Commit 424.1 → 376.9 (−11.1 %), Changes −8.9 %; by click the same −45…−47 KB; compact 1,200 −31…−33 KB (History −23.9 %, Tags −27.5 %); part builds a switch toolbar 1 → 0, tab parts 0 → 2, frames unchanged. Real window (NativeAOT, the 4K at 150 %, 120 Hz, GL; 3 holds, 8 launches alternated, 0 foreground takes, 0 nvlddmkm; repeat launches within 0.1 KB): wide History 174.0 → 127.1, Tags 140.2 → 93.3, Overview 100.6 → 53.7, Commit 421.5 → 374.6; compact (1,185 logical) History 137.2 → 104.4, Tags 118.9 → 86.2. (b) Vesper with both panes on one folder: `PartnerStays` (081921c7, round 8) took the cursor's own file in the other pane for a moving partner, though `DiffPartner` never pairs a file with itself; with the other cursor on a file a key 17.5 → 2.2 KB (−87 %), 1 → 0 builds (headless, 60 files); with the other cursor on ".." or a folder F3 pairs the file with itself, so its ring follows each key and every key still builds (29.5 KB: expert 8's 32.9 KB case) → question 72. Pixels: 56 frames (key and click, wide and compact, 1.0 and 1.5) equal the base's in two alternated runs (a first base run on a fresh fixture differed in Commit/Changes at 1.0 against every later run of either side; the toolbar band matched). step-ab (b6c752e2) `-Pairs 2`: JIT pair 1 2 slower (virtual/rebuild beside 20,000 RowHeights rows, the same 2 as the A/A), pair 2 0 (one a11y rich-text row's allocKB +0.9 KB, that pair alone); AOT 0/0 both pairs and A/A. New tests fail under reverse edits; Windows Junction 1,182, VesperCommander 954 (+4), Headless 1,794; Fedora Junction 1,182 (private D-Bus), the slnf (Vesper 936). |
| 17 | 0a7bb324, c23edfb9, fe46c02b | painting-player-bar-fade-layer (question 69: (b)) | (a) A subtree at opacity 0 is skipped when every element in it only paints (internal `Element.RenderOnlyPaints`; scroll views, text fields, selectable labels, video and web views are always drawn): a hidden player bar's frame renders 24 → 7 elements, window-sized alpha-0 layers 1 → 0; 171 of 171 headless Reel frames byte-identical. (b) `SliderElement` reports paint bounds (its box ∪ the track ∪ the thumb square ±(r+4)) and the player bar's glyphs are a private `CanvasElement` subclass reporting their border box +2, so the bar fades through a layer of its own size: 2.189 → 0.152 Mpx a fade frame at 1920×1140, 7.722 → 0.298 at 3778×2044. Real windows (NativeAOT, the 4K at 150 % primary, the 1440p to its left; 4 holds, 0 foreground takes, 0 nvlddmkm), first-fade DXGI local, first-fade private, end local, end private, Skia's cache at the end: Vulkan `ProceduralBackend` +12.0 → +0.0, +13.5 → +0.1, 67.5 → 55.5 (−18 %), 127.0 → 112.6 (−11 %), 18.26 → 7.26 MB; Vulkan with mpv (bands.y4m) +12.0 → +2.0, +14.4 → +3.9, 284.2 → 274.2, 493.0 → 479.4, 16.33 → 5.33; Vulkan 3778×2044 +32.0 → +2.0, +32.2 → +2.8, 194.4 → 164.4 (−15 %), 165.9 → 135.9 (−18 %), 38.26 → 8.26; GL `ProceduralBackend` +12.0 → +2.0, +27 → +3.4, 49.0 → 39.0 (−20 %), 140.8 → 115.6 (−18 %), 24.98 → 13.98; GL with mpv +24.0 → +2.0, +38 → +4, 271.1 → 249.1, private 458 against 482 and 533; nonlocal and working set unchanged. Pixels: 50 of 171 Reel frames differ, all fade frames, only device pixels (1888,1097) and (1888,1098) on the full-screen icon, by 1/255; goldens pass; Junction 74/74, Vesper 73/73, Meridian 27/27 frames (palette fades included) byte-identical. Only the Slider and the player's glyphs got bounds (the other controls' reach not worked out; no app fades one). Guard: gated UiTests `BarFadeGpuMemoryTests` (GL and Vulkan, 1920×1140, a 1920×78 bar; limit the bar's layer twice + 1 MB = 3 MB): +2.0 MB with the change (Skia's cache 13.71 GL / 5.92 Vulkan), +12.0 under the reverse edit (24.71 / 16.92); a second fade +0.0 everywhere. Aperture's player over `FakeMediaLibrary` headless: the layer is the bar's size, nothing drawn once it has gone. step-ab (e600d2b5) `-Pairs 2`: JIT and AOT `anim/signal tween, 1 kHz posts` −72 % (16.65 → 4.71 ms; its faded bar holds a Slider), `media/video player bar fade` −4.5 %; AOT otherwise clean, A/A clean; JIT `virtual/rebuild 20,000-row` +34…+42 % also in its A/A (noise), `grid/edit auto-column` +14 % one pair only; no allocKB moved. New tests fail under reverse edits; Windows Core 5,212, Headless 1,801, Rendering.Skia 462, Media 93, Media.Backends 18 (+12), Reel 53, Aperture 370 (+21), Fluent 38, the UiTests guard; Fedora the slnf with Reel and Aperture each in a private D-Bus session, 24 of 24 assemblies. ELEMENTS.md (one element with unknown paint bounds makes its faded ancestor window-sized). |
| 17 | 13fee514, b5ac3235 | a11y-atspi-adopt-focus-on-attach (correctness) | On a tree built from nothing (a Linux reader attached, or org.a11y.Status turned on late), `LinuxAccessibilityBridge.ApplyChanges` takes the node the tree marks focused as the holder and its target (the current item) as `_focusedPath`, so the item's object says focused and the list's does not, as after an early attach; a move the window tells in the same frame still goes out once (`_focusUntold`, which covers a field focused before the first tree); after a re-attach a served item that had the focus before the detach is marked unfocused. No signal at the attach itself, as UIA: Orca reads the focused state when it starts (`orca.py _activate_services` → `find_focused_object` → `AXUtilities.get_focused_object`), it does not wait for an event. Signals a move sends with the focus landed before / after the attach (AtSpiRig, headless): cursor walk eager (1,000) 0 / 2 → 2 / 2, selection Down eager 2 / 4 → 4 / 4, cursor walk served (10k, 1M) 0 / 2 → 2 / 2, selection Down served 0 / 2 → 2 / 2. `A11yAtSpiLateAttachTests` (19 cases: late or early status, walk and selection, eager and served, Tab out, a text field, a tree of element rows and an open context menu at the attach, detach and re-attach, a field focused before the first tree): 7 fail with the adopt undone, 2 more with the edge cases undone. step-ab (50bf6a18) `-Pairs 2 -Filter a11y/` (only the 21 AT-SPI rows reach the bridge): JIT 0/0 both pairs (one row's allocKB moved in pair 1 and the same way in the A/A), AOT 0/0, no allocKB moved, A/A clean. Windows Linux bridge 104, Core 5,212, Headless 1,820; Fedora the slnf (22 projects) green. ACCESSIBILITY-PLAN.md (a reader that attaches late hears the next move on both bridges). No real window (no desktop Orca). |
| 17 | e790d321, 91721d06 | text-heights-pass-height-only (measured first, then built) | Where a height ask's bytes went (base 13a3e123, JIT headless, prose Commit, per 6 px step): the heights pass 42.0 of 63.6 KB, of it 31.0 KB the break (503 `TextLine`s and a `LineSource` for each of 89 rows broken again), layouts and line arrays 9.5, memo puts 0.1, Junction's float[] 0.8; Changes 112 of 138 KB (84.5 the break). `TextMeasure.MeasureWrapped` now asks an internal `IWrappedSizeContext` (`SkiaMeasureContext`), and `TextPipeline.WrappedSize` breaks with the same decisions and adds the lines' widths and heights to the bit without making a line, keeping a size-only width range; a paragraph seen for the first time, or one whose kept layout was drawn (`TextLine.RunsMade`), is laid out as before (without that rule visible rows broke twice: 574f43a7 142 → 229 re-breaks a drag); first opens equal bytes. No public API. Headless JIT, UI KB a step (heights pass): prose Commit 63.6 → 21.7 (42.0 → 2.5), split 72.4 → 21.3, Changes 138.0 → 25.8 (112.0 → 4.1), split 196.5 → 41.1; a359e426 18.4 → 10.4 (9.3 → 1.5), split 24.5 → 15.3; 574f43a7 7.7 → 7.4, split 12.7 → 10.4; cycles equal or lower everywhere. Headless NativeAOT (idle-gated): prose Commit 78.7 → 35.2 KB, heights pass 49,494 → 896 B, 10.15/9.96 → 9.60/9.89 Mcycles; a359e426 split 33.6 → 23.5 KB (13,696 → 1,144 B; second-sweep re-breaks 1,575 → 1,772, cause not found); 574f43a7 31.9 → 32.1, split 34.4 → 31.0. Real window (NativeAOT, the 4K at 150 %, 120 Hz, GL, HDR on; 2 holds, 3 launches, 0 foreground takes, 0 nvlddmkm; timer steps): prose Commit 70.3/71.1 → 31.8/31.9 KB (heights pass 40 → 2.9), split 77.3/78.1 → 31.2; Changes 131.3/133.4 → 34.0 (101 → 4.4), split 188.6/189.6 → 51.7/51.1; a359e426 21.1/19.6 → 18.0/17.7, split 30.1/31.0 → 22.5; 574f43a7 28.1/24.6 → 25.0/22.2; Mcycles flat. Equality: 2,011,835 size asks against the reference layout's size (reallatin, realbig, zh/ja/ko/th, ar/he/emoji; 5 styles × 47 widths) with 402,360 layouts between and 114,500 range-end probes: 0 differ; corpus hashes unchanged (48k 7A18AF8DE8E05271, 240k F09E9038726E4BD6, also with a size asked before each layout); Junction heights, boxes and pixels equal in all 8 scenes. step-ab (13a3e123) `-Pairs 2`: JIT 0 slower both pairs (one faster in pair 1, also in the A/A), two a11y rich-text-in-list rows' allocKB 2.9 → 2.0 / 3.3 → 2.4 on JIT only (stack-allocation placement); AOT 0/0, no allocKB moved, A/A clean. `WrappedSizeTests` (5) fail under reverse edits; Windows Text 311, Skia 467, Headless 1,820 (goldens), Junction 1,182, Vesper 954; Fedora the slnf (22 projects, 11,746 tests); Fedora Junction 1,181 of 1,182 — `JunctionAppHostTests.TheBranchesViewShowsABranchCreatedOnDisk` fails 3 of 3 at the base 13a3e123 too (r17fix). ELEMENTS.md. |
| 17 | 9f9518e2 | junction-linux-ref-in-new-folder (correctness; found by step 8's Fedora run) | Fedora's `JunctionAppHostTests.TheBranchesViewShowsABranchCreatedOnDisk` failed 3 of 3 at 13a3e123 — and also at b5ab4704 (3 of 3 alone, 2 of 2 in its class, no D-Bus session), so no round 17 commit caused it; step 5's Fedora pass was a race won under load. `PumpUntilVisible` reads the a11y tree for 20 s; the Branches list held only "main": the summary was read in the first frame, and no reload followed because on Linux inotify watches a new folder only after it hears the folder made, by when git had written `refs/heads/feature/from-a-test` into it, and the refs watcher's filter had no `DirectoryName`, so the folder's own event was dropped too. A real Junction bug on Linux: a `feature/x` branch made in a terminal, a fetch's first slashed branch or a tag in a new folder left the views stale; Windows never affected (10 of 10 at 2-core affinity). Fix (`apps/Junction/Git/GitWatcher.cs`): the refs watcher also asks for folder events, so a folder made or removed under `.git/refs` counts as a ref moving; on Windows one more event inside the same debounce window. `GitWatcherTests.ARefMadeInANewFolder_IsToldAsARefMoving` (two branches and a tag in new folders, then a branch deleted) fails 3 of 3 under a reverse edit on Fedora (nothing heard); the host test and the new test 10 of 10 on Fedora (private D-Bus) and Windows (0x3); `JunctionAppHostTests` + `GitWatcherTests` 30 of 30 on both (twice on Fedora); Junction.Tests 1,183 of 1,183 on both. No step-ab (app watcher only; no suite row reaches it). |
| 18 | ca72af3e, 6d5dff93 | vesper-file-not-its-own-partner (question 72b: (2)) | `DiffPartner` returns no partner when its fallback would be the cursor's own file; `OpenDiff` returns early there (F3 does nothing, as on a folder), and the ring (`DiffTargetPath`) and `PartnerStays` follow the same rule; the palette's "Compare files (diff)" goes through `OpenDiff`; no a11y text names the partner. Headless (JIT, 10 Downs, alternated ×2), one folder in both panes: the other cursor on ".." 29.2 → 1.2 KB a key, 1 → 0 builds; on a folder the same; F3 there "Files are identical" (2 builds) → nothing, no build. The agent's call, in the same rule: the other cursor on the cursor's own file is a self-pair too, so a walk over that file builds on the key onto it and the key off it (2.0 → 6.5 KB a key over 10 keys, 0.2 builds a key; one line in `DiffPartner` to undo). Unchanged: the other cursor on another file 2.0 KB, two folders 1.2 / 2.0 KB; F3 with a real partner compares, the Missing fallback still opens "Right file missing". Frames byte-identical to the base's in every layout but the self-pair ones (only the ring row, 1,234 px; the self-pair F3 frame shows the panes): `r18b1\frames\selfpair-*-{before,after}.png`. step-ab (49bd5d81) `-Pairs 2` (no suite row runs Vesper; whole suite): AOT 0/0 both pairs, A/A clean; JIT pair 1 0/0, pair 2 11 a11y rows slower (load: both sides run the same suite code; the A/A 3 slower). 9 tests fail with the old fallback restored; Windows VesperCommander 963 (+4), Headless 1,828; Fedora the slnf (Vesper 945, +20). |
| 18 | e546143d, 3191b065, fc885fc2 | text-wrapped-size-cache-hit-keeps-range (the review objection to e790d321) | A size ask answered from the width-keyed `_cache` puts that layout's wrap range back, on the size path only (on Layout's hits the range ping-pongs between a drawn row's two widths); 3191b065 reads the range's bytes from the cache entry's weight, so a node grows 8 B, not 16 (e546143d alone read `text/wrap 150 paragraphs resize` 16.8 → 17.3 KB in both pairs; after it 17.0). Headless (JIT / AOT, idle-gated, alternated ×2), the sweep after the return to 1,440, UI KB a step: a359e426 split 23.7 / 23.6 → 18.6 / 18.5 (−22 %; size breaks 1,663 → 1,436, full layouts 109 → 7, characters itemized 239,123 → 193,973), unified 19.4 / 19.3 → 16.6 / 16.5 (−14 %; layouts 57 → 0), 574f43a7 split (a third sweep, past the base's one-time cache growth) 32.8 / 32.5 → 21.5 / 21.2 (−34 %), Vesper's zh drag 25.9 → 24.6 (−5 %; characters 434,070 → 364,749, round 17's count). The first sweep unchanged on JIT (33.0 / 52.1 / 37.8 KB; AOT +0.4 within its run-to-run spread); the first return to 1,440 costs 30.6 KB once (the drawn rows' second width is served by the opening layout, whose runs are made then), later returns equal. Real window (NativeAOT, the 4K at 150 %, 120 Hz, HDR on, GL; 2 holds, 8 launches, 0 foreground takes, 0 nvlddmkm): Junction split a359e426 after the return 23.6 → 18.6 KB a step (−21 %), the same counts as headless; first sweep 33.0 / 32.6 → 32.6. Equality: 3,980,865 sizes, 796,160 layouts and 114,500 range-end probes against the reference on the real documents with a second drag after the return, 0 differ; corpus hashes unchanged (7A18AF8DE8E05271, F09E9038726E4BD6), also with a size asked before each layout. step-ab (on 3191b065) `-Pairs 2`: JIT pair 1 3 slower (the RowHeights rebuild rows that flip either way, TableView sort +12 %), pair 2 clean; AOT pair 1 `wheel real-loop` +10 % (10 → 11 µs, no text code), pair 2 clean; A/A 0/0 both. 3 new `WrappedSizeTests` assertions fail under reverse edits; Windows Text 324, Skia 473, Headless 1,828, Junction 1,188, Vesper 963; Fedora the slnf (22 projects) and Junction 1,188 (private D-Bus). |
| 18 | ab4069ca, 5b7a4494, 5f61fb12 | reactivity-aperture-back-lands-where-it-was (the review objection to 0f9c8477) + question 76 (a) | (a) A grid page keeps its scroll offset and cursor in the `Visit` when it is left, and Back, Forward, Escape, the mouse back button and a rail reopen restore them: the new list's held offset, or the cursor's row when there is none, with the cursor's card made, ringed and focused in the Back's own frame; a build reveals the grid cursor only when the cursor or its row moved (keys and resizes still reveal it), so a wheel is no longer pulled back by a page fetch or a poster landing (without that, mouse users stopped at 480 instead of 2400); a cursor left out of sight comes to the first card on screen on arrival. Headless (JIT, 300 films, 1440×900, alternated ×2): Back lands at 2674 on card-m070, focused (was 0, nothing focused); Back 340.8 → 338.3 KB (336 made either way); the two keys after 279.1 + 75.6 KB, 2 builds, 361 made → 0.4 + 0.4 KB, 0 builds; Escape, Forward and a rail reopen the same (each next key ~278 KB → 0.4); the mouse case (15 notches, a click, mouse back) lands at 2400 (was 0). Real window (NativeAOT, guarded; **the 1440p only, 100 %, 200 Hz — the 4K was not attached**; 2 holds, 6 launches, 0 foreground takes, 0 nvlddmkm): lands at 2360 on card-m063 (was 0), the key after 274.8 KB / 1 build / 289 made → 0.5 KB / 0 builds; the mouse case at 2400. (b) Back and Escape focus the page's cursor card, or its first card on screen: Escape to Home sends one focus event on a live card (was none, the reader left on a gone element); Play's and Resume's accessible names carry the title ("Play Film number 000", "Resume Film number 053, 20 m in"), visible labels unchanged. Frames: Keys 19 of 26 byte-identical to the base's, the rest the landings, Home's ring after Back and after a later rail click (Home keeps the cursor Back gave it) and one rail tooltip a step later (`r18b3\frames\{base,change}-{Keys,Mouse,Reader}\`). Not covered (question 77): a page with no cursor opened by a number key or the palette still leaves the focus on nothing, as at the base. step-ab (818cece5) `-Pairs 2` (no suite row runs Aperture; whole suite): JIT pair 1 3 slower (RowHeights 20,000 rebuild +39–42 %, 400 faded icons +15 %), pair 2 and A/A clean, no allocKB moved; AOT 0 slower both pairs, A/A clean. Every new test fails under its reverse edit; Windows Aperture 435, Headless 1,828; Fedora Aperture 435 (private D-Bus). |
| 18 | 3442c3f5, 30a53f02, f97a6736, 89ecaeff | elements-context-menu-after-patch (correctness) + elements-selectable-label-static-wiring + layout-list-children-without-set | (a) An element's own context menu (TextInput, TextArea, RichTextEditor, selectable labels) is no longer stored in `ContextMenuItems`: the window asks the element through an internal virtual, and `PatchFrom` copies only a menu the app set (an app's null still turns the menu off; the public getter returns what it did). After one rebuild, before: Cut, Copy and Paste off and Select All on the build's copy; after: enabled and acting on the live element (headless clipboard). One delegate fewer a field or label (56 B JIT, 48 B AOT). (b) Selectable labels wire shared static handlers that read `e.CurrentTarget`, in one exact array of six: making a label selectable 880 → 248 B; a selectable label 1,496 → 920 B (JIT), 1,448 → 920 (AOT); −4.0…−6.3 % a wheel notch; a Junction file Down 223.1 → 211.9 KB (−5.0 %). (c) A VirtualList's window move tells kept children by a mark (a bool in padding: no element type grew) and reuses its children array while the row count holds (`VirtualListElement` 1,072 → 1,080 B); TreeDiffer's replace takes the same `ReplaceChildren`; two window moves 2,880 → 0 B; −4.2…−7.3 % a notch. Headless (b)+(c), JIT / AOT, alternated ×2, idle-gated: Vesper reader −12.4 / −12.0 %, compare −10.7 / −10.2 %, Junction Commit unified −9.7 / −9.4 %, split −10.2 / −9.8 %, History −5.0 / −5.1 %. Real window (NativeAOT, the 4K at 150 %, 120 Hz — attached again; 2 holds, 8 launches, 0 foreground takes, 0 nvlddmkm; 1 frame a notch both sides), KB a notch: Junction Commit unified 16.5 → 14.5 (−12.0 %), History −5.3 %, Vesper reader 15.2 → 12.7 (−16.2 %), compare −11.5 %. Pixels: 20 headless frames (wheel, wheel back, a selection drag, the menu open and closed; Junction Commit and History, Vesper's reader and compare) byte-identical, twice. step-ab (56215ccc) `-Pairs 2`: JIT and AOT 0 slower, 0 faster both pairs; 31 rows' allocKB down, none up (drag to reorder 8.8 → 4.0 KB, palette Down 3.9 → 3.3, list 100k wheel 27.9 → 26.8); A/A AOT clean, JIT 2 rows (the rich-text-in-list row, noise). Every new test fails under its reverse edit; Windows Core 5,227, Headless 1,832, Text 324, RichText.Markdown 62, Vesper 963, Junction 1,188; Meridian's RunnerView tests flake in full-suite runs at the base too (Waiting meridian-runnerview-build-count-flake); Fedora the slnf and Junction 1,188 (private D-Bus). |
| 18 | 8b7471c8, fada66d3 | a11y-uia-disconnect-off-ui-thread (closes Waiting a11y-client-frame-share) | Each apply hands the providers it let go to one MTA thread for the process, which tells UI Automation they are gone (`UiaDisconnectProvider`) and then does the Detach/Release, in order; a provider made again for the same element first gets its old one told on the calling thread (TellFirst); `Dispose` tells whatever the thread has not taken and spins (no pump, 2 s cap) on any in flight, all before `UiaReturnRawElementProvider(hwnd, 0, 0, 0)`; `HeadlessUiaReader.Disconnected`/`Clear` flush, bare-bridge tests call `FlushDisconnects`. No API, no pixels, AT-SPI untouched. Real window (NativeAOT Junction, the 4K at 150 %, 120 Hz, GL; an out-of-process FlaUI client asking, focus-only and reader-like; measured around `RenderFrame` inside `Application`, alternated ×2, min of 4 frames), UI-thread cycles / wall: the palette close after 10 Downs focus-only 25.71 M / 8.16 ms → 2.62 M / 0.97 ms (−88 % wall), reader-like 28.20 M / 8.94 ms → 2.85 M / 1.01 ms (−89 %), the worker 0.19–0.40 M / 0.06–0.15 ms for 13–14 disconnects; the first palette Down's disconnect 3.1–5.5 ms on the UI thread → 0.11–0.35 ms on the worker; the first Commit open focus-only 9.02 → 7.84 ms (−13 %), reader-like 8.85 → 7.26 (−18 %), a second open 2.03 → 0.95 and 1.77 → 1.02 ms; History Downs unchanged. The client logs show the same events (HasKeyboardFocus 12, IsSelected 50, structure 2/10/8), no errors; 2 holds, 8 launches and the UiTests, 0 foreground takes, 0 nvlddmkm. Aperture not run in a real window. Headless event streams: Aperture history, served items and Fluent nav equal the base's; Vesper's EnterFolder reader tests show the same 3 timing variants on both sides over 7–10 runs. step-ab (43b87bfa) `-Pairs 2`: JIT pair 1 0 slower / 8 faster (the A/A 11 faster, AT-SPI rows among them: noise), pair 2 0/0; AOT pair 1 0/1, pair 2 0/0, A/A 1 slower (a UIA rename row +10 %); no allocKB moved (headless never shows the cost). 3 new tests fail under reverse edits (off the UI thread; all told before the root is released at `Dispose`; an old provider told before its re-made one); Windows Core 5,227, Headless 1,835, Junction 1,188, Vesper 963, Aperture 435, UiTests ReaderFocus/AccessibilitySample/VirtualListSample 18 (8 WinFormsHost need the foreground: skipped); Fedora the slnf (22 projects). ACCESSIBILITY-PLAN.md. |
| 18 | 678a786c, b33b1ef6 | apps-aperture-artwork-retained | Where the bytes live (base, real Jellyfin as the named user, back at Home after 20 item pages): private 327 MB against 148 MB before the walk; decoded pictures 67 MB (210 pictures, 3 heroes 30.5 MB); Skia's GPU cache 84 MB (65 MB textures of live pictures, 60 MB of them off screen; 12 MB upload staging); GC committed 41–44 MB (24–27 after a full GC, heap 7–8); with every picture dropped and Skia purged private 163–170 MB. On NVIDIA Vulkan GPU memory counts in private bytes, ~1 s behind a purge. The survey's O(pages) was overstated: heroes were already capped at 4 (e18b6896); after 20 pages +179 MB, not ~+600. Built: heroes kept by their bytes (always the latest 2, up to 4 while they fit 24 MB, `HeroBytesKept`), and a hero let go is disposed at once on the UI thread (`PosterCache.Drop`; before, it stayed until another download landed). Real window (NativeAOT, Vulkan, the 4K at 150 %, 120 Hz, a 1440×900 window; 3 holds, base and change alternated ×2, 0 foreground takes, 0 nvlddmkm), cache decoded / private / working set / DXGI local MB: after 6 pages 43.1 → 35.2 / 268.4 → 248.0 (−7.6 %) / 228.3 → 216.7 (−5.1 %) / 118.1 → 111.1 (−5.9 %); after 20 pages 67.1 → 57.0 / 341.5 → 317.1 (−7.1 %) / 283.2 → 266.3 (−6.0 %) / 148.9 → 137.7 (−7.5 %); back at Home private 327.3 → 302.5 (−7.6 %; after a full GC 310.2 → 285.3, −8.0 %); nonlocal unchanged; Skia's cache 83.6 → 73.5. Blank frames: a page open 6.5–8.2 frames both sides (the hero blank until it lands, as before); Back to a kept hero 1 both sides, to a hero let go 2 and 4 against 1 (it loads again). Headless (50 pages, 150 %): decoded 72.3 → 52.0 MB, heroes held 4 → 2; 53 of 53 landed heroes byte-identical, the 2 loaded again included. Declined (DECISIONS.md): dropping the decoded copy after upload (−67 MB): no device-loss path, a texture is one window's, raster and capture need the pixels, a decode on demand is 10–35 ms on the UI thread. step-ab (71e62a28) `-Pairs 2` (no suite row runs Aperture): JIT 0 slower both pairs (11 and 8 faster a11y/virtual rows, the after side both times: run order, identical framework code), A/A 0/0; AOT 0/0 both pairs, A/A 0/0. 2 new tests fail under three reverse edits (a count-only bound, retire instead of drop, `Drop` without dispose); Windows Aperture 437 (+23), Headless 1,835; Fedora Aperture 437 (private D-Bus). |
| 18 | 941cffdf, 0f5cda09, 421cc677, f26168b7 | reactivity-aperture-parts-build-on-change + question 77 (a) | (a) Each part keeps a value of what it shows and builds from it alone: every app build compares the value now with the one shown (`Place(stale:)`); the title bar's value is server, user and scale; the rail's the lit door, libraries, the request server's user, the three counts and scale, its loaders read untracked for the compare. The view-only flag is gone; the hidden forced `MarkDirty` still invalidates every part (the oracle). No path marks anything, so sign-out/in, a server or user change, libraries, request doors, badges and the light all reach the parts. REACTIVITY.md updated. (b) Each rail door is its own part, made once and kept (fixed doors by name, libraries by slot: nothing made or dropped on a library change or sign-out); the door going dark and the one lighting are built through their `own:` revisions. Headless (JIT, 300 films, 1440×900, alternated ×2, repeats within 0.2 KB), base → (a) → (b) KB: Enter 167.4 → 100.8 → 72.9 (−56 %; part builds 4 → 1 → 1 door); library first open 753.6 → 589.4 → 564.8 (−25 %; 8 → 1 → 2); Forward 78.7 → 61.1 → 32.9 (−58 %); Back/Escape 333.8 → 288.5; number key 336.1 → 293.0; rail reopen 365.4 → 322.8; 0 (Favourites) 59.9 → 14.0; / (Search) 57.8 → 15.2; a paging build while wheeling 237.6 → 139.9 (part builds 4 → 0); sign-out 209.7 → 111.9; sign-in 476.5 → 438.6 (21 door builds, once per sign-in). Lit door, doors and the title bar's user equal to the base at all 183 steps. Real window (NativeAOT, Vulkan Mailbox, HDR on, the 4K at 150 %, 120 Hz; 1 hold, 6 launches, 0 foreground takes, 2 refused activations, 0 nvlddmkm; ~42 KB of probe line included): Enter 180.4 → 118.8 → 86.4 KB (part builds 4 → 1 → 1); library first open part builds 8 → 1 → 2 (KB 1,616–1,678 → 1,253–1,403, noisy with poster landings); Forward 89.9 → 72.2 → 45.3. Question 77 (a): every way into a page (number keys, 0, /, the rail, the title bar's user, page links; Aperture has no command palette) gives the first card the cursor and the keyboard, ringed, and a reader hears it; a click or Tab that took the keyboard while a slow page loaded keeps it (checked after the build: a kept part is not in the window's tree mid-build). +0.0–0.7 KB an arrival; the next key 0.4 KB, 0 builds. Behaviour: the first arrow on a page moves on from card 0, as after Back (18 tests updated). Pixels: (a)+(b) 183 walk frames byte-identical; question 77 9 frames, 3 identical, the arrivals differ only inside the focused card's and the rail button's box (the ring). Reverse edits: Rerender invalidating again 3 fail; rail never stale 6 PartTests + 2 RequestServerHostTests; title bar never stale 3; forced build not invalidating, the oracle 15; whole rail on a light change / dark door not built / a door made per build, the (b) tests; question 77's UIA focus test 4 of 4, the guard 2. step-ab (4ed3b4dc) `-Pairs 2` (no suite row runs Aperture; app-only change): JIT pair 1 2 slower (RowHeights rebuild +38–40 %), pair 2 11 slower on the a11y rows the A/A moves 10–17 %; AOT pair 1 2 slower (a11y tab AT-SPI +16 %, 400 faded icons +25 %), pair 2 and A/A clean: noise, framework code identical. Windows Aperture 448 (+11), Headless 1,835; Fedora Aperture 448 (private D-Bus). |
| 18 | d7eedc4b, 092201cc | reactivity-aperture-poster-lands-in-place (with r18-8's apps-aperture-artwork-landing-rebuilds-view) | Counted first (headless, base): view builds data/navigation + picture landings: library first open 4 + 8–9 (32 KB each); Home's start 2 + 5–7 (33–330 KB); a film page with cast 2 + 2–3 (84 KB); a series page 3 + 2 (47 KB); a season page 2 + 4 (121 KB); a 404 page 2 + 1 (30 KB). Built: a picture that lands goes into the element that waits for it (cards, heroes, cast faces), keyed by its URL so the reconcile never patches it from another item's; no view build for a landing or a failed fetch. The placeholder glyph is that image element, so nothing shows differently before a picture lands; a hero always has its image element (empty draws nothing). Data builds unchanged (the page's own loads). A landing re-lays only the image and its ancestors (a grid card 9 laid, 10 measured against 20 and 15 for the build it replaces; a hero 10/10, a face 12/16). Side fixes the landing builds had masked (`WindowFitTests`' full-build check): a grid row a taller window brings in asks for the page a build's estimate would load; a full build after a 404 makes the same tree. Headless (JIT, 1440×900, ×2 alternated), KB / builds / made: library first open 1,109–1,150 / 12–13 / 886–921 → 594–600 / 4 / 394–400 (−47 %); a Down bringing a row 145–205 / 1–2 / 120–161 → 59–82 / 0 / 43–45; Home's start 1,236 / 8 / 753 → 865 / 3 / 416; film page with cast 376–378 / 4 / 259 → 210–212 / 2 / 92; series page 154–157 / 4 → 110–112 / 3; season page 441.5 / 5 → 202 / 3; 404 page 70.8 / 3 → 41.6 / 2; one landing alone 31–84 KB / 1 build → 0.2–0.5 KB / 0. Real window (NativeAOT, the 1440p at 100 %, 200 Hz, the 4K not attached; 1 hold, 4 launches, 0 foreground takes, 0 nvlddmkm; ~42 KB probe line included): library first open 1,413–1,424 KB / 19 builds (15 picture) / 1,149–1,155 made / 16–17 frames → 604 / 4 / 388 / 16; ten Downs 1,895 / 16 / 1,219 → 947 / 1 / 339; film page 524–595 / 6–7 / 364–435 / 3–4 frames → 240 / 2 / 80 / 4; Home's start 13–15 builds / 857–898 made → 3 / 416; series page 166–259 → 172 KB (waiting picture boxes matched by key on a data rebuild, ~0.3 KB each). Pixels: 33 of 33 headless frames byte-identical (a distinct picture per item; 24 settled steps, 9 with a picture held back). Guard: the perf suite references no app, so suite proposal (k) is `ArtworkLandingTests` (7 tests, an item page whose pictures land one a frame with no build); GridRowKeepTests and PartTests expect no build; five reverse edits each fail (landing builds the view; no key on the waiting element; picture only by landing; a failure builds; a failed new hero size keeps the stand-in). REACTIVITY.md updated. step-ab (12fc7265) `-Pairs 2` (no suite row runs Aperture): JIT 0/0 both pairs, no allocKB moved, A/A clean; AOT the same. Windows Aperture 455 (+7), Headless 1,835; Fedora Aperture 455 (private D-Bus). |
| 18 | f980fc8d, 06ca5c94 | apps-aperture-home-shelf-turn-builds-home | Measured first (headless, 3 shelves of 30): a shelf turn ran one full app build of 66.7 KB, of it the turned shelf 56.3 (7 cards 39.3, heading and row 15.3 with the menu and "See all" 5.6) and the rest of Home 10.4. Built: each library-card shelf on Home is its own `Part`, kept by its row key (one film can sit on two shelves, so not by slot); a page turn (Right/Left past a page end, Up/Down onto another page, the shelf's Next/Previous) writes that part's own signal, so only that shelf builds (0 app builds, 1 shelf build ~50.6 KB) and hands back the menu and "See all". Headless (JIT, 1440×900, ×2 alternated, repeats identical), KB / app builds / made: a Right that turns a page 98.0 / 1 / 61 → 80.2 / 0 / 52 (−18 %); a Down/Up turn with pictures cached 67.0–68.4 → 50.9 (−24…−26 %); with 7 new pictures 93.1–96.7 → 77.0–79.2 (−17…−18 %); after the cursor left another shelf (base remade 2 shelves) 121.4 → 81.1 (−33 %); a Right within a page 0.4 = 0.4; Home's start 680.1 → 686.9 (+1.0 %, 3 host elements); Back to Home from a film page 180.2 → 183.9 (+2.1 %); with 8 shelves a turn −30 %, start +2.8 %, Back −1.3 %. Real window (NativeAOT, Vulkan, the 1440p at 100 %, 200 Hz, the 4K not attached; 1 hold of 86 s, 4 launches, 0 foreground takes, 0 nvlddmkm; ~28.5 KB probe line included), KB / builds / made / frames: a Right that turns 122.9 / 1 / 61 / 3 → 107.4 / 0 / 52 / 3; a Down turn 119.8–122.7 → 104.4–107.2, 3 frames; a cached Down/Up 95.1 → 79.8, 1 frame; a Right within a page 28.7 → 29.0 (the probe line walking 3 more hosts). Pixels: 23 of 23 headless frames byte-identical both pairs. Guard (suite proposal (j); the suite hosts no app): `HomeShelfTurnTests` (4), each failing under its reverse edit (a turn builds only its shelf; the cursor card ringed and focused after a turn; a shelf whose items change shows them; the forced build builds every shelf and equals what the turns left). REACTIVITY.md: a part's own build reads untracked what its owner's build reads; hand focus over only from a build the part ran alone (`afterBuild` also fires for builds `Place` starts inside the owner's build); `Mount()` before `Place(stale:)` when the flag depends on the part's last build. step-ab (0e796cf5) `-Pairs 2` (no suite row runs Aperture): JIT pair 1 2 slower (RowHeights 20,000-row rebuild +30…+36 %, the noisy row of steps 3 and 7) and 8 faster, pair 2 0 slower, A/A clean; AOT 0 slower both pairs, A/A clean; no allocKB moved. Windows Aperture 459 (+4; 23 skipped), Headless 1,835; Fedora Aperture 459 (private D-Bus). Main's 133bcfe6 (merged 936b5520, not on this branch) moves Up/Down shelf to shelf with no page turn: after the release merge the real case is Right/Left across a page end and Next/Previous (−18 %); see Waiting release-merge-main-shelf-arrows. |
| 18 | f7f2f6c5, 7f9b1e46 | dotnet-junction-collect-after-big-walk | `HighlightLanded` now also sets `_collectWhenIdle` when a landed walk was big: ≥ 2,000 lines walked or ≥ 32 MB allocated on the worker (`GC.GetAllocatedBytesForCurrentThread` around the walk). Why: real C# (HultUI's own sources) allocates 5.5 KB a line on AOT and JIT; one collection after a walk gave back 3.3 MB of working set at 500 lines, 8.1 at 1,000, 12.8 (−13 %) at 2,000, levelling at ~17.5 from 3,000; below 2,000 lines a walk allocates less than the gen-0 space the window keeps committed anyway (22–30 MB); 32 MB catches files of few long lines, past where the gain stops growing and 4× the 8.3 MB a language's first walk spends loading its grammar. Beyond the brief: the collection also waits until the window has had no input for 2 s (the existing public `Window.InputActivity`), so it never lands in a scroll after a walk. Real window (NativeAOT, guarded, the 1440p at 100 %, 200 Hz, primary, the 4K not attached; 2 holds of 166 and 156 s, 8 launches alternating, 0 foreground takes, 0 nvlddmkm), committed / working set / private MB at rest: after the Commit open (7,453 lines, 161 MB walked) 33.5 / 123.1 / ~120 → 8.4 / 98.0 / 94.2 (working set −20 %); after 3 first visits 40.9 / 133.0 / ~130 → 10.1 / 101.5 / 93.8 (−24 %); after a 4th visit and 7.5 s of wheel 42.1 / 145.2 → 11.3 / 113.7; real C# cut to 100–4,000 lines collected only after the 2,000-, 3,000- and 4,000-line walks (working set after 2,000 lines 131.2 → 108.4). Headless JIT heap / committed / working set at rest: Commit open 15.5 / 28.7 / 100.4 → 5.4 / 5.4 / 77.8; after 3 visits 19.4 / 41.7 / 114.9 → 7.1 / 7.1 / 79.7; the survey's scene ending in 6 revisits at 4 s 46.0 / 59.9 / 134.7 → 29.4 / 25.8 / 103.9 (one collection a rest; none after the cached revisits). jrepo: 0 collections (real window 873 lines, 14.8 MB walked; headless), memory equal. Frame cost: pauses 4.3–6.6 ms, one a rest (5 a launch), ~3.6 s after its key; no frame within 400 ms of any, so no late frame; none while keys or the wheel moved (the one after the wheel 2,029 ms after the last notch). Tests: `HighlightResumeTests` gains a finished 9,000-line walk (nothing collects while the wheel turns, one collection after it stops, none after a one-line file), failing under three reverse edits (no big-walk trigger, no input wait, every walk big); the kept-texts test expects one more collection when its resumed walk is big. NATIVE-AOT.md: start an at-rest collection's wait from the window's last input too; decide by what the work allocated, not what it kept. step-ab (6ef42e77) `-Pairs 2` (no suite row runs Junction's walk): JIT pair 1 0/0, pair 2 2 faster (RowHeights rebuild) and one rich-text a11y bytes move, the A/A moving the same rows the other way: noise; AOT 0/0 both pairs, no bytes moved, A/A clean. Windows Junction 1,189, Headless 1,835; Fedora Junction 1,189 (private D-Bus). |
| 18 | 0ec5182c, 1ddec166, a83bcd2e | painting-numericupdown-paint-bounds + painting-drag-ghost-layer-bounds (question 73 (b)) | (a) `NumericUpDownElement.PaintBounds`: the border box, the value's ink (`TextPaintBounds` with the same rect, style and one line), the hairline, the step strip and both chevrons plus half their stroke, grown by 1; states checked: at rest, focused, mid-edit, held, shorter than its line, narrower than its strip (the window's focus ring draws after the tree; an edit wider than the field keeps what fits its line; no disabled look, no caret). (b) New internal `ElementRenderer.TreePaintBounds` (the subtree's bounds with the root's own transform and sticky shift) bounds the source ghost (0.7) and a custom DragVisual (0.8). Snapping the layer's origin cannot remove the drag-frame difference (Skia already puts it on a whole device pixel); pinning it at the window's corner made all 32 frames byte-identical but grows the layer toward the window (0.199 Mpx a frame), so it stays as the user accepted. Headless (JIT, ×2 alternated): layer area Vesper rename 1.198 → 0.041 Mpx a frame, 17 of 17 frames byte-identical both pairs; Junction drag 2.916 → 0.026 Mpx a frame, frames before and after the drag identical, 23 of 29 drag frames differ in 2–7 pixels by ≤ 6/255, both pairs alike. Real window (NativeAOT, the 1440p at 100 %, 200 Hz, the 4K not attached; Junction 1440×900, Vesper 1240×820; 2 holds, 16 launches, 0 foreground takes, 0 nvlddmkm), MB first time Skia / DXGI local / private, and DXGI local at the end: Vesper rename GL +4.12 → +0.24 / +8.1 → +0.2 / +11.5–12.1 → +1.4–1.9, end 30.2 → 22.3 (−26 %); Vulkan +4.24 → +0.37 / +5.0 → +1.0 / +5.9 → +1.9, end 43.4 → 39.4 (−9 %); Junction drag GL +6.06 → +0.12 / +14.1 → +2.2 / +24.7–25.2 → +9.8–10.0, end 40.3 → 28.4 (−30 %); Vulkan +6.06 → +0.12 / +6.0 → +0.0 / +6.3 → ≈0, end 44.9 → 38.9 (−13 %). Tests: `OpacityLayerTests.FadedNumberFieldsAreBoundedAndLoseNothing`, `RenameCounterFadeTests` (Vesper), `DragGhostLayerTests` (Headless: layer size, a custom visual, the whole ghost at every edge and corner at 1× and 1.5×, a rotated source), failing under reverse edits (bounds null/unbounded; bounds cut to the element's own box, NUD 53–154 pixels off; the root's transform left out). ELEMENTS.md and DRAG-AND-DROP.md updated. step-ab (5057eb1c) `-Pairs 2` (no suite row fades a number field; `input/drag to reorder 200 rows` draws no frame mid-drag, 0.217 → 0.215 ms): JIT 0/0 both pairs, A/A clean; AOT `input/wheel … real loop` and `text/ellipsis resize` ~+11 % in one pair each, a11y tab rows 10–13 % faster, no bytes moved, A/A clean: noise. Windows Core 5,227, Rendering.Skia 473, Headless 1,845 (goldens unchanged), Vesper 964, Junction 1,189; Fedora Skia 473, Headless 1,621 (+128 skipped), Vesper 946, Junction 1,189 (each app in a private D-Bus session). |

## Examined, not worth it

From the round 18 surveys (details in `17-reports/r18-*`):

- **Aperture keyboard browsing** (0f9c8477; r18-1, r18-2, r18-4, r18-8): no layout pass on ring or Tab-stop moves; 1
  frame a key, 0.21–0.56 KB render a frame, only bounded layers (disabled page buttons 0.003 Mpx); Tab 0.5 KB; Back and
  Forward fetch nothing and cost the destination page's build, as Escape did; the history is 100 `Visit`s of three
  fields; the season watch builds once in 30 minutes.
- **Aperture's history retention** (r18-4): open/back ×300 1.32 → 1.35 MB after a full GC; 150 different films 1.32 →
  1.62 MB (text caches); `_arrivalFocus` frees 0.0 KB when cleared.
- **Deferring each digit or mark proof** (781a857f; r18-5, `TEXT_PROOFAFTER=8`): Junction's first frame 1,585 → 787
  HarfBuzz calls but 1,462 → 1,345 KB on AOT (−8.0 %, under the churn bar); the start-up bar unlikely (~800 calls of 1–3
  characters). r18-8 bisected Junction start +7.7 % (once a session) and History Down +5 % to 45cbde7b; r18-5 measured
  20 History Downs −8.8 KB with the digit table: low.
- **The 1,172 width hits a split-view sweep** (r18-5): answered by `_cache` at 0 B.
- **b1423e6e against frame pacing** (r18-3): at most one late frame a browse (gaps 23.5–23.7 ms against a 12–20 ms
  cadence; a 6–8 ms pause); the next browse commits ~22 MB again (+5,400–5,800 page faults); working set after two
  browses 150 against 187 MB.
- **The over-allocated swapchain's present in a drag** (r18-3): over 1 ms in most drag frames; the known trade of
  drag-only over-allocation (memory `swapchain-maintenance1`).
- **GL Reel's window-sized cache** (r18-3): Skia 12.49 → 35.74 MB from the default to a maximized size (~4.2 B a pixel),
  cause not proven; GL is not Reel's or Aperture's renderer.
- **Opacity-0 layers after 0a7bb324** (r18-2): Skia draws nothing for an alpha-0 layer (a save plus an empty clip), so
  they hold no GPU memory; Junction's dragged row at 0.4 is bounded (0.026 Mpx).
- **UIA raises a key** (r18-7): HasKeyboardFocus off/on ~5 % of a key's frame cycles with a reader-like client (dropping
  them changes what readers hear); the scroll-percent raise ~4 % of a History walk's frame cycles; Commit → History
  0.6–0.96 M cycles with 0 disconnects; `IRawElementProviderAdviseEvents` never asked for (round 12).
- **Reactivity in Aperture** (r18-6): a Down that brings a new grid row builds once, for its posters landing (→ step 8);
  a Home page turn is 2 builds by design; a film page builds 3 times as it opens (navigation and two loads, network
  timing).
- **`MediaItem` +72 B** (ee044d36, 93f8a26d; r18-4): 0.7 MB for 10,000 items. `WebViewElement` 632 → 688 B (e66ead9f),
  one a web view.
- **BoxElement's share of a reader notch** (r18-1): 24.6 % (4.9 KB, ~4 boxes a row); element size was closed before. A
  relayout boundary at a now-leaf VirtualList: not measured again (round 5: 1–2 %), a large engine change.
- **Expert 2's Junction host at 747–790 MB private** (r18-2): its host builds the settings in-process and skips
  `Program.cs`; the real app's scene ends at 171 MB working set (r18-8). A host artefact.

From the round 17 surveys (details in `17-reports/r17-*`):

- **text-heights-pass-same-height-range** (r17-5): a "same height" width range for Junction's heights pass needs a public
  out-range on `TextMeasure.MeasureWrapped`, for at most −4.9 KB of 27 KB a step (−18 %) on one scene; reusing
  `_diffHeights`' array −1.5 KB (5.7 %). Step 8 measures the height-only ask without API.
- **Swap interval 1 during tweens** (5da967d2's untried lever; r17-3): NVIDIA's write-combined pool fills the same either
  way (Vesper GL nonlocal 16.03 → 28.52 MB at visit 5, then flat), 90–94 frames per 5 visits on both sides.
- **The 100 ms display-colour poll on Vulkan without events** (r17-3): 13–15 UI wakes a second only when a GL-default app
  is forced onto Vulkan; Aperture and Reel, which ship on Vulkan, use display-colour events.
- **The palette's kept panel layer** (r17-3): +8 MB on GL, +4–5 MB on Vulkan, once a window: below the bar.
- **Keyed diff rows alone** (r17-6): −46 % measures a colour landing, +2 % bytes; only worth it with step 4's recolour.
- **A11y after the gate** (r17-7): a crossing into ≤ 200 items rebuilds the tree (an in-place refresh saves ~35 of 380
  KB, −9 %, readers only); a reader's attach costs 46–58 KB inside the ask plus 68–89 KB of the client's walk, once a
  window; the gate never closes after a reader quits (closing it would make an idle reader's next key silent). The
  kept-label array sized by UI-thread Name asks is a headless-probe trap: a real client asks off the UI thread (+3 KB
  entering 20,000 files, against 469 KB headless).
- **The TokenizeDiff skip on paced keys** (r17-4): at a 400 ms pace the worker walks until the next key either way (28–31
  MB a key on both sides); step 3's win is per finished pass. Vesper's viewer re-colours on every open (12 MB on workers
  a 166 KB markdown open): a cache would help only reopening the same file.
- **Column lists** (r17-1, r17-4, r17-7): ~6–8 KB a cell scrolled in, as rows; hit-testing a reverse walk over ~186
  cells; no app uses columns or the cursor yet.
- **Reel's pixel-path video frame** (r17-2): 0.242 KB a frame (`SKImage.FromPixels`), on the pixel path only; mpv's GPU
  path keeps one wrapper an image.
- **Meridian typing in an 89 KB body** (r17-2): 9.4 KB a frame, the edited line's shaping and the TextArea's incremental
  bookkeeping.
- **Junction's sideways pan, the splitter's press and release, Meridian's send, the player chrome** (r17-6): 0.08 builds a
  pan step; 2 view builds a drag; Meridian's chrome does not build on a send; no player build reads the position.
- **Vesper thumbs and Tab** (r17-8): +0.7 KB a key; 204 KB a Tab on both sides.

From the round 16 surveys (details in `17-reports/r16-*`):

- **apps-gl-native-commit-jump** (r16-8 lead; r16-2, r16-3): NVIDIA's GL shader compiler on a disk-cache miss, not a
  commit. The cache (`%LOCALAPPDATA%\NVIDIA\GLCache`) is keyed by exe name; m156's programs were new on this machine, so
  each new exe's first launch compiled them: +53–59 MB working set kept for that process plus one long flush (23.5–28.8
  ms), once per program per machine after a Skia or driver update. Repeats (ABBA) and five bisect builds equal with a warm
  cache; `__GL_SHADER_DISK_CACHE_PATH=<empty folder>` reproduces it (`__GL_SHADER_DISK_CACHE=0` is ignored on Windows).
  For PERFORMANCE.md: look for a `GLCache` file written in that second before blaming a commit; run a scene twice per exe.
- **layout-wide-step-two-frames** (r16-1, r16-6): a harness artifact. A `SetWindowPos` from another thread draws 2 frames
  on every grow step (shrinks 1); one posted with `InvokeOnMainThread` runs inside the drain's batch since c32c4b2e
  (step 2). On the window's own thread, outside a batch, every step draws 1 in Junction and Vesper (±120 px, ±4 px, a
  16 ms drag). For PERFORMANCE.md: make resize steps from a Win32 timer callback on the UI thread.
- **after-layout-callback** (question 51; r16-1): `Element.OnLayoutChanged` (7c0a3b79, the app team) is that hook; off it
  costs one static read a box written, on a thread-static read and a handler scan a box write plus a
  `ConditionalWeakTable.Remove` an unmount, 0 B a frame.
- **a11y-commit-tab-trailing-frame** (r16-7): an a11y-only pass that draws nothing since a86de0d1; gone under the gate
  when no client asks; with a reader it is the designed trailing bounds refresh.
- **junction-chromeparttests-load** (r16-6): no recurrence in rounds 13–16; Junction.Tests 1,161/1,161 twice under load.
- **junction-gitwatcher-burst-test** (r16-6): closed by f5bb3133 (the debounce on a ManualClock; `Forget()` drops the
  warm-up callback).
- **HarfBuzz 14.4's cold words** (r16-5, r16-8; plan 18's +193 ns a cold Segoe UI word): every second visit makes 0
  calls; first visits ≤ 1.0 ms once (the rich text editor's open), ≤ 0.10 ms at Junction's start, below the start-up
  bar; Consolas and the CJK UI faces carry 85–100 % of an open's cold calls (not timed). Corpus hashes and app counts
  equal to 8.3.1 (13 of 724,228 widths shaped instead of read). A kept shape plan a (face, script, direction): 0.03–0.95
  ms once a process. Whole-run shaping: Latin calls −77…−84 % but 2.4–2.9× the characters; in place of the word cache a
  Junction drag step would shape ~72k characters against 1,139 (64×); Common-script words would take their run's script.
- **Element size, +16–24 B an element** (r16-4): 943610c7 (`_accessibleEnabled` over an 8-byte boundary, +8),
  41e2775e (`InheritedStyle.CursorImage`, 72 → 80 B), d43b3518 (`MeasureCache._maxContentWidth`, +8): Junction History
  +3.1 % tree bytes, Vesper +3.2 %, +0.3 % of the heap; the walks' +1.3 % and the suite's +2 %. Packing the bools into a
  flags byte, the float into padding and the cursor image into a side table: −20 B an element, below every bar, medium.
  For PERFORMANCE.md: every Element field is paid on every element of every build; a bool is free only while the byte
  fields have room, and a 4-byte field in an embedded struct can cost 8 (check `GetUninitializedObject` sizes).
- **Kiota's model parse** (r16-4): ~40 KB an item, 25× the JSON, half of it `BaseItemDto.GetFieldDeserializers()` (155
  setter delegates and a dictionary, generated); once a page under one gen-0 GC. Kiota 2.1.6 is −5.6 % a request.
- **A Skia scratch purge after a tween on GL** (r16-3; r16-2's option (d)): NVIDIA keeps the memory in its own pool and
  reservations; sound only on Vulkan (`PurgeScratch`).
- **The posted-work drain's budget in the real Junction** (r16-6): 0 budget stops in 109 passes, at most 2 actions a
  pass; nothing to tune.
- **033a900c in the apps** (r16-6): 0 host-against-host reconciles in all five app suites.
- **The rich text editor's resize, 248 B a block a step** (r16-5): TODO.md's 1,015 blocks 251 KB a step; the apps'
  editors hold short texts.
- **One provider per served item per tell** (r16-7): −0.36 KB a key, key moves only; optional inside step 3.
- **Keys in an empty Junction Commit tab** (r16-2): 16 of 20 identical frames (round 15's declined "keys the apps
  ignore"); Vesper's PageDown at the list end, Meridian's map keys: known.
- **Vesper with the same folder in both panes** (r16-4): 30 KB a key at 100, 1,000 and 5,000 files, flat.

From the round 15 surveys (details in `17-reports/r15-*`):

- **gl-ui-thread-rest-wakes** (r15-3): a sampling artifact. Round 14's driver read each rest's closing mark after the
  next phase's UI work had begun (every nonzero rest was followed at once by UI work; every rest followed by a sleep
  read 0). Exactly bounded rests read 0 UI-thread runs in all 7 passive rests; 900 WinEvents a launch from another
  process woke the thread 0 times. For PERFORMANCE.md: bound a rest window from inside, or sleep after its closing mark.
- **text-bidi-real-window-private** (r15-3, r15-4, r15-5): NVIDIA's write-combined GL surfaces made by a resize's frames
  (`PAGE_READWRITE|PAGE_WRITECOMBINE`, ~11.9 MB each, never resident; walk 1 +19.1 MB on jrepo, +37.4 on the Arabic
  file, walk 2 +1.3 / +11.6), bounded, there without Arabic text too, never on Vulkan: not a leak, not c06aa102's.
- **mpv-cuda-context-at-rest** (r15-3): the CUDA thread (10.4 wakes a second, beside NVIDIA Vulkan's 115) exists only
  while a player exists and is gone after the dispose; libmpv loads every interop with its render context, so a
  software-decoded clip pays it too (~313 MB commit, ~104 MB resident while open); real films decode through NVDEC and
  need it. Skipping it for software streams needs the codec known before the render context is made.
- **gl-reel-paused-driver-threads** (r15-8): a paused Reel's 98 wakes a second are the NVIDIA Vulkan device's own
  polling threads (Reel's empty player rests at 115 a second, r15-3) and the CUDA thread above: nothing of ours to
  change.
- **vk-format-switch-chained** (r15-3, env-gated A/B of the destroy-first test): chaining the F16 → 8-bit switch saves
  ~30–230 ms once a process (create 339.6 / 136.0 → 13.4 / 12.3 ms, then a ~96 ms first present) and costs ~90 ms on
  each later switch off (~110 against ~20 ms) and ~95 ms on each later switch on (first present 93–102 against 0).
- **text-junction-first-visit-tokenize** (r15-5): a warm ~340-line C# file allocates 1.6–2.5 MB (4.7–7.3 KB a line);
  ≥83 % of the allocation-tick samples are TextMateSharp/Onigwrap types (Onigwrap.Region 12 %, OnigResult 9.5 %,
  OnigCaptureIndex 9.4 %, StateStack 7.1 %) plus Int32[] 27 %; HultUI's TextSpan[] 2.9 %. HultUI already uses
  TokenizeLine2 and stops at the last shown line: what is left lives in the dependency.
- **junction-files-head-key** (r15-6): not reachable. The Files view browses `_selectedCommitHash ?? "HEAD"`
  (Files.cs:92) and History selects a hash as soon as the log lands (History.cs:428–436).
- **A 16 MB width cache for BiDi probes** (r15-5; r15-4's Arabic cache lead): Junction's Arabic drag at 960 80.8 →
  59.1 MB (−27 %), at 1440 −7 %, keeping +8.8 MB; Meridian's drag −9 %; the drags still miss 209–458 new prefixes a
  frame (no cliff); the remaining ~1.1 MB a frame is RTL shaping without the word cache, declined in round 1.
- **The scrollbar tween's mid-curve identical frames** (r15-2): 4 of 28 at 200 Hz × 1.0, 1 of 18 at 120 × 1.0, 0 at
  120 × 1.5; telling them apart without drawing would also drop frames that change pixels.
- **Keys the apps ignore** (r15-2): Meridian's Down on the map, Reel's empty player, Vesper's PageDown at the end draw
  same-pixel frames (the root-handler cause of step 2 (c)) but are rare.
- **UIA `Rebase` over the whole tree per apply** (r15-7): ~1,100 dictionary lookups and 0 B an apply. **Meridian's
  palette typing** 3.9 KB a11y a key (its part). **Vesper's breadcrumbs renamed by position**: a handful a pane.
- **The watcher's per-window limits** (r15-6; `_ask` 256, `_waiting` 4,096): 1 reload per cold build of the
  103-project solution, nothing on incremental builds. **Per-file reads coalesced** (`R15_FILECOALESCE`): only 0–2
  reads dropped in a real window, and the naive version showed the kept diff on 12 of 20 first frames.
- **Folding GetCommit, GetCommitFiles and GetCommitDiff into one git run** (r15-4): −2 of ~10 runs and ~2 % of a key's
  bytes. **Palette open/close retention**: +20 KB over 40 toggles.

From the round 14 surveys (details in `17-reports/r14-*`):

- **media-interop-gl-preload, built and measured (round 14 step 8; DECISIONS.md 0ba99050):** with the opt-in, a Vulkan
  interop window loaded the GL driver on a worker after the device. NativeAOT probe host built as each app's Main, the
  4K at 150 %, before (104179d2) and after alternated, idle-gated: Reel opening a film at rest Open → interop ready
  96.5 → 44.0 ms but → first video frame only 196.5 → 177.3 ms; Aperture's first playback 96.6 → 46.1 and 236 → 230 ms;
  Reel started with a film 177 → 162 ms. Only 11–21 ms reaches the frame because mpv's GL context creation takes 76–88
  ms instead of 49–62 when the driver was loaded seconds earlier (a UI-thread load at device creation was as slow).
  Cost in every session that never plays: one more NVIDIA GL thread waking 10 a second for the process's life (113 →
  123), Reel's empty player 108 → 127 MB working set and 106 → 132 MB private, Aperture before any playback 143 → 161 and
  153 → 181 MB. Reverted; the patch (with Aperture's opt-in, no tests) is `r14b8\gl-interop-preload.patch` in session
  41943a9b's scratchpad. For PERFORMANCE.md: judge a driver preload by the frame that uses it; count its at-rest threads
  and memory in sessions that never use it; `SDL_GL_LoadLibrary` on a worker pumps nothing there.

- **Closed from Waiting:** junction-git-optional-locks (6: a content save makes `git status` write the index 0 times in 8;
  with `GIT_OPTIONAL_LOCKS=0` files rewritten with the same bytes stay stale, 192 entries and 2.6 MB re-hashed by every
  summary status; Changes' `git diff --name-status` writes the index anyway; the staging miss is question 58 instead),
  junction-commit-first-visit-frames (1, 8: 1.00 frames a key at 33 ms; at a reader's pace the second frame is the
  colours landing, by design), a11y-label-under-named-button (7: none of the apps' 11 buttons whose label changes does
  so without its card's structure).
- **Layout (1):** keying Junction's diff rows so the colours landing patches them (text measures 137 → 4, but 299
  patches and 312 → 320 KB, +2.5 %); wheel scrolling (1 frame and 8–14 KB a notch; a touchpad's 1/8 notches are
  coalesced to the display rate, 4.6–4.7 frames a notch at 120 Hz); a DPI change (3–4 frames, 0.6–1.2 MB, once a move
  between monitors).
- **Painting (2):** a press on selectable read-only text draws an identical frame (TextElement.cs:354 marks
  unconditionally, and the focus clause would need to know the text draws no focus); Junction's render-phase bytes on
  keys are row realization (97–99 %; paint calls 0.1–0.2 KB a frame); GPU memory at rest (Aperture 11.1 MB after the
  purge, the glyph atlas one 4 MB page: nothing left to purge); hover-style frames with identical pixels (Meridian's rail
  4 in a 1,668-move sweep).
- **GL and Vulkan (3):** extending the Vulkan start-up preload to `vkCreateInstance` (ceiling ~20 ms under the loader
  lock; round 7 measured the driver-load half at −5.2 ms; not without a timed prototype); holding 1 ms through playback
  (~1.4 hint changes a second, ~30–60 µs); the `SurfacePurge` wake (1 a run of size changes).
- **.NET runtime (4):** a git process start costs 27.7 KB (`Run`) / 31.4 KB (`RunAsync`) on all threads, `Process`'s
  stream buffers (only own pipes would avoid them); `_lastCuts` holding one closed document's text (at most one
  string); `SelectRepo`'s 180–240 KB on the UI thread (rare); culture-sensitive string calls elsewhere in `src` (none in
  hot paths).
- **Text (5):** making natural words at a start's second sight (new widths −25…−30 %, drags +14…+33 %); ending natural
  words at the ASCII change scanned from the table's end (several words a start; replaced by step 1 (a)'s rule); capping
  natural-word length (they average 22.7 characters); Korean (the plain path; drags 738–744 KB).
- **Reactivity (6):** AsyncLoader coalescing (let the in-flight result land, then start the newest key): Changes under a
  0.4 s churn 266 → 235 git runs but 30 → 78 builds, 26 → 68 frames, 1.8 → 8.1 MB UI; dropping superseded results is the
  cheaper rule.
- **Accessibility (7):** the wheel (1 ScrollPercent raise, ~0.3 KB a notch); the review editor's formatting bar enabling
  at once (one whole tree an editor focus change, rare; tellable only by changing the tree builder); History search
  renames (~46 Name raises a key; a11y-items-on-demand-sooner would cover them); Reel playback (≤ 4 range raises a second by
  design); d73b1439's handler checks (0 B).
- **App workloads (8):** the long "other" frames after a ref move are the bridge, not a timer tick (18.8–23.6 ms at 1 ms
  resolution, gone with `HULTUI_A11Y=0`); Vulkan rest wakes and timer resolution (NVIDIA's).

From the round 13 surveys (details in `17-reports/r13-*`):

- **Closed from Waiting:** size-change-restore-second-frame (1, 3: `SetWindowPos` from another thread draws 2 frames a
  grow, round 5's trap; from the window's own thread, where drags, maximize and snap come from, 1; 9 KB, 0.3 ms render);
  F16 kept drag targets (3: F16 is on only while an HDR video is on screen, ~2 KB a drag frame then);
  meridian-hover-frames-real (2: the probe's pointer column sat in the map list's scrollbar band, whose 0.12 s expand
  tween draws 16 frames at 120 Hz, 25 at 200 Hz, 3 headless); a11y-structural-walk-during-resize (7: the walks come at a
  drag's first step, whose bounds are final, or at one breakpoint rebuild: 0 saved); aperture-cursor-guard-textarea (6:
  the only TextArea is caught at ApertureApp.cs:500, which tests both types).
- **The Vulkan apps' ~112 rest wakes** (3, 4, 8): four NVIDIA driver threads in nvoglv64 (~97, 10, 4 and 1 a second),
  the busiest a 10 ms poll at the 1 ms resolution the device itself holds; a windowless console process with only a
  Vulkan device shows the same. Not ours; unchanged by present mode, OBS's layer, a11y or SDL's hint. Aperture and Reel
  on GL until a video plays would rest at ~10 a second, but that is a runtime renderer switch against the Vulkan-opt-in
  decision.
- **FIFO for 8-bit Vulkan** (3, 8): adds NVIDIA's DXGI threads (97 + 25 a second) and +37 MB local; MAILBOX stays.
- **Replacing the VkSurfaceKHR at the switch to 8-bit** (3): NVIDIA keeps its DXGI path (identical threads and memory);
  only a device rebuild is left (question 54).
- **SDL's 1 ms timer at rest** (4, 8; expert 3's step 8): no own wake saved in any app; NVIDIA Vulkan holds 1 ms anyway;
  the machine-wide effect is unmeasurable here. Kept as step 8 only under question 53.
- **8-bit against F16 paint cost** (2): identical draws, layers, atlas and image textures; F16 adds a 22.8 MB offscreen
  and larger swapchain images. GL's FBO 0 stencil (11.12 MB at 2160×1350) is accounting only, flat.
- **Capping short UI tweens below the display's rate** (2): frames scale with refresh (a 0.12 s tween is 15 frames at
  120 Hz, 24 at 200 Hz); a cap would change smoothness on high-refresh displays.
- **A bigger or 2-way fallback front table** (5): misses are 0.5 % (ja) and 1.6 % (zh) of drag questions. Rank CJK work
  on the real ja/zh fixtures, not round 12's generated one (its 11k new words a width thrash the 8,192-entry word cache
  on their own, hiding step 3's effect: −2 % there).
- **Skipping property raises for elements no client was ever given** (7): ~140 of a resize's 142 raises, ≤ 1,963 calls
  and QIs once a resize after the debounce, 0 managed B; below every bar (a finer form of question 50).
- **67fc0884's label array growth** (1, 4, 7): 24–40 B an item once an append, only with a reader (`GetItemLabel`
  already resized so). **Wrapper lookup for items served on demand** (7): one uncontended lock and 2 map lookups, 0 B.
  **Native pattern objects** (7): 70.4 KB native a 400-item warm walk, paced by the client, unchanged.
- **Bytes per pointer move that draws nothing** (1): 0.15–0.85 KB on all threads, no frame.
- **Junction History 1.10 frames a key with a pointer** (8): the hover tint really follows the scroll. **Aperture's
  5-frame bursts** (8): posters landing, by design.
- **Empty builds** (6): 0 in 31 headless walks of common actions in all five apps. Watcher misses both versions share
  (index-only changes, a linked worktree's own switch) heal at the next event; pack-refs, gc and config reloads are
  rare.
- **apps-junction-drag-a11y-bytes** (8's lead): the bridge's third of Junction's drag-step bytes is the one structural
  walk at the drag's first step (327 KB, final bounds); closed with a11y-structural-walk-during-resize.
- **Word cache entry size** (4): 56 B × 8,192 = 458,776 B, 65 KB under the LOH line; a 64 B entry would put the array on
  the LOH (keep in mind when the entry grows).

From the round 12 surveys (details in `17-reports/r12-*`):

- **terminal-noop-scrollback-wheel, terminal-row-strings-by-content** (2; closed from Waiting): no app references
  HultUI.Terminal and nothing in it changed since round 10 but the target framework; under "real apps first" no case.
- **vesper-folder-nudge-stale** (6, 8; closed from Waiting): real window, 1 of 40 keys stale at a 33 ms repeat, 0 of 10
  at 300 ms (~0.025 view builds a key, 0.3 % of 282 KB); 0 of 71 frames in the nudge's phase at 100 ms. Headless counts
  59 of 60 stale because the pump runs the posted nudge right after the next key's load starts. Only a 70–370 ms
  listing (a slow share) could pay it.
- **junction-held-key-bigfiles** (8; closed from Waiting): in real time at 33 ms 948–1,149 KB a key on all threads and
  0.10 git runs a key; round 11's 2,367–3,110 KB came from the headless clock. A new file at a 100 ms pace is tokenized
  once (~14.5 MB on the worker), by design (HighlightRest 120 ms).
- **The first F16 replacement's retained memory** (3): +26 MB DXGI non-local, +4.9 MB local, ~+50 MB private 15–30 s
  after one maximize, snap or drag in Aperture and Reel; it plateaus (later resizes ≤ +2 MB); driver-side (8-bit
  returns everything; a rebuild made earlier only moves it into start-up, round 4). Folded into question 49.
- **A resize step made from another thread** (1, 3): a grow draws 2–3 frames (WM_PAINT after the live frame's
  `ValidateRect`), the window's own thread 1; round 5's trap, real only when another process resizes the window.
- **Skipping the second draw of a growing step** (1): built behind a probe switch, not run: no real-app case.
- **GL resource-cache purging, a persistent program cache** (2): budget flat 8–9.4 MB over every interaction and 18
  resizes; 8 lazy program compiles a Junction session, all before the first key; SkiaSharp 3.119 has no API.
- **Resize bytes a step** (1, 3, 4): 6.6–46.5 KB on all threads, re-layout at each new width (caches miss by design),
  0 GCs in 40 steps; Vesper's 15 KB median drag frame (91 KB in WinSxS) is layout at each width; between two widths
  716/685 MeasureSize; rare.
- **Junction History's compact breakpoint** (1): 17 rows remade, 300–421 KB a crossing; the rows really change there.
- **Resize breakpoint rebuilds** (6): 2–4 crossings per 48 width steps, each changing what is shown; 0 on height steps.
- **Start-up proofs** (5): ~440 of Junction's 653 first-frame HarfBuzz calls are digit and mono-ASCII proofs, once per
  process.
- **"Last fallback face if it has the glyph"** (5): not exact (the OS picks another face for 4,054 of 16,931 ideographs
  in ja, 4,249 of 16,740 in ko): pixels would change.
- **Partial-word shaping in wrapped Latin text during a drag** (5): 18 HarfBuzz calls a frame.
- **OS fallback misses in a CJK listing** (5): Vesper 71 + 383 calls, once per page visit.
- **The UIA thunks' managed bytes** (4): ≤ 21 B a raise. **NativeAOT's thread pool** (4): workers retire on their own.
- **Building the a11y tree after the first present** (7): Junction's first tree is 103 nodes, +182 KB; unlikely to reach
  10 ms.
- **A label delegate made anew each build** (7): it skips the off-screen rename check, but no app does it.
- **IRawElementProviderAdviseEvents on the root** (7): UIA never asks for it, so a provider cannot learn "nobody
  subscribed" that way.
- **Spinners at the display rate** (3): none ran in any real launch. **GL drag frame bytes** (3): 1.9–2.4 KB, the
  per-size SKSurface, drag only. **F16 kept drag targets** (3): 2,000 B a drag frame, ~64 KB a 32-step drag, drag only.
- **More start-up frames in a real window** (2, 5, 8): Junction 13–14 frames to settle against 5 headless — loads
  landing in real time, drawn from the caches with 0 extra shaping.
- **Folding the hover settle into a breakpoint's resize frame** (1): 1 frame a crossing with the pointer over the list.

From the round 11 surveys (details in `17-reports/r11-*`):

- **layout-ordered-children** (1; closed from Waiting): Meridian's map, the one app case (618 hit visits a move in round
  10), now 40.5–42.5 at every size; apps ≤ 42.5 (Junction changes wheel 33.5–36.2); only suite rows remain.
- **The map selection drop without the list's layout mark** (1): −46 ComputeLayout a click (41 of them cache-clean), 0
  MeasureSize or KB; risks a list whose width comes from its rows. **Keeping the list's measure cache across a
  render-time `ReplaceChildren`**: 0 saved. **Measure-cache capacity**: fits once forced heights collapse; not raised
  without a case.
- **Paint on .NET 11** (2): `richtext/open 5,000 blocks` +7.2 KB render (+0.3 %, first open, the paragraph layout in
  render); sub-blob misses after a relayout keyed by run object (only the synthetic 600-span row pays, 223.6 a key;
  realistic rows 3; 0 managed bytes); pixel-route frames 248 B (surfaces without GL only); 7ca2b3db's second frame per
  width-changing step with a pan bar (merges into the next move's frame in a paced loop).
- **GL/Vulkan on .NET 11** (3): no tier-1 or PGO allocation in frame, present or resize paths (every steady round 0.0 B);
  an app element unmounted holding its own `IGpuImageSource` would turn 7e964aeb's gate off for the process (64–65
  frames a second on a held edge with a post), but no app or sample has one (GpuFrameSink.cs:58–59 documents it);
  other-thread bytes 0.5–0.6 B a frame.
- **.NET 11 JIT boxing patterns** (4): a nullable non-int struct boxes 24 B a value in a loop through
  `HashCode.Add/Combine`, a generic `v is null` / `v?.GetHashCode()` on `T?`, or a `Dictionary<(int, W?)>` lookup; single
  calls, `int?`, `EqualityComparer<T?>`, record structs with nullable fields, `HashSet<W?>` don't. None left on a hot
  path in src or apps (c99e23bf was the one). AOT 11 boxes only the generic `IsNull<T?>` loop (unused).
- **Text alternatives** (5): a range-key struct with the default comparer, 25 ns a lookup on both runtimes (slower than
  .NET 10's shape); a non-randomized word hash (Marvin 9 % of the walk; a crafted document could chain a bounded
  cache); the width cache's dispatch alone (595 lookups a step, ~7 µs).
- **Reactivity** (6): `WeakElementRef` derives from `WeakReference`, so each kept key is finalizable (one finalizer run
  and one extra survival when pruned; once a key, not per build); duplicate dependency reads (Junction 10 reads, 3
  distinct; lists reused, 0 B); Vesper folder in/out's 4 builds a key are chained (navigation, listing, preview part,
  nudge), one batch would not merge them; `AsyncLoader.Start` in Aperture's build (once a process).
- **Bridges on .NET 11** (7): UIA identical on .NET 10 and 11 in JIT, TC=0 and AOT (a label edit 0/80/80 B off/tree/UIA;
  a client walk 10.6 B a node; a served item 104 B); AT-SPI over a real D-Bus ±10 B a signal (PropertyChange 188 →
  179–181 B); our `CallMethodAsync` +64 B a call (start-up and status changes only).
- **Apps** (8): Vesper +1.5…+7.9 % and Junction −4.7…+0.5 % between rounds are the machine (the same binary reproduces
  them across days); Junction bigfiles +0.4 % same day; Junction Changes Down over 300 files 294 KB a key, 83 % first
  draws of 26.2 new diff rows; Meridian find typing 59.1 KB and History search 50.0 KB a character (by design); Vesper
  folder in/out 288 KB once per Enter (55 % first draws); Meridian palette with a reader "frameOther" 17.3 of 32.2 KB
  (the overlay, flat since round 8); Aperture Discover Down 0.67 builds a key.

From the round 10 surveys (details in `17-reports/r10-*`):

- **paint-text-lines-seek** (2; round 10 step 14, built and measured, 9f76ecb6 DECISIONS.md): binary search for a long
  label's first visible line (≥ 32 lines, tallest line and sorted flag on the layout, +8 B, exact): `text/wheel over a
  2,000-line plain label` min JIT −2.9 / −4.4 %, AOT −3.7 / −3.1 % (A/A 2.4 % JIT, 0.8 % AOT), rich-text row ±0.5 % / −3.9 %;
  no allocation moved. Real but under the 5 % bar (7–10 µs a step, ~4–5 ns a line); revisit for 20k+-line texts drawn
  every frame. Patch kept in the session scratchpad `r10b14\text-lines-seek.patch`.

- **Steady-state render bytes in the suite** (2): all VirtualList realization in the render pass by design (`TableView
  10k sort` 234 KB, typing beside a 150-line diff 249 KB, `list 100k replace` 143 KB). **Effects** (2): shadow and
  gradient tiles 0 cache misses, 0 B; layers only in scenarios built for them. **Image memory** (2): Vesper decodes at
  target size; `SkiaImageSource` shares pixels. **TextArea draws visible rows only** (2, 5).
- **Bytes per video frame** (3): 14.6 B JIT / 12.2 AOT on the UI thread, all from mpv's 9 `time-pos` changes a second
  (`MediaPlayer.Apply`'s Batch closure, MediaPlayer.cs:276); render 0 B; under the 0.1 KB target. **GPU memory left after
  a web view is disposed** (3): GL +16.3 then +7.5 MB, Vulkan +21.3, flat after; NVIDIA's GL driver pool (reused, not
  Skia's cache, predates 58aeb965); no framework lever. **F16 vs 8-bit GPU memory** (3): decided in round 3.
- **`Secrets.Resolve`'s variable name** (4): ~1.4 KB of a 47.1 KB drag frame (3 %); goes with step 13. **Hover settle
  under a wheel** (4): 0 B added. **The lent `TreeDiff`'s kept capacity** (4, 7): bounded, holds no node after `Clear`.
  **`SaveMap` writer's int-to-string** (4): off the UI thread.
- **The label cull loop's O(lines) checks** (5): a few µs of a 0.24 ms frame, 0 B; expert 2 proposes the seek (step 14,
  measure first). **HultUI.Syntax's per-line share** (5): 2.0–8.2 %, the span arrays it returns; TextMateSharp 92–98 %.
  **`TextPaintBounds`** (5): stops at scroll views. **bf74fbca's second registries** (5): +378 / +680 KB once per theme.
- **Vesper name prompt, Meridian dialogs, Junction MR title and composer** (6): already build only when something shown
  moves. **Discovery map at 2,000 endpoints** (6): ~35 B an endpoint a click, accepted in round 9 (step 11 removes the
  rest). **Junction History resize** (6): 33.6 KB a step, rows remade only at layout breakpoints. **Reel** (6): no
  typing or full build per key.
- **Bridges' whole-tree walks per structural apply** (7): 0 B, the window's walk is O(n) too. **History with a reader**
  (7): the 300 eager item nodes (on-demand items declined in round 2). **View switch at 600 endpoints with a reader**
  (7): UIA ~175 B a node, the lazy-wrapper lever declined in rounds 1–2. **Probe trap** (7):
  `HeadlessWindow.FindByAutomationId` builds a whole accessibility tree per call (+850 KB a click at 6,073 nodes inside a
  step); find elements once, outside the step — a guideline note.
- **Junction Files search Down's 96 TextArea measure misses** (1): O(1) each, 0 B. **ff6125a6 per frame** (1): one flag
  read, one ~7-parent walk after a change of children.
- **Junction diff find Enter +1.0 %** (8): bimodal noise over 13 runs in 6 trees. **Vesper Tab 92.2 KB**, **Meridian
  method pick 149.5 KB** (8): once per action, unchanged. **Reel playback 0.8 KB a frame** (8): the fake backend.

From the round 9 surveys (details in `17-reports/r9-*`):

- **junction-leads' old-side state convergence between hunks** (5): at best lines walked 82.2 → 68.2 % of new-side
  lines (−17 %) over 466 files, off the UI thread, an `IStateStack` compare per hunk; closed.
- **A long-line cap per grammar** (5): worker time only, DECISIONS.md declined a table; HTML's 2,000 bounds a minified
  inline script at JavaScript's cost (79 ms). **Counting non-ASCII by advance** (5): −25 % of one file's once-per-open
  cost (TODO.md), out of proportion. **Pretty HTML/XML** (5): worker, once per response, and it lowers the open bytes.
- **Lazy UIA wrappers** (7, again): 4.06 KB of 36.5 a reader-on Aperture grid Down (11 %), ~207 B a node added; declined
  rounds 1–2, no new scaling evidence. **AT-SPI cache add/remove per node** (7): push-based by design. **Meridian's new
  views with a reader** (7): 1–9 % over bridge-off, 0 trees.
- **RGBA16F interop image for an 8-bit page on Vulkan** (3): 13.5 vs 6.7 MB, 4.6 % of the process, Vulkan opt-in.
  **Closing the WGC frame pool on hide** (3): 0 MB in this process. **Web view open/close cycles** (3): GL +7.5 MB once
  then flat, Vulkan flat; browser processes exit with the last controller. GL drag frame 2,080 B and the driver's drag
  commit (3): as rounds 2 and 5.
- **Meridian find typing +8.5 %** (8, 2): the response Part 47.1 → 51.1 KB a character after main's Request tab and
  toggles; under the bar. **Response tabs** 53.1 KB a click, **Discovery** once per action (8). **Vesper transfer
  frames +21 %** (8): timing; no round 8 commit touches transfers.
- **Meridian dialogs** (4, 6): typing 0.22–0.59 KB a character, 0 builds; 200 open/type/close cycles 0 B retained.
  **Request tab `PrettyJson(sent.Body)`** (4): the sent body only.
- **Other row-level `Opacity` in apps** (2): Junction resolved review cards, clone list, dragged repo row; Meridian
  locked option rows, sending/stale response, runner row; disabled buttons — transient or a few layers.
- **Bounded, not leaks** (4): Linux/macOS `KeyringMemo` (one entry per secret), Aperture's per-shelf `KeptElements`
  (max(64, 2 × live)), the artwork memo (1,024), Vesper `_ringMoved` (two cells), `Restyle` (a `VisualState?` struct, no
  delegate). **e7d10c63's `PositionFromPoint` / `TickSelectionEdgeScroll`** (1, 4, 5): O(depth) and 0 B; no idle wake.
  **Vesper `MoveRing`'s linear search** (1): at most what is on screen, 0 B.

From the round 8 surveys (details in `17-reports/r8-*`):

- **runtime-element-layout-state phase 2** (1, 4): elements made × ~70 B are 4.0–7.6 % of a key's bytes in every walk,
  retained −1.5…−2.3 %; round 7's builds made fewer copies. Under both bars; closed.
- **layout-patch-keeps-layout-dropdown-expander** (1): a Fluent rebuild lays out 16 and 9 elements; closed.
- **Palette rows follow their item** (1): Junction palette type+erase −7.9 %, suite −1…−2 %; would change the documented
  `palette-row-N` automation ids.
- **Junction Files' 149 measure misses a key, grid/edit's auto column, TableView column resize, Vesper same-names
  misses** (1): the relayout boundary (round 5), the scenario's design, no app, small.
- **Snapping text blobs' x fraction** (2, 3): wheel steps are whole layout pixels (40 at any scale), so only touchpad pans
  add cache entries; snapping x moves glyphs.
- **VirtualList realization in the render pass, History's affordance shader per key (0.7 %), palette Down's 3.7 KB of
  overlay text, canvas and halo text, disabled buttons' groups** (2): by design, under 1 %, or no app draws them.
- **efaf947b's pixel-frame size reads, `SaveFrame` readbacks** (3): once per video frame; only when a frame is asked for.
- **`UiaDisconnectProvider` per drop, `_nativeLock` per lookup, `LocalPosition` O(depth), the `AsyncLocal` ambient
  application, `KeptElements` retention (max(64, 2 × live)), a kept `TreeDiff`** (4, 7): ≤ 0.5 a key, 0 B, or bounded.
- **Junction big files' gen 2, the CRLF third copy** (4): 0–1 per 100 keys, background GCs; split without the `Replace`
  copy (JunctionApp.Files.cs:630) only if it ever matters.
- **A kept touched-rows array, a long-line P per grammar, 600-span typing, TextArea typing with a reader, emoji labels on
  the BiDi path, multi-font `LineSource`, richtext/open cache misses** (5): trades retained memory for churn (−6.7 % a
  Meridian send), declined in DECISIONS.md or round 1, or needed.
- **Framework batching of the main-thread drain or of `MarkDirty`** (6; the drain half SUPERSEDED 2026-11-15 by round 15
  step 5, c32c4b2e, question 62 (b): an audit of apps/, samples/, src/ and tests/ found no such site): an await continuation that writes and then reads
  would see the old tree, in 5 apps; the two app sites (step 4) get the same win. **Junction Changes' diff pane as a
  `Part`** (6): ≤ 13 of 376 KB (−3.5 %). **Kept Brief columns** (6): gone with step 7.
- **A node-with-children's own change in place; the bridges' whole-tree pass per structural apply** (7): ≤ 4.5 % of a
  stage, +6 % on caret keys after 4+ stages in a row; a constant factor, 0 B.
- **Junction Changes' caret at 300 files** (6, 7, 8): 83 % render of the reached file's diff rows, which are needed; build
  12 %; the reader 4.5 % of a stage.
- **Commit view's all-thread rise after 3d54d779** (8): the grammars' first use moved from History's prefetch to the
  Commit tab (bisected; the second stretch 285.6 → 293 KB, GCs equal). **Junction Files all-thread +7.5 %, Meridian's
  method pick, History search's chrome** (8): off the UI thread, once per action, 1.7 KB.

From the round 7 surveys (details in `17-reports/r7-*`):

- **vk-startup-preload** (3): a worker loading `vulkan-1.dll` before `SDL_Init` cuts SDL's load 73–82 → 2.5–3.4 ms but
  grows `SDL_Init` 15–17 → 44–55 ms (loader lock); AOT min −5.2 ms, under the 10 ms bar.
- **Two same-size redraws after the first frame** (3): Windows' post-show WM_PAINT runs `OnWindowResized` at an
  unchanged size, then a frame after MOUSE_ENTER; after the first present, once per window.
- **GL `BeginGraphicsPreload`, `opengl32.dll` in a Vulkan window** (3): no new evidence; a one-off DLL load.
- **atspi-kept-tab-retire alone** (7, 4): one map operation instead of two saves ~1 % of the step; the `Count` locks
  are the cause (step 1).
- **Rebuilding only the changed container's a11y subtree** (7): −10 % Changes, −2.4 % Aperture; the in-place walk is
  step 7.
- **runtime-element-layout-state phase 2 alone** (1, 4): ~70 B of cold fields per element, −2…−9 % a key (Waiting, low).
- **Sticky rows' second O(n) pass** (1): ~300 calls a key at 300 files, 0 B. StickyRows twin cost +0.3 KB (+3 %).
- **Changes wheel at 300 files** (1, 8): the approved trade; breaks even at ~220 notches per arrow key, a mixed
  session −92 %.
- **The drawn line's second pass after lazy runs** (5): at most +2.6 % time; wheel rows 3.3–4.7 KB/step.
- **BiDi rows' `GlyphPosition[]`, text/create, a richtext/open closure, tokenize vs file size** (5): declined RTL
  cache, element size, 3.5 % under the bar, flat per line from 500 to 4,000 lines.
- **`Canvas.LocalClipBounds` per paragraph, steady render bytes, layers, rounded clips** (2): flat by depth; 0 B;
  opacity only; GPU work.
- **Coalescing `MarkDirty` in a dispatch** (6): doubles happen on one-off actions only. P5a stands (reconcile 9 % of a
  Meridian URL key at most).
- **Retained +470 B a dialog cycle** (4, round 6's question): the dialog's label names the cycle, so the text caches
  keep a new string each open; a constant label is 0 B over 1,400 cycles. Bounded caches, not a leak.
- **`TokenizeDiff`'s cancelled `Task.Delay` throw, `AtSpiServedItems` key copies, kept-tab object pooling** (4): under
  0.1 % of a key; only when a served list shrinks; no app keeps tabs.
- **Vesper Tab (102.8 KB, 93 % build), Meridian find's search (24 KB a character), Junction big files at 33 ms** (8):
  once per action; needed; timing, not a leak.

From the round 6 surveys (details in `17-reports/r6-*`):

- **meridian-history-aot-p90** (4, 6, 8): two regimes, not a runtime effect — the walk's first ~22 keys have the
  selection on screen (64 made, 16 laid out, 63 KB/key), later keys don't (48 made, 0 laid out, 46 KB; the total
  comparison's "same bytes" was off by +16.6 KB/key); the slow stretch moves between reps in both trees with identical
  counts. It exposed meridian-history-selection-offscreen (step 7).
- **palette-row-keys** (6): keyed by `Group|Title|Detail`, bytes +3 % bridge off and +11 % UIA, trees 0 → 0.75 per step
  (reordering kept rows is structural), patches 122 → 253. Replaced by palette-virtual-rows (step 9).
- **live-frame-requests-public** (3, 4): no consumer; 2a332218 draws a posting source at the timer's rate.
- **apps-tokenize-on-ui-thread** (5, 8): no tokenizer share on the UI thread in History, Files or Commit.
- **a11y-box-selected-patch** (7): the only producer is a tab switch, which also swaps a 901-node panel.
- **a11y-in-place-fallbacks** (7): 4 of ~1,150 refreshes; the composite grid's are real structure changes, kept to 20
  trees per 120 notches by the backoff.
- **Button `IsEnabled` flip, tab-switch wrappers, Junction History search tree** (7): once per user action, or under 5 %
  of a key; the AT-SPI part falls to step 15.
- **RowHeights height function per rebuild** (1): 20,000 calls, 0 B, array lookups; skipping needs an API.
- **Flex-shrink heap arrays above 64 items, hit-test enter/leave allocation** (1): 0 calls; 0.1–0.24 KB a hover step.
- **Keeping pool threads alive; the GL per-frame driver spin** (3): the walk never recurs; the spin is `glGetError` under
  NVIDIA's threaded driver (DECISIONS "One submit per frame").
- **`BoundingRect` O(depth)** (4): app trees are depth 2–3 (23–83 reads per hit test).
- **Retained heap over open/close cycles** (4): growth is the text pipeline's byte-bounded caches and the blob map; no leak.
- **Reel headless 0.6–0.7 KB per frame** (4): the fake backend and virtual clock; the real AOT window reads 90–111 B.
- **Rich document `Blocks` copy, BiDi `GlyphPosition[]`, emoji itemize substrings, an exact run array** (5): public API,
  a declined RTL word cache, lazy runs (step 14), or −1…−4 %.
- **Spanned-line composes, render-time cull visits, rounded clips, image decode, video pixel path** (2): needed anyway,
  layout-ordered-children, GPU-side, already bounded by every app, or 248 B a frame.
- **Mounts without unmounts** (6): patched-away twins count as mounted; not a leak. Reconcile pooling (P5a) stands at
  2.4–8 % of a key.
- **runtime-element-layout-state** (4; build step 8, declined b35a81d8, docs/DECISIONS.md): layout state in its own
  object made Box 584 → 384 B and Text 656 → 456 B unlaid-out (+48 B laid out) and cut bytes a key −16 % Junction
  summary, −15 % Meridian URL, −12 % Meridian History, −4…−10 % Vesper/Aperture (retained +0.9…+1.6 %; audit identical
  in 163 scenarios), but the box read moved off the element cost `scroll/wheel 5,000-row list` +8…+14 % (flat with a
  16 B box copy kept inline) and `layout/resize 2,000-row list` +3…+5 % on AOT (A/A ±1 %; inlining the setter still
  +2…+4 %). Patch kept in scratchpad `r6b8/change3-final.patch`. Phase 2 lead: fields no per-element pass reads
  (focus, a11y-observer and overlay-host references, `OverlayOrigin`, Text selection; 50–100 B a copy) can move out
  at no time cost.

From the round 5 surveys (details in `17-reports/r5-*`):

- **paint-subblob-trusted-face-bounds** (2): Consolas −10.5 % glyphs (≈ −2 % frame by round 3's ratio), Segoe
  UI/Arial −30 % but no app draws dense colour in them; 81 → 515 `GetGlyphWidths` per rich text keystroke.
- **paint-drawspannedline-overlap** (2): no new span producer; a latent contract, closed.
- **Bitmap-strike faces** (Courier New Regular, Calibri) drawing whole runs per stretch (2): no app colours text
  in them; would need per-ppem bounds and a new pixel A/B.
- **reconcile-restyle-on-change (b)** (6): style allocates 0 B; resolves = patched + made; time only in a 1–2 %
  phase; the component splits remove the patches instead. Closed.
- **Reconcile pooling (P5a)** (6): reconcile bytes ≤ 5 % of a key (Vesper 8.8, Meridian 12.0 KB). Stands.
- **`MonospaceAsciiAdvance` on `IMeasureContext`** (5): Junction's and Vesper's own (n + 1) × advance rule already
  holds for their faces; no byte would change.
- **A VirtualList measured as a leaf** (1): resize misses −25 % but it moves geometry (Junction Commit text
  measures 15 → 60). FlexBasis(0) on Vesper's dual panes (−20 % resize misses) is an app guideline.
- **Relayout boundary** (stop at an element whose size is unchanged) (1): a large engine change; app lists are
  virtual (~30 rows).
- **RGBA8 interop image on 8-bit swapchains** (3): 8.8 MB of 600 MB GPU memory (1.5 %).
- **A video in a second window during a drag** (3): no app opens a second video window.
- **GL resize re-wrap** (3): FBO 0's size and flip must match; cannot keep.
- **TextArea document as one string** (4): see Needs the user, round 5 question 3.
- **Two loop passes per drawn frame** (4): a pass with no frame allocates 0 B; the loop's design.
- **Junction LOH at session end** (8): live data equal (68.0 vs 68.1 MB); LOH free space by GC timing.

From the round 1 surveys (details in `17-reports/`):

- **Layout:** MinContentHeight outside the cache; layout/resize 2,000 rows at the semantic
  minimum; wrap chips, baseline, aspect tiles; sticky; TableView sort; element size (P5c);
  VirtualList wheel realization.
- **Painting:** per-draw shadow/gradient/dash objects; run blob cache bound; per-label
  layout lookup; SaveLayer/clips; picture caching (damage-rect decision); canvas context.
- **GL/Vulkan:** GL cache limit/purge; FBO 0 without stencil (pixel risk); video GPU
  churn; start-up GPU work (no program cache in SkiaSharp 3.119.2); F16 MAILBOX base cost
  (decided); MSAA atlas resolve (paint-side).
- **.NET:** overlay and row-replacement leaks (none); icon raster caches; Junction
  highlight prefetch (TextMateSharp); Vesper KeyChord enumerator; LINQ in the frame path
  (none).
- **Text:** RTL word cache; plain path for emoji/ZWJ; layout cache size; ICU breaks;
  word cache bound; moving line starts out of Render; incremental rich text paragraph.
- **Reactivity:** batching of signal writes (works); effects on unchanged inputs (skip);
  generated `[Reactive]` code (no boxing); TrackRead dedupe; reconcile pooling (P5a); new
  tree ~930 B per element.
- **a11y:** `RuntimeHelpers.GetHashCode` lever (stale); lazy wrappers; PropertyChanged
  duplicates (none); idle/caret/hover (0 rebuilds); Tab focus; shrinking
  AccessibilityNode (later).
- **Apps:** style and non-realize layout shares; idle loop bytes; start-up (no new lever);
  Junction start-up allocation (app git loading).

- **Round 2:** layout-virtuallist-keep-rows (prototyped: +6 % bytes, the template is 88 % of
  realize); moving realization out of render; reconcile-persistent-child-in-place and
  reconcile-unkeyed-shrink-linear (no app hits them); gl-image-retained-after-dispose GL
  half (NVIDIA driver pool; guideline: decode at the drawn size); paint-cover-half-pixel-crop
  (quality, not perf); a11y-scrollpercent-role (no such use); lazy UIA wrappers; Vulkan
  driver-side private bytes; GL scratch purge after resize; skipping shaping of "short"
  lines (no proven bound); apps-junction-heap-growth (not a leak: bounded caches + garbage).

- **Round 2 build:** gl-vk-one-submit-per-frame (54c56d9, DECISIONS.md "One submit per frame"): GL glGetError 3 → 1 and F16 submits 2 → 1 per frame, but frame time only −0.8…−4.5 % (JIT/AOT) because the driver wait moves from Flush to Present; pixels identical; reverted. So round 2 question 12 (no-op glGetError) is moot.

- **Round 3:** gpu-aperture-startup-swapchain-x3 (the second swapchain costs 0.7 ms; dropped);
  Vulkan display-colour poll (~1.1 KB per poll, only without colour events; becomes a deadline in
  step 10); F16 vs 8-bit GPU memory (decided); base ScrollView's render-time extent rescan (only
  Junction Files pays; goes with step 14); Junction Files' TextArea leaf misses (no
  MeasureContent); style-pass allocation on mount; an identity-first blob lookup (+38 % bytes;
  sub-blobs remove the hashing); Aperture's one SaveLayer (disabled pager button); gradient cache
  misses while resizing; reconcile array snapshots (4.6–7 % of rebuild bytes, P5a stands);
  fading the VideoPlayer bar alone; a11y-videoplayer-layout-refresh (folded into step 15);
  a11y-atspi-richtext-tables alone (−10 %, superseded); `GpuFrameSink` closures (<1 % of Reel's
  frame bytes); `LruCache.Counted` half-bound (needs a label over 2 MB); keyed item rows'
  `_retained` dictionary; TextArea document as one string (rope changes the public `Text`);
  richtext/open layout-cache misses (served by memos); Command palette shape misses; flush share
  under a shared GPU (54c56d9); working set and start-up (no new lever).

- **Round 4:** componenthost-patch-keeps-layout (never patched: a persistent host is re-added);
  element-forced-size-nan (−1.3 % retained); a VirtualList measured as a leaf (1–2 %); MeasureCache
  evictions (TextArea Size.Zero; the diff list, covered); TableView column resize (no app);
  junction-watcher-self-reload (0 reloads in 44 real starts; the flake is the test fixtures');
  idle-wake-latency-p90 (OS resuming a long-parked thread; only waking often removes it — question 4);
  JIT vs AOT bytes (101/122 within 1 KB); native blob leaks (none over 200 cycles); TableView sort AOT
  placement; no cull above the first clip (suite artefacts); line cull in multi-line DrawText; Skia glyph
  cache limit (0.03–1.29 MB used); pixel-path video frames (needs more native functions); sub-blob map
  stale keys (bounded); blob-lookup hashing (+8 B per run); TextArea as one string; ShowLines version
  check; rich text GlyphPosition (retained trade declined); the log's unique-word shapings; SpansOf
  wrapper; persistent host moved by its parent (no extra a11y rebuild today); text caret moves in
  TextInput/TextArea; reorder signal cost; instance/device on a worker; extension enumeration; minimized
  walk with a video sink (≤12k visits/s); purge waiting while parked; F16 first swapchain replacement;
  extra F16 submits (54c56d9); WebView interop on resize (no app); Vesper viewer PageDown; Meridian
  (flat, text-open and components remain).
- **Testing trap (round 4):** DirectWrite text is not byte-identical between two draws in one process
  after Skia's glyph strikes are purged (A/A differed in 4–5 of 120 frames; 0 with a 512 MB cache) —
  pixel A/Bs on system fonts need an A/A control. It explains round 3's unexplained 1–4 pixel diff.

## Needs the user

No question is open from rounds 1–17. Their answered questions are in the archive
([round 17's 69–72 and 72b](17-performance-team-ledger-archive.md#needs-the-user-round-17),
[round 16's 66, 67 and 68, with round 15's 64](17-performance-team-ledger-archive.md#needs-the-user-round-16),
[round 15's 62, 63 and 65](17-performance-team-ledger-archive.md#needs-the-user-round-15), [round 14](17-performance-team-ledger-archive.md#needs-the-user-round-14),
[round 13](17-performance-team-ledger-archive.md#needs-the-user-round-13), [round 12](17-performance-team-ledger-archive.md#needs-the-user-round-12),
[round 11](17-performance-team-ledger-archive.md#needs-the-user-round-11),
[round 10, with round 9's 26 and the earlier 10](17-performance-team-ledger-archive.md#needs-the-user-round-10),
[round 9](17-performance-team-ledger-archive.md#needs-the-user-round-9), [round 8](17-performance-team-ledger-archive.md#needs-the-user-round-8),
[round 7](17-performance-team-ledger-archive.md#needs-the-user-round-7), [round 6](17-performance-team-ledger-archive.md#needs-the-user-round-6)). Search it by candidate id
before asking anything an earlier round asked. Recommendations in brackets. Question 50 (a11y-gate-on-client)
was answered "not yet", then **build** (2026-11-16; built in round 16); 51 (after-layout-callback)
"only if another app needs it" (closed by 7c0a3b79); 57 (text-open-measures-every-line, full) "not now" (Waiting);
72b (a file as its own F3 partner in Vesper) → (2), F3 does nothing there (Waiting vesper-file-not-its-own-partner).

Round 18 questions (from the survey, 2026-11-22). Recommendations in brackets. Where Back lands in Aperture (step 3)
is not asked: 0f9c8477 and History.cs already promise "back where you were scrolled"; step 3 keeps the scroll offset.

73. **The drag ghost's layer** (r18-2; step 11 (b)): Junction's repository drag (the sidebar reorder, the only in-window
    drag in the apps) draws its ghost through a window-sized layer that the GPU keeps for the window's life: +18 MB of
    GPU memory at the default size after the first drag (about 48 MB maximized on the 4K, by arithmetic). Bounding the
    ghost's layer removes it (−27 % of the window's GPU memory), but during a drag 23 of 29 frames differ in 2–5 pixels
    at the ghost's edges by up to 6 of 255 (the layer's origin moves; frames before and after the drag are identical).
    (a) Keep today's ghost. (b) Bounds, with that change. [(b): the same kind of change as questions 68 and 69, during a
    drag only.] Before/after images on request (headless, `PAINT_DUMPDIR`).
74. **Two swapchain images on Vulkan** (r18-3; step 12): Reel and Aperture hold 3 swapchain images; 2 save GPU memory
    all session (Reel maximized-sized −30 MB in SDR, about −180 MB in HDR playback; Aperture −17 %) with the same frames
    in playback, but in a window drag while a video plays about 40 % of frames wait up to one refresh (8.5–9.1 ms at 120
    Hz) against 2 of 57 with 3. The HDR (F16) drag with 2 images is unmeasured: it is the HDR held-drag run that needs
    your approval. (a) Keep 3. (b) 2 for the 8-bit swapchain only (SDR video and browsing). (c) (b), and one guarded HDR
    drag run in the build (an HDR clip on the 4K, a maximized-sized window, stopping on any stall over 1 s) to decide
    F16. [(b) now; (c) if you approve the run.]
75. **Keep the page Back returns to in Aperture** (r18-6): Back to Home or a library makes the whole page again (Home
    323 elements, 322 KB; a grid 336, 338 KB). Keeping that one page hidden while away (~330 elements, ~0.3 MB) would
    make Back about −80…−90 %, but goes against REACTIVITY.md's rule that a replaced view is let go (the Meridian leak).
    (a) No. (b) Keep one page. [(a) for now: steps 7–9 shrink Back's builds first; ask again with their numbers if Back
    still matters.]
76. **Aperture with a screen reader** (r18-7; not perf): (1) Escape from a library opened from the rail goes Back to
    Home, whose cursor was never set, so the keyboard focus goes nowhere and the reader stays on a gone element until
    the next arrow; (2) Enter on a card opens its page and the reader hears only "Play, button", not the film. (a) Back
    and Escape focus the page's cursor card, or its first card when there is none, and Play's name carries the title
    ("Play <title>"). (b) Leave both. [(a); a small app change that can ride with step 3.]
    → the user asked for images and the image diff first (2026-11-22): sent the three frames that differ most (f015,
    f021, f029: 7, 4 and 5 pixels, max 6/255, at the ghost's left edge, the status dot and the right edge) at 3× with
    the differing pixels marked, and a whole frame for context (`r18-painting\images\`, `ghost_images.py`). → **(b)**
    (2026-11-22, the user, after the images: "It looks fine after the changes so lets go ahead with it.").
    → **(c)** (2026-11-22, the user: "b and do the test"): 2 images for the 8-bit swapchain, and the one guarded HDR
    drag run in step 12 (an HDR clip on the 4K, a maximized-sized window, modal-loop steps, stop on any stall over 1 s)
    decides F16.
    → **(a)** (2026-11-22, the user): no.
    → **(a)** (2026-11-22; the user's second answer read "75: a", taken as 76: a — correct it if not): it rides with
    step 3 as its own commit.

77. **Aperture: focus on a page opened by a number key or the palette** (step 3, r18b3; not perf): after step 3, Back
    and Escape focus a card, but a page that has no cursor opened by a number key (Digit2) or the command palette from a
    film page still leaves the keyboard focus on nothing, as before step 3 (no focus event; a reader stays on a gone
    element until the next arrow). (a) Focus the page's cursor card, or its first card on screen, on every way into a
    page, as Back and Escape now do. (b) Leave it. [(a): the same rule everywhere; a small app change, its own commit
    with step 7.]
    **Answered (a)** by the user 2026-11-23 ("Yes it should"): its own commit with step 7 (r18b7).

Reminder (expert 8): Aperture signed in to jellyfin.home as device "aperture-survey" in earlier rounds, as "aperture-r13" in round 13 (and twice in round 15), and once in
round 12 as "aperture-r12" (again in round 15); the user should remove those devices and revoke the API key when the work is done.
**Round 16: both of expert 8's sign-ins (as aperture-r13) returned `library=none`, so Aperture's library was not
measured; the user should check that jellyfin.home and the API key still work.**

Standing notes (from earlier rounds):

- **Aperture API key file** at `C:\Users\fredr\.secrets\jellyfin-api-key.txt` (outside the repo; never
  copy or print its contents; path only, e.g. `--api-key-file <that path>` with the run-hultui-app driver,
  server `http://jellyfin.home:8096`). The user should revoke the key and remove the "aperture-survey"
  devices on jellyfin.home when the work is done.
- Rounds 1–4's questions and answers are in [the archive](17-performance-team-ledger-archive.md#needs-the-user-rounds-14).
  Search it (by candidate id) before proposing anything an earlier round already asked about.

## Open

Both items need other hardware (r17-3: this desk has no AMD or Intel GPU, and nothing in rounds 16–17 touched the
destroy-first path, SdlVulkanHost.cs:845, or the depth/stencil request); surveys stop carrying them.

- **8a38ef4 on Linux/KWin and on AMD/Intel Windows:** not measured. Linux keeps the chained
  path. If AMD/Intel do not stall, destroy-first may cost them 21–24 ms per resize, as it
  cost NVIDIA's IMMEDIATE and FIFO. A real Aperture resize run is also still to do.
- **4315c4b on AMD, Intel and Linux (GLX/EGL):** Linux answered in round 10 (r10-3): WSL Fedora has Mesa 26.2.3
  (EGL/DRI with llvmpipe and d3d12) since 2026-10-09, and there depth 0 plus stencil 8 resolves to 24/8: nothing
  fails and nothing is saved (Mesa has no stencil-only config; the gfx line, SDL3Window.cs:1675–1679, reports it). AMD
  and Intel on Linux use the same Mesa lists (inferred). AMD and Intel on Windows (WGL) still not measured. Depth is a
  minimum in WGL/GLX/EGL, so 0 cannot fail where 16 succeeded.

## Log

From 2026-11-19 (round 17's survey); earlier entries are in the archive ([to 2026-10-25](17-performance-team-ledger-archive.md#log-2026-10-07-to-2026-10-25),
[2026-10-26 to 2026-10-27](17-performance-team-ledger-archive.md#log-2026-10-26-to-2026-10-27),
[2026-10-27 to 2026-11-02](17-performance-team-ledger-archive.md#log-2026-10-27-to-2026-11-02),
[2026-11-02 to 2026-11-03](17-performance-team-ledger-archive.md#log-2026-11-02-to-2026-11-03),
[2026-11-03 to 2026-11-08](17-performance-team-ledger-archive.md#log-2026-11-03-to-2026-11-08),
[2026-11-08 to 2026-11-09](17-performance-team-ledger-archive.md#log-2026-11-08-to-2026-11-09),
[2026-11-09 to 2026-11-11](17-performance-team-ledger-archive.md#log-2026-11-09-to-2026-11-11),
[2026-11-11 to 2026-11-12](17-performance-team-ledger-archive.md#log-2026-11-11-to-2026-11-12),
[2026-11-12, round 12](17-performance-team-ledger-archive.md#log-2026-11-12-round-12),
[2026-11-12 to 2026-11-13, round 13](17-performance-team-ledger-archive.md#log-2026-11-12-to-2026-11-13-round-13),
[2026-11-13 to 2026-11-14, round 14](17-performance-team-ledger-archive.md#log-2026-11-13-to-2026-11-14-round-14),
[2026-11-14 to 2026-11-16, round 15 and the screen-reader fix](17-performance-team-ledger-archive.md#log-2026-11-14-to-2026-11-16-round-15-and-the-screen-reader-fix),
[2026-11-18 to 2026-11-19, round 16](17-performance-team-ledger-archive.md#log-2026-11-18-to-2026-11-19-round-16)).

- 2026-11-19: round 17 survey started in a fresh context (session 35903ed7; usage 65 % weekly, the week resets
  2026-11-22 03:00 UTC; the 5-hour window 6 %). Main unmoved since the release (b131ae85). Moved to the archive by a
  checked script (`archive_r17.py`; every moved line found there, nothing lost): round 15's 12 Landed rows, round 16's
  build order with its closed-from-Waiting note, questions 64, 66, 67 and 68, and round 15's 151 log lines (2026-11-14,
  its survey, to the screen-reader fix on 2026-11-16). Attached: both monitors (the 4K at 150 % primary reading 119 Hz,
  the 1440p to its left). Steam is running. The eight experts survey round 16's build, 32c85e81..b131ae85, and main's
  8 commits outside plan 17, in their own worktrees on the archive commit, in real windows too.
- 2026-11-19: round 17 survey done (reports cae692da..fbf22216, `17-reports/r17-*`; all eight spawned at ~11:20 UTC, all
  in by 12:16; 65 → 71 % weekly, the 5-hour window 6 → 53 %). No expert satisfied. Review objections: 80e94eeb (expert 8
  bisected +48 MB working set after a big-file walk, committed 34.5 → 72.8 MB after a full GC; expert 4: Changes-mode
  kept texts are unreachable after any working-tree event) → step 2; b8a3dcf5's suite row `virtual/columns cursor walk
  100k item rows` measures nothing and makes every suite run that includes it exit 1, which step-ab refuses (experts 1
  and 4; checked: `step-ab.ps1:212` throws on a non-zero exit) → step 1 (a), first. Main's 8 commits: no real-app
  regression; round 16 helped the real apps (Junction start −18 %, a ref move −57 %, a wrapped drag −87 %, the first
  palette open +18 → +8 MB DXGI, Vesper's reader open −28 %). Experts 2 and 3 found the same window-sized layer in the
  video player's fading control bar (step 6, question 69); expert 5's ancestor-width label layouts were sent to expert 1
  mid-survey, who found the cause (an ancestor measures a VirtualList through its rows) → step 1 (b). Merged into a
  10-step build order; questions 69 (the player bar's pixels), 70 (colours before rows) and 71 (Jellyfin's user) asked;
  3 Waiting items and 3 suite proposals closed, 4 items added. Real windows: 83 guarded launches in 15 holds of
  `Global\HultUI-r17-desktop`, 0 foreground takes, 0 driver events, 0 screen reads. For PERFORMANCE.md: an element
  faded by `Opacity` gets a window-sized layer whenever a descendant has unknown paint bounds (Slider, Toggle,
  Checkbox, Dropdown…), so check Skia's cache after the first fade; a VirtualList inside auto-basis flex rows is
  measured through its rows at widths never drawn; strings over the LOH threshold kept in a cache move gen-2 timing
  and keep committed memory far above their own size: measure working set and committed after a full GC, not only the
  heap. Phase → build.
- 2026-11-19: round 17 step 1 landed (8dbae799, dd3ad712; captures 4c890435): the columns walk row focuses its list, so
  full suite runs exit 0 again; an ancestor measures a VirtualList as a leaf, not through its rows: Junction resize and
  splitter steps −48…−70 % UI bytes (real window Commit unwrapped 70.5–81.9 → 23.2–37.5 KB a step, 12.8–13.4 → 8.1–9.3
  Mcycles), pixels equal in 240 of 240 cases, the whole Windows suite and the Fedora run pass. It takes most of
  text-wrap-ranges-per-width's win (C# far misses 1,062 → 26); the heights pass makes what is left (step 8).
  layout-virtuallist-spacer-stale closed. For PERFORMANCE.md: size a VirtualList from outside (FlexGrow or a height, plus
  stretch or a width); run the layout audit's floor on the differing tests before blaming a change. Next: step 2.
- 2026-11-19: round 17 step 2 landed (34606bab, b1423e6e; captures b185a60c): the objection to 80e94eeb is answered. A
  working-tree event that leaves a diff's texts unchanged keeps its colours (bigrepo 288.7 → 9.4 MB an event, no
  uncoloured frames; real window 266 → 9.4 MB, a 2.8 s blink → none), and one aggressive collection 2 s after a
  text-keeping walk rests gives the memory back: end-of-scene working set 202.6 → 134.1 MB, below the 155.7 MB of the
  build before 80e94eeb, with 80e94eeb's revisit win kept (0 gen-2s a revisit pass). Question 72 not needed. The
  orchestrator corrected DECISIONS.md's monitor (the 4K at 150 %, not a 2560×1440 at DPI 144). For PERFORMANCE.md: a
  forced compacting gen-2 frees the heap but keeps committed memory; only `GCCollectionMode.Aggressive` gives it back:
  run it once at rest, between frames. After restoring a reverse edit, rebuild the test project (`--no-build` runs the
  app DLL copied into the test bin). Next: step 3.
- 2026-11-19: round 17 step 3 landed (a787c194; captures 23dd0de4): a diff's old version takes the lines it shares with
  the new one: −32 % lines walked over the last 200 commits' big files, 0 of 379 pairs differ (fresh and resumed);
  bigrepo Commit open 293.7 → 168.5 MB with colours ~1.3 s sooner in the real window. The desk changed: only the 1440p
  (100 %, 200 Hz) is attached, the 4K is not (read the desk again before each real-window step). For PERFORMANCE.md:
  TextMateSharp's `StateStack.Equals` is not enough to reuse a line's tokens (it ignores a begin that matched to the end
  of its line, which changes `\G` on the next line). Next: step 4.
- 2026-11-19: the user answered: question 69 → (b) (the player bar's bounds, with the one-level change; step 6), 70 → the
  recommendation ((b) now, step 4), 71 → named the Jellyfin account to browse as (kept in memory `aperture-jellyfin` and
  in briefs only: the repository is public).
- 2026-11-19: round 17 step 4 landed (b3bb93bd; captures ef113cef): a file's colours landing recolours the diff rows on
  screen instead of making them again: Junction first file visits −29…−35 % (real window 733.1–868.2 → 479.8–613.4 KB),
  the landing 334.5–347.3 → 79.9–93.7 KB with 0 rows made, a review's landing keeps the focused control (Waiting
  junction-review-focus-colours closed). Question 70's route (a) would add −25…−32 % and one frame less a first visit
  with the diff shown later: not built. The 4K was attached again during the step (read the desk before every hold).
  For PERFORMANCE.md: a virtual row costs ~0.6–0.8 KB an element plus ~0.9 KB a selectable label: keep elements per
  row few. Next: step 5.
- 2026-11-19: round 17 step 5 landed (3ea96588, 6751a51a; captures b5ab4704): each Junction view tab is its own part
  lit through a `Computed`, so a switch builds two tabs, not the toolbar (real window History 174.0 → 127.1 KB, Overview
  100.6 → 53.7, Tags 140.2 → 93.3, Commit 421.5 → 374.6); Vesper with both panes on one folder no longer builds on a
  cursor key while the other cursor is on a file (17.5 → 2.2 KB a key). Question 72 asked (a file ringed as its own
  F3 partner). For PERFORMANCE.md: for a one-of-N selection (tabs, segments) give each item its own part read through
  a `Computed` of the selection, so a change builds only the two items that moved; a Key on a part's root does not
  make its host replace it. Next: step 6.
- 2026-11-19: round 17 step 6 landed (0a7bb324, c23edfb9; captures fe46c02b): the video player's bar fades through a
  layer of its own size (the Slider and the bar's glyphs report paint bounds; a hidden subtree that only paints is
  skipped): first-fade DXGI local +12 / +24 / +32 MB → +0…+2 MB on Vulkan and GL, with real mpv too; scene-end local
  −4…−20 %, private −11…−18 %; only 2 pixels of the full-screen icon differ by 1/255 during the fade (question 69: (b)).
  The gated UiTests guard needed a 4th hold (the orchestrator allowed it): +2.0 MB with the change, +12.0 under the
  reverse edit, GL and Vulkan. The user answered question 72: (b), never pair a file with itself in Vesper → step 11.
  Lead: `Window.SetRoot(component.Mount())` never mounts the component (its `OnMount` never runs). For PERFORMANCE.md:
  one element with unknown paint bounds makes its faded ancestor window-sized (on GL with mpv it kept 24 MB); number
  every probe launch within a hold (a reused label overwrites its log). Next: step 7.
- 2026-11-20: round 17 step 7 landed (13fee514; captures b5ac3235): a Linux reader that starts after the app now hears
  the next focus move (AtSpiRig: a cursor walk after a late attach 0 → 2 signals a move, eager and served), as the UIA
  bridge did; no signal at the attach (Orca reads the focused state when it starts). For PERFORMANCE.md (testing): a
  served-list probe on synthetic rows sends no selected states and understates selection signals; use real string
  lists. Next: step 8.
- 2026-11-20: round 17 step 8 landed (e790d321; captures 91721d06): a wrapped height ask adds the lines up without making
  a layout (internal, no API): Junction's heights pass 40 → 2.9 KB a Commit step and 101 → 4.4 a Changes step in the
  real window, the prose Commit step 70.3 → 31.8 KB and Changes 131.3 → 34.0 (−55…−74 %); 0 of 2,011,835 sizes differ.
  Together with step 1, a wrapped Junction resize step is now about a quarter of round 16's. Found on the way: Fedora's
  `JunctionAppHostTests.TheBranchesViewShowsABranchCreatedOnDisk` fails 3 of 3 at 13a3e123 (it passed in step 5's Fedora
  run at b5ab4704, so step 6 or 7 or the environment): a fix agent bisects it before step 11 (r17fix). For
  PERFORMANCE.md: a height-only text ask must not take a drawn row's layout away (`TextLine.RunsMade`); a zero-allocation
  test of a re-break warms the same widths first. Steps 9 and 10 (measure-first leads) stay queued for round 18's
  survey: the weekly budget is at 81 % with ~2 days to its reset, kept for step 11, the full suite and the release.
- 2026-11-20: the Fedora failure was older than round 17 and a real Linux bug in Junction (9f9518e2): a ref made in a new
  folder (`feature/x`, a tag in a new folder) was never heard, because inotify starts watching a new folder only after
  it hears it made and the refs watcher dropped folder events; it now asks for them. Junction.Tests 1,183 of 1,183 on
  Fedora and Windows. For PERFORMANCE.md (testing): a test that waits on the file watcher can pass on Windows and race
  on Linux; run a Fedora failure alone three times at the last commit known to pass before bisecting. Next: step 11.
- 2026-11-20: round 17 step 11 stopped before any change, as its brief said: F3 is "Compare files", and with no partner
  it opens the diff as "Right file missing" with a copy button, not the file, so question 72's (b) cannot be built as
  asked. Question 72b put to the user with three ways (drop only the ring; F3 does nothing there; F3 opens the viewer)
  [(2)]; step 11 waits (Waiting vesper-file-not-its-own-partner). Round 17's build order is done (8 steps landed, the
  watcher fix, steps 9–10 queued for round 18). Phase → the full suite.
- 2026-11-20: main had moved by 18 commits (plan 19: the digit table with punctuation, right-to-left runs kept whole,
  the pipeline's shaper call counts; Junction's Changes load under index moves; WebView middle-click autoscroll); merged
  into the branch as 45cbde7b, no conflicts (TextPipeline.cs, GitWatcher.cs, JunctionApp.cs, DECISIONS.md auto-merged),
  the merged tree built. Round 17's full suite at 45cbde7b, 02:10–02:28: Windows `HultUI.slnx` 30 projects, 0 failures
  (Junction 1,188 in 10 m 5 s, Core 5,212, Headless 1,822, Text 324, Skia 471); Fedora the slnf (22 projects) and
  Junction 1,188, Meridian 680, Aperture 370, Reel 53 each in a private D-Bus session, 0 failures; docfx 0 warnings, 0
  errors. Phase → release.
- 2026-11-20: round 17 RELEASED. Merged into main e799c23b and pushed (origin/main 3c6d6a5b in the branch, tree identical
  to the tested branch but the ledger; the primary checkout fast-forwarded behind the guard). CI run 44653899561 green on
  the first attempt (test, test-linux, docs, meridian-keyring on Ubuntu and macOS); "Push on main" green. Round 17 in
  sum: 8 build steps landed, a Junction Linux watcher bug fixed on the way, steps 9–10 queued and step 11 waiting for
  question 72b. Real-app results: a wrapped Junction resize step about a quarter of round 16's (steps 1 and 8), a save
  that leaves a diff's texts as they were keeps its colours (288.7 → 9.4 MB an event), Junction's big-file working set
  202.6 → 134.1 MB, big multi-hunk diffs coloured with 32–45 % fewer lines walked, first file visits −29…−35 %, view
  switches −27…−47 %, the player bar's first fade 12–32 → 0–2 MB of GPU memory. Usage 65 → 85 % of the week. Round → 18,
  Phase survey, in a fresh context after the weekly reset.
- 2026-11-20: the user answered question 72b: (2), "b" — with both panes on one folder, F3 does nothing on a file whose
  only partner is itself (as on a folder) and nothing is ringed. Step 11 goes at the head of round 18's build order as a
  decided item (Waiting vesper-file-not-its-own-partner).
- 2026-11-22: round 18 survey started in a fresh context (session 00f54f03, after the weekly reset; usage 1 % weekly,
  the 5-hour window 7 %). Main had moved by 5 commits (fe8f4ecf: a control outside the framework can arm text input;
  Aperture's browser-style page history, whole-app keyboard use and New seasons row, 0f9c8477, ee044d36, f67b25ec,
  93f8a26d); merged into the branch as b28e9128, no conflicts. Moved to the archive by a checked script
  (`archive_r18.py`; every moved line found there, nothing lost): round 16's 10 Landed rows, round 17's build order with
  its closed-from-Waiting note (its untaken steps 9 and 10 copied into Waiting first, with their evidence), questions
  69–72b, and round 16's 123 log lines (2026-11-18, its survey, to the app releases on 2026-11-19). Attached: both
  monitors (the 4K at 150 % primary, 120 Hz; the 1440p at 100 %, 120 Hz, to its left). Steam is running. The eight
  experts survey round 17's build and main's 18 commits outside plan 17 since round 17's survey, in their own worktrees
  on the archive commit aa3a3696, in real windows too (desktop lock `Global\HultUI-r18-desktop`); all eight spawned at
  ~09:45 UTC.
- 2026-11-22: round 18 survey done (reports 4a15613a..f5124ce0, `17-reports/r18-*`; all eight spawned at ~09:45 UTC, all
  in by ~10:35; 1 → 8 % weekly, the 5-hour window 7 → 54 %). No expert satisfied. Review objections: e790d321 (a size
  ask answered from the width cache leaves a stale wrap range after a return to the opening width; expert 5) → step 2;
  0f9c8477 (Aperture's Back to a scrolled library lands at the top, against the commit's own promise; expert 6) → step
  3. Round 17 helped the real apps (expert 8: Junction first file visits −22 %, view switches −4…−40 %, bigrepo working
  set at rest 202.4 → 134.9 MB, the player bar's first fade +12 → +2 MB); main's plan 19 merge costs Junction's start
  +7.7 % once a session (low). Expert 7 found round 17's 82.8 % "other" share to be a frame-log trap and the real cost
  of a UIA client: 0.56–0.73 ms a `UiaDisconnectProvider` on the UI thread (step 5). Experts 1 and 4 measured the
  selectable-label wiring apart (−4 % alone headless, −11…−16 % with the children set in the real window): one step,
  separate commits, owner 1. Five Aperture candidates from main's new features (steps 3, 6–9). Merged into a 14-step
  build order headed by the decided step 1; questions 73 (the drag ghost's pixels), 74 (two swapchain images), 75 (keep
  Back's page) and 76 (Aperture with a reader) asked; 8 Waiting items closed, 2 added. Real windows: ~80 guarded
  launches in 17 holds of `Global\HultUI-r18-desktop`, 0 foreground takes, 0 driver events, 0 screen reads; both
  monitors attached throughout. Expert 8 signed in to Jellyfin as the named user. Older scratchpads' publishes have lost
  their DLLs (a cleanup); re-publish. For PERFORMANCE.md: a `SetWindowPos` resize without `WM_ENTERSIZEMOVE` rebuilds a
  Vulkan swapchain every step (stand-ins for a user's drag send the pair); read `Performance` after `EndFrame`, not in
  `FrameRendered`; a picture cache bounded by entries, not bytes, grows with every big picture shown; a component's own
  context-menu provider must survive a patch. Phase → build.
- 2026-11-22: the user answered question 74 → (c) ("b and do the test": 2 images for the 8-bit swapchain, and the
  guarded HDR drag run in step 12), 75 → (a), and 76 → (a) (the message said "75: a" twice; the second taken as 76).
  Question 73: the user asked for the images and the image diff first; the three frames that differ most and a whole
  frame were sent (`r18-painting\images\`). Step 1 is building (agent on `brief-r18b1.md`).
- 2026-11-22: the user answered question 73 → (b) after the images ("It looks fine after the changes so lets go ahead
  with it."): step 11 (b) bounds the drag ghost's layer, with the edge pixels it changes.
- 2026-11-22: round 18 step 1 landed (ca72af3e; captures 6d5dff93): in Vesper a file is never its own F3 partner, so
  with both panes on one folder and the other cursor on ".." or a folder a cursor key builds nothing (29.2 → 1.2 KB a
  key) and F3 does nothing there, as on a folder (question 72b (2)). The agent also treats the other cursor resting on
  the same file as a self-pair (a walk over it builds twice, 2.0 → 6.5 KB a key over 10 keys). Next: step 2.
- 2026-11-22: the user on step 1's extra call: "keep the self-pair walk as it is" (the other cursor resting on the same
  file stays a self-pair: no ring, F3 does nothing; a walk over it builds on the key onto it and the key off it).
- 2026-11-22: round 18 step 2 landed (e546143d, 3191b065; captures fc885fc2): the objection to e790d321 is answered —
  a size ask answered from the width cache puts the layout's wrap range back, so a resize drag after the window returns
  to the width a view opened at no longer breaks every row again (Junction split −21…−22 % a step, real window and
  headless; 574f43a7 −34 %; Vesper's zh drag back to round 17's itemizing). The second commit fixed the first's own
  +0.5 KB on a suite row by reading the range's bytes from the cache entry's weight (8 B a node instead of 16). For
  PERFORMANCE.md: a field added to a long-lived cache entry is paid on every node, so read what the entry already holds
  first; NativeAOT's per-phase UI bytes vary between identical runs, so take exact bytes from JIT and counts from AOT.
  Next: step 3.
- 2026-11-22: round 18 step 3 landed (ab4069ca, 5b7a4494; captures 5f61fb12): the objection to 0f9c8477 is answered —
  Aperture's Back, Forward, Escape, mouse back and rail reopen land where the grid was scrolled, the cursor's card focused
  (the keys after a Back 279.1 + 75.6 KB → 0.4 + 0.4 KB, 0 builds); a wheel is no longer pulled back to the cursor by
  a build; with question 76 (a) Escape to Home focuses a card and Play and Resume name the film for a reader. The desk
  changed: only the 1440p (100 %, 200 Hz) was attached for the real window. Question 77 asked (a page with no cursor
  opened by a number key or the palette). For PERFORMANCE.md: a view that lets go of its elements keeps what its next
  build needs (scroll offset, cursor) in the model; reveal a cursor when it moves, not on every build, or the wheel
  fights it. Next: step 4.
- 2026-11-22: round 18 step 4 landed (3442c3f5, 30a53f02, f97a6736; captures 89ecaeff): a text field's or selectable
  label's right-click menu acts on the live element after a rebuild (it acted on a detached copy); selectable labels
  wire shared handlers (880 → 248 B to make one); a VirtualList's window move builds no set and no new array. Wheel
  notches in the real window: Junction Commit −12.0 %, History −5.3 %, Vesper's reader −16.2 %, compare −11.5 %;
  step-ab 0 slower on both runtimes, 31 suite rows' bytes down. Found: Meridian's RunnerView tests fail "N builds of the
  results" in 4 of 20 full-suite runs, at the base too (a timing race in the test; CI may hit it) → Waiting
  meridian-runnerview-build-count-flake, to fix before round 18's full suite. For PERFORMANCE.md: an element's own
  wiring uses static handlers that read `e.CurrentTarget` in one exact array; an element's own delegate never goes in a
  public property a patch copies. Next: step 5.
- 2026-11-22: round 18 step 5 landed (8b7471c8; captures fada66d3): with a screen reader or any UIA client asking, the
  providers a frame lets go are told to UI Automation on a thread of their own: Junction's palette close 8.2–8.9 → ~1.0
  ms wall (−88…−89 %), the first Commit open −13…−18 %, the first palette Down's 3–5.5 ms disconnect off the UI thread;
  the same events reach the client. For PERFORMANCE.md: `UiaDisconnectProvider` costs ~0.6 ms on the UI thread with a
  real client (headless never shows it); a test that reads `ProviderDisconnected` flushes first; compare timing-dependent
  reader event streams across several runs as sets of variants. Next: step 6.
- 2026-11-22: round 18 step 6 landed (678a786c; captures b33b1ef6; the declined part in DECISIONS.md): Aperture keeps
  its heroes by their bytes and frees one let go at once: after 20 item pages on the real server private −7.1 %, working
  set −6.0 %, DXGI local −7.5 %; back at Home −7.6 %. The survey's ~+600 MB over 20 pages did not exist (heroes were
  already capped at 4: +179 MB). Dropping the decoded copy after upload (−67 MB) declined: no device-loss path. Lead →
  Waiting skia-rest-purge-vulkan (a rest-time `PurgeUnusedResources`, ~−70 MB at Aperture's Home). For PERFORMANCE.md: on
  NVIDIA Vulkan GPU textures and staging buffers count in private bytes, ~1 s behind a purge; a probe's own output must
  never contain the driver's stop marker (`MEM `). **Paused by the user after step 6 (2026-11-22, "Pause after step 6 and
  resume at midnight"):** a one-time timer re-runs the loop at 2026-11-23 00:00 local. Next: step 7.
- 2026-11-23: round 18 step 7 landed (941cffdf, 0f5cda09, question 77 (a) 421cc677; captures f26168b7): Aperture's title
  bar and rail build only when what they show moved, by a value compared on every app build (no call site marks a part;
  the forced-build oracle green), and each rail door is its own kept part. Headless Enter 167.4 → 72.9 KB (−56 %), Forward
  78.7 → 32.9 (−58 %), library first open 753.6 → 564.8 (−25 %), a paging build 237.6 → 139.9; real window Enter 180.4 →
  86.4 KB, part builds 4 → 1. Question 77 (a): every way into a page hands its first card the keyboard (Aperture has no
  command palette; a click or Tab during a slow load keeps the keyboard). For REACTIVITY.md / the guidelines: compare a
  part's value on every build rather than list call sites that mark it; read loaders untracked in the owner's compare; a
  kept part is outside the window's tree during the owner's build (`IsInTree`/`IsHosted` false), so check focus after
  it; make per-item parts once and keep them by slot. Known quirk, unchanged: after a sign-in to another server the rail
  shows the old server's libraries until the new list lands. Next: step 8.
- 2026-11-23: round 18 step 8 landed (d7eedc4b; captures 092201cc): an Aperture picture that lands goes into the element
  that waits for it, keyed by its URL, with no view build (cards, heroes, cast faces). Headless library first open
  1,109–1,150 → 594–600 KB (−47 %, builds 12–13 → 4), a film page 376 → 211 KB, a season page 441.5 → 202; real window
  library first open 1,413–1,424 → 604 KB, ten Downs 1,895 → 947 KB; 33 of 33 frames byte-identical; step-ab clean both
  runtimes. Suite proposal (k) closed as `ArtworkLandingTests` (the suite references no app). Two side fixes the landing
  builds had masked (a taller window's new grid row asks for its page; a full build after a 404 makes the same tree).
  For the guidelines: builds triggered by landings can hide a missing reader of the window size. Leads → Waiting
  aperture-postercache-slot-stall and reconcile-keyed-match-alloc. Next: step 9.
- 2026-11-23: round 18 step 9 landed (f980fc8d; captures 06ca5c94): each Aperture Home shelf is its own part kept by its
  row key, and a page turn builds only that shelf. Headless a Right that turns a page 98.0 → 80.2 KB (−18 %), a cached
  Down/Up turn 67–68 → 50.9 (−24…−26 %), after leaving another shelf 121.4 → 81.1 (−33 %); Home's start +1.0 %, Back to
  Home +2.1 % (3 host elements); real window a Right that turns 122.9 → 107.4 KB; 23 of 23 frames byte-identical;
  step-ab clean both runtimes (JIT pair 1's RowHeights rebuild rows the known noisy row). Suite proposal (j) closed as
  `HomeShelfTurnTests`. Main has moved on by 9 commits (framework ContextMenu, Button, Dropdown, TableView, Application,
  and 133bcfe6 Aperture Up/Down shelf to shelf): Waiting release-merge-main-shelf-arrows records the conflict and the
  tests to rewrite at the release merge. Leads → Waiting part-place-stale-double-build and aperture-poster-card-cost.
  Next: step 10.
- 2026-11-23: round 18 step 10 landed (f7f2f6c5; captures 7f9b1e46): Junction's one collection at rest also runs after
  a big colour walk that finished (≥ 2,000 lines or ≥ 32 MB allocated on the worker) and waits for 2 s without input.
  Real window working set at rest after the Commit open 123.1 → 98.0 MB (−20 %), after 3 first visits 133.0 → 101.5
  (−24 %), committed 40.9 → 10.1; jrepo 0 collections; pauses 4.3–6.6 ms, one a rest, no late frame; step-ab clean.
  For PERFORMANCE.md / probes: `GC.GetGCMemoryInfo().TotalCommittedBytes` is as of the last collection, so it reads
  stale or 0 at rest; read working set or private bytes there. Next: step 11.
- 2026-11-23: round 18 step 11 landed (0ec5182c, 1ddec166 (question 73 (b)); captures a83bcd2e): a number field reports
  where it paints, so Vesper's faded counter row fades in its own layer (layer 1.198 → 0.041 Mpx a frame; real window GL
  DXGI local at the end 30.2 → 22.3 MB, −26 %; Vulkan −9 %; pixels byte-identical); the drag ghost fades in a layer of
  its own size (2.916 → 0.026 Mpx a frame; Junction's drag GL 40.3 → 28.4 MB, −30 %; Vulkan −13 %; drag frames differ
  by ≤ 6/255 as the user accepted). step-ab clean. For PAINTING/DRAG-AND-DROP: the drag ghost is a faded group like any
  other, so one element with unknown bounds in a drag source (a Canvas) makes every drag frame window-sized, and Skia
  keeps that texture on GL and Vulkan alike. Next: step 12.

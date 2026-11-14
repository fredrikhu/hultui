# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 14
- **Phase:** build — round 14, step 3 next (the round 14 build order below; 9 steps; step 1 landed; step 2's ordinal
  parser landed and its per-file load waits for question 60: resume step 2's agent, a68a4a007051cd141 in
  `r14-agents.txt`, with the answer once no other agent builds). Questions 55–59 answered 2026-11-13 with the
  recommendations; 57 asked again 2026-11-14 with step 1's numbers; 60 open. Build briefs
  `brief-r14b<N>.md`, the agent ids in `r14-agents.txt`, survey probes `r14-<area>\` and build probes `r14b<N>\` in
  session 41943a9b's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\41943a9b-f657-4483-971a-a2b346b967f7\scratchpad\`. One build
  agent at a time in this worktree; real windows under the desktop lock `Global\HultUI-r14-desktop`. After the last
  step the full suite (`fullsuite-r13.sh` in session 17c11f5e's scratchpad as the model), then Phase → release. Round
  13's release: main 7cd6f083 + CI fixes e432c224, CI run 43499639788 green; app releases junction-v1.27.0,
  vesper-v1.27.0, meridian-v1.10.0, aperture-v1.25.0, reel-v1.11.0 (see the Log).
  To resume in a fresh context, run in this worktree: `/loop Run the next step of
  the performance team: follow docs/plans/17-performance-team.md, with state in docs/plans/17-performance-team-ledger.md.
  Read both once per session (afterwards only the ledger's Phase and the part the step needs), do the step the ledger's
  Phase calls for, record it in the ledger, and commit.` Before resuming, `git fetch` and merge main if it moved; build
  steps use `scripts/perf/step-ab.ps1`. Open for the user, not blocking: the live screen-reader check of menus (with
  question 50's bridge gate); another F16 held-drag run (Waiting) only with a new approval. Older rounds are in
  [the archive](17-performance-team-ledger-archive.md) (search it, don't read it whole). History note: rewritten
  2026-10-25 (every commit after 3353ce47 has a new ID; a pre-push hook refuses pushes built on the old history).
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`)
- **Review commits since:** round 14's survey reviews round 13's build, 8f01b6fd..e432c224 (the last survey report to the round 13 release; its two merges are round 13's own into main). Earlier: round 13's survey reviewed round 12's build, 5f1744ca..afc1ed40 (the last survey report to the round 12 release; the one merge is round 12's own into main). Round 12's survey reviewed round 11's build, 5731ee58..b0dd1954 (the last survey report to the
  round 11 release; it includes the .NET 11 upgrade's step 0 only as already reviewed). Earlier: round 11's survey reviewed round 10's build and the .NET 11 upgrade, f4d6050d..868e2ffc
  (the last survey report to the upgrade's captures; round 10's merge into main cc55bdcc is in the range). Earlier: round 10's survey reviewed round 9's build, 4919ac40..facced69 (the last survey report
  to the round 9 release; 82 commits, 81 not merges; no merge from main this round). Round 9's survey reviewed
  eaa1ec29..10acc8c0 (round 8's build, with main's merge 3a15b5b1); round 8's survey reviewed 17d4220a..df45b4a2.
- **Probe code:** round 14 surveys in session 41943a9b's scratchpad
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
| 1. Layout | 14 | no | none (0da9f80e: only `VirtualListElement` reads `IsHovered` while painting, every other hover look marks its own frame, a wheel notch under a resting pointer draws 1 frame in every app; 154a7ca3 holds at 150 % with fractional widths, 0 stale rows in 60 steps; a save over History 0 builds and 0 frames). Real windows (3 launches): Junction's wrapped diff draws 2 frames a resize or splitter step → step 4 (a); a save over Changes draws a same-pixel frame first → step 6 (a); Meridian's tab switch with the pointer on the map list draws a same-pixel frame → step 4 (b); hover stays on a row scrolled away from a resting pointer → question 59 |
| 2. SkiaSharp: painting | 14 | no | none (4fdb56cb: the purge drops no on-screen texture, a repaint right after uploads nothing, Aperture DXGI local 109.8 → 85.0 MB; 0da9f80e: 9,150 inputs that drew no frame all byte-identical to a forced frame; 8-bit path intact; d73b1439's walk-only frames draw nothing). Real windows (8 launches): Vesper draws a frame on every button release → step 5 (a); the scrollbar's hover tween draws 15 frames at 120 Hz, its tail frames identical → step 5 (b) |
| 3. GL and Vulkan | 14 | no | none (cd7e3db2: nothing paces without asking, 0 hint changes at rest or in 60 resize steps, a tween gives 1 ms back 1.0 s after; 4fdb56cb: one `SurfacePurge` wake per run of size changes, none on GL; 276789d8: reopen and close clean, no thread left). Real windows (15 launches): the video interop loads the GL driver inside the first video frame (~50 ms) → step 8 (question 55); the UI thread wakes 1.3–10.8 a second at rest with no loop pass and no message (lead, Waiting) |
| 4. .NET runtime | 14 | no | none (every round 13 commit read: da72e01b's table bounded, no growth over 60 repo switches; natural-end words under the LOH line, weighed into the 4 MB; labels retained as round 12, only the churn gone; 0 B a purge pass, a timer wait or a handler scan). Real windows (7 launches): Junction's History at a reading pace allocates 9.35–10.3 MB a key on all threads (the whole commit patch) → step 2; the watcher allocates 1.2 KB and throws one caught exception an event → step 3 (b) |
| 5. Text pipeline | 14 | no | 698c2758 (b): on real Chinese text the natural-end words cost +49…+72 % UI KB at every new width (1200 508 → 755, 1000 471 → 808 against (a) alone), winning only in drags → step 1 (a). Real window (8 launches; new real ko/th/ar/he/emoji fixtures): a wrapped Arabic README's drag at 1440 allocates 842.6 MB (every probe through the full BiDi layout) → step 1 (b); a CJK document past the word cache re-shapes everything a frame (156.9 MB a drag) → step 1 (c) (question 56) |
| 6. Reactivity and reconcile | 14 | no | none (watcher probe on both backends identical to round 13 step 1's; 8f9f2683 no stale case; d413e001's `Forget` loses no outside change; Meridian's tab switch −40 %). Real window (3 launches): a build in a gitignored folder reloads the repository (Changes 22–33 git runs a build) → step 3 (a); Vesper draws 1.70 frames a navigation, one with the new title over the old listing → step 6 (b); terminal staging is never heard → question 58 |
| 7. Accessibility bridges | 14 | no | none (all 22 label delegates give current names; d73b1439's fallback 10 → 11 is timing in one ReviewTests case; 0 STALE in 3,682 audits; 117 event streams identical). Real windows (21 launches): opening or closing the palette or a menu builds the whole window's tree (Junction palette close 98.7 % bridge bytes) → step 7; serving list items on demand from ~200 items (−16.6 % Junction start, −37 % a viewer open) waits with question 50's live check; 0 root asks in 21 launches |
| 8. App workloads | 14 | no | none (round 13 helped: 67 AOT walks geomean 0.997 headless; real windows (39 launches) pointer sweeps Junction diff 33 → 0 frames, Vesper 48 → 2; a save over History −27 % UI KB, the app's Fetch −36 %, checkout −34 %; Junction start −30 %; a Chinese drag at 960 logical 2,183 → 39.6 KB a step; Vesper wrapped drag 1 frame a step; Aperture −24.5 MB GPU after size changes; Junction at rest 15.6 ms timer). Leads: a ref move with History on screen pays the bridge 25–55 % of its bytes (Waiting, with question 50); Vesper's wrapped drag 248–269 KB a step (Waiting) |

## Queued candidates

Rounds 1–13's build orders all landed but round 6's step 8, round 10's step 14 and round 11's step 1 (c), measured
and declined, round 11's steps 13 and 14 and round 13's step 5 (b), measured with nothing to change, and round 13's
step 6 (c), not attempted (back to Waiting) (rounds 5–13 are in
[the archive](17-performance-team-ledger-archive.md#round-13-build-order), with each survey's closed-from-Waiting note). Round 14's build order
follows, from its survey.
A build step may bundle one expert's related small candidates as separate commits. Items marked **(user)** wait for a
decision in Needs the user; the rest of their step goes ahead.

### Round 14 build order

Ranked by expected effect in the real apps over cost (the brief's "Real apps first"); the review objection first. Each
step names the report lines its brief quotes (`r14-k` = `17-reports/r14-k-<area>.md`) and measures its real-app case in
a guarded real window (the desktop lock `Global\HultUI-r14-desktop` and the harnesses in session 41943a9b's scratchpad
`r14-<area>\`) as well as by step-ab. Text work uses real text (r13-5's ja/zh, r14-5's ko/th/ar/he/emoji fixtures).

1. **Text: the objection, wrapped BiDi lines and the cache cliff** (5; r14-5 REVIEW and Candidates 1–3) — separate
   commits. (a) **text-cjk-natural-end-trim**, the objection to 698c2758 (b): a table-end segment the word cache already
   has is its own answer (no natural word looked up or made), and a natural word also ends at the first change between
   ASCII letters/digits and other characters at or past the table's end. Headless zh UI KB 1000 808 → 555, 1200 755 →
   640, 960 723 → 532, drag at 960 1,933 → 1,369; ja −12…−23 % at new widths; the 154k-character file ±1 %; no phase
   worse; real window zh 1000 815 → 562 KB. (b) **text-bidi-textarea-probes**: `ComputeWrappedBidi` builds one unbounded
   line with no newline directly (no break-segment pass, no ICU line breaks), and `TextAreaElement.MeasureSubstring`
   measures through `MeasureTextWidth(span)` (TextAreaElement.cs:1188–1196, :1349–1354; TextPipeline.cs:2358;
   SkiaRenderContext.cs:1826). Arabic drag at 1440 830 → 44 MB headless, real window 842.6 → 32.8 MB (−96 %), 4.44 M →
   161k HarfBuzz calls; opening 25.3 → 14.4 MB; Hebrew, Thai and emoji drags −50…−80 %. Prove it with an equivalence
   test of every probe width on the four READMEs; check the end heap (28.3 → 40.0 MB by `GetTotalMemory(false)`). (c)
   **text-word-cache-cjk-cliff, measure first**: a scan-resistant admission once the cache is full (keep only words
   seen twice), which changes no memory; raised bounds (32,768 entries, 16 MB: the big file's drag 157.6 MB → 1.1 MB)
   only if the admission fails (question 56: yes, as its own commit). The report also gives what is left a drag frame
   (question 57). Patch `r14-text\r14-probe-and-variants.patch` (`R14_VAR=12`, `R14_VAR=16`
   with `R14_TAW=1`, `R14_WCAP`/`R14_WMB`, `R14_NAT=0`). Small; no API, no pixels.
2. **junction-commit-detail-whole-patch** (4, with 6; r14-4 Candidate 1, r14-8 Waiting) — `LoadDetail` parses the whole
   commit patch into `GitDiffLine`s though only the selected file's diff is read: the whole patch below 262,144
   characters, the opening file's patch above it (a git run per file's first visit); ordinal `StartsWith` in
   `ParseUnifiedDiff` (4 culture-sensitive calls a line on a Swedish machine; the suite's InvariantGlobalization hides
   them). HultUI's last 300 commits: all-thread bytes 628.7 → 133.7 MB (−79 %), gen 2 GCs 8 → 0, kept while selected up
   to 32 MB → ≤ 1 MB, 253 of 300 commits as today. Real window, jrepo History at a reading pace 9.35–10.3 MB a key on
   all threads (24–26 KB on the UI thread), 3–4 gen 2 GCs per 20 keys. Probe `r14-dotnet\patchprobe\`. Medium, app; no
   API. If the per-file path makes switching files in a big commit visibly slower, stop and ask.
3. **Junction's watcher during a build** (6, with 4; r14-6 Candidate 1, r14-4 Candidate 2) — separate commits. (a)
   **reactivity-junction-watch-ignored-paths**: an event under a folder git ignores (bin/obj, .vs, node_modules) reloads
   nothing; ask `git check-ignore --stdin` for a debounce window's new directories, cached and refreshed when .gitignore
   or info/exclude changes; a tracked file force-added inside an ignored folder is still heard. A build beside Changes
   22–33 git runs, 4–6 frames, 3–4 MB → 0; real window History, three builds 6/6/4 git runs → 2/0/0. (b)
   **dotnet-junction-watcher-debounce-timer**: one re-armed `ITimer` under the existing lock instead of a CTS, a
   `Task.Run`, a `Task.Delay` and a caught exception per event (GitWatcher.cs:135–139, :157–185): AOT 9.4–9.6 → 5.9 KB a
   file written, exceptions 2,690–2,875 → 0 per 1,000 files (a Junction build 267 events, a publish 551). (c)
   **reactivity-junction-watch-index-staging** (question 58: yes): the watcher hears index renames again and answers
   with a status, so terminal or IDE staging (`git add`, `restore --staged`, a staged submodule bump) reaches Changes:
   3 of 53 operations fixed on both backends, 0 regressions; cost 1–2 extra working-tree reloads after an outside
   checkout, pull or stash, and the app's own status heard back once or twice (measure it; `Forget` may cover the
   latter). Prototypes `r14-reactivity\final14.patch` (`R14_SKIPBINOBJ`), `r14-reactivity\fswidx\`,
   `r14-dotnet\watchprobe\`. Medium + small + small; no API.
4. **Junction's wrapped diff leads its width** (1; r14-1 Candidates 1 and 3) — separate commits. (a)
   **layout-junction-wrap-resize-lead**, as 154a7ca3 for Vesper: a window resize and a file-list splitter drag hand the
   build the body's laid-out width plus or minus the change (JunctionApp.cs:498, JunctionApp.Diff.cs:675 and :756,
   JunctionApp.Changes.cs:515); a resize crossing no breakpoint and no scale rebuilds only the view; a height-only resize
   builds nothing. Real window 2 → 1 drawn frames a step, 546 → 379 KB; splitter 216 → 49 PatchFroms, 446 → 266 KB
   (−41 %); height-only 180 → 3.2 KB; 18 of 18 led steps equal a full render. (b) **layout-hover-removed-row-no-frame**:
   a removed row's leave is not a drawn hover change (PointerState.cs:100): Meridian's tab switch with the pointer on the
   map list 2 → 1 frames (4 of 4), 0 of 8 stale. Prototype `r14-layout\probe-r14-full.patch` (`PROBE_JLEAD`,
   `ProbeJView`, `PROBE_NOREMOVEDCOUNT`). Small; no API, no pixels.
5. **Frames that draw what is on screen** (2; r14-2 Candidates 1–2) — separate commits. (a)
   **painting-vesper-pointer-up-frame**: Vesper's root `On<PointerUpEvent>` (CommanderApp.cs:776) becomes `OnPassive`:
   every click 2 → 1 frames (real window 20 → 10 per 10 clicks; each frame 448 elements, 213 runs). (b)
   **painting-scrollbar-tween-tail**: `Scrollbar.SetHover`'s tween ends at its target once less than half a device pixel
   is left: frames per scrollbar visit −27 % in Junction and Meridian (8 of 30 at 120 Hz, 13 of 48 at 200 Hz); every
   frame still drawn hashes as now. Probe `r14-painting\probe-r14.patch` (`PAINT_PASSIVE_UP`, `PAINT_BAR`). Small; no
   API, no settled pixels or goldens.
6. **Build once, when the load lands** (6, with 1; r14-1 Candidate 2, r14-6 Candidate 2) — separate commits. (a)
   **junction-changes-save-same-frame**, option (a): `Reload` starts the Changes view's own loads without a build and
   lets the landing build (as `SummaryFor` and `WorktreesFor` already do): a save over Changes 2.25 → ~1 frames; today's
   first frame redraws what is on screen (552 render visits; `PatchFrom` marks paint and style on every patch,
   Element.cs:1796, :1802 — not that). (b) **reactivity-vesper-nav-on-arrival**: Vesper's navigation (folder, cursor,
   marks, history, PaneDirty) applies when a local listing lands, or at the 350 ms connecting delay for a slow folder:
   real window 1.70 → 1.00 frames and 3 → 2 top builds a navigation, no frame with the new title over the old listing
   (`R14_NAVLEAD`, deferring only PaneDirty, made it worse: 1.42 → 1.92). If a slow folder's title would show later than
   today, stop and ask. Medium; app; no API.
7. **a11y-overlay-open-close-part** (7; r14-7 Candidate 1) — a hit-testable overlay's opening builds only its part under
   the root and its closing removes it, when nothing else in the frame needs a build (today `A11yOverlaysChanged` sets
   `_a11yPropertyDirty`, Window.cs:3799–3802, and `TryRebuildOverlays` declines an overlay with no part, :1185–1192):
   Junction palette close ~101 → ~6 KB (−94 %), open −28 %, a context menu close −83 %; O(window nodes) → O(overlay
   nodes). The palette's focus move must still be told; the palette and menu event-stream scenarios stay identical.
   Medium; no API.
8. **media-interop-gl-preload** (3; r14-3 Candidate; question 55: (b), opt-in) — when an app takes
   `HULTUI_GL_PRELOAD`'s opt-in, a Vulkan window with video interop loads the GL driver its hidden context needs on a
   worker after the Vulkan device is made (Reel with a file) or at the first rest (Aperture), joined only by
   `EnableInterop` (SdlVulkanHost.Interop.cs:147–236; `BeginPreload` returns early for Vulkan, SDL3Window.cs:2047);
   Aperture opts in as Reel does (one line); no default change. Open → interop ready 98/93 → 36/45 ms, Open → first
   video 246/229 → 180/189 ms. Measure Reel's first UI frame too (plan 15 §3.1: a preload joined before the window cost
   it ~95 ms). Small–medium; no API.
9. **layout-hover-follows-scroll** (1; r14-1 NEEDS THE USER; question 59: yes, images first) — when content scrolls
   under a resting pointer, hover (the list tint, hover styles) moves to the element now under it, as browsers do: one
   hit test a scroll frame (27–38 visits) before the frame draws, no extra frame. Today hover stays on the row that
   scrolled away (Junction 4 of 4 wheel series, Vesper 3 of 4). Make before/after images from headless frames after a
   wheel series (Junction History, Vesper's list) and return them to the orchestrator for the user **before
   committing**; commit only after the user's look. Small–medium; no API; changes where the tint is drawn during a
   scroll.

### Waiting

Re-ranked by the next survey.

- **live-reader check of menus** (user) — the six-step script in ACCESSIBILITY-PLAN.md (round 7 step 13) for Narrator,
  NVDA and Orca; a silent move would mean moving focus to the selected item (needs the user's approval).
- **junction-changes-leads** (6/7/8, reader behaviour, no perf number) — only the caret is told to a reader, not a
  multi-selection's other members; a reader's "select" on a folder row clicks it, which opens or closes the folder. (The
  perf parts were examined by round 8: below the bars.) Round 13: still so (round 12 touched no selection pattern).
- **junction-review-old-path** (app bug, not perf; round 7 step 8a) — the review composer sends GitLab `old_path` = the
  new path for a renamed file, because `GitFileDiff.OldPath` is never set (JunctionApp.Changes.cs:122, 124;
  GitRepository.cs:496, 511; `RenamedFrom` holds git's old path). Decide whether review positions on renamed files
  should carry the old path. Reproduces in round 13.
- **dropdown-follow-control-on-resize** (1, correctness, low) — a Dropdown list or DatePicker calendar stays at its
  opening point when a resize moves its control; anchoring changes how a press on the control behaves. Not perf.
- **junction-leads (remaining)** (tests) — `SelectConflict` (JunctionApp.Changes.cs:1477) and a conflict row's
  right-click have no test of their own (not perf; still so in round 13).
- **vesper-brief-filter-clear-reveal** (1, not perf; round 9 step 10) — clearing a filter with Backspace leaves Brief's
  cursor column out of view (the scroll is clamped to the narrower filtered layout; the sideways reveal has no
  apply-after-layout form like the vertical one). The scrollbar half was fixed by round 10 step 15 (`BriefScrolled`,
  CommanderApp.Views.cs:616–625; r13-6).
- **findbar-countonscreen-foreach** (4, nit) — `CountOnScreen` loops `foreach` over `bar.Children` (FindBar.cs:212; 32 B
  on AOT, 0.4 % of an 8 KB Enter), and `RewriteCount` (:317–319) calls it once a switch with the bar open; fix when
  FindBar is next touched.
- **textarea-chunked-document** (5/4; the user chose option B on 2026-10-25) — parked: Meridian's 89k body typing is
  ~238–241 KB/key, most of it the TextArea's new string; the user's 256k-character condition is unmet (round 13: still).
- **a11y-served-items-followups** — a live Orca check only (no desktop Orca here; optional).
- **gl-threaded-optimization-profile** (3; the user's driver setting, information only) — the walk lands while the
  window rests; the setting would remove only the per-frame spin (round 9: mean 357 JIT / 344 AOT, max 456/578
  `SwitchToThread` a paced frame; round 10: mean 378/376, max 723/644).
- **text-open-measures-every-line (full)** (5; user: not now, 2026-10-13). Round 13 (r13-5), stronger on real CJK text:
  a 1 px drag step re-breaks every wrapped row of the document (Japanese at 1440: 1,040 prefix tables, 119,899
  characters itemized and 84,495 fallback questions a frame; real window at 960 logical: 2,618 tables, 166,806
  characters) with ~40 rows on screen; step 3 removes the shaping and bytes, not this O(document) itemizing.
  Round 14 (r14-5): asked again as question 57 (zh at 1440 1,140 tables and 131k characters itemized a drag frame;
  the 154k-character file 2,178–5,288 tables and 9,900–13,550 HarfBuzz calls a frame; Arabic 1,846 BiDi probes a frame).
- **controls-menu-max-height-scroll** (1; not perf: no maximum height or scrolling), **layout-virtuallist-spacer-stale**
  (1; invisible; round 11 step 4: confirmed wrong but invisible, 209 stale spacer heights; marking the spacer needs its
  own proof).
- **aperture-poster-as-hero**, **aperture-tmdb-size-fallback**, **junction-narrow-gitlab-glyphs**,
  **meridian-pane-choice-not-persisted**, **meridian-min-width-wraps** (apps; not perf). Round 13 (r13-8):
  meridian-pane-choice-not-persisted reproduces (MeridianApp.cs:148, flipped at :340, never saved); the min-width wrap
  at 760×540 stands on Windows (46fccd88 keeps 448 / 1320); the others' code is unchanged.
- **syntax-shared-registry-rest** (5; parked) — Typst, Julia, PHP, Razor and HTML still use the shared registry (round
  11: `.typ` +42,419 KB kept); the user (question 42): no Typst files; parked.
- **junction-chromeparttests-load** (tests) — two `ChromePartTests` failed once under load (round 10). Round 13 (r13-6):
  no recurrence; since b6c6eb6f one case (a remote added) arrives through the real FileSystemWatcher alone, a new
  load-sensitive point. Watch at the full suite and in CI.
  Round 14 (r14-6): no recurrence; 63e005f3's `TempRepo.Settle()` removed round 13's ReviewTests race.
- **fedora-local-dotnet** (environment) — once Fedora ships the final `dotnet-sdk-11.0`, remove
  `/usr/local/share/dotnet` and both links, `/usr/local/bin/dotnet` and `/usr/local/sbin/dotnet` (`which` picks the sbin
  one). Round 13 (r13-4): Fedora 44's updates have only 11.0.100~rc.3; `wsl-test.sh` follows the dotnet on PATH.
  Round 14 (r14-4): `updates` and `updates-testing` still have only 11.0.100~rc.3; both links still in place.
- **f16-held-drag-post-stall** (3; one survey stall, round 11 step 13's approved re-run clean) — cause unknown. Round 13
  (r13-3): the only F16 drag left is during HDR playback, where mpv posts every frame (the stall's precondition); a run
  needs an HDR clip playing on the 1440p at 200 Hz and a new approval from the user.
- **Suite scenarios** (8) — (a) an idle Vulkan window counting every thread's wakes: keep, low, only as a gated UiTests
  guard of round 12 step 5 (116 against 212 a second). (b) frames per key under a real loop and (c) an app-shaped shell
  resize: closed in round 13 (DiffFrameCountTests covers (b); (c) went with a11y-structural-walk-during-resize).
  Round 14 (r14-8) proposes: (d) a 75,000-character Chinese wrap resize, 1 px a frame, on real gettext prose at 960
  logical px past the word cache (guards 698c2758); (e) a11y insert above 1,000 item rows with a UIA client listening
  (UiaRig); (f) a gated UiTests guard for 4fdb56cb (a Vulkan window's DXGI local memory back within 2 s of 18 size
  changes).
- **findbar-count-not-announced** (7, reader behaviour, not perf) — a find bar's new "n of m" reaches the bridge as a
  Name change on the count label; a reader speaks it only if it moves there (no `Announce`, no live regions).
- **vesper-start-focus-event** (7, lead) — at start Vesper raises a focus-changed event for a window that never had the
  foreground (round 13: 5 of 5 launches), and in 2 of 5 refuses a leaf patch and builds a second tree (~53 KB), start
  only. Cause not isolated; not perf.
- **a11y-gate-on-client** (7; question 50: not yet) — build the tree and raise events only after a UIA client asks for
  the window's root. Round 13 (r13-7): 0 root asks in 22 launches; the bridge costs Junction +40 % UI bytes at start and
  +29 % a commit-walk key, Vesper +29 % a drag. New risk evidence: a desktop-wide listener receives our bounds,
  structure, name and focus events (13 provider calls each) without ever asking for the root; a gate would cut it off.
  Decide after the user's live check with Narrator and NVDA (it could go with the menu check). Round 13 step 5 (b): each raise with a listener costs ~9 µs CPU (13 provider calls and 13 QIs, UI Automation's own work); a bulk rename (a keyed-by-index list swapping its contents renames every kept row, one raise each) could be told as one ChildrenInvalidated, or rows no client fetched could raise no Name: both change what a reader hears and need the same live check.
  Round 14 (r14-7): 0 root asks and 0 worker-thread calls in 21 launches of 5 apps; the bridge's share of UI-thread
  frame bytes: Junction start 17.5 %, palette open 35 %, palette close 98.7 %, clearing History's search 48 %, Vesper's
  viewer open 38 %, keys and wheel 1.4–2.5 %. Two items wait with it (below): a11y-items-on-demand-sooner and
  apps-junction-ref-move-a11y.
- **after-layout-callback** (1; question 51: only if another app needs it) — a public hook after layout and before
  render. Round 13 (r13-1): Vesper's wrapped viewer also needs a laid-out width, but a width lead fixes it without a
  hook (step 7); Aperture's and Junction's other after-frame reads are reveals or focus. Still not needed.
- **vk-format-switch-chained** (3; round 12 step 5; round 13 step 6 (c) not attempted) — switching F16 off rebuilds
  destroy-first: 325 ms in Reel (the 8-bit create alone 311 ms); the switch on is chained (2.7 ms plus a 96–154 ms first
  present), so a chained switch off would likely save ~200 ms once an HDR → SDR transition. Small; mind memory
  `resize-stretch-fix`.
  Round 14 (r14-3): keep, low; first an env-gated A/B of `DestroyBeforeReplacing` keyed on the new format (nobody
  has measured whether chaining shortens the 311 ms 8-bit create); no HDR clip run this round.
- **text-cjk-row-start-suffix** (5, lead; r13-5) — after step 3 the largest HarfBuzz source: a row-start word read off
  the line's word (Japanese 300 of 1,051 calls at 1000 px, 216 of 453 a drag; Chinese 201 of 1,165, 77 of 403). Needs
  the slice's own script guess and safe cuts at both ends. Medium–large; up to ~20–30 % of a new width's calls.
  Round 14 (r14-5): after step 1 (a) row-start segments are 88 of 951 calls (zh) and 118 of 681 (ja) at 1200;
  short words (under 8 characters) cut at need are the largest share (461 of 951); giving 2–7 character words their cuts
  in the tables halves a new width's calls but costs opening +248 KB (zh) / +121 KB (ja): a trade.
- **a11y-items-on-demand-sooner** (7; with question 50's live check; r14-7 Candidate 2) — serve a virtual list's items on
  demand above ~200 items, not 2,000 (`MaxEagerVirtualItems`, AccessibilityTreeBuilder.cs:18). AOT prototype, real
  windows with the desktop listener: Junction start 2,239.5 → 1,867.2 KB UI (−16.6 %; a11y 426 → 54 KB), Vesper's
  viewer open 1,742.5 → 1,099.2 (−37 %), a History search's 3 keys 2,840 → 718 provider calls, whole-tree builds for an
  overlay ~100 → 13–21 KB. A reader would notice: a relabelled served item is not told (AT-SPI keeps its copy), a list
  crossing the threshold builds the whole tree, a UIA client walking a long list makes a wrapper a step; AT-SPI needs
  the Orca check (a11y-served-items-followups). Decide with question 50's live Narrator/NVDA check.
- **apps-junction-ref-move-a11y** (8/7, lead; with question 50; r14-8 Candidate 1) — with History on screen a ref move
  pays the bridge 25–55 % of its UI bytes (terminal commit 1,579 vs 708 KB with `HULTUI_A11Y=0`, the app's Fetch 1,840
  vs 1,383) and one frame with 15.6–23.6 ms of "other": a new commit at the top moves every realized row, and a list
  keyed by index renames them all. Rows keyed by hash or a bulk rename told as one ChildrenInvalidated would change what
  a reader hears; count raises an event first (round 13 step 5 (b)'s probe).
- **apps-vesper-wrapped-drag-bytes** (8/1/5, lead; r14-8 Candidate 2) — Vesper's wrapped reader still allocates 248–269
  KB a drag step at 1 frame a step, none of it the bridge (64 PatchFroms a step headless suggests visible rows are
  rebuilt, not only re-measured); opening the reader 1,631 KB, 468 KB of it the bridge. Count builds and text calls a
  step first.
- **gl-ui-thread-rest-wakes** (3, lead; r14-3 REAL WINDOW) — the UI thread wakes at rest with no loop pass and no
  message (both hooks read 0): Junction 1.3–4.9 a second (3 launches), Reel 7.1–10.8 after a clip played, a resize run
  and driver-thread mpv reads (0 right after a fresh start); expert 4 saw 1.31 a second in 1 of 3 Junction launches
  (likely the real pointer). Next: stack-sample the UI thread with r14-3's h3 sequence.
- **mpv-cuda-context-at-rest** (3/8, lead; r14-8 REAL WINDOW) — after the first clip NVIDIA's `nvcuda64` thread wakes ~10
  a second, even for a raw y4m clip (mpv's CUDA context): whether CUDA could start only for a stream that
  hardware-decodes.
- **text-junction-first-visit-tokenize** (5/8, lead; r14-1 REAL WINDOW) — a file's first visit in Junction's commit walk
  allocates 7.0 MB on all threads against 454 KB a revisit: tokenizing a 300-line C# file 6.4–10.5 MB off the UI
  thread; the colours landing remakes the 34–37 rows on screen (rows unkeyed; keying them measured: +2.5 % bytes).
- **junction-files-head-key** (6, lead, unconfirmed; r14-6 REVIEW) — the Files view's file body and blame at HEAD are
  keyed `{repo}|HEAD|{path}` with no generation (JunctionApp.Files.cs:677–680, Blame.cs:87–93): an open file may keep
  showing old content after a commit. Correctness; check with the right loader first.
- **junction-summary-overtaken-at-start** (6, lead; r14-6 REVIEW) — AsyncLoader drops a superseded result, so a
  working-tree callback during the first full summary restarts it: with a log appended every 0.4–1 s the first summary
  lands at 0.8–1.3 s (1.3–2.1 s on 2 cores) after 2–4 dropped loads, its remote/for-each-ref/rev-parse/describe run 3–4
  times; on a slower machine it can be overtaken as long as callbacks keep coming (round 13's CI failure). Coalescing
  measured worse (Examined).
- **text-bidi-real-window-private** (5/3/4, check; round 14 step 1 (b), c06aa102) — after an Arabic README's walk in a
  real window (GL, the 4K at 150 %), private bytes read 158/161 → 188/189 MB after a full collection, while the GC's
  committed bytes (46.0/48.8 → 47.3/47.5) and the working set (160/162 → 162/161) stayed and headless private bytes did
  not move: commit charge outside the GC heap, never resident, in the real window only, where frames now come 14 times
  as fast (likely the GL driver's). The change makes no native object. Trace it once (VMMap-style region types) to rule
  out a leak.
- **vk-instance-extensions-once** (3; fix when Vulkan Init is next touched) — `HasInstanceExtension`
  (SdlVulkanHost.cs:532) enumerates on every call (8 `vkEnumerateInstanceExtensionProperties` calls at Init): one shared
  list saves 7.1–7.4 ms of start-up, below the 10 ms bar.

Closed from Waiting by the round 14 survey (→ Examined unless named): junction-git-optional-locks,
junction-commit-first-visit-frames, a11y-label-under-named-button, vesper-enter-two-frames (→ step 6 (b)),
junction-commit-detail-whole-patch (→ step 2).

## Landed

Round 13 (the round round 14's survey reviews; it moves to the archive at round 15's survey). Rounds 1–12 are in
[the archive](17-performance-team-ledger-archive.md#landed-rounds-14) ([round 5](17-performance-team-ledger-archive.md#landed-round-5),
[round 6](17-performance-team-ledger-archive.md#landed-round-6), [round 7](17-performance-team-ledger-archive.md#landed-round-7),
[round 8](17-performance-team-ledger-archive.md#landed-round-8), [round 9](17-performance-team-ledger-archive.md#landed-round-9),
[round 10](17-performance-team-ledger-archive.md#landed-round-10), [round 11](17-performance-team-ledger-archive.md#landed-round-11),
[round 12](17-performance-team-ledger-archive.md#landed-round-12)).

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 13 | 15f97351, 013e1f1b, 8e05c94e | reactivity-junction-watch-reftable + reactivity-junction-watch-lock-echo | (a) The objection to b6c6eb6f: GitWatcher also watches `.git/reftable` when it exists; the `tables.list` rename is a ref move, a reftable lock alone is not heard (no new echo). (b) In the refs folder a `*.lock` event or a folder's own Changed is a working-tree event; the reflog panel keys on DataGen (dropping lock events outright left 4 of 53 operations unheard: DECISIONS.md). Watcher probe, 53 real git operations (git 2.53), old, cda4bcd8 and new watchers side by side: 0 regressions against either, both backends; reftable ref moves heard as one 5/29 → 29/29, stale operations 29 (old) / 35 (cda4bcd8) → 8 (the 8 are index-only and linked-worktree changes all three miss); files backend the same 7 stale, refs reloads for nothing 7 → 5. Headless Junction, 9 outside operations: reftable 7/9 stale → 0, files 0 → 0. Headless no-op background sweep 17 → 5 git runs, UI 253.8 → 23.7 KB, all threads 824.9 → 180.7 KB, 2 → 1 builds and frames; toolbar Fetch with nothing new 28 → 17 runs, UI 1,262 → 1,031 KB (−18 %), all threads 2,223 → 1,607 (−28 %). Real window (NativeAOT, GL, the 4K at 150 %, jrepo clone, idle-gated, alternated ×2, 2 holds, 0 foreground takes): a no-op fetch 14 → 2 git runs, all threads 4,455 → 171 KB, UI 514 → 22 KB, 2 → 1 builds and frames; an outside checkout on reftable 4 runs / 1.65 MB stale → 16 runs / 6.1 MB, the correct full reload; on files unchanged. step-ab (cda4bcd8) `-Pairs 2`: JIT 0/0 both pairs (A/A: video bar fade +18 %, a11y edit +10 %), AOT 0/0, no allocKB moved. 5 new tests, each fails under a reverse edit (`[ReftableFact]` skips below git 2.45); Headless 1,572, Junction 1,104 pass. REACTIVITY.md watcher traps (reftable, lock files). |
| 13 | 0da9f80e, 185bedad | layout-hover-move-frames | An input's hover clause takes the settle's rule (c0ce9387): `HandleInputEvent` notes `DrawnHoverChanges` at the start of an input, and a changed hover asks for a frame only when that count moved or DevTools is open; hover styles, `HoverHighlight` tints, scrollbars, tooltips and enter/leave handlers still mark their own frames. Headless JIT, both rules in one process, 8 px / 12 px sweeps: Junction 761 moves 290 → 117 frames (−60 %), Vesper 292 moves 161 → 40 (−75 %), Meridian 971 moves 303 → 135 (−55 %); frames that only redrew what was on screen 191 → 2, 129 → 0, 179 → 3. Real windows (NativeAOT, the 4K at 150 %, 120 Hz, moves in-process, idle-gated): Junction (2 launches, order swapped) 244/258 → 129/114 frames, UI-thread cycles 664/699 → 380/355 M (−43/−49 %), diff sweep 42 → 3 frames, History 48 → 17; Meridian 194/180 → 125/140, 270/252 → 206/209 M (−24/−17 %); Vesper (1 launch; the in-hold idle gate skipped the swapped one twice) 108 → 38, 269 → 101 M (−63 %); bytes a sweep unchanged; 0 foreground takes. Stale check 0: the shown frame equals a full render after each of 2,024 sweep moves and 280 move-then-leave moves, both rules, and after 12 keys under a resting pointer in Junction and Vesper. step-ab (f4b15d54) `-Pairs 2` JIT + AOT: 0 slower, A/A clean; "loop/tooltip delay" −49 % both runtimes (JIT 0.489 → 0.250 ms; the move onto a tooltip owner draws no frame of its own), allocKB 2.7 → 2.5. `HoverMoveFrameTests` 5 + Junction `APointerSweepDownTheDiff_DrawsNoFrame`, each fails under a reverse edit; Core 5,085, Headless 1,577 (goldens), Vesper, Meridian, Aperture, Reel, Fluent, Media, Terminal pass; Junction 1,104 of 1,105 (`GitLab.ReviewTests.AReplyAndAResolveGoToTheThread` timed out loading the review under suite load, before any pointer input; 17/17 alone). ELEMENTS.md and the `IsHovered` doc: an element drawing from its own `IsHovered` gets no frame; use `.OnHover` or an enter/leave handler. |
| 13 | 698c2758, 54bd7d3a, b8fd2c67 | text-cjk-cuts-at-need | A word a prefix table cuts short is read off cuts made once: (a) a cut word with no cuts yet gets them at its first cut; (b) a table's last word cut by the table's end is read off the word to its natural end (≤ 1,024 characters; only from a line start, after a space or a font-run boundary); a row's later probes read the last cuts given with no word-cache lookup; natural-end words are made only while words read again earn more credit than words let go spend (without it a document past the word cache was 10–12 % slower). Real ja/zh READMEs in Junction's Files view (r13-5's `mkreal.py`). Headless NativeAOT, idle-gated, 2 alternated pairs, UI-thread CPU (min), HarfBuzz calls, UI KB: Japanese 1000 px 13.5 → 12.3 ms (−9 %), 2,086 → 1,119 calls, 989 → 777 KB; drag at 1440 272 → 269 ms, 1,854 → 517 calls, 1,468 → 802 KB; drag at 960 359 → 344 ms (−4 %), 11,628 → 3,911 calls, 6.1 → 2.8 MB. Chinese 1000 px 11.9 → 11.7 ms, 3,231 → 1,433 calls; drag at 1440 278 → 281 ms, 2,227 → 591 calls; drag at 960 651 → 337 ms (−48 %), 295,322 → 3,662 calls, 88.6 → 2.0 MB. Real window (the 4K at 150 %, 8 guarded launches in 3 holds, 0 foreground takes): Chinese drag at 960 694 → 381 ms (−45 %), 291,890 → 2,453 calls, 87.7 → 1.4 MB; Japanese drag at 960 390 → 366 ms (−6 %); the 1440 drags and 1000 px within ±2 %. Costs: Chinese, the first width that wraps a line +13 % headless (9.3 → 10.5 ms, +290 KB) and +9 % in the real window, opening the file +4 % (+250 KB); a 154k-character document past the word cache, drags +0.1 %, resizes −2…+7 %; Latin (TODO.md) fewer calls, times within noise. Corpora unchanged (48k 7A18AF8DE8E05271, 240k late and reference F09E9038726E4BD6; CJK wrap 0 of 1,344,616 widths differ, widths 1E6F1A93894D0B6A, rows 6C7FA51792E8EB84; reads off cuts 403,333 → 727,446); goldens byte-identical. step-ab (26bf78b9) `-Pairs 2`: JIT 0/0 both pairs, A/A clean; AOT pair 1 clean, pair 2 one row +11 % (`a11y/tab among 10 fields`, no prefix table, absent elsewhere: noise); `text/resize a wrapped TextArea` 12.5 → 12.2 KB. PrefixCutsTests +4 (five reverse edits fail them; two perf-only mutants stay exact and pass); TextAreaFitsByCountTests' bar relaxed from a twentieth to a third (the search it compares with now allocates 75 KB, not 1,074). Windows Text 302, Rendering.Skia 410, Headless 1,577, Junction 1,105, Vesper 931, Meridian 678; WSL Fedora Rendering.Skia 410, Text 302, Headless 1,403. DECISIONS.md: 34dbc7fc's note rewritten with the real-text numbers (b8fd2c67). |
| 13 | 8f9f2683, da72e01b, d413e001, 0a8361f9 | reactivity-junction-watch-view-skip + apps-junction-summaryfor-closures + reactivity-junction-own-action-echo | (a) A watcher event builds the view only when the view's last build read the generation the event moved, or a ref moved in the repo on screen (History also builds when the issue-tracker file changes). Headless per event: another repo's save 86.0 → 70.0 KB UI, 2 → 1 builds and frames, a touch there 18.4 → 2.4 KB, 1 → 0; History save 166.4 → 150.8 KB, 5 → 4 builds, 2 → 1 frames, a touch 20.5 → 2.7 KB, 1 → 0; Changes unchanged. Real window (NativeAOT, the 4K at 150 %): a save in the shown repo 205.6 → 187.8 KB UI, 5 → 4 builds, 2 → 1 frames; in another repo 91.1 → 71.9 KB, 2 → 1; touches 21.5 → 1.9 and 19.5 → 1.6 KB, 1 → 0; empty builds 16 → 4 a launch. (c) `SummaryFor` builds its key and closures only when the data generation moved (tracked per loader in a weak table; no API): History Down 29.8 → 27.2 KB a key (−8.7 %), History save 150.8 → 144.8 KB. (b) Built as neither proposed option: `RefreshAll` calls a new `GitWatcher.Forget()` on the shown repo's watcher, which drops what was heard so far (a callback already posted included), since the reload starting then reads it all; no git run added, nothing paused (the fingerprint cost 1–2 git runs and delayed every reload; the pause loses outside changes: DECISIONS.md). A checkout headless 28 → 15 git runs (14 with no watcher), UI 987 → 747 KB (−24 %), all threads 1,975 → 1,288 KB (−35 %), 17 → 15 builds, reftable the same; real window 28 → 17 runs, UI 1,740 → 1,215 KB (−30 %), all threads 10.58 → 6.52 MB (−38 %). Final PNGs and a11y trees as before (History, Changes, after checkouts on both backends); 2 real-window holds, alternated ×2, 0 foreground takes. step-ab (0d76fdb6) `-Pairs 2` JIT + AOT 0/0, no allocKB moved (no Junction rows in the suite). New tests fail with their part reversed; Headless 1,577, Junction 1,111 pass. REACTIVITY.md. (da72e01b's body says "16 asks for two key moves": it was 16 extra asks over the test's key presses.) |
| 13 | 4fdb56cb, 276789d8, cdb47cb9 | painting-vk-dead-size-stencils + media-mpv-event-wait-forever | (a) A Vulkan window frees the stencils of sizes it no longer shows once its size has rested: 1 s after the last change, and only after a frame drawn since the old targets went, the surface runs `PurgeUnusedResources` with a cutoff just after the targets were let go and never after the last frame began (a cutoff at the last frame, in whole ms, dropped on-screen images when `Window.SaveFrame` had read the frame back in that millisecond); it runs from a new loop deadline, `SurfacePurge`, at the end of SDL3Window's pump, which the modal size loop never reaches; no public API (one InternalsVisibleTo, Rendering.Skia → Platform.SDL3). Aperture signed in, NativeAOT, the 4K at 150 %, 8-bit, idle-gated, alternated (4 launches, then 3 with a wheel pass first), after 18 size changes: stencils 9 (21.77 MB) → 1 (2.78 MB); Skia budget 32.0–34.7 → 11.2 MB; DXGI local / non-local 104.8–109.8 / 30.6–32.1 → 84.0–85.0 / 25.1–26.6 MB; private at rest 200–207 → 181–184 MB, at the end 211–217 → 180 MB. The purge 1.05–1.33 ms plus one loop pass after a run of size changes; re-upload cost: it dropped 11 of 35 image textures (2.58 MB), the next wheel pass uploaded 7 again (2.54 MB), UI thread over those 28 frames 20.6 ms against 20.1–20.4. Frames saved before and after the rest byte-identical, the same in both builds. (b) mpv's event thread waits with no timeout (the only two flags read outside events, `_stopping` and `_traceHwdec`, are followed by `mpv_wakeup`): Reel paused at rest mpv-events 2.34/2.40 → 0.00/0.00 wakes a second, the app's own threads 2.4 → 0, all threads but the harness 128–134 → 126 (all NVIDIA's). (c) vk-format-switch-chained not attempted (→ Waiting). step-ab (bba011a9) `-Pairs 2`: JIT 0/0 both pairs, no allocKB moved; AOT pair 1 2 faster (icons/mount 600 −22 %, the same in its A/A: noise), pair 2 clean. Tests fail under reverse edits: `AfterSizeChangesRest_TheWindowKeepsOneStencil` (6 stencils without the purge; 2.5 s late without the deadline), two mpv tests (4 wakes in 2 s with the old wait; Dispose 5,011 ms without its wakeup). Validation layer proven loaded by a deliberate fault (20 × `VUID-vkCmdDraw-None-09600`), then 0 messages. Vulkan probe 41, Rendering.Skia 416, Platform.SDL3 55, Media 87, Media.Backends 30, MpvGL 3, Aperture 363 (+21 skipped), Reel 51 pass. 0 foreground takes, 0 nvlddmkm, no stall > 1 s. HDR.md. |
| 13 | 154a7ca3, 8048b644 | layout-vesper-wrap-resize-lead | `LeadWrappedBodyWidth` in Vesper's `OnWindowResized` gives the build the wrapped viewer's and diff's body width as the after-frame check last read it plus what the window gained or lost (it leads only from a width the check saw; the build clears the lead in `OnAfterBuild`); `SettleWrappedRowWidths` stays as the check. Headless JIT, 40 steps of 4 px, the lead off in the same binary as HEAD's path, alternated ×2, per step: viewer 2 → 1 frames and builds, PatchFroms 128 → 64, render visits 394 → 197, 363 → 299 KB (−18 %); diff 2 → 1, 172 → 86, 552 → 276, 502 → 417 KB (−17 %); 1.5× the same counts; AOT 361 → 297 and 499 → 415 KB; wrap off unchanged. Real window (NativeAOT, 1× monitor, steps on the window's own thread 16 ms apart, idle-gated, 1 hold, 0 foreground takes): viewer 2.03 → 1.03 frames, 2 → 1 builds, UI-thread time 3.35 → 2.87 ms (−14 %), 360 → 297 KB; diff 2.03 → 1.03, 4.31 → 3.66 ms (−15 %), 500 → 415 KB. Pixels (viewer and diff, wrap on/off, 1×/1.5×, grows, shrinks, a scale and a height change): settled frames byte-identical to HEAD's 104/104, scroll offsets equal 104/104; the first frame after a size change now equals the settled one 104/104 (HEAD drew stale heights in 2 and 4 of 9 two-frame steps). step-ab (61836127) `-Pairs 2` JIT + AOT 0/0, no allocKB moved (no suite row runs Vesper). `WrapResizeFrameTests` 3 (all fail with the lead removed; the wrong-guess test fails with the check's rebuild removed); Vesper 934 (+4 skipped), Headless 1,577 pass. LAYOUT.md. |
| 13 | 5849c137 (docs only) | apps-vesper-nav-uia-share (step 5 (b), measured: nothing to cut) | Real window (NativeAOT Vesper at 375ac33b with a probe, the 4K at 150 %, 2 holds, 12 launches, idle-gated, a11y on and `HULTUI_A11Y=0` alternated ×2, UI-thread CPU by `QueryThreadCycleTime`). Expert 8's scene (NavigateTo ×30 at 100 ms, 61 ↔ 28 files, pointer on the list): "other" 31–34 % of the six phases against 1 % off; the bridge 0.59–0.62 ms of the UI thread's 2.38 ms (1.75 off), ~75–80 % of it raises: 51 a navigation (36 Name, 13 BoundingRectangle, 2 StructureChanged), 0.46–0.49 ms CPU; the in-place walk 0.07–0.09 ms (1.5 a navigation, structural, 0 trees); wrapper upkeep ~0.02 ms. Each raise: 13 provider calls and 13 QIs, all on the UI thread (GetProviderOptions 5, host provider 3, fragment root 2, runtime id, native window handle, one Navigate), 0 on worker threads, 0 root asks; our answers < 2 µs of a 13.5–14.4 µs raise, the rest UI Automation's own. Enter into 200 files and Backspace out: 35 raises a navigation (29 Name: the 26 rows that kept their index, the pane, its title, its footer), 0.48–0.52 ms of 2.5–2.7 ms (2.05–2.09 off), 2 walks, 0 trees, bridge bytes 48 KB in / 9 KB out. Viewer open 0.29–0.30 ms over 2 frames, 8 raises, 179 KB (the 300 lines' item nodes and labels a reader is given); close 0.21–0.22 ms, 3 raises, 84 wrappers. Every event is about an element a reader can still reach, none told twice, rows that left raised nothing, each list's change of children one event: nothing cut (DECISIONS.md). |
| 13 | 8ca081b8, 628bcb58, 505123d7, 21735c50 | a11y-labels-kept-across-invalidaterows (question 52: option 2) | `InvalidateRows`, a theme switch, a display-scale change and a list dropping sticky rows keep the item labels; new Items or a new label delegate drop them, and a new delegate with labels kept asks for the frame's walk, so a reader hears the new names even when no row is made again; `InvalidateRow` still drops its one label. Apps given a new delegate: Meridian's History at a day change (8ca081b8) and Vesper's Find results, whose label reads `Dir(_findSide)` (628bcb58); every other caller and delegate reads only the item, uses `RowsAreComposite` rows or the one-argument template. Real window (NativeAOT, only the 1440p at 100 % attached that hour, one hold, idle-gated, before/after/after/before), UI KB with the bridge on: Junction start 3,113.3 → 2,238.1 (−28 %; a11y share 1,249 → 382 KB, walks over 100 KB 5 → 1); a History row-shape change (crossing the compact width) 801.8 → 585.0 in, 692.8 → 470.8 back (−217 KB each). A cross-process UIA client read the same names by hash (Junction's 1,000 items before and after a compact crossing; Meridian's 300 sends). Headless: the 1,000 labels kept through all 4 start rebuilds, the 216.8 KB walk frame gone, rebuild frames byte-identical. Event streams: 117 scenarios identical with d3c9bfeb. Audit: 0 WALK-VALUE/EVENT, fallbacks 10, STALE 10 — all in Headless `MiddleClickAutoScrollTests` (click handlers added to plain boxes after mount), the same with d3c9bfeb's VirtualListElement, and RIDE (told on a later frame) in the survey's audit at 2ced4e26: not this step (→ step 5 (c)). step-ab (d3c9bfeb) `-Pairs 2` JIT + AOT 0/0, no allocKB moved, A/A 0/0. Tests fail under reverse edits (labels kept through `InvalidateRows` and a theme or scale change; a new delegate alone tells the new names; Junction's start makes each label once; Meridian follows midnight, Vesper the folder). Core 5,085, Headless 1,580, Linux bridge 104 (Windows and Fedora), Junction 1,112, Vesper 935, Meridian 679, Aperture 363, Reel 51, Fluent 34. ELEMENTS.md ("Item labels a list keeps"), ACCESSIBILITY-PLAN.md, the XML docs. |
| 13 | d73b1439, 2816c888 | a11y-handler-added-after-mount (step 5 (c), the audit's 10 STALE) | Not perf. Bisect on `MiddleClickAutoScrollTests` with the audit: HEAD 10 STALE + 40 RIDE "Added BoxElement Button", with 0da9f80e's Window.cs change reversed 0 STALE + the same 40 RIDE — step 2 exposed it, did not cause it: the old hover frame was paint-only and walked nothing (readers heard of the rows at the scroll's layout refresh, five frames later, under both rules); it only made the audit skip the frame (the audit looks only at frames with nothing dirty). The older cause: adding a click handler to a mounted box, or a rebuild dropping its last one, marked nothing for accessibility. Fix: `Element.AddEntry` and `CopyAppHandlersFrom` notice a box's first click handler arriving or its last going and notify through an internal `AccessibilityProperties.ClickHandlers`; the window's observer sets `_a11yWalkDue` when `AccessibilityTreeBuilder.ClickDecidesRole` (no role set, not a ButtonElement, no other inferred kind), which asks for a frame that walks in place and draws nothing; no reader, no frame; step 2's rule kept; no public API. Audit, 7 suites, against step 5 (a)'s run: STALE 10 → 0, WALK-VALUE/EVENT 0, Headless RIDE "Added Button" 57 → 17, fallbacks 10 → 11 (the extra in Junction's GitLab ReviewTests, IsEnabled, a different test each run; the new route never ran in Junction's suite: likely noise, no baseline taken). Event streams 117 identical. Real apps: none adds a click handler to an element already in a window (274 sites act on fresh elements); rebuilds in Aperture, Meridian and Vesper add or drop handlers on kept boxes (887, 402, 150 changes) in frames already told. step-ab (f07506d0) `-Pairs 2`: AOT 0/0 both pairs, A/A 0/0; JIT pair 1 0/0, pair 2 one row +11 % (composite selection walk, bridge on; best mins +0.5 %; all five composite-walk rows 7–10 % slow in that run only: drift; A/A icons/mount 600 +26 %). `A11yClickHandlerAfterMountTests` 4 (reverse edits fail); Core 5,085, Headless 1,584, Linux a11y 104, Junction 1,112, Vesper 935, Meridian 679, Aperture 363, Reel 51, Fluent 34. ELEMENTS.md, ACCESSIBILITY-PLAN.md. |
| 13 | cd7e3db2, 12adfdca | gl-sdl-timer-resolution-at-rest (question 53: build it, measured with Steam closed) | Windows only, no public API: SDL's `SDL_TIMER_RESOLUTION` hint is "0" from SDL_Init, "1" before any wait shorter than 32 ms, "0" again one second after the last such wait (a probe confirmed SDL 3.5.0 calls timeBeginPeriod/timeEndPeriod on each change). Machine-wide resolution with Steam closed (the user, one 231 s hold, alternated ×2): no app 15.625 ms (2/2); Junction at rest 1 ms on the old build, 15.625 ms on the new (2/2 each); the new build 1 ms while animating, back to 15.625 ms within 6.5 s. Clock interrupts not shown (NtQuerySystemInformation's summed interrupt count can't separate them: 4,415–6,925 a second either way); 1 ms against 15.625 ms is 1,000 against 64 ticks a second only by Windows' definition. A probe thread sleeping 5 ms: GL apps 171–179 → 62.5–64.3 loops a second; Reel on NVIDIA Vulkan unchanged (~172–178; the driver holds 1 ms). The app's own wakes at rest unchanged (Junction 10.5/10.8 → 10.0/9.9, Reel GL 20.9/20.3 → 19.3, Reel Vulkan 113.6 → 112.6/112.9; the UI thread 0 passes). Frame pacing unchanged on the 1440p at 200 Hz and the 4K at 120 Hz (tween 200.0–200.2 and 120.0–120.2 fps, median 5.00 and 8.1–8.4 ms; a 1,000 Hz event storm and a 16 ms drag the same; the first paced frame after a rest on time, also when the machine's clock has to switch); Reel GL video 0 dropped, 0 delayed. Owned deadlines at rest on GL: a 250 ms tween delay 0.29–0.68 ms late on average → 9.2–12.3 ms (15.05 at most); on Vulkan ~0.5 ms both. step-ab (01c7a5d4) `-Pairs 2`: AOT 0/0 both pairs, no allocation moved, A/A 0; JIT pair 1 one row +1.7 % past the threshold ("component/reverse 1,000 keyed rows"), pair 2 none, A/A 0 (the headless suite never reaches this code). New policy and hint tests (reverse edits fail 3 and 6 of them); SDL3 tests, Headless 1,584, UiTests 25 + 50 real-window ones under the lock, every app suite pass. THREADING.md, ENVIRONMENT.md. 5 holds, 0 foreground takes, 0 nvlddmkm. |
| 14 | 2c809bc1, c06aa102, 5d9a5c71, 11ee4926, 9fd8cf07 | text-cjk-natural-end-trim (the review objection to 698c2758 (b)) + text-bidi-textarea-probes + text-word-cache-cjk-cliff (question 56: yes, the admission failed) | Junction's Files view on real gettext READMEs (zh 75,265, ja 79,011, both in one file 154,276, ar 76,025, he 27,913, th 41,708 characters; ACCESSIBILITY-PLAN.md with 208 emoji); NativeAOT probe host, each change behind a switch in the same binary, idle-gated, alternated ×2, UI-thread CPU min. (a) A table's last segment the word cache has is its own answer, and a natural word also ends at the first ASCII letter/digit ↔ other change at or past the table's end: zh resize to 1000 11.4 → 10.6 ms (808 → 555 KB, 1,433 → 1,141 calls), 1200 −7 %, 960 −8 %, drag at 960 1,928 → 1,353 KB; ja 1000 12.1 → 11.2; the 154k file ±1 %; real window zh 1000 13.3 → 11.6 ms (815 → 562 KB), ja 13.7 → 12.2. Slower: zh's frame back at 960 8.7 → 16.7 ms, a gen-1 collection that moved in (8.8 → 9.0 when it lands in the drag). (b) A TextArea's probes of a BiDi line are `MeasureTextWidth(span)` widths, and an unbounded BiDi line skips the break pass: Arabic drag at 1440 5,759 → 460 ms (−92 %; 830.9 → 44.4 MB; 4.37 M → 217k calls), drag at 960 7,014 → 701, open 170 → 98 (−42 %); Hebrew drag at 960 831 → 269 ms, Thai 295 → 152, emoji 83.8 → 79.2; real window Arabic drag 5,810 → 426 ms (841.8 → 32.8 MB). Slower: Thai at 1320 18.0 → 18.7 ms (+4 %, both pairs). Held: full-GC heap Arabic +3 MB (the width cache at its 4 MB bound); real-window Arabic private bytes +28–30 MB outside the GC heap and never resident (Waiting text-bidi-real-window-private). (c) A scan-resistant admission declined (4 variants: the big drag 157 → 184 / 177 / 63 / 76 MB, zh at 960 worse; DECISIONS.md); the bounds raised to 32,768 entries / 16 MB: the 154k file's drag at 1440 1,071 → 451 ms (157.5 MB → 953 KB; 396,885 → 908 calls), at 960 1,411 → 597, a new width −43…−52 %; real window drag 1,090 → 466 ms (156.9 MB → 708 KB); zh's frame back at 960 17.7 → 8.1 ms; TODO.md the same bytes and calls; held: cache 3.4 → 10.5 MB (the 154k file), zh 3.7 → 4.7, ja 4.0 → 6.0; private bytes 44.7 → 42.7 MB headless, 173 → 170 real window. Exact: 48k 7A18AF8DE8E05271, 240k F09E9038726E4BD6, CJK wrap 0 of 1,344,616 widths differ (1E6F1A93894D0B6A / 6C7FA51792E8EB84); 46,997 probe widths and layouts on the four READMEs 0 differ; goldens pass. step-ab (ded3e51c) `-Pairs 2` JIT + AOT 0/0, A/A 0/0; text/create 2,000 labels +192 KB (the cache's slots grow once). Tests fail under reverse edits; Windows Text 303, Rendering.Skia 421, Core 5,086, Headless 1,584, Junction 1,112, Vesper 935, Meridian 679; Fedora Rendering.Skia 421, Text 303 (9fd8cf07: the BiDi shaper bound 0.75, Noto faces give 0.51). DECISIONS.md. 4 holds, 0 foreground takes. |
| 14 | 44958e6e | junction-commit-detail-whole-patch, part: the parser's markers ordinal (the per-file load waits for question 60) | `ParseUnifiedDiff`'s nine `StartsWith(string)` calls are ordinal: on this sv-SE machine each was an ICU collation lookup (~890,000 on one plan merge; the suite's InvariantGlobalization never paid it), and a culture match is wrong too (a soft hyphen before "diff --git " made a second file). Parse of a whole patch, JIT, pinned, warmed, min / median of 10, alternated ×2: facced69 (8.0 M characters) 203.4 / 208.4 → 20.2 / 21.3 ms (−90 %), 61308cfd 148.1 → 13.4 ms (−91 %); bytes unchanged. Shared by the Commit tab, Changes, the range compare and conflicts. `DiffParsingTests.MarkersAreMatchedOrdinally_WhateverTheCulture` fails with the old comparisons; Junction.Tests 1,113 pass. |

## Examined, not worth it

From the round 14 surveys (details in `17-reports/r14-*`):

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
- **Framework batching of the main-thread drain or of `MarkDirty`** (6): an await continuation that writes and then reads
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

No question is open from rounds 1–13. Their answered questions are in the archive
([round 13](17-performance-team-ledger-archive.md#needs-the-user-round-13), [round 12](17-performance-team-ledger-archive.md#needs-the-user-round-12),
[round 11](17-performance-team-ledger-archive.md#needs-the-user-round-11),
[round 10, with round 9's 26 and the earlier 10](17-performance-team-ledger-archive.md#needs-the-user-round-10),
[round 9](17-performance-team-ledger-archive.md#needs-the-user-round-9), [round 8](17-performance-team-ledger-archive.md#needs-the-user-round-8),
[round 7](17-performance-team-ledger-archive.md#needs-the-user-round-7), [round 6](17-performance-team-ledger-archive.md#needs-the-user-round-6)). Search it by candidate id
before asking anything an earlier round asked. Recommendations in brackets. Question 50 (a11y-gate-on-client)
and 51 (after-layout-callback) were answered "not yet" and "only if another app needs it"; both are Waiting.
Round 14's a11y-items-on-demand-sooner and apps-junction-ref-move-a11y wait with question 50 (Waiting).

Round 14 questions (from the survey, 2026-11-13). Recommendations in brackets.

55. **media-interop-gl-preload** (r14-3; step 8): the video interop loads the GL driver inside the first video frame, on
    the UI thread (~50 ms of the first video frame in Reel opened with a film, and Aperture's first playback). Loading
    it on a worker first is outside SDL's documented threading contract (validated on Windows/WGL); today it is
    `HULTUI_GL_PRELOAD`'s opt-in, which Reel takes on Windows. (a) The framework does it by default on Windows for
    Vulkan windows with interop, or (b) it stays opt-in and Aperture opts in as Reel does (one line). [(b)] → **(b):
    opt-in, Aperture opts in as Reel does** (2026-11-13; step 8).
56. **text-word-cache-cjk-cliff** (r14-5; step 1 (c)): a CJK document whose words at one width pass the word cache's
    bounds (8,192 entries, 4 MB) re-shapes the whole document every frame: the 154k-character ja + zh file drags at
    157.6 MB headless and 156.9 MB in a real window. Raising the bounds to 32,768 entries / 16 MB: 1.1 MB and 0.78 MB,
    HarfBuzz calls 397k → 1,087; the cache keeps up to ~12 MB more while and after such a document is shown (Latin text
    fills ~2 MB; the process still ended 6.6 MB smaller). Step 1 (c) first tries a scan-resistant admission that changes
    no memory. If it does not remove the cliff, raise the bounds? [Yes, only if the admission fails.] → **yes, only
    if the admission fails** (2026-11-13; step 1 (c), told to its running agent).
57. **text-open-measures-every-line (full)**, asked again (2026-10-13: not now): may a TextArea measure only the lines
    near the viewport and estimate the rest of the scroll extent, refining it as lines are drawn (the scrollbar thumb can
    shift while scrolling)? Round 14 on real text: with ~40 rows on screen a drag frame makes 1,140–2,734 prefix tables
    and itemizes 131k–169k characters (zh/ja READMEs); the 154k-character file 2,178–5,288 tables and 9,900–13,550
    HarfBuzz calls a frame; an Arabic README 1,846 BiDi probes a frame. It is the root of step 1's costs. Large. [After
    step 1 lands, re-measure what is left and decide then.] → **re-measure after step 1, then decide** (2026-11-13;
    step 1's report gives what is left a drag frame; the question is asked again with those numbers).
    **Asked again 2026-11-14 with step 1's numbers.** Per drag frame after step 1, prefix tables / characters itemized /
    HarfBuzz calls: zh at 1440 1,140 / 131k / 11, at 960 2,734 / 169k / 57; ja 1,038 / 120k / 12 and 2,554 / 166k / 74;
    the 154k-character file 2,178 / 251k / 23 and 5,288 / 335k / 129; Arabic 1,845 BiDi probes and 5,429 calls at 1440,
    4,870 and 9,142 at 960. The shaping is gone; the O(document) itemizing stays: the largest drags now take ~11–12 ms a
    frame (the 154k file 451 ms and Arabic 460 ms per 40 frames, headless AOT), inside a 60 Hz frame. [Not now: what is
    left fits a frame on the largest real documents; keep it Waiting for a document that does not.]
58. **reactivity-junction-watch-index-staging** (r14-6): staging from a terminal or IDE (`git add`, `restore --staged`, a
    staged submodule bump) is never heard, so Changes stays stale until the next working-tree event. Hearing index
    renames again (and answering with a status) fixes 3 of 53 real git operations on both backends with 0 regressions,
    at the cost of 1–2 extra working-tree reloads after each outside checkout, pull or stash (2 git runs each over
    History, ~11 over Changes) and the app's own status heard back once or twice. Build it? [Yes: a correctness fix.]
    → **yes** (2026-11-13; step 3 (c)).
59. **Hover after a scroll** (r14-1): when content scrolls under a resting pointer, the list tint and hover styles stay on
    the row that scrolled away until the pointer moves (Junction 4 of 4 wheel series, Vesper 3 of 4). Should hover
    follow the row now under the pointer, as browsers do? One hit test a scroll frame (27–38 visits) before the frame
    draws, no extra frame; it changes where the tint is drawn during a scroll. [Yes, with before/after images from
    headless frames first.] → **yes, with before/after images first** (2026-11-13; step 9: the images go to the user
    before the change is committed).

Reminder (expert 8): Aperture signed in to jellyfin.home as device "aperture-survey" in earlier rounds, as "aperture-r13" in round 13, and once in
round 12 as "aperture-r12"; the user should remove those devices and revoke the API key when the work is done.

Standing notes (from earlier rounds):

- **Aperture API key file** at `C:\Users\fredr\.secrets\jellyfin-api-key.txt` (outside the repo; never
  copy or print its contents; path only, e.g. `--api-key-file <that path>` with the run-hultui-app driver,
  server `http://jellyfin.home:8096`). The user should revoke the key and remove the "aperture-survey"
  devices on jellyfin.home when the work is done.
- Rounds 1–4's questions and answers are in [the archive](17-performance-team-ledger-archive.md#needs-the-user-rounds-14).
  Search it (by candidate id) before proposing anything an earlier round already asked about.

## Open

- **8a38ef4 on Linux/KWin and on AMD/Intel Windows:** not measured. Linux keeps the chained
  path. If AMD/Intel do not stall, destroy-first may cost them 21–24 ms per resize, as it
  cost NVIDIA's IMMEDIATE and FIFO. A real Aperture resize run is also still to do.
- **4315c4b on AMD, Intel and Linux (GLX/EGL):** Linux answered in round 10 (r10-3): WSL Fedora has Mesa 26.2.3
  (EGL/DRI with llvmpipe and d3d12) since 2026-10-09, and there depth 0 plus stencil 8 resolves to 24/8: nothing
  fails and nothing is saved (Mesa has no stencil-only config; the gfx line, SDL3Window.cs:1675–1679, reports it). AMD
  and Intel on Linux use the same Mesa lists (inferred). AMD and Intel on Windows (WGL) still not measured. Depth is a
  minimum in WGL/GLX/EGL, so 0 cannot fail where 16 succeeded.

## Log

From 2026-11-12 (round 13's survey); earlier entries are in the archive ([to 2026-10-25](17-performance-team-ledger-archive.md#log-2026-10-07-to-2026-10-25),
[2026-10-26 to 2026-10-27](17-performance-team-ledger-archive.md#log-2026-10-26-to-2026-10-27),
[2026-10-27 to 2026-11-02](17-performance-team-ledger-archive.md#log-2026-10-27-to-2026-11-02),
[2026-11-02 to 2026-11-03](17-performance-team-ledger-archive.md#log-2026-11-02-to-2026-11-03),
[2026-11-03 to 2026-11-08](17-performance-team-ledger-archive.md#log-2026-11-03-to-2026-11-08),
[2026-11-08 to 2026-11-09](17-performance-team-ledger-archive.md#log-2026-11-08-to-2026-11-09),
[2026-11-09 to 2026-11-11](17-performance-team-ledger-archive.md#log-2026-11-09-to-2026-11-11),
[2026-11-11 to 2026-11-12](17-performance-team-ledger-archive.md#log-2026-11-11-to-2026-11-12),
[2026-11-12, round 12](17-performance-team-ledger-archive.md#log-2026-11-12-round-12)).

- 2026-11-12: round 13 survey started in a fresh context (session 17c11f5e; usage 22 % weekly). Main has not moved
  (origin/main afc1ed40 is in the branch). Moved to the archive by a checked script (`archive_r13.py`; every moved line
  found there, nothing lost): round 11's 15 Landed rows, round 12's build order with its closed-from-Waiting note, round
  12's answered questions 46–51, and round 11's 100 log lines (2026-11-11 to its release on 2026-11-12). Both monitors
  are attached (the 4K at 150 % primary, the 1440p to its left). The eight experts survey round 12's build,
  5f1744ca..afc1ed40 (29 commits, one merge: round 12's own into main), in their own worktrees on this commit, in real
  windows too.
- 2026-11-13: round 13 survey done (reports da6e72c8..8f01b6fd, `17-reports/r13-*`, ~75 min). .NET runtime satisfied;
  the other seven not. Review objections: b6c6eb6f (a reftable repository's ref moves read as working-tree events, so
  Junction stays stale after an outside checkout; correctness) → step 1; 34dbc7fc's DECISIONS.md note (measured on a
  generated fixture only) → step 3; 053a35ef and b6c6eb6f +0.8 KB a Junction build (below the bar) → step 4 (c). Round
  12 helped the real apps (67 walks geomean 1.003 headless; commit walk 1.00 frame a key from 2.00, Vesper navigation
  1.30 from 2.37, Vulkan rest 110–118 wakes a second from 207). Real windows: ~104 guarded launches in ~23 holds of
  `Global\HultUI-r13-desktop`, 0 foreground takes by the apps, 0 driver events, 0 screen reads. One disturbance: expert
  2's first batch dot-sourced `driver.ps1.inc` (not a .ps1 name), so the shell opened it in Visual Studio on the user's
  desktop; the user closed it, the batch failed before any window, and the helper is now `driver.inc.ps1`, checked
  before the lock is taken (my Stop-Process of the waiting batch was refused by the permission check). Merged into an
  8-step build order (one owner each; expert 2's stencil purge and expert 3's mpv wait in one Vulkan step; expert 8's
  `SummaryFor` objection into expert 6's step 4; expert 8's Vesper bridge lead into expert 7's step 5), 7 Waiting items
  closed, 4 added, questions 52–54 asked. For PERFORMANCE.md: an NVIDIA Vulkan device runs four polling threads and
  holds 1 ms timer resolution for its life (a Vulkan app rests at ~112 wakes a second here, a GL app at ~10); SDL holds
  1 ms for the app's life, so exclude probe threads from wake counts (a Sleep(5) loop wakes 170 a second at 1 ms); a
  Vulkan window keeps a stencil for each size it drew at; posted mouse moves count only while the real cursor is inside
  the window; mind the logical width (a 1,440 px window at 150 % is 960 logical); rank text work on real text, not
  generated characters; dot-source only `.ps1` files. Round 14's survey reviews round 13's build, 8f01b6fd..(round 13's
  release). Phase → build.
- 2026-11-13: round 13 step 1 landed (15f97351, 013e1f1b; captures 8e05c94e): Junction's watcher hears a reftable
  repository's refs move (the objection to b6c6eb6f: reftable ref moves heard 5/29 → 29/29, 0 regressions over 53 git
  operations on both backends, headless 7 of 9 outside operations stale → 0), and a ref's lock file alone is a
  working-tree event (a no-op fetch in a real window 14 → 2 git runs, 4,455 → 171 KB on all threads; the toolbar Fetch
  −18 % UI, −28 % all threads). step-ab 0/0 JIT and AOT. For PERFORMANCE.md: many git commands take a ref's lock even
  when they change nothing, so a lock event alone is not a ref move; a no-op reflog move on a detached HEAD only touches
  `.git/HEAD.lock`, which no watcher hears (as before). Next: step 2.
- 2026-11-13: round 13 step 2 landed (0da9f80e; captures 185bedad): a pointer move whose hover change nothing draws asks
  for no frame. Headless sweeps −55…−75 % frames (frames that only redrew what was on screen 191 → 2 in Junction); real
  windows Junction −43…−49 % UI-thread cycles a sweep (diff 42 → 3 frames), Vesper −63 %, Meridian −17…−24 %; 0 stale
  over 2,304 checked moves; step-ab 0 slower, the tooltip-delay row −49 %. Junction's
  `ReviewTests.AReplyAndAResolveGoToTheThread` timed out once under suite load (passes alone): watch at the full suite.
  For PERFORMANCE.md: an element that draws from its own `IsHovered` gets no frame on a move, so use `.OnHover` or an
  enter/leave handler; the in-hold idle gate (10 s quiet, 1 min timeout) often fails on this desktop, which idles near
  10 % CPU. Next: step 3.
- 2026-11-13: round 13 step 3 landed (698c2758; captures 54bd7d3a; DECISIONS.md b8fd2c67): a CJK word a prefix table
  cuts short is read off cuts made once. On the real Chinese README at 960 logical px a drag 88.6 → 2.0 MB and −48 %
  UI-thread time headless (real window −45 %, 291,890 → 2,453 HarfBuzz calls); Japanese 1000 px −9 %; elsewhere times
  within ±2 % and calls −46…−73 %. Kept with one measured cost: on Chinese the first width that wraps a line +13 %
  headless / +9 % real window (+290 KB) and opening +4 %, once a document. Corpora and goldens unchanged; step-ab 0
  slower but one AOT noise row. For PERFORMANCE.md: check a cache-dependent text change on a document twice the cache's
  size (where it can turn into a loss); time a resize's own frame, not the settle after it (single-frame headless phases
  swing ±7 ms on GC placement). Next: step 4.
- 2026-11-13: round 13 step 4 landed (8f9f2683, da72e01b, d413e001; captures 0a8361f9): a Junction watcher event builds
  the view only for a load it re-keys (a save in another repo 2 → 1 builds and frames, a touch 1 → 0; empty builds 16 →
  4 a launch), `SummaryFor` makes its closures only when the key moved (History Down −8.7 % a key), and the app's own
  action is not heard back (`GitWatcher.Forget()` when `RefreshAll` starts: a checkout −30 % UI, −38 % all threads, 28 →
  17 git runs in a real window). step-ab 0/0. For PERFORMANCE.md: what a build reads from disk without a load must be
  checked by the event handler; an app that watches its own writes should forget what was heard when its own full reload
  starts, not pause the watcher or read a fingerprint. Next: step 6 before 5, so question 52 can be answered first.
- 2026-11-13: round 13 step 6 landed (4fdb56cb, 276789d8; captures cdb47cb9): a Vulkan window frees the stencils of
  sizes it no longer shows once its size has rested (Aperture after 18 size changes: stencils 21.8 → 2.8 MB, Skia budget
  −66 %, DXGI local −21 MB, private −20…−37 MB; the purge ~1.2 ms, its re-uploads +0.3 ms over 28 frames), and mpv's
  event thread waits with no timeout (a paused player's own wakes 2.4 → 0 a second). (c), the chained HDR → SDR switch,
  not attempted: back to Waiting. step-ab 0 slower. For PERFORMANCE.md: a probe that wraps the platform loses SDL-only
  loop deadlines (a purge or display-colour poll never wakes it); `TotalProcessorTime` moves in 15.6 ms steps, time the
  UI thread with `QueryThreadCycleTime`. Next: step 7.
- 2026-11-13: round 13 step 7 landed (154a7ca3; captures 8048b644): Vesper's wrapped viewer and diff get their body
  width before the build on a resize: 2 → 1 frames and builds a step, −17…−18 % KB, real window −14…−15 % UI-thread
  time; settled frames byte-identical, and the first frame after a size change no longer shows stale heights. step-ab
  0/0. For PERFORMANCE.md: anything a build measures at a laid-out width (wrapped row heights handed to a virtual list)
  should get the led width too, or each size change builds and draws twice. Next: step 5 (b) (5 (a) waits for question
  52).
- 2026-11-13: round 13 step 5 (b) measured, nothing cut (DECISIONS.md 5849c137): in a real window the bridge's share of
  a Vesper navigation (31–34 % of the frame phases; 0.59–0.62 ms of 2.38 ms on the UI thread) is ~75–80 % raises, each
  13 provider calls and 13 QIs that are UI Automation's own work (our answers < 2 µs of a ~14 µs raise); every event
  concerns an element a reader can still reach. Lead vesper-enter-two-frames to Waiting. Every build step left needs the
  user (questions 52–54): asked; the full suite runs meanwhile. For PERFORMANCE.md: with a UIA listener running each
  Name, bounds, structure or focus event costs ~9 µs CPU, IsSelected one call; a list keyed by index renames every row
  it keeps when its contents swap (one raise a row).
- 2026-11-13: the user answered questions 52–54 with the recommendations: 52 option 2 (`InvalidateRows` keeps labels by
  default; Meridian passes a new label delegate at a day change; step 5 (a)), 53 build it, the step asking for a few
  minutes with Steam closed (step 8), 54 document it (HDR.md ea00556e, step 6 (d), written by the orchestrator; its
  commit message also claims these ledger lines, which landed in the next commit). Full suite on 5d4e779c
  (`fullsuite-r13.sh`, ~15 min): Windows `dotnet test HultUI.slnx -c Release` 30 projects, 0 failures (Core 5,085,
  Headless 1,577, Junction 1,111 in 10 m, Vesper 934, Meridian 678, Aperture 363, Rendering.Skia 416, Text 302, Reel 51,
  Fluent 34; UiTests 25 + 102 skipped, gate off); Fedora `wsl-test.sh` 22 projects, 0 failures (Core 5,084 + 1,
  Headless 1,403 + 88, Vesper 916 + 20); docfx `--warningsAsErrors` 0 warnings. It runs again after steps 5 (a) and 8.
- 2026-11-13: round 13 step 5 (a) landed (8ca081b8, 628bcb58, 505123d7; captures 21735c50): a list's `InvalidateRows`
  (and a theme or scale change) keeps its items' reader labels; Meridian's History and Vesper's Find results pass a new
  label delegate when the state their labels read changes. Junction start with the bridge on −28 % UI KB in a real
  window, a History row-shape change −217 KB; a client reads the same names; 117 event streams identical; step-ab 0/0.
  The audit found 10 STALE in Headless `MiddleClickAutoScrollTests` (a click handler added to a mounted box): RIDE in
  the survey's audit, so most likely uncovered by step 2 (a hover move that drew nothing used to carry the walk) → step
  5 (c) before the release. For PERFORMANCE.md: a label must read only its item, or the app passes a new delegate when
  the other state changes; when a framework contract changes, grep every delegate for state it reads beyond the item;
  real-window runs should log the window's DPI and the attached monitors (the 4K was absent for this step's hour).
- 2026-11-13: round 13 step 5 (c) landed (d73b1439; captures 2816c888): a click handler added to a mounted box (or a
  rebuild dropping its last one) is told to a reader by the next frame; the audit's STALE 10 → 0 across the 7 suites.
  Step 2 only exposed it (its old hover frame walked nothing; the audit skips dirty frames); no real app attaches a
  handler to a live element. Fallbacks 10 → 11 (a Junction GitLab test, a different one each run: watch). step-ab 0
  slower but one JIT drift row. For PERFORMANCE.md (testing): an audit that looks only at clean frames can be moved by a
  change in which frames are drawn; bisect it before blaming the change. Next: step 8.
- 2026-11-13: round 13 step 8 landed (cd7e3db2; captures 12adfdca): SDL's 1 ms timer is held only while the loop paces a
  wait shorter than 32 ms. The step paused once for the user ("NEED THE USER": Steam held 1 ms machine-wide, ~20,800
  interrupts a second); the user closed Steam, and with it closed the machine read 15.625 ms with no app, 1 ms with the
  old Junction at rest and 15.625 ms with the new one; animating it is 1 ms and back within 6.5 s. Own wakes and frame
  pacing unchanged; owned deadlines at rest on GL ~10 ms later (≤ 15.05 ms). Steam may be reopened. The 4K came back
  mid-run as the primary display with the 1440p at 120 Hz. For PERFORMANCE.md: a probe thread or a windowless process
  does not show the process's timer resolution (Windows ignores timer requests from a process with no visible window
  after a few seconds); read whether the machine's clock was raised with NtQueryTimerResolution with Steam closed, not
  an interrupt count. All eight build steps done (5 (b) measured with nothing to cut; 6 (c) back to Waiting); the full
  suite runs next.
- 2026-11-13: round 13 full suite on f0150d53 (`fullsuite-r13.sh`, ~12 min): Windows `dotnet test HultUI.slnx -c
  Release` 30 projects, 0 failures (Core 5,085, Headless 1,584, Junction 1,112, Vesper 935, Meridian 679, Aperture 363,
  Rendering.Skia 416, Text 302, Platform.SDL3 64, Reel 51, Fluent 34; UiTests 25 + 102 skipped, gate off); Fedora
  `wsl-test.sh` 22 projects, 0 failures (Core 5,084 + 1, Headless 1,410 + 88, Vesper 917 + 20); docfx
  `--warningsAsErrors` 0 warnings, 0 errors. origin/main afc1ed40 is in the branch. The user asked for app releases
  after this round's release. Phase → release.
- 2026-11-13: round 13 merged into main as 7cd6f083 (detached origin/main merge in this worktree, tree identical to the
  tested branch; origin/main afc1ed40 was in the branch and had not moved, so the push gate held). Primary checkout
  fast-forwarded behind the guard; branch reset to the merge and pushed. CI run 43481808112 on 7cd6f083 next; then the
  app releases.
- 2026-11-13: CI run 43481808112 on 7cd6f083 red: test-linux, docs and meridian-keyring (ubuntu, macOS) green; the
  Windows test job failed (a) `TimerResolutionHintTests.AWakeBeforeTheHoldIsOut_KeepsOneMillisecond` (step 8's new test,
  expected "1", got "0": the 1 s hold ran out on a slow 2-core runner — a wall-clock test) and (b) 15 of Junction's
  GitLab `ReviewTests` in a row, each `PumpUntil` 20 s with the review never coming up (round 12's CI passed them; two
  round 13 build steps saw one time out once locally under load). Not a known flake: the failed job re-run once (attempt
  2) for data, and a CI-fix agent reproduces both under CI-like conditions (2 cores, loaded), bisects round 13's
  suspects and fixes the causes on the branch. Main is red until the fix lands; app releases wait for green.
- 2026-11-13: CI attempt 2 on 7cd6f083 (failed job re-run): the same 15 Junction GitLab `ReviewTests` failed again in
  the same order (deterministic on the hosted runner); the step 8 timer test passed (a timing flake). Passed to the
  CI-fix agent.
- 2026-11-13: round 13 CI fixes landed (d797205f, 63e005f3; captures 72c0ba55; CI-fix agent, brief `brief-r13ci.md`).
  (a) The step 8 timer test was a wall-clock race (a wake 200 ms in had to beat the 1 s hold on Stopwatch; a runner
  stalling the process ~800 ms lost it): `SdlTimerResolution` reads an injected `TimeProvider` (internal; the public
  constructors pass `TimeProvider.System`), the tests drive a manual clock; the old test failed 5 of 5 here while a
  script suspended the testhost 1.1 s at a time (CPU burners did not reproduce it), after 10 of 10 under the stalls and
  20 of 20 on 2 cores. (b) ReviewTests: `Hosted` asked for the review once the history showed, but the summary holding
  the remotes could still be loading, so `LoadReview` failed at once ("not on GitLab"); folders the setup made report a
  change the first time git lists them, the watcher reloads 300 ms after start, and since 013e1f1b that event is a
  working-tree change that restarts the summary but not the log — on a slow 2-core runner the first summary is always
  overtaken. Fix in the test only: `TempRepo.Settle()` lists the setup's folders before the app opens (as `CopyTree`
  already does for copies), and the wait also needs the GitLab mark; 013e1f1b's rule kept; the app is unaffected (every
  way into a review sits behind a loaded merge request, and a landed summary keeps its value through reloads). Repro:
  ReviewTests alone held to 2 cores (affinity 0x3) failed 4–6 of 17 a run; bisect landed on 013e1f1b at once (its rule
  reversed: 0 of 34); after: 170 of 170 under 2 cores with Headless.Tests beside. Here `NtQueryTimerResolution` read
  15.625 ms (Steam not running). Windows `dotnet test HultUI.slnx -c Release` 30 projects pass (SDL3 64, Headless 1,584,
  Junction 1,112); Fedora `wsl-test.sh` 22 projects 0 failures (SDL3 55 + 5, Headless 1,410 + 88, Vesper 917 + 20);
  docfx 0 warnings. step-ab (5484fd89) 1 pair: JIT one row slower, AOT one faster, opposite ways on rows the change does
  not reach (noise), no allocKB moved. For memory `ci-red-findings`: a test class red every time on CI from a watcher
  change plus a test that asked for the review before the summary landed — 2-core affinity alone reproduces it;
  wall-clock races reproduce by suspending your own testhost (`NtSuspendProcess`, `scratchpad\r13ci\stall.ps1`), not with
  CPU burners; one Headless test failed once in 10 runs beside ReviewTests as load (name not captured).
- 2026-11-13: round 13's CI fixes merged into main as e432c224 (detached origin/main merge, tree identical, push gate
  held; primary fast-forwarded; branch moved to the merge and pushed). CI run 43499639788: test-linux, docs and
  meridian-keyring green; Windows test: Junction 1,112 all pass (the ReviewTests fixed), but
  `SdlWindowingPlatformTests.AWakeThePumpTookItself_LeavesTheNextWakeToEndAWait` failed once ("a wait woken 50 ms in ran
  2456 ms", bound < 1,500). That path is unchanged by step 8 when no short wait came before (no hold, one
  `SDL_WaitEventTimeout`), so most likely a runner stall like the timer test's; the failed job re-run once to tell.
- 2026-11-13: CI run 43499639788 on e432c224 green on attempt 2 (test, test-linux, docs, meridian-keyring on
  ubuntu-24.04 and macos-15); attempt 1's Windows test job failed
  `SdlWindowingPlatformTests.AWakeThePumpTookItself_LeavesTheNextWakeToEndAWait` once (a wait woken 50 ms in ran 2,456
  ms; a path step 8 leaves alone): a runner stall, `gh run rerun --failed` once. Round 13 landed eight steps (and 5 (c)
  and the CI fixes): Junction's watcher hears reftable refs and ignores lock echoes (a no-op fetch 14 → 2 git runs); a
  pointer move whose hover change nothing draws asks for no frame (−55…−75 % frames a sweep); CJK words cut by prefix
  tables read off cuts made once (a Chinese drag at 960 logical px 88.6 → 2.0 MB, −45 % in a real window); Junction
  events build and load less (a checkout −30 % UI KB); lists keep reader labels across `InvalidateRows` (Junction start
  −28 % UI KB with a reader) and a click handler added after mount is told; Vulkan windows free stencils of old sizes at
  rest (−19 MB after 9 sizes) and mpv waits without a timeout; Vesper's wrapped views lead their width (one frame a
  resize); Windows' 1 ms timer only while the loop paces frames. Round → 14, Phase → survey; the loop stops, and round
  14 starts in a fresh context. The user asked for app releases after this round (2026-11-13): junction-v1.27.0,
  vesper-v1.27.0, meridian-v1.10.0, aperture-v1.25.0, reel-v1.11.0 cut on e432c224 (CI green), annotated, messages from
  files, pushed 3 + 2; installer runs 43505664912 (Vesper), 43505665242 (Junction), 43505665602 (Meridian), 43505709936
  (Reel), 43505711156 (Aperture) started.
- 2026-11-13: all five installer runs green on the first attempt (each re-ran its suites): Junction-Setup-1.27.0.exe,
  VesperCommander-Setup-1.27.0.exe, Meridian-Setup-1.10.0.exe, Aperture-Setup-1.25.0.exe and Reel-Setup-1.11.0.exe
  published. The loop stops; round 14 starts in a fresh context with the resume command in the Phase.
- 2026-11-13: round 14 survey started in a fresh context (session 41943a9b; usage 42 % weekly). Main has not moved
  (origin/main e432c224 is in the branch). Moved to the archive by a checked script (`archive_r14.py`; every moved line
  found there, nothing lost): round 12's 6 Landed rows, round 13's build order with its closed-from-Waiting note, round
  13's answered questions 52–54, and round 12's 88 log lines (2026-11-12, its survey to its app releases). Attached: both
  monitors (the 4K at 150 % primary, the 1440p to its left); Steam is running (it holds 1 ms machine-wide). The eight
  experts survey round 13's build, 8f01b6fd..e432c224 (49 commits, two merges: round 13's own into main), in their own
  worktrees on this commit, in real windows too.
- 2026-11-13: round 14 survey done (reports fb946f66..06d4db59, `17-reports/r14-*`, ~65 min; 42 → 50 % weekly). No
  expert satisfied. One review objection: 698c2758 (b), the natural-end words, cost +49…+72 % UI KB at every new width
  on real Chinese text → step 1 (a). Round 13 helped the real apps more than headless shows (geomean 0.997 headless;
  real windows: pointer sweeps Junction diff 33 → 0 frames, Vesper 48 → 2; the app's Fetch −36 % and checkout −34 % UI
  KB; Junction start −30 %; a Chinese drag at 960 logical −98 % a step; Aperture −24.5 MB GPU after size changes).
  Real windows: 104 guarded launches in 19 holds of `Global\HultUI-r14-desktop`, 0 foreground takes, 0 driver events,
  0 screen reads. What only real windows or real data showed: a wrapped Arabic README allocates 842.6 MB a drag (every
  wrap probe through the full BiDi layout); History at a reading pace 9–10 MB a key on all threads (the whole commit
  patch); a build beside Junction reloads it from gitignored folders; Vesper's navigation draws a frame with the new
  title over the old listing (1.70 frames a navigation); a save over Changes, a Vesper click and a scrollbar hover tween
  draw same-pixel frames; opening or closing the palette builds the whole reader tree; the video interop loads GL
  inside the first video frame. Merged into an 8-step build order (expert 4's watcher timer into expert 6's step 3;
  expert 1's Changes frame into expert 6's step 6), 5 Waiting items closed, 9 added, questions 55–59 asked. For
  PERFORMANCE.md: frames drawn inside the size message carry the previous frame's "other" share (read only bytes and
  frames from drag rows); a parser's `StartsWith(string)` is culture-sensitive on a Swedish machine, and the suite's
  InvariantGlobalization hides it (use ordinal); a root pointer-up handler that changes nothing visible should be
  passive; `PatchFrom` marks paint on every patch, so a view build ahead of its load draws a same-pixel frame; a list
  keyed by index renames every kept row when a commit arrives at the top; walks on a shared fixture hear other
  sessions' git activity. Phase → build.
- 2026-11-13: step 1 spawned (expert 5, base ded3e51c, brief `brief-r14b1.md`). The user answered questions 55–59 with
  the recommendations: 55 (b), the GL preload stays opt-in and Aperture opts in (step 8); 56 yes, raise the word cache
  bounds only if step 1 (c)'s admission fails (told to step 1's agent); 57 re-measure after step 1, then ask again; 58
  yes, Junction hears terminal staging (step 3 (c)); 59 yes, hover follows content scrolled under the pointer, with
  before/after images for the user before the commit (new step 9).
- 2026-11-14: round 14 step 1 landed (2c809bc1, c06aa102, 5d9a5c71, 9fd8cf07; captures 11ee4926): the review objection
  answered (a CJK table's end reads a segment the cache has, a natural word stops at a Latin name: zh new widths −7…−8 %
  time, −18…−31 % KB); a TextArea's probes of a wrapped BiDi line are span widths (Arabic drag −92 % time, 830.9 → 44.4
  MB; real window 5,810 → 426 ms); the word cache holds 32,768 words / 16 MB (question 56: the admission failed; the
  154k-character CJK file's drag −58 %, 157.5 MB → 953 KB). Every corpus exact; step-ab 0/0. Open: real-window Arabic
  private bytes +28 MB outside the GC heap and never resident (Waiting, a check). Question 57 asked again with what is
  left a drag frame (itemizing only, ~11–12 ms a frame on the largest documents). For PERFORMANCE.md: log GC counts per
  phase (a drop in allocation moves a collection into one frame); take the idle gate before the desktop hold (it timed
  out inside again); test render contexts without prefix tables route a line search through `MeasureSubstring`; a text
  test's count bound needs margin for Fedora's faces. Next: step 2.
- 2026-11-14: round 14 step 2 paused for the user (question 60). Landed: 44958e6e, the diff parser's markers ordinal
  (a plan merge's parse 203 → 20 ms on this sv-SE machine). The per-file load is built and proved (948 files of the 47
  big commits read alone equal their slice of the whole patch; 7 tests, a pixel check of the pane on first and second
  visits) but not committed: in a big commit a file's first visit waits for a git run (33–47 ms, a "Loading…" frame).
  step-ab and Headless.Tests run after the answer. For PERFORMANCE.md: a parser that tests markers with
  `StartsWith(string)` pays ICU on a non-English culture; a headless key press already builds and renders, so a test
  of a key's own frame asserts right after `PressKey` (one pump lands the load). Next: step 3.

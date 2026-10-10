# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 3
- **Phase:** build — **paused after the round 3 survey** (the user asked for a break once the
  surveys were in, 2026-10-10). All eight reports are in `17-reports/r3-*`; the round 3 build
  order is below, and 13 questions wait in Needs the user. Steps marked **(user)** wait for an
  answer; the others can start. Next: round 3 build step 1. To resume, run in this worktree:
  `/loop Run the next step of the performance team: follow docs/plans/17-performance-team.md,
  with state in docs/plans/17-performance-team-ledger.md. Read both, do the step the ledger's
  Phase calls for, record it in the ledger, and commit.`
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`; the session
  moved here from `claude/ui-framework-performance-a2cca6` after a restart)
- **Review commits since:** 0951288 (round 4 reviews all of round 3's build)
- **Probe code kept for the build phase** (session scratchpad): round 1–2 `probes/`,
  `painting-probes.patch`, `PaintProbe.cs`, `probedriver.cs`; round 3 `l1r3/` (layout
  counters, inherited-style diff logger), `r3paint/` (render-pass probe, sub-blob and native
  handle prototypes), `r3-gl/`, `r3rt/` (per-loop log probe, heap dumps), `probes/reactivity/r3/`,
  `r3a11y/` (probe-src, live WSL AT-SPI driver), `r3apps/` (app probe with per-Build marks).

**App releases:** round 2's were cut at the user's request (2026-10-10). Cut them again only
when the user asks; follow memory `release-tagging` (≤3 tags per push).

## Verdicts

| Expert | Round | Satisfied | Review objections |
|---|---|---|---|
| 1. Layout | 3 | no | none |
| 2. SkiaSharp: painting | 3 | no | none |
| 3. GL and Vulkan | 3 | no | 82e382b: a minimized window rebuilds the a11y tree every loop pass (7–27 MB/s); 7e52ff5: whole-tree walk every pass while minimized and dirty, video or not |
| 4. .NET runtime | 3 | no | UIA keeps closed windows alive (~357 KB each, plateau ~100 windows; not proven a round 2 change) |
| 5. Text pipeline | 3 | no | none |
| 6. Reactivity and reconcile | 3 | no | none |
| 7. Accessibility bridges | 3 | no | 9c8bf91's −97 % only without a reading client (1,974 KB/keystroke with one; no worse than before); 72a4f96/9c8bf91: UIA ValueValue walks the live tree on a UIA thread; 2170222: status reads can finish out of order; 0912a5e: lists over 2,000 items told nothing |
| 8. App workloads | 3 | no | 0912a5e: a selection move forces a full a11y rebuild (History a11y share 7 → 30 %); round 2 otherwise helped every app (Junction −21 %, Vesper −12 %, Meridian −79 %, Reel −88…−91 % UI-thread bytes per session) |

## Queued candidates

Rounds 1 and 2's build orders all landed or were done (see Landed; 61edb19 declined). Round 3's
surveys (reports `17-reports/r3-*`) re-ranked everything. A build step may bundle one expert's
related small candidates as separate commits. Items marked **(user)** wait for a decision in
Needs the user; the rest of their step can go ahead.

### Round 3 build order

Round 2 regressions first, then value per cost.

1. **gl-vk-minimized-children-flag + gl-vk-undrawn-sources-walk** (3; regressions from 82e382b and
   7e52ff5) — the minimized branch returns before `ClearAllFlags`, so `DirtyFlags.Children` stays
   set and, with an AT client listening, the a11y tree is rebuilt every loop pass (2,016 elements
   7.0 MB/s GL, 10.8 MB/s Vulkan; 5,016 elements 26.9 MB/s) until restore; `CollectGpuSources`
   walks the whole tree every pass of a dirty minimized window even without video (123k–447k
   visits/s). Clear the consumed dirt (keep paint dirt); walk only when GPU image sources exist.
   Keep 7e52ff5's "new player loads while minimized" test green. Small.
2. **a11y round 2 fix-ups** (7): **a11y-selection-move-patch** (regression from 0912a5e: a move
   forces a full a11y rebuild; Junction History a11y bytes per key 217 → 294 KB, a11y share of the
   walk 7 → 30 %; patch IsSelected on the two item nodes as `MoveA11yFocus` does),
   **a11y-uia-disconnect-on-close** (leak raised by 4: `UiaReturnRawElementProvider(hwnd, 0, 0,
   NULL)` in Dispose; 150 cycles 36.7 → 1.29 MB managed), **a11y-uia-text-value-from-node**
   (thread safety: answer ValueValue from `Node.Value`, not a live-tree walk on a UIA thread),
   **a11y-atspi-status-serialize** (out-of-order status reads). With suite row
   **apps-scenario-selection-walk-real-loop-a11y** (8: keys every 60 ms in the real loop, bridge
   on, a11y rebuilds per key — would have caught 0912a5e).
3. **Junction rebuild costs** (8/6, app): **apps-junction-prefetch-rebuild** (the highlight
   prefetch's completion rebuilds the whole app outside Commit/Changes: History 2.05 → 1.0
   rebuilds per key, −26…−45 % bytes), **apps-junction-diff-flatten-per-build** (every build
   re-flattens the shown diff: Changes typing 1.19 MB per keystroke, ~−85 %; memoize rows by
   (diff, split, highlight) and use the two-argument `RenderWith`), and `AccessibleItemLabel` on
   Junction's diff, Vesper's diff, viewer and thumbnail lists (from **a11y-item-label-fallback**:
   readers hear `DiffRow { Type = Unified, … }` or "0", "1"; −700 KB per keystroke). The framework
   fallback stays `ToString()` (a guideline item). Suite row **apps-scenario-type-beside-diff**.
4. **VideoPlayer rebuilds** (6/3/1): **reactivity-videoplayer-position-in-place** (an Effect writes
   the elapsed label and slider; 41.2 KB per position tick, 10/s → 248 B, −99 %),
   **media-videoplayer-fade-rebuild** (bar fade writes the live element's opacity; Reel 12 s: 166
   rebuilds = 74 % of UI-thread bytes; fade frames 41 → ~3 KB), opt CanvasElement, SliderElement,
   VideoElement into PatchMarksOwnLayout (a11y lead: a layout pass per playing frame), and Reel's
   and Aperture's chapter panels read `Position` untracked (ReelApp.Chapters.cs:126,
   ApertureApp.Chapters.cs:149: whole-app rebuild 10×/s while open). Expert 3 reviews. Suite row
   **apps-scenario-video-bar-fade**.
5. **text-prefix-table-scratch** (5) — BuildPrefixWidths reuses one kept segment list and font-run
   list: long-document open −60 % (3,418 → 1,377 KB/op), wrap resize −11 %, wrapped typing −10 %,
   20,000-line wrapped log 207.7 → 75.2 MB. Also the span-based prefix measure that
   paint-cluster-line-measurer needs (TextTypes.cs:272). Small.
6. **layout-style-mark-once + layout-applytheme-layout-on-change** (1) — every PatchFrom walks its
   whole subtree to mark style, once per ancestor level (O(n × depth)): a "subtree marked" bit cuts
   mark visits −73…−90 % (Junction Files search 383,482 → 37,432 per key; every other counter
   identical on 102 rows); ApplyTheme marks layout only when a measured value changed (theme rows
   8.91 → 0.19 KB/op). Small.
7. **paint-raw-run-buffer + paint-polygon-pooled-path + paint-cluster-line-measurer** (2) —
   `AllocateRawPositionedRun` (AOT −17…−40 % on 6 rows); pooled SKPath and span for
   FillPolygon/DrawPolyline and Terminal's HalfEllipse (Powerline frame 12.49 → 1.62 KB); one
   measurer per merged-cluster line (1.1 KB per such line per steady frame → 0). Small.
8. **paint-spanned-line-sub-blobs** (2, medium) — draw each colour stretch from a kept sub-blob of
   only the glyphs that can reach its clip: glyphs per frame −76…−89 % (Junction diff hover
   24,527 → 3,420), pixel-identical by construction. Needs a non-outline pixel test, a GPU readback
   check, an empty-bounds fallback (colour emoji). Managed sub-blobs cost bytes (selectable viewer
   wheel 3.3 → 14.2 KB/op), so it lands with **paint-native-blob-handles (user)** (scroll/jump
   −62 % JIT / −74 % AOT) or with presized maps if that is declined.
9. **vk-purge-disposed-image-textures + vk-swapchain-target-per-frame** (3) — purge Skia's unlocked
   resources after image sources are disposed (8 photos: DXGI 391.5 → 213.2 MB); one render target
   per swapchain image (2,048 B per Vulkan frame → ≤0.1 KB; Aperture's quiet frames are all of it).
   The second changes HDR.md's "never cache a wrapped swapchain target": barrier each acquired image
   back to COLOR_ATTACHMENT_OPTIMAL, reused-frame capture, validation with a positive control.
10. **runtime-idle-wakeups + apps-tooling-generic-framelog** (4; with 3, 6, 7) — the loop waits for
    its next deadline or a wake instead of `WaitForEvents(16)`: idle ~60 → ≤1 wake/s in all five
    apps, minimized 38 → 0. Deadlines: animations incl. delay tweens, a11y trailing refresh 100 ms,
    range refresh 250 ms, resize throttle, inspector, Vulkan colour poll without colour events.
    Wakes: AccessibilityActionRouter queues (incl. AT-SPI SetRangeValue), AT-SPI status flip and
    attach. WinForms host 8 ms timer. With **reactivity-animation-deadline (user)** (ToastHost hold
    and auto-hide as `.Delay`; ~60 → 1 wake per delay) — a tween started after idle must not jump
    to its end. The env-gated per-loop log (r3rt patch) is the measurement; suite row
    **apps-scenario-loop-wakes**.
11. **runtime-pointer-events-on-demand + reactivity-pointer-rebuild-per-frame (user)** (4/6) — make
    PointerMove/Enter/Leave event objects only when a handler wants them (hover frame 568 → ~150 B);
    pointer-driven rebuilds once per drawn frame (splitter, pan, slider, scrubber: 8–16 rebuilds per
    frame with a fast mouse, −78…−94 % bytes per drag frame).
12. **reactivity-toasthost-fade-in-place (user) + apps-whole-app-tween-rebuilds** (6/8) — the toast
    fades its live element (one toast: 22–27 whole-app rebuilds → ~2–3, −90 %); Junction's spinner
    and remote-op tweens write Transform/Opacity on live elements (fetch: 12.1 MB per 61 frames).
    Suite row **apps-scenario-toast**.
13. **text-textarea-incremental-wrap + text-memo-cache-byte-bound** (5) — carry each hard line's
    breaks by index and patch line starts around the edit (retained −8.2 MB on a wrapped 20,000-line
    log; keystroke churn −27/−28 % on 19k JSON and the log); weight BiDi/styled memos by bytes
    (~16 MB bound; worst case 58 MB → ≤16). Medium. Proof: random edits vs a fresh area incl.
    surrogates, ZWJ, CJK; re-run b23b203's 2.0 M-line audit.
14. **Lists on item rows** (1/8): **layout-virtuallist-row-inherit (user)** first (rows resolved
    under their list: Junction Branches layouts per key 255 → 32, 0 px changed), then
    **adopt-item-rows** — Vesper panes (339 of 542 KB per key), Junction Branches and Tags (need
    stable or value-equal TreeNode items; −380 KB of 667 KB per key), Aperture's grid — and
    **apps-junction-files-virtual** (Files on a VirtualList with item rows: a 2,300-file search
    28 MB per key → ~200 KB; 3,501 hit visits per move → ~25). **a11y-item-label-keep (user)** goes
    with it (keep each item's a11y label while item and delegate are unchanged: −63 % per move with
    a reader).
15. **a11y events (user)** (7, correctness): **a11y-patched-props-untold** (Name, Description,
    Expanded, Role, AutomationId setters notify; a rebuild that changes only these is told nothing
    today), **a11y-role-change** (UIA ControlType, AT-SPI accessible-role),
    **a11y-atspi-cache-child-index** (two middle inserts in one frame lose children in Orca's
    cache), then **a11y-layout-refresh-moved-only** (skip the bounds refresh when nothing moved: a
    tween frame with a reader 8.7 KB → 0). Joins this round only if approved.
16. **linux-emoji-colour-fallback** (5, correctness) — emoji presentation and VS16 pick the colour
    emoji face (Linux 😀 ❤️ in DejaVu today; Windows #️⃣ ™️ ©️ monochrome). Changes those pixels;
    goldens unaffected (pinned faces). Proof on WSL with real Noto faces and on Windows.

### Waiting

Re-ranked by the round 4 survey.

- **reconcile-restyle-on-change** (6/1, medium–large) — mark style on patch only when inherited style
  or theme-fed values changed (component/edit 6,003 unchanged re-resolves of 6,003; Junction ~−90 %
  resolves, −9 layout marks per rebuild from Button/Kbd/TextInput/Toast ApplyTheme flip-flops). After
  step 6; 32 ApplyTheme overrides to audit. SelectionColor **(user)**.
- **a11y-virtual-items-and-selection (user)** (7) — Orca sees 0 items in lists over 2,000, and moves
  there are told on neither bridge (Junction History past 2,000 commits, Vesper folders over 2,000).
- **a11y-textarea-text-pattern (user)** (7) — 2,000-line TextArea keystroke sends 121,744 characters
  as Value (243 KB BSTR).
- **apps-scenario-atspi-rows + a11y-atspi-snapshot-per-diff** (7) — AT-SPI steps 2.2–4.1× UIA's,
  untimed; every apply rebuilds every object at 265 B/node (wheel 795.9 KB). Medium–large.
- **a11y-property-patch** (7, large) — after step 15 and snapshot-per-diff; build+diff is 94–99 % of
  a11y step bytes.
- **a11y-richtext-reader-keystroke (user)** (7, large) — 1,974 KB per keystroke in a 5,000-block
  document while a reader reads; public AccessibleText changes.
- **text-open-measures-every-line** (5, from 8) — a TextArea's first draw measures every line
  (Junction TODO.md open 2.8 MB, text 84 %; Meridian first response 7.5 MB, text 90 %). Medium.
- **apps-junction-components (user)** (8) — re-open "split Junction into components" (declined
  2026-10-06): unchanged titlebar, sidebar and toolbar are 70 % of History's Build bytes.
- **mpv-vo-thread-minimized (user)** (3) — `vid=no` while minimized: −38…−71 % process CPU, a
  glitch at every restore.
- **layout-ordered-children** (1, low, medium) — hit test and cull O(n) → O(log n + visible);
  suite-only once Files is virtual.
- **apps-tokenize-on-ui-thread** (5/8) — Junction's markdown code fences tokenize in Build under
  SyntaxHighlighter's single lock (up to a 3 s wait behind a worker); GitLab merge requests only.
- **tooling-textbench-warmup** (5/4) — fresh indices per warm-up pass, warm until the compiled-method
  count stops rising (990 methods compiled during measurement today).
- **test-frame-pacing-event-precondition** (4) — fails only on displays ≥~170 Hz.
- **test-vesper-diffview-escape-flake** (8) — 0 of 1,400 runs; keep watching CI.
- **paint-drawspannedline-overlap** (2, lowest) — no caller gives overlapping spans; could close with
  a Debug assert.
- **Suite scenarios** (8): Vulkan steady-frame bytes (Windows real window), rich-row walk,
  document-switch, image-grid.
- **aperture-hero-scrim-seam (user)**, **aperture-poster-as-hero** (crop at decode; not
  reproducible on the lab library), **aperture-tmdb-size-fallback**,
  **junction-narrow-gitlab-glyphs**, **meridian-pane-choice-not-persisted**,
  **meridian-min-width-wraps** (apps; not perf).

## Landed

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 1 | 7a827e9 | gl-vk-swapchain-rebuild-leak | Not a real VRAM leak (Task Manager bookkeeping; nvidia-smi and DXGI flat). Real cost: ~100 ms first-present stall per chained scRGB MAILBOX rebuild. Destroy-first for scRGB MAILBOX on Windows: resize 112 → 29 ms median, resize CPU 453–480 → 198–206 ms/s, stretched frames during resize 72 % → 0, Task Manager 1,416 → 173 MB per 40 resizes. IMMEDIATE, FIFO, 8-bit keep chaining. |
| 1 | 0f5816a | gl-no-depth-buffer | `SDL_GL_DEPTH_SIZE` 0, stencil 8 kept: GPU per GL window 55.3 → 46.8 MB at 2100×1350 (−15 %), 131.9 → 109.5 MB at 3818×2032 (−17 %); Task Manager, DXGI and nvidia-smi agree (~3 B/px). Start-up unchanged (probe first frame 427.8 → 427.2 ms, Junction 480.7 → 480.5). Junction, Vesper and a stencil-dependent FBO 0 scene byte-identical; a stencil-0 control broke the scene. |
| 1 | 17e3de4 | paint-text-zero-alloc | Index loops on the text draw path, `PrefixMeasurer` closure moved out, pooled corner `SKPath`. allocKB −10…−96 % on 43 JIT / 44 AOT rows; dense text 18.1 → 0.8 KB/op (AOT 29.8 → 12.5); TextInput keystroke 44.9 → 2.5; render pass ~0.1 KB/frame on text screens; time flat; goldens byte-identical. |
| 1 | 8ab877c | runtime-aot-trace-type-name | Element trace names looked up only while recording (internal `FrameTracer.BeginElement`). AOT allocKB −10…−99.6 % on 40 rows, none up: resize 2,000 rows 685.6 → 2.9 KB/op (time −16 %), TextInput typing 59.0 → 2.4 (−10 %), deep nesting 75.5 → 4.8 (−12 %), tiles 7.7 → 0.7; AOT per-frame bytes now equal JIT. JIT flat. HULTUI_TRACE output identical event for event. Junction AOT frame not re-measured. |
| 1 | 12d1480 | paint-image-zero-copy | `SetImmutable` on hand-over: SkiaImageSource's image shares the bitmap's pixels. Private bytes: Vesper 216 thumbnails 83.6 → 66.3 MB (−21 %), Aperture real library (27 posters) 85.0 → 67.3 MB (−21 %), GL window with 8 × 3840×2400 photos 1001 → 723 MB (−278 MB). Extrapolated up to ~260 MB at Aperture's 400-poster cap. Time and allocKB flat on all 83 suite rows; goldens byte-identical; GL readback exact. Vulkan not checked. |
| 1 | f84893d | aperture-poster-decode-scale | Aperture artwork requested at layout size × display scale (25 % steps), by the side that decides a cover fit; refetched on scale change with the old picture as stand-in. Real library, 27 pictures: decoded 17.6 → 4.4 MB at 100 % (−75 %), 9.9 MB at 150 % (−44 %), unchanged at 200 %; private 63.8 → 42.9 MB (−33 %) at 100 %, 63.2 → 54.1 (−14 %) at 150 %; Skia mip cache 5.9 → 0 MB. At the 400 cap 258 → 65 / 145 MB. As sharp or sharper (≥1 source px per device px). Same fix for cast, chapters and request-server pictures. |
| 1 | 4195da4, 7096431 | a11y-uia-wrapper-rebase | Every surviving UIA wrapper points at the current tree after each diff; stale wrappers dropped; no per-diff full-tree index. Trees held after 200 toggles 221 → 1 (30–34 MB → 0 retained); ApplyChanges 47–211 → 0.3–1.4 KB per diff; new Windows-only perf rows with the real UIA bridge on a hidden HWND: allocKB −22 / −29 / −30 % (JIT and AOT), label edit min −5.4 %; 0 slower. UIA event stream identical (1,118 events); sibling navigation now current. |
| 1 | 54631d4, 8aed816, 646139a, 9af7d75, 480b141, 65a01a3 | runtime-frame-floor (+ layout walk half, hover common ancestor, slider/number-field paint) | Steady and idle frames 0 B (tiles, dense text, tweens 0.6–0.7 → 0.0 KB/op; control screen repaint 1,296 → 0 B); Tab 28.4 → 0.3 KB (AOT 34.1 → 0.3); theme switch −62/−56 %; hover −83 %; component rebuilds −5.6 % alloc, −5 % time; 32 rows ≥10 % down, none up, JIT and AOT; 0 slower. Junction/Vesper AOT frames not re-measured. |
| 1 | 0178784 | text-word-alloc-diet | Word cache looked up in place by a span (kept alternate lookup, per-run font hash), exact glyph arrays, widths without glyphs, LRU node reuse. allocKB −10…−60 % on 17 rows JIT and AOT, none up (text/ellipsis 7,261 → 3,418 KB/op, text/wrap 3,093 → 1,572, richtext/open 18,172 → 11,211, 600-span paragraph 1,924 → 776); 0 slower; text/open −10 %, richtext/open −26 % min; output bit-identical over 1.78 M layouts; goldens byte-identical. |
| 1 | e962738, f3a31d6, 5eebbf6 | runtime-text-services-per-window | One SkiaTextServices per application (SDL, WinForms, headless; pinned set separate), disposed with the platform; surfaces dispose only services they built. 600 open/close cycles: +4 font streams, +44 KB private, +1 KB managed per cycle → flat. WinForms handle recreation 808 → 3 streams after 200 cycles. New row window/open a second window 0.500 → 0.265 ms min, 262 → 132 KB/op; 0 slower on 86 rows. Windows share bounded caches now (one UI thread only). |
| 1 | 1785d16 | gl-vk-offscreen-scratch-pool | Scratch purge after the frame that follows a let-go F16 offscreen. GPU memory: 4-size cycle DXGI 215.4 → 154.1 MB (Skia pool 57.1 → 0.1), 40-step drag 458.5 → 242.5 MB (pool 203.0 → 0.1); nvidia-smi agrees. Resize time unchanged on drags and two-size toggles; +0.5 ms on new sizes, +1 ms (3 %) cycling 4 sizes. Unbudgeted alone changes nothing. GL has no such offscreen. Validation 0 messages, positive control 40. Trap: `GetResourceCacheUsage` counts only budgeted resources. |
| 1 | e3cff87 | a11y-build-diff-garbage | Exact-length child arrays (leaves share an empty one), index walks, positional pairing until the first difference, one owner-slot lookup per list. Build 230 → 144 B/node; unchanged diff 121.7 KB → 176 B (3,001 nodes). a11y/edit every 10th 568 → 368 KB/op (−35 %), time −9…−10 %; UIA rows −41…−47 % alloc, label edit −8…−12 % time; JIT and AOT; 0 slower. Diff identical vs the old differ over 3,300 random pairs; UIA event stream identical (1,118 events); Linux a11y tests pass. |
| 1 | 4c3e3b1 | a11y-uia-reparent-wrapper | An element that moves to another a11y parent keeps its UIA wrapper; AT-SPI sends no cache-remove for a moved object and one children-changed:remove per moved child. Before, navigation to moved elements returned null and their events were dropped (6 of 16 in the reparent script). Other event streams identical (UIA 1,120, AT-SPI 6,886); allocation unchanged. |
| 1 | 93847ae | (measurement) | Windows-only perf rows `a11y/tab through 300 fields, UIA` and `a11y/wheel 1,000 rows, UIA`. |
| 1 | 70d68c1, 0b2b003 | a11y-focus-state-on-tab | A focus move patches IsFocused on 2 nodes of the applied tree. UIA raises HasKeyboardFocus old/new before the focus event (was: at the next rebuild); AT-SPI answers current focus and drops its late duplicate signals. Allocation unchanged; Tab with UIA 0.351 → 0.350 ms. |
| 1 | ab8917b | a11y-scroll-bounds-event | Scroll view nodes carry ScrollOffset; UIA raises Vertical/HorizontalScrollPercent on the view (Chromium's mapping, `browser_accessibility_manager_win.cc`) instead of a BoundingRectangle per on-screen node: 846 → 6 events, provider callbacks 11,844 → 84 per 30 notches; bounds and offscreen read live. a11y/wheel 1,000 rows, UIA −54 % min, −62 % median. Node still 136 B (packed states). AT-SPI unchanged (sent nothing before). |
| 1 | 6891682 | layout-truncation-clamp-basis | A MaxLines(1) label in a row starts at min(untruncated width, room), from a pinned MeasureCache entry (no new element bytes). Ellipsis rows −19…−42 % time JIT, −20…−43 % AOT, allocation −21…−52 % (text/ellipsis 4.75 → 3.32 ms, 3,418 → 1,887 KB/op); measurements per step halved; 0 slower. Pixels: ellipsis slack ≤ 0.89 px; zero-room cells draw nothing instead of overlapping. Goldens byte-identical. |
| 1 | 322df92 | layout-truncating-min-zero | A MaxLines(1) label's automatic minimum is 0 (nested boxes too); a label too narrow for its ellipsis draws nothing. Perf flat JIT and AOT. Fixes: Junction Branches name/tag overlap at 1200 px, Junction sidebar repo name/path running past the edge, Junction Tags subjects over the date column, Meridian's Send button off-window with a long URL. Exposed: Meridian's request tab titles and sub-tabs wrap at ≤1100 px and overlap the URL bar (follow-up 11e-fix); Junction's FlexShrink(0) "current" tag still overlaps at 1000 px. Goldens byte-identical. Images in scratchpad `truncation-minzero/`. |
| 1 | be1fd9d | apps-truncation-followups (Meridian) | Request tab titles one line (ellipsis, ≤220 px, narrow to 72 px, strip clips); sub-tabs one line in a sideways-scrolling strip; URL box clips; Chain, Assertions, History, Environments labels fixed. At 1100 px the editor gets its designed 228 px and the response pane fits (before: titles 4–11 lines deep over the URL bar, pane 179 px off-window). Breakpoint left at 1100 (asked the user). |
| 1 | 351e6cd | apps-truncation-followups (Junction) | Branch name cell clips; the name truncates first, then current/base/head/remote tags show whole or not at all; LAST COMMIT heading one line. No overlap at 1000/1200/1400 px. |
| 1 | 6cf4fe6, 17e94b0 | aperture-hero-scale | Heroes requested at hero box × display scale (25 % steps), by the cover-fit side, rounded up to a 128-device-px step; stand-in on scale change or step; TMDB w1920 above w1280; only the last 4 heroes kept. 150 %: item hero 0.699 → 1.022 source px per device px, request hero 0.639 → 0.958; item page private 62.1 → 67.6 MB (hero 4.2 → 9.0 MB). Resizing 7 steps 96.9 → 66.0 MB (8 heroes → 1); 7 pages 137.6 → 96.8 MB (106.1 before 11f). Request page at 100 % +7.4 MB (w1920, strict rounding kept by the orchestrator). Measured against the real Jellyfin and Seerr. |
| 1 | 5b2b676 | meridian-pane-breakpoint | Response pane goes to the bottom below 1320 dip (rail 48 + tree 264 + editor 448 + pane 560); 1100–1319 px windows put the pane at the bottom (at 760 tall: pane 380, editor full width). Ctrl+\ unchanged; plan and README updated. Exposed: the bottom pane is a fixed 380 px, so a 1280×672 window (1080p at 150 %, maximized) leaves ~90 px of sub-editor (follow-up 11g-fix). |
| 1 | 2bca842 | junction-branches-compact | Branches/Tags compact below 934 px of content (date only, ↑/↓ and eye headings, icon Checkout named "Checkout"); below 724 px LAST COMMIT/COMMIT move to the tooltip. BRANCH at 1000 px with the sidebar 48 → 165 px, 1200 px 160 → 277, 900 px 0 → 196. Narrow mode also hides the GitLab lock, pipeline glyph and MR chip that live in LAST COMMIT. |
| 1 | 53442e2 | meridian-bottom-pane-height | Bottom response pane keeps 380 dip in windows ≥ 736 tall and gives way below, with an editor floor of 316 (chrome + five form rows) and a pane floor of 162 (flex minimums, no rebuild on resize). 1280×672 editor 252 → 316 (header rows 3 → 5), 1100×600 180 → 316, 760×540 120 → 316; 1100×760, 1200×800, 1400×900 unchanged. No splitter (design token; Meridian has no settings file). |
| 1 | 1c8ee98, 758fa03 | apps-scenario-virtual-selection-walk-a11y + apps-scenario-textarea-highlighted-json | 5 perf-suite rows (94 scenarios now): virtual/selection walk 1,000 rows (+ a11y bridge on, + UIA), text/open and text/wheel highlighted JSON (19,013 lines, 22,968 spans identical to HultUI.Syntax). JIT min 0.466 / 0.534 / 0.565 / 10.74 / 0.939 ms, 108 / 412 / 412 / 10,811 / 14 KB; realize ~10 % and a11y 12–18 % of the walk step (below the app survey's shares: 5 elements per row vs ~14 in Junction). JSON open median ≈ 2× min from GCs. |
| 2 | 0ceecea | runtime-sdl-pump-closure | A real SDL loop pass allocates 0 B instead of 88 B (AOT 72). GL idle 5.5 KB/s → 0; GL spinner 3–4 MB/s → 0, no GCs; Vulkan idle 11.7 → 6.2 KB/s (display-colour poll left), spinner 4.45 → 0.41 MB/s (render target per frame left). Test on SDL's dummy driver runs in the normal suite. |
| 2 | ff7a970, 2612de1 | runtime-paced-wait-spin | SDL on Windows rounded waits down (1 ms → 0); the wait now asks 1 ms more. Spinner 45–49k → 420–447 loop passes/s (224–245 → 2.1–2.2 per frame); UI-thread cycles −39…−57 %; frames/s 200 → 200 on GL and Vulkan, JIT and AOT; live resize still draws inside every step; headless suite flat. Linux/macOS not changed (SDL rounds correctly there per its source). |
| 2 | 82e382b, 3766d78 | vk-minimized-swapchain-lost | A Vulkan window that drew while minimized draws again: 0 → 26–31 frames in the second after restore; resize ErrorNativeWindowInUseKHR → 29–31 frames (F16 MAILBOX, 8-bit, FIFO). Minimized and dirty 252–264 → 47–49 loop passes/s; idle after restore 401–403 → 61. **Minimized windows are no longer drawn** (GL and Vulkan): GL with a 30 Hz change 129 frames per 5 s, 47–56 ms/s → 0 frames, 9–13 ms/s. Validation 0 messages (before: a leaked VkSwapchainKHR); resize stretch 0; suite flat. Video while minimized unverified (step 2b). |
| 2 | 7e52ff5 | media-minimized-playback | 82e382b stopped serving mpv while minimized: 236–240 drops per 10 s, avsync up to 151 ms, Reel's next file silent until restore (23.9 s), seeks 235–426 ms. A minimized window now serves its video undrawn (`IGpuImageSource.ServiceUndrawn`: setup + SKIP_RENDERING): 0 drops, avsync ≤ 2 ms, next file 589–612 ms, seek 45–48 ms. Audio was never broken. Process CPU at 1080p24 minimized 63–70 → 105–112 Mcyc/s (still below drawing: GL 162–178); mpv's vo thread does its normal per-frame work. |
| 2 | 27b8803, 95e93b5 | text-textarea-patch-keeps-lines | Same text handed back by a rebuild keeps the line starts (style/width/context/culture changes still re-wrap). New row text/rebuild around highlighted JSON 1.696 → 0.399 ms JIT, 2.051 → 0.424 AOT, 1,414 → 22 KB. Meridian URL keystroke with an 18,884-line response 3.00 → 1.70 ms, 1,612 → 182 KB. Fixed a real bug: row height kept the old font after a font-size patch. |
| 2 | 3a057ea, e7717a0 | text-textarea-span-scan | Binary search on a running max of End (Meridian's spans overlap; End goes down 1,180 times), one reused list. JSON wheel 0.834 → 0.399 ms JIT, 1.584 → 0.412 AOT (AOT gap closed), 14–16 → 5 KB. Meridian wheel with find open 7.4–8.1 → 1.05–1.13 ms (−86 %). Contract: hand a new span list to change spans. Trap: AOT grid rows move 7–16 % with unrelated code layout (README note). |
| 2 | d049be7, d5176b7 | text-shaper-direct | HarfBuzz driven directly, one buffer and font per shaper (in-use guard for shared scratch). text/create 4.24 → 3.26 ms (−23 % JIT, −19 % AOT), 5,792 → 4,317 KB; JSON open −22/−20 %, 10,802 → 6,595 KB; styled bidi ellipsis −16/−17 %, −50 % KB; 6 rows −25…−50 % alloc; TextBench cold/latin −20 %, hebrew −14 %; 0 slower. Bytes per call 936 → 176. Bit-identical vs SKShaper over 72.6 k calls on Windows and 51.3 k on WSL Fedora (Linux lacks Indic/Thai/emoji fonts: compared as notdef there). Handles flat over 10 M calls. |
| 2 | 48823d5, 8a91d93 | text-shape-cache-byte-bound | Shape/word/layout caches bounded by estimated bytes (24 / 4 / 4 MB; estimates within 6 % of measured), LRU by weight, one entry ≤ half the bound. Retained after close: CSV 23.2 → 4.2 MB, 5,000-block rich text 8.8 → 4.5 MB; 19k-line JSON kept on purpose (~22 MB: the TextArea's keystroke store until measure-lines lands). Hit rates identical on all 95 rows; 0 slower JIT; AOT grid moves are code placement (control build). Correction: the survey's 52/155 MB were probe-held documents; real totals 4–26 MB. |
| 2 | 8d30d31, d27ea6b | paint-effect-object-cache | Blur filters (≤ 32) and gradient shaders (≤ 256, keyed by content + rect) kept between frames. paint/tiles shadow 7.3 → 0, gradient 11.8 → 0 KB/op, JIT and AOT; 0 slower; goldens byte-identical. |
| 2 | 103f30e, d27ea6b | paint-blob-builder-reuse | One kept SKTextBlobBuilder: 216 → 136 B per new blob; scroll/jump 10.3 → 6.3 KB/op JIT (13.1 → 9.1 AOT); scroll wheels −17…−33 %; 0 slower. |
| 2 | c85396e | junction-lane-palette | History lane palette built once per theme, index loops: graph paint 16,224 → 0 B per frame (headless fixture). |
| 2 | 93b8fef | aperture-postercache-trim-thread | Correctness: the poster cache's evictions are disposed on the UI thread at the next Get (the download thread disposed images the UI could be drawing). ELEMENTS.md: dispose image sources on the UI thread. |
| 2 | e7a7b03, bf5adcc, fbb5afc, 7755670 | paint-selectable-label-paragraph | A selectable/linked label keeps its paragraph and draws from it. Render-pass bytes per hover frame: Junction diff 10.6 → 0.0 KB (AOT 12.2 → 0), Vesper reader 10.9 → 0.0 (AOT 12.6 → 0); wheel −10…−11 KB/frame (rest is VirtualList realization). New rows text/selectable viewer hover 13.8 → 0.4 KB/op, wheel 14.7 → 3.3. Time flat, 0 slower, pixels byte-identical (pixel test over alignments, RTL, ellipsis, spans, selection). |
| 2 | da2ccd8, f578b55 | test-alloc-first-run-flake | Two causes. Headless: real code — the DevTools peak trackers' ring grew (536 B) while frame times fell; now a fixed 64-sample ring of time slices. Skia: no object at all — `GetAllocatedBytesForCurrentThread` counted the unused rest of the thread's allocation piece retired by a background-GC suspension (heap walk: one free object). All allocation tests now use `tests/Shared/Allocations.cs` (least of up to 8 rounds) with TieredPGO off in 6 test projects. Failed runs over 30 fresh runs per project: Headless 1 → 0, Skia 2 → 0; mutation checks still fail. |
| 2 | 0cfdbfc, 289faf9 | layout-grid-linear-tracks | Grid cells bucketed in one pass, origins by prefix sums: grid/create −42 %, resize −54 %, edit −64 % JIT (AOT −61/−72/−78 %, placement control 3–6 %); 1.63 M loop steps per step → a few linear passes; bit-identical (random tests vs the old loops). Grid plan examined, not worth it. |
| 2 | d13fe82 | layout-leaves-ignore-offered-height | `IgnoresAvailableHeight` defaults true; VideoElement opts out. controls/expander toggle MeasureSize −72 %, leaf measurements −93 %, row −7 % JIT / −6 % AOT, layout −39/−47 %. Every box identical across all 97 rows and ~556k audited cache hits. |
| 2 | 19b94e9 | layout-maxlines-setter-dirty | Correctness: MaxLines and Overflow setters mark layout. Perf flat. |
| 2 | 30cb978 | checkbox-size-layout | Correctness: Checkbox, RadioButton and ToggleSwitch sizes from the theme re-lay out. Perf flat. |
| 2 | ae90f1f, 4195353 | a11y-node-extras | AccessibilityNode 136 → 80 B (rare fields in a side object). allocKB −13…−39 % on 9 a11y rows, JIT and AOT (checkbox UIA 142 → 87, edit every 10th 368 → 259, wheel UIA 425 → 260); 0 slower; events and trees identical. |
| 2 | 7def7b1 | a11y-snapshot-stale-fields | Correctness: a UIA wrapper answers its node's current Role/AutomationId (Group → Dialog now 50032). No event change. |
| 2 | d40d301 | a11y-atspi-snapshot-diet | AT-SPI path built once (TryFormat + span concat; WSL caught boxing on Fedora's .NET), string[] child paths, presized dictionary: ~500 → ~270 B/node; AT-SPI steps −38…−47 % (−44…−51 % with ae90f1f). Snapshots and signals identical; WSL green. No live AT-SPI client run. |
| 2 | 94ba38d | a11y-focus-unbuilt-node | Correctness (approved): a focus move onto an element mounted in the same handler is told after the next rebuild — +1 UIA focus and AT-SPI state-changed:focused (FindBar Ctrl+F, mount+focus); GetFocus answers. Perf flat. |
| 2 | 0912a5e | a11y-virtual-item-selected | Correctness (approved): list item nodes carry IsSelected; SelectedIndex notifies; a no-scroll move raises IsSelected on the old and new item on both bridges. UIA walk +0.2 KB/op (+2–3 %): one rebuild per move while a reader listens. |
| 2 | c7dffaa | apps-scenario-tween-signal-rebuild | Real-loop suite row via `HeadlessSession.RunLoop` (virtual clock, 1 kHz posts; loop is `Application.RunUntil`): 15.1 rebuilds per frame at baseline. |
| 2 | bd808d4 | reconcile-blueprint-hashset | No set for a parent with no children: 288 → 80 B per `.Children` call; component edit/reverse −10.7…−11.0 % allocKB and −9…−17 % time (JIT/AOT), tabs −8.4 %, a11y/create −10.5 %; 0 slower. |
| 2 | 65cdae0, e1d86a1, c583ef3 | reactivity-tween-tick-per-frame | Animations step once per frame that may be drawn (approved). Row 15.1 → 1.0 rebuilds/frame, −93.7 % KB, −7/−8 % time. Reel (AOT, Vulkan, 11 s): VideoPlayer rebuilds 616–653 → 312–313, 2.1–2.2 → 1.06 per frame, UI-thread 25 → 13 MB, gen-0 1 → 0, frames 511–518. e1d86a1 fixed 65cdae0 holding drawn tweens to the 16 ms idle wait (120 Hz tween 89 → 121 fps). Minimized auto-hide and toasts run to time. |
| 2 | 495b0b4, 6527b0d | reactivity-scope-threadstatic | `[ThreadStatic]` reactive scope (approved): 160 → 0 B per effect run; fan-out 94.4 → 16.2 KB/op (−83 %), −6 % JIT; reads off-thread or after an await no longer subscribe; DECISIONS.md updated. |
| 2 | ca59c6d, 4d82571 | layout-zorder-sorted-allocs | Z-order kept on the child list, filled on first ask, dropped by child-list changes and the ZIndex/Type setters (layout never runs for a handler's ZIndex write). Sticky wheel 3.1 → 1.9 KB/op JIT (−38.7 %), 3.5 → 2.3 AOT; 5,000-row wheel −6.7 % JIT, −12.0 % AOT (placement control ≤ 2.8 %); jump −11.2 % AOT. Junction with a panel open: 1 sort + 52 scans per frame → 0. 0 slower. Child list 32 → 40 B, PositionStyle +8 B. |
| 2 | 913204a | maxlines-setter-thread | Fix-up of 19b94e9: setters mark only what is not already marked, so `.MaxLines(1)` on a new label off the UI thread no longer throws in Debug; a laid-out label still re-lays out and is thread-checked. Perf flat. |
| 2 | 8b40fde, c1b8918 | runtime-palette-filter-allocs | Joined text kept while the palette is open, title-only commands matched once, one array sort: palette row 592.5 → 430.3 KB/op JIT (−27.4 %), 651.9 → 424.3 AOT (−34.9 %); min −11.1 % JIT, −7.3 % AOT; Filter 167/357 → 4.7/9.0 KB per query; results identical to the old code. |
| 2 | 6457f94 | runtime-syntax-registry-first-use | `CanHighlight` answers from a 297-extension table checked against the registry. Vesper's first code folder AOT 7.0 → 2.2 ms and 5.3 MB → 761 KB (JIT 32–38 → 4.4 ms); the TextMate registry loads at the first tokenize instead. |
| 2 | b23b203, 40d054d, 28fbeb8, 05414ab | text-textarea-measure-lines | Width per hard line measured without layout (`IRenderContext.MeasureTextWidth`, bit-equal; 4 MB width cache with a weight); last frame's line strings reused; shape bound 24 → 8 MB. JSON open −21 % JIT / −20 % AOT (median −35 %), 6,599 → 1,903 KB; typing in 2,000 lines −22 %, 465 → 222 KB; undo-redo −28 %; steady TextArea repaint 2.2 KB → 0; 10,000-line JSON keystroke 1.66 ms even at 8 MB (was 25 ms at 8 MB). Meridian first response frame 12.4 → 5.8 MB. Retained after closing the JSON 9.6 → 4.3 MB. JSON wheel +3.4 KB (lines shaped when drawn). Line starts identical over ~2 M lines. AOT TableView sort +5…7 % unproven placement. |
| 2 | b471108 | gl-vk-resize-duplicate-frames | One live-resize frame per change, not per event: frames per SetWindowPos step 2–3 → 1 on GL, F16 and 8-bit, never 0, never at an old size. GL drag CPU −33 % (AOT −31 %), 8-bit −13…−33 %, F16 flat (driver-side swapchain rebuild); F16 drag submits Skia 278 → 120, host 119 → 40. Held live-resize timer 90 frames/s → 0 with video still drawn (mpv 0 dropped). Resize stretch 0 of ~1,000 samples. |
| 2 | 2170222 | a11y-atspi-active-on-status | Linux bridge active only while org.a11y.Status says AT is on (HULTUI_ACCESSIBILITY=1 overrides). 603-node keystroke with no reader 212.8 KB → 1.54 KB (= no bridge). Live-verified in WSL with pyatspi (off: 1-object tree, 0 events; on: full tree; off/on republishes). |
| 2 | 72a4f96 | a11y-value-for-text | No Value change for nodes that offer text (Chromium-cited): UIA rich text keystroke −16.7 % JIT / −14.0 % AOT; the whole-document ValueValue (1.5 MB BSTR) and AT-SPI accessible-value (~750 KB) gone; Value still readable on request. TextArea unchanged (no text pattern yet). |
| 2 | 9c8bf91, cfb5a1a | a11y-richtext-text-snapshot | Rich text editor's accessible text made on request (versioned). a11y/type in rich text 2,100 → 54 KB/op, −43 % JIT / −48 % AOT (UIA −39/−46 %). Streams identical. AT-SPI with a reader still builds it per keystroke (its text-changed needs it). |
| 2 | 6b8bbe4 | a11y-range-value-events | Range on nodes; UIA RangeValue property changes; AT-SPI Value interface + accessible-value; told at most every 250 ms (Chromium's media timeupdate rate). Progress tick rebuilds 60 → 5 per second, −45 % JIT / −52 % AOT; VideoPlayer seek now announced (0 → 19 per 10 s). Live-verified. |
| 2 | 074d898, 333d7de | linux-bridge-sync-context | Correctness (found by the live check): the Linux bridge's connecting constructor hung under HultUI's UI SynchronizationContext. Now connects at once; samples/AtSpiVerify bulk check PASS (also live-verifies d40d301). |
| 2 | 466dce0 | meridian-response-format-on-ui-thread | Prettify, split, line starts and tokenize on the thread pool, cancellable (new response, mode, tab switch, close); plain first above 2,000 lines, colour when spans land. 18,884-line JSON: UI time to text 108.4 → 2.53 ms (−98 %), to colour 4.04 ms; 312 KB response 3.8 → 1.4 ms. Headless JIT. |
| 2 | 9b512c6 | syntax-maxlines-uncoloured | Meridian colours responses up to 200,000 lines (JSON ~5.5 µs a line on a worker; 98k lines 0.54 s, 4.7 MB of spans; the 2 M-character cap stops JSON near 100k lines). Merged find list kept between builds: URL keystroke with find open 8.4 → 1.8 ms, 2,945 → 178 KB. Junction/Vesper keep the 20,000 default (already on workers). |
| 2 | 545d1b6, b9e5a78 | layout-virtuallist-item-rows | Opt-in `.RenderWith((item, selected) => …)` (`ItemRowTemplate`): kept rows whose item (EqualityComparer) and selected state are unchanged; `InvalidateRows()` for anything else; theme object switch re-makes rows. Twin row virtual/selection walk 1,000 item rows: rows per step 19 → 3, 96.8 → 25.7 KB/op (−73.5 %), −6 % JIT / −10 % AOT; layouts −72 %, resolves −40 %; a11y tree and pixels identical to the old template; 0 slower. |
| 2 | a96c185 | junction-history-item-rows | Junction History on item rows (shape key: stats, compact, theme): rows per key 17 → 2.83; realize 0.155 → 0.072 ms JIT, 0.20 → 0.079 AOT; 212 → 41 KB per key (−80 %); frame bytes −53 %, frame time −7 % JIT / −10 % AOT (headless, 80 keys). 16/17 frames byte-identical; the 17th fixes hover lost on a key press. |
| 2 | 337140a | wsl-shaper-real-fonts | d049be7 bit-identical on real Linux faces: 116,438 shaper calls, 0 differences, 80 faces, 0 missing glyphs in every script; Text 192/192, goldens 39/39 in WSL. New test that the corpus reaches real glyphs (mutation caught). |
| 2 | 595b8dd | wayland-minimized-restore | Correctness (found in WSL): after the app minimizes its own window on Wayland, SDL keeps the minimized flag after a restore, so since 82e382b the window stayed undrawn (0 frames in 15 s). A focus gain after a minimize now ends the minimized state on Wayland. Also verified: 0f5816a on Mesa gets depth 24/stencil 8 anyway (no stencil-only config; no saving, no risk, 48 contexts, 0 failures); Vulkan on lavapipe and Dozen pixel-exact through resizes, validation 0 messages; destroy-first not taken on Linux. WSL Linux suite: 9,917 passed, 0 failed. |
| 3 | e0b816f | vesper-grid-resize | User report (2026-10-10): after a window resize the thumbnail area kept the old width until a key press. Brief and Thumbnail grids are sized in the build from the window, but a resize rebuilt only for a scale change or a wrapped diff/viewer. Now rebuilds when either pane shows Brief or Thumbnails. Correctness; two headless tests (fail without it). |

## Examined, not worth it

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

- **Round 2 build:** gl-vk-one-submit-per-frame (61edb19, DECISIONS.md "One submit per frame"): GL glGetError 3 → 1 and F16 submits 2 → 1 per frame, but frame time only −0.8…−4.5 % (JIT/AOT) because the driver wait moves from Flush to Present; pixels identical; reverted. So round 2 question 12 (no-op glGetError) is moot.

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
  under a shared GPU (61edb19); working set and start-up (no new lever).

## Needs the user

Round 3 questions (2026-10-10, open). Recommendations in brackets.

1. **mpv-vo-thread-minimized:** turn the video track off while minimized (`vid=no`; Chromium does
   this for background video)? Reel minimized: process CPU −38…−71 %; at every restore video
   resumes 84–300 ms late (today 5–36 ms), A/V sync off by up to 170 ms for ~1.3 s, a 66–68 ms audio
   stall in 2 of 3 runs. [Keep today's behaviour.]
2. **Pointer moves once per frame:** (a) every pointer-move handler runs at most once per frame
   that may be drawn, with the last position (as browsers do; a drawing app would lose points
   without a coalesced-points API), or (b) only the splitter, pan, slider and scrubber? [(b).]
3. **ToastHost:** `onChanged` runs on appear, change and go only, not per fade step? [Yes.]
4. **SelectionColor:** with reconcile-restyle-on-change, an app-set `TextElement.SelectionColor`
   would start to show (today the theme overwrites it; no app sets it)? [Yes.]
5. **Animation clock** (with runtime-idle-wakeups): a tween started between frames starts its clock
   at its first step; frames shift by at most one interval? [Yes.]
6. **layout-virtuallist-row-inherit:** rows of a one-argument VirtualList inherit text style from
   their list, as item rows and every other child do (0 px changed in every app screen checked)?
   [Yes.]
7. **Added a11y events:** Name, Description and ExpandCollapse changes made by a rebuild
   (a11y-patched-props-untold); role changes (a11y-role-change); AT-SPI items and selection moves
   of lists over 2,000, on demand only (a11y-virtual-items-and-selection); extra children-changed
   for middle inserts (a11y-atspi-cache-child-index)? [Yes to all four.]
8. **TextArea:** apply round 2's rule (no whole-text Value change once it offers text: −121,744
   characters per keystroke at 2,000 lines), or keep Chromium's `<textarea>` mapping? [Apply the
   rule.]
9. **a11y-item-label-keep:** extend the item-rows contract to the a11y label (an equal item keeps
   its label until `InvalidateRows`)? [Yes.]
10. **a11y-richtext-reader-keystroke:** a large change to the public AccessibleText for long
    documents edited with a reader running (1,974 KB per keystroke at 5,000 blocks)? [Not now.]
11. **paint-native-blob-handles:** HultUI.Rendering.Skia calls three libSkiaSharp C functions
    directly (scroll/jump −62 % JIT, −74 % AOT; sub-blobs then cost no managed bytes); ties the
    renderer to SkiaSharp's C API at the pinned 3.119.2, as d049be7 did for HarfBuzz? [Yes, with a
    test that fails if the exports change.]
12. **aperture-hero-scrim-seam:** end the hero's horizontal scrim at 0 % instead of 15 % alpha
    (a visible seam at x 998/999; a deliberate pixel change, not perf)? [Yes.]
13. **apps-junction-components:** re-open "split Junction into components" (declined 2026-10-06)?
    Junction History key: Build and reconcile 84 % of the bytes, unchanged titlebar, sidebar and
    toolbar 70 % of the Build; Meridian URL key: Build 92 %; Vesper: both panes rebuilt every key.
    [Yes, Junction's chrome first, one app at a time.]

To do for the user when the work is done: remove the "aperture-survey" device registrations on
jellyfin.home (rounds 2 and 3) and revoke the API key.

Round 2 questions, answered 2026-10-09 (the user took every recommendation; 11 yes):

1. Gate the Linux bridge on `org.a11y.Status` → **yes**, with an always-on override.
2. List items on demand at any size → **not now** (declined for plan 17 round 2).
3. Drop the Value change for elements with a text pattern → **yes**.
4. Range value events for Slider/ProgressBar → **yes**.
5. Added events for a11y-focus-unbuilt-node and a11y-virtual-item-selected → **yes**.
6. Tweens step once per rendered frame → **yes**.
7. `[ThreadStatic]` reactive scope → **yes**, low priority.
8. Colour Meridian responses over 20,000 lines → **yes, after** tokenizing moves off the
   UI thread.
9. Opt-in VirtualList item-rows API, adopted one list at a time → **yes**.
10. Tab strips that narrow then scroll → **outside plan 17** (not perf).
11. Install Mesa and Noto fonts in WSL Fedora → **yes**.
12. No-op glGetError — moot: 61edb19 showed the driver wait only moves (declined).

13. **wsl-atspi-stack** → **yes** (2026-10-10): installed at-spi2-core 2.60.7, dbus-daemon
    1.16.2, python3-gobject 3.56.3, python3-pyatspi 2.58.2 and gobject-introspection (needed for
    pyatspi's typelibs) in WSL Fedora; `import pyatspi` works. Live AT-SPI checks are now possible.

Note: the round 2 app survey registered a Jellyfin device named "aperture-survey".

Provided 2026-10-07: **Aperture API key file** at `C:\Users\fredr\.secrets\jellyfin-api-key.txt`
(outside the repo; never copy or print its contents). Use with the run-hultui-app driver:
`--app aperture --server http://jellyfin.home:8096 --api-key-file <that path>`. Real-library
measurement (poster grid, artwork decode, a11y on a large library) belongs to the app
workloads expert in the round 2 survey, and to aperture-poster-decode-scale's build. The
user should revoke the key when the work is done.

Answered:

- 1. Flex basis of truncating labels → **clamp** (2026-10-08, after the side-by-side renders):
  candidate layout-truncation-clamp-basis.
- Truncating labels shrinking below their longest word → **yes** (2026-10-08): candidate
  layout-truncating-min-zero.
- 2. Bounds events on scroll → **yes**, one scroll change instead: candidate
  a11y-scroll-bounds-event.
- 3. Stale focus state → **yes**, fix it: candidate a11y-focus-state-on-tab.
- 4. Aperture poster decode size → **yes**: candidate aperture-poster-decode-scale.
- Aperture hero images at 150 % → **yes, make them sharp** (2026-10-08): candidate
  aperture-hero-scale.
- Meridian response-pane breakpoint → **yes, move it to ~1320 px** (2026-10-08): candidate
  meridian-pane-breakpoint.
- Junction Branches compact table at ≤1000 px → **yes** (2026-10-08): candidate
  junction-branches-compact.
- 5. `MarkDirty()` joining an open batch → **no**: it stays synchronous, as REACTIVITY.md
  documents. The guidelines should tell app authors to call it once per handler.

Decided by the orchestrator: idle wake-ups count as a bar (brief updated); a change aimed
at per-frame allocation checks its bytes on AOT too (brief updated).

## Open

- **7a827e9 on Linux/KWin and on AMD/Intel Windows:** not measured. Linux keeps the chained
  path. If AMD/Intel do not stall, destroy-first may cost them 21–24 ms per resize, as it
  cost NVIDIA's IMMEDIATE and FIFO. A real Aperture resize run is also still to do.
- **0f5816a on AMD, Intel and Linux (GLX/EGL):** not measured (WSL Fedora has no GL
  stack; installing one needs sudo). Depth is a minimum in WGL/GLX/EGL, so 0 cannot fail
  where 16 succeeded.

## Log

- 2026-10-07: brief, ledger and `scripts/perf/idle-gate.ps1` written; round 1 survey started.
- 2026-10-07: user removed the round cap and allowed more roles. Split into eight: text
  pipeline out of SkiaSharp, reactivity and reconcile out of layout, accessibility
  bridges out of .NET, and a new app-workloads profiler. Experts 5–8 joined the round 1
  survey late; 1, 2 and 4 were told their narrower scope mid-survey.
- 2026-10-07: session restarted and moved to worktree `distracted-mendel-5071ba`; the eight
  surveys were resumed from their transcripts. All eight reported; no expert satisfied.
  Reports saved in `17-reports/`; survey worktrees removed (probe code kept in the
  scratchpad). Round 1 build started: 12 items, best first.
- 2026-10-08: round 1 build items 1–11e and the truncation app follow-ups landed (see
  Landed). Paused before 11f at the user's request; nothing uncommitted. Two questions open
  for the user (Needs the user). Nothing pushed or merged.
- 2026-10-09: round 1 full suite passed (30 projects, 0 failures); merged into main as
  64ba531 on a detached origin/main (main had 3dca7b4/de5f0e6 from another session), suite
  passed again on the merge, pushed de5f0e6..64ba531, primary fast-forwarded behind the guard.
- 2026-10-09: CI green on 64ba531 (run 37861109308). Round 2 survey started, eight experts.
- 2026-10-09: round 2 surveys in (all eight not satisfied); reports in `17-reports/r2-*`;
  queue re-ranked into 14 build steps; 12 questions for the user; survey worktrees removed.
- 2026-10-09: the user answered all 12 round 2 questions (recommendations taken; WSL install yes). Queued items 15–18.
- 2026-10-09: installed Mesa + Noto fonts in WSL Fedora (user approved); round 2 step 6 next.
- 2026-10-10: round 2 full suite passed (30 projects, 0 failures); merged into main as 0951288, pushed 64ba531..0951288, primary fast-forwarded.
- 2026-10-10: CI green on 0951288 (run 38072271269); tagged and pushed the five app releases (3 + 2).
- 2026-10-10: all five app releases published with installers (Vesper re-run once: runner not acquired). Round 3 survey started.
- 2026-10-10: round 3 surveys in (all eight not satisfied); reports in `17-reports/r3-*`. Three
  round 2 regressions (82e382b, 7e52ff5, 0912a5e) and a UIA closed-window leak lead the queue;
  16 build steps, 13 questions for the user; survey worktrees removed (probe code kept in the
  scratchpad). Paused for a break at the user's request.
- 2026-10-10: two user reports during the pause. Vesper's thumbnail area kept its old width after a
  resize until a key press: fixed e0b816f (Brief and Thumbnails rebuild on resize). Aperture's Home
  resizes much slower than other apps: an app-workloads diagnosis agent is measuring it (Vulkan F16
  swapchain vs whole-app rebuild per step vs card pictures); result goes into the round 3 build.

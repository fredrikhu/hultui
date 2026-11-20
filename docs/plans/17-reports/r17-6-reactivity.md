SATISFIED: no

REVIEW: none. The worktree is clean at 0df5e3ea. All probes were reverted from files restored out of HEAD.
- **ec57c4ee: no objection (read).**
  - The set-aside runs after the `IsDrawingFrame` return, and `FlushNow` runs only when `outer > 0`.
  - A nested `RunFrameCore` pass has already set its batches aside, so a live frame inside it gets `outer = 0`, as a user's drag does.
  - A build that throws in the flush gets its batches back in the `finally`.
  - A build that itself resizes gets a nested live frame with nothing set aside, and the outer one then finds no pending resize.
- **5766a137: no objection (read, headless only).** A toggle stays 2 frames. A refused toggle writes the player's flag twice, which is one extra build and rare.
- **49b0da84: no objection.** Still building: a breakpoint or a scale change (`Rerender`, the chrome and the view), and the splitter's press, release and arrow keys (`MarkViewDirty`, so a drag is 2 view builds). See Examined.
- **80e94eeb: no objection.** Kept texts are dropped on landing, on a scheme change and by the 16 MB LRU. They are never dropped on a repository close, nor for working-tree keys of older generations, which can never be asked for again; those sit inside the 16 MB bound until evicted. Small, inside the accepted bounded trade.
- **791d1a44: no objection (read).** A single staging move is unchanged by construction (`IsMoveOf` is true for its own keys): 1 `MarkDirty` build at the click, 1 git command, 1 `RefreshAll` reload. Interleaved moves get the same counts and are now correct. No processes were counted.
- **d3e40ecb: the emitted code is the same.**
  - 4.14.0 is the only Microsoft.CodeAnalysis.CSharp in the NuGet cache.
  - A restore with `4.*` writes the same assets (4.14.0).
  - The generator DLL is byte-identical either way (SHA-256 93F3A0DB…4F63).
  - Reactivity and Components are untouched in the range otherwise.
- **38ec9773, b8a3dcf5 (VirtualList): nothing odd, not bisected.** Junction's lists make 34 rows a file stop, and a History Down is 1 build at 16.5–17.8 KB. Not measured in Vesper.

CANDIDATES:
- id: reactivity-junction-colours-before-rows (re-ranks Waiting junction-colour-landing-remakes-rows, owner 6 with 5)
  what: A file's colours landing after its rows' first frame makes every row on screen again. Either recolour the realized code labels in place (no `InvalidateRows`), or land the colours before the first frame.
  evidence:
    - **Probe:** headless JIT probe in Junction.Tests: 5 real C# files of HultUI.Core (every 12th line changed), Commit view, 1,440×900; 4 first visits by Down, 4 revisits by Up.
    - **Unwrapped:**
      - A first visit is 744.1–822.5 KB: 2 builds, 2 frames, 68 rows made, 514 text measures.
      - The landing alone is 332.9–395.5 KB: 1 build, 34 rows, 264 measures. Phase bytes: build 60.3–65.2 KB, render 272.5–282.8 KB.
      - A revisit (colours cached) is 365.3–378.4 KB: 1 build, 1 frame, 34 rows, 250 measures.
    - **Wrapped:** first visits 744.5–955.5 KB, landing 307.5–321.3 KB, revisits 360.7–390.8 KB.
    - **Floor:** a probe that skips the row invalidation when only the colours changed (wrong pixels; a floor only) brings the landing to 62.0–66.9 KB (0 rows, 0 measures, render 0), and a first visit to 474.0–545.6 KB (−36…−40 %).
    - **Spans are not the cost:**
      - With no spans at all, the landing is still 325–379 KB.
      - Keying the rows by index, so `InvalidateRows` patches the kept rows, cuts measures 264 → 143 (wrapped 258 → 138) but not bytes (+1.7–2.1 %; render 276–288 KB).
      - With keyed rows and no spans, render is still 263.6–265.4 KB.
      - So an invalidated row costs about 7.8 KB at paint even when patched in place. The cause is not isolated (a lead for 5 and 6).
  real app:
    - Junction, every first visit of a file in the Commit view (the History prefetch colours only a commit's opening file), and the first Commit open.
    - Browsing a commit's files is a core action.
    - Real window, AOT: first visits 744.4–817.4 KB and 2 frames, revisits 362.5–375.6 KB and 1 frame; first Commit open 1,202.2–1,221.2 KB, 2 builds, 68 rows.
  expected: churn bar.
    - (a) Colours before the first frame: a first visit becomes a revisit, −50…−54 % and 2 → 1 frames.
    - (b) Recolour in place: up to −36…−40 % of a first visit. This assumes setting `Spans` on the kept, selectable code labels costs only their own paragraphs, which a build must measure first.
  cost:
    - (b) is small–medium, app side. `RenderDiffRow` records each realized code label by row; at the landing it sets the new spans and skips `InvalidateRows` when the row shape differs only in Highlight/Rows. No API, same settled pixels.
    - (a) changes when the diff first shows: NEEDS THE USER.
    - Keyed rows with a stable row identity would also keep a focused review control across a landing (Waiting junction-review-focus-colours).
- id: reactivity-junction-view-tabs-part
  what: Give the toolbar's seven view tabs a part of their own. Today every view switch builds the whole toolbar part (it reads `View`), git actions included.
  evidence:
    - The toolbar part builds once a switch for 52.6–53.1 KB: tabs 33.8–34.2 KB (about 4.9 KB a tab, 5 elements each), Fetch/Pull/Push/stash/tools 15.9 KB, about 3 KB reconcile.
    - **Prototype:** `ViewTabsPart` mounted inside the toolbar's build, behind an environment switch. The toolbar part then builds 0 times a switch and the tabs part once.
    - **Headless:** −18.4 KB a switch:

      | Switch to | Before → after (KB) | Change |
      |---|---|---|
      | History | 136.2 → 117.8 | −13.5 % |
      | Tags | 98.0 → 79.6 | −18.8 % |
      | Overview | 150.8 → 132.5 | −12.1 % |
      | Branches | 160.0 → 141.6 | −11.5 % |
      | Changes | 202.7 → 184.3 | −9.1 % |
      | Commit | 454.2 → 435.9 | −4.0 % |

    - **Real window** (2 launches a side): History 134.5–135.2 → 116.3–117.0, Tags 96.9 → 78.7, Overview 161.7 → 143.5, Commit 412.4 → 394.1. Repeat launches identical.
  real app: Junction, keys 1–7 and tab clicks; tens to hundreds of switches a session.
  expected:
    - Churn bar on the History, Tags, Overview and Branches switches (−11…−19 %); not on Commit (−4 %).
    - Per-tab parts that build only the 2 tabs whose active state moved (tabs read `View` untracked; `SetView` bumps the two tabs' own revisions) would save about 24 KB more: History switch about −31 %, Tags about −43 %. Not prototyped.
  cost: small, app side (JunctionApp.Chrome.cs). Extend ChromePartTests to the new part. No API. One more ComponentHost in the toolbar row: confirm pixels by frame hashes.

REAL WINDOW:
- **Setup:** one hold, 4 launches of a NativeAOT Junction host built from this worktree with the probes in, tab split 0/1/0/1. The scene: the probe repo, History, then Commit, 4 Downs, 4 Ups and 6 view switches.
- **Monitor:** the 4K at 150 % (window DPI 144; logged DPI-virtualised as `\\.\DISPLAY2` 2560×1440, primary), 120 Hz, GL. Timer resolution 1 ms (Steam). The OS cursor was over the window.
- **Hold:** waited 149.8 s, held 146.2 s. 0 foreground takes, 0 nvlddmkm events. The idle gate before the hold failed (another expert's testhost), so only counts and bytes are reported.
- **Result:** the real window showed nothing the headless run did not. Every bytes, frames, builds and rows count matched headless JIT within 0.5 % and repeated exactly across launches. With no UIA client asking, a11y is off in both.

WAITING, RE-RANKED:
- **junction-colour-landing-remakes-rows:** stronger. It is every first file visit, not only the session's first Commit open. Folded into the first candidate above.
- **junction-summary-overtaken-at-start:** unchanged; no commit in the range touches `SummaryFor` or AsyncLoader. Low lead.
- **junction-review-focus-colours:** unchanged. A stable-key or recolour-in-place build of the first candidate would fix it as a by-product. Keep it with that candidate.
- **vesper-local-waiting-text:** unchanged ("Connecting to" at CommanderApp.Pane.cs:1190). The user's call, not perf.
- **junction-changes-leads, junction-review-old-path, junction-leads (remaining):** unchanged (no `OldPath` or `SelectConflict` change in the range).

EXAMINED, NOT WORTH IT:
- **Keyed diff rows alone:** −46 % measures a landing, +2 % bytes. Only worth it with the recolour.
- **Junction's sideways pan:** 12 wheel steps make 1 build and 34 rows in all (0.08 builds a step). The pan moves cells without a build.
- **Splitter press and release:** 2 view builds a drag, once a drag.
- **Files view first open:** 3 builds (loading states), once a session.
- **Meridian send** (read): the view builds twice (start, end) and the chrome does not; the elapsed readout is rewritten in place.
- **Reel and Aperture player chrome** (read): no build reads the position (untracked or effects). The transport is its own component.
- **Vesper navigation:** PartTests pins it to the window plus that pane's own four parts. Not re-measured.
- **80e94eeb's dead working-tree keys:** inside the 16 MB bound.

NEEDS THE USER:
- First candidate, route (a): may Junction hold a newly selected file's first frame until its colours land, when the tokenize is short? It would give 1 frame instead of 2 and about −50 % bytes a first visit, but delays when the diff first shows.
  - Before/after frame sequences can be captured headlessly by frame hashes of one file stop.
  - Route (b) needs no decision.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\35903ed7-ff10-416d-84e9-b6c09675ab6b\scratchpad\r17-reactivity\`
- `ProbeR17Tests.cs`: the headless probe (colour landing, pan, builds per action).
- `probe17.patch`: the probes at 0df5e3ea — ComponentHost/Part build counters, PerformanceCounters phase bytes, and Junction's `PROBE_KEYROWS`/`PROBE_NOSPANS`/`PROBE_NOINVAL`/`PROBE_TABSPLIT` switches. The tab split itself was made by `patch_tabs.py`; `patch_perf.py` makes the phase bytes.
- `Chrome.withprobe.cs.txt`.
- Results: `colour-*.txt`, `builds-*.txt`, `pan-*.txt`.
- Real window: `start\` (the host; `src\Script.cs` writes PROBE lines with builds, rows and chrome builds at each mark), `aotp\Junction\`, `rest\` (`common17.ps1`, `batch17a.ps1`, `make-crepo.ps1`, `sum17.py`, `runs\h1-*`, `runs-h1.log`), `fx\crepo`.
- Logs: `locks.log`, `holds.log`, `gate-h1.txt`.

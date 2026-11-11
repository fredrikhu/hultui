SATISFIED: no

**Method.** I did not re-run the upgrade's walks. Most numbers come from rows already measured; I ran only the extra walks listed here.
- **Round 10's effect on the apps.** Base: round 10 survey's after side (3462f18e, round 9's release, 2026-11-09). After: the upgrade's base side (ca31555d, round 10's end). Both use SDK 10.0.300 / ILCompiler 10.0.8, identical walk sources (Program.cs, R9.cs, R10.cs hashes equal) and identical fixtures (jrepo 3c2afc67; every coll*, images, spec*.json the same file count, bytes and hashes).
- **Extra runs:**
  - a Vesper bisect point (b2fdcf52, AOT);
  - same-day re-runs of the round-9-release and round-10-end AOT hosts (Vesper and Junction);
  - a new JIT walk host on dee48fbb (net11.0, SDK 11.0.100) with a tab-switch walk and a view-size probe.
- **Safety:** all headless; 0 real windows, 0 screen captures, 0 Jellyfin sign-ins. My fixture server on 127.0.0.1:8767 is stopped, build servers are shut down, and the worktree is clean at dee48fbb.

REVIEW: none.
- **Vesper rose +1.5…+7.9 % from round 9's release to round 10's end, but the cause is the machine, not round 10's code:**
  - Tab 92.2 → 99.5 KB, folder in/out +2.9 %, insert +1.9 %, partner/typefind +1.5 %.
  - The same 3462f18e AOT binary gives Tab 99.4 KB today (92.2 on 2026-11-09). Folder in/out is +2.9 %, insert +1.6 %, partner +1.1 %.
  - The bisect point b2fdcf52 already reads 99.4.
  - Same day, round 9's release vs round 10's end: all 11 Vesper walks are equal (thumbs −3.7 %; transfer is a timing walk).
- **Junction:** the same binary moves −0.3…−4.7 % between days. Same day, only bigfiles-100ms is +0.4 % (581.8 → 584.4 KB a key), under every bar.
- **.NET 11 on the apps:**
  - The upgrade's 69 AOT walks: geometric mean 1.000.
  - The only allocation that is new on .NET 11 is JIT-only (c99e23bf), and the apps ship as NativeAOT.
  - No app adds a nullable value type to a `HashCode` in a loop. Junction's review signature (JunctionApp.Review.cs:640-660) adds only non-null ints, bools and ids.

**Round 10 helped the real apps.** Over the 69 walks the geometric mean of after/base is **0.674**. The sum of KB a key falls 1,919,189 → 38,495 (−98.0 %), mostly the Runner.
- **Runner** (rail counted from the scanned tree, kept result rows, results-only builds):
  - 200 requests: 1,783,391 → 24,979 KB a run (−98.6 %); 50 requests: 121,536 → 5,800 (−95.2 %).
  - MeridianApp builds 606 → 208 a run at 200 (top-level builds; per[top]) and 156 → 58 at 50.
- **Vesper Sync** (kept rows, live ring): 300 files 2,568.6 → 0.5 KB a key; 60 files 453.7 → 0.5. Builds a key: 1 app + 2 parts → 0.
- **Meridian response pane drag** (size through a Ref): 39.2 → 3.6 KB a move (−90.9 %); builds 1 → 0.
- **Discovery map as a VirtualList:**
  - Click at 600 endpoints: 81.3 → 23.4 KB (−71.2 %).
  - But layout visits a click 19.7 → 70.3, and measure misses 59.2 → 70.4. Layout is 7.5 % of the click's cycles.
  - This is the app case of the Waiting item virtuallist-selection-relayout (owner 1).
- **Discovery from a URL:** 6,418 → 2,964 KB (−53.8 %); measure misses 3,092 → 293, layout visits 2,482 → 211.
- **Aperture toast frames** (the toast's going-away build is view-only): 0.5 → 0.4 KB (−18.6 %).
- **Not seen by any walk** (all 22 Junction walks are within ±0.5 %): Junction's prompt typing, view-only clicks, diff width kept, pane drag and pan; Brief's columns and return; the Markdown grammars. Their in-app numbers are the build steps' own headless walks. Brief Down is unchanged (2.6 KB).
- **Shares of UI cycles, 64 walks, .NET 11 AOT:**
  - Render (headless): median 90.3 %, p25 86.3, p75 96.1.
  - Build: median 1.1 %, max 10.7 (Vesper folder in/out).
  - Layout: median 0.4 %, max 8.3 (Meridian response tabs).
  - Accessibility: max 6.5 % (Aperture grid with a reader).
- **Start-up shares** (the upgrade's 42 guarded launches, medians; base = after within 0.6 points):
  - GL apps (Junction, Vesper, Meridian): process start to SDL_Init 13.6–14.8 %, SDL_CreateWindow 58.5–66.1 % (the driver load), first frame 16.0–24.8 %.
  - Vulkan apps (Aperture, Reel): 10.3–10.5 %, 24 %, Vulkan instance and device 26.2–26.6 %, first frame 31.4–32.2 %.
  - Nothing new: Vulkan stays opt-in.

CANDIDATES:
- id: apps-meridian-body-formatted-again (owner 6, app; 4 for the retained-memory check)
  what: Meridian formats and colours a response body again on a worker every time it is shown again: each switch back to a request tab, and each pretty/raw toggle. It has one job slot keyed by tab, response and mode (`_respJob`, MeridianApp.Response.cs:461-505), so the view and colours of the tab or mode just left are thrown away.
  evidence:
  - **New tab-switch walk** (JIT): two tabs, each with its big response sent; 20 clicks alternate between request-tab-0 and request-tab-1, each followed by 150 ms of sleep and 250 ms of frames.
    - HTML and JSON tabs: a switch to the HTML tab is **51,016 KB on all threads**, to the JSON tab 6,252 KB.
    - On the UI thread a switch is 316 KB: 1 MeridianApp and 6 Part builds.
    - GCs over 20 switches: **35/8/2**.
    - XML and JSON tabs: XML 10,864 KB, JSON 6,228 KB; GCs 10/2/1.
  - **Pretty/raw toggle** (the upgrade's AOT walks, 20 toggles). UI thread 124–149 KB a toggle in every case; Part builds 2.5–3 a toggle.

    | Body | Raw, all threads | Pretty, all threads | GCs |
    |---|---|---|---|
    | JSON | 5,739 KB | 6,125 KB | 7/0/0 |
    | HTML | 2,639 KB | 52,125 KB | 32/3/0 |
    | XML | 1,359 KB | 10,671 KB | 6/0/0 |

  - Round 9's "pretty HTML/XML: worker, once per response" is once per showing in fact.
  - The bytes are mostly TextMateSharp's tokenize (round 9 step 14). A kept result avoids that work instead of trying to shrink it.
  - While the worker runs, a tab switched back to shows an empty body (Response.cs:428, "still being formatted"). Bodies over 2,000 lines show uncoloured text next, then colour.
  expected: **memory churn bar**, all-thread KB per switch-back or toggle-back to a kept view:
  - HTML 51,016 → about the UI thread's 200–316 KB (−99.4 % or more); XML 10,864 → about the same (−97 %); JSON 6,252 → about the same (−95…−97 %).
  - Builds a switch or toggle: the body's 2 landing builds go, so 3 → 1 Part build.
  - Gen-1/gen-2 GCs over 20 switches 8/2 → about 0.
  - This is an estimate: I did not prototype the cache.
  cost: small–medium, app only, no API.
  - **Retained memory:** one view per kept entry. View-size probe (JIT, `GC.GetTotalMemory(true)`; the body's text, lines, line starts and per-line spans):
    - pretty HTML (3,641 lines): 1,265 KB;
    - pretty XML (2,463 lines): 967 KB;
    - JSON (2,004 lines): 364 KB;
    - raw HTML/XML (one line): 24–62 KB.
  - That is 9–27× the raw body. A small least-recently-used cache, for example 4 (tab, response, mode) entries, bounds it.
  - **Visible change:** frames during a switch change (see NEEDS THE USER). Frames at rest do not.

WAITING, RE-RANKED:
- **Suite scenarios:** still worth one row, document-switch.
  - Junction Files Down is 86.2 KB a key: 1.23 JunctionApp builds a key (the file's text lands and builds again), 149.1 measure misses, 60.8 layout visits; build is 8.6 % of cycles. Unchanged since round 10.
  - Round 10's two other patterns landed as suite rows in its steps 4 and 7. No suite row covers a list cursor that swaps a document whose text arrives later.
  - The tab-switch case above is an app cache, not a suite row.
- **junction-review-old-path:** still reproduces. `GitFileDiff.OldPath` is never set (GitDiffLine.cs:31; the doc comment at :44 says so). No round 10 commit touched apps/Junction/Git. Not perf.
- **junction-leads (remaining):** still true. `SelectConflict` (JunctionApp.Changes.cs:1477) has 0 hits in tests/. Not perf.
- **aperture-poster-as-hero:** the code is unchanged (the Primary fallback at ApertureApp.Cards.cs:538-539). Not exercised (no sign-in).
- **aperture-tmdb-size-fallback:** unchanged (RequestModels.cs:419, `w1920`, no fallback). Not exercised.
- **junction-narrow-gitlab-glyphs:** round 10 touched the GitLab views only with `MarkViewDirty` (JunctionApp.MergeRequests.cs, 4 lines in, 3 out). Not exercised (needs GitLab).
- **meridian-pane-choice-not-persisted:** still reproduces. `_responseBottomChoice` (MeridianApp.cs:126) is flipped at :307 and never saved.
- **meridian-min-width-wraps:** I did not take new screenshots. Round 10's Meridian commits only moved the pane's size behind a Ref (bfcba907: same `ResponsePaneSize`, room and minimums) and emptied refs. The tab strip and the Send bar are untouched, so round 10's shots stand.
- **junction-chromeparttests-load:** 0 failures in the upgrade's 2 Windows full runs (Junction 1,092 of 1,092) and 2 Fedora runs; round 10's release CI was green. Keep watching; nothing to do.
- **meridian-history-test-midnight:** still true, and wider than the ledger says.
  - HistoryTests.cs:411 sends at `now.AddMinutes(-1)`, which is yesterday from 00:00 to 00:01 local.
  - The app itself reads `DateTimeOffset.Now` in 11 places in MeridianApp.History.cs (:44, :46, :79, :134, :302, :319, :321, :406, :469, :483, :521). A fixed clock in the test alone cannot fix it.
  - Same risk from 00:00 to 03:20 local:
    - HistoryTests :356-384 (sends from now − 90 min; :380 compares against a second `Now`);
    - HistoryTests :468, :547, :601;
    - PartTests:42; ReaderFocusTests:122.
  - Fix: History reads `_app.Time.GetLocalNow()` (Meridian already uses `_app.Time` for the web preview and the response workers), and the tests set noon. Small; tests only besides that.

EXAMINED, NOT WORTH IT:
- **Vesper +1.5…+7.9 % and Junction −4.7…+0.5 %:** the machine, not round 10's code. The same binary reproduces them across days (see REVIEW).
- **Junction bigfiles +0.4 %** (2.6 KB a key, same day): under every bar.
- **Junction held key over 40 files of 8,000 lines** (bigfiles walk, AOT). I left it as not judged:
  - At a 33 ms pace: all threads 2,367–3,110 KB a key against 585 on the UI thread; 0.65–0.85 git shows a key; GCs 6–7/3/3 per 40 keys.
  - At 100 ms: 1,133–1,147 KB a key; 0.10 git shows a key.
  - On my JIT host: 0 git shows (the prefetch finished during the slower settle).
  - The walk's pace is the headless clock, while the worker and `Task.Delay(HighlightRest)` run on real time. So the walk cannot say what a real 33 ms key repeat reads.
  - JunctionApp.Highlight.cs:196-201 already rests a file reached while another tokenizes. Measure with real-time key repeat (`JUNCTION_BENCH`) before calling it a candidate.
- **Junction Changes Down over 300 files:** 294 KB a key, 83 % in render. That is the first draw of 26.2 new diff rows (9.4 KB a row; round 7: 9.35), known as unavoidable.
- **Meridian find typing, 59.1 KB** (1 response-part build a character) and **History search, 50.0 KB** (1 view build a character): both designed so (round 7 and round 6); ±0.1 %.
- **Vesper folder in/out:** 288 KB and 4 builds a key (navigation, rows landing, footer); 55 % of the bytes are first draws; once per Enter.
- **Meridian palette with a reader:** "frameOther" is 58.8 % of cycles and 17.3 of 32.2 KB a key. That is work outside the probe's scopes (the overlay), not a lead. Bytes are flat since round 8 (31.9 → 32.2).
- **Aperture Discover Down:** 0.67 builds a key, cards already kept; examined in round 9.

NEEDS THE USER:
- **apps-meridian-body-formatted-again: frames during a switch change.** Today a tab switched back to shows an empty body until the worker lands; a toggle shows the other mode greyed (opacity 0.45), then uncoloured text, then colour. With a kept view the finished, coloured body appears at once.
  - Approve that change?
  - How many views to keep: about 0.4–1.3 MB each for 40–90 KB bodies. [Suggested: the last 4 (tab, response, mode).]
  - Before/after images: headless frames of the first frame after a click on request-tab-0, before and after the change.
- **Jellyfin:** 0 "aperture-survey" sign-ins this round. The earlier devices and the API key are still to be removed and revoked.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\ec6d577c-f9ba-4479-acbd-908e67c0ea0a\scratchpad\r11-apps\`
- **Scripts:** `mkr10.py` (round 10 effect jsonl), `mksame.py` (same binary, two sides), `shares.py`, `sharestats.py`, `startshares.py`, `show.py`, `tree.ps1`, `bisect.ps1`, `run.ps1`, `aotpub.ps1`, `srv.py`.
- **Data:**
  - `r10effect.jsonl`, `r10effect.txt` (69 walks, round 9 release vs round 10 end);
  - `runs\bisect.jsonl` (b2fdcf52);
  - `runs\today.jsonl` and `runs\rerun.jsonl` (same-day re-runs of both hosts);
  - `same10.jsonl`, `sameday.jsonl`;
  - `runs\tabswitch.jsonl` and `runs\now-jit-r1-meridian11.out.txt` (the tab switch);
  - `runs\bigfiles.jsonl` (JIT held key).
- **Walk host:** `walk\src\` (round 10's host plus `R11.cs`: the meridian11 tab-switch walk and the view-size probe, and 1 dispatch line in Program.cs).
- **Trees and builds:** `tree-now` (dee48fbb with probe), `walk\now\WalkHost.csproj`, `walk\bin-now` (JIT), `tree-xb2fdcf52` with `walk\aot-xb2fdcf52`.
- **Copied hosts:** `walk\aot-r10after` (3462f18e) and `walk\aot-r11base` (ca31555d).
- **Fixtures:** read from `r11u\apps\fx` and `r11u\apps\www`.

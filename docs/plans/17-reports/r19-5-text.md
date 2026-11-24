# Round 19 survey, expert 5: Text pipeline

SATISFIED: no

**Setup.** I measured at 56d9a3f6 (detached, SDK 11.0.100). I used round 18 step 13's text probe (probe3-check.patch)
plus a new probe in TextArea `WrapLines` (TaProbe). For each searched hard line it hashes the line's rows and compares
them with that line's rows at its previous search. Runs were headless (JIT and AOT) plus one real-window hold. All
probes are now reverse-applied: the worktree is clean, the build servers are shut down and no host is running.

**Orchestrator's jrepo note.** My Junction fixture is a copy of round 18's `r18-text\fx\jrepo`, not the shared r14 one.
`git status` there is clean (0 changes, 2,726 tracked files), so no reset was needed. The full-GC heap is 9.9–10.7 MB,
and the Junction numbers stand.

REVIEW:

- **e546143d, 3191b065: no objection.** Vesper's zh reader drag after the return to 1,440 is 24.5 KB a step, with
  364,749 characters itemized (round 17's count) and 846 size breaks. The restore cannot help where the 4 MB
  width-keyed cache no longer holds the opening layouts (see EXAMINED).
  - **Nit (CS0419, `MeasurementCache.cs:393`):** `<see cref="TryGet"/>` should be `TryGet(TKey, out TValue)`.
  - **Second nit (CS1574 at :358, older: from 41884e7a):** in `InPlace`, the cref should be
    `LruCache{TKey, TValue}.TryGet(TKey, out TValue)`.
  - Built HultUI.Text with both edits: both warnings go; the 4 other CS0419s in HultUI.Text stay. The edits are undone;
    they are in cref-fix.patch.
- **eec26ffd: no objection.** Vesper ar drag is 20.2 KB a step: 85 BiDi layouts and 20,475 answers from a range, the
  build's numbers. he is 12.0 KB.
- **fd943d6f: no objection.** I read the code and did not recount the calls. `NumLabel` has a fixed 46/44 px width and
  FlexShrink(0). A number of up to 5 digits at 11 px mono fits the 38 px content width, so it takes the one-line answer.
  The build measured the measure calls equal.
- **bd3605ff (DrawTextLine, ScrollbarElement): no objection, and no app caller yet** (`git grep apps`: 0).
  - A line is laid out at +inf width with 1 line, answered by the width-independent shape cache (0 entries in the
    width-keyed cache).
  - Probe: a 1,440×900 canvas of 13 px mono lines (50 visible), the real documents, JIT.
  - Scroll 3 lines a step: zh 18.8 KB a step, ar 12.3, archive 43.5, emoji 8.6. Scrolling back over lines drawn before:
    2.1–2.6 KB for 40 steps. Redrawing the same screen: 0.34–0.39 KB a frame. A jump: zh 257 KB, ar 136, archive 75,
    emoji 59.
  - A TextArea on the same documents pays at open instead (zh 4,694 KB and 7,446 HarfBuzz calls; archive 11,602 KB),
    then 3.5–8.7 KB a notch.
  - Only cost to note: a line is shaped whole past the canvas edge (an archive line is up to 2,556 characters: 14.5 KB a
    new line). The API's own "keep a call under 4,096 characters" covers it.

CANDIDATES:

- id: text-textarea-wrap-ranges
  what: For each wrapped hard line, a TextArea keeps the widths its row breaks hold for, and a new width inside that
  range reuses the line's old rows instead of searching the line again. Today a resize step searches every hard line
  that does not fit whole.
  - The range comes from the two probes that decide each `FindLineBreak`: lo fits, lo+1 does not.
  - It is intersected over the line's rows.

  evidence: headless JIT, 1,440×900, a 40-step drag of 1 px. "Same" means the rows equal the rows that line had at its
  previous width. "After" removes the same-row lines' WrapLine bytes.

  | View, document | KB a step | Lines searched a step | Same rows | WrapLine KB of drag KB | After |
  |---|---|---|---|---|---|
  | Meridian, ARCHIVE 602k, at 960 | 140.9 | 2,928 | 96.6 % | 5,392 of 5,635 | ~49.9 (−65 %) |
  | Meridian, ar | 629.0 | 376 | 96.0 % | 24,888 of 25,161 | ~144.0 (−77 %) |
  | Meridian, emoji | 51.7 | 497 | 96.8 % | 1,785 of 2,068 | ~23.7 (−54 %) |
  | Meridian, big ja+zh | 30.3 | 792 | 93.8 % | 1,037 of 1,211 | ~21.9 (−28 %) |
  | Meridian, TODO.md | 39.0 | 663 | 96.3 % | 1,007 of 1,559 | ~29.8 (−23 %) |
  | Meridian, ja | 21.1 | 440 | 94.2 % | 672 of 845 | ~16.8 (−20 %) |
  | Junction Files, ledger 190k, at 1,440 | 37.9 | 89 | 92.4 % | 892 of 1,519 | ~16.7 (−56 %) |
  | Junction Files, ledger, at 960 | 26.4 | 679 | 94.8 % | 659 of 1,057 | ~15.4 (−42 %) |

  - **Itemizing:** the archive itemizes 298k characters and builds 2,582 prefix tables a step; big ja+zh builds 45k
    tables a step (1.80 M a drag).
  - **AOT headless:** identical to JIT.
  - **At 1,440 Meridian's response pane keeps its splitter width,** so the window drag there costs 1.9 KB a step.
  - **Why the range is exact under the search's own rule:** the search ends with lo ≥ hi, so it measured both lo and
    lo+1, and the walk back to a space depends only on End(lo). So the rows are a function of the width over
    [w(lo), w(lo+1)). That holds under the rule `FindLineBreak` documents: a longer prefix never measures narrower.
  - **Where that rule fails** (joining, kerning), a fresh search starting from another guess could stop elsewhere. So a
    line keeps no range unless its prefix table is monotonic near lo, and the build needs an equality run on the real
    fixtures (as in rounds 15 and 18).

  real app:
  - **Meridian's response viewer** is always wrapped: dragging the response splitter, or a window narrower than the room
    for the pane (≤ ~1,320 here), with a large non-ASCII body (markdown, docs, logs). Pretty JSON is printable ASCII and
    already breaks by count.
  - **Junction's Files view body** is a TextArea: window resize or the tree splitter.
  - **How often:** a few drags a session, 40–100 steps each.
  - **Real window:** Meridian archive 153.3 → ~55.9 KB a step (−64 %); Junction ledger 40.3 → ~29.6 (−27 %).

  expected: churn bar, −20…−77 % UI KB a step headless and −27…−64 % in a real window. Also the scaling bar: O(wrapping
  hard lines) per step becomes O(lines whose breaks move). The time win is likely large (~298k → ~20k characters
  itemized a step on the archive); the build phase times it.

  cost: medium. TextArea wrap only:
  - note the two deciding probes;
  - 2 floats per hard line beside `_lineWidths` (28 KB for the archive's 3,590 lines);
  - the previous width's rows to copy from;
  - an equality corpus against the existing reference paths (`ClustersByRow`/`FitsByCount = false`).

  No API; pixels unchanged where the rows are equal.

REAL WINDOW:

- **Hold and launches:** 1 hold of `Global\HultUI-r19-desktop`, after the idle gate passed (waited 182.2 s for the lock,
  held 65.9 s). 4 NativeAOT launches of the probe host on GL: h1 Junction Files on the ledger, h2 Meridian archive at
  960, h3 Vesper archive reader, h4 Meridian big at 960.
- **Guards:** 0 foreground takes, 0 refused activations, `WS_EX_NOACTIVATE` set 8× a launch, all 4 exited 0 on
  WM_CLOSE, 0 nvlddmkm events, no screen reads.
- **Desk:** only the 1440p was attached (DISPLAY1, 2,560×1,440 at 100 %, 200 Hz, HDR on, primary). Client 1,440×900 at
  DPI 96. Steam running; timer resolution not read, so no wake claims.
- **What it showed:**
  - **TextArea counts equal headless to the line:** archive 117,125 searched / 113,091 same; big 31,669 / 29,695;
    Junction 3,555 / 3,283 (headless 3,285). 1 frame a step.
  - **Bytes:** Meridian archive 153.3 KB a step (headless AOT 140.9). Junction is 40.3 KB a step, but its WrapLine bytes
    are only 498 KB (headless 892): the real window's Junction does ~1.1 MB of other work during the drag, not split
    further. So the candidate's real-window share there is −27 %, not −56 %.
  - **Vesper archive:** the return to the opening width costs 1,106 KB with 188 re-breaks even with no resize before
    it. The opening layouts had already left the 4 MB cache at open (202 / 4,090 KB at the end).

WAITING, RE-RANKED:

- **text-open-measures-every-line (full): keep Waiting (user: not now).** Vesper zh open is unchanged: 211,271
  characters itemized / 3,059 tables (r18: 211,248 / 3,059). Others: ja 190,188 / 1,292; big 401,276 / 4,364; archive
  1,000,493 / 362 and 9,885 KB. A TextArea's open is the same O(document) (Meridian archive: 3,025 lines searched, 580k
  characters, ~5.0 MB in WrapLine); the new candidate does not change it.
- **text-cjk-row-start-suffix: low, unchanged.** HarfBuzz calls a 40-step drag at 1,440 / 960: zh 1,577 / 3,006 (39 /
  75 a step), ja 1,441 / 2,992.
- **syntax-shared-registry-rest: parked.** No change under src/HultUI.Syntax since 49bd5d81.
- **textarea-chunked-document: parked.** No TextArea storage change; the range touched it only in 3442c3f5 (the menu)
  and bd3605ff (scrollbar paint). Not measured again. text-textarea-wrap-ranges is independent of storage.
- **suite-bidi-rows-fresh-session: what the rows should do.**
  - **The change:** an untimed 40-step sweep in setup at widths 2 px off the timed ones (`DragWidth(i, from: 1042)`).
    Each paragraph's memo and range, and the fill of the width-keyed cache's 2,048 entries, are then paid before
    timing. The timed widths 1,028…1,012 are never answered by that cache.
  - **Prototype:** JIT, 5 rounds × 5 steps, probe build; base = BiDi ranges off (`TEXT_NOBIDIRANGE`). allocKB, base →
    new:

    | Row | As now | One far layout | Sweep | 200 steps after the sweep |
    |---|---|---|---|---|
    | bidi wrap 150 | 129.7 → 159.6 (+23 %) | 143.3 → 114.6 | 40.2 → 11.6 (−71 %) | 40.1 → 11.8 |
    | rtl wrap 150 Hebrew | 173.1 → 189.3 (+9 %) | 152.7 → 129.1 | 33.8 → 8.5 (−75 %) | 38.5 → 11.6 |
    | text/wrap 150 (plain) | 17.0 = 17.0 | 15.6 | 13.6 | 14.7 |

    - **The sweep works:** 5 timed steps read the 200-step steady state (bidi within 0.2 KB).
    - **One far layout is not enough:** the cache still fills inside the timed steps.
    - **The plain row carries the same one-time fill:** warm it too.
    - **Without a warm, 200 steps:** 58.9 → 33.8 and 71.1 → 46.2.
  - **Then:** re-take the three baselines once. Cost small, suite only.
  - "create 2,000 unique emoji labels" +4 % is a real range entry per label, as the ledger says.

EXAMINED, NOT WORTH IT:

- **A return to a width laid out before, on documents bigger than the 4 MB layout cache:** e546143d's restore finds no
  layout there.
  - **Vesper archive:** the return costs 1,063 KB headless and 1,106 KB in a real window (188–196 re-breaks, 620k
    characters).
  - **Others:** ar returning to 960 costs 127 KB (234 BiDi layouts); big 20–22 KB.
  - **Why not:** it is one step a return, no dearer than any new width (archive resize to 960 is 2,126 KB), and the next
    drag is unaffected. A second remembered range per paragraph would answer it, but text-wrap-ranges-per-width was
    closed in round 18.
- **Widths only sized, never laid out:** Vesper zh returning to 960 after the 960 drag re-breaks 293 rows (79k
  characters, 20.6 KB). One step.
- **Vesper reader drag on the archive:** 54 KB a step at 1,440 (22 re-breaks a step), 44.5 at 960. These are long
  paragraphs whose breaks really move.
- **DrawTextLine on real text:** numbers in REVIEW; no app uses it, so nothing to build.

NEEDS THE USER: none.

FILES (all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\e91920b9-3793-496a-9a73-f017250d8772\scratchpad\r19-text\`):

- **Probes, reverse-applied:** probe3-check.patch, TextProbe.cs.r18b13, r19-probe-ta-suite.patch (TextArea probe hook
  and suite warm switch), TaProbe.cs.r19, cref-fix.patch.
- **Host:** host\Program.cs (adds the `canvas`/`tarea` LineScene and `TEXT_RW`), host\TextHost.csproj, host\jit,
  host\aot.
- **Scripts:** run.ps1, sumv.ps1, suite.ps1, batch19.ps1, aotpub.ps1.
- **Runs:** runs\v-* (Vesper); runs\m-* (Meridian); runs\j-led*.log (Junction); runs\canvas-*, runs\tarea-* (the canvas
  and TextArea line scenes); runs\suite-*.txt|json (suite rows); runs\h1–h4-*-real.txt (real window).
- **Logs:** holds.log, batch-r19a.log, gate.log, build-*.log.
- **Fixtures:** fx\docs, fx\jrepo (clean).

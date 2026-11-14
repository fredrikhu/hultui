SATISFIED: no

I measured at e6f7b5aa (detached, SDK 11.0.100). All numbers are counts and bytes; there are no timings. Headless runs used a JIT probe host; real windows used a NativeAOT publish of the same host, showing Junction's Files view on real text. The probe and all variant switches were reverse-applied afterwards: the worktree is clean and the build servers are shut down.

**Fixtures:** the round 13 ja/zh READMEs, plus new READMEs built from installed gettext translations (`mkreal14.py`):
- Korean, 76,823 characters;
- Thai, 41,708;
- Arabic, 76,025;
- Hebrew, 27,913;
- ACCESSIBILITY-PLAN.md with 208 emoji (one every 4th paragraph). No emoji-rich Markdown exists on disk.
- "big": ja + zh in one file, 154,276 characters.

REVIEW: one objection, to 698c2758 part (b), the natural-end words.
- **What (b) costs on Chinese.** Against (a) alone (same build, (b) switched off), (b) costs UI-thread bytes at every new width, not only the first. Shown as (b) off → shipped:

  | Phase | UI KB | HarfBuzz calls |
  |---|---|---|
  | open | 4,341 → 4,522 | |
  | 1200 | 508 → 755 (+49 %) | 1,056 → 1,119 |
  | 1000 | 471 → 808 (+72 %) | |
  | 1320 | 321 → 383 | |
  | 960 | 602 → 723 | |

- **Where (b) wins on Chinese:** only in drags: 1440 809 → 753 KB, 960 2,269 → 1,933 KB. Japanese wins at every width.
- **Cause:**
  - New natural words are made at every new width: 460 at 1200, 693 at 1000, 201 at 1320, 524 at 960; 220–934 earlier ones are found again.
  - They start after a space or a font-run boundary inside a row. Chinese gettext text has spaces around Latin words and `%s`, so each width's table end lands in a new segment.
  - At 1200 they shape 10,430 characters, against the 2,634 of segment they replace.
  - 58 % of those characters are in words that run past an ASCII↔CJK change to the next space.
- **Past the word cache** (the big file): (b) is neutral, within ±1 %, as DECISIONS.md says. But the cliff itself is the larger cost (candidate 3).

CANDIDATES:
- id: text-bidi-textarea-probes
  what: Two changes:
    - (a) `ComputeWrappedBidi` on one unbounded line with no newline builds the line directly. It skips the break-segment pass and the ICU line breaks, which only decide breaks an unbounded line never makes.
    - (b) `TextAreaElement.MeasureSubstring` measures through `MeasureTextWidth(span)` (the width cache) instead of `MeasureText(Substring)`.
  evidence:
    - **Why it is slow today:** where `MeasurePrefixes` refuses a line (BiDi: RTL text, emoji, Thai, Devanagari, any combining mark; TextPipeline.cs:2358), `FindLineBreak` measures every probe as a new substring through the full BiDi layout (TextAreaElement.cs:1188–1196, :1349–1354; SkiaRenderContext.cs:1826).
    - **Per probe, that layout runs:** ICU BiDi, ICU line breaks, a width for every break segment, then the line again. RTL runs are shaped without the word cache, and each probe is inserted into the 4 MB layout cache.
    - **Arabic, per drag frame at 1440:** 1,846 probes, 213k characters through ICU BiDi, 109k HarfBuzz calls and 20.8 MB on the UI thread.
    - **Headless UI KB, shipped → (a) → (a)+(b):**

      | Document, phase | UI KB | HarfBuzz calls |
      |---|---|---|
      | Arabic, open | 25,347 → 13,927 → 14,441 | 127,620 → 64,444 |
      | Arabic, new width (1200) | 26,224 → 14,382 | 136,397 → 67,467 |
      | Arabic, 40-frame drag at 1440 | 830,083 → 485,901 → 44,426 | 4,366,977 → 2,193,063 → 217,133 |
      | Arabic, drag at 960 | 1,050,031 → 616,136 → 80,729 | |
      | Hebrew, drag | 26,340 → 17,033 → 9,337 | |
      | Hebrew, drag at 960 | 85,387 → 55,372 → 19,400 | |
      | Thai, drag | 17,925 → 16,268 → 5,348 | |
      | Thai, drag at 960 | 45,749 → 41,544 → 9,081 | |
      | Emoji, drag at 960 | 6,831 → 5,992 → 3,434 | |

    - **For scale:** TODO.md's drag at 960 is 1,773 KB; at 1440 no emoji line wraps (813 KB). A Chinese drag is 753 KB and 590 calls; Korean takes the plain path (738 KB).
    - **Exact:** (a) builds the same single line, since the loop at +inf never breaks. (b) is `MeasureWidth`'s BiDi branch, the same `Layout(+inf, 1 line)` width.
  real app: Junction's Files view (and the Changes, Conflicts and Rebase TextAreas), Meridian's request and response bodies, Vesper's quick view and Aperture's report, whenever a line with RTL text, emoji, Thai or marks wraps. Translated READMEs (`README.ar.md`, `.he.md`, `.fa.md`) are common; English READMEs with emoji reach this path once a narrow window or split pane wraps them.
    - **Real window (AOT, 4K at 150 %):** Arabic drag at 1440 842.6 → 32.8 MB (−96 %), 4,440,392 → 160,594 calls; drag at 960 1,081.5 → 62.6 MB; back to 1440 after it 22.5 MB → 17.6 KB; opening 25.4 → 14.5 MB.
  expected: memory churn −43…−96 % per open, new width or drag frame on RTL/Thai/emoji documents, and scaling (repeated probes become hits). HarfBuzz calls fall 50–96 %, so time almost surely too; confirm in build.
  cost: small (~8 lines in ComputeWrappedBidi, 1 in TextAreaElement). No API, no pixel change.
    - **Proof:** an equivalence test of every probe width on the four READMEs, old against new.
    - **Check in build:** the GC heap at the end read 28.3 → 40.0 MB (`GetTotalMemory(false)`, which includes uncollected garbage; the width cache is bounded at 4 MB).
    - **Left after it:** a new width still shapes ~67k RTL runs (`BidiLine`, uncached). An RTL word cache would cut that, but it was declined in round 1 and changes how widths are defined.
- id: text-cjk-natural-end-trim
  what: Answers the review objection with two changes to 698c2758's (b):
    - (i) a table-end segment the word cache already has is its own answer: no natural word is looked up or made;
    - (ii) a natural word also ends at the first change between ASCII letters/digits and other characters after its start, when that is at or past the table's end (otherwise as now).
  evidence: headless UI KB (HarfBuzz calls), shipped → candidate, with (b) off for reference:

    | Doc | Phase | Shipped → candidate | (b) off |
    |---|---|---|---|
    | zh | open | 4,522 → 4,460 | 4,341 |
    | zh | 1200 | 755 → 640 (1,119 → 951) | 508 |
    | zh | 1000 | 808 → 555 (1,433 → 1,141) | 471 |
    | zh | 1320 | 383 → 318 | |
    | zh | drag | 753 → 653 (590 → 428) | |
    | zh | 960 | 723 → 532 | 602 |
    | zh | drag at 960 | 1,933 → 1,369 (3,617 → 2,564) | |
    | ja | 1200 | 659 → 574 | |
    | ja | 1000 | 779 → 596 | |
    | ja | 960 | 626 → 532 | |
    | ja | drag at 960 | 2,838 → 2,498 | |
    | ja | drag | 820 → 804 | |
    | big | all phases | within ±1 % | |

    No phase is worse. Exact: every read is the cached segment or RunCuts'.
  real app: Junction's Files view and the other TextAreas on CJK text, at every resize, maximize, snap or pane drag, and when a file opens.
    - **Real window (AOT, 4K at 150 %), zh:** 1200 796 → 657 KB, 1000 815 → 562, drag 520 → 460 (423 → 332 calls), 960 742 → 544, drag at 960 1,414 → 949 (2,467 → 1,672), open 4,561 → 4,499.
    - **Real window, ja:** 1200 700 → 615, 1000 786 → 602, drag at 960 1,868 → 1,685.
  expected: memory churn −12…−33 % at new widths and in drags, HarfBuzz calls −15…−32 %. The first-width cost over (a) alone drops from +247 to +132 KB.
  cost: small (~10 lines in TableEndSegment and NaturalEnd). No API, no pixel change.
- id: text-word-cache-cjk-cliff
  what: The word cache's bounds (8,192 entries, 4 MB): a CJK document whose words at one width pass them re-shapes the whole document every frame, because LRU with a cyclic access larger than the cache misses everything.
  evidence: the big file with both bounds raised (32,768 entries, 16 MB):
    - 40-frame drag at 1440: 157,557 → 1,085 KB, 397,120 → 1,087 calls (376,848 evictions → 0);
    - drag at 960: 213,283 → 5,833 KB;
    - a new width: 4.7–5.2 → 1.2–1.5 MB;
    - open: +921 KB.
    - **Each bound binds alone:** with 16 MB alone, the 8,192 entries bind (no change); with 32,768 entries alone, the 4 MB binds.
    - **The zh README alone passes the cache at 960:** 3,156 evictions in the drag; with the bounds raised the return to 960 goes 177 → 16 KB and the drag at 960 3,617 → 2,998 calls.
    - **Cache size at the end:** big 20,950 entries / 12.9 MB; zh 11,611 / 6.2 MB.
    - **Real window:** big drag 156.9 MB → 0.78 MB, 395,858 → 789 calls; drag at 960 211.9 → 4.6 MB. Word cache at the end 3.5 → 11.4 MB, yet process private 172.7 → 166.1 MB (less churn, lower peak).
  real app: Junction's Files view on a CJK document past ~100k characters, and the 75k Chinese README in a 1,440 px window at 150 %.
  expected: memory churn −74…−99 % for such documents (scaling).
  cost: small (two constants) but retains up to ~12 MB more after such a document → NEEDS THE USER. A scan-resistant admission (once full, keep only words seen twice) changes no memory, but I did not measure it.

REAL WINDOW:
- **Setup:** one hold (r14a): waited 199.1 s, held 173.0 s, 8 launches of the NativeAOT probe host.
  - Junction at 2,160×1,350 px (1,440×900 logical) on the 4K at 150 %, 120 Hz, primary.
  - The 1440p at 100 %, 120 Hz, attached to its left.
  - GL, RTX 4080, driver 610.88, HDR on.
- **Guards:** 0 foreground takes, 0 refused activations, WM_CLOSE exit 0 on all 8, 0 nvlddmkm events, no screen reads.
- **Same as headless:** counts within a few percent; every drag step drew one frame (40/40) in every launch.
- **What the real window adds:**
  - Physical-pixel drag steps (0.67 logical px) shape fewer words at 1440 than headless (zh 423 against 590 calls).
  - After the Arabic README, the layout cache was full of probe layouts (1,518 entries, 4,093 of 4,096 KB) and the memo cache full (3,056 entries, 10.2 MB): every other cached layout had been pushed out.
  - The Arabic launch ran 34 s against 19–21 s for the others.

WAITING, RE-RANKED:
- **text-cjk-row-start-suffix:** still a lead, below candidates 1–3.
  - Its share after candidate 2: row-start segments are 88 of 951 calls (zh) and 118 of 681 (ja) at 1200. In a drag at 960 they are 52–65 % of what is left, but only 34–55 calls a frame.
  - The largest share at a new width is now short words (under 8 characters) cut at need: 461 of 951 (zh, 1200).
  - Giving 2–7 character words their cuts in the tables halves a new width's calls (zh 951 → 481, 1,141 → 690; ja 681 → 506) and puts zh 1200 below (a) alone (616 → 471 KB). It costs opening +248 KB (zh) / +121 KB (ja), so it is a trade.
- **text-open-measures-every-line (full):** worth asking again; it is the root of all three candidates.
  - **Per drag frame with ~40 rows on screen:** zh at 1440 1,140 prefix tables and 131k characters itemized; at 960 2,734 tables and 169k; ja at 960 2,554 / 166k.
  - **The big file:** 2,178–5,288 tables and 9,900–13,550 HarfBuzz calls a frame.
  - **Arabic:** 1,846 BiDi probes a frame.
- **textarea-chunked-document:** keep parked. Nothing changed in TextArea storage; the 256k-character condition is still unmet.
- **syntax-shared-registry-rest:** keep parked. No syntax code changed, and the user has no Typst files.

EXAMINED, NOT WORTH IT:
- **Making natural words at a start's second sight:** new widths −25…−30 % KB on zh, but drags +14…+33 % (2,578 against 1,933 KB at 960).
- **Ending natural words at the ASCII change, scanned from the table's end:** not anchored to the start, so one start makes several words; replaced by (ii).
- **Capping natural-word length:** moot; they average 22.7 characters.
- **Korean:** plain path; drags 738–744 KB, below Chinese.

NEEDS THE USER:
- **text-word-cache-cjk-cliff:** raise the word cache to 32,768 entries / 16 MB? Gain: −99 % churn and HarfBuzz calls in drags of CJK documents past ~100k characters (or zh at 960). Cost: the cache keeps up to ~12 MB more while and after such a document is shown; Latin text fills about 2 MB. In a real window the process still ended 6.6 MB smaller.
- **text-open-measures-every-line (full):** re-ask with the numbers above ("not now", 2026-10-13).

FILES: in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\41943a9b-f657-4483-971a-a2b346b967f7\scratchpad\r14-text\
- `r14-probe-and-variants.patch`: the counters plus switches. `R14_VAR=12` is candidate 2; `R14_VAR=16` with `R14_TAW=1` is candidate 1; `R14_WCAP=32768;R14_WMB=16` is candidate 3; `R14_VAR=76` is the short-word cuts; `R14_NAT=0` turns (b) off.
- `host\` (Program.cs, TextHost.csproj; builds jit–jit5 and the aot2 publish).
- Scripts: `run.ps1` (headless), `batch.ps1` (guarded, desktop lock), `cmp.py`, `mkreal14.py`, `findemoji.py`.
- `fx\real{ko,th,ar,he,emoji,big}` (git repositories).
- `runs\`, `holds.log`, `batch-r14a.log`.

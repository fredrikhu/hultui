SATISFIED: no

I measured at dee48fbb (detached, SDK 11.0.100, Windows), headless only, with no window opened. The worktree is clean at the end: the prototype is reverted and saved as patches.

**How the AOT profiles were taken.** This runs without admin rights. VS 18's `VSDiagnostics.exe` (CpuUsageHigh config) launched step-ab's AOT publishes, the collector service sampled them, and a small TraceEvent tool read the samples using the publish's PDB. `dumpbin` gave the per-instruction view.

**Timings are indicative only.** Other surveys were running and there was no idle gate. Each figure is the min of 3 alternated runs.

REVIEW: none.
- **Corpora are unchanged on .NET 11 (JIT).**
  - 48,000 layouts hash 7A18AF8DE8E05271 (1,004,690 lines, 22,578,320 glyphs).
  - Both 240,000-layout corpora hash F09E9038726E4BD6.
  - 0 layouts differ from round 10's files.
- **No .NET 11 JIT-only allocation in the text path.** Probe `types` mode, 24 steps: all 70 text rows and 22 TextArea rows are at or below round 10. Run again with `DOTNET_TieredCompilation=0` (optimized code from the first call, where c99e23bf's boxing showed), none rose either.
- **Range commits in my area are all sound:** c542faac, 7b7a486c, a86b208f, a0ef7841, c99e23bf.
  - Typing on .NET 11, KB a key: 89k body with an emoji 192.4, 2,000 lines 206.5, wrapped 254.1, with a reader 309.2.

CANDIDATES:
- id: text-word-cache-own-table (this resolves the Waiting item dotnet11-aot-textarea-long-document)
  what: The word shape cache gets its own chained table. Lookups by span call the comparer code directly, and the LRU is an index-linked list inside the entries. This replaces `Dictionary` + `AlternateLookup` + `LinkedList`. The rules stay LruCache's: 8,192 entries, the 4 MB weight bound, at most half the bound per entry, least recently used out first, Put replaces.
  evidence:
  - **Cause.** .NET 11's NativeAOT no longer devirtualizes the `IAlternateEqualityComparer` calls inside `Dictionary.AlternateLookup`.
    - New frames on 11 only: `RhpInterfaceDispatch`, plus `WordShapeKeyComparer.GetHashCode`/`Equals` out of line. On .NET 10 both were inlined.
    - The width cache's `AlternateLookup<WidthKey>` dispatches the same way.
  - **Profile, "open a long document", share of the line walk** (`GetLineStarts` inclusive, about 36.7k samples a run), .NET 10 → 11:
    - lookup self 15.69 → 19.14 %
    - comparer Equals 0 → 3.14, GetHashCode 0 → 0.99, interface dispatch 0 → 0.95
    - Marvin 6.59 → 9.29
    - the lookup's inclusive share of the process 15.7 → 24.9 %
  - **Profile, resize row:** lookup self 17.09 → 21.35 + 3.12 + 1.15 + 1.09; Marvin 3.73 → 6.51.
  - **Marvin is not the cause.** `dumpbin` shows the same instruction sequence on 10 and 11. Its samples under the word lookup go 1,188 (10) → 2,205 (11) → 1,123 (prototype); its other callers do not move.
  - **Call counts a step** (JIT probe counters):
    - open: 38,036 word lookups (120,246 chars, 0 misses, 1.00 Equals each), 545 prefix tables (108,135 chars), 1,776 prefix widths, 595 width lookups
    - resize: 38,588 lookups, 661 tables (116,720 chars), 2,119 prefix widths
  - **AOT micro-probe**, the same key and comparer shapes, 36,578 lookups, min of 400 trials, ns per lookup, .NET 10 → 11:

    | Shape | .NET 10 | .NET 11 |
    |---|---|---|
    | Today's lookup | 15.3 | 26.6 (+74 %) |
    | With LruCache's move-to-front | 30.9 | 37.9 |
    | Own table | 14.3 | 14.3 |
    | Hash alone | 4.8 | 4.9 |

    - Both runtimes make 1.00 GetHashCode and 1.00 Equals calls a lookup.
    - 12.2 ns × 38,036 = 0.46 ms, which matches the row's +0.52 ms (3.466 → 3.984).
  - **Prototype** (patch `proto-words-only.patch`), indicative, min ms base → words only:
    - open 4.011 → 2.912 (−27 %); resize 3.272 → 2.527 (−23 %)
    - richtext/open 5,000 −17 %, wrap 150 resize −17 %, ellipsis 1,000 −13 %, multi-font ellipsis −10 %, create 2,000 labels −10 %, styled ellipsis −6 %
    - 42 text/richtext rows: none slower beyond +2.1 % (noise).
  - **Prototype checks:** the 48k corpus hash is unchanged; HultUI.Text.Tests 294/294 pass.
  - **Allocation moves a little:** create 2,000 labels 3,079 → 3,025 KB/op, page down JSON 41.5 → 38.4 (no node objects while the cache fills).
  expected:
  - **Time bar:** both regressed AOT rows end below .NET 10, and most label and wrap rows gain 6–17 %.
  - **Retained memory:** about 124 → 52 B of bookkeeping per word, so −0.59 MB per pipeline at 8,192 words and 8,192 fewer objects. This is layout arithmetic, not measured.
  - JIT should gain less (PGO devirtualizes); the build phase measures it.
  cost: medium-small (~130 lines in MeasurementCache.cs, 5 small edits in TextPipeline.cs), no API, no pixels. Hits are the same by construction.
  - Tests needed: eviction order, weight bound, re-put, span equals string, growth and rehash, free-slot reuse.
  - Prove with the 48k/240k corpora.
  - Optional: the width cache the same way, but it is only 595 lookups a step (~7 µs).
- id: text-itemize-extends-below-marks
  what: `FontItemizer.Extends` (FontItemizer.cs:165) asks `CharUnicodeInfo.GetUnicodeCategory` for every itemized character after the first. A character below U+0300 can never extend the one before it, so a `< 0x300 => false` arm, or a check before the call at :85, skips the lookup.
  evidence:
  - It runs once per itemized char: more than 110k a step on these rows.
  - `GetUnicodeCategoryNoBoundsChecks` + `GetUnicodeCategory` + Extends self are 13.9 % of the line walk on 11 (7.7 % of the process); all callers are Extends.
  - 0 codepoints below U+0300 are Mn/Mc/Me, and the joiners, selectors, tags and skin tones are all ≥ U+200C, so the result is identical.
  - Indicative, on top of the word-cache prototype: open −7.1 %, resize −4.9 %, wrap 150 −4.5 %, richtext/open −4.8 %, labels ±0.5 %.
  expected: Time bar on the long-document row (−7 %); borderline elsewhere. Every layout itemizes, so it helps everywhere a little.
  cost: small (one line plus a test over U+0000–U+02FF), no API.
- id: text-itemize-primary-glyph-per-char (lead, not prototyped)
  what: Itemize calls `primaryFont.HasGlyph` once per character, an interface call each time.
  evidence: with both fixes above, `SkiaResolvedFont.HasGlyph` is 8.5 % of the line walk (5.5 % of the process), 99 % of it from Itemize.
  expected: perhaps −5 % on the TextArea rows; count and measure first. One option: a per-call ASCII mask of characters already confirmed in the primary face.
  cost: small-medium, no API.

WAITING, RE-RANKED:
- **dotnet11-aot-textarea-long-document:** cause found. It is the word cache's alternate lookup (above), not the line walk's own code. JIT is not affected.
  - The SDK-only build (SDK 11 targeting net10) has a separate artefact: `FontItemizer.Extends` self 3.2 → 6.0–6.8 %. It is gone on net11, so it is not shipped.
  - Close this item into text-word-cache-own-table.
- **textarea-chunked-document:** keep parked. Typing rows are unchanged on 11 (206.5 / 254.1 / 204.4 / 309.2; 30-word rows 753.0 / 825.6 KB a key), and no typed text reaches the 256k-character condition.
- **syntax-shared-registry-rest:** first file in its own process, full GC, KB kept (allocated):

  | Extension | Grammars | Kept | Allocated |
  |---|---|---|---|
  | .typ / .typc | 57 | +42,419 | 117,238 |
  | .jl | 9 | +9,183 | |
  | .php | 8 | +4,449 | |
  | .cshtml | 6 | +4,567 | |
  | .html | 4 | +3,078 | |

  - HTML with or without script/style keeps about the same (3,255 / 3,122). For comparison: .md +1,311 (after step 12), .js +1,608, .css +521.
  - **Typst is now the largest single retention**: ×1.4 what Markdown kept before step 12. It is worth step 12's fence-scan route (medium) if Junction Files or Vesper meet .typ files.
  - **The rest stay parked.** Julia and PHP are rare, and HTML saves ~2 MB only for markup-only pages, which are rare.
- **syntax-merged-theme (queued; scope only):**
  - **Code:** SyntaxHighlighter.cs only.
    - A merged raw theme: the include chain flattened, included rules before main rules, GUI colours main over included, `GetInclude` → null.
    - An options wrapper used in `EnsureRegistry` (:356). `RootedOptions`/`RootedTheme` (:541–617) wrap the merged theme; `ParentsNamed` walks one theme.
    - About 50–80 lines.
  - **Themes affected:** only Dark+ and Light+ (the two bundled themes with an include). The other 19 should change 0 lines.
  - **Tests:** ThemeColourTests +3: Dark+ JSON key #9CDCFE, `this` #569CD6, and a Light+ counterpart. Its existing VS Code assertions should hold, and the rooted PowerShell rule must still land last.
  - **Goldens: none change.** None of the 14 Headless goldens use HultUI.Syntax, and no other test project references it except HultUI.Syntax.Tests. The hard-coded #4EC9B0/#9CDCFE spans in FrameAllocationTests, TextAreaHighlightTests and SpannedLineSubBlobTests are input data. Meridian's ResponseBodyWorkerTests compare against the highlighter's own output.
  - **Corpus check:** round 9's VS Code emulation (`r9b14b\probe`, 1.76 M lines × 21 themes). The ~974k differing lines should fall to the 54 PowerShell lines and the other explained ones.
  - **Images:** step 14b's headless frame harness. Expected per-key cost 0; the theme load builds one trie instead of two.
- **tokenize-diff-public (queued; scope only):**
  - **Code:** in SyntaxHighlighter.cs, make `TokenizeDiff` (:205), `LineRange` (:14) and `TokenizedDiff` (:18) public.
  - **Surface choices:**
    - Make `clock` optional (`TimeProvider? clock = null`, as `TokenizeLines` has it).
    - Document `LineRange` as 1-based with both ends included, and validate rising, non-overlapping ranges with First ≥ 1: the code reads `shown[^1].Last`.
    - Document that each array is null for a side with no text or nothing shown and holds spans only for shown lines.
    - Keep `LinesWalked`: Junction returns it.
  - **Junction:** it uses only TokenizeDiff and LineRange from the internals (JunctionApp.Highlight.cs:38, :290), so drop `<InternalsVisibleTo Include="Junction" />`. `LineRange` collides with nothing (GitLab's type is `GlLineRange`).
  - **Tests:** a reflection test in TokenizeDiffTests that the three are public, plus validation tests.
  - **Docs:** no Syntax guide exists; a few lines in docs/THEMES.md or a new short section. docfx `--warningsAsErrors` must stay at 0 (crefs now point at public members).
  - Small; no perf number.

EXAMINED, NOT WORTH IT:
- **A range-key struct with the default comparer** (no alternate lookup) instead of a table of its own: 25 ns a lookup on both runtimes, slower than today's .NET 10 shape.
- **A cheaper non-randomized word hash:** Marvin is 9 % of the walk, but a crafted document could chain a bounded cache.
- **The width cache's dispatch on 11:** 595 lookups a step, ~7 µs, below every bar.

NEEDS THE USER:
- **Typst (.typ):** do the apps meet Typst files? The first one keeps +42 MB until `SetTheme`. The exact fix (load only fenced languages) has no colour change. This is a scope question, not an API or pixel one.

FILES: all in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\ec6d577c-f9ba-4479-acbd-908e67c0ea0a\scratchpad\r11-text\
- **Profiling:**
  - `prof.ps1` runs one profile (VSDiagnostics, needs session ids 0–255, waits on its own PID).
  - `ana\` is the analyzer (modes prof / callers / callees / addrs) and `ana.ps1` runs it.
  - `cmpprof.py` compares shares; `disasm.py` annotates instructions.
  - `profs\*.txt` (open/resize × s10n10, s11n10, s11n11, proto) and `profs\marvin-*.asm`.
  - The collector service VSStandardCollectorService150 (manual start) was left running.
- **Micro-probe:** `alt\Program.cs` with `alt10\` and `alt11\` (AOT) and `aotpub.ps1`.
- **Prototype:** `proto.patch` (word cache + Extends), `proto-words-only.patch`, `aotsuite\` and `aotsuite2\` publishes, `ab.ps1`, `ab3.ps1`, `ab-textarea.txt`, `ab-text.txt`, `ab3.txt`.
- **Corpora:** `dumps.ps1`, `dumps.txt`, `h48.txt`, `h240late.txt`, `h240ref.txt`, `h48-proto.txt`.
- **Rows:** `rows.ps1`, `tip-text.txt`, `tip-textarea.txt`, `tc0-text.txt`, `tc0-textarea.txt`, `cmprows.py`.
- **Syntax:** `tok\` (the tok probe on net11), `grammars.ps1`, `grammars.txt`, `page.html`, `plain.html`.
- **Text probe:** `probe\` (round 10's, on net11).

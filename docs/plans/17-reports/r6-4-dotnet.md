# Round 6 survey: 4. .NET runtime

I measured at ef4288ad. Its `src` and `apps` are the same as f31194ab, so the total comparison's captures apply to this code. I copied the headless walk host from the total comparison (`total/partB/walk`) and pointed it at this worktree. Then I added:
- per-key counters: frames rendered, blobs made, glyphs drawn, Skia font-cache growth;
- UI-thread allocations by type, from `GCAllocationTick` samples (one per ~100 KB). Walks ran 3–40 times longer to get 100–1,300 samples each, JIT only;
- elements made per key against elements ever measured or laid out (temporary counters in `Element`/`LayoutEngine`, removed again with `git apply -R`);
- live heap after full collections over open/close cycles, with the recording bridge on, plus heap dumps (`dotnet-dump`, `dumpheap -stat`);
- a field census of `BoxElement`/`TextElement`;
- the round 5 TextArea typing probe, now with `apps/AppRuntime.props`.

I also re-read the six `5b8ac7d-total-after` suite captures. NativeAOT was used for the Meridian History trace. Only counts and bytes are reported. The worktree is clean.

Probes are in `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\855f4ddd-feda-4f0a-a595-a83e07af08f8\scratchpad\r6-dotnet\`:
- `walk\src\Program.cs`: the host. Builds are in `walk\bin-w6` (JIT) and `walk\aot-w6` (AOT).
- Scripts: `walkpub.ps1`, `walkrun.ps1`, `allocbatch.ps1`, `batch2.ps1`, `batch3.ps1`, `dumpcycles.ps1`, and `perkey.py`, `suiterows.py`, `heapdiff.py`, `scan.py`.
- Other probes: `sizes\` (field census), `gcprobe\` (TextArea typing), `probe-elements.patch` (the temporary counters).
- Data: `runs\` and `heapstat-*.txt`.

SATISFIED: no

## Review

No objections. What I checked in 643eb07b..f31194ab:
- **76b6f78e, 574f43a7:** a patch clears the tail of the kept handler array, so no stale app delegate stays alive. Arrays never shrink, which costs at most 24 B per handler an element ever held. The `OnPassive(handler, listening)` overload makes a wrapper and a delegate per call. That is documented, and no hot path uses it.
- **a52de219:** the per-thread scratch list is cleared after use, and a mutation test covers it.
- **bd88e747:** `BoundingRect` now walks up the parents, so each read is O(depth). UIA's `HitTest` reads it for every child of every node that contains the point. App trees are shallow, though:
  - Meridian: 119 nodes, depth 3, 23 reads and 27 parent steps per hit test.
  - Vesper: 205 nodes, depth 2, 83 reads and 116 steps.
  - Not worth a change.
- **69a9fac2:** AT-SPI objects made on request stay until the list's item count shrinks or the list goes. That is bounded by the item count: a client that walks 100,000 items keeps 100,000 objects. Noted only.
- **91d48601:** every reader of the lazy glyphs is on the UI thread. `TextMeasure` is documented as UI-thread, and Junction and Vesper call it from builds only.
- **e5ff3fea:** each variable face has a 2 KB page table plus 2 KB per 256-glyph page that is used. Bounded.
- **2a332218, 0d3bc10a, 4175f435, 08c49d10, 6895ac2d:** no per-frame allocation added. They use kept lists and struct enumerators; the caret-x arrays belong to the accessibility snapshot.
- **828a7d69:** the large-object threshold is active. A 174 KB document string stays in gen 0: no gen-2 collection caused by large-object allocation in 200 keys.
- **Suite (151 rows):** no row allocates more than 1 KB and 5 % more on AOT than on JIT.

## Candidates

- **id: runtime-element-layout-state** (owner 4; expert 1 reviews)
  - **what:** move `Element`'s layout output and measure cache out of the element into an object made when the element is first laid out:
    - `_layout` (72 B), `MeasureCache` (120 B), `CacheConstraintW/H`, `CacheForcedW/H`, `CacheResultSize` and `FlexPlan` (36 B);
    - 228 B in all, used at 18 sites in 2 files, plus 7 writes of `Layout`.
    - `Layout` stays a property; a never-laid-out element reads `default`, as today.
  - **evidence:**
    - **Element sizes:** `Box.New()` is 584 B and `Text.New()` 656 B. Element has 56 fields (510 B).
    - **Most elements a key builds are throwaway copies.** In a rebuild, the new tree's copy of a kept element is only copied from (`PatchFrom`) and then dropped. It is never styled, measured or laid out. Per key (JIT; AOT allocates the same):

      | Walk | KB/key | Made | Laid out | Copies |
      |---|---|---|---|---|
      | Meridian History (fixture walk) | 53 | 54.7 | 6.7 | 48 |
      | Meridian History, selection on screen | 63 | 64 | 16 | 48 |
      | Meridian URL typing | 77 | 54 | 0 | 54 |
      | Junction summary typing | 77.5 | 61 | 0 | 61 |
      | Vesper Brief | 34 | 18 | 0 | 18 |
      | Aperture grid Right | 114 | 111 | 47 | 64 |
      | Aperture grid Down | 181 | 179 | 113 | 66 |
      | Vesper folder in/out | 330 | 192 | 105 | 87 |
      | Vesper Detail | 45 | 20.6 | 11.6 | 9 |
      | Junction History | 54 | 42.4 | 36.4 | 6 |
      | Vesper Insert | 303 | 162 | 150 | 12 |

    - **Elements dominate what is left.** `BoxElement` plus `TextElement` are 35–60 % of a key's bytes in these walks (Meridian History 57 %, Aperture 48 %, Junction History 47 %, Meridian URL 35 %). With their style objects and child arrays they are 55–75 %.
  - **expected:**
    - Each throwaway copy shrinks by about 220 B (Box 584 → ~364 B, −38 %). An element that is laid out costs about 28 B more (an 8 B pointer plus the separate object).
    - Per key: Meridian History −20 % (on-screen selection −16 %), Junction summary −17 %, Meridian URL −15 %, Vesper Brief −12 %, Aperture Right −11 %, Down −6 %, Vesper folder in/out −5 %, Vesper Detail −4 %, Junction History −0.5 %, Vesper Insert +0.5 %. That clears the churn bar on 6 walks.
    - **Trade-off:** retained memory is about +28 B per live element: Vesper's 481-element window +13 KB, 10,000 live elements +280 KB.
    - **Phase 2 (−100…−150 B more per copy):** `ResolvedInherited` (32 B), `OverlayOrigin`, the focus, accessibility-observer and overlay-host references, and `TextElement`'s `_paragraph`, `_contentBox` and selection state (~48 B).
  - **cost:** medium, internal only. No API or pixel change. The build phase must time the extra pointer read on `Layout` (hot in render and hit testing; AOT placement noise) and run the layout audit.

- **id: meridian-body-edit-bytes** (re-rank up; owner 8, 4 reviews)
  - **what:** typing in a request body re-splits and re-parses the whole document on every keystroke.
  - **evidence:**
    - Headless JIT, 89,000-character body ("Notes Big"), 240 keys: **1,812 KB/key**. Of that, `String` is 77 % (1,399 KB, about 8 copies of the 178 KB document), `char[]` 17 % and `StringBuilder` 6 %. There are 25 gen-0 and 8 gen-1 collections. No elements are laid out (49 copies a key).
    - The TextArea alone costs 186 KB/key at that size (gcprobe).
    - The cause: `ReqDocument.SetBody` runs `ReplaceLineEndings` and `Split('\n')` into one `SourceLine` per line, then `Touch()` → `Reparse()` walks every line and joins the body again (ReqDocument.cs:229–255, :318).
  - **expected:** about −85…−90 % per keystroke (1.8 MB → ~0.2–0.3 MB) and about 3 gen-0 collections per 240 keys. It also makes most of textarea-chunked-document's case moot below 256k characters.
  - **cost:** small–medium, in Meridian.Core: a fast path when only the body's content changed (keep the content, splice the lines without a full re-parse). No API or pixel change.

- **id: meridian-history-selection-offscreen** (owner 8; a bug, not perf; found by the p90 trace)
  - **what:** History's Down and Up move the cursor (`HandleHistoryKey`), but nothing scrolls the list.
  - **evidence:** after the walk, `selected=49 scrollY=0 viewportH=800 contentH=8400` (28 px rows, about 28 rows visible). Past row ~28 the selection is off screen and no row is remade (0 laid out per key). `git log -S IntoView` finds no scroll-into-view call in that file, ever.
  - **expected:** a correctness fix. Afterwards each Down past the screen scrolls, and the bytes per key rise to the on-screen figure (63 KB, not 46). Walk numbers for Meridian History after rounds 4–5 are partly from an off-screen selection.
  - **cost:** small: scroll into view by `index × RowRun`, as Junction does.

## Examined, not worth it

- **meridian-history-aot-p90:** close it; this is the trace it asked for. The walk's first ~22 keys are a different workload, not a runtime effect:
  - That is the only stretch where the selection is on screen. Each key remakes two rows: 64 made and 16 laid out per key, 63 KB. Later keys run with the selection off screen: 48 made, 0 laid out, 46 KB. Two regimes of 2,000 keys each confirm the split, so the "same bytes" in the total comparison was wrong by +16.6 KB/key.
  - Otherwise those keys count the same as the rest: 1 frame, 3 blobs made, 2,439 glyphs drawn, no font-cache growth, 0 collections, 5 finalizers pending at the walk's start.
  - Their extra time is all in render (+1.4 ms on 5 of the first 15 keys in a quiet AOT run). It moved from run to run under load, and a 3 s wait before measuring did not remove it.
- **Reel playback, headless 0.6–0.7 KB per frame (9,600 frames):** the samples are `Action`, closures, `WeakReference`, `Object`, `SKPixmap` and `PositionChanged`. No `src` or `apps` code makes a `WeakReference`, so this is the fake backend and the virtual clock. Round 5's real AOT window read 90–111 B per frame.
- **Retained heap over cycles (bridge on):**
  - Meridian palette: +83 KB after 25 cycles, then +7 KB over 375 more.
  - Vesper mkdir prompt: flat. Vesper viewer: +571 KB in the first 25 cycles, then +9 KB to 200. Vesper folder in/out: +184 KB over 200 cycles, slowing down (+28 KB from cycle 100 to 200).
  - Meridian History view in and out: flat without Down. With Down it grows about 3.5 KB per entry first viewed, and stays flat after the 300th entry (cycles 300, 400, 600 and 800 all read 2,511 KB).
  - The dump diff puts all of that growth in the text pipeline's byte-bounded caches (`GlyphPosition[]` +318 KB, `ShapedRun` +81 KB, word, shape and width LRU nodes and maps about +320 KB, layouts) and in the blob map. No leak.
- **Idle wakes:** not re-measured. The `src` matches the total comparison, whose real windows showed 0 UI-thread wakes per second in all five apps.
- **Strings in Vesper Insert:** 138 KB/key (45 % of 303 KB), with 150 elements laid out per key. This is for vesper-marks-in-row-shape. `MarkKey` is a stored `FullPath`, so the strings come from remaking the rows.
- **Junction History:** 5.2 MB/key on all threads and 91/40/5 collections per 300 keys. This is apps-junction-highlight-prefetch, unchanged.
- **textarea-chunked-document (5/4):** keep it low. The TextArea alone, 200 keys, about 20 MB of other live heap:

  | Document string | KB/key | Gen-2 per 200 keys |
  |---|---|---|
  | 174 KB | 186 | 1 (alloc-small) |
  | 518 KB | 531 | 6 (background, large-object) |
  | 1.7 MB | 1,748 | 24 (background, large-object) |

  It matters only above about 256k characters. meridian-body-edit-bytes is the larger lever at 89k.
- **live-frame-requests-public (3/4):** close it. There is still no consumer, and 2a332218 already draws a source that only posts at SDL's timer rate.

## Needs the user

- none

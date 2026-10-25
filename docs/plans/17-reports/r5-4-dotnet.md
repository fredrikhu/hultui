# Round 5 survey: 4. .NET runtime

At 969f3308. Measured with JIT and NativeAOT. The suite ran in full both ways (140 rows). Real windows used the default frame and never took focus: the in-process hint, a CBT hook with `WS_EX_NOACTIVATE`, a 5 ms guard and WM_CLOSE at the end. The guard never fired and no activation was refused. Other measurements came from headless probes. Only counts, bytes and GC numbers are reported here, no timings. The worktree is clean (one temporary `PublishAot` edit to the perf suite's csproj was restored). Probe code is in the session scratchpad `r5rt/`:
- `host/`: R5Host. Modes for idle, `PROBE_WALK=1` (a Down key every 150 ms through the window's input path) and a `PROBE gc` line (heap, GC commit, gen-0 budget, private bytes, working set).
- Scripts: `run.ps1`, `build.ps1`, `aotpub.ps1`, `cmp5.py`, `jitaot.py`, `perkey.py`.
- `blobprobe/`: census of the run ink-extent map; `PROBE_FLUSHTEXT=1` empties the text caches.
- `vesperprobe/`: Vesper's kept cells and view-mode cycles.
- `gcprobe/`: GCs per keystroke in a long TextArea, counted by an EventListener.
- Data: `jit-1.json`, `aot-1.json`, `runs/`.

SATISFIED: no

## Review

- **ee6d0bb0 (owner 3, minor; an extension point, not perf): posted work no longer draws in any Windows size/move loop.** `OnSizeMoveStarted` sets the poster for every window (SDL3Window.cs:746). From then on, `posted = !_mainThreadQueue.IsEmpty && !LiveFrameRequests.Answering` is false (Application.cs:371).
  - **Who is affected:** a GPU source outside HultUI.Media and WebView. `IGpuImageSource`, `GpuFrameSink` and `IGpuFrameRenderer` are public, but `LiveFrameRequests` is internal. Such a source signals through a post, which the modal loop never runs.
  - **What changed:** before, SDL's live-resize timer drew it for that post about 64 times a second. Now it freezes until the move or held drag ends.
  - **Fix:** suppress the posted-work draw only while requests have arrived recently (for example in the last 100 ms), or make the request public. Small.
  - **Thread safety holds:** a message posted just after the loop ends is ignored (`s_liveFrameWindow` is 0). A stale waiting flag costs at most one extra frame.
- **No other objections:**
  - **671cf74, 3200a2a2:** a wake can no longer be lost. `NeedsRender` checks a11y actions and attach after `PumpEvents`. `IsRunning` (LINQ `Any` over a `List`) now runs twice per idle pass and allocates 0 B: Reel on AOT, 0.0–1.8 B per non-render pass, amortized.
  - **418f25a6:** the rented buffer is returned cleared, and a nested notify rents its own.
  - **d7c0c046:** using a view after the next text call throws (tested). The document is released at the next call. Scratch segments are trimmed above 4,096.
  - **a476866c:** the 219 KB is transient; the cache holds at most 64 face/size pairs.
  - **dddaac36:** the UI thread is the only writer, and `Value`/`Range` are replaced as one object.
  - **5e6ef9a2:** `AccessibleText` copies the line starts into its own `int[]`, so a UIA thread never reads the TextArea's list that is patched in place.
  - **fbec1130, a3aeabb2:** no new allocation paths.
  - **f9980647:** the kept SKImage is released through `ReleaseFrameTarget`, called from `GpuFrameSink.Dispose`.
  - **445b34c8:** the CBT hook uses an `UnmanagedCallersOnly` function pointer with a nested ref-struct scope.
  - **b52ca178:** one retention gap, see candidate 2.
  - **e6a432fa:** see Examined.

## Candidates

- **id: runtime-gc-gen0-budget-and-loh-threshold** (owner 4; each part its own commit)
  - **what:** set two GC knobs in the five NativeAOT apps: (a) cap the gen-0 budget at 6 MB, (b) raise the LOH threshold to about 512 KB.
  - **evidence, (a) gen-0 budget:**
    - The default gen-0 budget here is 17,301,504 B; it follows L3 size (33 MB on the i7-14700K). One interaction fills gen 0 to that size before the first GC, and the GC keeps it committed.
    - Junction History walk, AOT, a11y off, 99 keys:

      | Setting | Private | Working set | GC committed | GCs in the walk (gen0/gen1) |
      |---|---|---|---|---|
      | Default | 127.9, 130.2 MB | 116.7, 119.7 MB | 18.68 MB | 1/0 |
      | `GCgen0size`=4 MB (budget 6,291,456) | 115.2, 114.8 MB | 105.2, 105.1 MB | 7.19 MB | 5–6/1 |
      | `GCgen0MaxBudget`=6 MB | 116.1 MB | 107.3 MB | 9.25 MB | 3/1 |

    - Vesper pane walk:

      | Setting | Private | Working set | GC committed | GCs in the walk (gen0/gen1) |
      |---|---|---|---|---|
      | Default | 117.2, 118.9 MB | 112.0, 111.9 MB | 17.7 MB | 1/0 |
      | `GCgen0size`=4 MB | 107.4 MB | 100.6 MB | 6.2 MB | 5/1 |
      | `GCgen0MaxBudget`=6 MB | 109.1 MB | 102.2 MB | 8.0 MB | 3/1 |

    - Bytes per key are unchanged (68.6 vs 68.8 KB; 224.0 vs 224.3 KB).
    - Idle apps with no GC yet are unchanged: Junction 115.2 vs 114.3 MB, Meridian 107.0 vs 108.3 MB.
    - NativeAOT honours both knobs as `DOTNET_` variables; `GC.GetConfigurationVariables` reports 6,291,456. To build them into the exe, Microsoft.NETCore.Native.targets:268 already passes `--runtimeopt:gcServer=1`, so `<IlcArg Include="--runtimeopt:GCgen0MaxBudget=600000" />` should work. Verify that in the shipped exe.
  - **evidence, (b) LOH threshold:**
    - Every TextArea edit makes the document a new string (TextAreaElement.cs:1129). Above 42,500 characters that string lands on the large-object heap, and large-object allocation triggers gen-2 GCs. Probe with concurrent GC on, 2,000 lines (231 KB string, 243.5 KB/key):

      | Live heap | Default (per 200 keys) | Threshold 512 KB (per 200 keys) |
      |---|---|---|
      | 3.6 MB | 14 blocking gen-2 | 1 gen-0 + 1 gen-1 |
      | 15.7 MB | 12 background gen-2 | – |
      | 27.4 MB | – | 2 gen-0 + 1 background gen-2 |
      | 39.6 MB | 3 background gen-2 | – |

    - A 20,000-line document (2.3 MB string) gets 32 background gen-2 per 100 keys. A 4 MB threshold turns that into 12 blocking gen-1, which is not a clear win, so keep about 512 KB. That covers documents up to roughly 256k characters, such as Junction's TODO.md (89k).
  - **expected:**
    - (a) clears the retained-memory bar: working set −10 % in Junction (118.2 → 105.9 MB) and −9 % in Vesper; private −11 % and −8 %. The saving is smaller on CPUs with less L3.
    - (b) removes the full GC that comes every 14–17 keystrokes when typing in 42.5k–256k-character documents.
    - The build phase must time both: frame p95/p99 and `GC.GetTotalPauseDuration` over the app walks and a TODO.md typing run.
  - **cost:** small. Two runtime options per app, plus a PERFORMANCE.md note. No API or pixel impact. Process-wide: more gen-0/gen-1 GCs, and large arrays under 512 KB are allocated in gen 0.

- **id: paint-run-ink-extents-dead-keys** (owner 2, code in SkiaRenderContext; retention found by 4)
  - **what:** the run ink-extent map (b52ca178) keys on `ShapedRun` by reference and holds up to 2,048 runs (SkiaRenderContext.cs:106, :1133, :1164). Once the text caches drop a run, no lookup can hit it again, yet the map keeps it alive. Replace the map with a per-face table of glyph x-extents, filled lazily by the same `GetGlyphWidths` call at size 2048. Then compute each run's union per drawn line. Storing the extent on the run itself (+8 B per run) would also work.
  - **evidence:** blob probe, 20,000 spanned lines in Cascadia Mono, 397 screens scrolled, then the document closed:
    - The map still holds 1,592 runs (136,661 glyphs); 1,460 of them are held by no blob cache.
    - After the text caches empty (a UI-culture switch), the map alone holds 2.29 MB: heap 12.46 → 10.17 MB, 18 % of what remains. The blob caches hold 0.20 MB more.
    - The size grows with line length, since a TextArea line is one run here (86 glyphs, ~1.4 KB).
    - Consolas and Segoe UI are unaffected: the map stays empty for faces whose bounding box Skia trusts.
  - **expected:** retained memory −2.3 MB in that window (−18 % of the heap left) once its document's text has gone from the caches; the ~57 KB map per context also goes. The extents stay bit-identical, so the sub-blobs and pixels are unchanged.
  - **cost:** small to medium. No API change (the run-field variant adds an internal field to `ShapedRun` in HultUI.Text). Per drawn line, a loop over the run's glyphs replaces one dictionary lookup.

## Examined, not worth it

- **Suite allocation vs round 4:** all 140 rows are inside round 4's range, JIT (23 after-captures) and AOT (18). None moved by both ≥5 % and ≥0.5 KB against the last capture.
- **JIT vs AOT:** 120 of 140 rows are within 1 KB; the rest within ±4.5 %, except `overlay/dropdown open 200 items` at −8.2 % on AOT (lower). The old `ToString`-label outlier shrank from +74 KB (+12.8 %) to +15.4 KB (+3.7 %), since the leaf patch.
- **App key walks, JIT vs AOT:** Junction History 69.5 / 68.6 KB/key; Vesper 226.5 / 223.8 KB/key. With UIA on (AOT): Junction 81.4 (0.78 trees/key), Vesper 226.6. No AOT-only allocation. Vesper's whole-window build is apps-vesper-components, already waiting.
- **Idle on AOT (20 s, UIA client listening):** Junction, Vesper, Meridian, Aperture (lab Jellyfin), Reel and the GL probe all 0.0 passes/s.
- **Private bytes since round 4:** up about 72 MB, but the machine changed, not the code. Round 4's own AOT exe today reads 169.9 MB for Junction (98.1 then) and 97.6 for the probe. On the same day, r5 vs r4: Junction 170.1 vs 169.9 MB (heap 7.54 vs 7.90); Aperture 200.7 vs 199.6 MB (heap 10.87 vs 10.68).
- **Steady video frame:** Reel 1080p24 on AOT, 90–111 B/frame with a11y off, at the 0.1 KB target. With UIA, 315–333 B/frame at 1.1 trees/s; that is the elapsed label's layout refresh, already waiting as a11y-layout-refresh-bounds-only.
- **e6a432fa (Vesper kept cells):** after leaving Brief, 140 cells per pane stay referenced but hold 0 KB the tree does not already hold. 30 Brief/Thumbnails/Detail cycles plateau at 6.15 MB from cycle 19: bounded caches, not a leak.
- **Two loop passes per drawn frame** (Reel 48 passes/s at 24 fps; 4.08 per key in Junction): a pass with no frame allocates 0 B. This is the loop's design, and does not count against the idle bar.

## Needs the user

1. **TextArea document as one string**, declined in rounds 3 and 4 because a rope changes the public `Text`. There is new GC evidence: typing above 42,500 characters triggers a gen-2 GC every 14–17 keys at app-sized heaps (blocking at 3.6 MB of live heap), and every 2–3 keys at 2.3 MB documents. Recommendation: take the LOH-threshold half of candidate 1 first. Revisit chunked storage, with `OnTextChanged` handing over the string only when asked, only if documents above about 256k characters matter.

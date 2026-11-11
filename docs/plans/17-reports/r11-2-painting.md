SATISFIED: yes

I measured at dee48fbb on SDK 11.0.100 / net11.0. Everything ran headless and I counted calls and bytes only, with no timings. I opened no real windows, so no monitor was involved. Round 10's paint probe (`probe-r10.patch` plus `PaintProbe.cs`) applied cleanly; the Window.cs hunks moved 44 lines. I reverse-applied the patch afterwards and deleted PaintProbe.cs. The worktree is clean and `dotnet build-server shutdown` has been run.

REVIEW: none.
- **Paint code:** only 207fbe25 touches `src/HultUI.Rendering.Skia`, `src/HultUI.Core/Rendering` or `HultUI.Terminal*`, and only their TargetFramework line.
- **Suite, 221 rows shared with round 10's .NET 10 run (3462f18e):**
  - Every paint counter is within 5 % (`cmp.py` prints no row).
  - A strict render-bytes diff (`kbdiff.py`, any change over 0.005 KB) finds 1 row up: `richtext/open 5,000 blocks` 2,235.5 → 2,242.6 KB (+0.3 %). It is a first open, under every bar. The upgrade saw +5.4 KB total on this row on the same code. The steady rich text rows (type, enter, wheel) are byte-identical.
  - 26 rows went down by 0.03–0.25 KB, all VirtualList rows (round 10's list work).
- **Vesper (vdrv, 1240×820):**
  - Detail and Thumbnails are identical to round 10, cut and plain.
  - Brief draws 30 rounded rects a frame instead of 32.
  - Sync with Show equal on: Down/Up renders with 0 B and drawRun goes 222 → 217.5 (kept rows, the ring moves). The wheel is 0.38 KB a step either way.
- **The two TextArea rows that got slower on AOT** draw exactly what they drew on .NET 10, on JIT and AOT:
  - `text/open a long document in a TextArea`: 35.4 draws, 3,828.8 glyphs, 35.4 blobs made, 308.01 KB.
  - `text/resize a wrapped TextArea`: 36 draws, 4,138 glyphs, 2.6 blobs, 9.94 KB.
  - So the AOT slowdown is codegen per call, not more paint work (for experts 4 and 5).

.NET 11 STEADY-FRAME CHECK (asked): the paint path still allocates 0 B.
- **JIT default** (5 s pre-warm, 5 rounds × 5 steps): 102 of 230 rows render with 0 B.
- **`DOTNET_TieredCompilation=0`:** every method is fully optimized from its first call, the codegen that boxed MetricDigest (TieredPGO=0 changed nothing there).
  - The same 102 rows are at 0 B.
  - Every KB counter on all 230 rows equals the default run within 0.005 KB, except `richtext/open` (−0.2 %).
- **NativeAOT** (published with `aot.ps1`, step-ab's wrapper):
  - The same 102 rows are at 0 B.
  - 51 rows render 0.16 KB more on AOT. 48 of them are the probe's own `"~UpdateContentSize:" + GetType().Name`, which makes a new string on every call on AOT (checked by `aotcheck.py`). The other 3 are rich text rows at +0.01–0.04 KB.
- **Coverage:** the 0 B rows exercise every paint path the probe counts, except the ones that make something new (paragraph, runsMadeInRender, subMiss, imageFromPixels, glyphInkRead). Covered at 0 B (rows):
  - blob lookups (91) and sub-blob lookups (9);
  - the gradient shader and blur filter caches;
  - faded leaves, transforms (4), rounded clips (8), layers (3);
  - decorations, images (8) and spanned lines (9).
- **Key probe (`keys\`):** each case ran 1.2 M warm-up calls with pauses for the background compiler, then 1 M measured calls, on default tiering and on TC=0. Every paint-side key shape allocates 0 B a call:
  - `ResolvedInheritedStyle` lookup (`_prefixMeasurers`);
  - `FontKey` and `FallbackKey` lookups (shapes copied, the latter with a `string?` field);
  - `SubRun` lookup;
  - `TextSpan` `==`, `GetHashCode` and `SequenceEqual` (nullable fields);
  - `RichMarks` `IsNone`, `==` and `GetHashCode` (`Color?`, `float?` fields);
  - `TextHighlight`;
  - `GradientKey`'s `HashCode.Add<GradientStop>`;
  - `SameGlyphs`' `HashCode.Combine(int, float, string)`;
  - `FaceInk`'s `InkExtent?`.
- **Control case:** `HashCode.Add<Color?>` plus `Add<float?>`, twice, costs 96 B a call. That reproduces c99e23bf's box. Record-struct equality and hashing go through `EqualityComparer<T?>.Default`, which does not box; only `HashCode.Add`/`Combine` over a `Nullable<T>` does.
- **Grep:** no `HashCode.Add`/`Combine` over a `Nullable<T>` is left in `src` (MetricDigest is fixed) or in `apps`. Junction's ChangeRow and review signature, Vesper's and Meridian's keyring hashes all take non-nullable values (ThreadView's `Id` is a string, `Resolved` a bool).
- **Terminal:** identical on default tiering and TC=0 (numbers below).

CANDIDATES: none.

WAITING, RE-RANKED:
- **terminal-noop-scrollback-wheel:** unchanged, keep low.
  - R8Probe on .NET 11, default and TC=0: a no-op notch still draws one whole frame. At the bottom that is 5,440 glyphs and 836 draws; at the top 5,682 and 882. It allocates 112 B a notch, 0 B of it in render. These are round 10's numbers exactly.
  - The repaint still comes from `ScrollViewport` (TerminalElement.cs:566).
  - No HultUI.Terminal commit since round 10 except the TFM line. Only the terminal sample and the perf suite reference HultUI.Terminal, so no shipping app gains.
- **terminal-row-strings-by-content:** unchanged, keep low, same reason. A streamed line costs 10,276 B of render, all in `TerminalElement`, with 55 draws and 1 blob made, identical to round 10.

EXAMINED, NOT WORTH IT:
- **`richtext/open 5,000 blocks` +7.2 KB render on .NET 11 (+0.3 %):** first open only. The bytes are the paragraph layout done inside render, which is the text pipeline (expert 5). The steady rich text rows did not move.
- **Sub-blob misses after a relayout:**
  - `richtext/type in a paragraph of 600 spans` makes 223.6 sub-blobs a key, because every sub-draw misses. `SubRun` is keyed by the run object by design (SkiaRenderContext.cs:150-155). Whole-run blobs miss only 2 a key. The misses allocate 0 managed bytes (native blobs).
  - Realistic rows make 3 a key: rich text typing in 5,000 blocks and its a11y variants. Highlighted JSON makes 18.6 a wheel step. Only the synthetic 600-span paragraph pays.
  - A content-keyed fallback would need shared native refs (medium cost) for a row no app has.
- **Pixel-route frames, 248 B a drawn frame** (two SkiaSharp wrappers in `SKImage.FromPixels`, e.g. `VideoElement` 6,448 B over 26 frames): this is round 4's "pixel-path video frames" item. Only a surface without GL takes this route. GL and Vulkan wrap textures once (0 B), and a WebView uses a `GpuFrameSink` on GL.
- **7ca2b3db (the diff pan reads its width after the frame):** headless resize and drag steps with a pan bar draw 2 frames instead of 1 (the commit's own count). It happens only when the width moved and the bar is shown. In a paced loop the second frame should merge with the next move's frame. Not an objection; a live Windows resize was not counted (expert 3's held-resize harness could).
- **Caveat on r11u's AllocHook:** its .NET 11 run lists `System.UInt32` (2.5 MB), `Object[]` and a `<>c__DisplayClass0_0` that its .NET 10 run does not. Unverified, but probably the listener's own event payloads, not HultUI. Read its type lists with that in mind.

NEEDS THE USER: none.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\ec6d577c-f9ba-4479-acbd-908e67c0ea0a\scratchpad\r11-painting\`
- **Probe:**
  - `probe-r11.patch`: round 10's probe at dee48fbb, including the off-by-default `PROBE_LINESEEK`. Copy `PaintProbe.cs` to `src/HultUI.Core/Rendering/` first, then `git apply` the patch from the worktree.
  - `PaintProbe.cs`.
- **Scripts:**
  - `cmp.py` (5 % compare), `kbdiff.py` (strict KB diff), `coverage.py` (paint paths at 0 B), `aotcheck.py` (AOT vs JIT attribution), `top.py`, `newrows.py`.
  - `aot.ps1`: NativeAOT wrapper publish to `aot\out`.
- **Drivers (net11.0, this worktree):**
  - `keys\`: the struct-key probe.
  - `probe\`: R8Probe, for the terminal wheel and stream.
  - `vdrv\`: Vesper, on round 10's `fx\` fixtures.
- **Outputs:**
  - Suite: `suite-probe-r11.txt` (JIT), `suite-probe-r11-tc0.txt`, `suite-probe-r11-aot.txt`, `ab-r10-r11.txt`.
  - Terminal: `terminal-wheel.txt`, `terminal-wheel-tc0.txt`, `terminal-stream.txt`, `terminal-stream-tc0.txt`.
  - Vesper: `vesper-views.txt`, `vesper-sync.txt`.
  - Build logs: `build-*.log`, `publish-aot.log`.

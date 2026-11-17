# Major dependency upgrades

Three Dependabot pull requests from 2026-11-14 were closed on 2026-11-16 without merging. They
are migrations, not version bumps: two broke real tests in CI, and the third put a generated
client on a runtime it was not built for. Each one gets its own branch, lands as its own change,
and is done only when the full suite is green on both operating systems.

| # | Upgrade | Closed PR | Why it waits |
|---|---------|-----------|--------------|
| 1 | SkiaSharp 3.119 → 4.x, HarfBuzzSharp 7.3 → 14.x | fredrikhu/hultui#2 (4.155.0 / 14.4.0) | Breaks the C API pin, goldens, bold-text metrics, zero-allocation tests |
| 2 | Kiota 1.22 → 2.x, together with Jellyfin.Sdk 2025.10 → 2026.10 | fredrikhu/hultui#3 (Kiota 2.1.6 alone) | Jellyfin.Sdk 2025.10.21 is generated against Kiota 1.x |
| 3 | xunit.runner.visualstudio 2.x → 4.x | fredrikhu/hultui#6 (4.0.1) | Breaks cross-thread tests on Windows and Linux |

**Order.** Kiota first: it is the smallest, it is confined to Aperture, and nothing waits on it.
Then xunit, because a runner that changes test threading should not land in the same week as a
renderer change. SkiaSharp last: it is the largest, and it needs a real-window check and a perf
pass.

**Dependabot keeps asking.** Closing a Dependabot pull request skips only that exact version.
The next release in the same major (SkiaSharp 4.156, say) arrives as a new pull request. Until
an item here starts, close those with a link to this file. Or add an `ignore` entry with
`update-types: [version-update:semver-major]` to `.github/dependabot.yml`, next to the SSH.NET
entry, and remove it when the work starts.

## 1. SkiaSharp 4 and HarfBuzzSharp 14

**Status (2026-11-17): in progress on `claude/skiasharp-4`, SkiaSharp 4.156.1 (Skia m156) with HarfBuzzSharp 14.4.0.101.**

- **Builds** with no compile breaks. `NativeTextBlob`'s four C functions are declared with the same signatures, so its pin moves to 4.156.1.
- **Allocations fixed (step 5).** SkiaSharp 4's `SKPath` mutations make a new `SKPathBuilder` (80 B) each time a drawn path is changed again. The pooled path is now an `SKPathBuilder`, drawn and clipped through `NativePath` (four C functions, like `NativeTextBlob`; docs/DECISIONS.md, 2026-11-17). `PolygonTests`, `TextDrawAllocationTests` and `FrameAllocationTests` are back to 0 B a frame, and `NativePathTests` (8) hold the exports, the signatures and the pixels to SkiaSharp's own path calls.
- **Bold text fixed (step 4).** In 4.x, `SKTypeface.FromFamilyName(name, style)` is `MatchFamily(name, style) ?? Default`, so a family DirectWrite does not know (`sans-serif`) came back as the regular default face and dropped the weight. `SkiaFontCollection` now asks for the default family in the weight and slant wanted, as 3.x's legacy lookup answered. The four bold tests pass; `HultUI.Rendering.Skia.Tests` 435/435.
- **Obsolete calls** replaced: the `DrawText` overload, `SKTypeface.GetGlyph` (the face's own `SKFont` now), and `SKPath.Add*` in two icon sets.
- **Goldens (step 6): 14 baselines regenerated, each reviewed.** Five `ControlGoldenTests`, four `OpacityLayerTests` and four `RichTextGoldenTests` failed; `TablesAndPictures` holds two baselines, and its second (`richtext-picture-selected`) only showed when the first was rewritten. Same sizes; 1.3–7.4 % of pixels change, the largest channel difference is 31–72 of 255, and at most one pixel per image differs by more than 64. Zoomed, glyph shapes, positions and weights and the shadow blur are identical; only edge anti-aliasing differs, as expected of Skia's path rasterizer between m119 and m156 (the goldens draw text as outlines).
- **Full suite on Windows** (after the regenerated goldens): 29 of 30 assemblies pass. The 30th fails only `Junction.Tests` `CommitPatchTests.AnotherCommitsDetail_LetsTheShownOneGo`, which fails locally on `main` too and passes in CI.
- **Linux, first run (WSL Fedora, `HultUI.CrossPlatform.slnf`):** everything passed except `ControlGoldenTests.Text_WeightsFamiliesSizesAndWrapping`. In `text.png`, 21 pixels differed from the Windows baseline (largest channel difference 45): the stroke ends of the oblique `t` in "the slant", and the tops of "Åäö". Regenerated on Linux, `opacity-overflow.png` also differed in bytes (62 pixels, at most 4 apart, inside its tolerance).
  - **Cause:** the glyph outlines. Under `TextAsOutlines` they came from `SKFont.GetGlyphPath`, which reads the font through DirectWrite on Windows and FreeType on Linux, and the two disagree on some glyphs. For the five bundled DejaVu faces at 11, 13, 14 and 24 px, 44 of 1,260 outlines differ in structure (DirectWrite keeps a cubic where FreeType has a quadratic and a line), and 16 differ by 0.005–0.02 px. SkiaSharp 3.119.4 gives exactly the same differences, so they are not new. Skia m119's rasterizer happened to make the same `text.png` pixels from both; m156's does not.
  - **`main` was not fully byte-identical either:** at 99db58cf, `opacity-overflow.png` and `opacity-shadow.png` made on Linux differ from Windows, inside their tolerance.
- **Fixed: pinned text takes its outlines from HarfBuzz** (`GlyphOutlines`; docs/DECISIONS.md, 2026-11-17). HarfBuzz reads the font file itself through `hb_font_draw_glyph`, which both 14.4 native libraries export but HarfBuzzSharp does not bind, so it is called through function pointers. Its outlines for the 1,260 glyphs are byte-identical on Windows and Linux, with the points Skia's have. Single-point contours (hinting anchors, such as one in DejaVu's u) are dropped, as DirectWrite and FreeType drop them. `GlyphOutlinesTests` (6) holds the exports, the bounds to Skia's within 0.03 px, and the floats to one hash. Regenerated from it, only `text.png` and `opacity-overflow.png` changed on Windows, and both became byte-identical to the frames Linux had made.
- **Linux, second run:** `HultUI.CrossPlatform.slnf` passes in full (22 assemblies, the outline hash included). All 14 baselines regenerated on Linux are **byte-identical** to the Windows ones. Junction, Meridian, Aperture and Reel are not in that filter and were not run on Linux; Meridian's pinned-text `NarrowEditorLayoutTests` passes on Windows.
- **Perf A/B (step 7), measured 2026-11-17** against `main` 99db58cf with `scripts/perf/step-ab.ps1`: two alternated, idle-gated pairs each on JIT and NativeAOT. Allocations moved on **no row**, and the A/A pairs flag no row.
  - **Windows: 38–67 rows slower, 3–6 faster.** The slower rows are 10–17 % in **render**, and on AOT up to 50 % (`paint/dense text screen`, `a11y/type in a rich text editor inside a VirtualList row`): everything that draws much text, rounded boxes or 1 px borders. `paint/tiles shadow` is 55 % faster, `overlay/dialog open` 20 %, and the real-loop rows that fade (`anim/signal tween`, `media/video player bar fade`) 15–28 %.
  - **Cause of the render rows: Skia m156 rasterizes RGBA8888 more slowly than m119 did**, and the headless surface (`SkiaRasterRenderSurface`) is RGBA8888. Each draw call timed alone on a CPU surface, SkiaSharp 3.119.4 against 4.156.1 (P-cores, min of 300 batches):

    | 400 or 1,600 draws | Windows RGBA | Windows BGRA | Linux RGBA | Linux BGRA |
    |---|---|---|---|---|
    | AA rects | +34 % | 0 % | +14 % | +3 % |
    | AA round rects | +14 % | −12 % | +8 % | −3 % |
    | 1 px round-rect strokes | +36 % | −5 % | +14 % | 0 % |
    | linear-gradient round rects | +13 % | +13 % | +3 % | +2 % |
    | text blobs, 11 px | +20 % | −10 % | +6 % | −16 % |

    BGRA8888 is Skia's native order on both OSes (`SKImageInfo.PlatformColorType`). RGBA costs about 2× BGRA on Windows and 3–8× on Linux in **both** versions: 1,600 text blobs take 1.2 ms in BGRA and 2.4 in RGBA on Windows (3.119), and 1.5 against 8.0 ms on Linux. The software renderer (`RendererKind.Software`, the fallback when OpenGL cannot start) drew into the same surface until c39c9478 (on `main` the same day) had it draw straight into SDL's window surface, which is BGRA on Windows. GPU windows do not pay it.
  - **Cause of the layout rows: HarfBuzz 14.4's shaper costs more per call.** Swapping only `libHarfBuzzSharp.dll` 8.3.1 → 14.4 under `main`'s build reproduces every layout move: `text/create 2,000 unique labels` +14.5 %, `… emoji labels` +13.6 %, `text/styled bidi ellipsis` +6.9 %, `text/bidi ellipsis` −5.5 %. Timed alone on a kept buffer and font, `hb_shape` on a one-letter word takes 237 → 414 ns with Segoe UI (+193 ns), +137 ns with Arial and +37 ns with DejaVu Sans. On 3–9 letter words Segoe UI is +32 % slower, Arial +1 % and DejaVu 26 % faster, and 80-character lines are +4 %. It is in running the shape plan: a kept plan (`hb_shape_plan_execute`) saves 40–50 ns on both versions alike. Only words the word cache has not shaped yet pay it.
  - **Linux (WSL Fedora, 207 rows, all but the 28 UIA rows; three pairs, unpinned because WSL shows no CPU frequencies): 0–1 slower, 2 faster.** `paint/tiles rounded + border` +11 % in two of three pairs; `paint/tiles shadow` −22 %, `anim/signal tween` −13 %; `text/create 2,000 unique labels` −6.5 %. HarfBuzz 7.3 → 14.4 here.
  - **The m119-named decisions.** The GPU text-blob cache that the whole-pixel baseline works around (2026-11-02) is unchanged in m156: `TextBlob::Key` still takes the origin's subpixel fraction when a blob has direct sub-runs, and `BlobIDCacheEntry::findBlobIndex` still scans every entry for the blob on each draw, so the decision stands; its GPU numbers belong to step 8. The run-blob maps and the opacity layer bounds show no move of their own: `paint/tiles transform + opacity` −6 %, `paint/400 faded icons` +3 %, `scroll/jump` +10 % (render, RGBA).
- **Fixed: the CPU surface draws in BGRA, Skia's native order** (docs/DECISIONS.md, 2026-11-17). `SkiaRasterRenderSurface` takes `SKImageInfo.PlatformColorType` and says so (`PixelColorType`); `PeekPixels` and `CapturePixels` hand out BGRA, while `ReadPixels`, `PresentCapturing` and the golden comparison still give RGBA; the software renderer's copy, kept for window surfaces Skia cannot draw into, tells SDL `ARGB8888`. Every golden was regenerated (blends round differently in the native blitters, at most 4 of 255), and all 14 are again byte-identical on Linux. `SkiaRasterRenderSurfaceTests.TheFrame_IsInSkiasNativeOrder_BgraOnTheDesktop` holds it.
  - **Suites:** Windows, every assembly passes, Junction included; Vesper's `KeptRowTests.AFolderMeasuredWithSpace_ShowsItsTotal` failed once in the full run (a 10 s wait for a background folder size, under load) and passes 3 of 3 alone. Linux, `HultUI.CrossPlatform.slnf` passes in full; `HultUI.Headless.Tests` takes 25 s instead of 33.
  - **Perf A/B again, against `main` 99db58cf:** Windows JIT 0 slower and 196–198 faster in both pairs; NativeAOT 0–1 slower (`text/create 2,000 unique labels` +15 %, the HarfBuzz cost above) and 190–194 faster. Linux 0–1 slower and 190–192 faster; the one is `input/wheel over a list that cannot scroll, real loop` +14–19 % in two of three pairs, a step of 8 µs that draws no frame (no Skia in it). Allocations moved on no row, and the A/A pairs flag none. Minimum ms, `main` → this branch:

    | Row | Windows JIT | Linux |
    |---|---|---|
    | `paint/dense text screen` | 0.708 → 0.377 | 2.176 → 0.465 |
    | `paint/tiles rounded + border` | 0.901 → 0.486 | 2.473 → 0.703 |
    | `paint/tiles shadow` | 4.372 → 0.867 | 11.683 → 1.116 |
    | `paint/tiles transform + opacity` | 2.111 → 0.940 | 7.684 → 1.416 |
    | `richtext/redraw at the top of a 300x10 table` | 0.616 → 0.333 | 2.005 → 0.453 |
    | `terminal/dense colour screen, unique rows` | 5.902 → 4.102 | 11.781 → 5.229 |
    | `paint/tiles gradient` | 1.433 → 1.523 | 5.265 → 5.545 |
    | `text/create 2,000 unique labels` | 2.857 → 3.249 | 3.065 → 2.419 |

    Gradients stay about 6 % slower (m156's gradient shader, in either byte order), and cold Segoe UI words about 14 % (HarfBuzz 14.4). Shaping whole runs instead of word by word would be the way round the HarfBuzz cost: a text-pipeline change for a plan of its own, not part of this upgrade.
- **Merged to `main`** as 3530f3b4 (2026-11-17), over c39c9478, which had the software renderer draw straight into SDL's window surface: on Windows that is XRGB8888, so the software renderer already drew BGRA there, and the BGRA switch covers headless, the goldens and the perf suite. The copy kept for other window-surface formats takes its SDL format from `PixelColorType`. The full suite passes on the merge, all 30 assemblies.
- **Real windows (step 8), 2026-11-17, Windows, RTX 4080 on NVIDIA 610.88, the HDR display on (SDR white 240 nits), 120 Hz:**
  - **`HultUI.UiTests` with `HULTUI_UITESTS=1`:** 148 of 166 pass, 17 skip and 1 fails. The skips are the 14 tests that need `HULTUI_UITESTS_FOREGROUND`, plus mpv's three, which need `HULTUI_MPV_PATH`. Run again with it set (the installed Reel's `libmpv-2.dll`), the video tests pass, 55 of 56 in that filter: Vulkan 8-bit, half-float MAILBOX and GL with video, `AnHdrVideo_SwitchesTheSwapchainToHalfFloat_OnceEachWay`, the steady video frame's allocations, a video in a minimized window, and mpv's GL path. Without `HULTUI_MPV_PATH`, those Vulkan video tests pass in under a millisecond without running a clip. Also passing: Vulkan drags, resizes and captures on every swapchain variant, `SaveFrame` on every path, live resize inside the modal loop, frame pacing, and the software renderer (`TheFrame_IsDrawnAndReachesTheWindow`, `AWindowThatGrows_IsDrawnAtItsNewSize`). No guard handed the foreground back.
  - **The one failure is not this upgrade's:** `OsDropTests.AWebPage_IsOfferedTheDrag_WithItsFiles_AndItsAnswerReachesTheOperatingSystem` ("the page's acceptance had not reached the operating system after 1013 ms") fails the same way, 3 of 3, on `main` a892dc14 from before the merge.
  - **Native AOT publishes:** Junction, Vesper Commander, Meridian, `meridian` (the CLI), Aperture and Reel all publish with no trim or AOT warnings (no IL2xxx or IL3xxx).
  - **The acquire fence stays.** SkiaSharp 4.156's managed API has no `GRBackendSemaphore`, no `GRFlushInfo` and no way to set an image's layout, and its native library exports the same five flush and submit functions as 3.119.4 (`gr_direct_context_flush`, `_flush_and_submit`, `_flush_image`, `_flush_surface`, `_submit`). So there is still nothing to hand a semaphore to, and the host still has to put the image layout back for Skia. `GRContextOptions` is unchanged too, so there is still no program cache. Nothing here can go.
- **Left:** run each native build on the desktop, watch an HDR video on the HDR display, and do a live resize by hand.

**What broke in #2** (CI run 43824026613, 2026-11-16; the log was read only up to the first
failures, so there may be more):

- **C API pin.** `NativeTextBlobTests.SkiaSharp_IsThePinnedVersion`. `NativeTextBlob` calls four
  functions of libSkiaSharp's C API through function pointers: `sk_textblob_builder_make`,
  `sk_canvas_draw_text_blob`, `sk_textblob_get_bounds` and `sk_textblob_unref`
  (docs/DECISIONS.md, "Text blobs are native handles", 2026-10-11).
- **Bold text measures differently.**
  - `TextParagraphTests.Caret_AfterABoldWord_SitsWhereTheBoldWordEnds`
  - `TextTruncationTests.MinContentWidth_AccountsForABoldSpanWideningItsWord`
  - `TextPipelineStyledTests.ABoldSpan_WrapsWhereTheBolderTextActuallyEnds`
  - `TextPipelineStyledTests.ABoldSpan_MakesItsRangeWider`
- **Zero-allocation tests.** `FrameAllocationTests.ASteadyRepaintOfTasks_AllocatesNothing` and
  `PolygonTests.PolygonsAndPolylines_AllocateNothingPerFrame`.
- **Pixels.** Four `OpacityLayerTests`, four `RichTextGoldenTests` (Showcase dark and light,
  tables and pictures, a selection across blocks) and `SaveFrameTests`.

**What to expect for performance.** Checked on 2026-11-17 against the release notes and
bindings, not measured. Expect modest gains in shaping text that isn't cached yet, larger on
Linux, no free win in steady frame time, and an allocation regression to fix first.

- **HarfBuzz: real but narrow gains.** Speedups documented since our versions:
  - 10.3.0: faster OpenType kerning and ligatures (Roboto benchmark 10.3 → 9.4 ms), at about
    1 KB more cache per font face.
  - 11.0.x: the OpenType shaper no longer allocates.
  - 11.3.0: glyph advances up to 24 % faster.
  - Later releases: "various shaping speedups", without numbers.

  Shaped words are cached, so these show only on a cache miss: first layout, rows scrolled into
  view for the first time, and typing. Frames that only repaint shaped text won't change.
- **Linux moves further than Windows.** No HarfBuzzSharp.NativeAssets.Linux 8.x was ever
  published, so today Linux runs the 7.3.0 native library under the 8.3.1 managed binding
  (SkiaSharp.HarfBuzz 3.119 brings HarfBuzzSharp 8.3.1.3). Windows runs 8.3.1 throughout. The
  upgrade is 7.3 → 14.4 on Linux and 8.3.1 → 14.4 on Windows, so measure the two separately.
- **SkiaSharp: no documented frame-time win.** The 4.148 to 4.155 release notes list API,
  lifecycle, variable-font and security changes, none for speed. `GRContextOptions` has the same
  six properties in 3.119.4 and 4.155, so there is still no persistent program cache for
  start-up GPU work. Skia's internals moved 36 milestones, so the m119-tuned decisions in step 7
  could get better or worse.
- **A known cost first.** #2's run failed `FrameAllocationTests` and `PolygonTests`: 4.x
  allocates on every frame where 3.119 allocates nothing (step 5).
- **The real upside is Graphite.** 4.155 binds Skia's Graphite backend for Vulkan, Dawn and
  Metal (`SKGraphiteContext` and related types). Using it means a new render backend for HultUI,
  as the Vulkan work was, so it gets its own plan after this one, to be decided by measurement.

**Steps:**

1. **Bump all four packages together** (SkiaSharp, SkiaSharp.HarfBuzz,
   SkiaSharp.NativeAssets.Linux, HarfBuzzSharp.NativeAssets.Linux). The `skia` Dependabot group
   exists for this reason.
2. **Recheck the C API.** Diff `binding/SkiaSharp/SkiaApi.generated.cs` between the old and new
   tags of mono/SkiaSharp for the four functions. The move to 3.119.4 was checked the same way:
   the file was byte-identical. If a signature changed, change the function pointer types in
   `NativeTextBlob.cs`. Then move the pin in `NativeTextBlob.cs`, `NativeTextBlobTests.cs` and
   docs/DECISIONS.md.
3. **Recheck HarfBuzz.** `SkiaTextShaper` shapes with HarfBuzzSharp directly (e21e376), so the
   managed binding's 8.3.1 → 14 is a direct API change for us. `ShaperEquivalenceTests` must stay green against
   SkiaSharp.HarfBuzz's shaper.
4. **Bold text.** Find out what changed in a bold run's advance (font embolden, metrics, or
   shaping) before changing any expected width. These tests guard real layout: wrapping, carets,
   min-content.
5. **Allocations.** Find each new per-frame allocation with the allocation tests and remove it, or
   route around the SkiaSharp wrapper that makes it. Never loosen the zero bound.
6. **Goldens.** Review each diff image (CI uploads `golden-failures-windows`) before regenerating
   with `HULTUI_UPDATE_GOLDENS`. Windows and Linux must still produce identical PNG bytes.
7. **Perf.** Several decisions were measured against Skia m119: the run-blob maps, the whole-pixel
   baseline for the GPU text-blob cache (docs/DECISIONS.md, 2026-11-02), and the opacity layer
   bounds. Run an idle-gated perf-suite A/B and re-measure any decision that names m119. Include
   text that isn't cached yet (first layout, scrolling into new rows, typing) on both OSes,
   because that is where HarfBuzz's changes show.
8. **Real windows.** Run GL and Vulkan, HDR video on an HDR display, live resize, and Native AOT
   publishes of each app. Of the 3.119 limits that docs/design/HDR-LOG.md and `SdlVulkanHost.cs`
   describe, `GRContextOptions` is unchanged in 4.155, so the missing program cache stays. Check
   whether the fence on acquire can go, and record anything that can as its own follow-up.

**Done when** the full suite is green on both operating systems, every golden change has been
reviewed and explained, the perf A/B shows no regression, and the real-window check has passed.

## 2. Kiota 2 together with Jellyfin.Sdk 2026.10

**Status (2026-11-17): done on `claude/kiota2-jellyfin-sdk`. Every check below passed.**

- Jellyfin.Sdk 2026.10.12 and Kiota 2.1.6 build with no code changes.
- Aperture's tests pass: 364, plus 4 new `JellyfinSerializationTests`. These read dashless ids
  through Kiota 2 and show that stock Kiota still loses them, so `JellyfinSerialization` stays.
- The Native AOT publish has no trim or AOT warnings. Against the lab server (Jellyfin 12.4.0),
  the native probe signed in with an API key as a named user (`--user`), listed both libraries,
  and read an item in full: 9 chapters, trickplay, artwork tag. Dashless user and item ids work
  end to end.
- The live tests pass, 9 of 9 (`LiveServerTests`). `APERTURE_LIVE_SERVER` must be a full URL
  (`http://jellyfin.home:8096`) because one test reads its host with `new Uri`. `APERTURE_USER`
  must be set too, because the lab server has several users.
- Playback works in the app (the native build, with the release's `libmpv-2.dll` beside it).
- Found on the way, on `main` as well: `ApertureFailure.UserMessage` drops the detail of a
  `NotFound`. So an API-key sign-in without a user, on a server with several users, reads "That
  is no longer on jellyfin.home:8096" instead of asking which user to browse as.
- Not taken: the SDK's `JellyfinRequestAdapter` now has its own dashless-id converter and a
  faster parse node, but it throws on a malformed id where Aperture's converter reads
  `Guid.Empty`, and it takes no serialization writer. Adopting it is a separate decision.

**Why Kiota cannot move alone.** Jellyfin.Sdk 2025.10.21 depends on Kiota 1.20.1. The
`Aperture.Core.csproj` comment pins Kiota forward to 1.22.2 because 1.20.1 carries
GHSA-7j59-v9qr-6fq9; 1.22.2 is the same major and binary-compatible. Kiota 2.0.0 (2026-05-06)
removed APIs:

- `IAsyncParseNodeFactory`, now merged into `IParseNodeFactory`
- the synchronous `KiotaSerializer.Deserialize` and `DeserializeCollection`
- `MultipartBody.GetPartValue<T>(string)` and `RemovePart(string)`

Code generated against 1.x that calls a removed member fails at runtime, the first time that
path runs, not at build. #3's green CI only proves the paths the tests take. There is also no
security gain, because 1.22.2 already has the fix.

**Steps:**

1. **Bump both.** Move Jellyfin.Sdk to 2026.10.12 or the newest stable release; 2026.10.12
   depends on Kiota 2.1.2 for net8.0 and net10.0. Move all six Kiota packages to the newest 2.x
   at the same time, or drop the explicit Kiota references if the SDK's own minimums are free of
   advisories.
2. **Server compatibility.** Jellyfin.Sdk versions follow Jellyfin server releases. Check which
   server versions 2026.10.x supports against the lab server (`jellyfin.home:8096`) and the
   servers Aperture claims to support.
3. **Code changes.** Fix the build breaks from the regenerated client in `Aperture.Core`, and
   recheck Aperture's workarounds around the SDK's behaviour.
4. **Comment.** Rewrite the `Aperture.Core.csproj` comment for the new pairing.
5. **Check.** Run Aperture's tests, publish Native AOT with no new trim or AOT warnings, and smoke
   test against the lab: sign in, libraries, a detail page, playback, and Seerr requests.

**Done when** Aperture's tests are green, the AOT publish is clean, and the lab smoke test passes.

## 3. xunit.runner.visualstudio 4

**Status (2026-11-17): done on `claude/xunit-runner-4`, Windows. Linux and repeated CI runs
are still to come.**

- **Cause.** Runner 4 runs test bodies on thread-pool threads (`.NET TP Worker`); 2.x ran them
  on xunit's own dedicated threads (`.NET Long Task`). A wait on a `Task.Run` from a pool thread
  can take the task off that thread's own queue and run it inline, which happened in two runs of
  three in a probe. So "another thread" became the test thread. An explicit `maxParallelThreads`
  did not bring the dedicated threads back. Nothing in the product was wrong: a running app's UI
  thread is not a pool thread, so its `Task.Run` always reaches another thread.
- **Fixes.** `HeadlessSession` queues deferred work with `PreferFairness`, to the pool's global
  queue, as `Task.Run` from a real UI thread does, so its `WaitAll` cannot run producers inline.
  `tests/Shared/OtherThread.cs` runs a delegate on a thread of its own; it replaces the 24
  `Task.Run(...).Result`, `.Wait()` and `.GetAwaiter().GetResult()` hops in 14 test files.
- All 30 test projects are on `xunit.runner.visualstudio` `4.*`. `xunit` itself stays on `2.*`.
- **Full suite on Windows:** 7 failures before the fixes (the cross-thread set below, less
  Meridian and SSH, which passed that run by luck), 0 after.
- **Left:** the Linux suite, three green CI runs in a row on both OSes, and step 3 (central
  package management), which was not needed for this.
- **Not changed:** `SingleInstance.Send` and `PtyConnection.Start` hop the same way
  (`Task.Run(...).GetAwaiter().GetResult()`). That is safe from a real UI thread, and their tests
  pass under runner 4. But a caller on a pool thread that has a synchronization context could
  still have that hop run inline.

**What broke in #6** (CI run 43645282357, 2026-11-14). Every failure involves another thread:

- `ReactiveScopeThreadTests.ARead_OnAnotherThreadInsideAnEffect_DoesNotSubscribeIt`: expected 1,
  got 2. Fails on both OSes.
- `LoopWakeTests.APostFromAnotherThread_WakesTheLoop` and `ForcingAccessibilityClientsOn_WakesTheLoop`
- `DeferredAsyncLoadTests.AProducerRunsOffTheTestThread`
- `VirtualListItemLabelTests.ALabelReadOffTheUIThread_IsNeitherKeptNorTakenFromWhatIsKept`
- `RichTextTextOnRequestTests.ABridgeThread_GetsTheTextFromTheDocument_ThenFromTheLayout_AsAReaderKeepsReading`
- `WebViewControllerTests.EventsFromAnotherThread_AreAppliedOnTheUIThread_AtTheNextFrame` (Windows)
- `SshConnectionTests.OutputNotRead_HoldsTheServerBack` (Windows)
- `ResponseBodyWorkerTests.TheBodyIsTokenizedOnAWorkerNeverOnTheUiThread`, two cases (Windows)

The cause has not been found yet. A likely candidate: the new runner changes which thread, or
which synchronization context, a test body runs on, and these tests assume the old behaviour.

**Steps:**

1. **Find the cause first, on one test.** Start with `ReactiveScopeThreadTests`, the smallest.
   Log the test thread, `SynchronizationContext.Current` and the thread the work runs on, under
   runner 2.x and under 4.x.
2. **Decide the scope.** Either fix the assumption the tests share (preferred if it is one
   helper), or do this together with a move to xunit v3. v3 has new package names, test projects
   become executables, and runner 4 is built for it. `xunit` itself is still on `2.*` today.
3. **One line instead of 30.** About 30 test projects carry floating `2.*` references. Consider
   central package management (`Directory.Packages.props`) so the runner, xunit and
   Microsoft.NET.Test.Sdk versions each live in one place.
4. **Check flakiness.** Thread-sensitive tests can pass by luck. Require three consecutive green
   CI runs on both OSes, and check that Windows CI time does not grow (Junction.Tests is the long
   pole).

**Done when** the full suite is green three runs in a row on both operating systems, with no
test weakened to get there.

## Related, not a major

**ppy.SDL3-CS 2026.320.0 → 2026.1002.1** was taken out of #4 and is open on its own as
fredrikhu/hultui#8. The version number looks like a minor bump, but it is six months of
SDL `main` snapshots. Hosted CI runs no real windows, so before merging, check live resize, Snap
Layouts, HDR state, frame pacing and timer resolution on Windows, and Wayland on Linux.

**Status (2026-11-17): checked on `claude/sdl3-cs-2026-1002`.** Taken on our own branch, because
#8 again adds a PackageReference to `src/HultUI/HultUI.csproj` that does not belong there. The
`HdrInterop` spike moves with it.

- **What moves:** both packages carry SDL 3.5.0 from `main`. `SDL_GetRevision` reads
  `SDL-3.5.0-550394e` before and `SDL-3.5.0-ea7a2da` after: 1,168 SDL commits. It builds with no code
  changes.
- **The SDL changes on our paths, read:**
  - **Borderless windows:** SDL now swallows the caption painting of a borderless window on
    Windows (`WM_NCACTIVATE` with `lParam = -1`, and the undocumented `WM_NCUAHDRAWCAPTION` and
    `WM_NCUAHDRAWFRAME`; libsdl-org/SDL 6a9449c09). Our Windows custom frame never sets
    `SDL_WINDOW_BORDERLESS`: it keeps the native frame and subclasses the window procedure. So the
    change does not reach it.
  - **Activation:** `WM_NCACTIVATE` now takes the cursor position at that moment (0a8ccb11e), and
    the corner and border-colour DWM attributes are set only on Windows 11 (159bba9aa).
  - **Wayland's SDR white:** SDL now reports the output's reference luminance over 80
    (2a623fd22). HultUI does not use it there; it uses the 203-nit protocol constant
    (`WaylandScRgbReferenceWhite`), whose comment now says so.
  - **Timers:** both SDL3.dll import `timeBeginPeriod`, `timeEndPeriod` and
    `MsgWaitForMultipleObjects`, and read `SDL_TIMER_RESOLUTION`. 24404d904 fixes a timer overflow.
  - **Dependencies:** the new SDL3.dll no longer imports the Visual C++ runtime
    (`VCRUNTIME140.dll`, `api-ms-win-crt-*`). The Linux `libSDL3.so` needs only libc and libm.
  - **New, not taken:** `SDL_PROP_WINDOW_CREATE_WIN32_STYLE_EX_NUMBER` (5b02b2217) could set
    `WS_EX_NOACTIVATE` when a window is made.
- **Windows:** the full suite passes, 30 assemblies. With `HULTUI_MPV_PATH` set, `HultUI.UiTests`
  pass 153 of 168. The 15 skips are 14 tests that take the foreground on purpose and the
  display-change test, which needs a second monitor. No guard handed the foreground back. Among the
  passes: live resize inside the modal loop, frame pacing, the loop's waits and wake, the custom
  frame's activation and system menu (with the Snap Layouts band answering `HTMAXBUTTON`),
  placement, maximized and visible placement, Vulkan in 8-bit and half-float, HDR video, mpv, the
  software renderer, OS drops, IME for password fields, and the key map.
- **Native builds:** the five apps publish with no IL warnings. They were run on the desktop as for
  item 1 (Junction on GL and on software, Vesper Commander, Meridian, Aperture, and Reel with the PQ
  clip): no foreground taken, every saved frame right, and every resize drawn. Reel's HDR numbers
  match the old SDL exactly: scRGB swapchain, maximum 5.109 against SDR white at 3.00. Start-up,
  six alternated runs of the native Meridian each: median first frame 227.6 ms on the old SDL and
  229.7 on the new (minimum 221.1 and 222.5).
- **Linux:** `HultUI.CrossPlatform.slnf` passes in full on WSL Fedora (22 assemblies). Under WSLg,
  the TodoList sample opened and drew right on Wayland (`SDL_VIDEO_DRIVER=wayland`) and on X11: GL
  on llvmpipe, Vulkan on Mesa's Dozen over the RTX 4080, and software, `framebuffer=drawn` on both.
- **Not covered:**
  - KDE on Wayland with HDR. No such machine is here, and WSLg's compositor has no colour
    management.
  - The Snap Layouts flyout by hand; its hit test is covered.
  - A second monitor: only one was connected.
  - macOS.

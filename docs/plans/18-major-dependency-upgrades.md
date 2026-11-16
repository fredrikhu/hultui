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

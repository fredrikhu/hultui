# Round 4 survey: 4. .NET runtime

At 653971d; JIT and NativeAOT; real windows never focused; a per-phase byte probe and a dummy-driver
lost-wake harness. Probes reverted; worktree clean. Probe code in the session scratchpad `r4rt/`
(`host/` real-window host with latency, wakerace, wakerace2 modes; `run.ps1`, `build.ps1`; `wakerace/`
dummy-driver lost-wake proof; `blobprobe/`; `cmp.py`, `jitaot.py`, `jit-head-1.json`, `aot-head-1.json`,
`runs/`).

SATISFIED: no

## Review

- **aa11334 (with eba3197's wake coalescing, from before plan 17): a wake can be lost, and the post then
  waits for the next SDL event.** `SdlWindowingPlatform.PumpEvents` clears `_wakePending` before polling
  (SdlWindowingPlatform.cs:83); `SDL3Window.PollAllEvents` (SDL3Window.cs:760) ignores the wake event
  type, and input handlers run inside that poll loop (:834). A `Wake` landing between the clear and the
  poll's last `SDL_PollEvent` pushes an event the poll then consumes: the flag stays 1 with no event
  queued, and every later `Wake` returns early (:132) until the next `PumpEvents`. Under the old 16 ms
  idle cap that cost ≤16 ms; since aa11334 an idle loop with no deadline waits `Timeout.Infinite`, so a
  swallowed post waits for the next input or window event. Dummy driver, that interleaving: a wake posted
  50 ms into the wait ran to the full 2,000 ms cap in 3 of 3 runs; with the flag cleared after the poll,
  51 ms. Real never-focused GL window: 0 lost in 7,000 posted pairs and 600 pairs with a 2 ms input
  handler (any drawn frame's next pass clears the flag; only a pass that ends idle is exposed). The
  comment at :78-82 calling it harmless is stale.
- No other objections: all 122 suite rows JIT and AOT inside round 3's ranges and within 0.8 KB JIT of
  the dbcb7d8 capture; all five apps 0.0 loop passes/s idle, JIT and AOT; no blob leaks (3891499,
  8ddc3eb); thread safety holds (7ab0ba7, f878ec4, 6af2d0a, `UIThread` parking, the WinForms kick);
  6c726ea walks only on a display-scale change, 0 B; Reel, Aperture and VideoPlayer effects disposed on
  close and unmount; `ActiveChanged` unsubscribed in `Dispose`.

## Candidates

- **runtime-sdl-wake-flag-after-poll.** Clear the wake-coalescing flag where the poll consumes the wake
  event (or after the poll), not before it. Evidence above (3/3 waits ran the 2,000 ms cap vs 51 ms). The
  WinForms host already clears its flag when the kick is consumed (HultUIApplicationHost.cs:205-215);
  headless has no race. Correctness: closes an unbounded lost wake; no cost. Small; a dummy-driver test
  in SdlWindowingPlatformTests can pin it (the interleaving is reproducible step by step).
- **media-video-frame-bytes** (owner 4, with 3 reviewing; same as expert 3's
  media-vulkan-video-frame-bytes). Reel, 1080p24, Vulkan, quiet playback, real window, `HULTUI_A11Y=0`:
  527 B per drawn frame JIT, 551 B AOT (13.2–13.4 KB/s): `TryRenderFrame` 144 B (the lambda's display
  class and Action, GpuFrameSink.cs:102 — made at method entry, so early returns pay it); `WrapFrame` 216
  B (`GRBackendTexture` and `SKImage` wrappers and handle-table entries, SkiaVulkanRenderSurface.cs:340-364);
  layout 41–44 B (the elapsed label per position report); other render 28 B. The `GRBackendTexture` is
  never disposed: one finalizable object per video frame (24–120/s), its native object freed only by the
  finalizer. Churn 527 → ~170 B per steady video frame (−68 %); finalizable objects per frame 1 → 0.
  Small (per-frame inputs in fields with one cached Action; `texture.Dispose()` after `FromTexture`);
  medium if the `SKImage` wrapper also becomes a native handle.
- **tooling-textbench-warmup** (re-measured). Warm up until `JitInfo.GetCompiledMethodCount` stops
  rising; print methods compiled during measurement. At HEAD 922–928 compiled during measurement (1,301
  during warm-up), down from 990. Program.cs:46-55 unchanged. Tooling. Small.
- **test-frame-pacing-event-precondition** (re-measured). Make the storm thread's cadence independent
  of `Sleep(1)` (e.g. a 1 ms spin). Still fails: "the storm raised 516 events, not enough to test 202
  frames" on the 200.01 Hz display (606 needed, FramePacingTests.cs:36, :116-121); the other 3
  FramePacingTests pass under ForegroundGuard. Test reliability above ~170 Hz. Small.

## Examined, not worth it

- JIT vs AOT bytes: 101 of 122 rows within 1 KB, 20 more within ±4.5 %; the outlier `a11y/type beside a
  150-line diff, rows made in Build, bridge on` AOT +74 KB (+12.8 %) — the `ToString`-label anti-pattern
  row, same gap since 6902403.
- Idle in real windows (default frame, 20 s): Junction, Vesper, Meridian, Aperture (lab Jellyfin), Reel
  0.0 passes/s JIT and AOT; GL probe 0.0; minimized 0.1 (the probe's post); Vulkan without colour events
  12.0 passes/s (the 100 ms colour poll, 614–688 B per poll — only `HULTUI_RENDERER=vulkan` on a GL app);
  200 fps spinner GL 0 B/frame, Vulkan 24 B/frame (that poll).
- Native blobs and sub-blobs: 200 headless open/close cycles, 604,880 made and released; heap flat at
  4.86 MB from cycle 25, handles flat at 269, private +2.3 MB over 175 cycles; a window scrolled through
  20,000 unique spanned lines then closed keeps 0.2 MB native and 0.07 MB heap. **Lead for expert 2:**
  drawing a spanned line the first time makes 7.5 native blobs per line with sub-blobs vs 1.1 with whole
  runs (140,312 vs 20,359 over 455 screens; the sub-blob cache emptied 68 times); no suite row or app
  pays this on Windows (Cascadia Mono is variable, draws whole runs) — relevant to
  paint-subblob-variable-fonts.
- **idle-wake-latency-p90** (slow wakes over 4 ms per 80 posts, 500 ms apart, never-focused GL window,
  alternated under load):

  | Variant | Slow wakes |
  |---|---|
  | Round 2's 16 ms loop | 1, 3 per 80 |
  | Current loop | 6–7 per 80 in each of 6 runs |
  | Current + a no-op post every 16 ms | 3, 1 per 80 |
  | Not pinned to P-cores | 4, 4 per 60 |
  | EcoQoS (power throttling) off | 5, 5 per 60 |
  | 1 ms timer | 7, 5 per 80 |
  | UI thread at AboveNormal priority | 7, 6 per 80 |

  Only waking often removes it: the OS resuming a long-parked thread, not HultUI's wake path; fixing it
  would cost ~60 wakes/s. Recommend closing the watch item.
- table/TableView 10k sort AOT placement: allocation identical JIT/AOT (380.6 / 379.8 KB); ~1.3×10^5
  comparisons per sort, each 4 indirect calls (TableView.cs:164-169) — a tight loop whose AOT time
  follows code placement; an array copy would remove 2 interface calls per comparison, but only the
  Fluent sample uses TableView.
- **Lead for expert 7:** with a UIA client listening, Reel quiet playback spends 517 B per frame on a11y
  (13 KB/s, half the playback bytes): 3.0 full tree builds per second from the scrubber's 250 ms range
  refresh; a range-value patch like 3730052's would remove it.

## Needs the user

1. **idle-wake-latency-p90:** close it as OS behaviour, or have the user run the latency probe once with
   its window focused by hand (the only way to measure a focused window)?

# Round 2 survey: 4. .NET runtime

Counting-platform probe around real SDL windows (GL and Vulkan, unfocused), perf suite JIT
and AOT, headless retention probes; worktree clean at 6ba7e2d. Probe source and captures in
the session scratchpad `r2rt/`.

SATISFIED: no

## Review of round 1

- **6f42157 "an idle pass allocates 0 B" holds only headless.** In a real SDL window every loop pass allocates 88 B (JIT) / 72 B (AOT) inside `SdlWindowingPlatform.PumpEvents`: `SdlWindowingPlatform.cs:84` wraps the callback again per call (`w => onWindowClose(w)`), so the delegate cached at `Application.cs:374` does not help. → runtime-sdl-pump-closure.
- **d5d0c50 + 0178784: the shared caches are bounded by entries, not bytes,** and since d5d0c50 live as long as the application. After a TextArea closes, the heap keeps 4.3 MB (TODO.md) to 155 MB (17k × 1,000-char CSV). → text-shape-cache-byte-bound.
- Otherwise none: no suite row moved ≥ 10 % allocKB vs round 1's last captures (89 rows, JIT and AOT). Shared text services: every app `TextMeasure` caller is on the UI thread; workers only read files and post back.

## Candidates

- **runtime-sdl-pump-closure.** Pass `Action<IPlatformWindow>` through to `SDL3Window.PollAllEvents` (SDL3Window.cs:705) or cache the adapter. Per pass exactly 88 / 72 B inside PumpEvents, 0 B elsewhere; idle 5.7 KB/s; animating 3.0–4.0 MB/s with 1–3 gen0 + 0–1 gen1 per 6–10 s. Real GL steady frames → 0 B. Small.
- **runtime-paced-wait-spin** (co-owned with 3). SDL subtracts elapsed time and truncates to whole ms, so `WaitForEvents(1)` (Application.cs:451-452 → SdlWindowingPlatform.cs:101-104) returns at once and the loop busy-spins the last ~1 ms of every interval while anything is dirty. Spinner on a 200 Hz display: 41,281–46,008 loop passes/s vs ~200 frames/s (207–230 passes per frame, GL and Vulkan, JIT and AOT); every `wait(1)` returns without waiting; longer waits shortened ~1 ms; each spin pass pumps SDL, drains posts and ticks animations (41–46k ticks/s). Fix: round the timeout up. ~210 → 2–3 passes per frame (−99 %). `FramePacingTests` must still read 0.5–1.3× the display rate. Linux not measured. Small.
- **runtime-idle-wakeups** (confirmed). Idle GL: 64.3–65.0 passes/s (JIT/AOT, a11y on/off, TextInput focused), 0 frames, every pass a 16 ms timeout (Application.cs:459). Vulkan idle 183 B/pass; display-colour poll ~620 B per 100 ms (SDL3Window.cs:781, :1238-1243). WinForms host ticks every 8 ms (HultUIApplicationHost.cs:34, :146-151). Deadlines needed: trailing a11y refresh (Window.cs:787-788), resize throttle, inspector refresh, Vulkan colour poll, active animations (incl. delay tweens like the terminal's 530 ms blink). Explicit wakes needed: AccessibilityActionRouter (enqueues without waking), AT-SPI attach. Already wake: WM_GETOBJECT on Windows, InvokeOnMainThread, SynchronizationContext.Post. 65 → ~0 wakes/s (≤ 10/s on Vulkan). Medium. Build after the two above (same loop code).
- **runtime-palette-filter-allocs.** `CommandPalette.Filter` (CommandPalette.cs:177-204) joins each non-matching command's other text per keystroke, scores titles twice when a command has no other text, ranks through a 3-key LINQ OrderBy. overlay/command palette filter 2,000: 592 KB/op JIT, 651 AOT (not escape analysis). Prototype (text joined once per open; (score, length, index) tuples with Array.Sort): Filter 232/427 → 36/40 KB per query, identical lists, 1,411 fewer FuzzyMatch calls per keystroke; row ~592 → ~396 KB/op (−33 %). Small; public `Filter` keeps its signature.
- **text-shape-cache-byte-bound** (owner 5). Shape cache 16,384 entries (TextPipeline.cs:21), word cache 8,192 (:34-35); every plain Layout inserts its whole string (:272-278). Retained after closing a TextArea: TODO.md 4.3 MB; 19,000-line JSON 30.7 MB; 20,000 × 200-char log 52.3 MB; 17,000 × 1,000-char CSV 155.4 MB (~1.4 KB per entry + ~9.5 B per char). Probably part of apps-junction-heap-growth. Bound by bytes/glyphs (size-aware LRU) or keep TextArea lines out. −25…−150 MB retained. Small–medium.

## Examined, not worth it

- Steady frames hold: suite JIT and AOT 0 B/op on tiles, dense text, tweens, spinners; hover 0, Tab 0, slider 1, checkbox 1, TextInput 2 KB/op; unchanged with JIT stack allocation off; AOT within 1 KB of JIT on 93 of 94 rows.
- Real GL frame (200-row tree, spinner, a11y on): 0 B per frame beyond the pump. Vulkan ~2.6 KB per frame by design (fresh render target per frame, memory `vulkan-renderer` trap 2); lead for 3.
- Real SDL window open/close × 400 (UIA client listening): managed heap and handles flat, private bytes plateau. No leak.
- Small caches (digit tables, fallback cache, FocusTraversal array, frame ring, SlidingWindowMax): bounded.
- LayoutEngine.cs:1249 floors array for > 64 overflowing items: negligible. Aperture `_heroes.Find` and `Post` closures: once per Build/await.
- AOT publish: no trim or AOT warnings.
- Other experts' items re-checked: layout-zorder-sorted-allocs still 4 KB/op on scroll/sticky sections wheel; junction-lane-palette still present.

## Needs the user

None.

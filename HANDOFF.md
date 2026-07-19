# Session handoff — Linux AT-SPI export + benchmarks (2026-07-20)

Temporary document. Delete once absorbed. (This replaces the earlier
headless-mode handoff, whose items are all done and folded into `TODO.md`.)

## Status

All work is pushed on branch **`linux-atspi-export`**; working tree clean.

```
8c09c60 Update Linux benchmark numbers after driver upgrade
889dc62 AT-SPI: confirm child reorders need no signal (served from live snapshot)
7aead0f AT-SPI: report Component extents in device pixels
cc64c46 AT-SPI: implement Action.DoAction via a shared action router
b061154 AT-SPI: emit Cache add/remove signals and fix the startup root race
4176013 Capture Linux benchmark numbers in the sample README
9b33210 Implement and verify the Linux AT-SPI2 accessibility export
```

Local verification on Fedora: **626 tests green** (`dotnet test HultUI.CrossPlatform.slnf -c Release`; 496 Core + 1 skipped, 42 Headless, 31 Text, 24 Linux-a11y, 20 Skia, 7 TodoList, 6 SourceGen). Build clean apart from the pre-existing `CS9191`.
**The Windows-only FlaUI suite has NOT been run against this branch** — see below.

## Check this first (Windows session)

1. **FlaUI re-verification — the important one.** `cc64c46` refactored the Windows
   UIA *action* path: the invoke/toggle/select/set-value/focus logic + the pending-
   action queue + per-frame drain were lifted out of the Windows-only
   `Sdl3UiaHostBridge` into a shared `AccessibilityActionRouter` (`src/HultUI/`),
   which both the Windows UIA host and the Linux AT-SPI bridge now use. It is
   behavior-preserving by construction (pure delegation, same queue semantics), and
   `Window` now drains that router each frame on **all** platforms (was Windows-
   gated) — identical behavior on Windows. But only FlaUI proves it. Run
   `dotnet test tests/HultUI.UiTests` (net10.0-windows, real SDL windows) and
   confirm UIA invoke/toggle/value/focus still round-trip. `7aead0f` also added
   `IAccessibilityGeometryHost` (router implements it) — Linux-only consumer, no
   Windows behavior change.

2. **Windows CI / pipelines.** `junction-installer.yml` builds `HultUI.slnx` on
   windows-latest and runs `dotnet test HultUI.slnx --filter !~HultUI.UiTests`.
   This branch adds a test project (`HultUI.Platform.Accessibility.Linux.Tests`) to
   `HultUI.slnx` **and** `HultUI.CrossPlatform.slnf`, plus a new
   `HultUI.Core.Accessibility` interface and the router. All compile cross-platform
   (`CA1416` verified 0 warnings on Linux; that analysis is host-independent). The
   24 new pure role-mapping tests will also run on the Windows leg. Confirm green.

3. **Windows benchmark rerun.** Re-run
   `dotnet run -c Release --project samples/HultUI.Samples.Benchmark -- --auto`
   (and `--auto --virtual`) on Windows, **on this same commit**, for a clean
   Windows-vs-Linux comparison — see the benchmark note below.

4. **Linux CI leg**: confirmed green locally (the `.slnf` resolves on a case-
   sensitive FS, CRLF-baseline fix holds under LF).

## What was done on Linux (AT-SPI2 export)

The top open item is done: `LinuxAccessibilityBridge` is a working AT-SPI2
application, verified on a live GNOME/Wayland a11y stack with **pyatspi (Orca's
client library)**.

- Discovers the a11y bus (`org.a11y.Bus.GetAddress`), registers via `Socket.Embed`,
  serves the whole tree from one `IPathMethodHandler` over
  `/org/a11y/atspi/accessible/*` + `Cache.GetItems`. Interfaces: Accessible,
  Component, Application, Action, Properties, Peer, Introspectable. Signals:
  children-changed, state-changed (focused/checked/enabled), property-change, and
  Cache add/remove. Served from an immutable per-rebuild snapshot (bus-thread
  handlers read it lock-free).
- **`Action.DoAction` works** — click/toggle/select route through the shared
  `AccessibilityActionRouter` onto the UI thread. Verified live (`do_action` on the
  button incremented the counter, on the checkbox toggled it).
- Component extents are device pixels (bounds × display scale). Under Wayland
  absolute screen coords are unavailable, so SCREEN/WINDOW coordType both give
  window-relative pixels — that's the correct answer here, not a gap.
- Reorders need **no** signal — every query is served from the fresh snapshot, so
  child order is always current (A/B verified with a persistent pyatspi client).
- Verification harness: `samples/AtSpiVerify` drives the bridge **headlessly** (no
  display) via `HeadlessWindow.InstallAccessibilityBridge`/`InstallActionRouter`;
  `atspi_inspect.py` / `atspi_action.py` / `atspi_reorder.py` read it back with
  pyatspi. Re-verify needs a running `at-spi2-registryd`.

The scaffold's AT-SPI numeric role codes were **guesses and wrong** (push button
25→43, check box 45→7, text 40→61); corrected against a live GTK app and pinned by
the 24 unit tests. Exactly the class of bug on-desktop verification exists to catch.

## Benchmark note

The Linux README numbers (2026-07-20) were captured **after a `dnf upgrade`** that
healed the GPU present path (the earlier run logged `libEGL: failed to create dri2
screen` and fell back). Effect: present-bound in-place ops dropped sharply (table
`select` 1.3→0.4 ms; virtual `select`/`swap` ~3–4.5→~0.6 ms), while CPU-bound bulk
`create` moved up (`create 1,000` ~10→~17). The two Linux captures (07-19 pre-,
07-20 post-upgrade) are therefore **not** comparable to each other either.

For a real Windows-vs-Linux number, run `--auto` on Windows **on this commit** —
same binary, same GPU. Compare within a single session only; cross-session numbers
are noise. `SDL_WINDOW_ACTIVATE_WHEN_SHOWN=0` opens the window without stealing
focus. **Any layout-engine change should be A/B'd with the benchmark** — unit tests
did not catch a viewport-collapsing regression.

## Decisions and dead ends not recorded in the repo

- **Do not change the `FlexShrink = 1` default.** Fixed-height items shrinking in a
  bounded overflowing column is correct CSS. The fixed bug was narrower: a scroll
  container's indefinite main axis not propagating to nested containers.
- **The min-content clamp was attempted and reverted.** Do not approximate CSS
  `min-height:auto` from the flex Phase 1 measurement — for a container that is its
  full content extent, so an ancestor wrapping a scroll view gets pinned to the
  whole scrollable height (14,640px tall, 1007 draw calls vs 47). All unit tests
  passed with the bug; only the benchmark caught it. A correct fix needs a real
  recursive min-content pass where scroll containers contribute zero. Add a
  shrink-heavy benchmark first — the current suite barely exercises the path.

## Traps hit (durable)

- **Baselines in raw string literals** carry the source file's line endings (CRLF on
  Windows, LF on Linux with autocrlf), so a baseline can pass one CI leg and fail
  the other. Normalise with `ReplaceLineEndings("\n")`.
- Inside `HultUI.*`, bare **`Text` binds to the `HultUI.Text` namespace**, not the
  builder. Alias `using Text = HultUI.Core.Builders.Text;` *inside* the namespace.
- `grep "warning CS"` **does not match `warning CA1416`** — platform-analyzer
  warnings hide from that filter.
- **AT-SPI specifics (new):** `Tmds.DBus.Protocol`'s `MessageWriter`/`Reader` are
  **ref structs** — can't be a `using` variable and passed by `ref` at once
  (dispose by hand). Never guess AT-SPI role numbers — introspect a live GTK app
  with `busctl`. The registry probes the app root *during* the `Embed` round-trip on
  the receive thread, so the root must be seeded into the snapshot **before** Embed
  or that probe answers `UnknownObject` (caught only by watching the raw bus, not
  pyatspi). **Verify with two lenses**: a real client (pyatspi) for tree/semantics
  and `busctl monitor` on the raw bus for wire-format/timing — each caught bugs the
  other missed.

## Open follow-ups (in TODO.md)

1. **FlaUI re-verification** of the shared-router refactor (Windows-only; item 1
   above). This is the one real risk from the Linux work.
2. **Orca speech pass** — largely covered since Orca reads via the same pyatspi API
   already verified; the gap is Orca-specific event handling and actually listening.
3. The `state-changed:defunct` a cold-start event monitor shows is a client caching
   artifact, not app-fixable. Reorder `children-changed` only if an Orca test proves
   a child-order-caching client needs it (`atspi_reorder.py` reproduces reorders).

## Memory that does not transfer

The Linux session's assistant memory (the AT-SPI bridge notes) is keyed by project
path under `~/.claude/projects/.../memory/` and won't be visible from Windows. The
durable facts are reproduced above and in `TODO.md`; the Windows-side memory files
from before are still where you left them.

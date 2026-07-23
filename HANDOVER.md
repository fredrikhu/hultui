# RESOLVED: Linux vs Windows GL / benchmark performance investigation

*Opened 2026-07-20 on Windows; concluded 2026-07-23 on the Fedora side. Both
questions it raised are answered below. **Windows session: once you've read this,
this file can be deleted** — the durable numbers now live in
`samples/HultUI.Samples.Benchmark/README.md` (the "Linux capture (2026-07-23)"
section).*

## What was asked

1. The **benchmark suite** had Linux looking slightly *ahead* on an old capture,
   but a same-commit re-capture was queued to confirm after the 2026-07-23
   optimizations.
2. The **Junction GitClient** got ~2× the fps on Windows; the gap was in the GL
   path (Present, and Render 0.2–0.3 ms Windows vs 1–1.5 ms Linux).

## Finding 1 — the benchmark gap was the accessibility bridge

Machine confirmed to be the **same box dual-booting** (RTX 4080 both sides), so
it's a clean OS comparison.

The naive same-machine benchmark showed Linux *slower* on bulk ops (create/append
/clear). Root cause: on any live desktop the **AT-SPI bus is reachable, so the
Linux a11y bridge eager-connects and does per-frame work** during the benchmark.
A headless CI box has no bus and benches a11y-free; the Windows column runs with
an active background-UIA bridge. Apples-to-oranges until the a11y state is pinned.

Pinned via the new **`HULTUI_A11Y=0`** master switch:

- **Framework CPU (a11y off, both OSes): Linux is faster.** create-10,000 floor
  110–126 ms on Linux vs the Windows bridge-off floor of 146 ms. The handover's
  premise ("`.NET` + framework fine on Linux") holds — Linux is ahead.
- **`--profile` proved the a11y cost is *outside* every rendered pass**: with the
  bridge on vs off, build/style/layout/render/present are identical (~120 ms for
  create-10k both ways); the ~110 ms difference was pure D-Bus emission, showing
  up only as +~130 MB frame alloc and 2–3× the GC.

### Fix shipped: AT-SPI event coalescing (mirrors the Windows UIA change)

The Linux bridge emitted **one `object:children-changed` per added/removed node**
— redundant on a bulk change, since the per-node `Cache.Add/RemoveAccessible`
signals already carry the node set and a client re-reads a parent's children from
the fresh snapshot. It now emits **one children-changed per affected parent**
(`TreeDiff.BoundaryAdded/BoundaryRemoved` + dedupe by parent in
`LinuxAccessibilityBridge`). **Verified on a live `pyatspi` client** (Orca's
stack): it tracked a 0⇄200 child cycle exactly while receiving one signal per bulk
op (`samples/AtSpiVerify` bulk mode + `atspi_bulk_verify.py`).

Effect (a11y active): `clear 1,000` 9.3 → 2.85 ms; `create 10,000` shed its
~27 ms children-changed storm. The **per-node cache-add is deliberately kept** —
AT-SPI is push-based and that ~60 ms on create-10k is real cache-warming that
makes screen-reader navigation instant (UIA, being pull-on-demand, simply never
pays it). So Linux `create`/`append` stay above Windows *with a11y on*; that's a
protocol difference, not a regression.

> Note: the old suggestion to equalize with `HULTUI_UIA_EVENTS=0` on Linux was
> wrong — that var is Windows-only. Use `HULTUI_A11Y=0` (new, all platforms).

## Finding 2 — the GL / present gap no longer reproduces

Steady-state GitClient trace (~15 k continuous frames via the new
`HULTUI_TRACE_MS` capture hook): **Render 0.18 ms median, Flush 0.09 ms,
Present 0.02 ms**, ~2,990 fps uncapped. Present is nowhere near a 16.6/8.3 ms
vsync wait (so `swapInterval=0` genuinely holds), and Render now matches the
Windows reference (0.2–0.3 ms), not the 1–1.5 ms measured on 2026-07-20. The old
gap was the broken `dri2`/EGL fallback the README's 07-20 note mentioned; a
driver/stack update (now NVIDIA 610.43.03) healed it. gfx line:
`video=wayland renderer="NVIDIA GeForce RTX 4080/PCIe/SSE2" gl="3.3.0 NVIDIA
610.43.03" swapInterval=0 samples=0`.

## Code changes in this branch

- `src/HultUI.Core/Accessibility/TreeDiff.cs` — `BoundaryAdded()`/
  `BoundaryRemoved()` (+ tests in `TreeDiffCoalescingTests`).
- `src/HultUI.Platform.Accessibility.Linux/LinuxAccessibilityBridge.cs` —
  coalesced children-changed; per-node cache add/remove kept.
- `src/HultUI/AccessibilityBridgeFactory.cs` — `HULTUI_A11Y=0` master off switch.
- `samples/AtSpiVerify/` — bulk-cycle mode + `atspi_bulk_verify.py` (live-client
  verification of the coalescing).
- `samples/HultUI.Samples.GitClient/Program.cs` — `HULTUI_TRACE_MS` continuous-
  frame trace-capture hook (SDL3/Wayland won't turn a signal into a clean quit).

Tests green: Core 508, Headless 46, Accessibility.Linux 24, TreeDiffCoalescing 10.

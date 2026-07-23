# Handover: Linux vs Windows GL performance investigation

*Written 2026-07-20 on the Windows machine, for a Claude Code session on the
Fedora side. Self-contained — the Windows session's memory is not available on
Linux. Delete this file when the investigation concludes.*

## The observation

- The **benchmark suite** (`samples/HultUI.Samples.Benchmark --auto` / `--profile`,
  CPU-side phases) has **Linux slightly ahead** — .NET + framework code are fine.
- The **Junction Git app** (`samples/HultUI.Samples.GitClient`, Release) gets
  **~2x the fps on Windows**. The gap is in the GL path:
  - **Present** is the primary difference (exact numbers not recorded — measure).
  - **Render** phase: **0.2–0.3 ms Windows vs 1–1.5 ms Linux**.

So: CPU-side framework ✅, GL submit + swap ❌. Driver/compositor territory.

**Unresolved variable:** confirm with Fredrik whether the Fedora setup is the
same machine dual-booting or different hardware. Different GPUs would reframe
everything below.

## New tooling (commit adding this file)

Every real window now prints one line at creation and stamps it into traces:

```
[HultUI] gfx: video=windows renderer="NVIDIA GeForce RTX 4080/PCIe/SSE2"
  vendor="NVIDIA Corporation" gl="3.3.0 NVIDIA 610.62" swapInterval=0 samples=0
  drawable=2160x1395px window=2160x1395 density=1 scale=1.5
  os="Microsoft Windows 10.0.26200"
```

↑ That is the actual **Windows reference line** (Debug run, 2026-07-20; config
doesn't differ Debug/Release). Note: `swapInterval=0` *achieved*, no MSAA,
display scale 1.5, drawable 2160×1395.

Implementation, if it needs touching: `SDL3Window.DescribeGraphics()`
(`src/HultUI.Platform.SDL3/SDL3Window.cs`), default-null member on
`IPlatformWindow`, logged/stamped in `src/HultUI/Window.cs` (ctor + F11 path),
`FrameTracer.Annotate` records it as a `cat="Meta"`, `name="Graphics"` event.

## Step 1: capture the Linux gfx line

```bash
dotnet build HultUI.slnx -c Release
HULTUI_TRACE=/tmp/junction-linux.json \
  dotnet run --project samples/HultUI.Samples.GitClient -c Release --no-build
```

Compare field-by-field against the Windows line above. Each field is a
hypothesis-killer:

| Field | If it differs from Windows… |
|---|---|
| `renderer="llvmpipe…"` / `"softpipe"` | **software rendering** — case closed, fix GL context creation/driver install |
| `swapInterval=1` (or `?`) | swap is vsync'd — the fps gap is a cap, not a cost. `SDL_GL_SetSwapInterval(0)` failed silently (common on Wayland/EGL) |
| `samples=4/8/16` | unrequested MSAA framebuffer — Mesa config pick; explains slower Render+Present |
| `drawable=` much larger | HiDPI scale mismatch — comparing different pixel counts |
| `video=wayland` vs `x11` | present semantics differ entirely between the two — test both (below) |

## Step 2: trace, and split waiting from working

The trace (`HULTUI_TRACE=<path>`; saved on window close) is Chrome-trace JSON —
load in ui.perfetto.dev. Phases are `cat="Phase"`: Style/Layout/A11y/Render/
Flush/Present. The `Graphics` meta event is at t≈0.

- **Present ≈ (frame budget − everything else)** → it's a vsync/compositor
  *wait*, not overhead. Fix is configuration.
- **Present large and variable, uncorrelated with 16.6/8.3 ms** → real
  compositor/driver cost.
- GPU stalls show up in **Flush**; subtract Flush+Present for CPU-side
  comparisons with the Windows numbers.

## Step 3: experiment matrix

One variable per run; keep the gfx line + a trace per run. Suggested order:

```bash
# A. Kill any vsync at the driver level
vblank_mode=0 dotnet run ...          # Mesa (AMD/Intel)
__GL_SYNC_TO_VBLANK=0 dotnet run ...  # NVIDIA proprietary

# B. Backend comparison — SDL3 picks per distro; force each explicitly
SDL_VIDEODRIVER=x11 dotnet run ...
SDL_VIDEODRIVER=wayland dotnet run ...

# C. Mesa threaded dispatch — often a big win for many-small-GL-calls
#    profiles like Skia/Ganesh; directly targets the 1–1.5 ms Render phase
mesa_glthread=true dotnet run ...

# D. Minor, try last
MESA_NO_ERROR=1 dotnet run ...
```

Expected outcomes: A or the gfx line resolves the fps gap (throttling); C is
the most likely lever on the Render-phase delta if the renderer string shows
Mesa. If NVIDIA proprietary on Linux, per-call dispatch overhead vs the Windows
driver is a known cost with no great env-var fix.

## Repo facts you'd otherwise rediscover

- Build/test: `dotnet build HultUI.slnx`; `dotnet test HultUI.CrossPlatform.slnf`
  is the Linux-suitable test set (skips Windows-only FlaUI UiTests). The
  `/testing` skill (`.claude/skills/testing/SKILL.md`) has the full map.
- Benchmark: `samples/HultUI.Samples.Benchmark --auto` (scripted, closes
  itself), `--profile` prints per-phase + GC stats. Use it for the CPU-side
  sanity check on the same machine/session. Pull latest before capturing:
  as of 2026-07-23 the scripted runs disable the F12 HUD (historical numbers
  on both OSes had it on; measured impact was within noise for these
  single-frame op latencies, but keep configurations identical), and the
  text pipeline gained a word-level shape cache that changes absolute
  create-row numbers vs older recordings.
- F11 toggles tracing with keyboard focus (saves to `/tmp/hultui-trace.json`);
  the env var records from startup and needs no focus.
- Vsync is intentionally off in code: `SDL_GL_SetSwapInterval(0)` in
  `SDL3Window` ctor — but verify the *achieved* value via the gfx line.
- Skia targets the default framebuffer, `GL 3.3 core` requested, stencil 8
  (`SkiaGLRenderSurface.cs`); Present = Skia flush + `SDL_GL_SwapWindow`.

## Report back

Record findings (gfx lines, per-phase numbers, which experiment moved what) in
this file or a follow-up doc, commit, and push — the Windows-side session picks
it up from git. If a code change falls out (e.g. EGL preference, swap-interval
retry, glthread hint), normal PR flow.

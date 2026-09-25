# Verifying the four Linux fixes

Four findings from `docs/design/HDR-LINUX-BRIEF.md` were fixed on the Windows machine in commit
`00e9a24`. **Three of them are on code paths that Windows cannot execute at all** — two Wayland,
one X11 — so nothing about them has been run. This is how to find out whether they work.

Hand this file to a Claude Code session on the Linux machine. It assumes the same setup as the
brief: NVIDIA proprietary driver, KDE Plasma, two monitors at scale 1.0 and 1.5, both HDR-capable.

```bash
git pull && git log --oneline -1     # expect 00e9a24 or later
dotnet build src/HultUI.Platform.SDL3 samples/HultUI.Samples.Minimal samples/HultUI.Samples.VideoPlayer
```

**Before anything, clear the overrides.** `HULTUI_VULKAN_WHITE_SCALE` set in a shell from the
earlier session would make fix 1 pass for the wrong reason, which is the single most likely way
to get a false green here:

```bash
env | grep -i HULTUI
```

## Fix 1 — the dim scRGB window

**What changed.** `SDL3Window.WhiteScale()` now returns the protocol constant **2.5375** when the
video driver is `wayland`, instead of going through `SdrWhiteLevelNits` — a Windows query that
answers a flat 80 everywhere else and left the scale at 1.

**This is the one that cannot be checked from a file.** The F16 and 8-bit captures agreed within
1 LSB when the window was visibly dim, because the image was always right and only the scale was
wrong. **A capture passing proves nothing. Only the screen does.**

First confirm the number is reaching the host, with nothing forced:

```bash
HULTUI_RENDERER=vulkan HULTUI_STARTUP=1 dotnet run --project samples/HultUI.Samples.Minimal
```

Expect `whiteScale=2.54` in the `vulkan display changed` lines and a
`R16G16B16A16Sfloat/ExtendedSrgbLinearEXT` swapchain. If it still says `whiteScale=1`, the driver
string is not `wayland` — print `SDL_GetCurrentVideoDriver()` before going further.

Then the actual test, which needs a person looking at two windows:

```bash
dotnet run --project samples/HultUI.Samples.Minimal &                      # GL
HULTUI_RENDERER=vulkan dotnet run --project samples/HultUI.Samples.Minimal # Vulkan, scRGB by itself
```

**Pass:** side by side on the same monitor, the two windows look the same — the white background
in particular. **Do this on both DP-2 and DP-3**, because matching on both is the whole claim: the
scale is fixed rather than proportional to the monitor's brightness, and if it were proportional
one of the two would be off.

**Fail, and what it would mean.** Still dim: the NVIDIA WSI is probably not presenting
`ExtendedSrgbLinearEXT` as Windows-scRGB after all, and the number would have to come from the
compositor instead — that reopens the question the protocol text looked like it had closed. Too
bright: 2.5375 is being applied on top of something else. Matches on one monitor and not the
other: the scale is proportional after all, and the earlier by-eye result was confounded by the
primary/secondary swap noted in the brief.

Then check the other direction still works:

```bash
# with the monitors switched out of HDR mode
HULTUI_RENDERER=vulkan HULTUI_STARTUP=1 dotnet run --project samples/HultUI.Samples.Minimal
```

**Pass:** `hdrDisplay=off`, a `B8G8R8A8Unorm` swapchain, and the window looks like the GL one. The
white scale is irrelevant on an sRGB swapchain — the surface ignores it — so a wrong value here
should be invisible. If the SDR window changed appearance, the scale is leaking into the 8-bit
path and that is a new bug.

## Fix 2 — the X11 rebuild loop

**What changed.** `SdlVulkanHost.DisplayChanged` asks what format `ChooseFormat` would actually
produce and compares that against `_format`, instead of comparing what was *wanted* against what
was *obtained*. Those two can never agree on a surface that has no wide format to give.

```bash
SDL_VIDEODRIVER=x11 HULTUI_RENDERER=vulkan HULTUI_VULKAN_F16=1 HULTUI_STARTUP=1 \
  dotnet run --project samples/HultUI.Samples.Minimal 2>&1 | grep -E "swapchain|display changed|wide colour"
```

**Pass:** one `vulkan swapchain` line at start-up (plus one per real resize), every
`display changed` line saying `format unchanged`, and **at most one** `wide colour asked for,
surface has none`. Before the fix this printed three rebuilds at start-up and repeated the wide
colour line each time.

**Fail:** repeated `rebuilding for scRGB` means the comparison is still wrong. If instead the
swapchain now *never* rebuilds when it should, check the Wayland case below — that is the
regression this fix could have introduced.

**The regression check that matters more than the fix.** Dragging a window between an HDR and a
non-HDR monitor must still change the format:

```bash
HULTUI_RENDERER=vulkan HULTUI_STARTUP=1 dotnet run --project samples/HultUI.Samples.Minimal
```

Put one monitor in HDR mode and the other in SDR, then drag the window across. **Pass:** the trace
shows `rebuilding for scRGB` one way and `rebuilding for sRGB` the other, and the window stays
looking correct on both. This is phase 3's whole behaviour and the fix touches the code that
decides it.

## Fix 3 — the mixed-scale Wayland shrink

**What changed.** The `scaleOnlyChange` branch of `SDL3Window.UpdateSize` no longer runs on
Wayland. **This affects the GL path too**, so it is the one change here that an ordinary Linux
app would notice, and it is the one most worth trying to break.

```bash
for i in 1 2 3 4 5 6; do
  dotnet run --project samples/HultUI.Samples.Minimal &
  sleep 4; kill %1 2>/dev/null
done
```

**Pass:** the window is 500x300 logical every time, never 333x200. The brief recorded 6 out of 6
launches shrinking before the fix, so 6 clean launches is a real signal rather than luck. Check
with `kscreen-doctor -o` for which output it landed on, or just look.

Repeat with `HULTUI_RENDERER=vulkan`, and read the size off a `HULTUI_STARTUP=1` trace
(`vulkan swapchain <w>x<h>`) if looking is ambiguous.

**Now the regression.** Removing DIP preservation on Wayland is only right if the compositor
really does preserve the logical size across an output change. Drag a window slowly from the 1.0
output to the 1.5 output and back, by hand, on **both renderers**:

- **Pass:** the window keeps its apparent physical size and the UI keeps its proportions. Text
  and the button scale with the monitor, the layout does not reflow to a different number of DIPs.
- **Fail:** the window doubles or halves in apparent size when it crosses, or the content jumps
  to a different logical size. That would mean KWin does *not* preserve logical size here and the
  branch was doing something real after all — in which case the right fix is narrower: keep the
  branch but make it use the scale from the same event as the pixels, rather than skipping it.

Also worth one run on X11, where the branch still runs and must be unchanged:

```bash
SDL_VIDEODRIVER=x11 dotnet run --project samples/HultUI.Samples.Minimal
```

## Fix 4 — the capture comment

Documentation only: `SdlVulkanHost.Capture.cs` no longer claims its default frame is reachable in
an app that renders only when dirty. Nothing to run. If you want to confirm the behaviour it now
describes, launch Minimal with `HULTUI_VULKAN_CAPTURE=/tmp/idle.ppm`, touch nothing for ten
seconds, and confirm no file appears — then hover the button and confirm one does.

## Still open: the fd-per-import leak

The brief's fd check could not have found a leak. The shared image is sized to the **video**, not
the window, so 159 window resizes re-exported nothing — `glImportMemoryFdEXT` ran exactly once.

**A resolution change is what re-exports it**, and there is an easy way to cause one: open a
second file of a different size in the same window. `LendInteropImage` sees a different size,
calls `DestroyInteropImage`, and creates and imports a fresh one.

Make two clips of different sizes (the brief has the generator; run it twice with different `w`
and `h` — say 320x180 and 1280x720), then open them alternately in the running player through
**Open…**, twenty times or so, sampling the descriptor count as you go:

```bash
HULTUI_RENDERER=vulkan dotnet run --project samples/HultUI.Samples.VideoPlayer -- /tmp/small.y4m &
pid=$!
while kill -0 $pid 2>/dev/null; do ls /proc/$pid/fd | wc -l; sleep 2; done
```

**Pass:** the count is flat across the swaps. **Fail:** it climbs by roughly one per swap, which
would mean `DestroyInteropImage` is not releasing the imported memory object, or that the fd
needs closing on this driver after all despite `glImportMemoryFdEXT` being specified to consume
it. Confirm the swaps are really happening by watching for a second
`vulkan interop image WxH RGBA16F` line per change in `HULTUI_STARTUP=1`.

## What to send back

Same as before: append results to this file under a **Results** heading and commit, rather than
pasting. Say which monitor and which display server each observation came from — the brief's one
ambiguity was a by-eye result whose monitor had been swapped by KDE without anyone noticing.

For the by-eye checks in fixes 1 and 3, **say plainly that a person looked**, and say who looked
at what. A capture that agrees is not evidence for either of them, and reporting one as though it
were is the specific mistake this whole exercise has already made once.

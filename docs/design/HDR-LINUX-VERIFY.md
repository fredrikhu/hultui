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

**The regression this could have caused is already ruled out — on Windows.** Comparing against
what `ChooseFormat` would produce could have stopped the rebuild happening at all, which reads as
a fix for the X11 loop and is not one. With one monitor in SDR and one in HDR, a window moved
between them four times gave exactly one rebuild per transition in both directions and
`format unchanged` for every other display event; with a video playing across the same moves, the
shared interop image was created once, survived three swapchain rebuilds, and the frame after two
format switches was identical to the never-switched one. Details in
`spikes/HdrInterop/results/windows.txt`.

So the Linux run only needs the X11 half above. If you want the monitor-move check anyway, the
same recipe works — but **use a video, not Minimal**: the default capture frame is the first that
reuses a swapchain image and an idle window never reaches it, so an idle window writes captures
only from swapchains it has already left.

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

## The fd-per-import leak — now one command

**Windows has answered its half.** `--churn` does a thousand export-import-release cycles and
counts handles: no growth, against a control of 200 dropped handles that grows by exactly 200.
Run the same thing for the descriptor side:

```bash
dotnet run --project spikes/HdrInterop -- --churn
```

**Pass:** `VERDICT: 1000 released cycles cost no handles`, *and* the line above it saying the
control climbed. **If the control did not climb, the run says nothing** — the tool prints that in
those words rather than reporting a pass, because two earlier versions of this test reported
clean while being unable to fail.

On Linux the control exports a descriptor and never imports it, since `glImportMemoryFdEXT`
consumes the one it is given and there is nothing to withhold from it. A rise in the released
batch would mean the import is *not* consuming the descriptor on this driver, which is the thing
worth knowing.

One Windows finding that may or may not hold there: **NVIDIA returns the same Win32 handle for
repeated exports of one `VkDeviceMemory`**. The test therefore reallocates each cycle, as the
renderer does on a size change. If Mesa or NVIDIA-Linux hands back a fresh fd per export of one
allocation, that difference is worth recording.

## Also still open, and not solved by the above

The `--churn` run above tests the API sequence, not the renderer's own lifecycle. Exercising
`DestroyInteropImage` + `CreateSharedImage` through the app needs a **resolution change**, which
means a second file of a different size in the same window — `LendInteropImage` sees the new size
and rebuilds. Reel plays the folder, so a directory of clips whose sizes alternate will do it,
provided the files carry an extension Reel accepts (`.mkv`, `.mp4`, …; a `.y4m` is ignored by the
playlist and you get one clip and no transitions). Tried on Windows: it advanced once and then
stopped, so the app-level version of this remains unproven on both platforms and the `--churn`
result is what stands.

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

## Phase 5 — HDR video, which is now on by default

Added after the four fixes above and **never run on Linux**. It is the one item here that
changes what an ordinary Vulkan window does without anyone opting in, so it is the one most
worth breaking.

`MpvBackend` now sets `target-prim=bt.709`, `target-trc=linear` and `target-peak` whenever the
renderer lends an scRGB target, and the frame is composited as linear light instead of an
encoded picture. `HULTUI_VIDEO_HDR=0` turns it off.

**You need an HDR file.** Confirm what it is before drawing conclusions from it:

```bash
dotnet run --project spikes/HdrInterop -- --probe /path/to/film.mkv
```

**`pq` or `hlg` is HDR**; `bt.1886` and friends are SDR whatever the filename says. Prefer one
that reports a real `max luma`, since `target-peak` is otherwise assumed. The file used on
Windows was 2001 (`pq`, `bt.2020`, max luma 1000.6 nits) from `\proxmox.home\Media`.

### 5a. Does the range reach the swapchain?

```bash
HULTUI_RENDERER=vulkan HULTUI_STARTUP=1 HULTUI_VIDEO_HDR_PROBE=120   HULTUI_VULKAN_CAPTURE=/tmp/hdr.ppm HULTUI_VULKAN_CAPTURE_FRAME=600   dotnet run --project samples/HultUI.Samples.VideoPlayer -- /path/to/film.mkv --start=1200
```

Two numbers come out, and they answer different questions. `[interop] sample n/6` is read
through GL straight after mpv renders — what the **producer** wrote. `[vulkan] scRGB range` is
read at capture time, after Skia composited — what reached the **swapchain**. Windows, on a
1000-nit film:

| | shared image | swapchain (SDR white 3.00) |
|---|---|---|
| default | **4.926** | **12.492**, tens of thousands above white |
| `HULTUI_VIDEO_HDR=0` | 1.000 | 3.000, none above |

**Pass:** the shared image exceeds 1.0 and the swapchain exceeds its SDR white. 4.926 is
1000/203 — the mastering peak against libplacebo's reference white — so a 1000-nit film should
give roughly that whatever the machine.

**Fail, and what each means.** Shared image at 1.0: mpv is not honouring the target options on
this driver, and the spike (`--range`, below) will say so more cheaply. Shared image above 1.0
but swapchain at the white scale: the loss is in Skia's compositing, which on Windows it is not.

**Read six samples, not one.** A film has plenty of black frames and one reports a maximum of
0.000, which reads exactly like a producer writing nothing. Three separate wrong conclusions were
drawn from that on Windows, including twice after the lesson had been written down.

### 5b. The Wayland prediction, which is the interesting part

On Wayland `WhiteScale` is **2.5375 = 203/80**, and libplacebo puts its linear 1.0 at **203
nits**. Those are the same number, arrived at independently — one from the colour-management
protocol, one from a readback. So on Wayland the conversion between them is exactly 1, and
`FrameColorFilter` should return **null**: no scaling at all.

**Which makes a falsifiable prediction: on Wayland, video white and interface white should be
the same brightness.** On Windows they sit 18% apart, because the UI is drawn at the display's
SDR white of 240 nits and the video at its true 203.

**So put a white UI element next to a white part of the picture and look.** If they match, two
independent derivations of 203 agree and the constant is right. If the video is visibly dimmer
or brighter than the interface, one of them is wrong and this is where it shows — no measurement
on Windows can catch it, because there the two are *meant* to differ.

`HULTUI_VIDEO_WHITE=ui` forces the other policy (video stretched to match the UI). On Wayland it
should make no visible difference at all, which is the same prediction from the other side.

### 5c. The things that should be untouched

- **X11.** No scRGB, so no linear light is ever asked for: the trace should say
  `video target encoded` and video should look exactly as it did before. A regression here would
  mean the gate is not checking the colour space properly.
- **An SDR display.** Same expectation — switch the monitors out of HDR mode and check video
  still looks right, not dark or blown out.
- **`HULTUI_VIDEO_HDR=0`.** Should be indistinguishable from before phase 5.
- **The spike, if the app disagrees with it:** `--range <file> target-trc=linear
  target-prim=bt.709 target-peak=1000` reports what mpv writes into a shared image with no
  renderer involved, which separates a libplacebo problem from a compositing one in one run.

## What to send back

Same as before: append results to this file under a **Results** heading and commit, rather than
pasting. Say which monitor and which display server each observation came from — the brief's one
ambiguity was a by-eye result whose monitor had been swapped by KDE without anyone noticing.

For the by-eye checks in fixes 1 and 3 and in 5b, **say plainly that a person looked**, and say
who looked at what. A capture that agrees is not evidence for either of them, and reporting one as though it
were is the specific mistake this whole exercise has already made once.

## Results

Run 2026-09-25 on the Linux machine by a Claude Code session, with Fredrik at the screens for
every by-eye item; each one below names him. Base commit `2f78046` (includes `00e9a24`), plus
the X11 GL fix described under **New finding**, which went in mid-run: everything before it
was run without it, and it touches only the GL proc resolver.

**Setup, as `kscreen-doctor -o` reported it at the start and again after the HDR toggle:**
Fedora 44 KDE Plasma, Wayland session, RTX 4080 on NVIDIA 615.71.09.
DP-2: 2560x1440, scale 1.0, priority 1 (primary), logical x 0–2560, HDR on, SDR brightness 408.
DP-3: 3840x2160, scale 1.5, priority 2, logical x 2560–5120, HDR on, SDR brightness 455.
`env | grep -i HULTUI` was empty before anything ran; no override was set at any point except
the ones named per step.

**How "which monitor" was established.** Not by eye and not by SDL: a KWin script, loaded
over D-Bus, printed each window's `output.name` and logical `clientGeometry`. The same
mechanism placed windows for the side-by-side checks, so every by-eye observation below names
the output KWin says the window was on at that moment.

### Fix 1 — pass, by log and by eye, on both monitors

- **Log (Wayland, nothing forced):** `gfx: video=wayland … format=R16G16B16A16Sfloat
  colorSpace=ExtendedSrgbLinearEXT hdrDisplay=on`, and every `vulkan display changed` line
  says `whiteScale=2.54 format unchanged`.
- **By eye, HDR on — Fredrik looked.** GL (left) and Vulkan scRGB (right) side by side at the
  same height, plain `dotnet run` / `HULTUI_RENDERER=vulkan`, no other overrides.
  On **DP-2** (KWin: both `output=DP-2`): *same*. Then both moved to **DP-3** (KWin: both
  `output=DP-3`; Vulkan rebuilt to 750x450 with `whiteScale=2.54`): *same*. The two outputs
  have different SDR brightness settings (408 vs 455), so matching on both is the fixed-scale
  claim, not a proportional one that happened to line up.
- **SDR direction — Fredrik looked.** Both outputs switched to HDR off with
  `kscreen-doctor output.DP-2.hdr.disable output.DP-3.hdr.disable`. Vulkan log:
  `hdrDisplay=off`, `B8G8R8A8Unorm/SrgbNonLinear` swapchains. Side by side on **DP-2**
  (KWin-confirmed): *same*. The log still prints `whiteScale=2.54` in this mode; the eye check
  says it does not leak into the 8-bit path. HDR was re-enabled afterwards and `kscreen-doctor`
  showed both outputs back at HDR on, 408/455 nits.

### Fix 2 — the loop is gone; the pass criterion as written is off by one

`SDL_VIDEODRIVER=x11 HULTUI_RENDERER=vulkan HULTUI_VULKAN_F16=1 HULTUI_STARTUP=1`, 14 runs:

- **No `rebuilding for scRGB` in any run.** Every `display changed` line (three per start-up)
  says `format unchanged`. The fix does what it says.
- **But there are two swapchains and two `wide colour asked for` lines per start-up, not one.**
  The window is created at 500x300 and XWayland then resizes it to 750x450 (scale 1.5; KWin
  shows 500x300 logical) — a real resize, which the criterion allows. The wide-colour line is
  logged by `ChooseFormat(trace: true)`, which every swapchain *build* calls, so it follows the
  build count; "at most one" should read "one per swapchain".
- **One run of 14 had a third, same-size swapchain** (750x450 → 750x450, ~145 ms after the
  display events, still no `rebuilding`). It was the first run after the build and did not
  recur in 13 more runs with a temporary stack trace on `RecreateSwapchain` (removed again), so
  the caller is not known. The same-size double build under Fix 3 below is a likely relative.

### Fix 3 — pass: launches measured by KWin, drags looked at by Fredrik

- **Launches, Wayland:** 6 GL + 6 Vulkan, each measured through KWin at ~4 s: **12/12 at
  500x300 logical, 0 at 333x200**, all on `DP-2`. That is the transition that used to shrink:
  both renderers start at scale 1.5 (GL `drawable=750x450px density=1.5`; Vulkan first
  swapchain 750x450) and end on the 1.0 output at 500x300 px. KWin put every launch on DP-2;
  one earlier Vulkan run landed on DP-3 at 750x450 px, i.e. also 500x300 logical.
- **Drag, both renderers — Fredrik dragged and looked.** GL and Vulkan windows, each dragged
  slowly by hand DP-2 → DP-3 → DP-2: *pass* for both — kept apparent physical size, text and
  button scaled with the monitor, no reflow. The Vulkan trace during the drags agrees:
  swapchains alternate 750x450 ↔ 500x300, constant 500x300 logical.
- **Observation, not a failure:** every output crossing builds **two** swapchains of the same
  size ~2 ms apart. Suspected (not confirmed): `SkiaVulkanRenderSurface.Resize` rebuilds on a
  scale-only change, which a swapchain does not depend on.
  **Confirmed and fixed afterwards.** A trace in `Resize` showed each crossing arriving as two
  calls, new pixels at the old scale, then the same pixels at the new scale (e.g. `750x450
  scale=1` then `750x450 scale=1.5`), and the second rebuilt. `Resize` now keys on pixels only;
  the scale was not read anywhere else in the class. Result: one swapchain per crossing (4
  crossings → 4 builds) and two at start-up instead of three. An idle window presents only one
  frame on the post-crossing swapchain, so that frame was captured (`CAPTURE_FRAME=1`) in both
  directions: content sits at the same layout units (35,40 → 320,109) on DP-2 at 500x300 and
  on DP-3 at 750x450, and the DP-3 frame looks right. The new scale reaches the picture
  without the rebuild.
- **X11, where the branch still runs:** Vulkan 3/3 — KWin 500x300 logical on DP-2,
  `drawable=750x450px`. GL on X11 could not be checked before the fix below because it crashed
  at start-up; after it, 3/3 reach `first frame shown`, KWin 500x300 logical on DP-2.

### New finding — GL on X11 segfaulted on every launch (fixed here)

`SDL_VIDEODRIVER=x11` with the GL renderer died with SIGSEGV 3/3, right after `gl context`, in
`GrGLExtensions::init` ← `GrGLMakeAssembledGLInterface` (coredump backtrace). It predates the
four fixes: the resolver involved is unchanged since `da7c019` (Phase 3), and `00e9a24`'s
`SDL3Window` changes do not run before this point.

Cause, confirmed with a temporary probe: Skia resolves `eglQueryString` and
`eglGetCurrentDisplay` through the same callback it uses for GL. On X11, SDL uses GLX, and
libglvnd's `glXGetProcAddress` returns a dispatch stub for *any* name — consecutive addresses
for `eglQueryString`, `eglGetCurrentDisplay` and a made-up `eglNoSuchFunctionXyz` alike. Skia
took them as real, the display stub returned `0xfffffffffffffdf8`, and the query stub returned
a garbage non-null "extension string" that Skia then parsed. On Wayland (EGL) the real
functions come back and a made-up name resolves to 0. The GL context itself was fine on both
drivers: current, 3.3 core, same NVIDIA version string.

Fix: `SDL3Window.GetGLProcAddress` sends `egl*` names to `SDL_EGL_GetProcAddress`, which
returns NULL when SDL is not on EGL, so Skia takes its no-EGL path. Verified: X11 GL 3/3 clean
with KWin 500x300 logical; Wayland GL unchanged (starts, 500x300, first frame). Not
investigated: `SDL_VIDEO_FORCE_EGL=1` on X11 fails earlier and cleanly, in `SDL_CreateWindow`
("Could not create GLES window surface"). Mesa's GLX (`__GLX_VENDOR_LIBRARY_NAME=mesa`)
crashed the same way before the fix, which fits the stub coming from libglvnd rather than a
vendor.

### Fix 4 — the comment's numbers do not hold here

Vulkan Minimal with `HULTUI_VULKAN_CAPTURE`, untouched: **a capture appeared** (`captured
frame 4 (500x300)`), both times it was tried. The pointer was not over the window — KWin put
the cursor at (3710, 843), on DP-3, and the window on DP-2 — so this is not hover.
Bisecting with `HULTUI_VULKAN_CAPTURE_FRAME`: frame 4 → file; 5, 6, 7, 10 → no file. **An idle
window here presents exactly four frames on its last swapchain**, not three, so the default
(images + 1 = 4) is reached with nobody touching anything. The comment's general warning (an
idle window may stop short) still stands; the specific "three frames, one short" does not
describe this run. The count probably depends on how many swapchains start-up builds (three
here on Wayland: 750x450, 500x300, 500x300), so a number in that comment is likely to be wrong
somewhere. The hover half was not run, because the file already appeared without it.

**Comment fixed afterwards.** Re-bisected on the build with the one-swapchain-per-crossing fix.
KWin was now opening the window on DP-3, the output it is created for, so there was a single
750x450 swapchain: frame 3 captured, 4/5/6 not (2 runs each). The idle count on this one
machine is therefore 3, 4, or 1 after a crossing, depending only on where the window lands.
The comment now says that, gives those three measurements instead of one number, and says a
capture that *does* appear proves nothing about idleness either.

### fd-per-import leak — pass, at both levels

**`--churn`:**

```
  1000 cycles, released properly
    handles 95 -> 95   (delta 0)
  200 exports deliberately dropped (the control: fd never imported)
    handles 95 -> 295   (delta +200)
  The control climbed, so this measurement can see a leak.
  VERDICT: 1000 released cycles cost no handles. The cycle does not leak.
```

**Repeated export of one allocation.** `--churn` printed only `(fd path)` here, so it now
samples the fd side too (`ChurnBench.SampleFds`: all fds held open until the last is taken,
then closed). Five exports of **one** `VkDeviceMemory` gave **five distinct live descriptors**
(fd92–96). Unlike Windows, where NVIDIA returned one identical handle, each export on
NVIDIA-Linux is a new descriptor that must be imported or closed. They cannot be told apart
by inode: every opaque fd NVIDIA exports is an open of `/dev/nvidiactl` (all `7:1377`, fresh
allocations included), so which allocation an fd names is not visible to the kernel.

**App level, through the renderer — Fredrik did the swaps.** VideoPlayer on Vulkan
(scRGB), two generated y4m clips (320x180, 1280x720), swapped by dropping them from Dolphin
onto the window (not through Open…). Fredrik reported every swap changed the picture. Trace:
**41 `vulkan interop image` lines, strictly alternating 320x180 / 1280x720 — 40 resolution
changes**. Descriptor count sampled every 2 s: 199 after the first image, then 200–202 through
all 40 changes, 201 at the end. A per-swap leak would have added ~40. So `DestroyInteropImage`
+ `CreateSharedImage` release cleanly on this driver, and the app-level version that stalled
on Windows is proven here.

# Linux round 2 — what changed after the first verification

The first round is [HDR-LINUX-VERIFY.md](HDR-LINUX-VERIFY.md) and every check in it has been
answered; its Results sections are yours. This is a separate file so that new asks are not mixed
in with resolved ones.

Four commits landed on Windows after that file was last touched (`4808f17`):

| | |
|---|---|
| `ea498a5` | Re-benchmarked both renderers; recorded the default-renderer decision |
| `429cfa8` | **Fixed the resize stretch** — a live-resize frame that was never drawn, plus `IMMEDIATE` |
| `d1d7cd5` | Established that Vulkan's remaining costs are the F16 swapchain; **reordered window creation** |
| `e53b681` | Over-allocated swapchain via `swapchain_maintenance1`, opt-in and off by default |

**Two of these need Linux specifically, and one of them is a risk I introduced.** The rest is worth
numbers but nothing depends on it.

---

## 0. First, the thing that is still not done

**Synchronisation validation has never run, on either machine.** The section is already written —
[Validation, for the semaphore change](HDR-LINUX-VERIFY.md#validation-for-the-semaphore-change--the-one-thing-windows-cannot-check)
in the first file — and it still describes the current code exactly; nothing in the four commits
above touched the semaphore or the command-buffer ring.

It stays the highest-stakes item here. The interop rests on Skia's read being ordered after our
barrier by submission order alone, and nothing has ever tested that. **Do this before the rest of
this file**, because if it fails, several things below stop mattering.

---

## 1. The window-creation reorder — the risk I introduced

**This is the one to look at hardest, because it broke the GL renderer on Windows and no test
caught it.**

### What changed

A window is created at the size the app asked for in density-independent units, and on a scaled
display that is not the pixel size it should end up with: 500x300 at 150% wants to become 750x450.
That correction used to happen *after* the renderer was built, which made the Vulkan path allocate
an entire swapchain at the wrong size and immediately throw it away. It now happens **before** the
renderer exists.

That change had a consequence I did not predict. `UpdateSize` early-returns when the size has not
changed, and the **GL** surface had been getting its *initial* size from that very correction —
`PixelWidth` was 0 until then, so a change was always detected. Moving the correction earlier
consumed that transition, and every GL launch on Windows died with:

```
Unhandled exception. System.InvalidOperationException: No canvas set. Call BeginFrame first.
```

The Vulkan path was immune, because its host takes the size in its constructor. The fix is that the
surface is now told its size **explicitly** after construction, instead of relying on a side effect
that only fired on scaled displays.

### Why Linux is where this could still be wrong

The reorder moves an `SDL_SetWindowSize` call earlier in the sequence, and **on Wayland the
compositor owns window sizing.** That is not a guess: the first round found exactly this, and
`UpdateSize` already carries a `scaleOnlyChange` branch that is skipped on Wayland with a comment
saying it made things worse there. A call that behaves one way under Win32 and another under a
compositor is the shape of thing that reorder could disturb.

### What to check

**Both renderers, both display servers, and a scaled output.** The GL half matters as much as the
Vulkan half — GL is still the default and still what every shipped build uses, and GL is what broke
on Windows.

```bash
# GL is the empty renderer value: anything other than "vulkan" leaves it on GL.
for driver in wayland x11; do
  for renderer in "" vulkan; do
    echo "== $driver / ${renderer:-gl} =="
    SDL_VIDEODRIVER=$driver HULTUI_RENDERER=$renderer \
    HULTUI_STARTUP=1 HULTUI_A11Y=0 \
      dotnet run -c Release --project samples/HultUI.Samples.Minimal
  done
done
```

Establish, and write down:

- **No crash on either renderer.** Specifically not `No canvas set. Call BeginFrame first.` — that
  is the Windows failure signature and it would be the same bug reaching Linux.
- **The window opens at the size it should.** The first round found a real bug here once already
  (a 500x300 window shrinking to 333x200 on a mixed-scale KDE desktop, six launches out of six),
  so this is a place where being off is already known to be possible. Check on a fractional-scale
  output, and on a **mixed-scale desktop** if you still have one configured — that is the case that
  caught it before.
- **The positive control: exactly ONE swapchain build in the trace at start-up**, not two. Before
  this change a scaled display produced `vulkan swapchain 500x300` followed immediately by
  `vulkan swapchain 750x450`. One line means the reorder did what it was for; two means it did not
  take effect here and the rest of this section's result is about untested code.
- **Resize still works after it** — drag an edge, cross between outputs. The first round's Fix 3
  covered mixed-scale crossing; this touches the same machinery.

**If the window size is wrong on Wayland, say so and stop there** rather than working around it.
The correction may simply need to stay after renderer creation on Wayland, in which case the wasted
swapchain comes back on that platform and that is an acceptable price — it was measured on Windows
as costing nothing anyway (total start-up did not change).

---

## 2. Does the resize stretch even exist here?

**On Windows, a dragged window visibly stretched its old contents before snapping.** Two causes,
both now fixed: a live-resize frame that the 16 ms throttle dropped and that nothing ever redrew
(the throttle is gone from that path), and the `IMMEDIATE` present mode on top of it (the default
is now `MAILBOX`).

**It may be a Windows problem only.** A Vulkan swapchain there is DXGI underneath and the default
scaling behaviour is `PRESENT_SCALING_STRETCH` — the artefact has a name in the specification. A
Wayland compositor does not necessarily do the same thing with a mismatched surface. **Finding out
that this never happened on Linux is a perfectly good result**, and worth knowing before anyone
carries the Windows reasoning onto this platform.

### How it was made measurable, which matters more than the answer

A swapchain readback **cannot** answer this. The image was always correct; the question was what the
*screen* showed. So: draw a bar of a known device width, resize the window, capture the **screen**
immediately, and measure the bar. A bar wider than it was drawn means the compositor scaled a
smaller image up. A merely *stale* frame keeps the bar at its true width — which is what separates
"stale" from "stretched", and was the whole difficulty.

On Windows it came back at 402 device pixels where 300 was drawn, on a resize that grew the window
by exactly 1.333. Every stretched sample measured 402 and never anything between.

**Use whatever screen-capture tool KDE gives you** rather than porting the harness; the measurement
is "how wide is the bar on screen", not the script.

### And the discipline that made it trustworthy

**The rate varies enormously between runs on the same build** — Windows saw 25 in 174, 18 in 150,
4 in 120, and twice 0 in 150 on builds that definitely stretched. So:

- **A clean result only counts if a control beside it stretched.** Run `HULTUI_VULKAN_PRESENT=immediate`
  with `HULTUI_VULKAN_FIXED_EXTENT=0` as the arm that *should* fail. If that arm reads zero, the
  measurement detected nothing and every clean arm beside it is worthless. This exact trap was hit
  twice on Windows and it is why the write-up hedges its own conclusion.
- **Sample dozens of resizes, not a handful.**

### What to send back

Whether it stretches at all on KWin, under `wayland` and `x11`, with a control that fired. If it
does not stretch on either, say so plainly — that is the useful answer.

---

## 3. `swapchain_maintenance1` — does Mesa even offer it, and does the video penalty reproduce?

Off by default, behind `HULTUI_VULKAN_FIXED_EXTENT=1`. It allocates the swapchain larger than the
window (rounded up to a 256-pixel bucket) and presents it un-scaled with
`PRESENT_SCALING_ONE_TO_ONE` and `PRESENT_GRAVITY_MIN`, so a drag rebuilds only when the window
outgrows the allocation.

**Every number behind it is one NVIDIA driver on Windows**, which is exactly the situation the
first round was written to correct.

```bash
HULTUI_RENDERER=vulkan HULTUI_VULKAN_FIXED_EXTENT=1 HULTUI_STARTUP=1 HULTUI_A11Y=0 \
  dotnet run -c Release --project samples/HultUI.Samples.Minimal
```

The trace answers the first question on its own:

```
vulkan present scaling: oneToOne=True gravityMin=True scaledExtent=1x1..4294967294x4294967294 — fixed extent available
vulkan swapchain 750x450 x3 ... (allocated 768x512, one-to-one)
```

- **If `oneToOne` or `gravityMin` is False**, the feature declines itself and rebuilds per resize as
  before. Write down what the capabilities actually said — **on Mesa or a hybrid GPU this is the most
  likely thing to differ**, and a "not offered" is a result, not a failure.
- **If the swapchain line has no `(allocated …)` suffix**, it silently did nothing. That happened on
  Windows first: the bucket was being clamped to `VkSurfaceCapabilitiesKHR.maxImageExtent`, which
  under Win32 equals `currentExtent`, so it came straight back to the window size. With scaling
  chained the spec bounds the extent by `min/maxScaledImageExtent` instead. A different WSI may have
  its own version of that trap.
- **Run it under the validation layers.** This chains a new structure into swapchain creation and
  enables a device extension with a feature bit; Windows was clean but Windows also never shows the
  layers anything unless asked.

### The number that decided the default, and the one most likely to differ

**On Windows the over-allocated swapchain costs 4K HDR playback about 70% more CPU** — 91–95 ms/s
against 48–63, three reps with GL alongside as a drift reference. The reading is that a swapchain
whose extent matches the window can be handed to the compositor directly, and one that does not is
copied instead. That is a DXGI-flavoured explanation, and **there is no reason to assume a Wayland
compositor behaves the same way.**

If the penalty does *not* reproduce on KWin, the right default may differ per platform — which would
be the first time in this project that it did, so it needs the measurement rather than the argument:

```bash
# A/B on one binary, ROUND-ROBIN so drift lands on all three arms rather than on whichever
# went last. Absolute numbers move by a third between sessions on the Windows machine; only
# the ordering within one run means anything.
film=/path/to/4k-hdr.mkv
exe=samples/HultUI.Samples.VideoPlayer/bin/Release/net10.0/HultUI.Samples.VideoPlayer
for rep in 1 2 3; do
  for case in gl vk-rebuild vk-fixed; do
    case $case in
      gl)         env_args=(HULTUI_RENDERER=) ;;
      vk-rebuild) env_args=(HULTUI_RENDERER=vulkan HULTUI_VULKAN_FIXED_EXTENT=0) ;;
      vk-fixed)   env_args=(HULTUI_RENDERER=vulkan HULTUI_VULKAN_FIXED_EXTENT=1) ;;
    esac
    env "${env_args[@]}" HULTUI_A11Y=0 "$exe" "$film" --start=1200 &
    pid=$!
    sleep 12                                    # let the decoder settle
    before=$(awk '{print $14+$15}' /proc/$pid/stat)
    sleep 15                                    # the measurement window
    after=$(awk '{print $14+$15}' /proc/$pid/stat)
    kill $pid
    # jiffies -> ms of CPU per second of wall clock
    echo "$case rep$rep: $(( (after - before) * 1000 / (15 * $(getconf CLK_TCK)) )) ms/s"
    sleep 2
  done
done
```

**Check the VO line says `cuda[...]` in each arm before believing any of it** — a run that quietly
fell back to a copy mode is measuring a different thing, and the first round established that
`hwdec-current` can rule a copy in but never out.

Measure it on a **compressed 4K HDR clip**, not an uncompressed one — the first round already
established that disk reads swamp everything otherwise.

---

## 4. Numbers worth having, since all of mine are Windows

Low priority; skip if time is short. Recorded on Windows for comparison (RTX 4080, 120 Hz HDR
display at 150%, `HULTUI_A11Y=0`, Release, round-robin):

| | GL | Vulkan 8-bit | Vulkan F16 |
|---|---|---|---|
| start-up to first frame | 552 ms | 581 | 679 |
| the first frame itself | 55 ms | 51 | 151 |
| resize CPU, 120 resizes | 264 ms/s | 246–319 | 801 |
| two windows, private | 82 MB | 89 | 132 |
| present pass | 0.10 ms | 0.20 | 0.40 |

**The claim worth testing is the shape, not the values: every Vulkan penalty is the half-float
swapchain and not the API.** An 8-bit Vulkan window starts within 30 ms of GL, resizes at GL's cost
and sits 7 MB above it. `HULTUI_VULKAN_F16=0` is how that column is produced. If the shape does not
hold on Mesa, the conclusion in `HDR.md` that this is "the price of 16-bit colour" is a
one-driver conclusion and should be labelled as such.

---

## What to send back

Per item: what you ran, what it printed, and what you concluded. The same two rules as last time,
both of which earned their place:

- **For anything judged by eye, say plainly that a person looked, and who looked at what.** The
  resize stretch cannot be captured by the renderer — only seen.
- **For anything where a clean result is the good news, name the control that fired.** A quiet run
  and a broken measurement look identical. On Windows the stretch harness twice reported zero while
  the bug was present, and only the control revealed it.

And say which of these you did **not** get to, rather than leaving it to be inferred from silence.

## Results

Run 2026-09-27/28 on the Linux machine by a Claude Code session, base commit `ec56ee0`, **with no
code changed** (the controls below were temporary, env-gated patches, reverted before the real
runs). Fedora 44 KDE Plasma 6 Wayland, RTX 4080 on NVIDIA **615.71.09**, Vulkan loader and
validation layers **1.4.341**, system `mpv-libs` 0.41.0. `kscreen-doctor -o`: DP-2 2560x1440 scale
1.0 (primary), DP-3 3840x2160 scale 1.5, both HDR on, SDR brightness 408/455. `env | grep HULTUI`
empty. "Which output" and window sizes came from KWin (`output.name`, `clientGeometry`), as in
round 1. **Nobody judged anything by eye this round**: every result is a log, a KWin reading or a
screen capture. Fredrik's only part was moving the pointer onto DP-2 for the launches that needed it.

**No Mesa and no hybrid GPU here.** Every "Linux" number below is NVIDIA's proprietary driver, so it
is a second driver rather than the Mesa data point the brief hoped for.

### 0. Synchronisation validation: clean on both paths and both display servers, with controls that fired

**Real runs.** VideoPlayer, 2001 at 20:00, `HULTUI_RENDERER=vulkan`, 90 s each with 21 KWin-driven
resizes and moves between DP-2 and DP-3 (24–31 swapchain rebuilds a run). Every run showed
`VO: [libmpv] 3840x1746 cuda[p010]` (zero-copy NVDEC) and the ordering it claimed in the trace.

| | Wayland | X11 |
|---|---|---|
| semaphore (`semaphore ordering`) | **0 messages**, 31 swapchains | **0 messages**, 24 swapchains |
| `HULTUI_VULKAN_SEMAPHORE=0` (`glFinish ordering`) | **0 messages**, 31 swapchains | **0 messages**, 28 swapchains |

**Features enabled: core validation and synchronisation validation**, set by
`VK_LOADER_LAYERS_ENABLE='*validation*' VK_LAYER_VALIDATE_SYNC=1`, and **proved per run**: each log
has the layer's own status line, `Current Enables: VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT`
(requested with `VK_LAYER_REPORT_FLAGS=info,warn,perf,error`). That line was the only validation
output in all four logs. The brief's `VK_LAYER_ENABLES=…` spelling still works on this layer but is
deprecated, and it warns that it overrides any new-style setting.

**The controls, which matter more than the zeros.** A quiet layer proves nothing, so the interop
barrier was temporarily broken on purpose (env-gated patch, reverted afterwards, `git status`
clean):

| deliberate fault | what the layer said |
|---|---|
| mip range past the image's one level (core) | `VUID-VkImageMemoryBarrier-subresourceRange-01724` ×20 — output path works |
| clear the shared image, then an unordered layout transition, **same** command buffer | `SYNC-HAZARD-WRITE-AFTER-WRITE` ×10, naming the shared image |
| the same, the transition in a **second submit** | `vkQueueSubmit(): WRITE_AFTER_WRITE … submitted on the current VkQueue`, Wayland and X11 |

The last row is exactly the class the design depends on: a missing dependency **between two submits
on the one queue**. Sync validation catches it here, sees the shared image, and reports nothing on
the real code.

**Three controls that did not fire, and why that is information, not failure:**
- `srcAccessMask = COLOR_ATTACHMENT_WRITE` with `TOP_OF_PIPE`: silent. Not a check this layer makes
  on legacy image barriers, so it was no control.
- Two unchained layout transitions, the first one a `QUEUE_FAMILY_EXTERNAL` acquire: silent. Sync
  validation appears to treat the ownership acquire specially. Not pursued.
- **The hand-back barrier with its destination scope removed** (`dstStage = TOP_OF_PIPE`,
  `dstAccess = 0`): silent, even though cross-submit detection demonstrably works. So Skia's sample
  of the image is ordered by **more than our barrier**. The likely reason is that Skia records its own
  barrier on the wrapped texture (it tracks layout inside the wrapper). Not confirmed: it would need
  an API dump. If true, the central assumption holds **with margin** on this Skia. It is also an
  argument for not removing our barrier on the strength of it.

**Found by reading, not tested, and not something validation can see:** there is no Vulkan → GL
ordering. There is no release barrier to `QUEUE_FAMILY_EXTERNAL` and no semaphore GL waits on, so
mpv's GL write of the next video frame is not ordered on the GPU after the previous UI frame's
Skia sample of the same image. The acquire fence only waits for the frame that last presented *that
swapchain image*, two or three frames back with MAILBOX and 3–4 images. The old `glFinish` path had
the same gap (its queue wait ran after GL had already written), so the semaphore change did not
introduce it. The symptom, if it ever showed, would be a torn or half-updated video frame. It is a
GL-side write-after-read, invisible to every Vulkan layer by construction.

**Confirmed and fixed afterwards.** Measured first, with a temporary fence on the present submit
checked at the moment GL starts writing the shared image. At normal load the previous frame's
Vulkan work had **always** finished: 0/1200 on Wayland and 0/1200 on X11, and under a 16 ms resize
storm 0/1440 and 1/960 (each rebuild's `vkDeviceWaitIdle` drains the queue anyway). The UI renders at
the video's ~24 fps, so each frame has ~40 ms to finish. With the GPU made to take longer (the video
drawn 20, 60 or 250 extra times per frame with a blur) the exposure appeared: **3, 8 and 59 of 960**
GL writes started while the previous frame's Skia sample was still pending. Real, and reachable
whenever frames are GPU-bound.

The fix is the mirror of the existing semaphore. A second shared semaphore runs Vulkan → GL. The
present submit, the frame's last, records a release barrier (queue family → external,
`SHADER_READ_ONLY` kept, fragment-shader source scope) when the frame drew the image, and signals
it. `LendInteropImage`, on the GL context before mpv writes, calls `glWaitSemaphoreEXT` with the
texture and `GL_LAYOUT_SHADER_READ_ONLY_EXT`. A pending flag keeps it to one wait per signal.
Without semaphores (`HULTUI_VULKAN_SEMAPHORE=0`, or a driver without them), the fallback does a
queue wait at the same point instead. Two smaller defects on the same path went in with it:
- **`glSignalSemaphoreEXT` was never flushed.** The GL context stays current, so no context switch
  flushes it implicitly, and a Vulkan wait on a binary semaphore needs the signal already submitted.
  `SignalSemaphore` now calls `glFlush`, which does not block.
- **The shared image could be destroyed while a frame still sampled it**, when the video's size
  changes. That branch now waits for the device first. It is rare: the image follows the video's
  size, not the window's.

Checked:
- **Sync validation, core + sync (layer status line in every log)**: 60 s runs on the semaphore and
  fallback paths, Wayland and X11, with moves between outputs. 0 messages each.
- **A control that the GL wait is live**: marking the release pending without signalling it stalls
  GL. No probe sample arrived in 20 s, against six from the normal build, whose content kept changing.
- **Cost**: 4K CPU 128–130 ms/s against 113–144 before (round-robin, a separate build of the parent
  commit). Descriptors flat at 202–203. Under the forced GPU load, frame throughput is identical
  before and after (600 frames in 30 s).
- `HultUI.Rendering.Skia.Tests` 140, `HultUI.Core.Tests` 905, `HultUI.Media.Tests` 54 pass.
- **Not shown: a torn frame before the fix.** Exposure was measured, not the corruption itself.
  **Not run on Windows**, where the same code uses `OPAQUE_WIN32` and `GL_EXT_semaphore_win32`.

**A procedural note, since it nearly produced a false pass:** the first batch of these runs passed
the layer settings as one shell string, which zsh does not split. The layer never loaded, and the
runs came back "clean". They are not counted above. The per-run `Current Enables` line was added
so that this cannot happen silently again.

### 1. The window-creation reorder: no crash anywhere, correct sizes, but two swapchain builds at start-up, not one

**On Wayland the moved `SDL_SetWindowSize` never runs**, which removes most of the risk the brief
was worried about. `InputScale = PixelDensity / DisplayScale`, and on Wayland SDL's window
coordinates are logical, so on DP-3 that is 1.5 / 1.5 = 1 and the correction branch is skipped. It
runs on **X11**, where SDL sees a 1.0 density and a 1.5 display scale: XWayland windows are 750x450 X
pixels, and KWin scales X11 clients down on DP-2. That is correct behaviour for KWin's XWayland
scaling, not a bug.

- **No crash, and no `No canvas set`**: every Minimal launch this round (more than 70: GL and Vulkan, Wayland and X11,
  on both outputs), all reaching a live window.
- **Size**: every launch 500x300 logical by KWin. Wayland on DP-3: swapchain 750x450. **Launched on
  DP-2**, the mixed-scale case that shrank in round 1 (pointer placed there by Fredrik, output
  confirmed by KWin): **12/12 at 500x300 (6 GL, 6 Vulkan), 0 at 333x200.** X11 on DP-2: 500x300
  logical, 750x450 swapchain.
- **Resize after it**: KWin-driven resizes and DP-3 → DP-2 → DP-3 crossings on all four combinations.
  Every Vulkan swapchain equalled KWin's client size × the output's scale (700x472@1.5 → 1050x708,
  900x622@1.0 → 900x622, 500x272@1.5 → 750x408). On X11 the crossings correctly caused no rebuild.
  GL windows stayed alive and followed. These were scripted, **not dragged or looked at by a person**.
- **The positive control did not come back as one line.** Wayland on DP-3:

  ```
  vulkan swapchain 750x450 x3 R16G16B16A16Sfloat/ExtendedSrgbLinearEXT   t=171 ms
  skia surface
  vulkan swapchain 750x450 x3 R16G16B16A16Sfloat/ExtendedSrgbLinearEXT   t=197 ms
  platform window ready
  ```

  The *wrong-size* build is gone: there is no 500x300 line, so the reorder did what it was for. The
  second build is the **same size**, and it comes from the GL fix, not from Wayland. The new explicit
  `ActiveSurface?.Resize(PixelWidth, PixelHeight, DisplayScale)` reaches
  `SkiaVulkanRenderSurface.Resize`, whose `_width/_height` start at 0, so it never early-returns and
  calls `RecreateSwapchain`. With the fixed extent off (the default, and unavailable here anyway),
  `Rebuild` rebuilds unconditionally. X11 shows the same pair, and a launch on DP-2 shows three builds
  (750x450, 750x450, then 500x300 once KWin reports 1.0) where round 1's fix had got it to two.
  **By the code this happens on every platform, Windows included.** My guess is that the one-line
  observation on Windows predates the GL fix in the same commit. **Left unfixed on purpose**, so that
  Windows can confirm it. The likely fix is one line: seed `_width/_height` from the host's size when
  the surface is constructed, so the explicit call is a no-op on Vulkan and still sizes GL.

  **Fixed afterwards.** `IVulkanSurfaceHost` now exposes the swapchain's window size
  (`WidthPx`/`HeightPx`) and `SkiaVulkanRenderSurface` starts from it, so the explicit `Resize`
  is a no-op on Vulkan and still sizes GL. Result on this machine: **one** start-up build on
  Wayland (DP-3, 750x450 F16) and X11 (750x450 8-bit). All four GL/Vulkan × Wayland/X11 launches
  reach `first frame shown`, and the same KWin resize and crossing sequence rebuilds to exactly the
  sizes it did before. VideoPlayer: one start-up build, zero-copy, real content in the shared
  image. `HultUI.Rendering.Skia.Tests` 140 and `HultUI.Core.Tests` 905 pass. **Not run on
  Windows**, where the same one-line change should take its start-up from two builds to one.

### 2. The resize stretch does not happen on KWin, on either display server, and the measurement could have seen it

**Method.** KWin's `ScreenShot2.CaptureArea` at native resolution (10–15 ms a capture, through a
temporary authorisation, since removed), five captures straight after each KWin-driven resize
between 600x400 and 800x533 logical frames (×1.333, the Windows ratio), on DP-3. Two measurements
per capture: the **indigo button's width in device pixels** (the bar: a stretch makes it wider) and
the **white client width** along a row (which buffer size the compositor is showing).

| Minimal, Vulkan, `FIXED_EXTENT=0` | captures | stretched | old buffer on screen (stale) |
|---|---|---|---|
| Wayland, default (**MAILBOX**) | 200 / 40 resizes | **0** | 40, every first capture, both directions |
| Wayland, `HULTUI_VULKAN_PRESENT=immediate` (brief's control) | 200 / 40 | **0** | 40 |
| X11, default | 200 / 40 | **0** | 20, grows only |
| X11, `immediate` | 200 / 40 | **0** | 20, grows only |

The button measured **137 px in all 800 captures**, stale ones included.

**The brief's control did not fire, and here it cannot**: nothing stretches on this machine, so
IMMEDIATE is not an arm that "should fail". What stands in for it is the stale count. Every arm
caught the compositor showing the **previous** buffer right after a resize: the window has its new
size and the image is the old one. That is exactly the mismatched moment DXGI resolves by scaling,
and in every such capture KWin showed it **unscaled**, with the button at its true width. So the
measurement was fast enough to see the moment a stretch would happen, and it saw stale, not
stretched. That is the answer: **the stretch is a DXGI artefact, and the Windows reasoning should
not be carried over to KWin.** (On X11 a shrink is never stale, because a top-left crop of the old
buffer looks identical to the right one.)

**Also found:** NVIDIA's X11 surface offers FIFO, IMMEDIATE and FIFO_LATEST_READY, with **no
MAILBOX**, so `ChoosePresentMode` gives **IMMEDIATE on X11 by default** (trace:
`vulkan present mode Immediate`). Wayland gets MAILBOX. The "MAILBOX is the default" in `HDR.md`
is a Windows-and-Wayland statement.

### 3. `swapchain_maintenance1`: advertised by NVIDIA's Linux driver, but no present scaling on any surface

```
vulkan present scaling: oneToOne=False gravityMin=False scaledExtent=0x0..0x0 — rebuilding per resize
```

Same on Wayland and X11. **No swapchain line has an `(allocated …)` suffix**, and the build count
through a drag equals the rebuild path's. Confirmed independently of our code: `vulkaninfo` lists
`VK_EXT_swapchain_maintenance1`, `VK_KHR_swapchain_maintenance1` and
`VK_EXT/KHR_surface_maintenance1` with `swapchainMaintenance1 = true`, but
`VkSurfacePresentScalingCapabilitiesKHR` is `supportedPresentScaling: None`, gravity `None`, and
scaled extents 0x0 for **every** surface, Wayland included. So the feature declines itself cleanly,
which is the "not offered" result the brief anticipated, on NVIDIA rather than Mesa. Under validation
(core + sync, `FIXED_EXTENT=1`, a drag and output crossings on both display servers): 0 messages,
though with the feature declined that exercises only the capability query.

**The video A/B was not run, because here it cannot measure anything**: with scaling unavailable,
`vk-fixed` *is* `vk-rebuild`. Whether a Wayland compositor charges for a non-matching extent stays
open. Item 2 does bear on it: KWin never scales a mismatched buffer, so the premise of the
over-allocation (stop the compositor scaling) has nothing to fix on this platform to begin with.

### 4. Numbers: the "every Vulkan penalty is F16" shape does not hold on NVIDIA Linux

Minimal on DP-3 (1.5, HDR, 120 Hz), `HULTUI_A11Y=0`, Release, round-robin. "Vulkan 8-bit" is
`HULTUI_VULKAN_F16=0` (trace: `B8G8R8A8Unorm/SrgbNonLinear`). Absolute start-up numbers are not
comparable with Windows' (a different clock origin and a different OS). Compare within a column.

| | GL | Vulkan 8-bit | Vulkan F16 |
|---|---|---|---|
| start-up to first frame, median of 5 | 173 ms | 269 | 262 |
| the first frame itself, median of 5 | 43 ms | 48 | 53 |
| resize CPU, 120 resizes, 3 reps | 215–228 ms/s | 264–283 | 300–342 |
| same, size unchanged | 12–16 ms/s | — | 8–20 |
| one window, private (`smaps_rollup`), 3 reps | 43 MB | 66 | 66–67 |
| present pass (`Benchmark --profile`, 0.1 ms resolution), 2 reps | 0.1 | 0.1 | 0.1 |

- **Start-up and memory are the API here, not the half-float swapchain.** Vulkan's roughly +95 ms
  and +23 MB are the same with and without F16. The start-up gap is mostly instance and device
  creation (76 ms against 34 for GL context creation), plus ~13 ms in window creation and ~9 ms in
  the Skia surface. F16's first-frame compile, 100 ms on Windows, is **5 ms** here.
- **Resize**: F16 adds ~50 ms/s over 8-bit (Windows: ~500), and 8-bit Vulkan sits ~50 above GL.
  The rebuild count is the same in both arms (122).
- **Present**: no arm resolves above 0.1 ms. Windows' F16 figure of 0.40 would have shown.

So **"the price of 16-bit colour" in `HDR.md` is a Windows (DXGI/NVIDIA 610) conclusion** and should
be labelled that way. On NVIDIA Linux, 16-bit colour is nearly free and Vulkan's fixed costs are
the API's.

Method notes: the resize storm is a 16 ms `QTimer` inside a KWin script resizing through 12 sizes,
with CPU from `/proc/<pid>/stat` utime+stime over 2.5 s (all threads, driver threads included, like
`TotalProcessorTime`). Memory is **one** window, not two: MultiWindow opens its second window only
on a click. Linux private pages are not Windows private bytes either, so only the difference
between columns carries over.

### Not done

- **Item 3's video CPU A/B**, which is unmeasurable on this driver (above).
- **Mesa, a hybrid GPU, a compositor other than KWin**: none available.
- **By-eye drags** for item 1's "resize still works": replaced by KWin-scripted resizes and crossings
  checked against swapchain sizes. A person did not drag or look.
- **The Vulkan → GL ordering gap** in item 0: measured and fixed afterwards (see item 0). Not yet run on Windows.
- **The duplicate start-up swapchain** in item 1: fixed afterwards (see item 1). Not yet run on Windows.

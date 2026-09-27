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

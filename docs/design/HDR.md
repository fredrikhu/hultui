# HDR output

**Status:** phases 0 to 5 built. Verified on Windows and on Linux (KDE Wayland, NVIDIA);
untested on Mesa and on a hybrid GPU. HDR video is on by default on the Vulkan renderer,
which is itself opt-in through `HULTUI_RENDERER=vulkan`.
**Date:** 2026-09-27.

**Before changing anything here, read [Traps](#traps).** It is not a list of curiosities: most
of those cost hours, several were shipped and found by the other platform, and four of them were
nearly recorded as successes by measurements that could not have failed. The Linux runs are in
`HDR-LINUX-BRIEF.md`, `HDR-LINUX-VERIFY.md` and `HDR-PHASE1-LINUX.md`; the numbers behind the
claims are in `spikes/HdrInterop/results/`.

The goal is HDR video in Reel and Aperture, on Windows *and* Linux, with the transport
bar and every other piece of UI composited over it correctly. This is the design that
follows from wanting all three at once, and it is not a small one.

## Why the current stack cannot do it

HDR is a property of the **presented surface**, not of the drawing. A window has to be
handed to the compositor as an HDR-capable swapchain — scRGB FP16, or HDR10 with PQ
encoding and metadata — before anything drawn into it can be HDR.

**OpenGL on Windows has no way to ask for one.** WGL has no colorspace extension that
any driver honours for HDR. So the present path is the blocker: `SkiaGLRenderSurface`
draws perfectly good pixels and then hands them to a swapchain that can only be SDR.

That is the whole problem. It is not Skia, not mpv, and not the element tree.

## What is ruled out, and why

Two arrangements would give HDR sooner, and both were rejected for the same reason:
they are Windows-only.

- **mpv owns the video output** (`--wid`, a child window, `vo=gpu-next` with
  `gpu-api=d3d11`). mpv does PQ passthrough and tone mapping itself and it works today.
  But the overlay can no longer composite onto the picture — it has to become a layered
  window above a child HWND — and the whole arrangement is Win32.
- **DirectComposition with two visuals**, video in an HDR swapchain and UI in a second
  with premultiplied alpha. Correct, standard on Windows, and has no Linux counterpart.

Both also stop video being an element in the tree, which is the thing that makes the
current `VideoPlayer` overlay simple.

## The route: Vulkan everywhere

Cross-platform plus HDR leaves exactly one API. **Vulkan** has
`VK_EXT_swapchain_colorspace` and `VK_EXT_hdr_metadata` on both Windows and Linux, and
SkiaSharp 3.119.2 exposes the backend (`GRContext.CreateVulkan`, the `GRVk*` types are
in the shipped assembly). D3D12 is also exposed and is also Windows-only, so it is out
for the same reason as the two above.

### The part that makes this harder than it looks

`SkiaGLRenderSurface` does **not** share one framebuffer with mpv. mpv renders into its
own GL texture and Skia wraps it as an image to draw:

```csharp
internal SKImage? WrapTexture(in GpuFrame frame)   // SkiaGLRenderSurface
```

That seam is what keeps video an ordinary element. It also breaks the moment Skia stops
speaking GL: **mpv's render API is OpenGL-only.** `mpv_render_context_create` accepts
`opengl` and `sw`, nothing else — there is no Vulkan render type upstream, and the
constants in `Mpv.cs` reflect exactly that. So a Vulkan `WrapTexture` has a GL texture on
one side and a Vulkan image on the other.

The bridge is **external memory**: allocate the image in Vulkan, export it
(`VK_KHR_external_memory_win32` / `_fd`), import it into the GL context that mpv renders
with (`GL_EXT_memory_object_win32` / `_fd`), and give mpv an FBO over the imported
texture. Done once per resize, not per frame. This is a standard technique and it is also
**the single biggest risk in the plan** — if the interop does not work on the drivers we
care about, the route collapses and HDR goes back to being Windows-only.

Hence: the interop is the first thing built, on its own, before any of the rest.

### The build has to change too

`native/mpv/build-libmpv.sh` deliberately trims all of this out:

```
-Dvulkan=disabled -Dshaderc=disabled -Dglslang=disabled -Dlcms=disabled
-Dopengl=enabled  -Dd3d11=disabled
```

For HDR, **`lcms` has to come back** — without colour management mpv cannot tone map or
convert primaries. `vulkan`/`shaderc` are only needed if mpv itself ever renders with
Vulkan, which on this route it does not: it keeps rendering with GL into an imported
image. So the build grows by libLCMS2 and its licence-report entry, and not by a shader
compiler. That is a much smaller concession than it first looked.

## Phases

**0 — The interop spike. DONE, and it works.** `spikes/HdrInterop`: a Vulkan `RGBA16F`
image, exported, imported into a GL context, cleared from GL and read back through Vulkan
with the colour intact — and then mpv decoding into the same FBO, which read back as real
video rather than the clear colour. On an RTX 4080 on Windows.

Confirmed on **Linux** as well, on the same card: the `fd` export imports into GL, the
colour survives the round trip, and mpv decodes into the shared image. Windows and Linux
return the same pixel values for the same frame, to two decimals — so the bridge this whole
route rests on is cross-platform in fact, not on paper.

`target-colorspace-hint` turns out to be a no-op without render params describing the
target, so it belongs in phase 4 and is opt-in in the spike until then.

*Not* proven anywhere: AMD and Intel, and Mesa rather than NVIDIA's own driver. Those are
where an `fd` export is most likely to behave differently, and phase 1 should be treated as
unproven on them.

**1 — `SkiaVulkanRenderSurface`. DONE on Windows.** An app runs on
`HULTUI_RENDERER=vulkan` and draws correctly: the captured swapchain image is the Minimal
sample, indigo button and antialiased text, 30 distinct colours. GL is untouched and
remains the default.

One trap worth carrying forward: **Skia refuses a render target without
`VK_IMAGE_USAGE_TRANSFER_SRC_BIT`** and says nothing — `SKSurface.Create` simply returns
null. The bisect in `spikes/HdrInterop` pins it.

**Untested on Linux, and untested under resize anywhere.** The commands for both are in
[HDR-PHASE1-LINUX.md](HDR-PHASE1-LINUX.md), which is the next thing to do — phase 2 changes
colour handling and wants a known-good renderer underneath it.

The original plan for this phase read:

**1 — `SkiaVulkanRenderSurface`.** A fourth `IRenderSurface` — the interface is four
members and there are already three implementations, so this is the least novel part.
Vulkan instance, device, swapchain, `GRVkBackendContext`, wrapping swapchain images as
Skia render targets, resize, present. SDR only, sRGB, identical output to the GL surface.
Selected by an environment variable so the GL path stays the default and the shipping one.

**2 — F16 and a colour space.** Move the surface to `RgbaF16` with an explicit
`SKColorSpace`. Every `Color` in the framework is currently 8-bit sRGB with no tag, so
this is where the UI either keeps looking right or goes subtly wrong everywhere — themes,
gradients, the shadow and tint work in every app. Expect this phase to be about the apps,
not the renderer.

**Detection, found while starting phase 3:** a Vulkan surface lists every format and colour
space the *driver* supports, not what the *display* is doing. The list is byte-for-byte the
same with the monitor in HDR mode and out of it — checked, not assumed — so enumeration can
never answer "is HDR on". SDL asks the compositor, which is the only thing that knows:
`SDL_PROP_DISPLAY_HDR_ENABLED_BOOLEAN`, surfaced as `IPlatformWindow.IsHdrDisplay` and
printed by `DescribeGraphics` on both backends.

**scRGB is 80 nits, and Windows is not** (2026-09-25, found by photographing two windows
side by side rather than by any capture). `VK_COLOR_SPACE_EXTENDED_SRGB_LINEAR_EXT` fixes
1.0 at 80 nits; Windows composites SDR content at its "SDR content brightness" level, which
on this machine reads **240 nits** through `DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL`.
So a correct linear 1.0 lands three times too dim beside every other window, and the
swapchain image is right the whole time — the capture cannot see it and neither can a
screenshot, because the compositor is doing exactly what it was told.

**Where that factor can live, investigated** (`spikes/HdrInterop --text`):

| how | fill | glyph |
|---|---|---|
| destination primaries divided by 3 | 3 | 1 |
| `SKColorFilter.CreateColorMatrix` ×3 | 1 (clamped) | 1 |
| `paint.ColorF` = 3 | 12.8 (through the transfer fn) | 1 |
| SkSL colour filter ×3 | 3 | 1 |
| **drawn 0..1 offscreen, image scaled once on the way out** | **3** | **3** |

**No paint can put a glyph past 1.** Not a bent colour space, not a colour matrix, not a
shader. And it is not that text escapes colour management — sRGB red into a linear Display
P3 destination lands at 0.823 for a fill and 0.824 for a glyph, so text is converted exactly
like a fill. The clamp is specific to exceeding 1 on the glyph path.

So the scale stops being a property of the drawing and becomes a property of the finished
picture: render the frame into an offscreen surface at 0..1, where a glyph and a fill agree
because neither is being asked for anything unusual, then multiply once while blitting to
the swapchain. **Built**, and white text now matches white fills exactly — nought
mis-scaled white pixels against the 8-bit reference, where before there were 511.

**The clamp is at 1, and only upward.** Scaling *down* works per paint for both fills and
glyphs (0.251 and 0.251 through either a colour matrix or an SkSL filter). That matters for
the choice below, because HDR10 scales down where scRGB scales up.

### scRGB or HDR10, measured

| | unpaced fps |
|---|---|
| 8-bit sRGB, straight to the swapchain | ~7500 (drifts 7100–9200 between runs) |
| F16 scRGB, straight to the swapchain | ~4110 |
| F16 scRGB, offscreen and blit | ~3950 |

**The extra pass costs about 4%.** The 2× is the 64-bit render target, not the blit — which
was the thing worth knowing, because it was the blit that looked like the reason to prefer
HDR10.

So **scRGB stays**. HDR10 would halve the bandwidth, being 32 bits a pixel like the 8-bit
path, and its downward scale needs no offscreen. Against that: PQ encodes *absolute*
luminance and Skia's colour management is relative, so sRGB white would land at 10,000 nits
and putting it back at 240 means a linear scale applied before a non-linear encode — which
is not what a colour filter does, and not something the colour space can express either.
That is a new investigation with its own unknowns, in exchange for bandwidth that is not
currently the constraint. Worth revisiting if it ever is.

**3 — The HDR swapchain. Built on Windows.** The format now follows the display rather than
an environment variable: `IsHdrDisplay` decides, `HULTUI_VULKAN_F16` overrides it either way
(1 forces scRGB on, 0 forces it off), and the SDR white level comes from the display too.
`VK_EXT_swapchain_colorspace` is enabled whenever the loader has it, not only when wide
colour is wanted at startup — an instance cannot gain an extension later and a window can be
dragged onto an HDR display at any time.

`SDL_EVENT_WINDOW_DISPLAY_CHANGED` re-asks, and rebuilds the swapchain only when the answer
changes. **Tested both ways** on a machine with one HDR monitor and one SDR one:

```
display changed: hdr=False whiteScale=1  format unchanged      (SDR monitor, 80 nits)
display changed: hdr=True  whiteScale=3  rebuilding for scRGB  (HDR monitor, 240 nits)
display changed: hdr=False whiteScale=1  rebuilding for sRGB   (back again)
```

SDL emits several of those events per move; the guard means one rebuild each way and the
rest say so.

**The white level is per monitor**, which only shows on a mixed setup: the query follows the
window's display through `MonitorFromWindow` and matches the GDI device name against the
display configuration's source name. Asking whichever active path answered first — which is
what it did before there was a mixed setup to try — is answering about the wrong screen half
the time.

**HDR metadata is deliberately not sent.** `VK_EXT_hdr_metadata` is present on this device,
but `VkHdrMetadataEXT` wants the display's maximum and minimum luminance and the content's
light levels. SDL's binding here has neither `SDR_WHITE_POINT` nor `HDR_HEADROOM`; the white
point came from the Windows display configuration API and maximum luminance would need DXGI
as well. Sending invented numbers is worse than sending none — a display may tone map to
them — and for an scRGB swapchain Windows composites from the display's own profile anyway.
It belongs with HDR10, where the numbers are load-bearing.

**Linux, now run.** X11 indeed cannot: it offers only `SRGB_NONLINEAR` and reports no HDR,
correctly. Wayland does the whole thing — 28 surface formats including
`R16G16B16A16Sfloat/ExtendedSrgbLinearEXT`, SDL reports HDR when the monitors are in HDR mode,
and phase 3 picks scRGB by itself with nothing forced.

**But the white level is a different quantity there, and the window came out dim.** On Windows
SDR white is a property of the display, queried per monitor. On Wayland it is a **protocol
constant**: `color-management-v1` defines Windows-scRGB — which is what a compositor takes
`VK_COLOR_SPACE_EXTENDED_SRGB_LINEAR_EXT` to be — as 1.0 = 80 cd/m², and says that where a
reference white must be assumed it is **2.5375**, the BT.2408 reference of 203 cd/m². The
compositor maps that onto whatever its output drives SDR white at, so the number does not move
with the monitor and asking the monitor is the wrong move.

That matches what was found by eye before the protocol was read: on KWin with two HDR monitors
at 408 and 455 nits, 1 and 2.0 are visibly dim, 5.1 (408/80) too bright, and 2.54 matches the GL
window on **both** — which a scale proportional to the monitor's brightness could not have done.

Two traps worth stating. **SDL's own `SDR_white_level` is the wrong number for this path**: it
reads 1.0 on Wayland, following the EGL convention where 1.0 already *is* reference white, and
feeding it in reproduces the dim picture exactly. And **no capture can show this** — the F16 and
8-bit captures agree within 1 LSB, because the image is right and only the scale is wrong. Only
the screen shows it.

`SDL3Window.WhiteScale` now returns 2.5375 on Wayland. Mesa's WSI was not tested and may not
present scRGB as Windows-scRGB at all.

**4 — HDR video through the seam.**

**The starting point is not what this plan assumed.** Video already plays on the Vulkan
renderer, correctly, today — a 1080p file decodes and composites under the transport bar
with nothing added. `GpuFrameSink` chooses its path by whichever the surface asks for first,
and since `SkiaVulkanRenderSurface` is not an `IExternalGLSurface`, it falls through to the
pixel path and mpv uses its own software renderer. Nothing is broken; it is simply going the
long way round.

**What that costs, measured:** the same file, the same window, 10 seconds of wall clock —
**1.75 CPU-seconds on GL and 3.50 on Vulkan.** Exactly twice, and at 1080p. A 4K frame is
four times the pixels, so the gap widens with the content people actually want HDR for.

So phase 4 is not "make video work on Vulkan", it is "stop routing it through main memory",
and the prize is that doubled CPU plus the ability to carry more than 8 bits at all.

The route is phase 0's bridge, in the real renderer:

1. A GL context alongside the Vulkan device, on the same GPU — the window is created
   `SDL_WINDOW_VULKAN` and has none, so this is a second, hidden one. The spike already
   creates exactly this pair and checks the device UUIDs match.
2. A Vulkan `RGBA16F` image, exported and imported into that GL context as a texture with an
   FBO over it — `internal_format` on `mpv_opengl_fbo` stops being 0.
3. mpv renders into it as it already does on GL, and Skia draws the Vulkan image directly.
   No CPU copy anywhere.
4. ~~`target-colorspace-hint` then means something.~~ **This step was wrong — see below.**

**The seam is done** (`GpuFrameTarget`, `IExternalGLSurface.TryAcquireFrameTarget`,
`ISkiaExternalSurface.WrapFrame`). Target ownership can now sit on either side: the source
keeps making its own texture unless the renderer says it cannot composite one, in which case
the renderer lends a framebuffer and composites whatever it lent. A GL surface says nothing
and behaves exactly as before; the default implementation returns false.

Two things moved to make room for the second implementation. `GpuFrame` carries
`RendererOwned`, because a Vulkan surface has no meaningful texture name to put in
`TextureId`. And the render context no longer names `SkiaGLRenderSurface` and call its
`WrapTexture` — it holds an `ISkiaExternalSurface` and calls `WrapFrame`, since a method
named for a GL texture is one a Vulkan surface could never satisfy.

**What the ordering costs, measured.** The one thing that could have made this arrangement
worse than the copy it removes is synchronisation: GL and Vulkan write and read one image,
and something has to order them. The spike's `--syncbench` measures the crudest ordering
there is — `glFinish` on the GL side, a barrier submit and `vkQueueWaitIdle` on the Vulkan
side — as a stand-in for mpv rendering and Skia reading:

| | GL draw | + `glFinish` | + Vulkan wait | total |
|---|---|---|---|---|
| 1080p | 0.00 ms | 0.02 ms | 0.25 ms | **0.27 ms/frame** |
| 4K | 0.00 ms | 0.04 ms | 0.25 ms | **0.29 ms/frame** |

The cost is flat across resolutions, which says what it is: not the image being waited on,
but the fixed price of submitting a command buffer and waiting for the queue to drain — and
a renderer that already submits work every frame pays most of that regardless. At 24 fps it
is **7 ms of the 1000 ms in a second**, against the 175 ms/s the software copy costs at
1080p. Even at 60 fps it is 16 ms/s.

So external semaphores (`VK_KHR_external_semaphore` with `GL_EXT_semaphore`) would remove
the stall, but they are an optimisation rather than a precondition. Phase 4 can be built
with the blunt ordering and tightened later if a profile ever asks for it.

**Built, and working on Windows.** `SdlVulkanHost.Interop` makes the hidden GL context,
creates an exportable `RGBA16F` image, imports it into GL as a texture with a framebuffer over
it, and hands the Vulkan handle back to `SkiaVulkanRenderSurface` to draw. mpv is told the
framebuffer's internal format, so `internal_format` on `mpv_opengl_fbo` is no longer 0 and the
decoder stops assuming eight bits inside a target that holds sixteen.

Evidence, on an RTX 4080: `vulkan interop ready on NVIDIA GeForce RTX 4080/PCIe/SSE2` followed
by `vulkan interop image 320x180 RGBA16F`, and a captured swapchain frame that holds the clip
the right way up with the right colours. The capture is the only honest evidence there is —
the headless driver renders through `SkiaRasterRenderSurface` and never touches this backend.

**Three things make a failure harmless rather than fatal.** The GL context is not created until
the first frame that draws a video, so a window that never shows one pays none of the 130 ms it
costs. Every step that can fail — a missing extension, a GL context on the other GPU, an import
the driver refuses — answers by leaving `SupportsExternalGL` false, which sends the source down
its pixel path: the slower route that worked before, rather than a renderer compositing an image
it cannot read. And `HULTUI_VULKAN_INTEROP=0` refuses it outright, for a machine where it is
available and still wrong.

**The CPU saving, measured on Linux.** It could not be measured on Windows — an uncompressed
1080p test clip is about 75 MB/s of disk reads, larger than the copy being measured, and the
readings swung between 39 and 276 ms of CPU per second. On a compressed 1080p24 x264 clip, in a
release build, over a 10-second window after five seconds of warm-up, two rounds agreeing to 1%:

| Path | CPU |
|---|---|
| Vulkan, interop | **135 ms/s** |
| Vulkan, `HULTUI_VULKAN_INTEROP=0` | **810 ms/s** |
| OpenGL renderer | 122–127 ms/s |

So the interop removes about **675 ms of CPU per second** and puts the Vulkan renderer level
with GL, which is what the phase was for. Why the software path costs 810 ms/s there against the
~175 estimated on Windows is unexplained; the two Linux figures come from identical mpv settings,
so the comparison between them holds whatever that turns out to be.

**Linux works, on the NVIDIA proprietary driver.** Wayland and X11 both, the frame the right way
up with the right colours, no fallback to the software path, and the fd half of the handle
question behaves. Synchronisation is *cheaper* there: 0.16 ms a frame at both 1080p and 4K
against Windows' 0.27 on the same card. Validation printed nothing about this code across 159
swapchain recreations — in particular nothing about the `FinishExternalFrame` queue-family
transfer or its `GENERAL` layout, which was the part of phase 4 that is convention rather than
certainty. Details in `docs/design/HDR-LINUX-BRIEF.md`.

**The export cycle does not leak, on Windows.** `spikes/HdrInterop --churn` runs a thousand
export-import-release cycles and counts the process's handles: no change, against a control of
200 deliberately dropped handles that raises it by 200. The control is the point — the first two
versions of that test both reported a clean pass while being incapable of failing. One exported
the same allocation every time, and **NVIDIA hands back the identical Win32 handle for repeated
exports of one `VkDeviceMemory`**, so nothing could accumulate; the other read
`Process.HandleCount`, which comes through a performance counter that does not move per handle
and showed a flat line while 200 handles leaked. The renderer reallocates on a size change, so
the honest cycle reallocates too.

The same command runs on Linux and answers the fd half, with a control of its own: there, a
descriptor is consumed by the import, so the control exports one and never imports it.

**The scRGB path carries video correctly.** With the display in HDR, the frame goes through the
interop into the shared image, is drawn into the linear F16 offscreen as sRGB, and is multiplied
by the white scale on the way to the swapchain — and the captured result is identical to the
8-bit run to within 1 LSB (255,169,166 over 0,0,145 in both). That was the last combination
listed as untested on either platform.

**Still untested:** Mesa and a hybrid GPU, neither of which exists on the machines to hand.

**Phase 4 delivers the bits and the copy, but not yet the range.** Step 4 of the plan above
assumed `target-colorspace-hint` would start working once there was a real target colour space to
hint at. There still is no way to say what that target is. `mpv_opengl_fbo` carries `fbo`, `w`,
`h` and `internal_format` and nothing else, and no `MPV_RENDER_PARAM` describes a colour space —
the whole list is API_TYPE, OPENGL_INIT_PARAMS, OPENGL_FBO, FLIP_Y, DEPTH, ICC_PROFILE,
AMBIENT_LIGHT, WL_DISPLAY, ADVANCED_CONTROL, NEXT_FRAME_INFO, BLOCK_FOR_TARGET_TIME,
SKIP_RENDERING, the DRM pair and the SW group. The phase 0 finding stands unchanged, and nothing
about phase 4 altered it.

What follows is that **mpv still believes it is drawing to an SDR sRGB target**, and tone-maps HDR
content down before it ever reaches the half-float image. The interop removes the copy and carries
sixteen bits of precision; it does not yet carry HDR range. Every frame checked so far has been
SDR content, where this makes no difference and the numbers came out exact — which is precisely
why it did not show up.

The route is mpv's own options rather than a render param: `target-prim`, `target-trc` and
`target-peak`, set to describe the swapchain when it is scRGB. **Tried, and it works** — see
below.

### Why not just build libmpv with Vulkan?

The obvious question, and the answer is not a build flag. mpv v0.41.0's `render.h` defines
exactly two API types:

```c
#define MPV_RENDER_API_TYPE_OPENGL "opengl"
#define MPV_RENDER_API_TYPE_SW     "sw"
```

There is no `render_vk.h` in the source tree. The **render API** — the one where the host
provides the target and mpv draws into it — speaks OpenGL or CPU memory, and no
configuration adds a third.

mpv itself renders with Vulkan perfectly well: `--vo=gpu-next --gpu-api=vulkan`, through
libplacebo. But that path draws into a surface **mpv owns**, which for an embedded player
means `--wid` and a child window. That was considered and rejected near the top of this
document, for reasons that have not changed — the overlay can no longer composite onto the
picture, video stops being an element in the tree, and the windowing is per platform. It
would also need libplacebo with Vulkan and a shader compiler, which is the weight the
trimmed build exists to avoid.

So the GL-into-a-Vulkan-image bridge is not a workaround for having chosen badly. It is the
only arrangement where mpv keeps rendering through the API that embeds, and the pixels still
land somewhere a Vulkan renderer can use without a copy.

What is left is the implementation behind it, and the hard part is not the seam:

- **A GL context for a Vulkan window.** SDL cannot make one on a `SDL_WINDOW_VULKAN`
  window, so it means a second hidden window purely to host the context — which is what the
  phase 0 spike does, including checking the device UUIDs match.
- **Cross-API synchronisation, every frame.** GL writes the image and Vulkan reads it. The
  spike settled this with `glFinish` and a queue wait, which is correct and is a stall on
  both sides; doing it properly wants `VK_KHR_external_semaphore` with `GL_EXT_semaphore`.
  Until that is measured, it is not certain the 1.75 CPU-seconds saved are not partly spent
  again on stalls.

**5 — HDR range, then SDR UI over it.**

**The enabling question is answered.** `spikes/HdrInterop --range` renders a clip into the
shared image and reports what came back. Three findings, in order:

`target-trc` **does reach libplacebo through the OpenGL render API.** A mid-grey that arrives
encoded at 0.5020 comes back at 0.1921 with `target-trc=linear`, which is that value decoded
through gamma 2.4. The options are not ignored just because no render param carries them.

**SDR content cannot show range, whatever the target says.** Every combination above left the
maximum at exactly 1.0, because an SDR source has nothing brighter than reference white. Telling
the clip it is PQ BT.2020 on the way in (`vf=format=gamma=pq:primaries=bt.2020`) gives it
something to carry, and then `target-peak` opens the range: the maximum goes to 4.9258.

**`target-peak` sets the range, and the normalisation is exact:**

| `target-peak` | maximum written |
|---|---|
| 203 | 1.0000 |
| 406 | 2.0000 |
| 1015 | 5.0000 |

So **libplacebo's linear output puts 1.0 at 203 nits** — the BT.2408 reference white, to four
decimals. That is the same 203 the Wayland colour-management protocol names as the reference
white of Windows-scRGB, which is where the 2.5375 in `SDL3Window.WhiteScale` comes from. The two
ends of this pipeline already agree on the constant, and the conversion is therefore

> mpv linear (1.0 = 203 nits) → scRGB (1.0 = 80 nits) is a multiply by **2.5375**

which is exactly the scale the Wayland path already applies to the whole frame.

**What that leaves to build.** A seam, because `HultUI.Media` has no idea what colour space the
renderer chose: the renderer knows it lent an scRGB target and what the display's peak is, and
the backend needs both to set `target-trc`, `target-prim` and `target-peak`. Then the video image
has to be drawn as *linear* rather than sRGB-encoded, which is a different `SKColorSpace` on the
wrap in `WrapFrame`. Neither is large; both are untested against real HDR content, which neither
machine has.

**Built, working, and on by default** (`HULTUI_VIDEO_HDR=0` turns it off). The seam carries
`WantsLinearLight` and `PeakNits` down to the producer and `ReferenceWhiteNits` back up;
`MpvBackend` sets `target-prim`/`target-trc`/`target-peak` and reports libplacebo's 203;
`WrapFrame` declares the image linear and scales it into the canvas.

**Measured end to end on a real HDR10 film** (2001, PQ/BT.2020, mastering peak 1000.6 nits),
at two points in the pipeline in the same run:

| | shared image | swapchain |
|---|---|---|
| `HULTUI_VIDEO_HDR=0` | max **1.000**, none above 1 | max **3.000** (= SDR white), none above |
| `HULTUI_VIDEO_HDR=1` | max **4.766**, 1.2M samples above 1 | max **7.180**, 257k samples above SDR white |

4.926 appeared in another sample, which is 1000/203 exactly: the mastering peak, arriving
intact. And the 0..1 offscreen does **not** clamp — 7.18 passed through it — so the worry that
phase 2's design would block this was unfounded.

**Two false conclusions on the way there, both from the same mistake.** 2001 opens on a
three-minute black overture, and several measurements were taken at frame 60, about 2.5 seconds
in. A black frame reports a maximum of 0.000 or 1.000 and reads exactly like a producer that
tone-mapped everything away, so the pipeline was twice declared broken when it was working and
the film was simply black. The sample now takes `--start=<seconds>`, and the probe takes six
spaced samples rather than one, because one landed on black again even after the seek.

**The measurement that settled it** was reading the shared image back **through GL**, inside
`RunExternalGL` right after mpv renders (`HULTUI_VIDEO_HDR_PROBE=<n>`). The equivalent read
through Vulkan needs the image's layout, which Skia owns and moves; guessing it returned a
uniform 0.000 from an image that plainly had a picture in it. GL has no layouts.

**Why on by default.** A renderer that goes to the trouble of picking an HDR swapchain and then
asks the decoder for a tone-mapped SDR picture has thrown away the only thing the swapchain was
for. The blast radius is also smaller than it sounds: `HULTUI_RENDERER=vulkan` is itself opt-in,
the request is only made on an scRGB swapchain, and `MpvBackend` is the only producer in the tree
that implements `IGpuFrameRenderer` — anything else writes into the pixel ring and never sees it.
A future producer that ignores the request keeps writing encoded pixels and reports
`ReferenceWhiteNits` of zero, so the seam degrades without it knowing this exists.

**Still open:** the luminance question below.

**And then the original question.** A white transport bar at scRGB 1.0 next to a 1000-nit
highlight looks grey; at the highlight's level it is blinding. Everything the overlay draws needs
a deliberate luminance, and that is a design decision per app rather than a renderer setting. It
now has a number attached: on Windows the UI's scale comes from the display's own SDR white (240
nits here, so 3.0) while video reference white wants 2.5375, so UI white and video white sit
**18% apart** unless one of them is made to yield.

**Superseded text:** **5 — SDR UI over HDR video.** A white transport bar at scRGB 1.0 next to a 1000-nit
highlight looks grey; at the highlight's level it is blinding. Everything the overlay
draws needs a deliberate luminance, and that is a design decision per app rather than a
renderer setting.

## Traps

Everything below cost hours, and most of it was nearly shipped as a false green. They are
grouped by **shape** rather than by phase, because the shapes recur and the phases do not: the
same four mistakes were made repeatedly, in different clothes, by people who had already written
the previous one down.

### 1. Measurements that cannot fail

The commonest and the worst. A check that reports the same thing whether the code works or not
reads as evidence and is not.

- **A frame with nothing in it looks exactly like a pipeline that lost the picture.** Both report
  a maximum of 0.0 or 1.0. Three separate wrong conclusions came from capturing 2001 at frame 60,
  2.5 seconds into a three-minute black overture — twice *after* the lesson had been written down.
  Take several spaced samples, and prove the content was there before reading anything into a
  maximum.
- **A maximum cannot see dithering.** That is a property of gradients. Every check here was a
  peak value until one was not, and an 8-bit target dithered inside a 16-bit surface went
  unnoticed for two phases.
- **An 8-bit test clip cannot show 8-bit quantisation** — it is already on the grid being looked
  for. The dithering above needed a 10-bit ramp to appear at all.
- **`Process.HandleCount` does not move per handle.** It comes through a performance counter and
  sat flat at 492 while 200 handles leaked. `GetProcessHandleCount` is exact.
- **Re-exporting one allocation is not a leak test.** NVIDIA on Windows returns the *identical*
  `HANDLE` every time, so nothing accumulates whatever you do with it. NVIDIA on Linux returns a
  distinct fd per export — the same test, opposite behaviour. Reallocate each cycle, as the
  renderer does.
- **The headless driver proves nothing about this backend.** It renders through
  `SkiaRasterRenderSurface` and never touches a GPU backend, so a GL-versus-Vulkan screenshot
  comparison is identical whatever either one does.
- **A capture proves the image, never the screen.** And some things no capture can show at all:
  the SDR white scale is invisible to one, because F16 and 8-bit captures agree within 1 LSB
  while the window is plainly dim — the image is right and the compositor is obeying it.
- **An idle window may never reach the default capture frame.** It presents 3, 4, or 1 frames
  depending on which output it opened on; the default is images + 1. A missing capture can mean
  an idle window rather than a renderer that drew nothing.

**So: build the positive control first.** `--churn` deliberately drops 200 handles and refuses to
report a verdict unless that raises the count. Two earlier versions of that test reported a clean
pass while being incapable of failing.

### 2. State another API owns, and moves

Anything holding a handle to something Skia or a driver also tracks will go stale, silently.

- **Never cache a `GRBackendRenderTarget` over a swapchain image.** Skia tracks the layout inside
  the target and this backend transitions to `PRESENT_SRC_KHR` behind it. Three images meant
  three good frames and then black for ever. Build it fresh each frame declaring `UNDEFINED`.
- **Never cache a renderer-owned `SKImage` either**, for the same reason — `DrawGpuFrame`
  re-wraps whenever `frame.RendererOwned`.
- **Do not read the shared image through Vulkan.** It needs the layout Skia owns; guessing
  returned a uniform 0.000 from an image that plainly had a picture in it. Read it through **GL**
  (`glReadPixels` inside `RunExternalGL`) — GL has no layouts. That is what
  `HULTUI_VIDEO_HDR_PROBE` does.
- **Handle ownership is asymmetric across platforms.** GL does *not* take an `OPAQUE_WIN32`
  handle, so the caller must `CloseHandle`; `glImportMemoryFdEXT` *consumes* the descriptor it is
  given. Getting it backwards leaks on one platform and double-frees on the other, and neither
  shows on the first import.
- **`GL_DEDICATED_MEMORY_OBJECT_EXT` is required** when Vulkan allocated the memory dedicated,
  which it always does here. Without it the result is undefined, and on NVIDIA-Linux undefined
  was a texture that read as black above about 2560x1440 — no GL error, complete framebuffer,
  every 4K film black. Windows happened to work, which is how it shipped.

### 3. Defaults that are not neutral

Several of these APIs treat "unspecified" as a decision, not as "work it out".

- **`MPV_RENDER_PARAM_DEPTH` omitted means 8 bits and dithering**, and mpv does *not* infer it
  from `internal_format`. Measured: omitted and `DEPTH=8` are byte-identical.
- **`internal_format = 0` on `mpv_opengl_fbo` means "assume 8"** for the same reason.
- **`target-colorspace-hint` is a no-op through the render API** and always was. No
  `MPV_RENDER_PARAM` names a colour space, and `mpv_opengl_fbo` carries only fbo, size and
  format. The way in is mpv's `target-prim` / `target-trc` / `target-peak` **options**.
- **Without `target-peak` nothing exceeds 1.0** however the transfer is set. The transfer alone
  changes the numbers and keeps the ceiling.
- **Skia returns a null surface with no diagnostic** when the swapchain lacks
  `VK_IMAGE_USAGE_TRANSFER_SRC_BIT`, or when `GRVkExtensions` or `VkPhysicalDeviceFeatures2` are
  left null — it then assumes the minimum and declines formats the device plainly supports.
- **SDL's `SDR_white_level` is the wrong number on Wayland.** It reads 1.0, the EGL convention
  where 1.0 already *is* reference white, and feeding it in reproduces a dim picture exactly.

### 4. One platform hiding the other's bug

Every one of these was invisible on the machine it was written on.

- **SDR white is a protocol constant on Wayland and a display query on Windows.**
  `color-management-v1` fixes Windows-scRGB reference white at 2.5375 (203 nits over scRGB's 80);
  Windows reports whatever its brightness slider says. Asking the monitor on Wayland is the wrong
  move.
- **KWin shows an sRGB window through a pure 2.2 gamma**, where Skia uses the piecewise curve.
  The two meet at white, so white matches and *shadows* do not — which is why it survived phase 2
  and 3 unnoticed. A colour-space tag cannot fix it, because Skia converts every `SKColor` from
  piecewise sRGB into whatever the canvas claims to be.
- **`glXGetProcAddress` returns a dispatch stub for any name at all** under libglvnd. Skia asked
  for `eglQueryString` on X11, got a stub, parsed garbage, and segfaulted on every GL launch. On
  Wayland the real functions come back and a made-up name resolves to 0.
- **Windows tolerates `SDL_GL_MakeCurrent` with a null context; Wayland dereferences it.**
- **A mixed-scale Wayland desktop reports an output change as two events**, new pixels at the old
  scale then the same pixels at the new one. Code that treats the second as a pure DPI change
  preserves a size already computed wrongly.
- **X11 cannot do HDR at all**, and says so honestly — only `SRGB_NONLINEAR`.

**So: a green suite on one OS says nothing about the other here.** The Linux session found a bug
in this Windows work and this Windows session found one in theirs, both within a day, both in
code the author had verified.

### The display's peak, which was assumed and is now asked for

`target-peak` decides where tone mapping lands, and it was a flat assumed 1000 nits because
neither platform appeared to expose a peak. SDL does, as **headroom** — a multiple of SDR white
rather than an absolute luminance, which turns out to be the more useful of the two. Multiplied
by whatever 1.0 already means here it gives an absolute peak on both platforms with no branch:

> peak nits = `WhiteScale()` x 80 x `SDL.window.HDR_headroom`

which is the display's own SDR white on Windows and the 203-nit protocol reference on Wayland,
where SDL's `SDR_white_level` reads a useless 1.0 but its headroom is real compositor data.

**This machine's display peaks at 455 nits, not 1000**, so the assumption was wrong by more than
a factor of two and wrong in the worst direction — mpv was tone-mapping a film to 1000 nits for a
panel that clips at 455. Measured, same frame, shared image peak:

| `target-peak` | shared image max |
|---|---|
| queried (455) | 2.240 |
| forced 1000 (the old behaviour) | 4.926 |

2.240 x 203 = 455 exactly. `HULTUI_VULKAN_PEAK_NITS` still overrides.

A headroom of exactly 1 is a real answer and is treated as one: it says the display peaks at its
own SDR white, which the Linux machine nearly does (1.0022). Only a missing number falls back to
assuming.

### Synchronisation, measured and left alone

Phase 1 said the CPU stalled once a frame and that fixing it was "the first thing to do if
this backend is ever to ship". Half of that was already fixed and the comment had not caught
up: the queue drain after Skia submits was replaced by a semaphore the present waits on.

The other half is the acquire, still a fence because SkiaSharp 3.119.2 has nothing to hand a
semaphore to — no `GRFlushInfo`, no `GRBackendSemaphore`, and only `gr_direct_context_flush`
and `flush_and_submit` in its binding, neither taking flush info. **That fence costs 0.0064 ms
a frame**, over 120 frames of continuous animation paced to a 120 Hz display. Replacing it
moves six microseconds from the CPU to the GPU.

Where Vulkan does cost more than GL is a fixed **0.15 to 0.25 ms a frame**, and only where the
frame is small enough to notice: `select row` 0.43 against 0.59 ms, `clear` 0.27 against 0.41,
while `create 10,000` is 316.6 against 319.8 and `append 1,000` is actually faster on Vulkan.
That is consistent with the two things this backend does that GL does not — an extra
command-buffer submit, and a render target rebuilt every frame because Skia cannot be told an
image's layout. Both are the same binding limitation, and neither is a stall.

**So the synchronisation work is not the blocker it was written up as, and is not being done.**
What it was blocking — making Vulkan the default renderer — is now a question about Mesa, a
hybrid GPU and a 0.2 ms frame cost, not about a stall.

## Zero-copy decode, afterwards

`hwdec` is `auto-copy` today — every frame goes GPU → CPU → GPU. That is affordable at
1080p SDR and wrong at 4K HDR, where it is roughly 24 MB per frame.

It was going to belong **after** the above, on the reasoning that the destination for a
zero-copy frame is the Vulkan image from phase 0 — `hwdec=auto` keeps frames in GPU memory,
mpv renders them into the same imported target, nothing new needed on the receiving side.

**That reasoning was wrong, and investigating it first is what showed why.**

**`hwdec=auto` is already in use and is not zero-copy.** It resolves to `d3d11va-copy` here.
So does `auto-copy`. Asking for `d3d11va`, `dxva2`, `nvdec` or `cuda` explicitly gives
`hwdec-current = no` — software decoding — because **this libmpv is built with every
zero-copy interop disabled**, as its own configuration line says:

```
-Dd3d11=disabled -Ddirect3d=disabled -Dgl-win32=disabled
-Dgl-dxinterop=disabled -Degl-angle=disabled
```

The `-copy` variants survive because the decoder is FFmpeg's and needs no GL interop at all.
Those flags were set to strip *window output* backends — "the framework's GL context is the
only display" — and stripping display interop stripped decode interop with it, which is a
different thing and is the thing this phase needs.

**And on Windows the flag change would not help.** With a desktop WGL context the only
zero-copy bridge is `hwdec_dxva2gldx`, and it shares a `D3DFMT_X8R8G8B8` surface and hands
back `IMGFMT_RGB0` — **8-bit RGB**. It would deliver a zero-copy path that throws away
exactly what phases 2 to 5 were built to carry. The 10-bit bridge is `hwdec_d3d11egl`, which
needs an **ANGLE EGL** context; ANGLE is a D3D11 implementation of GL ES and will not import
Vulkan memory through `GL_EXT_memory_object`, so adopting it would cost the interop that the
whole design rests on.

**So zero-copy on Windows is incompatible with this architecture**, not merely unbuilt. The
copy stays, and what it costs is now known: **468 ms of CPU per second** for 4K HEVC HDR
(against 921 with hardware decoding off altogether, so the decoder is doing real work).

**Linux is a different answer.** `hwdec_cuda` and `hwdec_vaapi` map the hardware format
itself (`IMGFMT_CUDA`, `IMGFMT_VAAPI`) and keep the real subformat, so they carry 10 bits.
Both are disabled in that build too (`-Dvaapi=disabled -Dvdpau=disabled`, and no ffnvcodec),
so it is the same native-build change — but there it could actually deliver. Unverified:
whether CUDA-GL or VAAPI-EGL interop works in the hidden GL context beside a Vulkan device,
which is the one question that decides it.

**Cost of finding this out:** two build flags and an afternoon, against rebuilding FFmpeg and
mpv first and then discovering the Windows path is 8-bit.

**What to run there is written down**: `docs/design/HDR-LINUX-ZEROCOPY.md`, ordered so that the
step which can end the project — measuring what the copy actually costs, which needs no rebuild —
comes first, and the one that decides the approach — whether CUDA can register a texture whose
storage is Vulkan-owned memory — comes before the build rather than after it.

**Answered on Linux, and it works** (results in that file). The Linux machine was never running
the trimmed build — it loads the distribution's libmpv, which has `cuda-interop` — so
`hwdec=nvdec` was available all along: `hwdec-current = nvdec`, ten bits and the full range
arrive intact, and 4K CPU drops from ~400 to ~155 ms/s. The crux was not one: mpv registers its
*own* plane textures with CUDA and renders from them into our FBO like any other frame.

The catch is that `auto-copy` is currently what makes the backend portable — the copy
hides every difference between NVDEC, VAAPI, D3D11VA and VideoToolbox. Turning it off
means those differences become ours, and a per-platform fallback matrix with it.

## What this costs

Phases 0–1 are the bulk of the engineering. Phase 2 is the one most likely to produce
visible regressions in apps that have nothing to do with video. Phase 3 is small but
untestable without an HDR display and an HDR-capable compositor on each platform.

Headless tests carry on unaffected throughout: they run on `SkiaRasterRenderSurface` and
never touch a GPU backend, which is why none of this shows up in CI either way. The
verification for all of it is a real window on a real machine.

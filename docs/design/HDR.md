# HDR output

**Status:** phase 0 done and it passed — see `spikes/HdrInterop`. Nothing else built.
**Date:** 2026-09-25.

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

Still true, and still untested: on Linux this needs the Wayland colour-management protocol,
and X11 simply cannot.

**4 — HDR video through the seam.** mpv renders into an `RGBA16F` FBO — the field is
already there and currently passed as zero:

```csharp
var fbo = new mpv_opengl_fbo { fbo = (int)framebuffer, w = width, h = height, internal_format = 0 };
```

With `target-colorspace-hint` mpv hands back linear HDR rather than tone-mapped SDR, and
Skia composites the overlay over it in the same space.

**5 — SDR UI over HDR video.** A white transport bar at scRGB 1.0 next to a 1000-nit
highlight looks grey; at the highlight's level it is blinding. Everything the overlay
draws needs a deliberate luminance, and that is a design decision per app rather than a
renderer setting.

## Zero-copy decode, afterwards

`hwdec` is `auto-copy` today — every frame goes GPU → CPU → GPU. That is affordable at
1080p SDR and wrong at 4K HDR, where it is roughly 24 MB per frame.

It belongs **after** the above, not before, because the destination for a zero-copy frame
is the Vulkan image from phase 0: `hwdec=auto` keeps frames in GPU memory and mpv renders
them into the same imported target, so nothing new is needed on the receiving side once
that target exists. Doing it first would mean building it twice.

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

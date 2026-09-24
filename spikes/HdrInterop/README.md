# HDR interop spike — phase 0

**Result: the sharing works on both platforms.** NVIDIA RTX 4080, Windows and Linux,
2026-09-25. What mpv then draws into the shared image is confirmed on Windows and still
open on Linux — see *Where it stands* below.

This is phase 0 of [`docs/design/HDR.md`](../../docs/design/HDR.md): the one experiment
that decides whether HDR is reachable on Windows *and* Linux at all.

## The question

HDR needs an HDR-capable swapchain, and OpenGL on Windows cannot ask for one. Vulkan can,
on both platforms — but **mpv's render API is OpenGL-only**, and there is no Vulkan type
upstream. So a Vulkan-drawn window has a GL texture on one side and a Vulkan image on the
other, and they have to be the same memory.

If that does not work on real drivers, HDR is Windows-only and the design is wrong.

## What it does

1. Creates a Vulkan device and one `RGBA16F` image whose memory is exportable.
2. Exports the memory (`VK_KHR_external_memory_win32`, or `_fd` on Linux).
3. Imports it into a GL context (`GL_EXT_memory_object_win32` / `_fd`), builds a texture
   over it and an FBO over that.
4. **Clears the FBO from GL and reads the image back through Vulkan.** If the colour
   survives, the two APIs are looking at one allocation.
5. With a file argument, has mpv decode into the same FBO and reads back again, which is
   the case that actually matters.

It also compares the Vulkan device UUID with `GL_DEVICE_UUID_EXT`. On a machine with two
GPUs those differ and the import fails in ways that look like driver bugs.

## Running it

```bash
dotnet run --project spikes/HdrInterop
```

That does steps 1–4 and needs nothing but a GPU. For step 5, pass a video file — or a
`.rawvideo` one, which the spike configures the demuxer for, since this build of libmpv is
trimmed and has no lavfi to generate a test source with:

```bash
dotnet run --project spikes/HdrInterop -- "D:\films\Stalker.mkv"
```

`HULTUI_MPV_PATH` points at a libmpv if one is not beside the binary.

## What came out

```
[1] Vulkan device      : NVIDIA GeForce RTX 4080
    image              : 256x144 RGBA16F, 524288 bytes
[2] GL renderer        : NVIDIA GeForce RTX 4080/PCIe/SSE2
    GL_EXT_memory_object      : yes
    same GPU as Vulkan        : yes
[3] exported Win32 handle : 0x648
    imported into GL          : ok
    texture + FBO over it     : ok
[4] GL cleared to          : 0.25, 0.5, 0.75
    Vulkan read back         : 0.25, 0.5, 0.75
[5] mpv → the shared image : clip.rawvideo
    read back                : min 0, max 0.996, mean 0.495
```

Step 4 is the interop; step 5 is mpv using it without complaint or a GL error.

## Where it stands

| | Windows | Linux |
|---|---|---|
| Export / import (steps 1–4) | pass | pass (`fd` 93) |
| Same GPU, UUIDs match | yes | yes |
| mpv into the shared image (step 5) | pass — real range | **flat white**, `min 0.996 max 1` |

Steps 1–4 are the experiment. They pass on both, which is the result phase 1 needed.

Step 5 on Linux wrote *something* — the clear colour was gone — but every pixel came back
at 1.0. Two things differ from the Windows run at once, so neither is yet the cause: the
platform, and the file. Windows used this spike's synthetic BGRA clip, which carries no
colour metadata at all; Linux used a real 1080p H.264 MKV, which is tagged BT.709.

The prime suspect is `target-colorspace-hint`, which the spike used to set unconditionally.
It tells mpv to render for the target's colour space — and the API user is then expected to
say what that space is, through the render params, which this spike does not. An unstated,
very bright target is exactly what would clip SDR content to white. **It is now off unless
you pass `--hint`**, so the two can be compared. On Windows with the untagged raw clip it
makes no difference either way, which is consistent with the suspicion rather than proof of
it — untagged content gives mpv nothing to convert.

To settle it, on the Linux machine with the same file:

```bash
dotnet run --project spikes/HdrInterop -- "/path/to/file.mkv"          # hint off
dotnet run --project spikes/HdrInterop -- "/path/to/file.mkv" --hint   # hint on
```

Each run now prints five sampled pixels and writes the readback to
`/tmp/hdr-interop-readback.ppm`, which any image viewer opens. Looking at the frame settles
in a second what the statistics argue about — and a frame that is genuinely white (a title
card, a flash) looks like one.

## What this does **not** prove

**One GPU, two platforms.** AMD and Intel are untested on both, and Linux has only been
run on NVIDIA's proprietary driver — Mesa's `GL_EXT_memory_object_fd` support varies, and
that is where an fd export is most likely to behave differently.

Nor does it say anything about performance, about HDR itself (the image is `RGBA16F` but
nothing here presents it), or about what happens when the image is resized under a live
swapchain.

## Notes for whoever builds phase 1

- **Vortice.Vulkan 3.x generates the structs but not the commands.** Only eight global
  entry points are methods on `Vortice.Vulkan.Vulkan`; everything past `vkCreateInstance`
  comes from `vkGetInstanceProcAddr` / `vkGetDeviceProcAddr`. `VkCmd.cs` is that loader and
  is the part most worth keeping.
- **Dedicated allocation matters.** `VkMemoryDedicatedAllocateInfo` is not formally
  required for this, and the drivers that misbehave without it do so quietly.
- **Both halves have to declare the sharing**: the image through
  `VkExternalMemoryImageCreateInfo`, *and* the allocation through
  `VkExportMemoryAllocateInfo`. Leaving either out fails at export, not at creation.
- **`glTextureParameteri(GL_TEXTURE_TILING_EXT, GL_OPTIMAL_TILING_EXT)` before
  `glTextureStorageMem2DEXT`**, to match `VK_IMAGE_TILING_OPTIMAL` on the Vulkan side.
- The mpv side is the ordinary render-API path from `MpvBackend`, with `internal_format`
  left at 0 so the FBO's existing `RGBA16F` storage is used.

This directory is deliberately **not** in `HultUI.slnx`: it is throwaway, and nothing in CI
should build it or carry its Vulkan dependency.

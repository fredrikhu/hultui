# HDR interop spike — phase 0

**Result: it works, on both platforms, end to end.** NVIDIA RTX 4080, Windows and Linux,
2026-09-25.

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

Four runs — two platforms × hint on and off — on the same 1080p H.264 file:

| | Windows | Linux |
|---|---|---|
| Export / import (steps 1–4) | pass | pass (`fd`) |
| mpv into the shared image | `min 0 max 1 mean 0.133` | `min 0 max 1 mean 0.133` |
| With `--hint` | identical | identical |

The sampled pixels agree to two decimals across every run:
`(0.07,0.11,0.13) (0.13,0.17,0.19) (0.06,0.09,0.13) (0.02,0.02,0.02) (0.07,0.07,0.09)`.

**Windows and Linux produce the same numbers**, which is a stronger result than either run
alone: the same frame decoded through the same path lands identically whether the memory
crossed as a Win32 handle or an fd. The raw output is in `results/`.

### The earlier white frame was a white frame

An earlier Linux run came back `min 0.996 max 1` and I guessed at
`target-colorspace-hint` clipping SDR content. **That was wrong.** The file simply opened on
a white frame. With a different file the same command gives a normal dark scene, and
`--hint` changes nothing at all — the numbers are identical with and without it, on both
platforms.

That is worth knowing rather than just correcting: with no render params describing the
target, the hint is a **no-op**, not a hazard. It only starts doing anything in phase 4,
when there is a real swapchain with a real colour space to hint at. It stays behind
`--hint` so that phase can turn it on deliberately.

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

# HDR interop spike — phase 0

**Result: it works.** On Windows, NVIDIA RTX 4080, driver as of 2026-09-25.

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

## What this does **not** prove

**One GPU, one driver, one platform.** AMD and Intel on Windows are untested, and so is
every Linux driver — where the export is an fd rather than a handle and Mesa's
`GL_EXT_memory_object_fd` support varies by driver. The spike runs on Linux unchanged; it
has simply not been run there.

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

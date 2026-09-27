using System.Runtime.InteropServices;
using HultUI.Core.DevTools;
using HultUI.Core.Rendering;
using SDL;
using Vortice.Vulkan;
using static SDL.SDL3;
using static HultUI.Platform.SDL3.VulkanCommands;

namespace HultUI.Platform.SDL3;

/// <summary>
/// One image shared between this window's Vulkan device and a hidden OpenGL context —
/// phase 4 of <c>docs/design/HDR.md</c>.
///
/// <para><b>Why there is a second context at all.</b> mpv renders with OpenGL and nothing
/// else: its render API defines <c>opengl</c> and <c>sw</c>, and no build flag adds a third.
/// A Vulkan window has no GL context, so before this the video went through <c>sw</c> — a
/// decode into main memory, a copy out, an upload back — which works, costs twice the CPU at
/// 1080p, and cannot carry more than eight bits.</para>
///
/// <para>So a GL context is made beside the Vulkan device, and the two are given one
/// allocation rather than a copy each: Vulkan creates an image whose memory is exportable,
/// GL imports that memory as a texture, mpv renders into a framebuffer over it, and Skia
/// draws the Vulkan image. Nothing crosses the bus twice. Phase 0 proved drivers allow it;
/// <c>spikes/HdrInterop</c> is that proof and this is the same sequence in the real
/// renderer.</para>
///
/// <para><b>Everything here is lazy.</b> Loading a GL driver costs about 130 ms and most
/// windows never show a video, so none of it happens until the first frame that asks. A
/// failure at any step is not an error: <see cref="TryEnableInterop"/> answers false, the
/// renderer stops offering itself as an external GL surface, and the video takes the
/// software route it took before.</para>
/// </summary>
internal sealed unsafe partial class SdlVulkanHost : IVulkanInteropHost
{
    // The image is RGBA16F on both sides. Half-floats are the point: an 8-bit shared texture
    // would remove the copy and keep the bit depth that makes HDR impossible.
    private const uint GlRgba16f = 0x881A;
    private const VkFormat InteropFormat = VkFormat.R16G16B16A16Sfloat;

    // COLOR_ATTACHMENT because GL renders into it, SAMPLED because Skia draws it, and the
    // two transfer bits because Skia validates images against them the same way it validates
    // the swapchain.
    private const uint InteropUsage =
        UsageColorAttachment | UsageSampled | UsageTransferSrc | UsageTransferDst;

    private SDL_Window* _glWindow;
    private SDL_GLContextState* _glContext;
    private InteropGl? _gl;

    /// <summary>Null until the first attempt, then the answer for good. A driver does not
    /// gain external-memory support while the process runs.</summary>
    private bool? _interopReady;

    private VkImage _interopImage;
    private VkDeviceMemory _interopMemory;
    private ulong _interopMemorySize;
    private uint _glMemoryObject, _glTexture, _glFramebuffer;
    private int _interopWidth, _interopHeight;

    /// <summary>Whether GL has drawn into the image since the last barrier. Kept so that a
    /// <see cref="RunOnGLContext"/> which only set something up does not pay for a submit.</summary>
    private bool _interopDirty;

    /// <summary>The same resolver the GL window uses, handed to whatever renders here — mpv
    /// loads its own GL through it.</summary>
    public Func<string, nint> GlGetProcAddress { get; } = ResolveGl;

    private static nint ResolveGl(string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name);
        Array.Resize(ref bytes, bytes.Length + 1);
        fixed (byte* p = bytes) return SDL_GL_GetProcAddress(p);
    }

    /// <summary>
    /// <c>HULTUI_VULKAN_INTEROP=0</c> refuses the shared image and leaves video on the
    /// software path.
    ///
    /// <para>There to be turned off. The interop is the faster path and the only one that can
    /// carry more than eight bits, but it leans on two drivers agreeing about one allocation,
    /// and a machine where that goes wrong should still be able to watch something. It is
    /// also how the two paths are measured against each other on one clip.</para>
    /// </summary>
    private static readonly bool InteropAllowed =
        Environment.GetEnvironmentVariable("HULTUI_VULKAN_INTEROP") != "0";

    public bool TryEnableInterop()
    {
        if (_interopReady is { } known) return known;
        _interopReady = false;

        if (_disposed || _device.IsNull) return false;
        if (!InteropAllowed) return Refuse("HULTUI_VULKAN_INTEROP=0");
        if (!_interopExtensionsEnabled)
            return Refuse("the Vulkan device has no external-memory extensions");
        if (!CreateHiddenGLContext()) return false;

        var gl = _gl!;
        if (!gl.HasMemoryObject)
            return Refuse("this GL driver has no GL_EXT_memory_object");
        if (!gl.HasPlatformImport)
            return Refuse(OperatingSystem.IsWindows()
                ? "this GL driver has no GL_EXT_memory_object_win32"
                : "this GL driver has no GL_EXT_memory_object_fd");

        // The one failure that looks like a driver bug rather than a configuration: on a
        // laptop the GL context can land on the integrated GPU while Vulkan picked the
        // discrete one, and importing across two devices fails obscurely further down.
        var glUuid = gl.DeviceUuid();
        var vkUuid = VulkanDeviceUuid();
        if (glUuid is null || !glUuid.AsSpan().SequenceEqual(vkUuid))
            return Refuse("GL is on a different GPU than Vulkan " +
                $"(GL {Describe(glUuid)} vs Vulkan {Convert.ToHexString(vkUuid)})");

        _interopReady = true;
        CreateSharedSemaphore(gl);
        StartupTrace.Mark($"vulkan interop ready on {gl.Renderer}" +
            (_sharedSemaphore.IsNull ? " (glFinish ordering)" : " (semaphore ordering)"));
        return true;

        bool Refuse(string why)
        {
            StartupTrace.Mark($"vulkan interop unavailable: {why}");
            Console.Error.WriteLine($"[vulkan] video stays on the software path: {why}");
            return false;
        }

        static string Describe(byte[]? uuid) =>
            uuid is null ? "(unknown)" : Convert.ToHexString(uuid);
    }

    /// <summary>
    /// A hidden one-pixel GL window, purely to own a context.
    ///
    /// <para>The real window is <c>SDL_WINDOW_VULKAN</c> and cannot also be
    /// <c>SDL_WINDOW_OPENGL</c>, so the context needs a window of its own. Nothing is ever
    /// drawn to it or swapped on it; it exists because a GL context is made from a
    /// window.</para>
    ///
    /// <para>The context is made current here and left current. The render thread is the only
    /// thread that touches it, the Vulkan side does not care what GL has bound, and entry
    /// points resolved through WGL belong to the context that was current when they were
    /// resolved — so resolving after making it current is not a detail that can be
    /// moved.</para>
    /// </summary>
    private bool CreateHiddenGLContext()
    {
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, 4);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, 5);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_PROFILE_MASK,
            (int)SDL_GLProfile.SDL_GL_CONTEXT_PROFILE_CORE);

        _glWindow = SDL_CreateWindow("hultui-interop", 1, 1,
            SDL_WindowFlags.SDL_WINDOW_OPENGL | SDL_WindowFlags.SDL_WINDOW_HIDDEN);
        if (_glWindow is null)
        {
            StartupTrace.Mark($"vulkan interop: SDL_CreateWindow failed: {SDL_GetError()}");
            return false;
        }

        _glContext = SDL_GL_CreateContext(_glWindow);
        if (_glContext is null)
        {
            StartupTrace.Mark($"vulkan interop: SDL_GL_CreateContext failed: {SDL_GetError()}");
            SDL_DestroyWindow(_glWindow);
            _glWindow = null;
            return false;
        }

        SDL_GL_MakeCurrent(_glWindow, _glContext);
        _gl = new InteropGl();
        return true;
    }

    public void RunOnGLContext(Action draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        if (_glContext is null) return;
        SDL_GL_MakeCurrent(_glWindow, _glContext);
        draw();
    }

    public VulkanInteropImage? LendInteropImage(int widthPx, int heightPx)
    {
        if (_interopReady != true || widthPx <= 0 || heightPx <= 0) return null;
        if (_interopImage.IsNull || _interopWidth != widthPx || _interopHeight != heightPx)
        {
            DestroyInteropImage();
            if (!CreateSharedImage(widthPx, heightPx)) return null;
        }

        _interopDirty = true;
        return new VulkanInteropImage(
            Image: _interopImage.Handle,
            Memory: _interopMemory.Handle,
            MemorySize: _interopMemorySize,
            Format: (uint)InteropFormat,
            Usage: InteropUsage,
            // What FinishExternalFrame leaves it in, which is the only layout the renderer
            // ever sees it in.
            Layout: (uint)VkImageLayout.ShaderReadOnlyOptimal,
            WidthPx: widthPx,
            HeightPx: heightPx,
            GlFramebuffer: _glFramebuffer,
            GlTexture: _glTexture,
            GlInternalFormat: GlRgba16f);
    }

    /// <summary>
    /// The image, its exported handle, and the GL objects over it.
    ///
    /// <para>Two declarations are needed and either one alone fails at export rather than at
    /// creation: the image says it will be shared
    /// (<see cref="VkExternalMemoryImageCreateInfo"/>) and the allocation says the same
    /// (<see cref="VkExportMemoryAllocateInfo"/>).</para>
    /// </summary>
    private bool CreateSharedImage(int width, int height)
    {
        var gl = _gl!;
        var handleType = OperatingSystem.IsWindows()
            ? VkExternalMemoryHandleTypeFlags.OpaqueWin32
            : VkExternalMemoryHandleTypeFlags.OpaqueFD;

        var externalInfo = new VkExternalMemoryImageCreateInfo
        {
            sType = VkStructureType.ExternalMemoryImageCreateInfo,
            handleTypes = handleType,
        };
        var imageInfo = new VkImageCreateInfo
        {
            pNext = &externalInfo,
            imageType = VkImageType.Image2D,
            format = InteropFormat,
            extent = new VkExtent3D(width, height, 1),
            mipLevels = 1,
            arrayLayers = 1,
            samples = VkSampleCountFlags.Count1,
            tiling = VkImageTiling.Optimal,
            usage = (VkImageUsageFlags)InteropUsage,
            sharingMode = VkSharingMode.Exclusive,
            initialLayout = VkImageLayout.Undefined,
        };
        VkImage image;
        if (CreateImage(_device, &imageInfo, 0, &image) != VkResult.Success) return false;
        _interopImage = image;

        var requirements = new VkMemoryRequirements();
        GetImageMemoryRequirements(_device, image, &requirements);
        _interopMemorySize = requirements.size;

        var dedicated = new VkMemoryDedicatedAllocateInfo
        {
            sType = VkStructureType.MemoryDedicatedAllocateInfo,
            image = image,
        };
        var exportInfo = new VkExportMemoryAllocateInfo
        {
            sType = VkStructureType.ExportMemoryAllocateInfo,
            pNext = &dedicated,
            handleTypes = handleType,
        };
        var allocate = new VkMemoryAllocateInfo
        {
            pNext = &exportInfo,
            allocationSize = requirements.size,
            memoryTypeIndex = FindMemoryType(requirements.memoryTypeBits,
                VkMemoryPropertyFlags.DeviceLocal),
        };
        VkDeviceMemory memory;
        if (AllocateMemory(_device, &allocate, 0, &memory) != VkResult.Success)
            return FailImage("vkAllocateMemory");
        _interopMemory = memory;

        if (BindImageMemory(_device, image, memory, 0) != VkResult.Success)
            return FailImage("vkBindImageMemory");

        if (!ImportIntoGL(gl, width, height)) return FailImage("the GL import");

        _interopWidth = width;
        _interopHeight = height;
        StartupTrace.Mark($"vulkan interop image {width}x{height} RGBA16F ({requirements.size} bytes)");
        return true;

        bool FailImage(string what)
        {
            Console.Error.WriteLine($"[vulkan] the shared image failed at {what}");
            DestroyInteropImage();
            return false;
        }
    }

    /// <summary>
    /// The Vulkan allocation, as a GL texture with a framebuffer over it.
    ///
    /// <para>The tiling has to be set on the texture before its storage is declared, and it
    /// has to say OPTIMAL to match the image Vulkan made. A texture left at the default
    /// linear tiling over an optimally-tiled allocation reads as noise rather than
    /// failing.</para>
    /// </summary>
    private bool ImportIntoGL(InteropGl gl, int width, int height)
    {
        bool ok = false;
        RunOnGLContext(() =>
        {
            _glMemoryObject = gl.CreateMemoryObject();
            gl.MarkDedicated(_glMemoryObject);
            if (OperatingSystem.IsWindows())
            {
                nint handle = ExportWin32Handle();
                if (handle == 0) return;
                gl.ImportWin32(_glMemoryObject, _interopMemorySize, handle);
                // GL copies what it needs out of an OPAQUE_WIN32 handle rather than taking
                // ownership of it, so this side still has to close it. A file descriptor is
                // the other way round and glImportMemoryFdEXT consumes it.
                CloseHandle(handle);
            }
            else
            {
                int fd = ExportFd();
                if (fd < 0) return;
                gl.ImportFd(_glMemoryObject, _interopMemorySize, fd);
            }
            if (gl.Error() != 0) return;

            _glTexture = gl.CreateTexture();
            gl.SetOptimalTiling(_glTexture);
            gl.TextureStorageMem(_glTexture, GlRgba16f, width, height, _glMemoryObject);
            if (gl.Error() != 0) return;

            _glFramebuffer = gl.CreateFramebuffer();
            gl.AttachColour(_glFramebuffer, _glTexture);
            ok = gl.FramebufferComplete(_glFramebuffer);
        });
        return ok;
    }

    private nint ExportWin32Handle()
    {
        if (GetMemoryWin32Handle is null) return 0;
        var info = new VkMemoryGetWin32HandleInfoKHR
        {
            sType = VkStructureType.MemoryGetWin32HandleInfoKHR,
            memory = _interopMemory,
            handleType = VkExternalMemoryHandleTypeFlags.OpaqueWin32,
        };
        nint handle;
        return GetMemoryWin32Handle(_device, &info, &handle) == VkResult.Success ? handle : 0;
    }

    private int ExportFd()
    {
        if (GetMemoryFd is null) return -1;
        var info = new VkMemoryGetFdInfoKHR
        {
            sType = VkStructureType.MemoryGetFDInfoKHR,
            memory = _interopMemory,
            handleType = VkExternalMemoryHandleTypeFlags.OpaqueFD,
        };
        int fd;
        return GetMemoryFd(_device, &info, &fd) == VkResult.Success ? fd : -1;
    }

    /// <summary>
    /// Orders what GL wrote before what Vulkan is about to read, and takes the image back
    /// from the external queue.
    ///
    /// <para>Both halves are the blunt kind. <c>glFinish</c> waits for everything GL has, and
    /// the barrier is submitted and waited on rather than signalled through a shared
    /// semaphore. Measured at 0.27 ms a frame at 1080p and 0.29 at 4K — the same number at
    /// four times the pixels, because it is the cost of a submit rather than of the image —
    /// against the 175 ms a second the copy it replaces costs.
    /// <c>VK_KHR_external_semaphore</c> would remove the wait; the measurement is what says
    /// it is an optimisation rather than a precondition.</para>
    /// </summary>
    public void FinishExternalFrame()
    {
        if (!_interopDirty || _interopImage.IsNull) return;
        _interopDirty = false;

        long beforeFinish = FinishTrace ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        // The whole saving. glSignalSemaphoreEXT returns as soon as the signal is queued,
        // where glFinish returned only once the GPU had finished — 0.74 to 5.33 ms of blocked
        // CPU a frame on a 4K decode. The barrier submit below waits on the semaphore
        // instead, so the ordering happens on the GPU, which is where it belongs.
        if (!_sharedSemaphore.IsNull) _gl!.SignalSemaphore(_glSemaphore, _glTexture);
        else _gl!.Finish();

        long afterFinish = FinishTrace ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        ProbeRangeOnce();

        // The ring only exists for the semaphore path; without one the old one-shot is
        // correct and its wait is what makes freeing the buffer safe.
        bool ring = !_sharedSemaphore.IsNull && EnsureInteropCommandRing();

        VkCommandBuffer cmd;
        VkFence slotFence = VkFence.Null;
        if (ring)
        {
            int slot = _interopSlot;
            _interopSlot = (_interopSlot + 1) % _interopCmd.Length;
            cmd = _interopCmd[slot];
            slotFence = _interopFence[slot];

            // Three frames of slack, so in practice this is already signalled.
            var f = slotFence;
            WaitForFences(_device, 1, &f, 1, NoTimeout);
            ResetFences(_device, 1, &f);

            var begin = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
            Check(BeginCommandBuffer(cmd, &begin), "vkBeginCommandBuffer");
        }
        else
        {
            cmd = BeginOneShot();
        }

        // Where the image comes from depends on how GL handed it over. A signalled semaphore
        // carries the layout with it — SHADER_READ_ONLY, which is what SignalSemaphore asks
        // for — so the barrier is left with only the queue ownership to take. Without one,
        // GENERAL is what the other API is taken to have left it in.
        var from = _sharedSemaphore.IsNull
            ? VkImageLayout.General
            : VkImageLayout.ShaderReadOnlyOptimal;
        var barrier = new VkImageMemoryBarrier
        {
            sType = VkStructureType.ImageMemoryBarrier,
            srcAccessMask = VkAccessFlags.None,
            dstAccessMask = VkAccessFlags.ShaderRead,
            oldLayout = from,
            newLayout = VkImageLayout.ShaderReadOnlyOptimal,
            srcQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_EXTERNAL,
            dstQueueFamilyIndex = _queueFamily,
            image = _interopImage,
            subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1),
        };
        CmdPipelineBarrier(cmd, VkPipelineStageFlags.TopOfPipe, VkPipelineStageFlags.FragmentShader,
            VkDependencyFlags.None, 0, 0, 0, 0, 1, &barrier);
        if (ring) SubmitBarrier(cmd, _sharedSemaphore, slotFence);
        else EndOneShot(cmd);
        if (FinishTrace) RecordFinish(beforeFinish, afterFinish);
    }

    /// <summary>
    /// <c>HULTUI_VULKAN_FINISH_TRACE=1</c> splits what ordering a real video frame costs into
    /// the <c>glFinish</c> and the barrier submit that follows it.
    ///
    /// <para>The spike measured this at 0.27 ms a frame and that number was taken as settled.
    /// It was measured against a <c>glClear</c> on an otherwise idle GPU, which is not what a
    /// 4K decode looks like: <c>glFinish</c> waits for whatever GL has outstanding, and on
    /// this driver waiting is not free of CPU.</para>
    /// </summary>
    private static readonly bool FinishTrace =
        Environment.GetEnvironmentVariable("HULTUI_VULKAN_FINISH_TRACE") == "1";

    private long _finishTicks, _barrierTicks;
    private int _finishFrames;

    private void RecordFinish(long before, long afterFinish)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        _finishTicks += afterFinish - before;
        _barrierTicks += now - afterFinish;
        if (++_finishFrames < 120) return;

        double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        double finish = _finishTicks * toMs, barrier = _barrierTicks * toMs;
        Console.Error.WriteLine($"[vulkan] per frame over {_finishFrames}: glFinish " +
            $"{finish / _finishFrames:0.000} ms, barrier submit {barrier / _finishFrames:0.000} ms " +
            $"({(finish + barrier) / _finishFrames * 24:0} ms of CPU a second at 24 fps)");
        _finishTicks = _barrierTicks = 0;
        _finishFrames = 0;
    }

    /// <summary>
    /// <c>HULTUI_VULKAN_SEMAPHORE=0</c> goes back to ordering GL against Vulkan with
    /// <c>glFinish</c> and a queue wait.
    ///
    /// <para>There to be turned off for a driver that mishandles a shared semaphore, and to
    /// measure the difference on one clip: on a 4K HDR decode the CPU is 62 ms a second with
    /// the semaphore and 156 without.</para>
    /// </summary>
    private static readonly bool SemaphoreAllowed =
        Environment.GetEnvironmentVariable("HULTUI_VULKAN_SEMAPHORE") != "0";

    private VkSemaphore _sharedSemaphore;
    private uint _glSemaphore;

    /// <summary>
    /// One semaphore, exported from Vulkan and imported into GL, reused every frame.
    ///
    /// <para>Best effort throughout: a driver that will share an image but not a semaphore
    /// keeps working and pays the old CPU block instead. Every step here may fail quietly,
    /// and a null <see cref="_sharedSemaphore"/> is what selects that path.</para>
    /// </summary>
    private void CreateSharedSemaphore(InteropGl gl)
    {
        if (!SemaphoreAllowed || !_semaphoreExtensionsEnabled || !gl.HasSemaphore) return;

        var handleType = OperatingSystem.IsWindows()
            ? VkExternalSemaphoreHandleTypeFlags.OpaqueWin32
            : VkExternalSemaphoreHandleTypeFlags.OpaqueFD;

        var export = new VkExportSemaphoreCreateInfo
        {
            sType = VkStructureType.ExportSemaphoreCreateInfo,
            handleTypes = handleType,
        };
        var info = new VkSemaphoreCreateInfo { pNext = &export };
        VkSemaphore semaphore;
        if (VulkanCommands.CreateSemaphore(_device, &info, 0, &semaphore) != VkResult.Success) return;

        // Exported out here, imported inside the GL context: a lambda cannot take the
        // address of a captured local, and only the import needs the context anyway.
        // Two sets of locals on purpose: one the address is taken of, one the lambda
        // captures. C# forbids a single local being both.
        nint exportedHandle = 0;
        int exportedFd = -1;
        if (OperatingSystem.IsWindows())
        {
            if (GetSemaphoreWin32Handle is null) { DestroySemaphore(_device, semaphore, 0); return; }
            var get = new VkSemaphoreGetWin32HandleInfoKHR
            {
                sType = VkStructureType.SemaphoreGetWin32HandleInfoKHR,
                semaphore = semaphore,
                handleType = handleType,
            };
            if (GetSemaphoreWin32Handle(_device, &get, &exportedHandle) != VkResult.Success)
            { DestroySemaphore(_device, semaphore, 0); return; }
        }
        else
        {
            if (GetSemaphoreFd is null) { DestroySemaphore(_device, semaphore, 0); return; }
            var get = new VkSemaphoreGetFdInfoKHR
            {
                sType = VkStructureType.SemaphoreGetFDInfoKHR,
                semaphore = semaphore,
                handleType = handleType,
            };
            if (GetSemaphoreFd(_device, &get, &exportedFd) != VkResult.Success)
            { DestroySemaphore(_device, semaphore, 0); return; }
        }

        nint win32Handle = exportedHandle;
        int fd = exportedFd;
        uint imported = 0;
        RunOnGLContext(() =>
        {
            imported = gl.CreateSemaphore();
            if (OperatingSystem.IsWindows())
            {
                gl.ImportSemaphoreWin32(imported, win32Handle);
                // Ours to close, the same asymmetry as an imported memory object: GL copies
                // what it needs out of an OPAQUE_WIN32 handle rather than taking it.
                CloseHandle(win32Handle);
            }
            else
            {
                gl.ImportSemaphoreFd(imported, fd);   // consumes the descriptor
            }
            if (gl.Error() != 0) imported = 0;
        });

        if (imported == 0) { DestroySemaphore(_device, semaphore, 0); return; }
        _sharedSemaphore = semaphore;
        _glSemaphore = imported;
    }

    /// <summary>
    /// Command buffers for the hand-back barrier, three of them with a fence each.
    ///
    /// <para>A one-shot buffer has to be waited on before it can be freed, and that wait was
    /// the whole remaining cost once the semaphore removed <c>glFinish</c>: the block simply
    /// moved from GL into <c>vkQueueWaitIdle</c>, which grew to 6.6 ms a frame. A buffer that
    /// is reused rather than freed needs no wait at all — only the assurance that the slot
    /// coming round again has finished, which three frames of slack makes free in practice.</para>
    /// </summary>
    private VkCommandBuffer[] _interopCmd = [];
    private VkFence[] _interopFence = [];
    private int _interopSlot;

    private bool EnsureInteropCommandRing()
    {
        if (_interopCmd.Length > 0) return true;

        var buffers = new VkCommandBuffer[3];
        var fences = new VkFence[3];
        var allocate = new VkCommandBufferAllocateInfo
        {
            commandPool = _pool,
            level = VkCommandBufferLevel.Primary,
            commandBufferCount = 3,
        };
        fixed (VkCommandBuffer* p = buffers)
            if (AllocateCommandBuffers(_device, &allocate, p) != VkResult.Success) return false;

        // Created signalled: the first time round each slot, there is nothing to wait for.
        var fenceInfo = new VkFenceCreateInfo { flags = VkFenceCreateFlags.Signaled };
        for (int i = 0; i < fences.Length; i++)
        {
            VkFence fence;
            if (CreateFence(_device, &fenceInfo, 0, &fence) != VkResult.Success) return false;
            fences[i] = fence;
        }

        _interopCmd = buffers;
        _interopFence = fences;
        return true;
    }

    /// <summary>
    /// Submits the hand-back barrier waiting on the semaphore, and does not wait for it.
    ///
    /// <para>Nothing blocks the CPU here. Skia reads the image later in the same frame and is
    /// ordered after this by submission order on the one queue, which is what the present
    /// transition already relies on.</para>
    /// </summary>
    private void SubmitBarrier(VkCommandBuffer cmd, VkSemaphore wait, VkFence fence)
    {
        Check(EndCommandBuffer(cmd), "vkEndCommandBuffer");
        var stage = VkPipelineStageFlags.FragmentShader;
        var submit = new VkSubmitInfo
        {
            waitSemaphoreCount = wait.IsNull ? 0u : 1u,
            pWaitSemaphores = wait.IsNull ? null : &wait,
            pWaitDstStageMask = wait.IsNull ? null : &stage,
            commandBufferCount = 1,
            pCommandBuffers = &cmd,
        };
        Check(QueueSubmit(_queue, 1, &submit, fence), "vkQueueSubmit");
    }

    private uint FindMemoryType(uint typeBits, VkMemoryPropertyFlags wanted)
    {
        var properties = new VkPhysicalDeviceMemoryProperties();
        GetMemoryProperties(_physicalDevice, &properties);
        for (uint i = 0; i < properties.memoryTypeCount; i++)
        {
            if ((typeBits & (1u << (int)i)) == 0) continue;
            if ((properties.memoryTypes[(int)i].propertyFlags & wanted) == wanted) return i;
        }
        throw new InvalidOperationException($"no Vulkan memory type for {wanted}");
    }

    private byte[] VulkanDeviceUuid()
    {
        var id = new VkPhysicalDeviceIDProperties { sType = VkStructureType.PhysicalDeviceIdProperties };
        var properties = new VkPhysicalDeviceProperties2
        {
            sType = VkStructureType.PhysicalDeviceProperties2,
            pNext = &id,
        };
        GetPhysicalDeviceProperties2(_physicalDevice, &properties);
        var uuid = new byte[16];
        for (int i = 0; i < 16; i++) uuid[i] = id.deviceUUID[i];
        return uuid;
    }

    private void DestroyInteropImage()
    {
        if (_gl is { } gl && _glTexture != 0)
            RunOnGLContext(() =>
            {
                gl.DeleteFramebuffer(_glFramebuffer);
                gl.DeleteTexture(_glTexture);
                gl.DeleteMemoryObject(_glMemoryObject);
            });
        _glFramebuffer = _glTexture = _glMemoryObject = 0;

        if (!_interopImage.IsNull) DestroyImage(_device, _interopImage, 0);
        if (!_interopMemory.IsNull) FreeMemory(_device, _interopMemory, 0);
        _interopImage = VkImage.Null;
        _interopMemory = VkDeviceMemory.Null;
        _interopMemorySize = 0;
        _interopWidth = _interopHeight = 0;
        _interopDirty = false;
    }

    private void DisposeInterop()
    {
        if (_interopReady is null) return;
        // Before the fences go: a buffer may still be in flight, and the device wait in
        // Dispose has already happened by the time this runs.
        foreach (var fence in _interopFence) if (!fence.IsNull) DestroyFence(_device, fence, 0);
        _interopFence = [];
        _interopCmd = [];
        if (_glSemaphore != 0 && _gl is { } g) RunOnGLContext(() => g.DeleteSemaphore(_glSemaphore));
        if (!_sharedSemaphore.IsNull) DestroySemaphore(_device, _sharedSemaphore, 0);
        _sharedSemaphore = VkSemaphore.Null;
        _glSemaphore = 0;
        DestroyInteropImage();
        if (_glContext is not null) SDL_GL_DestroyContext(_glContext);
        if (_glWindow is not null) SDL_DestroyWindow(_glWindow);
        _glContext = null;
        _glWindow = null;
        _gl = null;
    }

    /// <summary>
    /// <c>HULTUI_VIDEO_HDR_PROBE=&lt;n&gt;</c> reads the shared image back through GL on the
    /// nth frame and says what the producer actually wrote into it.
    ///
    /// <para>One frame, once, because the read is the whole image — 53 MB at 4K — and stalls.
    /// It exists because the two numbers this pipeline reports are answers to different
    /// questions: the swapchain report is taken after Skia has composited, and a missing range
    /// there could be the producer never making one or the renderer losing it.</para>
    /// </summary>
    private static readonly int ProbeFrame =
        int.TryParse(Environment.GetEnvironmentVariable("HULTUI_VIDEO_HDR_PROBE"), out int n) && n > 0
            ? n : 0;

    private int _probeFrames;
    private int _probesTaken;
    private const int Probes = 6;

    /// <summary>
    /// Several samples, spaced out, because one is not evidence. A single read landed on a
    /// black frame and reported 0.000, which reads exactly like a producer writing nothing —
    /// and a film has plenty of black frames to land on.
    /// </summary>
    private void ProbeRangeOnce()
    {
        if (ProbeFrame == 0 || _probesTaken >= Probes || _glFramebuffer == 0) return;
        if (++_probeFrames < ProbeFrame) return;
        _probeFrames = 0;
        _probesTaken++;

        var (max, over1, levels, reversals) = _gl!.ReadRange(_glFramebuffer, _interopWidth, _interopHeight);
        Console.Error.WriteLine($"[interop] sample {_probesTaken}/{Probes} of the shared image " +
            $"({_interopWidth}x{_interopHeight}): max {max:0.000}, {over1} above 1.0; " +
            $"middle row {levels} levels, {reversals} reversals");
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}

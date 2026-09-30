using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using HultUI.Core.DevTools;
using HultUI.Core.Rendering;
using SDL;
using Vortice.Vulkan;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3;

/// <summary>
/// Images shared between this window's Vulkan device and a hidden OpenGL context — phase 4 of
/// <c>docs/design/HDR.md</c>, and seam W7 of <c>docs/design/WEBVIEW.md</c>.
///
/// <para><b>Why there is a second context at all.</b> mpv renders with OpenGL and nothing
/// else: its render API defines <c>opengl</c> and <c>sw</c>, and no build flag adds a third.
/// A Vulkan window has no GL context, so before this the video went through <c>sw</c> — a
/// decode into main memory, a copy out, an upload back — which works, costs twice the CPU at
/// 1080p, and cannot carry more than eight bits.</para>
///
/// <para>So a GL context is made beside the Vulkan device, and the two are given one
/// allocation rather than a copy each: Vulkan creates an image whose memory is exportable,
/// GL imports that memory as a texture, the producer renders into a framebuffer over it, and
/// Skia draws the Vulkan image. Nothing crosses the bus twice. Phase 0 proved drivers allow
/// it; <c>spikes/HdrInterop</c> is that proof and this is the same sequence in the real
/// renderer.</para>
///
/// <para><b>One image per source.</b> Each GPU source — a video, a web page — is lent an image
/// of its own (an <see cref="InteropSlot"/>), with its own pair of semaphores and its own
/// hand-back bookkeeping. There used to be one per window, which Skia only reads at the
/// frame's flush: a second source that frame would have drawn over the first one's picture,
/// or re-created it at another size under a draw already recorded, so the second was refused
/// and drew nothing. A source gives its image up with <see cref="ForgetInteropImage"/>.</para>
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

    /// <summary>
    /// One source's image: the Vulkan allocation, the GL objects over it, the two semaphores
    /// that order it, and where it is in being handed back and forth.
    /// </summary>
    private sealed class InteropSlot(object owner)
    {
        public readonly object Owner = owner;

        public VkImage Image;
        public VkDeviceMemory Memory;
        public ulong MemorySize;
        public uint GlMemoryObject, GlTexture, GlFramebuffer;
        public int Width, Height;

        /// <summary>Whether GL has drawn into the image since the last barrier. Kept so that a
        /// <see cref="RunOnGLContext"/> which only set something up does not pay for a submit.</summary>
        public bool Dirty;

        /// <summary>GL signals it when it has written; the barrier submit waits on it.</summary>
        public VkSemaphore Shared;
        public uint GlShared;

        /// <summary>
        /// The other direction: Vulkan signals it once the frame that sampled the image is
        /// done, and GL waits on it before writing the next one.
        ///
        /// <para>Without it nothing ordered a GL write after the previous frame's Skia read of
        /// the same image. The acquire fence only waits for the frame that last presented
        /// <i>that swapchain image</i>, two or three frames back. At the video's frame rate the
        /// read had always finished anyway; with the GPU made to take longer than a frame, 59 of
        /// 960 GL writes began while it was still pending.</para>
        /// </summary>
        public VkSemaphore Release;
        public uint GlRelease;

        /// <summary>Set when a frame drew from the image, so its present must release it to GL.</summary>
        public bool ReleaseOwed;

        /// <summary>Set once Vulkan has signalled the release, until GL has waited on it. A binary
        /// semaphore takes exactly one wait per signal, so this is what keeps the two in step.</summary>
        public bool ReleasePending;

        /// <summary>Ordered by semaphores; otherwise by <c>glFinish</c> and queue waits.</summary>
        public bool Ordered => !Shared.IsNull;
    }

    private readonly List<InteropSlot> _slots = [];

    /// <summary>Sources that gave their image up, from any thread; the images go on the render
    /// thread, at the next lend or present.</summary>
    private readonly ConcurrentQueue<object> _forgotten = new();

    /// <summary>Whether a slot should try for a pair of shared semaphores.</summary>
    private bool _semaphoresUsable;

    /// <summary>The same resolver the GL window uses, handed to whatever renders here — mpv
    /// loads its own GL through it.</summary>
    public Func<string, nint> GlGetProcAddress { get; } = ResolveGl;

    /// <summary>How many sources hold an image. For tests.</summary>
    internal int InteropImageCount => _slots.Count;

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
        // A GL context may span several devices and names each; any one matching is the GPU
        // Vulkan picked.
        var glUuids = gl.DeviceUuids();
        var vkUuid = VulkanDeviceUuid();
        if (!glUuids.Exists(uuid => uuid.AsSpan().SequenceEqual(vkUuid)))
            return Refuse("GL is on a different GPU than Vulkan " +
                $"(GL {Describe(glUuids)} vs Vulkan {Convert.ToHexString(vkUuid)})");

        _interopReady = true;
        _semaphoresUsable = SemaphoreAllowed && _semaphoreExtensionsEnabled && gl.HasSemaphore;
        StartupTrace.Mark($"vulkan interop ready on {gl.Renderer}" +
            (_semaphoresUsable ? " (semaphore ordering)" : " (glFinish ordering)"));
        return true;

        bool Refuse(string why)
        {
            StartupTrace.Mark($"vulkan interop unavailable: {why}");
            Console.Error.WriteLine($"[vulkan] video stays on the software path: {why}");
            return false;
        }

        static string Describe(List<byte[]> uuids) =>
            uuids.Count == 0 ? "(unknown)" : string.Join(", ", uuids.Select(Convert.ToHexString));
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

    public VulkanInteropImage? LendInteropImage(object owner, int widthPx, int heightPx)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_interopReady != true || widthPx <= 0 || heightPx <= 0) return null;
        DestroyForgottenSlots();

        var slot = SlotOf(owner) ?? NewSlot(owner);

        // A frame that took the image and never presented still owes it back; settle that
        // first, so the wait below has a signal to wait for.
        FlushOwedRelease(slot);

        // GL is about to write the image, so the last frame that sampled it has to be done.
        // Called on the GL context, before the producer draws. With the semaphore the wait
        // is the GPU's; without one it is the old blunt kind, which in practice finds the
        // queue already idle.
        if (slot.ReleasePending)
        {
            _gl!.WaitSemaphore(slot.GlRelease, slot.GlTexture);
            slot.ReleasePending = false;
        }
        else if (!slot.Ordered && !slot.Image.IsNull)
        {
            _vk.QueueWaitIdle(_queue);
        }

        if (slot.Image.IsNull || slot.Width != widthPx || slot.Height != heightPx)
        {
            // A GPU-side wait orders GL, not the destruction, which is the CPU's. A video's
            // image follows the video's size, which changes with the file; a page's follows the
            // element, and so every step of a resize pays this.
            if (!slot.Image.IsNull) _vk.DeviceWaitIdle(_device);
            DestroySlotImage(slot);
            if (!CreateSharedImage(slot, widthPx, heightPx)) return null;
        }

        slot.Dirty = true;
        return new VulkanInteropImage(
            Image: slot.Image.Handle,
            Memory: slot.Memory.Handle,
            MemorySize: slot.MemorySize,
            Format: (uint)InteropFormat,
            Usage: InteropUsage,
            // What FinishExternalFrame leaves it in, which is the only layout the renderer
            // ever sees it in.
            Layout: (uint)VkImageLayout.ShaderReadOnlyOptimal,
            WidthPx: widthPx,
            HeightPx: heightPx,
            GlFramebuffer: slot.GlFramebuffer,
            GlTexture: slot.GlTexture,
            GlInternalFormat: GlRgba16f);
    }

    public void ForgetInteropImage(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _forgotten.Enqueue(owner);
    }

    private InteropSlot? SlotOf(object owner)
    {
        foreach (var slot in _slots)
            if (ReferenceEquals(slot.Owner, owner)) return slot;
        return null;
    }

    private InteropSlot NewSlot(object owner)
    {
        var slot = new InteropSlot(owner);
        if (_semaphoresUsable) CreateSharedSemaphores(slot);
        _slots.Add(slot);
        StartupTrace.Mark($"vulkan interop: source {_slots.Count} of this window" +
            (slot.Ordered ? "" : " (glFinish ordering)"));
        return slot;
    }

    /// <summary>
    /// The images of sources that gave them up. The GPU may still be reading one — the frame
    /// before last drew it — so this waits for the device first, which is fine for something
    /// that happens when a video or a page goes away.
    /// </summary>
    private void DestroyForgottenSlots()
    {
        if (_forgotten.IsEmpty) return;
        bool waited = false;
        while (_forgotten.TryDequeue(out var owner))
        {
            if (SlotOf(owner) is not { } slot) continue;
            if (!waited) { _vk.DeviceWaitIdle(_device); waited = true; }
            DestroySlot(slot);
            _slots.Remove(slot);
            StartupTrace.Mark($"vulkan interop: a source gave its image back ({_slots.Count} left)");
        }
    }

    /// <summary>
    /// The image, its exported handle, and the GL objects over it.
    ///
    /// <para>Two declarations are needed and either one alone fails at export rather than at
    /// creation: the image says it will be shared
    /// (<see cref="VkExternalMemoryImageCreateInfo"/>) and the allocation says the same
    /// (<see cref="VkExportMemoryAllocateInfo"/>).</para>
    /// </summary>
    private bool CreateSharedImage(InteropSlot slot, int width, int height)
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
        if (_vk.CreateImage(_device, &imageInfo, 0, &image) != VkResult.Success) return false;
        slot.Image = image;

        var requirements = new VkMemoryRequirements();
        _vk.GetImageMemoryRequirements(_device, image, &requirements);
        slot.MemorySize = requirements.size;

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
        if (_vk.AllocateMemory(_device, &allocate, 0, &memory) != VkResult.Success)
            return FailImage("vkAllocateMemory");
        slot.Memory = memory;

        if (_vk.BindImageMemory(_device, image, memory, 0) != VkResult.Success)
            return FailImage("vkBindImageMemory");

        if (!ImportIntoGL(gl, slot, width, height)) return FailImage("the GL import");

        slot.Width = width;
        slot.Height = height;
        StartupTrace.Mark($"vulkan interop image {width}x{height} RGBA16F ({requirements.size} bytes)");
        return true;

        bool FailImage(string what)
        {
            Console.Error.WriteLine($"[vulkan] the shared image failed at {what}");
            DestroySlotImage(slot);
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
    private bool ImportIntoGL(InteropGl gl, InteropSlot slot, int width, int height)
    {
        bool ok = false;
        RunOnGLContext(() =>
        {
            slot.GlMemoryObject = gl.CreateMemoryObject();
            gl.MarkDedicated(slot.GlMemoryObject);
            if (OperatingSystem.IsWindows())
            {
                nint handle = ExportWin32Handle(slot.Memory);
                if (handle == 0) return;
                gl.ImportWin32(slot.GlMemoryObject, slot.MemorySize, handle);
                // GL copies what it needs out of an OPAQUE_WIN32 handle rather than taking
                // ownership of it, so this side still has to close it. A file descriptor is
                // the other way round and glImportMemoryFdEXT consumes it.
                CloseHandle(handle);
            }
            else
            {
                int fd = ExportFd(slot.Memory);
                if (fd < 0) return;
                gl.ImportFd(slot.GlMemoryObject, slot.MemorySize, fd);
            }
            if (gl.Error() != 0) return;

            slot.GlTexture = gl.CreateTexture();
            gl.SetOptimalTiling(slot.GlTexture);
            gl.TextureStorageMem(slot.GlTexture, GlRgba16f, width, height, slot.GlMemoryObject);
            if (gl.Error() != 0) return;

            slot.GlFramebuffer = gl.CreateFramebuffer();
            gl.AttachColour(slot.GlFramebuffer, slot.GlTexture);
            ok = gl.FramebufferComplete(slot.GlFramebuffer);
        });
        return ok;
    }

    private nint ExportWin32Handle(VkDeviceMemory memory)
    {
        if (_vk.GetMemoryWin32Handle is null) return 0;
        var info = new VkMemoryGetWin32HandleInfoKHR
        {
            sType = VkStructureType.MemoryGetWin32HandleInfoKHR,
            memory = memory,
            handleType = VkExternalMemoryHandleTypeFlags.OpaqueWin32,
        };
        nint handle;
        return _vk.GetMemoryWin32Handle(_device, &info, &handle) == VkResult.Success ? handle : 0;
    }

    private int ExportFd(VkDeviceMemory memory)
    {
        if (_vk.GetMemoryFd is null) return -1;
        var info = new VkMemoryGetFdInfoKHR
        {
            sType = VkStructureType.MemoryGetFDInfoKHR,
            memory = memory,
            handleType = VkExternalMemoryHandleTypeFlags.OpaqueFD,
        };
        int fd;
        return _vk.GetMemoryFd(_device, &info, &fd) == VkResult.Success ? fd : -1;
    }

    private readonly List<InteropSlot> _dirty = [];
    private readonly List<VkSemaphore> _barrierWaits = [];

    /// <summary>
    /// Orders what GL wrote before what Vulkan is about to read, and takes each image GL drew
    /// into back from the external queue.
    ///
    /// <para>With shared semaphores, GL signals them and the barrier submit waits on them on
    /// the GPU, from a ring of command buffers with a fence each, so the CPU does not block.
    /// Without, <c>glFinish</c> and a waited one-shot submit order it instead: correct, and
    /// 0.74 to 5.33 ms a frame under a 4K decode. Normally one image is dirty — each source runs
    /// its own <see cref="RunOnGLContext"/> — but the barriers are recorded together if not.</para>
    /// </summary>
    public void FinishExternalFrame()
    {
        _dirty.Clear();
        foreach (var slot in _slots)
            if (slot.Dirty && !slot.Image.IsNull) _dirty.Add(slot);
        if (_dirty.Count == 0) return;

        long beforeFinish = FinishTrace ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        // The whole saving. glSignalSemaphoreEXT returns as soon as the signal is queued,
        // where glFinish returned only once the GPU had finished — 0.74 to 5.33 ms of blocked
        // CPU a frame on a 4K decode. The barrier submit below waits on the semaphore
        // instead, so the ordering happens on the GPU, which is where it belongs.
        bool finish = false;
        _barrierWaits.Clear();
        foreach (var slot in _dirty)
        {
            slot.Dirty = false;
            if (slot.Ordered)
            {
                _gl!.SignalSemaphore(slot.GlShared, slot.GlTexture);
                _barrierWaits.Add(slot.Shared);
            }
            else finish = true;
        }
        if (finish) _gl!.Finish();

        long afterFinish = FinishTrace ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        ProbeRangeOnce(_dirty[0]);

        // The ring only exists for the semaphore path; without one the old one-shot is
        // correct and its wait is what makes freeing the buffer safe.
        bool ring = !finish && EnsureInteropCommandRing();

        VkCommandBuffer cmd;
        VkFence slotFence = VkFence.Null;
        if (ring)
        {
            int index = _interopSlot;
            _interopSlot = (_interopSlot + 1) % _interopCmd.Length;
            cmd = _interopCmd[index];
            slotFence = _interopFence[index];

            // Three frames of slack, so in practice this is already signalled.
            var f = slotFence;
            _vk.WaitForFences(_device, 1, &f, 1, NoTimeout);
            _vk.ResetFences(_device, 1, &f);

            var begin = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
            Check(_vk.BeginCommandBuffer(cmd, &begin), "vkBeginCommandBuffer");
        }
        else
        {
            cmd = BeginOneShot();
        }

        foreach (var slot in _dirty)
        {
            // Where the image comes from depends on how GL handed it over. A signalled semaphore
            // carries the layout with it — SHADER_READ_ONLY, which is what SignalSemaphore asks
            // for — so the barrier is left with only the queue ownership to take. Without one,
            // GENERAL is what the other API is taken to have left it in.
            var from = slot.Ordered ? VkImageLayout.ShaderReadOnlyOptimal : VkImageLayout.General;
            var barrier = new VkImageMemoryBarrier
            {
                sType = VkStructureType.ImageMemoryBarrier,
                srcAccessMask = VkAccessFlags.None,
                dstAccessMask = VkAccessFlags.ShaderRead,
                oldLayout = from,
                newLayout = VkImageLayout.ShaderReadOnlyOptimal,
                srcQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_EXTERNAL,
                dstQueueFamilyIndex = _queueFamily,
                image = slot.Image,
                subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1),
            };
            _vk.CmdPipelineBarrier(cmd, VkPipelineStageFlags.TopOfPipe, VkPipelineStageFlags.FragmentShader,
                VkDependencyFlags.None, 0, 0, 0, 0, 1, &barrier);
            // Taken from GL, so it has to be handed back: see ReleaseInteropImages. On both
            // paths — the glFinish one used to take ownership every frame and never return it,
            // so every acquire after the first was of an image the external side had never
            // been given.
            slot.ReleaseOwed = true;
        }
        SubmitBarrier(cmd, _barrierWaits, slotFence, oneShot: !ring);
        if (FinishTrace) RecordFinish(beforeFinish, afterFinish);
    }

    /// <summary>
    /// <c>HULTUI_VULKAN_FINISH_TRACE=1</c> splits what ordering a real video frame costs into
    /// the GL hand-over (<c>glFinish</c>, or the semaphore signal) and the barrier submit that
    /// follows it.
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

    /// <summary>
    /// Two semaphores for one slot, exported from Vulkan and imported into GL, reused every
    /// frame: one for each direction the image changes hands.
    ///
    /// <para>Best effort throughout: a driver that will share an image but not a semaphore
    /// keeps working and pays the old CPU block instead. Every step here may fail quietly, and
    /// a slot left without its <see cref="InteropSlot.Shared"/> semaphore is on that path.
    /// Both or neither, so there is only one fallback to reason about.</para>
    /// </summary>
    private void CreateSharedSemaphores(InteropSlot slot)
    {
        var gl = _gl!;
        if (!TryShareSemaphore(gl, out var toVulkan, out uint toVulkanGl)) return;
        if (!TryShareSemaphore(gl, out var toGl, out uint toGlGl))
        {
            RunOnGLContext(() => gl.DeleteSemaphore(toVulkanGl));
            _vk.DestroySemaphore(_device, toVulkan, 0);
            return;
        }
        slot.Shared = toVulkan;
        slot.GlShared = toVulkanGl;
        slot.Release = toGl;
        slot.GlRelease = toGlGl;
    }

    private bool TryShareSemaphore(InteropGl gl, out VkSemaphore shared, out uint glSemaphore)
    {
        shared = VkSemaphore.Null;
        glSemaphore = 0;

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
        if (_vk.CreateSemaphore(_device, &info, 0, &semaphore) != VkResult.Success) return false;

        // Exported out here, imported inside the GL context: a lambda cannot take the
        // address of a captured local, and only the import needs the context anyway.
        // Two sets of locals on purpose: one the address is taken of, one the lambda
        // captures. C# forbids a single local being both.
        nint exportedHandle = 0;
        int exportedFd = -1;
        if (OperatingSystem.IsWindows())
        {
            if (_vk.GetSemaphoreWin32Handle is null) { _vk.DestroySemaphore(_device, semaphore, 0); return false; }
            var get = new VkSemaphoreGetWin32HandleInfoKHR
            {
                sType = VkStructureType.SemaphoreGetWin32HandleInfoKHR,
                semaphore = semaphore,
                handleType = handleType,
            };
            if (_vk.GetSemaphoreWin32Handle(_device, &get, &exportedHandle) != VkResult.Success)
            { _vk.DestroySemaphore(_device, semaphore, 0); return false; }
        }
        else
        {
            if (_vk.GetSemaphoreFd is null) { _vk.DestroySemaphore(_device, semaphore, 0); return false; }
            var get = new VkSemaphoreGetFdInfoKHR
            {
                sType = VkStructureType.SemaphoreGetFDInfoKHR,
                semaphore = semaphore,
                handleType = handleType,
            };
            if (_vk.GetSemaphoreFd(_device, &get, &exportedFd) != VkResult.Success)
            { _vk.DestroySemaphore(_device, semaphore, 0); return false; }
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

        if (imported == 0) { _vk.DestroySemaphore(_device, semaphore, 0); return false; }
        shared = semaphore;
        glSemaphore = imported;
        return true;
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
            if (_vk.AllocateCommandBuffers(_device, &allocate, p) != VkResult.Success) return false;

        // Created signalled: the first time round each slot, there is nothing to wait for.
        var fenceInfo = new VkFenceCreateInfo { flags = VkFenceCreateFlags.Signaled };
        for (int i = 0; i < fences.Length; i++)
        {
            VkFence fence;
            if (_vk.CreateFence(_device, &fenceInfo, 0, &fence) != VkResult.Success) return false;
            fences[i] = fence;
        }

        _interopCmd = buffers;
        _interopFence = fences;
        return true;
    }

    /// <summary>
    /// Submits the hand-back barriers waiting on the semaphores GL signalled. From the ring it
    /// does not wait for the submit; a one-shot buffer it waits for, and frees.
    ///
    /// <para>Nothing blocks the CPU on the ring path. Skia reads the images later in the same
    /// frame and is ordered after this by submission order on the one queue, which is what the
    /// present transition already relies on.</para>
    /// </summary>
    private void SubmitBarrier(VkCommandBuffer cmd, List<VkSemaphore> waits, VkFence fence, bool oneShot)
    {
        Check(_vk.EndCommandBuffer(cmd), "vkEndCommandBuffer");
        int count = waits.Count;
        var semaphores = stackalloc VkSemaphore[Math.Max(count, 1)];
        var stages = stackalloc VkPipelineStageFlags[Math.Max(count, 1)];
        for (int i = 0; i < count; i++)
        {
            semaphores[i] = waits[i];
            stages[i] = VkPipelineStageFlags.FragmentShader;
        }
        var submit = new VkSubmitInfo
        {
            waitSemaphoreCount = (uint)count,
            pWaitSemaphores = count == 0 ? null : semaphores,
            pWaitDstStageMask = count == 0 ? null : stages,
            commandBufferCount = 1,
            pCommandBuffers = &cmd,
        };
        Check(_vk.QueueSubmit(_queue, 1, &submit, fence), "vkQueueSubmit");
        if (!oneShot) return;
        Check(_vk.QueueWaitIdle(_queue), "vkQueueWaitIdle");
        _vk.FreeCommandBuffers(_device, _pool, 1, &cmd);
    }

    /// <summary>
    /// Records the hand-back to GL of every image a frame drew — or of <paramref name="only"/>
    /// — into <paramref name="cmd"/>, and adds the semaphores its submit has to signal to
    /// <paramref name="signals"/>: none for an image on the glFinish path.
    ///
    /// <para>The mirror of the acquire in <see cref="FinishExternalFrame"/>: ownership back to
    /// the external queue. With a semaphore the layout stays SHADER_READ_ONLY, which is what
    /// GL's wait is told. Without one it goes to GENERAL, which is what that path's acquire
    /// takes it back from, so the two agree about what the other side had it in. The source
    /// scope is the fragment shader because that is where Skia samples it, and submission order
    /// makes it cover Skia's earlier submit. No access mask: a read leaves nothing to make
    /// available, so ordering it is all a later write needs. On the glFinish path the ordering
    /// is the queue wait in <see cref="LendInteropImage"/>, which also covers this.</para>
    /// </summary>
    private void ReleaseInteropImages(VkCommandBuffer cmd, List<VkSemaphore> signals, InteropSlot? only = null)
    {
        foreach (var slot in _slots)
        {
            if (only is not null && !ReferenceEquals(slot, only)) continue;
            if (!slot.ReleaseOwed || slot.Image.IsNull) continue;
            slot.ReleaseOwed = false;

            var barrier = new VkImageMemoryBarrier
            {
                sType = VkStructureType.ImageMemoryBarrier,
                srcAccessMask = VkAccessFlags.None,
                dstAccessMask = VkAccessFlags.None,
                oldLayout = VkImageLayout.ShaderReadOnlyOptimal,
                newLayout = slot.Ordered ? VkImageLayout.ShaderReadOnlyOptimal : VkImageLayout.General,
                srcQueueFamilyIndex = _queueFamily,
                dstQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_EXTERNAL,
                image = slot.Image,
                subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1),
            };
            _vk.CmdPipelineBarrier(cmd, VkPipelineStageFlags.FragmentShader, VkPipelineStageFlags.BottomOfPipe,
                VkDependencyFlags.None, 0, 0, 0, 0, 1, &barrier);
            if (!slot.Ordered) continue;
            slot.ReleasePending = true;
            signals.Add(slot.Release);
        }
    }

    private readonly List<VkSemaphore> _releaseSignals = [];

    /// <summary>
    /// Hands images back to GL on their own, for a frame that took them and was never presented
    /// — a draw that threw, or a swapchain rebuilt between the video and the present. The
    /// release normally rides the present's submit; without this, the next GL write skipped
    /// its wait (nothing had signalled) and the next acquire took an image that had never been
    /// given back. Rare, so a blocking one-shot submit is fine.
    /// </summary>
    /// <param name="only">One source's image, before it is lent again; null for every image owed.</param>
    private void FlushOwedRelease(InteropSlot? only = null)
    {
        bool owed = false;
        foreach (var slot in _slots)
            if (slot.ReleaseOwed && !slot.Image.IsNull && (only is null || ReferenceEquals(slot, only))) owed = true;
        if (!owed) return;

        var cmd = BeginOneShot();
        _releaseSignals.Clear();
        ReleaseInteropImages(cmd, _releaseSignals, only);
        Check(_vk.EndCommandBuffer(cmd), "vkEndCommandBuffer");
        int count = _releaseSignals.Count;
        var signals = stackalloc VkSemaphore[Math.Max(count, 1)];
        for (int i = 0; i < count; i++) signals[i] = _releaseSignals[i];
        var submit = new VkSubmitInfo
        {
            commandBufferCount = 1,
            pCommandBuffers = &cmd,
            signalSemaphoreCount = (uint)count,
            pSignalSemaphores = count == 0 ? null : signals,
        };
        Check(_vk.QueueSubmit(_queue, 1, &submit, VkFence.Null), "vkQueueSubmit");
        Check(_vk.QueueWaitIdle(_queue), "vkQueueWaitIdle");
        _vk.FreeCommandBuffers(_device, _pool, 1, &cmd);
    }

    private uint FindMemoryType(uint typeBits, VkMemoryPropertyFlags wanted)
    {
        var properties = new VkPhysicalDeviceMemoryProperties();
        _vk.GetMemoryProperties(_physicalDevice, &properties);
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
        _vk.GetPhysicalDeviceProperties2(_physicalDevice, &properties);
        var uuid = new byte[16];
        for (int i = 0; i < 16; i++) uuid[i] = id.deviceUUID[i];
        return uuid;
    }

    private void DestroySlotImage(InteropSlot slot)
    {
        if (_gl is { } gl && slot.GlTexture != 0)
        {
            uint framebuffer = slot.GlFramebuffer, texture = slot.GlTexture, memory = slot.GlMemoryObject;
            RunOnGLContext(() =>
            {
                gl.DeleteFramebuffer(framebuffer);
                gl.DeleteTexture(texture);
                gl.DeleteMemoryObject(memory);
            });
        }
        slot.GlFramebuffer = slot.GlTexture = slot.GlMemoryObject = 0;

        if (!slot.Image.IsNull) _vk.DestroyImage(_device, slot.Image, 0);
        if (!slot.Memory.IsNull) _vk.FreeMemory(_device, slot.Memory, 0);
        slot.Image = VkImage.Null;
        slot.Memory = VkDeviceMemory.Null;
        slot.MemorySize = 0;
        slot.Width = slot.Height = 0;
        slot.Dirty = false;
        slot.ReleaseOwed = false;
    }

    /// <summary>A slot's image and semaphores. The device must be idle.</summary>
    private void DestroySlot(InteropSlot slot)
    {
        DestroySlotImage(slot);
        if (_gl is { } g && (slot.GlShared != 0 || slot.GlRelease != 0))
        {
            uint shared = slot.GlShared, release = slot.GlRelease;
            RunOnGLContext(() =>
            {
                g.DeleteSemaphore(shared);
                g.DeleteSemaphore(release);
            });
        }
        if (!slot.Shared.IsNull) _vk.DestroySemaphore(_device, slot.Shared, 0);
        if (!slot.Release.IsNull) _vk.DestroySemaphore(_device, slot.Release, 0);
        slot.Shared = slot.Release = VkSemaphore.Null;
        slot.GlShared = slot.GlRelease = 0;
        slot.ReleasePending = false;
    }

    private void DisposeInterop()
    {
        if (_interopReady is null) return;
        // Before the fences go: a buffer may still be in flight, and the device wait in
        // Dispose has already happened by the time this runs.
        foreach (var fence in _interopFence) if (!fence.IsNull) _vk.DestroyFence(_device, fence, 0);
        _interopFence = [];
        _interopCmd = [];
        foreach (var slot in _slots) DestroySlot(slot);
        _slots.Clear();
        _forgotten.Clear();
        if (_glContext is not null) SDL_GL_DestroyContext(_glContext);
        if (_glWindow is not null) SDL_DestroyWindow(_glWindow);
        _glContext = null;
        _glWindow = null;
        _gl = null;
    }

    /// <summary>
    /// <c>HULTUI_VIDEO_HDR_PROBE=&lt;n&gt;</c> reads a shared image back through GL every nth
    /// frame, six times, and says what the producer actually wrote into it.
    ///
    /// <para>Six samples because one can land on a black frame, which reads exactly like a
    /// producer writing nothing; no more, because the read is the whole image — 53 MB at 4K —
    /// and stalls.
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
    private void ProbeRangeOnce(InteropSlot slot)
    {
        if (ProbeFrame == 0 || _probesTaken >= Probes || slot.GlFramebuffer == 0) return;
        if (++_probeFrames < ProbeFrame) return;
        _probeFrames = 0;
        _probesTaken++;

        var (max, over1, levels, reversals) = _gl!.ReadRange(slot.GlFramebuffer, slot.Width, slot.Height);
        Console.Error.WriteLine($"[interop] sample {_probesTaken}/{Probes} of the shared image " +
            $"({slot.Width}x{slot.Height}): max {max:0.000}, {over1} above 1.0; " +
            $"middle row {levels} levels, {reversals} reversals");
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}

using System.Runtime.InteropServices;
using HultUI.Core.DevTools;
using HultUI.Core.Rendering;
using SDL;
using Vortice.Vulkan;
using static SDL.SDL3;
using static Vortice.Vulkan.Vulkan;
using static HultUI.Platform.SDL3.VulkanCommands;

namespace HultUI.Platform.SDL3;

/// <summary>
/// A Vulkan device and swapchain for an SDL window — the platform half of
/// <see cref="IVulkanSurfaceHost"/>, and phase 1 of <c>docs/design/HDR.md</c>.
///
/// <para><b>Synchronisation is deliberately the simple kind.</b> A finished Vulkan renderer
/// acquires with a semaphore, has the renderer wait on it, signals a second on completion
/// and presents waiting on that — no CPU stall anywhere. That needs semaphores threaded
/// through the draw submission, and SkiaSharp 3.119.2 exposes no <c>GRFlushInfo</c>: the
/// binding has <c>Flush()</c> and <c>Submit()</c> and nothing to hang a semaphore on. So
/// this acquires with a <i>fence</i> and waits on it, then waits the queue idle after Skia
/// submits. Correct, and a stall of one frame's GPU work per frame.</para>
///
/// <para>That is the right trade for phase 1, whose goal is output indistinguishable from
/// the GL path rather than a faster path. It is also the first thing to fix if this backend
/// is ever to ship, and it needs either a richer binding or a submission of our own.</para>
/// </summary>
internal sealed unsafe partial class SdlVulkanHost : IVulkanSurfaceHost, IDisposable
{
    private const ulong NoTimeout = ulong.MaxValue;

    /// <summary>Whether to ask for the half-float linear surface — phase 2 of
    /// docs/design/HDR.md. Off by default: the 8-bit sRGB path is what ships.</summary>
    private static readonly bool WantsWideColor =
        Environment.GetEnvironmentVariable("HULTUI_VULKAN_F16") == "1";

    // VkImageUsageFlagBits, spelled out because getting these wrong is invisible: an
    // earlier version had 0x04 commented as TRANSFER_SRC when it is SAMPLED, and the
    // swapchain went without the one bit Skia actually requires.
    private const uint UsageTransferSrc = 0x01;
    private const uint UsageTransferDst = 0x02;
    private const uint UsageSampled = 0x04;
    private const uint UsageColorAttachment = 0x10;

    /// <summary>
    /// What the swapchain images are created with, and what Skia is told they have.
    ///
    /// <para><b>TRANSFER_SRC is not optional.</b> Skia refuses to make a surface over a
    /// render target without it — <c>SKSurface.Create</c> returns null and says nothing —
    /// and the bisect in spikes/HdrInterop proves it: identical in every other respect,
    /// 0x17 succeeds and 0x16 fails. COLOR_ATTACHMENT alone fails too.</para>
    /// </summary>
    private const uint SwapchainUsage =
        UsageColorAttachment | UsageTransferSrc | UsageTransferDst | UsageSampled;

    private VkInstance _instance;
    private VkSurfaceKHR _surface;
    private VkPhysicalDevice _physicalDevice;
    private VkDevice _device;
    private VkQueue _queue;
    private uint _queueFamily;

    private VkSwapchainKHR _swapchain;
    private VkImage[] _images = [];

    /// <summary>Signalled when this image's frame has finished drawing; the present waits on
    /// it. One per image rather than one shared, because the previous frame's signal must not
    /// still be pending when the next frame wants to signal it — and re-acquiring an image is
    /// the point at which the presentation engine says its last use is done.</summary>
    private VkSemaphore[] _renderFinished = [];

    /// <summary>The layout-transition command buffer for each image, recorded fresh each
    /// frame. Kept rather than allocated and freed per frame: freeing a command buffer that
    /// may still be executing is undefined, and without the queue drain it often is.</summary>
    private VkCommandBuffer[] _presentCmd = [];
    private VkFormat _format = VkFormat.B8G8R8A8Unorm;
    private VkColorSpaceKHR _colorSpace = VkColorSpaceKHR.SrgbNonLinear;
    private VkPresentModeKHR _presentMode = VkPresentModeKHR.Fifo;
    private VkCommandPool _pool;
    private VkFence _acquireFence;

    private uint _usage;
    private uint _acquiredIndex;
    private bool _haveAcquired;
    private int _widthPx;
    private int _heightPx;
    private bool _disposed;

    public nint Instance => _instance.Handle;
    public nint PhysicalDevice => _physicalDevice.Handle;
    public nint Device => _device.Handle;
    public nint GraphicsQueue => _queue.Handle;
    public uint GraphicsQueueFamily => _queueFamily;
    public uint ApiVersion => (uint)VkVersion.Version_1_2;
    public int ImageCount => _images.Length;

    public string DeviceName { get; private set; } = "";

    public string[] EnabledInstanceExtensions { get; private set; } = [];
    public string[] EnabledDeviceExtensions { get; private set; } = ["VK_KHR_swapchain"];

    /// <summary>Kept alive for the renderer's lifetime — Skia holds the pointer.</summary>
    private nint _features2;
    public nint PhysicalDeviceFeatures2 => _features2;

    public nint GetProcedureAddress(string name, nint instance, nint device) =>
        Resolve(name, instance, device);

    public static SdlVulkanHost Create(SDL_Window* window, int widthPx, int heightPx)
    {
        var host = new SdlVulkanHost();
        host.Init(window, widthPx, heightPx);
        return host;
    }

    private void Init(SDL_Window* window, int widthPx, int heightPx)
    {
        Check(vkInitialize(), "vkInitialize");

        // SDL knows which instance extensions this window system needs — VK_KHR_surface plus
        // the platform one — and asking it is the only portable way to find out.
        uint extensionCount = 0;
        var sdlExtensions = SDL_Vulkan_GetInstanceExtensions(&extensionCount);
        if (sdlExtensions is null)
            throw new InvalidOperationException($"SDL_Vulkan_GetInstanceExtensions: {SDL_GetError()}");

        var names = new string[extensionCount];
        for (uint i = 0; i < extensionCount; i++)
            names[i] = Marshal.PtrToStringUTF8((nint)sdlExtensions[i]) ?? "";

        // Without VK_EXT_swapchain_colorspace, SRGB_NONLINEAR is the only colour space a
        // swapchain may legally use — drivers list the others regardless, which makes this
        // easy to leave out and then work by accident on one machine.
        if (WantsWideColor) names = [.. names, "VK_EXT_swapchain_colorspace"];
        EnabledInstanceExtensions = names;

        var appInfo = new VkApplicationInfo { apiVersion = VkVersion.Version_1_2 };
        using var extensions = new VkStringArray(names);
        var instanceInfo = new VkInstanceCreateInfo
        {
            pApplicationInfo = &appInfo,
            enabledExtensionCount = extensions.Length,
            ppEnabledExtensionNames = extensions,
        };
        VkInstance instance;
        Check(vkCreateInstance(&instanceInfo, &instance), "vkCreateInstance");
        _instance = instance;
        LoadInstance(instance);

        ulong surfaceHandle = 0;
        if (!SDL_Vulkan_CreateSurface(window, (VkInstance_T*)_instance.Handle, null,
                (VkSurfaceKHR_T**)&surfaceHandle))
            throw new InvalidOperationException($"SDL_Vulkan_CreateSurface: {SDL_GetError()}");
        _surface = new VkSurfaceKHR(surfaceHandle);

        PickPhysicalDevice();
        CreateDeviceAndQueue();
        BuildSwapchain(widthPx, heightPx);
    }

    /// <summary>The first device with a queue that can both draw and present to this
    /// surface, preferring a discrete one. A queue that can do only one of the two is no
    /// use here and the separate-queue arrangement is not worth its complexity.</summary>
    private void PickPhysicalDevice()
    {
        uint count = 0;
        Check(EnumeratePhysicalDevices(_instance, &count, null), "vkEnumeratePhysicalDevices");
        if (count == 0) throw new InvalidOperationException("no Vulkan physical device");
        var devices = stackalloc VkPhysicalDevice[(int)count];
        Check(EnumeratePhysicalDevices(_instance, &count, devices), "vkEnumeratePhysicalDevices");

        VkPhysicalDevice best = default;
        uint bestFamily = 0;
        bool bestDiscrete = false;

        for (int i = 0; i < count; i++)
        {
            var candidate = devices[i];
            if (!TryFindPresentQueue(candidate, out uint family)) continue;

            var properties = new VkPhysicalDeviceProperties();
            GetPhysicalDeviceProperties(candidate, &properties);
            bool discrete = properties.deviceType == VkPhysicalDeviceType.DiscreteGpu;

            if (best.IsNull || (discrete && !bestDiscrete))
            {
                best = candidate;
                bestFamily = family;
                bestDiscrete = discrete;
                DeviceName = Marshal.PtrToStringUTF8((nint)properties.deviceName) ?? "(unnamed)";
            }
        }

        if (best.IsNull) throw new InvalidOperationException("no Vulkan device can present to this window");
        _physicalDevice = best;
        _queueFamily = bestFamily;
    }

    private bool TryFindPresentQueue(VkPhysicalDevice device, out uint family)
    {
        family = 0;
        uint count = 0;
        GetQueueFamilyProperties(device, &count, null);
        if (count == 0) return false;
        var families = stackalloc VkQueueFamilyProperties[(int)count];
        GetQueueFamilyProperties(device, &count, families);

        for (uint i = 0; i < count; i++)
        {
            if ((families[i].queueFlags & VkQueueFlags.Graphics) == 0) continue;
            uint supported = 0;
            if (GetSurfaceSupport(device, i, _surface, &supported) != VkResult.Success) continue;
            if (supported == 0) continue;
            family = i;
            return true;
        }
        return false;
    }

    private void CreateDeviceAndQueue()
    {
        float priority = 1f;
        var queueInfo = new VkDeviceQueueCreateInfo
        {
            queueFamilyIndex = _queueFamily,
            queueCount = 1,
            pQueuePriorities = &priority,
        };

        using var extensions = new VkStringArray(["VK_KHR_swapchain"]);
        var deviceInfo = new VkDeviceCreateInfo
        {
            queueCreateInfoCount = 1,
            pQueueCreateInfos = &queueInfo,
            enabledExtensionCount = extensions.Length,
            ppEnabledExtensionNames = extensions,
        };
        VkDevice device;
        Check(CreateDevice(_physicalDevice, &deviceInfo, 0, &device), "vkCreateDevice");
        _device = device;
        LoadDevice(device);

        VkQueue queue;
        GetDeviceQueue(_device, _queueFamily, 0, &queue);
        _queue = queue;

        // Skia reads this to build its caps. Queried after the device exists and kept in
        // unmanaged memory because Skia holds the pointer rather than copying.
        _features2 = System.Runtime.InteropServices.Marshal.AllocHGlobal(
            sizeof(VkPhysicalDeviceFeatures2));
        var features = (VkPhysicalDeviceFeatures2*)_features2;
        *features = new VkPhysicalDeviceFeatures2 { sType = VkStructureType.PhysicalDeviceFeatures2 };
        GetPhysicalDeviceFeatures2(_physicalDevice, features);

        var poolInfo = new VkCommandPoolCreateInfo
        {
            queueFamilyIndex = _queueFamily,
            flags = VkCommandPoolCreateFlags.ResetCommandBuffer,
        };
        VkCommandPool pool;
        Check(CreateCommandPool(_device, &poolInfo, 0, &pool), "vkCreateCommandPool");
        _pool = pool;

        var fenceInfo = new VkFenceCreateInfo();
        VkFence fence;
        Check(CreateFence(_device, &fenceInfo, 0, &fence), "vkCreateFence");
        _acquireFence = fence;
    }

    private void BuildSwapchain(int widthPx, int heightPx)
    {
        var capabilities = new VkSurfaceCapabilitiesKHR();
        Check(GetSurfaceCapabilities(_physicalDevice, _surface, &capabilities), "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");

        // currentExtent of 0xFFFFFFFF means "you choose", which is what Wayland says; every
        // other case is the compositor telling us the size and disagreeing with it fails.
        var extent = capabilities.currentExtent.width == uint.MaxValue
            ? new VkExtent2D(
                Math.Clamp((uint)widthPx, capabilities.minImageExtent.width, capabilities.maxImageExtent.width),
                Math.Clamp((uint)heightPx, capabilities.minImageExtent.height, capabilities.maxImageExtent.height))
            : capabilities.currentExtent;

        if (extent.width == 0 || extent.height == 0)
        {
            StartupTrace.Mark($"vulkan swapchain SKIPPED (asked {widthPx}x{heightPx})");
            _swapchain = VkSwapchainKHR.Null;
            _images = [];
            return;
        }

        _format = ChooseFormat(out var colorSpace);
        _colorSpace = colorSpace;

        // A surface that cannot offer these is one this renderer cannot draw on, and saying
        // so here beats a null surface three call frames later.
        uint usage = SwapchainUsage & (uint)capabilities.supportedUsageFlags;
        if ((usage & (UsageColorAttachment | UsageTransferSrc)) != (UsageColorAttachment | UsageTransferSrc))
            throw new InvalidOperationException(
                $"this surface supports usage 0x{(uint)capabilities.supportedUsageFlags:X} — " +
                "Skia needs at least COLOR_ATTACHMENT and TRANSFER_SRC");
        _usage = usage;

        uint imageCount = capabilities.minImageCount + 1;
        if (capabilities.maxImageCount > 0 && imageCount > capabilities.maxImageCount)
            imageCount = capabilities.maxImageCount;

        var old = _swapchain;
        var info = new VkSwapchainCreateInfoKHR
        {
            surface = _surface,
            minImageCount = imageCount,
            imageFormat = _format,
            imageColorSpace = colorSpace,
            imageExtent = extent,
            imageArrayLayers = 1,
            // TransferDst as well as ColorAttachment: Skia wants it for the clear path, and
            // a swapchain image without it fails GRBackendRenderTarget validation.
            imageUsage = (VkImageUsageFlags)_usage,
            imageSharingMode = VkSharingMode.Exclusive,
            preTransform = capabilities.currentTransform,
            compositeAlpha = VkCompositeAlphaFlagsKHR.Opaque,
            presentMode = _presentMode = ChoosePresentMode(),
            clipped = true,
            oldSwapchain = old,
        };

        VkSwapchainKHR swapchain;
        Check(VulkanCommands.CreateSwapchain(_device, &info, 0, &swapchain), "vkCreateSwapchainKHR");
        _swapchain = swapchain;
        if (!old.IsNull) DestroySwapchain(_device, old, 0);

        uint got = 0;
        Check(GetSwapchainImages(_device, _swapchain, &got, null), "vkGetSwapchainImagesKHR");
        var images = new VkImage[got];
        fixed (VkImage* p = images)
            Check(GetSwapchainImages(_device, _swapchain, &got, p), "vkGetSwapchainImagesKHR");
        _images = images;

        CreatePerImageObjects();

        _widthPx = (int)extent.width;
        _heightPx = (int)extent.height;
        StartupTrace.Mark($"vulkan swapchain {_widthPx}x{_heightPx} x{_images.Length} {_format}/{_colorSpace}");
        ResetCapture();
    }

    /// <summary>
    /// One semaphore and one command buffer per swapchain image, replacing the set the last
    /// swapchain had. Both are indexed by image, so an image that is still being presented
    /// never has its own reused underneath it.
    /// </summary>
    private void CreatePerImageObjects()
    {
        DestroyPerImageObjects();

        int count = _images.Length;
        _renderFinished = new VkSemaphore[count];
        _presentCmd = new VkCommandBuffer[count];

        var semaphoreInfo = new VkSemaphoreCreateInfo();
        var allocate = new VkCommandBufferAllocateInfo
        {
            commandPool = _pool,
            level = VkCommandBufferLevel.Primary,
            commandBufferCount = 1,
        };

        for (int i = 0; i < count; i++)
        {
            VkSemaphore semaphore;
            Check(CreateSemaphore(_device, &semaphoreInfo, 0, &semaphore), "vkCreateSemaphore");
            _renderFinished[i] = semaphore;

            VkCommandBuffer cmd;
            Check(AllocateCommandBuffers(_device, &allocate, &cmd), "vkAllocateCommandBuffers");
            _presentCmd[i] = cmd;
        }
    }

    private void DestroyPerImageObjects()
    {
        // The caller has already waited the device idle; nothing here is in flight.
        foreach (var semaphore in _renderFinished)
            if (!semaphore.IsNull) DestroySemaphore(_device, semaphore, 0);
        _renderFinished = [];

        for (int i = 0; i < _presentCmd.Length; i++)
        {
            var cmd = _presentCmd[i];
            if (!cmd.IsNull) FreeCommandBuffers(_device, _pool, 1, &cmd);
        }
        _presentCmd = [];
    }

    /// <summary>
    /// How frames reach the screen, and deliberately not vsync.
    ///
    /// <para>The GL path sets <c>SDL_GL_SetSwapInterval(0)</c> and lets the framework decide
    /// when to draw: <c>Application</c> waits out the display interval itself, except when
    /// something asks to be unpaced — <c>FramePacing.Unlimited</c>, or the DevTools
    /// continuous-redraw mode. FIFO is vsync in the presentation engine, so it overrides
    /// that decision from underneath and pins the loop to the refresh rate however fast the
    /// framework wanted to go. Continuous redraw read exactly 120 on a 120 Hz display.</para>
    ///
    /// <para>IMMEDIATE is the equivalent of swap interval 0 and may tear; MAILBOX is the
    /// tear-free fallback, which still lets the renderer run ahead and simply drops frames
    /// nobody sees. FIFO is the last resort and the only mode an implementation must
    /// support.</para>
    /// </summary>
    private VkPresentModeKHR ChoosePresentMode()
    {
        uint count = 0;
        if (GetSurfacePresentModes(_physicalDevice, _surface, &count, null) != VkResult.Success || count == 0)
            return VkPresentModeKHR.Fifo;

        var modes = stackalloc VkPresentModeKHR[(int)count];
        if (GetSurfacePresentModes(_physicalDevice, _surface, &count, modes) != VkResult.Success)
            return VkPresentModeKHR.Fifo;

        bool immediate = false, mailbox = false;
        for (uint i = 0; i < count; i++)
        {
            if (modes[i] == VkPresentModeKHR.Immediate) immediate = true;
            if (modes[i] == VkPresentModeKHR.Mailbox) mailbox = true;
        }

        var chosen = immediate ? VkPresentModeKHR.Immediate
            : mailbox ? VkPresentModeKHR.Mailbox
            : VkPresentModeKHR.Fifo;
        StartupTrace.Mark($"vulkan present mode {chosen}");
        return chosen;
    }

    /// <summary>
    /// A plain 8-bit BGRA or RGBA format with the sRGB-nonlinear colour space — phase 1 is
    /// SDR and the aim is to match the GL path exactly. The <c>_SRGB</c> variants are
    /// deliberately not preferred: they would have the hardware do the encode, which is a
    /// different answer from the one the GL surface gives, and telling the two apart is the
    /// whole test for this phase. Phase 3 is where this function learns about HDR.
    /// </summary>
    private VkFormat ChooseFormat(out VkColorSpaceKHR colorSpace)
    {
        colorSpace = VkColorSpaceKHR.SrgbNonLinear;

        uint count = 0;
        Check(GetSurfaceFormats(_physicalDevice, _surface, &count, null), "vkGetPhysicalDeviceSurfaceFormatsKHR");
        if (count == 0) return VkFormat.B8G8R8A8Unorm;

        var formats = stackalloc VkSurfaceFormatKHR[(int)count];
        Check(GetSurfaceFormats(_physicalDevice, _surface, &count, formats), "vkGetPhysicalDeviceSurfaceFormatsKHR");

        // Half-float, linear light, 1.0 being SDR white — scRGB. Every colour the framework
        // has is 8-bit sRGB, so this is only correct because the renderer now tells Skia what
        // the destination is and lets it convert; before phase 2 it would have washed the
        // whole UI out. Opt-in while that claim is only proven on one machine.
        if (WantsWideColor)
        {
            for (uint i = 0; i < count; i++)
            {
                if (formats[i].format != VkFormat.R16G16B16A16Sfloat) continue;
                if (formats[i].colorSpace != VkColorSpaceKHR.ExtendedSrgbLinearEXT) continue;
                colorSpace = formats[i].colorSpace;
                return formats[i].format;
            }
            StartupTrace.Mark("vulkan wide colour asked for, surface has none");
        }

        for (uint i = 0; i < count; i++)
        {
            if (formats[i].colorSpace != VkColorSpaceKHR.SrgbNonLinear) continue;
            if (formats[i].format is VkFormat.B8G8R8A8Unorm or VkFormat.R8G8B8A8Unorm)
            {
                colorSpace = formats[i].colorSpace;
                return formats[i].format;
            }
        }

        colorSpace = formats[0].colorSpace;
        return formats[0].format;
    }

    public VulkanFrameTarget? AcquireNextImage()
    {
        if (_disposed || _swapchain.IsNull || _images.Length == 0) return null;
        if (_haveAcquired) return Current();

        uint index = 0;
        var fence = _acquireFence;
        var result = VulkanCommands.AcquireNextImage(_device, _swapchain, NoTimeout, VkSemaphore.Null, fence, &index);

        if (result is VkResult.ErrorOutOfDateKHR)
        {
            // The window changed under us. Rebuilding here rather than reporting an error
            // keeps the caller's "no frame this time" contract simple.
            RecreateSwapchain(_widthPx, _heightPx);
            return null;
        }
        if (result is not (VkResult.Success or VkResult.SuboptimalKHR)) return null;

        // The fence is how this waits for the image without a semaphore to hand Skia — see
        // the note on the class.
        Check(WaitForFences(_device, 1, &fence, 1, NoTimeout), "vkWaitForFences");
        Check(ResetFences(_device, 1, &fence), "vkResetFences");

        _acquiredIndex = index;
        _haveAcquired = true;
        return Current();
    }

    private VulkanFrameTarget Current() => new(
        _images[_acquiredIndex].Handle,
        (uint)_format,
        // Undefined every frame: the contents are about to be overwritten, and claiming the
        // present layout would have Skia skip a transition it still needs to make.
        Layout: 0,
        _widthPx, _heightPx, (int)_acquiredIndex, _usage, (uint)_colorSpace);

    public void PresentAcquiredImage()
    {
        if (!_haveAcquired || _swapchain.IsNull) return;

        // The capture reads the image on the CPU, so it is the one thing here that still has
        // to wait for the GPU. Diagnostic only, and off unless asked for.
        CaptureIfAsked(_images[_acquiredIndex]);

        // Skia leaves the image as a colour attachment; the presentation engine requires
        // PRESENT_SRC_KHR, and there is no binding call to ask Skia for the transition.
        //
        // No queue drain before this any more. The barrier's source stage is
        // COLOR_ATTACHMENT_OUTPUT, which is an execution dependency against everything Skia
        // submitted earlier on the same queue — ordering that Vulkan already guarantees and
        // that vkQueueWaitIdle was paying for twice.
        var done = TransitionToPresent(_images[_acquiredIndex], _acquiredIndex);

        uint index = _acquiredIndex;
        var swapchain = _swapchain;
        var present = new VkPresentInfoKHR
        {
            // …and this is what replaces the drain: presentation waits for that submit to
            // signal rather than for the whole queue to fall idle. Without a wait semaphore
            // a present is not ordered against prior queue work at all.
            waitSemaphoreCount = 1,
            pWaitSemaphores = &done,
            swapchainCount = 1,
            pSwapchains = &swapchain,
            pImageIndices = &index,
        };
        var result = QueuePresent(_queue, &present);
        _haveAcquired = false;

        if (result is VkResult.ErrorOutOfDateKHR or VkResult.SuboptimalKHR)
            RecreateSwapchain(_widthPx, _heightPx);
    }

    /// <summary>Records and submits the transition to PRESENT_SRC_KHR, returning the
    /// semaphore it signals when that work is done.</summary>
    private VkSemaphore TransitionToPresent(VkImage image, uint imageIndex)
    {
        var cmd = _presentCmd[imageIndex];
        var begin = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        Check(BeginCommandBuffer(cmd, &begin), "vkBeginCommandBuffer");

        var barrier = new VkImageMemoryBarrier
        {
            sType = VkStructureType.ImageMemoryBarrier,
            srcAccessMask = VkAccessFlags.ColorAttachmentWrite,
            dstAccessMask = VkAccessFlags.None,
            oldLayout = VkImageLayout.ColorAttachmentOptimal,
            newLayout = VkImageLayout.PresentSrcKHR,
            srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            image = image,
            subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1),
        };
        CmdPipelineBarrier(cmd, VkPipelineStageFlags.ColorAttachmentOutput,
            VkPipelineStageFlags.BottomOfPipe, VkDependencyFlags.None, 0, 0, 0, 0, 1, &barrier);

        Check(EndCommandBuffer(cmd), "vkEndCommandBuffer");

        var signal = _renderFinished[imageIndex];
        var submit = new VkSubmitInfo
        {
            commandBufferCount = 1,
            pCommandBuffers = &cmd,
            signalSemaphoreCount = 1,
            pSignalSemaphores = &signal,
        };
        Check(QueueSubmit(_queue, 1, &submit, VkFence.Null), "vkQueueSubmit");
        return signal;
    }

    public void RecreateSwapchain(int widthPx, int heightPx)
    {
        if (_disposed) return;
        Check(DeviceWaitIdle(_device), "vkDeviceWaitIdle");
        _haveAcquired = false;
        BuildSwapchain(widthPx, heightPx);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_device.IsNull)
        {
            DeviceWaitIdle(_device);
            DestroyPerImageObjects();
            if (!_acquireFence.IsNull) DestroyFence(_device, _acquireFence, 0);
            if (!_pool.IsNull) DestroyCommandPool(_device, _pool, 0);
            if (!_swapchain.IsNull) DestroySwapchain(_device, _swapchain, 0);
            DestroyDevice(_device, 0);
        }
        if (!_surface.IsNull) DestroySurface(_instance, _surface, 0);
        if (!_instance.IsNull) DestroyInstance(_instance, 0);
        if (_features2 != 0) System.Runtime.InteropServices.Marshal.FreeHGlobal(_features2);
    }

    private static void Check(VkResult result, string what)
    {
        if (result != VkResult.Success)
            throw new InvalidOperationException($"{what}: {result}");
    }
}

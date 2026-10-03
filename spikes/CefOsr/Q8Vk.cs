using System.Diagnostics;
using System.Runtime.InteropServices;
using CefOsr.Capi;
using Vortice.Vulkan;
using static CefOsr.Native;
using static Vortice.Vulkan.Vulkan;

namespace CefOsr;

/// <summary>
/// Question 8 in Vulkan: in on_accelerated_paint, import the dmabuf through
/// VK_EXT_external_memory_dma_buf and VK_EXT_image_drm_format_modifier, copy it into an image
/// of ours, wait, and return. Structs and enums from Vortice.Vulkan; commands loaded by hand.
/// </summary>
internal static unsafe partial class Q8
{
    static VkInstance _inst;
    static VkPhysicalDevice _phys;
    static VkDevice _dev;
    static VkQueue _queue;
    static uint _family;
    static VkCommandPool _pool;
    static VkCommandBuffer _cmd;
    static VkFence _fence;
    static VkImage _own;
    static VkDeviceMemory _ownMem;
    static int _vkW, _vkH;
    static VkPhysicalDeviceMemoryProperties _memProps;
    static string _vkInfo = "";

    static delegate* unmanaged<VkInstance, byte*, nint> _gipa;
    static delegate* unmanaged<VkDevice, byte*, nint> _gdpa;

    static nint I(string n) { var b = System.Text.Encoding.ASCII.GetBytes(n + "\0"); fixed (byte* p = b) return _gipa(_inst, p); }
    static nint D(string n) { var b = System.Text.Encoding.ASCII.GetBytes(n + "\0"); fixed (byte* p = b) { var r = _gdpa(_dev, p); return r != 0 ? r : throw new Exception(n); } }

    static void Check(VkResult r, string what) { if (r != VkResult.Success) throw new Exception($"{what}: {r}"); }

    static void VkInit()
    {
        Check(vkInitialize(), "vkInitialize");
        var lib = NativeLibrary.Load("libvulkan.so.1");
        _gipa = (delegate* unmanaged<VkInstance, byte*, nint>)NativeLibrary.GetExport(lib, "vkGetInstanceProcAddr");
        var app = new VkApplicationInfo { apiVersion = VkVersion.Version_1_3 };
        var ici = new VkInstanceCreateInfo { pApplicationInfo = &app };
        VkInstance inst;
        Check(vkCreateInstance(&ici, &inst), "vkCreateInstance");
        _inst = inst;

        var enumerate = (delegate* unmanaged<VkInstance, uint*, VkPhysicalDevice*, VkResult>)I("vkEnumeratePhysicalDevices");
        var props = (delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceProperties*, void>)I("vkGetPhysicalDeviceProperties");
        uint n = 0; enumerate(_inst, &n, null);
        var devs = stackalloc VkPhysicalDevice[(int)n]; enumerate(_inst, &n, devs);
        for (int i = 0; i < n; i++)
        {
            VkPhysicalDeviceProperties p; props(devs[i], &p);
            if (p.deviceType == VkPhysicalDeviceType.IntegratedGpu || p.deviceType == VkPhysicalDeviceType.DiscreteGpu)
            {
                _phys = devs[i];
                _vkInfo = "Vulkan on " + Marshal.PtrToStringUTF8((nint)p.deviceName);
                break;
            }
        }
        var memProps = (delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceMemoryProperties*, void>)I("vkGetPhysicalDeviceMemoryProperties");
        VkPhysicalDeviceMemoryProperties mp; memProps(_phys, &mp); _memProps = mp;
        var qfp = (delegate* unmanaged<VkPhysicalDevice, uint*, VkQueueFamilyProperties*, void>)I("vkGetPhysicalDeviceQueueFamilyProperties");
        uint qn = 0; qfp(_phys, &qn, null);
        var qs = stackalloc VkQueueFamilyProperties[(int)qn]; qfp(_phys, &qn, qs);
        for (uint i = 0; i < qn; i++) if ((qs[i].queueFlags & VkQueueFlags.Graphics) != 0) { _family = i; break; }

        float prio = 1;
        var qci = new VkDeviceQueueCreateInfo { queueFamilyIndex = _family, queueCount = 1, pQueuePriorities = &prio };
        var exts = new[] { "VK_KHR_external_memory_fd", "VK_EXT_external_memory_dma_buf", "VK_EXT_image_drm_format_modifier" };
        using var extNames = new VkStringArray(exts);
        var dci = new VkDeviceCreateInfo { queueCreateInfoCount = 1, pQueueCreateInfos = &qci, enabledExtensionCount = extNames.Length, ppEnabledExtensionNames = extNames };
        var createDevice = (delegate* unmanaged<VkPhysicalDevice, VkDeviceCreateInfo*, nint, VkDevice*, VkResult>)I("vkCreateDevice");
        VkDevice dev;
        Check(createDevice(_phys, &dci, 0, &dev), "vkCreateDevice (" + string.Join(", ", exts) + ")");
        _dev = dev;
        _gdpa = (delegate* unmanaged<VkDevice, byte*, nint>)I("vkGetDeviceProcAddr");
        VkQueue q; ((delegate* unmanaged<VkDevice, uint, uint, VkQueue*, void>)D("vkGetDeviceQueue"))(_dev, _family, 0, &q); _queue = q;
        var pci = new VkCommandPoolCreateInfo { queueFamilyIndex = _family, flags = VkCommandPoolCreateFlags.ResetCommandBuffer };
        VkCommandPool pool; Check(((delegate* unmanaged<VkDevice, VkCommandPoolCreateInfo*, nint, VkCommandPool*, VkResult>)D("vkCreateCommandPool"))(_dev, &pci, 0, &pool), "pool"); _pool = pool;
        var cai = new VkCommandBufferAllocateInfo { commandPool = _pool, level = VkCommandBufferLevel.Primary, commandBufferCount = 1 };
        VkCommandBuffer cb; Check(((delegate* unmanaged<VkDevice, VkCommandBufferAllocateInfo*, VkCommandBuffer*, VkResult>)D("vkAllocateCommandBuffers"))(_dev, &cai, &cb), "cmd"); _cmd = cb;
        var fci = new VkFenceCreateInfo();
        VkFence fence; Check(((delegate* unmanaged<VkDevice, VkFenceCreateInfo*, nint, VkFence*, VkResult>)D("vkCreateFence"))(_dev, &fci, 0, &fence), "fence"); _fence = fence;
        _vkInfo += ", device extensions " + string.Join(", ", exts);
    }

    static uint MemType(uint bits, VkMemoryPropertyFlags want)
    {
        for (uint i = 0; i < _memProps.memoryTypeCount; i++)
            if ((bits & (1u << (int)i)) != 0 && (_memProps.memoryTypes[(int)i].propertyFlags & want) == want) return i;
        throw new Exception("no memory type");
    }

    static VkImage NewImage(int w, int h, VkImageTiling tiling, VkImageUsageFlags usage, void* next)
    {
        var ici = new VkImageCreateInfo
        {
            pNext = next, imageType = VkImageType.Image2D, format = VkFormat.B8G8R8A8Unorm,
            extent = new VkExtent3D((uint)w, (uint)h, 1), mipLevels = 1, arrayLayers = 1,
            samples = VkSampleCountFlags.Count1, tiling = tiling, usage = usage,
            sharingMode = VkSharingMode.Exclusive, initialLayout = VkImageLayout.Undefined,
        };
        VkImage img;
        Check(((delegate* unmanaged<VkDevice, VkImageCreateInfo*, nint, VkImage*, VkResult>)D("vkCreateImage"))(_dev, &ici, 0, &img), "vkCreateImage");
        return img;
    }

    static VkImageMemoryBarrier Barrier(VkImage img, VkImageLayout from, VkImageLayout to, uint srcFamily, uint dstFamily,
        VkAccessFlags srcAccess, VkAccessFlags dstAccess) => new()
    {
        oldLayout = from, newLayout = to, srcQueueFamilyIndex = srcFamily, dstQueueFamilyIndex = dstFamily,
        image = img, srcAccessMask = srcAccess, dstAccessMask = dstAccess,
        subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1),
    };

    const uint QueueFamilyExternal = ~1u;   // VK_QUEUE_FAMILY_EXTERNAL

    /// <summary>Import the frame, copy it into our image, wait. Optionally read our image back.</summary>
    static bool VkCopyFrame(cef_accelerated_paint_info_t* info, byte[]? readBack = null)
    {
        int w = info->extra.coded_size.width, h = info->extra.coded_size.height;
        if (_dev.IsNull) VkInit();
        var getMemReq = (delegate* unmanaged<VkDevice, VkImage, VkMemoryRequirements*, void>)D("vkGetImageMemoryRequirements");
        var alloc = (delegate* unmanaged<VkDevice, VkMemoryAllocateInfo*, nint, VkDeviceMemory*, VkResult>)D("vkAllocateMemory");
        var bind = (delegate* unmanaged<VkDevice, VkImage, VkDeviceMemory, ulong, VkResult>)D("vkBindImageMemory");
        var destroyImage = (delegate* unmanaged<VkDevice, VkImage, nint, void>)D("vkDestroyImage");
        var free = (delegate* unmanaged<VkDevice, VkDeviceMemory, nint, void>)D("vkFreeMemory");
        if (w != _vkW || h != _vkH)
        {
            _own = NewImage(w, h, VkImageTiling.Optimal, VkImageUsageFlags.TransferDst | VkImageUsageFlags.TransferSrc | VkImageUsageFlags.Sampled, null);
            VkMemoryRequirements r; getMemReq(_dev, _own, &r);
            var mai = new VkMemoryAllocateInfo { allocationSize = r.size, memoryTypeIndex = MemType(r.memoryTypeBits, VkMemoryPropertyFlags.DeviceLocal) };
            VkDeviceMemory m; Check(alloc(_dev, &mai, 0, &m), "own memory"); _ownMem = m;
            Check(bind(_dev, _own, _ownMem, 0), "bind own");
            _vkW = w; _vkH = h;
        }

        // The imported image: the dmabuf's own layout, said explicitly.
        var plane = new VkSubresourceLayout { offset = info->planes_0.offset, rowPitch = info->planes_0.stride };
        var explicitMod = new VkImageDrmFormatModifierExplicitCreateInfoEXT { drmFormatModifier = info->modifier, drmFormatModifierPlaneCount = 1, pPlaneLayouts = &plane };
        var external = new VkExternalMemoryImageCreateInfo { pNext = &explicitMod, handleTypes = VkExternalMemoryHandleTypeFlags.DmaBufEXT };
        var img = NewImage(w, h, VkImageTiling.DrmFormatModifierEXT, VkImageUsageFlags.TransferSrc, &external);
        VkMemoryRequirements req; getMemReq(_dev, img, &req);
        int fd = dup(info->planes_0.fd);   // a successful import takes ownership of the fd
        var fdProps = new VkMemoryFdPropertiesKHR();
        Check(((delegate* unmanaged<VkDevice, VkExternalMemoryHandleTypeFlags, int, VkMemoryFdPropertiesKHR*, VkResult>)D("vkGetMemoryFdPropertiesKHR"))(
            _dev, VkExternalMemoryHandleTypeFlags.DmaBufEXT, fd, &fdProps), "vkGetMemoryFdPropertiesKHR");
        var dedicated = new VkMemoryDedicatedAllocateInfo { image = img };
        var import = new VkImportMemoryFdInfoKHR { pNext = &dedicated, handleType = VkExternalMemoryHandleTypeFlags.DmaBufEXT, fd = fd };
        var ia = new VkMemoryAllocateInfo { pNext = &import, allocationSize = req.size, memoryTypeIndex = MemType(req.memoryTypeBits & fdProps.memoryTypeBits, 0) };
        VkDeviceMemory mem;
        var res = alloc(_dev, &ia, 0, &mem);
        if (res != VkResult.Success) { close(fd); destroyImage(_dev, img, 0); return false; }
        Check(bind(_dev, img, mem, 0), "bind imported");

        var begin = (delegate* unmanaged<VkCommandBuffer, VkCommandBufferBeginInfo*, VkResult>)D("vkBeginCommandBuffer");
        var end = (delegate* unmanaged<VkCommandBuffer, VkResult>)D("vkEndCommandBuffer");
        var barrier = (delegate* unmanaged<VkCommandBuffer, VkPipelineStageFlags, VkPipelineStageFlags, VkDependencyFlags, uint, nint, uint, nint, uint, VkImageMemoryBarrier*, void>)D("vkCmdPipelineBarrier");
        var copy = (delegate* unmanaged<VkCommandBuffer, VkImage, VkImageLayout, VkImage, VkImageLayout, uint, VkImageCopy*, void>)D("vkCmdCopyImage");
        var bi = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        Check(begin(_cmd, &bi), "begin");
        var bars = stackalloc VkImageMemoryBarrier[2];
        // Take the buffer from the external owner (CEF's GPU process) without discarding it.
        bars[0] = Barrier(img, VkImageLayout.General, VkImageLayout.TransferSrcOptimal, QueueFamilyExternal, _family, 0, VkAccessFlags.TransferRead);
        bars[1] = Barrier(_own, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal, ~0u, ~0u, 0, VkAccessFlags.TransferWrite);
        barrier(_cmd, VkPipelineStageFlags.TopOfPipe, VkPipelineStageFlags.Transfer, 0, 0, 0, 0, 0, 2, bars);
        var region = new VkImageCopy
        {
            srcSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
            dstSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
            extent = new VkExtent3D((uint)w, (uint)h, 1),
        };
        copy(_cmd, img, VkImageLayout.TransferSrcOptimal, _own, VkImageLayout.TransferDstOptimal, 1, &region);
        // Give it back to the external owner.
        bars[0] = Barrier(img, VkImageLayout.TransferSrcOptimal, VkImageLayout.General, _family, QueueFamilyExternal, VkAccessFlags.TransferRead, 0);
        barrier(_cmd, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.BottomOfPipe, 0, 0, 0, 0, 0, 1, bars);

        VkBuffer buf = default; VkDeviceMemory bufMem = default;
        if (readBack != null)
        {
            var bci = new VkBufferCreateInfo { size = (ulong)(w * h * 4), usage = VkBufferUsageFlags.TransferDst, sharingMode = VkSharingMode.Exclusive };
            Check(((delegate* unmanaged<VkDevice, VkBufferCreateInfo*, nint, VkBuffer*, VkResult>)D("vkCreateBuffer"))(_dev, &bci, 0, &buf), "buffer");
            VkMemoryRequirements br; ((delegate* unmanaged<VkDevice, VkBuffer, VkMemoryRequirements*, void>)D("vkGetBufferMemoryRequirements"))(_dev, buf, &br);
            var bai = new VkMemoryAllocateInfo { allocationSize = br.size, memoryTypeIndex = MemType(br.memoryTypeBits, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent) };
            Check(alloc(_dev, &bai, 0, &bufMem), "buffer memory");
            Check(((delegate* unmanaged<VkDevice, VkBuffer, VkDeviceMemory, ulong, VkResult>)D("vkBindBufferMemory"))(_dev, buf, bufMem, 0), "bind buffer");
            bars[0] = Barrier(_own, VkImageLayout.TransferDstOptimal, VkImageLayout.TransferSrcOptimal, ~0u, ~0u, VkAccessFlags.TransferWrite, VkAccessFlags.TransferRead);
            barrier(_cmd, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.Transfer, 0, 0, 0, 0, 0, 1, bars);
            var bic = new VkBufferImageCopy
            {
                imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
                imageExtent = new VkExtent3D((uint)w, (uint)h, 1),
            };
            ((delegate* unmanaged<VkCommandBuffer, VkImage, VkImageLayout, VkBuffer, uint, VkBufferImageCopy*, void>)D("vkCmdCopyImageToBuffer"))(
                _cmd, _own, VkImageLayout.TransferSrcOptimal, buf, 1, &bic);
        }
        Check(end(_cmd), "end");
        var cmd = _cmd;
        var si = new VkSubmitInfo { commandBufferCount = 1, pCommandBuffers = &cmd };
        var fence = _fence;
        Check(((delegate* unmanaged<VkQueue, uint, VkSubmitInfo*, VkFence, VkResult>)D("vkQueueSubmit"))(_queue, 1, &si, _fence), "submit");
        ((delegate* unmanaged<VkDevice, uint, VkFence*, uint, ulong, VkResult>)D("vkWaitForFences"))(_dev, 1, &fence, 1, ulong.MaxValue);
        ((delegate* unmanaged<VkDevice, uint, VkFence*, VkResult>)D("vkResetFences"))(_dev, 1, &fence);
        if (readBack != null)
        {
            void* mapped;
            ((delegate* unmanaged<VkDevice, VkDeviceMemory, ulong, ulong, uint, void**, VkResult>)D("vkMapMemory"))(_dev, bufMem, 0, ulong.MaxValue, 0, &mapped);
            Marshal.Copy((nint)mapped, readBack, 0, Math.Min(readBack.Length, w * h * 4));
            ((delegate* unmanaged<VkDevice, VkDeviceMemory, void>)D("vkUnmapMemory"))(_dev, bufMem);
            ((delegate* unmanaged<VkDevice, VkBuffer, nint, void>)D("vkDestroyBuffer"))(_dev, buf, 0);
            free(_dev, bufMem, 0);
        }
        destroyImage(_dev, img, 0);
        free(_dev, mem, 0);   // closes the imported (dup'd) fd
        return true;
    }

    static int RunVk(Options o)
    {
        if (Host.Initialize([], o) != 1) return 1;
        Host.Pump(() => Host.ContextReady, 15000);
        int frames = 0, failures = 0;
        double ms = 0, max = 0;
        byte[]? first = null;
        Host.AcceleratedPaint += (b, type, p) =>
        {
            var info = (cef_accelerated_paint_info_t*)p;
            var sw = Stopwatch.StartNew();
            byte[]? rb = frames == 8 ? new byte[info->extra.coded_size.width * info->extra.coded_size.height * 4] : null;
            try { if (!VkCopyFrame(info, rb)) { failures++; return; } }
            catch (Exception e) { failures++; if (failures == 1) Log("Vulkan: " + e.Message); return; }
            double t = sw.Elapsed.TotalMilliseconds;
            if (frames++ == 0) Log($"first frame: {_vkInfo}; import + copy + wait {t:F2} ms (includes device creation)");
            else if (rb == null) { ms += t; max = Math.Max(max, t); }
            if (rb != null) first = rb;
        };
        Host.ViewWidth = 800; Host.ViewHeight = 600; Host.Scale = 1.5f;
        Host.CreateBrowser(Host.DataUrl(StillPageForQ8), sharedTexture: true, sync: false);
        Host.Wait(2500);
        if (first != null)
        {
            Png.SaveBgra("results/q8-vk-imported.png", Marshal.UnsafeAddrOfPinnedArrayElement(first, 0), _vkW, _vkH);
            string Px(int x, int y) { int i = (y * _vkW + x) * 4; return $"({x},{y}) BGRA {first[i]:X2} {first[i + 1]:X2} {first[i + 2]:X2} {first[i + 3]:X2}"; }
            Log($"read back from our image {_vkW}x{_vkH}: {Px(10, 10)}; {Px(149, 149)}; {Px(150, 150)}; {Px(_vkW - 10, _vkH - 10)}; {Px(_vkW - 151, _vkH - 151)}");
        }
        Log($"{frames} frames copied, {failures} failed; import + copy + wait avg {(frames > 2 ? ms / (frames - 2) : 0):F2} ms, max {max:F2} ms");
        Q1.Shutdown();
        return 0;
    }
}

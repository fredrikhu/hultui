using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace HdrInterop;

/// <summary>
/// A Vulkan device, and one RGBA16F image on it whose memory can be handed to another
/// API. This is the thing phase 0 exists to test: if OpenGL cannot be made to render into
/// this image, the route in docs/design/HDR.md does not work.
/// </summary>
internal sealed unsafe class VulkanImage : IDisposable
{
    public VkInstance Instance;
    public VkPhysicalDevice PhysicalDevice;
    public VkDevice Device;
    public VkQueue Queue;
    public uint QueueFamily;

    public VkImage Image;
    public VkDeviceMemory Memory;
    public ulong AllocationSize;
    public int Width, Height;

    public string DeviceName = "";
    public byte[] DeviceUuid = new byte[16];

    private VkCommandPool _pool;

    public static VulkanImage Create(int width, int height)
    {
        var vk = new VulkanImage { Width = width, Height = height };
        vk.Init();
        vk.CreateExportableImage();
        return vk;
    }

    private void Init()
    {
        Check(vkInitialize(), "vkInitialize");

        // No application name: it is optional, and the only thing it changes is what a
        // driver's log calls us.
        var appInfo = new VkApplicationInfo { apiVersion = VkVersion.Version_1_2 };

        // The capabilities extensions are what let the device report which external handle
        // types it can export at all.
        var instanceExtensions = new[]
        {
            "VK_KHR_external_memory_capabilities",
            "VK_KHR_get_physical_device_properties2",
        };

        using var instExt = new VkStringArray(instanceExtensions);
        var instanceInfo = new VkInstanceCreateInfo
        {
            pApplicationInfo = &appInfo,
            enabledExtensionCount = instExt.Length,
            ppEnabledExtensionNames = instExt,
        };
        VkInstance instance;
        Check(vkCreateInstance(&instanceInfo, &instance), "vkCreateInstance");
        Instance = instance;
        VkCmd.LoadInstance(instance);

        // The first discrete device, or the first of any kind. On a laptop this is the
        // choice that decides whether the GL context is even on the same GPU.
        uint deviceCount = 0;
        Check(VkCmd.EnumeratePhysicalDevices(Instance, &deviceCount, null), "vkEnumeratePhysicalDevices");
        if (deviceCount == 0) throw new InvalidOperationException("no Vulkan physical device");
        var devices = stackalloc VkPhysicalDevice[(int)deviceCount];
        Check(VkCmd.EnumeratePhysicalDevices(Instance, &deviceCount, devices), "vkEnumeratePhysicalDevices");

        VkPhysicalDevice chosen = default;
        for (int d = 0; d < deviceCount; d++)
        {
            var candidate = devices[d];
            var props = new VkPhysicalDeviceProperties();
            VkCmd.GetPhysicalDeviceProperties(candidate, &props);
            if (chosen.IsNull) chosen = candidate;
            if (props.deviceType == VkPhysicalDeviceType.DiscreteGpu) { chosen = candidate; break; }
        }
        if (chosen.IsNull) throw new InvalidOperationException("no Vulkan physical device");
        PhysicalDevice = chosen;

        var properties = new VkPhysicalDeviceProperties();
        VkCmd.GetPhysicalDeviceProperties(PhysicalDevice, &properties);
        DeviceName = ReadFixedName(&properties);

        // The UUID is how the GL side is checked to be the same GPU.
        var idProps = new VkPhysicalDeviceIDProperties { sType = VkStructureType.PhysicalDeviceIdProperties };
        var props2 = new VkPhysicalDeviceProperties2
        {
            sType = VkStructureType.PhysicalDeviceProperties2,
            pNext = &idProps,
        };
        VkCmd.GetPhysicalDeviceProperties2(PhysicalDevice, &props2);
        for (int i = 0; i < 16; i++) DeviceUuid[i] = idProps.deviceUUID[i];

        QueueFamily = FindGraphicsQueue();

        float priority = 1f;
        var queueInfo = new VkDeviceQueueCreateInfo
        {
            queueFamilyIndex = QueueFamily,
            queueCount = 1,
            pQueuePriorities = &priority,
        };

        var deviceExtensions = OperatingSystem.IsWindows()
            ? new[] { "VK_KHR_external_memory", "VK_KHR_external_memory_win32", "VK_KHR_dedicated_allocation" }
            : new[] { "VK_KHR_external_memory", "VK_KHR_external_memory_fd", "VK_KHR_dedicated_allocation" };

        using var devExt = new VkStringArray(deviceExtensions);
        var deviceInfo = new VkDeviceCreateInfo
        {
            queueCreateInfoCount = 1,
            pQueueCreateInfos = &queueInfo,
            enabledExtensionCount = devExt.Length,
            ppEnabledExtensionNames = devExt,
        };
        VkDevice device;
        Check(VkCmd.CreateDevice(PhysicalDevice, &deviceInfo, 0, &device), "vkCreateDevice");
        Device = device;
        VkCmd.LoadDevice(device);

        VkQueue queue;
        VkCmd.GetDeviceQueue(Device, QueueFamily, 0, &queue);
        Queue = queue;

        var poolInfo = new VkCommandPoolCreateInfo
        {
            queueFamilyIndex = QueueFamily,
            flags = VkCommandPoolCreateFlags.ResetCommandBuffer,
        };
        VkCommandPool pool;
        Check(VkCmd.CreateCommandPool(Device, &poolInfo, 0, &pool), "vkCreateCommandPool");
        _pool = pool;
    }

    private uint FindGraphicsQueue()
    {
        uint count = 0;
        VkCmd.GetQueueFamilyProperties(PhysicalDevice, &count, null);
        var families = stackalloc VkQueueFamilyProperties[(int)count];
        VkCmd.GetQueueFamilyProperties(PhysicalDevice, &count, families);
        for (uint i = 0; i < count; i++)
            if ((families[i].queueFlags & VkQueueFlags.Graphics) != 0) return i;
        throw new InvalidOperationException("no graphics queue family");
    }

    /// <summary>
    /// The image, with its memory marked exportable. Two things matter and both are easy
    /// to leave out: the image itself must say it will be externally shared
    /// (<see cref="VkExternalMemoryImageCreateInfo"/>), and the allocation must say the same
    /// (<see cref="VkExportMemoryAllocateInfo"/>). Dedicated allocation is not strictly
    /// required but every vendor's documentation asks for it here, and the drivers that
    /// misbehave without it do so silently.
    /// </summary>
    private void CreateExportableImage()
    {
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
            format = VkFormat.R16G16B16A16Sfloat,
            extent = new VkExtent3D(Width, Height, 1),
            mipLevels = 1,
            arrayLayers = 1,
            samples = VkSampleCountFlags.Count1,
            tiling = VkImageTiling.Optimal,
            usage = VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled
                  | VkImageUsageFlags.TransferSrc | VkImageUsageFlags.TransferDst,
            sharingMode = VkSharingMode.Exclusive,
            initialLayout = VkImageLayout.Undefined,
        };
        VkImage image;
        Check(VkCmd.CreateImage(Device, &imageInfo, 0, &image), "vkCreateImage");
        Image = image;

        var requirements = new VkMemoryRequirements();
        VkCmd.GetImageMemoryRequirements(Device, Image, &requirements);
        AllocationSize = requirements.size;

        var dedicated = new VkMemoryDedicatedAllocateInfo
        {
            sType = VkStructureType.MemoryDedicatedAllocateInfo,
            image = Image,
        };
        var exportInfo = new VkExportMemoryAllocateInfo
        {
            sType = VkStructureType.ExportMemoryAllocateInfo,
            pNext = &dedicated,
            handleTypes = handleType,
        };
        var allocInfo = new VkMemoryAllocateInfo
        {
            pNext = &exportInfo,
            allocationSize = requirements.size,
            memoryTypeIndex = FindMemoryType(requirements.memoryTypeBits, VkMemoryPropertyFlags.DeviceLocal),
        };
        VkDeviceMemory memory;
        Check(VkCmd.AllocateMemory(Device, &allocInfo, 0, &memory), "vkAllocateMemory");
        Memory = memory;

        Check(VkCmd.BindImageMemory(Device, Image, Memory, 0), "vkBindImageMemory");
    }

    /// <summary>The OS handle for the image's memory, for the other API to import.</summary>
    public nint ExportWin32Handle()
    {
        var info = new VkMemoryGetWin32HandleInfoKHR
        {
            sType = VkStructureType.MemoryGetWin32HandleInfoKHR,
            memory = Memory,
            handleType = VkExternalMemoryHandleTypeFlags.OpaqueWin32,
        };
        nint handle;
        Check(VkCmd.GetMemoryWin32Handle(Device, &info, &handle), "vkGetMemoryWin32HandleKHR");
        return handle;
    }

    public int ExportFd()
    {
        var info = new VkMemoryGetFdInfoKHR
        {
            sType = VkStructureType.MemoryGetFDInfoKHR,
            memory = Memory,
            handleType = VkExternalMemoryHandleTypeFlags.OpaqueFD,
        };
        int fd;
        Check(VkCmd.GetMemoryFd(Device, &info, &fd), "vkGetMemoryFdKHR");
        return fd;
    }

    /// <summary>
    /// Copies the image back into host memory so the caller can look at what the other API
    /// wrote. Slow and entirely the point: this is the evidence.
    /// </summary>
    public Half[] ReadBack()
    {
        ulong bytes = (ulong)Width * (ulong)Height * 8;   // RGBA, 16 bits each

        var bufferInfo = new VkBufferCreateInfo
        {
            size = bytes,
            usage = VkBufferUsageFlags.TransferDst,
            sharingMode = VkSharingMode.Exclusive,
        };
        VkBuffer buffer;
        Check(VkCmd.CreateBuffer(Device, &bufferInfo, 0, &buffer), "vkCreateBuffer");

        var requirements = new VkMemoryRequirements();
        VkCmd.GetBufferMemoryRequirements(Device, buffer, &requirements);
        var allocInfo = new VkMemoryAllocateInfo
        {
            allocationSize = requirements.size,
            memoryTypeIndex = FindMemoryType(requirements.memoryTypeBits,
                VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent),
        };
        VkDeviceMemory bufferMemory;
        Check(VkCmd.AllocateMemory(Device, &allocInfo, 0, &bufferMemory), "VkCmd.AllocateMemory(readback)");
        Check(VkCmd.BindBufferMemory(Device, buffer, bufferMemory, 0), "vkBindBufferMemory");

        var cmd = BeginCommands();

        // GL left the image in whatever layout it pleased; the external queue transfer is
        // what tells Vulkan to stop assuming otherwise.
        var barrier = new VkImageMemoryBarrier
        {
            sType = VkStructureType.ImageMemoryBarrier,
            srcAccessMask = VkAccessFlags.None,
            dstAccessMask = VkAccessFlags.TransferRead,
            oldLayout = VkImageLayout.General,
            newLayout = VkImageLayout.TransferSrcOptimal,
            srcQueueFamilyIndex = VK_QUEUE_FAMILY_EXTERNAL,
            dstQueueFamilyIndex = QueueFamily,
            image = Image,
            subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1),
        };
        VkCmd.CmdPipelineBarrier(cmd, VkPipelineStageFlags.TopOfPipe, VkPipelineStageFlags.Transfer,
            VkDependencyFlags.None, 0, 0, 0, 0, 1, &barrier);

        var region = new VkBufferImageCopy
        {
            bufferOffset = 0,
            bufferRowLength = 0,
            bufferImageHeight = 0,
            imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
            imageOffset = default,
            imageExtent = new VkExtent3D(Width, Height, 1),
        };
        VkCmd.CmdCopyImageToBuffer(cmd, Image, VkImageLayout.TransferSrcOptimal, buffer, 1, &region);

        EndCommands(cmd);

        void* mapped;
        Check(VkCmd.MapMemory(Device, bufferMemory, 0, bytes, 0, &mapped), "vkMapMemory");
        var pixels = new Half[Width * Height * 4];
        new Span<Half>(mapped, pixels.Length).CopyTo(pixels);
        VkCmd.UnmapMemory(Device, bufferMemory);

        VkCmd.DestroyBuffer(Device, buffer, 0);
        VkCmd.FreeMemory(Device, bufferMemory, 0);
        return pixels;
    }

    private VkCommandBuffer BeginCommands()
    {
        var info = new VkCommandBufferAllocateInfo
        {
            commandPool = _pool,
            level = VkCommandBufferLevel.Primary,
            commandBufferCount = 1,
        };
        VkCommandBuffer cmd;
        Check(VkCmd.AllocateCommandBuffers(Device, &info, &cmd), "vkAllocateCommandBuffers");

        var begin = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        Check(VkCmd.BeginCommandBuffer(cmd, &begin), "vkBeginCommandBuffer");
        return cmd;
    }

    private void EndCommands(VkCommandBuffer cmd)
    {
        Check(VkCmd.EndCommandBuffer(cmd), "vkEndCommandBuffer");
        var submit = new VkSubmitInfo { commandBufferCount = 1, pCommandBuffers = &cmd };
        Check(VkCmd.QueueSubmit(Queue, 1, &submit, VkFence.Null), "vkQueueSubmit");
        Check(VkCmd.QueueWaitIdle(Queue), "vkQueueWaitIdle");
        VkCmd.FreeCommandBuffers(Device, _pool, 1, &cmd);
    }

    private uint FindMemoryType(uint typeBits, VkMemoryPropertyFlags wanted)
    {
        var properties = new VkPhysicalDeviceMemoryProperties();
        VkCmd.GetMemoryProperties(PhysicalDevice, &properties);
        for (uint i = 0; i < properties.memoryTypeCount; i++)
        {
            bool allowed = (typeBits & (1u << (int)i)) != 0;
            if (allowed && (properties.memoryTypes[(int)i].propertyFlags & wanted) == wanted) return i;
        }
        throw new InvalidOperationException($"no memory type for {wanted}");
    }

    /// <summary>The device name out of the fixed byte buffer Vulkan reports it in.</summary>
    private static string ReadFixedName(VkPhysicalDeviceProperties* properties) =>
        System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)properties->deviceName) ?? "(unnamed)";

    private static void Check(VkResult result, string what)
    {
        if (result != VkResult.Success) throw new InvalidOperationException($"{what}: {result}");
    }

    public void Dispose()
    {
        if (Device.IsNull) return;
        if (!_pool.IsNull) VkCmd.DestroyCommandPool(Device, _pool, 0);
        if (!Image.IsNull) VkCmd.DestroyImage(Device, Image, 0);
        if (!Memory.IsNull) VkCmd.FreeMemory(Device, Memory, 0);
        VkCmd.DestroyDevice(Device, 0);
        if (!Instance.IsNull) VkCmd.DestroyInstance(Instance, 0);
    }
}

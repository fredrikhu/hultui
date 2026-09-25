using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace HdrInterop;

/// <summary>
/// The instance- and device-level Vulkan commands this spike calls, loaded by hand.
///
/// <para>Vortice.Vulkan 3.x generates the 2,179 structs and enums — which is the part
/// worth having, because getting a layout wrong here is a silent memory bug — but exposes
/// only the eight global entry points as methods. Everything past <c>vkCreateInstance</c>
/// is fetched through <c>vkGetInstanceProcAddr</c> / <c>vkGetDeviceProcAddr</c>, which is
/// how Vulkan expects it to work anyway and how <see cref="Gl"/> and the SDL layer in this
/// repo already do the same job.</para>
/// </summary>
internal static unsafe class VkCmd
{
    // ── instance level ───────────────────────────────────────────────────
    public static delegate* unmanaged<VkInstance, uint*, VkPhysicalDevice*, VkResult> EnumeratePhysicalDevices;
    public static delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceProperties*, void> GetPhysicalDeviceProperties;
    public static delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceProperties2*, void> GetPhysicalDeviceProperties2;
    public static delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceFeatures2*, void> GetPhysicalDeviceFeatures2;
    public static delegate* unmanaged<VkPhysicalDevice, uint*, VkQueueFamilyProperties*, void> GetQueueFamilyProperties;
    public static delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceMemoryProperties*, void> GetMemoryProperties;
    public static delegate* unmanaged<VkPhysicalDevice, VkDeviceCreateInfo*, nint, VkDevice*, VkResult> CreateDevice;
    public static delegate* unmanaged<VkInstance, nint, void> DestroyInstance;

    // ── device level ─────────────────────────────────────────────────────
    public static delegate* unmanaged<VkDevice, uint, uint, VkQueue*, void> GetDeviceQueue;
    public static delegate* unmanaged<VkDevice, VkImageCreateInfo*, nint, VkImage*, VkResult> CreateImage;
    public static delegate* unmanaged<VkDevice, VkImage, VkMemoryRequirements*, void> GetImageMemoryRequirements;
    public static delegate* unmanaged<VkDevice, VkMemoryAllocateInfo*, nint, VkDeviceMemory*, VkResult> AllocateMemory;
    public static delegate* unmanaged<VkDevice, VkImage, VkDeviceMemory, ulong, VkResult> BindImageMemory;
    public static delegate* unmanaged<VkDevice, VkMemoryGetWin32HandleInfoKHR*, nint*, VkResult> GetMemoryWin32Handle;
    public static delegate* unmanaged<VkDevice, VkMemoryGetFdInfoKHR*, int*, VkResult> GetMemoryFd;
    public static delegate* unmanaged<VkDevice, VkBufferCreateInfo*, nint, VkBuffer*, VkResult> CreateBuffer;
    public static delegate* unmanaged<VkDevice, VkBuffer, VkMemoryRequirements*, void> GetBufferMemoryRequirements;
    public static delegate* unmanaged<VkDevice, VkBuffer, VkDeviceMemory, ulong, VkResult> BindBufferMemory;
    public static delegate* unmanaged<VkDevice, VkCommandPoolCreateInfo*, nint, VkCommandPool*, VkResult> CreateCommandPool;
    public static delegate* unmanaged<VkDevice, VkCommandBufferAllocateInfo*, VkCommandBuffer*, VkResult> AllocateCommandBuffers;
    public static delegate* unmanaged<VkCommandBuffer, VkCommandBufferBeginInfo*, VkResult> BeginCommandBuffer;
    public static delegate* unmanaged<VkCommandBuffer, VkPipelineStageFlags, VkPipelineStageFlags, VkDependencyFlags,
        uint, nint, uint, nint, uint, VkImageMemoryBarrier*, void> CmdPipelineBarrier;
    public static delegate* unmanaged<VkCommandBuffer, VkImage, VkImageLayout, VkBuffer, uint, VkBufferImageCopy*, void> CmdCopyImageToBuffer;
    public static delegate* unmanaged<VkCommandBuffer, VkResult> EndCommandBuffer;
    public static delegate* unmanaged<VkQueue, uint, VkSubmitInfo*, VkFence, VkResult> QueueSubmit;
    public static delegate* unmanaged<VkQueue, VkResult> QueueWaitIdle;
    public static delegate* unmanaged<VkDevice, VkCommandPool, uint, VkCommandBuffer*, void> FreeCommandBuffers;
    public static delegate* unmanaged<VkDevice, VkDeviceMemory, ulong, ulong, uint, void**, VkResult> MapMemory;
    public static delegate* unmanaged<VkDevice, VkDeviceMemory, void> UnmapMemory;
    public static delegate* unmanaged<VkDevice, VkBuffer, nint, void> DestroyBuffer;
    public static delegate* unmanaged<VkDevice, VkDeviceMemory, nint, void> FreeMemory;
    public static delegate* unmanaged<VkDevice, VkImage, nint, void> DestroyImage;
    public static delegate* unmanaged<VkDevice, VkCommandPool, nint, void> DestroyCommandPool;
    public static delegate* unmanaged<VkDevice, nint, void> DestroyDevice;

    private static delegate* unmanaged<VkDevice, byte*, nint> _getDeviceProcAddr;

    /// <summary>For anything that resolves its own entry points — Skia does.</summary>
    public static nint GetDeviceProcAddr(VkDevice device, byte* name) =>
        _getDeviceProcAddr is null ? 0 : _getDeviceProcAddr(device, name);

    public static void LoadInstance(VkInstance instance)
    {
        EnumeratePhysicalDevices = (delegate* unmanaged<VkInstance, uint*, VkPhysicalDevice*, VkResult>)I(instance, "vkEnumeratePhysicalDevices");
        GetPhysicalDeviceProperties = (delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceProperties*, void>)I(instance, "vkGetPhysicalDeviceProperties");
        GetPhysicalDeviceProperties2 = (delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceProperties2*, void>)I(instance, "vkGetPhysicalDeviceProperties2");
        GetPhysicalDeviceFeatures2 = (delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceFeatures2*, void>)I(instance, "vkGetPhysicalDeviceFeatures2");
        GetQueueFamilyProperties = (delegate* unmanaged<VkPhysicalDevice, uint*, VkQueueFamilyProperties*, void>)I(instance, "vkGetPhysicalDeviceQueueFamilyProperties");
        GetMemoryProperties = (delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceMemoryProperties*, void>)I(instance, "vkGetPhysicalDeviceMemoryProperties");
        CreateDevice = (delegate* unmanaged<VkPhysicalDevice, VkDeviceCreateInfo*, nint, VkDevice*, VkResult>)I(instance, "vkCreateDevice");
        DestroyInstance = (delegate* unmanaged<VkInstance, nint, void>)I(instance, "vkDestroyInstance");
        _getDeviceProcAddr = (delegate* unmanaged<VkDevice, byte*, nint>)I(instance, "vkGetDeviceProcAddr");
    }

    public static void LoadDevice(VkDevice device)
    {
        GetDeviceQueue = (delegate* unmanaged<VkDevice, uint, uint, VkQueue*, void>)D(device, "vkGetDeviceQueue");
        CreateImage = (delegate* unmanaged<VkDevice, VkImageCreateInfo*, nint, VkImage*, VkResult>)D(device, "vkCreateImage");
        GetImageMemoryRequirements = (delegate* unmanaged<VkDevice, VkImage, VkMemoryRequirements*, void>)D(device, "vkGetImageMemoryRequirements");
        AllocateMemory = (delegate* unmanaged<VkDevice, VkMemoryAllocateInfo*, nint, VkDeviceMemory*, VkResult>)D(device, "vkAllocateMemory");
        BindImageMemory = (delegate* unmanaged<VkDevice, VkImage, VkDeviceMemory, ulong, VkResult>)D(device, "vkBindImageMemory");
        GetMemoryWin32Handle = (delegate* unmanaged<VkDevice, VkMemoryGetWin32HandleInfoKHR*, nint*, VkResult>)D(device, "vkGetMemoryWin32HandleKHR", optional: true);
        GetMemoryFd = (delegate* unmanaged<VkDevice, VkMemoryGetFdInfoKHR*, int*, VkResult>)D(device, "vkGetMemoryFdKHR", optional: true);
        CreateBuffer = (delegate* unmanaged<VkDevice, VkBufferCreateInfo*, nint, VkBuffer*, VkResult>)D(device, "vkCreateBuffer");
        GetBufferMemoryRequirements = (delegate* unmanaged<VkDevice, VkBuffer, VkMemoryRequirements*, void>)D(device, "vkGetBufferMemoryRequirements");
        BindBufferMemory = (delegate* unmanaged<VkDevice, VkBuffer, VkDeviceMemory, ulong, VkResult>)D(device, "vkBindBufferMemory");
        CreateCommandPool = (delegate* unmanaged<VkDevice, VkCommandPoolCreateInfo*, nint, VkCommandPool*, VkResult>)D(device, "vkCreateCommandPool");
        AllocateCommandBuffers = (delegate* unmanaged<VkDevice, VkCommandBufferAllocateInfo*, VkCommandBuffer*, VkResult>)D(device, "vkAllocateCommandBuffers");
        BeginCommandBuffer = (delegate* unmanaged<VkCommandBuffer, VkCommandBufferBeginInfo*, VkResult>)D(device, "vkBeginCommandBuffer");
        CmdPipelineBarrier = (delegate* unmanaged<VkCommandBuffer, VkPipelineStageFlags, VkPipelineStageFlags, VkDependencyFlags, uint, nint, uint, nint, uint, VkImageMemoryBarrier*, void>)D(device, "vkCmdPipelineBarrier");
        CmdCopyImageToBuffer = (delegate* unmanaged<VkCommandBuffer, VkImage, VkImageLayout, VkBuffer, uint, VkBufferImageCopy*, void>)D(device, "vkCmdCopyImageToBuffer");
        EndCommandBuffer = (delegate* unmanaged<VkCommandBuffer, VkResult>)D(device, "vkEndCommandBuffer");
        QueueSubmit = (delegate* unmanaged<VkQueue, uint, VkSubmitInfo*, VkFence, VkResult>)D(device, "vkQueueSubmit");
        QueueWaitIdle = (delegate* unmanaged<VkQueue, VkResult>)D(device, "vkQueueWaitIdle");
        FreeCommandBuffers = (delegate* unmanaged<VkDevice, VkCommandPool, uint, VkCommandBuffer*, void>)D(device, "vkFreeCommandBuffers");
        MapMemory = (delegate* unmanaged<VkDevice, VkDeviceMemory, ulong, ulong, uint, void**, VkResult>)D(device, "vkMapMemory");
        UnmapMemory = (delegate* unmanaged<VkDevice, VkDeviceMemory, void>)D(device, "vkUnmapMemory");
        DestroyBuffer = (delegate* unmanaged<VkDevice, VkBuffer, nint, void>)D(device, "vkDestroyBuffer");
        FreeMemory = (delegate* unmanaged<VkDevice, VkDeviceMemory, nint, void>)D(device, "vkFreeMemory");
        DestroyImage = (delegate* unmanaged<VkDevice, VkImage, nint, void>)D(device, "vkDestroyImage");
        DestroyCommandPool = (delegate* unmanaged<VkDevice, VkCommandPool, nint, void>)D(device, "vkDestroyCommandPool");
        DestroyDevice = (delegate* unmanaged<VkDevice, nint, void>)D(device, "vkDestroyDevice");
    }

    private static nint I(VkInstance instance, string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name + ' ');
        nint address = (nint)vkGetInstanceProcAddr(instance, bytes).Value;
        if (address == 0) throw new InvalidOperationException($"Vulkan instance command missing: {name}");
        return address;
    }

    private static nint D(VkDevice device, string name, bool optional = false)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name + ' ');
        fixed (byte* p = bytes)
        {
            nint address = _getDeviceProcAddr(device, p);
            if (address == 0 && !optional)
                throw new InvalidOperationException($"Vulkan device command missing: {name}");
            return address;
        }
    }
}

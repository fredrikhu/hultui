using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace HultUI.Platform.SDL3;

/// <summary>
/// The Vulkan commands the swapchain needs, loaded by hand.
///
/// <para>Vortice.Vulkan generates the structs and enums — the part worth having, since a
/// wrong layout here is a silent memory bug — but exposes only the global bootstrap calls
/// as methods. Everything past <c>vkCreateInstance</c> comes from
/// <c>vkGetInstanceProcAddr</c> / <c>vkGetDeviceProcAddr</c>, which is how Vulkan intends it
/// and how <c>SDL3Window</c> already resolves GL.</para>
/// </summary>
internal static unsafe class VulkanCommands
{
    // ── instance ─────────────────────────────────────────────────────────
    public static delegate* unmanaged<VkInstance, uint*, VkPhysicalDevice*, VkResult> EnumeratePhysicalDevices;
    public static delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceProperties*, void> GetPhysicalDeviceProperties;
    public static delegate* unmanaged<VkPhysicalDevice, uint*, VkQueueFamilyProperties*, void> GetQueueFamilyProperties;
    public static delegate* unmanaged<VkPhysicalDevice, uint, VkSurfaceKHR, uint*, VkResult> GetSurfaceSupport;
    public static delegate* unmanaged<VkPhysicalDevice, VkSurfaceKHR, VkSurfaceCapabilitiesKHR*, VkResult> GetSurfaceCapabilities;
    public static delegate* unmanaged<VkPhysicalDevice, VkSurfaceKHR, uint*, VkSurfaceFormatKHR*, VkResult> GetSurfaceFormats;
    public static delegate* unmanaged<VkPhysicalDevice, VkSurfaceKHR, uint*, VkPresentModeKHR*, VkResult> GetSurfacePresentModes;
    public static delegate* unmanaged<VkPhysicalDevice, VkDeviceCreateInfo*, nint, VkDevice*, VkResult> CreateDevice;
    public static delegate* unmanaged<VkInstance, VkSurfaceKHR, nint, void> DestroySurface;
    public static delegate* unmanaged<VkInstance, nint, void> DestroyInstance;
    public static delegate* unmanaged<VkPhysicalDevice, byte*, uint*, VkExtensionProperties*, VkResult> EnumerateDeviceExtensionProperties;
    public static delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceFeatures2*, void> GetPhysicalDeviceFeatures2;

    // ── device ───────────────────────────────────────────────────────────
    public static delegate* unmanaged<VkDevice, uint, uint, VkQueue*, void> GetDeviceQueue;
    public static delegate* unmanaged<VkDevice, VkSwapchainCreateInfoKHR*, nint, VkSwapchainKHR*, VkResult> CreateSwapchain;
    public static delegate* unmanaged<VkDevice, VkSwapchainKHR, uint*, VkImage*, VkResult> GetSwapchainImages;
    public static delegate* unmanaged<VkDevice, VkSwapchainKHR, nint, void> DestroySwapchain;
    public static delegate* unmanaged<VkDevice, VkSwapchainKHR, ulong, VkSemaphore, VkFence, uint*, VkResult> AcquireNextImage;
    public static delegate* unmanaged<VkQueue, VkPresentInfoKHR*, VkResult> QueuePresent;
    public static delegate* unmanaged<VkDevice, VkFenceCreateInfo*, nint, VkFence*, VkResult> CreateFence;
    public static delegate* unmanaged<VkDevice, uint, VkFence*, uint, ulong, VkResult> WaitForFences;
    public static delegate* unmanaged<VkDevice, uint, VkFence*, VkResult> ResetFences;
    public static delegate* unmanaged<VkDevice, VkFence, nint, void> DestroyFence;
    public static delegate* unmanaged<VkDevice, VkSemaphoreCreateInfo*, nint, VkSemaphore*, VkResult> CreateSemaphore;
    public static delegate* unmanaged<VkDevice, VkSemaphore, nint, void> DestroySemaphore;
    public static delegate* unmanaged<VkDevice, VkCommandPoolCreateInfo*, nint, VkCommandPool*, VkResult> CreateCommandPool;
    public static delegate* unmanaged<VkDevice, VkCommandBufferAllocateInfo*, VkCommandBuffer*, VkResult> AllocateCommandBuffers;
    public static delegate* unmanaged<VkCommandBuffer, VkCommandBufferBeginInfo*, VkResult> BeginCommandBuffer;
    public static delegate* unmanaged<VkCommandBuffer, VkPipelineStageFlags, VkPipelineStageFlags, VkDependencyFlags,
        uint, nint, uint, nint, uint, VkImageMemoryBarrier*, void> CmdPipelineBarrier;
    public static delegate* unmanaged<VkCommandBuffer, VkResult> EndCommandBuffer;
    public static delegate* unmanaged<VkQueue, uint, VkSubmitInfo*, VkFence, VkResult> QueueSubmit;
    public static delegate* unmanaged<VkQueue, VkResult> QueueWaitIdle;
    public static delegate* unmanaged<VkDevice, VkResult> DeviceWaitIdle;
    public static delegate* unmanaged<VkDevice, VkCommandPool, uint, VkCommandBuffer*, void> FreeCommandBuffers;
    public static delegate* unmanaged<VkDevice, VkCommandPool, nint, void> DestroyCommandPool;
    public static delegate* unmanaged<VkDevice, nint, void> DestroyDevice;
    public static delegate* unmanaged<VkDevice, VkBufferCreateInfo*, nint, VkBuffer*, VkResult> CreateBuffer;
    public static delegate* unmanaged<VkDevice, VkBuffer, VkMemoryRequirements*, void> GetBufferMemoryRequirements;
    public static delegate* unmanaged<VkDevice, VkBuffer, VkDeviceMemory, ulong, VkResult> BindBufferMemory;
    public static delegate* unmanaged<VkDevice, VkMemoryAllocateInfo*, nint, VkDeviceMemory*, VkResult> AllocateMemory;
    public static delegate* unmanaged<VkCommandBuffer, VkImage, VkImageLayout, VkBuffer, uint, VkBufferImageCopy*, void> CmdCopyImageToBuffer;
    public static delegate* unmanaged<VkDevice, VkDeviceMemory, ulong, ulong, uint, void**, VkResult> MapMemory;
    public static delegate* unmanaged<VkDevice, VkDeviceMemory, void> UnmapMemory;
    public static delegate* unmanaged<VkDevice, VkBuffer, nint, void> DestroyBuffer;
    public static delegate* unmanaged<VkDevice, VkDeviceMemory, nint, void> FreeMemory;
    public static delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceMemoryProperties*, void> GetMemoryProperties;

    private static VkInstance _instance;
    private static delegate* unmanaged<VkDevice, byte*, nint> _getDeviceProcAddr;

    public static void LoadInstance(VkInstance instance)
    {
        _instance = instance;
        EnumeratePhysicalDevices = (delegate* unmanaged<VkInstance, uint*, VkPhysicalDevice*, VkResult>)I("vkEnumeratePhysicalDevices");
        GetPhysicalDeviceProperties = (delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceProperties*, void>)I("vkGetPhysicalDeviceProperties");
        GetQueueFamilyProperties = (delegate* unmanaged<VkPhysicalDevice, uint*, VkQueueFamilyProperties*, void>)I("vkGetPhysicalDeviceQueueFamilyProperties");
        GetSurfaceSupport = (delegate* unmanaged<VkPhysicalDevice, uint, VkSurfaceKHR, uint*, VkResult>)I("vkGetPhysicalDeviceSurfaceSupportKHR");
        GetSurfaceCapabilities = (delegate* unmanaged<VkPhysicalDevice, VkSurfaceKHR, VkSurfaceCapabilitiesKHR*, VkResult>)I("vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
        GetSurfaceFormats = (delegate* unmanaged<VkPhysicalDevice, VkSurfaceKHR, uint*, VkSurfaceFormatKHR*, VkResult>)I("vkGetPhysicalDeviceSurfaceFormatsKHR");
        GetSurfacePresentModes = (delegate* unmanaged<VkPhysicalDevice, VkSurfaceKHR, uint*, VkPresentModeKHR*, VkResult>)I("vkGetPhysicalDeviceSurfacePresentModesKHR");
        CreateDevice = (delegate* unmanaged<VkPhysicalDevice, VkDeviceCreateInfo*, nint, VkDevice*, VkResult>)I("vkCreateDevice");
        DestroySurface = (delegate* unmanaged<VkInstance, VkSurfaceKHR, nint, void>)I("vkDestroySurfaceKHR");
        DestroyInstance = (delegate* unmanaged<VkInstance, nint, void>)I("vkDestroyInstance");
        EnumerateDeviceExtensionProperties = (delegate* unmanaged<VkPhysicalDevice, byte*, uint*, VkExtensionProperties*, VkResult>)I("vkEnumerateDeviceExtensionProperties");
        GetMemoryProperties = (delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceMemoryProperties*, void>)I("vkGetPhysicalDeviceMemoryProperties");
        GetPhysicalDeviceFeatures2 = (delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceFeatures2*, void>)I("vkGetPhysicalDeviceFeatures2");
        _getDeviceProcAddr = (delegate* unmanaged<VkDevice, byte*, nint>)I("vkGetDeviceProcAddr");
    }

    public static void LoadDevice(VkDevice device)
    {
        GetDeviceQueue = (delegate* unmanaged<VkDevice, uint, uint, VkQueue*, void>)D(device, "vkGetDeviceQueue");
        CreateSwapchain = (delegate* unmanaged<VkDevice, VkSwapchainCreateInfoKHR*, nint, VkSwapchainKHR*, VkResult>)D(device, "vkCreateSwapchainKHR");
        GetSwapchainImages = (delegate* unmanaged<VkDevice, VkSwapchainKHR, uint*, VkImage*, VkResult>)D(device, "vkGetSwapchainImagesKHR");
        DestroySwapchain = (delegate* unmanaged<VkDevice, VkSwapchainKHR, nint, void>)D(device, "vkDestroySwapchainKHR");
        AcquireNextImage = (delegate* unmanaged<VkDevice, VkSwapchainKHR, ulong, VkSemaphore, VkFence, uint*, VkResult>)D(device, "vkAcquireNextImageKHR");
        QueuePresent = (delegate* unmanaged<VkQueue, VkPresentInfoKHR*, VkResult>)D(device, "vkQueuePresentKHR");
        CreateFence = (delegate* unmanaged<VkDevice, VkFenceCreateInfo*, nint, VkFence*, VkResult>)D(device, "vkCreateFence");
        WaitForFences = (delegate* unmanaged<VkDevice, uint, VkFence*, uint, ulong, VkResult>)D(device, "vkWaitForFences");
        ResetFences = (delegate* unmanaged<VkDevice, uint, VkFence*, VkResult>)D(device, "vkResetFences");
        DestroyFence = (delegate* unmanaged<VkDevice, VkFence, nint, void>)D(device, "vkDestroyFence");
        CreateSemaphore = (delegate* unmanaged<VkDevice, VkSemaphoreCreateInfo*, nint, VkSemaphore*, VkResult>)D(device, "vkCreateSemaphore");
        DestroySemaphore = (delegate* unmanaged<VkDevice, VkSemaphore, nint, void>)D(device, "vkDestroySemaphore");
        CreateCommandPool = (delegate* unmanaged<VkDevice, VkCommandPoolCreateInfo*, nint, VkCommandPool*, VkResult>)D(device, "vkCreateCommandPool");
        AllocateCommandBuffers = (delegate* unmanaged<VkDevice, VkCommandBufferAllocateInfo*, VkCommandBuffer*, VkResult>)D(device, "vkAllocateCommandBuffers");
        BeginCommandBuffer = (delegate* unmanaged<VkCommandBuffer, VkCommandBufferBeginInfo*, VkResult>)D(device, "vkBeginCommandBuffer");
        CmdPipelineBarrier = (delegate* unmanaged<VkCommandBuffer, VkPipelineStageFlags, VkPipelineStageFlags, VkDependencyFlags, uint, nint, uint, nint, uint, VkImageMemoryBarrier*, void>)D(device, "vkCmdPipelineBarrier");
        EndCommandBuffer = (delegate* unmanaged<VkCommandBuffer, VkResult>)D(device, "vkEndCommandBuffer");
        QueueSubmit = (delegate* unmanaged<VkQueue, uint, VkSubmitInfo*, VkFence, VkResult>)D(device, "vkQueueSubmit");
        QueueWaitIdle = (delegate* unmanaged<VkQueue, VkResult>)D(device, "vkQueueWaitIdle");
        DeviceWaitIdle = (delegate* unmanaged<VkDevice, VkResult>)D(device, "vkDeviceWaitIdle");
        FreeCommandBuffers = (delegate* unmanaged<VkDevice, VkCommandPool, uint, VkCommandBuffer*, void>)D(device, "vkFreeCommandBuffers");
        DestroyCommandPool = (delegate* unmanaged<VkDevice, VkCommandPool, nint, void>)D(device, "vkDestroyCommandPool");
        DestroyDevice = (delegate* unmanaged<VkDevice, nint, void>)D(device, "vkDestroyDevice");
        CreateBuffer = (delegate* unmanaged<VkDevice, VkBufferCreateInfo*, nint, VkBuffer*, VkResult>)D(device, "vkCreateBuffer");
        GetBufferMemoryRequirements = (delegate* unmanaged<VkDevice, VkBuffer, VkMemoryRequirements*, void>)D(device, "vkGetBufferMemoryRequirements");
        BindBufferMemory = (delegate* unmanaged<VkDevice, VkBuffer, VkDeviceMemory, ulong, VkResult>)D(device, "vkBindBufferMemory");
        AllocateMemory = (delegate* unmanaged<VkDevice, VkMemoryAllocateInfo*, nint, VkDeviceMemory*, VkResult>)D(device, "vkAllocateMemory");
        CmdCopyImageToBuffer = (delegate* unmanaged<VkCommandBuffer, VkImage, VkImageLayout, VkBuffer, uint, VkBufferImageCopy*, void>)D(device, "vkCmdCopyImageToBuffer");
        MapMemory = (delegate* unmanaged<VkDevice, VkDeviceMemory, ulong, ulong, uint, void**, VkResult>)D(device, "vkMapMemory");
        UnmapMemory = (delegate* unmanaged<VkDevice, VkDeviceMemory, void>)D(device, "vkUnmapMemory");
        DestroyBuffer = (delegate* unmanaged<VkDevice, VkBuffer, nint, void>)D(device, "vkDestroyBuffer");
        FreeMemory = (delegate* unmanaged<VkDevice, VkDeviceMemory, nint, void>)D(device, "vkFreeMemory");
    }

    /// <summary>Resolves by name, for anything that loads its own entry points — Skia does,
    /// through <c>GRVkBackendContext.GetProcedureAddress</c>.</summary>
    public static nint Resolve(string name, nint instance, nint device)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name + '\0');
        if (device != 0 && _getDeviceProcAddr is not null)
        {
            fixed (byte* p = bytes)
            {
                var address = _getDeviceProcAddr(new VkDevice(device), p);
                if (address != 0) return address;
            }
        }
        return (nint)vkGetInstanceProcAddr(new VkInstance(instance == 0 ? _instance.Handle : instance), bytes).Value;
    }

    private static nint I(string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name + '\0');
        nint address = (nint)vkGetInstanceProcAddr(_instance, bytes).Value;
        if (address == 0) throw new InvalidOperationException($"Vulkan instance command missing: {name}");
        return address;
    }

    private static nint D(VkDevice device, string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name + '\0');
        fixed (byte* p = bytes)
        {
            nint address = _getDeviceProcAddr(device, p);
            if (address == 0) throw new InvalidOperationException($"Vulkan device command missing: {name}");
            return address;
        }
    }
}

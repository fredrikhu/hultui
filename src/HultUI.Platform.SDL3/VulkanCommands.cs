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
///
/// <para><b>One per host, not one per process.</b> Every window builds its own instance and
/// device, and the spec allows a command resolved for a device to be called only with that
/// device or its children. This used to be a static table that each new window overwrote, so
/// the first window went on calling the second window's device pointers. That worked because
/// both windows load the same driver, and would have broken the day two windows picked
/// different GPUs.</para>
/// </summary>
internal sealed unsafe class VulkanCommands
{
    // ── instance ─────────────────────────────────────────────────────────
    public delegate* unmanaged<VkInstance, uint*, VkPhysicalDevice*, VkResult> EnumeratePhysicalDevices;
    public delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceProperties*, void> GetPhysicalDeviceProperties;
    public delegate* unmanaged<VkPhysicalDevice, uint*, VkQueueFamilyProperties*, void> GetQueueFamilyProperties;
    public delegate* unmanaged<VkPhysicalDevice, uint, VkSurfaceKHR, uint*, VkResult> GetSurfaceSupport;
    public delegate* unmanaged<VkPhysicalDevice, VkSurfaceKHR, VkSurfaceCapabilitiesKHR*, VkResult> GetSurfaceCapabilities;
    public delegate* unmanaged<VkPhysicalDevice, VkSurfaceKHR, uint*, VkSurfaceFormatKHR*, VkResult> GetSurfaceFormats;
    public delegate* unmanaged<VkPhysicalDevice, VkSurfaceKHR, uint*, VkPresentModeKHR*, VkResult> GetSurfacePresentModes;
    public delegate* unmanaged<VkPhysicalDevice, VkDeviceCreateInfo*, nint, VkDevice*, VkResult> CreateDevice;
    public delegate* unmanaged<VkInstance, VkSurfaceKHR, nint, void> DestroySurface;
    public delegate* unmanaged<VkInstance, nint, void> DestroyInstance;
    public delegate* unmanaged<VkPhysicalDevice, byte*, uint*, VkExtensionProperties*, VkResult> EnumerateDeviceExtensionProperties;
    public delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceFeatures2*, void> GetPhysicalDeviceFeatures2;
    public delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceProperties2*, void> GetPhysicalDeviceProperties2;

    /// <summary>
    /// The chained form of the surface capability query, from
    /// <c>VK_KHR_get_surface_capabilities2</c>. Needed because the present-scaling capabilities
    /// of <c>VK_KHR_surface_maintenance1</c> arrive only in its <c>pNext</c>, and they are asked
    /// <i>per present mode</i> — which the one-argument query has nowhere to say.
    ///
    /// <para>Null when the instance lacks the extension; every caller checks.</para>
    /// </summary>
    public delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceSurfaceInfo2KHR*, VkSurfaceCapabilities2KHR*, VkResult> GetSurfaceCapabilities2;

    // ── device ───────────────────────────────────────────────────────────
    public delegate* unmanaged<VkDevice, uint, uint, VkQueue*, void> GetDeviceQueue;
    public delegate* unmanaged<VkDevice, VkSwapchainCreateInfoKHR*, nint, VkSwapchainKHR*, VkResult> CreateSwapchain;
    public delegate* unmanaged<VkDevice, VkSwapchainKHR, uint*, VkImage*, VkResult> GetSwapchainImages;
    public delegate* unmanaged<VkDevice, VkSwapchainKHR, nint, void> DestroySwapchain;
    public delegate* unmanaged<VkDevice, VkSwapchainKHR, ulong, VkSemaphore, VkFence, uint*, VkResult> AcquireNextImage;
    public delegate* unmanaged<VkQueue, VkPresentInfoKHR*, VkResult> QueuePresent;
    public delegate* unmanaged<VkDevice, VkFenceCreateInfo*, nint, VkFence*, VkResult> CreateFence;
    public delegate* unmanaged<VkDevice, uint, VkFence*, uint, ulong, VkResult> WaitForFences;
    public delegate* unmanaged<VkDevice, uint, VkFence*, VkResult> ResetFences;
    public delegate* unmanaged<VkDevice, VkFence, nint, void> DestroyFence;
    public delegate* unmanaged<VkDevice, VkSemaphoreCreateInfo*, nint, VkSemaphore*, VkResult> CreateSemaphore;
    public delegate* unmanaged<VkDevice, VkSemaphore, nint, void> DestroySemaphore;
    public delegate* unmanaged<VkDevice, VkCommandPoolCreateInfo*, nint, VkCommandPool*, VkResult> CreateCommandPool;
    public delegate* unmanaged<VkDevice, VkCommandBufferAllocateInfo*, VkCommandBuffer*, VkResult> AllocateCommandBuffers;
    public delegate* unmanaged<VkCommandBuffer, VkCommandBufferBeginInfo*, VkResult> BeginCommandBuffer;
    public delegate* unmanaged<VkCommandBuffer, VkPipelineStageFlags, VkPipelineStageFlags, VkDependencyFlags,
        uint, nint, uint, nint, uint, VkImageMemoryBarrier*, void> CmdPipelineBarrier;
    public delegate* unmanaged<VkCommandBuffer, VkResult> EndCommandBuffer;
    public delegate* unmanaged<VkQueue, uint, VkSubmitInfo*, VkFence, VkResult> QueueSubmit;
    public delegate* unmanaged<VkQueue, VkResult> QueueWaitIdle;
    public delegate* unmanaged<VkDevice, VkResult> DeviceWaitIdle;
    public delegate* unmanaged<VkDevice, VkCommandPool, uint, VkCommandBuffer*, void> FreeCommandBuffers;
    public delegate* unmanaged<VkDevice, VkCommandPool, nint, void> DestroyCommandPool;
    public delegate* unmanaged<VkDevice, nint, void> DestroyDevice;
    public delegate* unmanaged<VkDevice, VkBufferCreateInfo*, nint, VkBuffer*, VkResult> CreateBuffer;
    public delegate* unmanaged<VkDevice, VkBuffer, VkMemoryRequirements*, void> GetBufferMemoryRequirements;
    public delegate* unmanaged<VkDevice, VkBuffer, VkDeviceMemory, ulong, VkResult> BindBufferMemory;
    public delegate* unmanaged<VkDevice, VkMemoryAllocateInfo*, nint, VkDeviceMemory*, VkResult> AllocateMemory;
    public delegate* unmanaged<VkCommandBuffer, VkImage, VkImageLayout, VkBuffer, uint, VkBufferImageCopy*, void> CmdCopyImageToBuffer;
    public delegate* unmanaged<VkDevice, VkDeviceMemory, ulong, ulong, uint, void**, VkResult> MapMemory;
    public delegate* unmanaged<VkDevice, VkDeviceMemory, void> UnmapMemory;
    public delegate* unmanaged<VkDevice, VkBuffer, nint, void> DestroyBuffer;
    public delegate* unmanaged<VkDevice, VkDeviceMemory, nint, void> FreeMemory;
    public delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceMemoryProperties*, void> GetMemoryProperties;

    // Phase 4: an image of our own to share with GL, and the handle that shares it. The
    // export commands come from an extension that may not be there, so they are resolved
    // optionally and are null when the device went without it.
    public delegate* unmanaged<VkDevice, VkImageCreateInfo*, nint, VkImage*, VkResult> CreateImage;
    public delegate* unmanaged<VkDevice, VkImage, VkMemoryRequirements*, void> GetImageMemoryRequirements;
    public delegate* unmanaged<VkDevice, VkImage, VkDeviceMemory, ulong, VkResult> BindImageMemory;
    public delegate* unmanaged<VkDevice, VkImage, nint, void> DestroyImage;
    public delegate* unmanaged<VkDevice, VkMemoryGetWin32HandleInfoKHR*, nint*, VkResult> GetMemoryWin32Handle;
    public delegate* unmanaged<VkDevice, VkMemoryGetFdInfoKHR*, int*, VkResult> GetMemoryFd;
    public delegate* unmanaged<VkDevice, VkSemaphoreGetWin32HandleInfoKHR*, nint*, VkResult> GetSemaphoreWin32Handle;
    public delegate* unmanaged<VkDevice, VkSemaphoreGetFdInfoKHR*, int*, VkResult> GetSemaphoreFd;

    private VkInstance _instance;
    private delegate* unmanaged<VkDevice, byte*, nint> _getDeviceProcAddr;

    public void LoadInstance(VkInstance instance)
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
        GetPhysicalDeviceProperties2 = (delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceProperties2*, void>)I("vkGetPhysicalDeviceProperties2");
        // Resolved optionally because the extension is enabled only beside surface_maintenance1.
        // The spec lets vkGetInstanceProcAddr answer null for a command whose instance extension
        // was not enabled, and through I() that null would stop the Vulkan renderer starting at
        // all. The Khronos loader 1.4.357 on Windows happens to hand back a pointer anyway
        // (checked with the pair deliberately left out), which is why this never threw here;
        // a stricter loader need not. Callers use it only when the extension is enabled.
        GetSurfaceCapabilities2 = (delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceSurfaceInfo2KHR*, VkSurfaceCapabilities2KHR*, VkResult>)OptionalInstance("vkGetPhysicalDeviceSurfaceCapabilities2KHR");
        _getDeviceProcAddr = (delegate* unmanaged<VkDevice, byte*, nint>)I("vkGetDeviceProcAddr");
    }

    public void LoadDevice(VkDevice device)
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
        CreateImage = (delegate* unmanaged<VkDevice, VkImageCreateInfo*, nint, VkImage*, VkResult>)D(device, "vkCreateImage");
        GetImageMemoryRequirements = (delegate* unmanaged<VkDevice, VkImage, VkMemoryRequirements*, void>)D(device, "vkGetImageMemoryRequirements");
        BindImageMemory = (delegate* unmanaged<VkDevice, VkImage, VkDeviceMemory, ulong, VkResult>)D(device, "vkBindImageMemory");
        DestroyImage = (delegate* unmanaged<VkDevice, VkImage, nint, void>)D(device, "vkDestroyImage");
        GetMemoryWin32Handle = (delegate* unmanaged<VkDevice, VkMemoryGetWin32HandleInfoKHR*, nint*, VkResult>)Optional(device, "vkGetMemoryWin32HandleKHR");
        GetMemoryFd = (delegate* unmanaged<VkDevice, VkMemoryGetFdInfoKHR*, int*, VkResult>)Optional(device, "vkGetMemoryFdKHR");
        GetSemaphoreWin32Handle = (delegate* unmanaged<VkDevice, VkSemaphoreGetWin32HandleInfoKHR*, nint*, VkResult>)Optional(device, "vkGetSemaphoreWin32HandleKHR");
        GetSemaphoreFd = (delegate* unmanaged<VkDevice, VkSemaphoreGetFdInfoKHR*, int*, VkResult>)Optional(device, "vkGetSemaphoreFdKHR");
    }

    /// <summary>Resolves by name, for anything that loads its own entry points — Skia does,
    /// through <c>GRVkBackendContext.GetProcedureAddress</c>.</summary>
    public nint Resolve(string name, nint instance, nint device)
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

    /// <summary>A core instance command, which every implementation has; missing means the
    /// loader itself is broken, and saying so beats a null call later.</summary>
    private nint I(string name)
    {
        nint address = OptionalInstance(name);
        if (address == 0) throw new InvalidOperationException($"Vulkan instance command missing: {name}");
        return address;
    }

    /// <summary>Like <see cref="I"/> but returns zero rather than throwing, for a command that
    /// belongs to an instance extension the instance may not have enabled.</summary>
    private nint OptionalInstance(string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name + '\0');
        return (nint)vkGetInstanceProcAddr(_instance, bytes).Value;
    }

    /// <summary>Like <see cref="D"/> but returns zero rather than throwing, for a command
    /// that belongs to an extension the device may not have enabled.</summary>
    private nint Optional(VkDevice device, string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name + '\0');
        fixed (byte* p = bytes) return _getDeviceProcAddr(device, p);
    }

    private nint D(VkDevice device, string name)
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

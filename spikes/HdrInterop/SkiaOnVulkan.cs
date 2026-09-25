using SkiaSharp;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace HdrInterop;

/// <summary>
/// Skia drawing into the same Vulkan image, through <c>GRContext.CreateVulkan</c>.
///
/// <para>The step between phase 0 and phase 1 of docs/design/HDR.md. Phase 0 proved the
/// image can be shared with OpenGL; this proves SkiaSharp will accept a device we built
/// ourselves and draw into that image — which is the next thing that could quietly not
/// work, and is far cheaper to find out here than inside a swapchain.</para>
///
/// <para>Deliberately offscreen. A swapchain is acquire/submit/present choreography that
/// belongs to the platform layer, and none of it is needed to answer this question.</para>
/// </summary>
internal static unsafe class SkiaOnVulkan
{
    // VkFormat.R16G16B16A16Sfloat, VK_IMAGE_TILING_OPTIMAL, and the usage the image was made with.
    private const uint FormatRgba16F = 97;
    private const uint TilingOptimal = 0;
    private const uint LayoutUndefined = 0;
    private const uint UsageColourSampledTransfer = 0x01 | 0x02 | 0x04 | 0x10;

    /// <param name="withExtensions">Tell Skia which extensions and device features are
    /// enabled, as the real renderer does. Off is what phase 0 did.</param>
    /// <param name="zeroAlloc">Describe the memory as a swapchain image does — all zeroes —
    /// rather than pointing at the allocation we made.</param>
    /// <param name="instanceExtensions">What to tell Skia the instance has, when that is
    /// not what this spike's own instance has. The renderer's list comes from SDL and is
    /// shorter, which is the thing being tested.</param>
    public static (bool Ok, string Message) Draw(VulkanImage vk,
        bool withExtensions = false, bool zeroAlloc = false,
        string[]? instanceExtensions = null, string[]? deviceExtensions = null,
        bool features2 = true)
    {
        // Skia asks for entry points by name and does not care where they come from, so the
        // resolver is the same two calls the rest of this spike uses.
        GRVkGetProcedureAddressDelegate resolve = (name, instance, device) =>
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(name + '\0');
            if (device != 0)
            {
                fixed (byte* p = bytes)
                {
                    var address = VkCmd.GetDeviceProcAddr(new VkDevice(device), p);
                    if (address != 0) return address;
                }
            }
            return (nint)vkGetInstanceProcAddr(new VkInstance(instance), bytes).Value;
        };

        GRVkExtensions? extensions = null;
        nint featuresPtr = 0;
        if (withExtensions)
        {
            extensions = GRVkExtensions.Create(resolve, vk.Instance.Handle, vk.PhysicalDevice.Handle,
                instanceExtensions ?? vk.InstanceExtensions, deviceExtensions ?? vk.DeviceExtensions);
            if (features2)
            {
                featuresPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(VkPhysicalDeviceFeatures2));
                var f = (VkPhysicalDeviceFeatures2*)featuresPtr;
                *f = new VkPhysicalDeviceFeatures2 { sType = VkStructureType.PhysicalDeviceFeatures2 };
                VkCmd.GetPhysicalDeviceFeatures2(vk.PhysicalDevice, f);
            }
        }

        using var backend = new GRVkBackendContext
        {
            VkInstance = vk.Instance.Handle,
            VkPhysicalDevice = vk.PhysicalDevice.Handle,
            VkDevice = vk.Device.Handle,
            VkQueue = vk.Queue.Handle,
            GraphicsQueueIndex = vk.QueueFamily,
            MaxAPIVersion = (uint)VkVersion.Version_1_2,
            GetProcedureAddress = resolve,
            Extensions = extensions,
            VkPhysicalDeviceFeatures2 = featuresPtr,
        };

        using var context = GRContext.CreateVulkan(backend);
        if (context is null) return (false, "GRContext.CreateVulkan returned null");

        var imageInfo = new GRVkImageInfo
        {
            Image = vk.Image.Handle,
            Alloc = zeroAlloc
                ? new GRVkAlloc { Memory = 0, Offset = 0, Size = 0, Flags = 0 }
                : new GRVkAlloc { Memory = vk.Memory.Handle, Offset = 0, Size = vk.AllocationSize },
            ImageTiling = TilingOptimal,
            // Undefined: Skia is free to transition from whatever GL left behind, and the
            // contents are about to be overwritten anyway.
            ImageLayout = LayoutUndefined,
            Format = (uint)vk.Format,
            ImageUsageFlags = vk.Usage,
            SampleCount = 1,
            LevelCount = 1,
            CurrentQueueFamily = vk.QueueFamily,
            SharingMode = 0,
        };

        using var target = new GRBackendRenderTarget(vk.Width, vk.Height, imageInfo);
        if (!target.IsValid) return (false, "the render target over the Vulkan image is not valid");

        var colorType = vk.Format switch
        {
            VkFormat.R16G16B16A16Sfloat => SKColorType.RgbaF16,
            VkFormat.B8G8R8A8Unorm => SKColorType.Bgra8888,
            VkFormat.R8G8B8A8Unorm => SKColorType.Rgba8888,
            _ => SKColorType.Bgra8888,
        };
        using var surface = SKSurface.Create(context, target, GRSurfaceOrigin.TopLeft, colorType);
        if (surface is null) return (false, $"SKSurface.Create returned null ({colorType})");

        // Something with obvious structure: a dark ground, a bright disc, and a band along
        // the bottom. Flat colour would not tell a real draw from a clear.
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(20, 20, 26));

        using var disc = new SKPaint { Color = new SKColor(230, 180, 60), IsAntialias = true };
        canvas.DrawCircle(vk.Width / 2f, vk.Height / 2f, vk.Height * 0.3f, disc);

        using var band = new SKPaint { Color = new SKColor(60, 140, 200) };
        canvas.DrawRect(0, vk.Height - 16, vk.Width, 16, band);

        context.Flush();
        context.Submit(synchronous: true);

        extensions?.Dispose();
        if (featuresPtr != 0) System.Runtime.InteropServices.Marshal.FreeHGlobal(featuresPtr);
        return (true, "drew");
    }
}

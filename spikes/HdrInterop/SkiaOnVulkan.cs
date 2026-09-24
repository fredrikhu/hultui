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

    public static (bool Ok, string Message) Draw(VulkanImage vk)
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

        using var backend = new GRVkBackendContext
        {
            VkInstance = vk.Instance.Handle,
            VkPhysicalDevice = vk.PhysicalDevice.Handle,
            VkDevice = vk.Device.Handle,
            VkQueue = vk.Queue.Handle,
            GraphicsQueueIndex = vk.QueueFamily,
            MaxAPIVersion = (uint)VkVersion.Version_1_2,
            GetProcedureAddress = resolve,
        };

        using var context = GRContext.CreateVulkan(backend);
        if (context is null) return (false, "GRContext.CreateVulkan returned null");

        var imageInfo = new GRVkImageInfo
        {
            Image = vk.Image.Handle,
            Alloc = new GRVkAlloc { Memory = vk.Memory.Handle, Offset = 0, Size = vk.AllocationSize },
            ImageTiling = TilingOptimal,
            // Undefined: Skia is free to transition from whatever GL left behind, and the
            // contents are about to be overwritten anyway.
            ImageLayout = LayoutUndefined,
            Format = FormatRgba16F,
            ImageUsageFlags = UsageColourSampledTransfer,
            SampleCount = 1,
            LevelCount = 1,
            CurrentQueueFamily = vk.QueueFamily,
            SharingMode = 0,
        };

        using var target = new GRBackendRenderTarget(vk.Width, vk.Height, imageInfo);
        if (!target.IsValid) return (false, "the render target over the Vulkan image is not valid");

        using var surface = SKSurface.Create(context, target, GRSurfaceOrigin.TopLeft, SKColorType.RgbaF16);
        if (surface is null) return (false, "SKSurface.Create returned null");

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

        return (true, "drew");
    }
}

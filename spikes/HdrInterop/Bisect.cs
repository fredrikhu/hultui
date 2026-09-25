using Vortice.Vulkan;

namespace HdrInterop;

/// <summary>
/// Why <c>SKSurface.Create</c> returns null over a swapchain image.
///
/// <para>Phase 1's renderer acquires a swapchain image, wraps it in a
/// <c>GRBackendRenderTarget</c> that validates, and then cannot make a surface from it. The
/// spike wraps an image it allocated itself and succeeds. Three things differ between those
/// two cases, and arguing about which is cheaper to settle than guessing: the pixel format
/// (BGRA8 rather than RGBA16F), the memory description (a swapchain image is declared with
/// an all-zero <c>GRVkAlloc</c>, since its memory is the presentation engine's), and whether
/// Skia was told about enabled extensions and device features.</para>
///
/// <para>Each row below changes exactly one of them.</para>
/// </summary>
internal static class Bisect
{
    private readonly record struct Case(
        string Name, VkFormat Format, bool WithExtensions, bool ZeroAlloc,
        string[]? InstanceExtensions = null, string[]? DeviceExtensions = null,
        bool Features2 = true, uint? Usage = null);

    /// <summary>What SDL asks for on Windows, and what the renderer's device enables —
    /// notably without VK_KHR_get_physical_device_properties2.</summary>
    private static readonly string[] SdlInstance = ["VK_KHR_surface", "VK_KHR_win32_surface"];
    private static readonly string[] RendererDevice = ["VK_KHR_swapchain"];

    public static int Run()
    {
        Console.WriteLine("── bisect: what stops Skia wrapping a swapchain image ──\n");

        Case[] cases =
        [
            new("RGBA16F, own alloc, no ext   (phase 0's known-good)", VkFormat.R16G16B16A16Sfloat, false, false),
            new("BGRA8,   own alloc, no ext   (format alone)         ", VkFormat.B8G8R8A8Unorm, false, false),
            new("BGRA8,   zero alloc, no ext  (as a swapchain image) ", VkFormat.B8G8R8A8Unorm, false, true),
            new("BGRA8,   own alloc, with ext (as the renderer does) ", VkFormat.B8G8R8A8Unorm, true, false),
            new("BGRA8,   zero alloc, with ext (the failing case)    ", VkFormat.B8G8R8A8Unorm, true, true),
            new("RGBA16F, zero alloc, no ext  (alloc alone)          ", VkFormat.R16G16B16A16Sfloat, false, true),

            // The renderer does not have the spike's extension list. Its instance is
            // whatever SDL asked for and its device has only the swapchain.
            new("BGRA8,   zero alloc, renderer's ext lists            ", VkFormat.B8G8R8A8Unorm, true, true,
                SdlInstance, RendererDevice),
            new("BGRA8,   zero alloc, renderer's ext, no features2    ", VkFormat.B8G8R8A8Unorm, true, true,
                SdlInstance, RendererDevice, Features2: false),

            // The last difference left: the renderer's swapchain is COLOR_ATTACHMENT |
            // TRANSFER_DST | SAMPLED, while this spike's image also has TRANSFER_SRC.
            new("BGRA8,   zero alloc, renderer's usage 0x16          ", VkFormat.B8G8R8A8Unorm, true, true,
                SdlInstance, RendererDevice, Usage: 0x10 | 0x02 | 0x04),
            new("BGRA8,   zero alloc, colour attachment only 0x10    ", VkFormat.B8G8R8A8Unorm, true, true,
                SdlInstance, RendererDevice, Usage: 0x10),
        ];

        int failures = 0;
        foreach (var c in cases)
        {
            string outcome;
            try
            {
                // A fresh device per case: a GRContext remembers what it was told, and
                // sharing one would let an earlier row decide a later one.
                using var vk = VulkanImage.Create(256, 144, c.Format, c.Usage);
                var result = SkiaOnVulkan.Draw(vk, c.WithExtensions, c.ZeroAlloc,
                    c.InstanceExtensions, c.DeviceExtensions, c.Features2);
                outcome = result.Ok ? "ok" : $"FAILED — {result.Message}";
                if (!result.Ok) failures++;
            }
            catch (Exception ex)
            {
                outcome = $"THREW — {ex.GetType().Name}: {ex.Message}";
                failures++;
            }
            Console.WriteLine($"  {c.Name}  {outcome}");
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "Every case passed — the cause is not in this matrix, and the difference is\n" +
              "somewhere else in the real renderer."
            : "Read down the column that changed: the first row to fail names the cause.");
        return failures == 0 ? 0 : 10;
    }
}

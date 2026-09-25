using System.Text;
using Vortice.Vulkan;
using static HultUI.Platform.SDL3.VulkanCommands;

namespace HultUI.Platform.SDL3;

/// <summary>
/// What this window's Vulkan actually got, in the shape <c>DescribeGraphics</c> uses for GL.
///
/// <para>The GL version answers "which driver, which swap interval, how many samples" —
/// questions you ask when a machine renders differently from the one beside it. The Vulkan
/// version answers those and one more that matters for <c>docs/design/HDR.md</c>: which
/// formats and colour spaces this surface will actually accept. Phase 3 has to pick an HDR
/// one, and whether there is one to pick is a property of the display, the compositor and
/// the driver together — not something to assume from a specification.</para>
/// </summary>
internal sealed unsafe partial class SdlVulkanHost
{
    public string Describe(int pixelWidth, int pixelHeight, float displayScale, float refreshRate)
    {
        var text = new StringBuilder();
        text.Append(System.Globalization.CultureInfo.InvariantCulture,
            $"vulkan device=\"{DeviceName}\" api=1.2 present={_presentMode}");
        text.Append(System.Globalization.CultureInfo.InvariantCulture,
            $" format={_format} colorSpace={_colorSpace} images={_images.Length}");
        text.Append(System.Globalization.CultureInfo.InvariantCulture,
            $" drawable={pixelWidth}x{pixelHeight}px scale={displayScale} refresh={refreshRate}Hz");

        text.Append(" surfaceFormats=[");
        text.Append(string.Join(", ", SupportedFormats().Select(f => $"{f.Format}/{f.ColorSpace}")));
        text.Append(']');
        return text.ToString();
    }

    /// <summary>
    /// Every format and colour space pair the surface will take. Enumerated rather than
    /// cached from swapchain creation, because this is a diagnostic and the interesting case
    /// is the pairs that were <i>not</i> chosen.
    /// </summary>
    public IReadOnlyList<(VkFormat Format, VkColorSpaceKHR ColorSpace)> SupportedFormats()
    {
        if (_physicalDevice.IsNull || _surface.IsNull) return [];

        uint count = 0;
        if (GetSurfaceFormats(_physicalDevice, _surface, &count, null) != VkResult.Success || count == 0)
            return [];

        var native = stackalloc VkSurfaceFormatKHR[(int)count];
        if (GetSurfaceFormats(_physicalDevice, _surface, &count, native) != VkResult.Success) return [];

        var list = new List<(VkFormat, VkColorSpaceKHR)>((int)count);
        for (uint i = 0; i < count; i++) list.Add((native[i].format, native[i].colorSpace));
        return list;
    }
}

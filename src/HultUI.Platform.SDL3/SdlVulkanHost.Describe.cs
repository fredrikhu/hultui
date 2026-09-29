using System.Text;
using Vortice.Vulkan;

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

        text.Append(System.Globalization.CultureInfo.InvariantCulture,
            $" hdrMetadataExt={(HasDeviceExtension("VK_EXT_hdr_metadata") ? "yes" : "no")}");

        text.Append(" surfaceFormats=[");
        text.Append(string.Join(", ", SupportedFormats().Select(f => $"{f.Format}/{f.ColorSpace}")));
        text.Append(']');
        return text.ToString();
    }

    /// <summary>Whether the physical device offers an extension — asked for the description
    /// rather than to enable it, since enabling happens at device creation.</summary>
    public bool HasDeviceExtension(string name)
    {
        if (_physicalDevice.IsNull) return false;
        uint count = 0;
        if (_vk.EnumerateDeviceExtensionProperties(_physicalDevice, null, &count, null) != VkResult.Success
            || count == 0) return false;

        var properties = stackalloc VkExtensionProperties[(int)count];
        if (_vk.EnumerateDeviceExtensionProperties(_physicalDevice, null, &count, properties) != VkResult.Success)
            return false;

        for (uint i = 0; i < count; i++)
            if (string.Equals(System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)properties[i].extensionName),
                    name, StringComparison.Ordinal))
                return true;
        return false;
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
        if (_vk.GetSurfaceFormats(_physicalDevice, _surface, &count, null) != VkResult.Success || count == 0)
            return [];

        var native = stackalloc VkSurfaceFormatKHR[(int)count];
        if (_vk.GetSurfaceFormats(_physicalDevice, _surface, &count, native) != VkResult.Success) return [];

        var list = new List<(VkFormat, VkColorSpaceKHR)>((int)count);
        for (uint i = 0; i < count; i++) list.Add((native[i].format, native[i].colorSpace));
        return list;
    }
}

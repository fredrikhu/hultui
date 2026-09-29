using Vortice.Vulkan;

namespace HultUI.Platform.SDL3;

/// <summary>
/// Writing a presented frame to disk, for the one thing a Vulkan backend cannot otherwise
/// prove.
///
/// <para>The headless driver renders through <c>SkiaRasterRenderSurface</c> and never touches
/// a GPU backend — which is exactly why it is safe and fast, and exactly why it cannot say
/// whether this renderer draws correctly. Screen-grabbing the window captures whatever is in
/// front of it. So the only honest evidence is the swapchain image itself, read back the way
/// the phase 0 spike read back its own.</para>
///
/// <para>Set <c>HULTUI_VULKAN_CAPTURE</c> to a path and the first presented frame that reuses
/// a swapchain image is written there as a PPM, once per swapchain, and the run carries on. Diagnostic only: it stalls the queue and
/// allocates a staging buffer per capture.</para>
/// </summary>
internal sealed unsafe partial class SdlVulkanHost
{
    private static readonly string? CapturePath =
        Environment.GetEnvironmentVariable("HULTUI_VULKAN_CAPTURE");

    /// <summary>
    /// Which presented frame to capture. Not the first, deliberately.
    ///
    /// <para>Not frame 1. While every swapchain image is still fresh, every belief about it
    /// is true — a renderer can draw those frames perfectly and every frame afterwards
    /// black, which is exactly what the layout-caching bug did, and a capture of frame 1 saw
    /// none of it. The default is therefore the <b>first frame that reuses an image</b>,
    /// which is one past the swapchain's count: the earliest frame at which stale state can
    /// show.</para>
    ///
    /// <para><b>Whether an idle window reaches it is not predictable.</b> An app that renders
    /// only when dirty presents as many frames per swapchain as events happen to dirty it,
    /// and that depends on where the window lands, not on the renderer. On one KDE/Wayland
    /// machine with a three-image swapchain, the same untouched window presented three frames
    /// when it opened on the output it was created for (default missed), four on the last
    /// swapchain when it opened on the other one (default reached), and one after being moved
    /// between outputs. So a capture that never appears may mean an idle window rather than a
    /// renderer that drew nothing, and one that does appear says nothing about whether an idle
    /// window would have — touch the window (hovering a button is enough) before concluding
    /// either way, or set the override below.</para>
    ///
    /// <para><c>HULTUI_VULKAN_CAPTURE_FRAME</c> overrides it; a bigger number needs an app
    /// that keeps drawing, and a smaller one gives up the reuse this default exists for.</para>
    /// </summary>
    private static readonly int CaptureFrameOverride =
        int.TryParse(Environment.GetEnvironmentVariable("HULTUI_VULKAN_CAPTURE_FRAME"), out var n) && n > 0
            ? n : 0;

    private int CaptureFrame => CaptureFrameOverride > 0 ? CaptureFrameOverride : _images.Length + 1;

    private int _presentedFrames;
    private bool _captured;

    /// <summary>
    /// Called when a swapchain is built, so the count starts again and the capture fires
    /// once per swapchain rather than once per process.
    ///
    /// <para>That makes the file the newest swapchain's work: resize the window and what
    /// lands on disk is from the size it is now, which is the only way to check a resize
    /// without being able to look at the screen. One-shot-per-process capture fires within
    /// the first few frames and can never see one.</para>
    /// </summary>
    private void ResetCapture()
    {
        _presentedFrames = 0;
        _captured = false;
    }

    /// <summary>Called after Skia's work is done and before the present transition, when the
    /// image holds this frame and is still a colour attachment.</summary>
    private void CaptureIfAsked(VkImage image)
    {
        if (_captured || string.IsNullOrEmpty(CapturePath)) return;
        if (++_presentedFrames < CaptureFrame) return;
        _captured = true;
        try { Capture(image, CapturePath); }
        catch (Exception ex) { Console.Error.WriteLine($"[vulkan] capture failed: {ex.Message}"); }
    }

    private void Capture(VkImage image, string path)
    {
        int width = _widthPx, height = _heightPx;
        int bytesPerPixel = BytesPerPixel(_format);
        ulong bytes = (ulong)width * (ulong)height * (ulong)bytesPerPixel;

        var bufferInfo = new VkBufferCreateInfo
        {
            size = bytes,
            usage = VkBufferUsageFlags.TransferDst,
            sharingMode = VkSharingMode.Exclusive,
        };
        VkBuffer buffer;
        Check(_vk.CreateBuffer(_device, &bufferInfo, 0, &buffer), "vkCreateBuffer");

        var requirements = new VkMemoryRequirements();
        _vk.GetBufferMemoryRequirements(_device, buffer, &requirements);

        var properties = new VkPhysicalDeviceMemoryProperties();
        _vk.GetMemoryProperties(_physicalDevice, &properties);
        uint typeIndex = uint.MaxValue;
        const VkMemoryPropertyFlags wanted = VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent;
        for (uint i = 0; i < properties.memoryTypeCount; i++)
        {
            if ((requirements.memoryTypeBits & (1u << (int)i)) == 0) continue;
            if ((properties.memoryTypes[(int)i].propertyFlags & wanted) != wanted) continue;
            typeIndex = i;
            break;
        }
        if (typeIndex == uint.MaxValue) throw new InvalidOperationException("no host-visible memory type");

        var allocate = new VkMemoryAllocateInfo { allocationSize = requirements.size, memoryTypeIndex = typeIndex };
        VkDeviceMemory memory;
        Check(_vk.AllocateMemory(_device, &allocate, 0, &memory), "vkAllocateMemory");
        Check(_vk.BindBufferMemory(_device, buffer, memory, 0), "vkBindBufferMemory");

        var cmd = BeginOneShot();

        var toSource = Barrier(image, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.TransferSrcOptimal,
            VkAccessFlags.ColorAttachmentWrite, VkAccessFlags.TransferRead);
        _vk.CmdPipelineBarrier(cmd, VkPipelineStageFlags.ColorAttachmentOutput, VkPipelineStageFlags.Transfer,
            VkDependencyFlags.None, 0, 0, 0, 0, 1, &toSource);

        var region = new VkBufferImageCopy
        {
            imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
            imageExtent = new VkExtent3D(width, height, 1),
        };
        _vk.CmdCopyImageToBuffer(cmd, image, VkImageLayout.TransferSrcOptimal, buffer, 1, &region);

        // Back to where the present transition expects to find it.
        var back = Barrier(image, VkImageLayout.TransferSrcOptimal, VkImageLayout.ColorAttachmentOptimal,
            VkAccessFlags.TransferRead, VkAccessFlags.ColorAttachmentWrite);
        _vk.CmdPipelineBarrier(cmd, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.ColorAttachmentOutput,
            VkDependencyFlags.None, 0, 0, 0, 0, 1, &back);

        EndOneShot(cmd);

        void* mapped;
        Check(_vk.MapMemory(_device, memory, 0, bytes, 0, &mapped), "vkMapMemory");
        WritePpm(path, (byte*)mapped, width, height, _format, SdrWhiteScale);
        if (_format == VkFormat.R16G16B16A16Sfloat) ReportRange((Half*)mapped, width, height);
        _vk.UnmapMemory(_device, memory);

        _vk.DestroyBuffer(_device, buffer, 0);
        _vk.FreeMemory(_device, memory, 0);
        Console.Error.WriteLine($"[vulkan] captured frame {_presentedFrames} ({width}x{height}) to {path}");
    }

    private static VkImageMemoryBarrier Barrier(VkImage image, VkImageLayout from, VkImageLayout to,
        VkAccessFlags srcAccess, VkAccessFlags dstAccess) => new()
        {
            sType = VkStructureType.ImageMemoryBarrier,
            srcAccessMask = srcAccess,
            dstAccessMask = dstAccess,
            oldLayout = from,
            newLayout = to,
            srcQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
            image = image,
            subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1),
        };

    private static int BytesPerPixel(VkFormat format) =>
        format == VkFormat.R16G16B16A16Sfloat ? 8 : 4;

    /// <summary>
    /// The swapchain as an 8-bit PPM, whatever it actually is.
    ///
    /// <para>Channel order comes off the format rather than being assumed, and a half-float
    /// surface is <b>encoded back to sRGB on the way out</b>. That is the whole point of the
    /// file: scRGB holds linear light, and writing those numbers straight into a PPM would
    /// show a washed-out image that says nothing about whether the renderer is right. Encoded,
    /// it should be indistinguishable from the 8-bit path — which is the test.</para>
    /// </summary>
    private static void WritePpm(string path, byte* pixels, int width, int height, VkFormat format,
        float whiteScale)
    {
        var body = new byte[width * height * 3];

        if (format == VkFormat.R16G16B16A16Sfloat)
        {
            // Divided by the same scale the renderer multiplied by, so the file shows what
            // the display shows rather than numbers three times too large.
            var halves = (Half*)pixels;
            float inverse = whiteScale > 0f ? 1f / whiteScale : 1f;
            for (int i = 0, o = 0; i < width * height; i++)
                for (int c = 0; c < 3; c++)
                    body[o++] = (byte)Math.Clamp(
                        SrgbEncode((float)halves[i * 4 + c] * inverse) * 255f + 0.5f, 0f, 255f);
        }
        else
        {
            bool bgra = format is VkFormat.B8G8R8A8Unorm or VkFormat.B8G8R8A8Srgb;
            for (int i = 0, o = 0; i < width * height; i++)
            {
                byte a = pixels[i * 4], b = pixels[i * 4 + 1], c = pixels[i * 4 + 2];
                body[o++] = bgra ? c : a;
                body[o++] = b;
                body[o++] = bgra ? a : c;
            }
        }

        using var file = File.Create(path);
        char nl = '\n';
        file.Write(System.Text.Encoding.ASCII.GetBytes($"P6{nl}{width} {height}{nl}255{nl}"));
        file.Write(body);
    }

    /// <summary>
    /// The half-float swapchain's actual range, which the PPM beside it cannot show.
    ///
    /// <para>A PPM is eight bits and the writer divides by the white scale before encoding, so
    /// everything above white lands on 255 and an HDR frame looks exactly like an SDR one.
    /// That is the whole difficulty with this backend: the file is right, the screen is right,
    /// and neither proves the other. The numbers do — a maximum above the white scale means
    /// something brighter than SDR white reached the swapchain.</para>
    /// </summary>
    private void ReportRange(Half* pixels, int width, int height)
    {
        float min = float.MaxValue, max = float.MinValue;
        int aboveWhite = 0;
        for (int i = 0; i < width * height; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                float v = (float)pixels[i * 4 + c];
                if (float.IsNaN(v)) continue;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
                if (v > SdrWhiteScale * 1.001f) aboveWhite++;
            }
        }
        Console.Error.WriteLine($"[vulkan] scRGB range: min {min:0.000} max {max:0.000} " +
            $"(SDR white is {SdrWhiteScale:0.00}); {aboveWhite} channel samples above it" +
            (aboveWhite > 0 ? " — HDR reached the swapchain" : ""));
    }

    /// <summary>Linear light to sRGB, the standard piecewise curve.</summary>
    private static float SrgbEncode(float linear)
    {
        if (float.IsNaN(linear) || linear <= 0f) return 0f;
        return linear <= 0.0031308f
            ? linear * 12.92f
            : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
    }

    private VkCommandBuffer BeginOneShot()
    {
        var allocate = new VkCommandBufferAllocateInfo
        {
            commandPool = _pool,
            level = VkCommandBufferLevel.Primary,
            commandBufferCount = 1,
        };
        VkCommandBuffer cmd;
        Check(_vk.AllocateCommandBuffers(_device, &allocate, &cmd), "vkAllocateCommandBuffers");
        var begin = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        Check(_vk.BeginCommandBuffer(cmd, &begin), "vkBeginCommandBuffer");
        return cmd;
    }

    private void EndOneShot(VkCommandBuffer cmd)
    {
        Check(_vk.EndCommandBuffer(cmd), "vkEndCommandBuffer");
        var submit = new VkSubmitInfo { commandBufferCount = 1, pCommandBuffers = &cmd };
        Check(_vk.QueueSubmit(_queue, 1, &submit, VkFence.Null), "vkQueueSubmit");
        Check(_vk.QueueWaitIdle(_queue), "vkQueueWaitIdle");
        _vk.FreeCommandBuffers(_device, _pool, 1, &cmd);
    }
}

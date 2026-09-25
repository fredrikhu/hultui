using Vortice.Vulkan;
using static HultUI.Platform.SDL3.VulkanCommands;

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
/// <para>Set <c>HULTUI_VULKAN_CAPTURE</c> to a path and the next presented frame is written
/// there as a PPM, once, and the run carries on. Diagnostic only: it stalls the queue and
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
    /// show, and still reachable in an app that renders only when dirty.</para>
    ///
    /// <para><c>HULTUI_VULKAN_CAPTURE_FRAME</c> overrides it; a bigger number needs an app
    /// that keeps drawing.</para>
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
        Check(CreateBuffer(_device, &bufferInfo, 0, &buffer), "vkCreateBuffer");

        var requirements = new VkMemoryRequirements();
        GetBufferMemoryRequirements(_device, buffer, &requirements);

        var properties = new VkPhysicalDeviceMemoryProperties();
        GetMemoryProperties(_physicalDevice, &properties);
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
        Check(AllocateMemory(_device, &allocate, 0, &memory), "vkAllocateMemory");
        Check(BindBufferMemory(_device, buffer, memory, 0), "vkBindBufferMemory");

        var cmd = BeginOneShot();

        var toSource = Barrier(image, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.TransferSrcOptimal,
            VkAccessFlags.ColorAttachmentWrite, VkAccessFlags.TransferRead);
        CmdPipelineBarrier(cmd, VkPipelineStageFlags.ColorAttachmentOutput, VkPipelineStageFlags.Transfer,
            VkDependencyFlags.None, 0, 0, 0, 0, 1, &toSource);

        var region = new VkBufferImageCopy
        {
            imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
            imageExtent = new VkExtent3D(width, height, 1),
        };
        CmdCopyImageToBuffer(cmd, image, VkImageLayout.TransferSrcOptimal, buffer, 1, &region);

        // Back to where the present transition expects to find it.
        var back = Barrier(image, VkImageLayout.TransferSrcOptimal, VkImageLayout.ColorAttachmentOptimal,
            VkAccessFlags.TransferRead, VkAccessFlags.ColorAttachmentWrite);
        CmdPipelineBarrier(cmd, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.ColorAttachmentOutput,
            VkDependencyFlags.None, 0, 0, 0, 0, 1, &back);

        EndOneShot(cmd);

        void* mapped;
        Check(MapMemory(_device, memory, 0, bytes, 0, &mapped), "vkMapMemory");
        WritePpm(path, (byte*)mapped, width, height, _format);
        UnmapMemory(_device, memory);

        DestroyBuffer(_device, buffer, 0);
        FreeMemory(_device, memory, 0);
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
    private static void WritePpm(string path, byte* pixels, int width, int height, VkFormat format)
    {
        var body = new byte[width * height * 3];

        if (format == VkFormat.R16G16B16A16Sfloat)
        {
            var halves = (Half*)pixels;
            for (int i = 0, o = 0; i < width * height; i++)
                for (int c = 0; c < 3; c++)
                    body[o++] = (byte)Math.Clamp(SrgbEncode((float)halves[i * 4 + c]) * 255f + 0.5f, 0f, 255f);
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
        Check(AllocateCommandBuffers(_device, &allocate, &cmd), "vkAllocateCommandBuffers");
        var begin = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        Check(BeginCommandBuffer(cmd, &begin), "vkBeginCommandBuffer");
        return cmd;
    }

    private void EndOneShot(VkCommandBuffer cmd)
    {
        Check(EndCommandBuffer(cmd), "vkEndCommandBuffer");
        var submit = new VkSubmitInfo { commandBufferCount = 1, pCommandBuffers = &cmd };
        Check(QueueSubmit(_queue, 1, &submit, VkFence.Null), "vkQueueSubmit");
        Check(QueueWaitIdle(_queue), "vkQueueWaitIdle");
        FreeCommandBuffers(_device, _pool, 1, &cmd);
    }
}

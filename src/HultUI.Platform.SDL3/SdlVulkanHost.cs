using System.Runtime.InteropServices;
using HultUI.Core.DevTools;
using HultUI.Core.Rendering;
using SDL;
using Vortice.Vulkan;
using static SDL.SDL3;
using static Vortice.Vulkan.Vulkan;
using static HultUI.Platform.SDL3.VulkanCommands;

namespace HultUI.Platform.SDL3;

/// <summary>
/// A Vulkan device and swapchain for an SDL window — the platform half of
/// <see cref="IVulkanSurfaceHost"/>, and phase 1 of <c>docs/design/HDR.md</c>.
///
/// <para><b>Synchronisation: measured, and left alone.</b> This used to wait the queue idle
/// after Skia submitted — a real stall, and the reason the class once said it cost a frame's
/// GPU work per frame. That half is gone: the present-transition submit signals a semaphore
/// and <c>vkQueuePresentKHR</c> waits on it, which is the ordering the drain was paying for.
/// What remains is the acquire, which still uses a <i>fence</i> because SkiaSharp 3.119.2
/// exposes no <c>GRFlushInfo</c> and no <c>GRBackendSemaphore</c> — the only native entry
/// points its binding has are <c>gr_direct_context_flush</c> and <c>flush_and_submit</c>,
/// neither taking flush info — so there is nothing to hand a semaphore to.</para>
///
/// <para><b>That fence costs 0.0064 ms a frame</b>, measured over 120 frames of continuous
/// animation paced to a 120 Hz display: six microseconds, or eight hundredths of one per
/// cent of the frame. Replacing it with a semaphore would move six microseconds from the CPU
/// to the GPU. The residual cost against the GL path is about 0.15 to 0.25 ms a frame, which
/// shows only on operations small enough for it to matter — <c>select row</c> 0.43 against
/// 0.59 ms, <c>clear</c> 0.27 against 0.41 — and is invisible on anything layout-bound
/// (<c>create 10,000</c>: 316.6 against 319.8). That is consistent with the two things this
/// backend does that GL does not: one extra command-buffer submit per frame, and a render
/// target Skia makes it rebuild every frame because there is no way to tell Skia an image's
/// layout. Both are the same binding limitation as the semaphore, and neither is a stall.</para>
/// </summary>
internal sealed unsafe partial class SdlVulkanHost : IVulkanSurfaceHost, IDisposable
{
    private const ulong NoTimeout = ulong.MaxValue;

    /// <summary>
    /// <c>HULTUI_VULKAN_F16</c> as an override rather than a switch: 1 forces the half-float
    /// surface on, 0 forces it off, and unset leaves the decision to the display. Phase 3
    /// makes the display the default answer; the override stays because a machine whose HDR
    /// state is reported wrongly should still be drivable, and because forcing it on an SDR
    /// display is how the conversion gets tested without changing monitor settings.
    /// </summary>
    private static readonly bool? WideColorOverride =
        Environment.GetEnvironmentVariable("HULTUI_VULKAN_F16") switch
        {
            "1" => true,
            "0" => false,
            _ => null,
        };

    /// <summary>
    /// <c>HULTUI_VULKAN_PRESENT</c> pins the present mode to <c>immediate</c>, <c>mailbox</c>
    /// or <c>fifo</c>; unset leaves the choice to <see cref="ChoosePresentMode"/>.
    ///
    /// <para>It exists because a window dragged by its edge visibly stretches its old
    /// contents before snapping to a correct frame, and presentation is the only candidate
    /// left: the throttle draws the same number of frames on both renderers, a swapchain
    /// rebuild costs a millisecond, and nothing accumulates. A mode this surface does not
    /// offer is ignored rather than fatal — the point is to compare, not to insist.</para>
    /// </summary>
    private static readonly VkPresentModeKHR? PresentModeOverride =
        Environment.GetEnvironmentVariable("HULTUI_VULKAN_PRESENT")?.ToLowerInvariant() switch
        {
            "immediate" => VkPresentModeKHR.Immediate,
            "mailbox" => VkPresentModeKHR.Mailbox,
            "fifo" => VkPresentModeKHR.Fifo,
            "fifo-relaxed" or "fiforelaxed" => VkPresentModeKHR.FifoRelaxed,
            _ => null,
        };

    /// <summary>
    /// <c>HULTUI_VULKAN_FIXED_EXTENT=1</c> allocates the swapchain larger than the window and
    /// presents it un-scaled, through <c>VK_KHR_swapchain_maintenance1</c>. <b>Off by default,
    /// and the reason is video.</b>
    ///
    /// <para>It does what it was built for. Rebuilding three half-float images for every size a
    /// dragged window passes through is what a resize costs — 122 rebuilds and roughly 5 GB of
    /// allocation churn across one drag — and over-allocating to a 256-pixel bucket takes that
    /// to 10 rebuilds and resize CPU from 801 to 466 ms/s. It also removes the resize stretch
    /// <i>structurally</i> rather than by racing: with ONE_TO_ONE the compositor has nothing to
    /// scale, and even IMMEDIATE, which stretched 18 frames in 150 with an exact-size swapchain,
    /// stretched 0 in 150 with this on.</para>
    ///
    /// <para><b>And it costs 4K HDR playback about 70% more CPU: 91–95 ms/s against 48–63</b>,
    /// measured three times against GL as a drift reference. A swapchain whose extent matches
    /// the window can be handed to the compositor directly; one that does not appears to lose
    /// that path and be copied instead. Video runs for hours and a drag lasts a second, so the
    /// default is the one that favours video — the same reasoning that chose MAILBOX over FIFO,
    /// pointing the other way.</para>
    ///
    /// <para>Turning this on only <i>during</i> a drag would get both, and is the obvious next
    /// step; it needs a settled-size signal the renderer does not have today.</para>
    /// </summary>
    private static readonly bool? FixedExtentOverride =
        Environment.GetEnvironmentVariable("HULTUI_VULKAN_FIXED_EXTENT") switch
        {
            "1" => true,
            "0" => false,
            _ => null,
        };

    /// <summary>Whether the instance got the surface-maintenance pair, so present-scaling
    /// capabilities can be asked for at all.</summary>
    private bool _surfaceMaintenanceEnabled;

    /// <summary>Whether the device got <c>swapchain_maintenance1</c> and the surface really
    /// offers ONE_TO_ONE with MIN gravity. Only then may the swapchain outsize the window.</summary>
    private bool _fixedExtentAvailable;

    /// <summary>The extent the swapchain images were actually allocated at, which is at least
    /// the window's and may be larger. The window size is <see cref="_widthPx"/>.</summary>
    private int _allocWidthPx;
    private int _allocHeightPx;

    /// <summary>Whether this window's display is in HDR mode. Set by the window, which is
    /// the thing that can ask SDL, and refreshed when the window changes display.</summary>
    public bool DisplayIsHdr { get; set; }

    private bool WantsWideColor => WideColorOverride ?? DisplayIsHdr;

    /// <summary>
    /// Whether the instance has <c>VK_EXT_swapchain_colorspace</c>. Without it
    /// <c>SRGB_NONLINEAR</c> is the only colour space a swapchain may use, whatever the surface
    /// lists — so scRGB is never chosen, however much it is wanted.
    /// </summary>
    private bool _colorSpaceExtensionEnabled;

    // VkImageUsageFlagBits, spelled out because getting these wrong is invisible: an
    // earlier version had 0x04 commented as TRANSFER_SRC when it is SAMPLED, and the
    // swapchain went without the one bit Skia actually requires.
    private const uint UsageTransferSrc = 0x01;
    private const uint UsageTransferDst = 0x02;
    private const uint UsageSampled = 0x04;
    private const uint UsageColorAttachment = 0x10;

    /// <summary>
    /// What the swapchain images are created with, and what Skia is told they have.
    ///
    /// <para><b>TRANSFER_SRC is not optional.</b> Skia refuses to make a surface over a
    /// render target without it — <c>SKSurface.Create</c> returns null and says nothing —
    /// and the bisect in spikes/HdrInterop proves it: identical in every other respect,
    /// 0x17 succeeds and 0x16 fails. COLOR_ATTACHMENT alone fails too.</para>
    /// </summary>
    private const uint SwapchainUsage =
        UsageColorAttachment | UsageTransferSrc | UsageTransferDst | UsageSampled;

    private VkInstance _instance;
    private VkSurfaceKHR _surface;
    private VkPhysicalDevice _physicalDevice;
    private VkDevice _device;
    private VkQueue _queue;
    private uint _queueFamily;

    private VkSwapchainKHR _swapchain;
    private VkImage[] _images = [];

    /// <summary>Signalled when this image's frame has finished drawing; the present waits on
    /// it. One per image rather than one shared, because the previous frame's signal must not
    /// still be pending when the next frame wants to signal it — and re-acquiring an image is
    /// the point at which the presentation engine says its last use is done.</summary>
    private VkSemaphore[] _renderFinished = [];

    /// <summary>The layout-transition command buffer for each image, recorded fresh each
    /// frame. Kept rather than allocated and freed per frame: freeing a command buffer that
    /// may still be executing is undefined, and without the queue drain it often is.</summary>
    private VkCommandBuffer[] _presentCmd = [];
    private VkFormat _format = VkFormat.B8G8R8A8Unorm;
    private VkColorSpaceKHR _colorSpace = VkColorSpaceKHR.SrgbNonLinear;
    private VkPresentModeKHR _presentMode = VkPresentModeKHR.Fifo;
    private VkCommandPool _pool;
    private VkFence _acquireFence;

    private uint _usage;
    private uint _acquiredIndex;
    private bool _haveAcquired;
    private int _widthPx;
    private int _heightPx;
    private bool _disposed;

    public nint Instance => _instance.Handle;
    public nint PhysicalDevice => _physicalDevice.Handle;
    public nint Device => _device.Handle;
    public nint GraphicsQueue => _queue.Handle;
    public uint GraphicsQueueFamily => _queueFamily;
    public uint ApiVersion => (uint)VkVersion.Version_1_2;
    public int ImageCount => _images.Length;
    public int WidthPx => _widthPx;
    public int HeightPx => _heightPx;

    public string DeviceName { get; private set; } = "";

    public string[] EnabledInstanceExtensions { get; private set; } = [];
    public string[] EnabledDeviceExtensions { get; private set; } = ["VK_KHR_swapchain"];

    /// <summary>
    /// What a device needs before an image of its can be handed to OpenGL — phase 4.
    ///
    /// <para>Asked for whenever the driver has them, not when a video is opened: a device
    /// cannot gain an extension afterwards, and whether this window will ever play one is
    /// not knowable at start-up. Unused, they cost nothing. The platform half differs
    /// because the handle does: Windows shares an <c>HANDLE</c> and Linux a file
    /// descriptor.</para>
    ///
    /// <para>Dedicated allocation is not formally required and is asked for anyway — every
    /// vendor's documentation wants it here, and the drivers that misbehave without it do so
    /// silently.</para>
    /// </summary>
    private static string[] InteropDeviceExtensions => OperatingSystem.IsWindows()
        ? ["VK_KHR_external_memory", "VK_KHR_external_memory_win32", "VK_KHR_dedicated_allocation"]
        : ["VK_KHR_external_memory", "VK_KHR_external_memory_fd", "VK_KHR_dedicated_allocation"];

    /// <summary>
    /// What ordering GL's writes against Vulkan's reads without stalling the CPU needs.
    ///
    /// <para>Asked for separately from the memory extensions and allowed to be absent: a driver
    /// that shares an image but not a semaphore still works, it just pays <c>glFinish</c> —
    /// which on a 4K decode is 0.74 to 5.33 ms a frame, so this is worth asking for.</para>
    /// </summary>
    private static string[] SemaphoreDeviceExtensions => OperatingSystem.IsWindows()
        ? ["VK_KHR_external_semaphore", "VK_KHR_external_semaphore_win32"]
        : ["VK_KHR_external_semaphore", "VK_KHR_external_semaphore_fd"];

    /// <summary>Whether the semaphore extensions were enabled too. False falls back to
    /// <c>glFinish</c>, which is correct and costs the CPU.</summary>
    private bool _semaphoreExtensionsEnabled;

    /// <summary>Whether every one of those was actually enabled. False means no interop, and
    /// a video on this window goes the software route instead.</summary>
    private bool _interopExtensionsEnabled;

    /// <summary>Kept alive for the renderer's lifetime — Skia holds the pointer.</summary>
    private nint _features2;
    public nint PhysicalDeviceFeatures2 => _features2;

    /// <summary>Set by the window, which is the thing that knows which display it is on.</summary>
    public float SdrWhiteScale { get; set; } = 1f;

    /// <summary>Set by the window, which knows which compositor it is under.</summary>
    public bool SdrIsGamma22 { get; set; }

    /// <summary>
    /// The display's peak luminance, from <c>HULTUI_VULKAN_PEAK_NITS</c> or zero.
    ///
    /// <para>There is no query behind this. Windows gives an SDR white level and no peak, and
    /// this SDL binding exposes no HDR headroom, so a number here can only come from the
    /// person running it. Zero means "unknown" rather than "dark", and a producer told zero
    /// assumes rather than believes.</para>
    /// </summary>
    public float DisplayPeakNits
    {
        get => PeakOverride > 0f ? PeakOverride : _displayPeakNits;
        set => _displayPeakNits = value;
    }

    private float _displayPeakNits;

    private static readonly float PeakOverride =
        float.TryParse(Environment.GetEnvironmentVariable("HULTUI_VULKAN_PEAK_NITS"),
            System.Globalization.CultureInfo.InvariantCulture, out float nits) && nits > 0f
            ? nits : 0f;

    public nint GetProcedureAddress(string name, nint instance, nint device) =>
        Resolve(name, instance, device);

    public static SdlVulkanHost Create(SDL_Window* window, int widthPx, int heightPx, bool displayIsHdr)
    {
        var host = new SdlVulkanHost { DisplayIsHdr = displayIsHdr };
        try
        {
            host.Init(window, widthPx, heightPx);
        }
        catch
        {
            // A driver that is present but unusable fails partway, with an instance and a
            // surface already made. The window falls back to GL and is replaced, so whatever
            // was built has to go now: nothing else holds this host to dispose it later.
            host.Dispose();
            throw;
        }
        return host;
    }

    private void Init(SDL_Window* window, int widthPx, int heightPx)
    {
        Check(vkInitialize(), "vkInitialize");

        // SDL knows which instance extensions this window system needs — VK_KHR_surface plus
        // the platform one — and asking it is the only portable way to find out.
        uint extensionCount = 0;
        var sdlExtensions = SDL_Vulkan_GetInstanceExtensions(&extensionCount);
        if (sdlExtensions is null)
            throw new InvalidOperationException($"SDL_Vulkan_GetInstanceExtensions: {SDL_GetError()}");

        var names = new string[extensionCount];
        for (uint i = 0; i < extensionCount; i++)
            names[i] = Marshal.PtrToStringUTF8((nint)sdlExtensions[i]) ?? "";

        // Without VK_EXT_swapchain_colorspace, SRGB_NONLINEAR is the only colour space a
        // swapchain may legally use — drivers list the others regardless, which makes this
        // easy to leave out and then work by accident on one machine.
        //
        // Enabled whenever the loader has it, not only when wide colour is wanted now: an
        // instance cannot gain an extension later, and the window may be dragged onto an HDR
        // display at any point. An extension that goes unused costs nothing.
        _colorSpaceExtensionEnabled = HasInstanceExtension("VK_EXT_swapchain_colorspace");
        if (_colorSpaceExtensionEnabled)
            names = [.. names, "VK_EXT_swapchain_colorspace"];

        // For a swapchain whose extent may differ from the window's. Both are instance
        // extensions and both are needed: surface_maintenance1 defines the present-scaling
        // capabilities, and they are only reachable through the chained capability query that
        // get_surface_capabilities2 provides. Enabled whenever present, like the colour-space
        // one above — an instance cannot gain an extension later.
        if (HasInstanceExtension("VK_KHR_get_surface_capabilities2")
            && (HasInstanceExtension("VK_KHR_surface_maintenance1")
                || HasInstanceExtension("VK_EXT_surface_maintenance1")))
        {
            names = [.. names, "VK_KHR_get_surface_capabilities2"];
            names = [.. names, HasInstanceExtension("VK_KHR_surface_maintenance1")
                ? "VK_KHR_surface_maintenance1"
                : "VK_EXT_surface_maintenance1"];
            _surfaceMaintenanceEnabled = true;
        }
        EnabledInstanceExtensions = names;

        var appInfo = new VkApplicationInfo { apiVersion = VkVersion.Version_1_2 };
        using var extensions = new VkStringArray(names);
        var instanceInfo = new VkInstanceCreateInfo
        {
            pApplicationInfo = &appInfo,
            enabledExtensionCount = extensions.Length,
            ppEnabledExtensionNames = extensions,
        };
        VkInstance instance;
        Check(vkCreateInstance(&instanceInfo, &instance), "vkCreateInstance");
        _instance = instance;
        LoadInstance(instance);

        ulong surfaceHandle = 0;
        if (!SDL_Vulkan_CreateSurface(window, (VkInstance_T*)_instance.Handle, null,
                (VkSurfaceKHR_T**)&surfaceHandle))
            throw new InvalidOperationException($"SDL_Vulkan_CreateSurface: {SDL_GetError()}");
        _surface = new VkSurfaceKHR(surfaceHandle);

        PickPhysicalDevice();
        CreateDeviceAndQueue();
        BuildSwapchain(widthPx, heightPx);
    }

    /// <summary>The first device with a queue that can both draw and present to this
    /// surface, preferring a discrete one. A queue that can do only one of the two is no
    /// use here and the separate-queue arrangement is not worth its complexity.</summary>
    /// <summary>Whether the loader offers an instance extension, asked before the instance
    /// exists — which is the only time it can be asked and the only time it matters.</summary>
    private static bool HasInstanceExtension(string name)
    {
        uint count = 0;
        if (vkEnumerateInstanceExtensionProperties((byte*)null, &count, null) != VkResult.Success || count == 0)
            return false;

        var properties = stackalloc VkExtensionProperties[(int)count];
        if (vkEnumerateInstanceExtensionProperties((byte*)null, &count, properties) != VkResult.Success)
            return false;

        for (uint i = 0; i < count; i++)
        {
            var have = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)properties[i].extensionName);
            if (string.Equals(have, name, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// Re-chooses the surface format for the display the window is on now, rebuilding the
    /// swapchain only when the answer actually changed.
    ///
    /// <para>Dragging a window from an HDR monitor to an SDR one has to change the format,
    /// not merely the scale: scRGB on an SDR display is a different picture, and an 8-bit
    /// sRGB swapchain cannot carry HDR. The white level moves with it, because it is a
    /// property of the display and not of the machine.</para>
    /// </summary>
    public void DisplayChanged(bool isHdr, float sdrWhiteScale)
    {
        DisplayIsHdr = isHdr;
        SdrWhiteScale = sdrWhiteScale;

        // Against what the choice would actually produce, not against what was wanted.
        //
        // Comparing WantsWideColor with "is the format wide" says rebuild for ever on a
        // surface that has no wide format to give: X11 offers only SRGB_NONLINEAR, so with
        // HULTUI_VULKAN_F16=1 every display event decided it was still owed scRGB and rebuilt
        // the same 8-bit swapchain — three times at start-up, and resetting the capture
        // counter each time. Asking the surface costs one query and is the honest question.
        bool rebuilding = !_swapchain.IsNull && ChooseFormat(out _, trace: false) != _format;
        StartupTrace.Mark($"vulkan display changed: hdr={isHdr} whiteScale={sdrWhiteScale:0.##} " +
            $"peak={(DisplayPeakNits > 0f ? $"{DisplayPeakNits:0}" : "unknown")} " +
            (rebuilding ? $"rebuilding for {(WantsWideColor ? "scRGB" : "sRGB")}" : "format unchanged"));
        if (rebuilding) RecreateSwapchain(_widthPx, _heightPx);
    }

    private void PickPhysicalDevice()
    {
        uint count = 0;
        Check(EnumeratePhysicalDevices(_instance, &count, null), "vkEnumeratePhysicalDevices");
        if (count == 0) throw new InvalidOperationException("no Vulkan physical device");
        var devices = stackalloc VkPhysicalDevice[(int)count];
        Check(EnumeratePhysicalDevices(_instance, &count, devices), "vkEnumeratePhysicalDevices");

        VkPhysicalDevice best = default;
        uint bestFamily = 0;
        bool bestDiscrete = false;

        for (int i = 0; i < count; i++)
        {
            var candidate = devices[i];
            if (!TryFindPresentQueue(candidate, out uint family)) continue;

            var properties = new VkPhysicalDeviceProperties();
            GetPhysicalDeviceProperties(candidate, &properties);
            bool discrete = properties.deviceType == VkPhysicalDeviceType.DiscreteGpu;

            if (best.IsNull || (discrete && !bestDiscrete))
            {
                best = candidate;
                bestFamily = family;
                bestDiscrete = discrete;
                DeviceName = Marshal.PtrToStringUTF8((nint)properties.deviceName) ?? "(unnamed)";
            }
        }

        if (best.IsNull) throw new InvalidOperationException("no Vulkan device can present to this window");
        _physicalDevice = best;
        _queueFamily = bestFamily;
    }

    private bool TryFindPresentQueue(VkPhysicalDevice device, out uint family)
    {
        family = 0;
        uint count = 0;
        GetQueueFamilyProperties(device, &count, null);
        if (count == 0) return false;
        var families = stackalloc VkQueueFamilyProperties[(int)count];
        GetQueueFamilyProperties(device, &count, families);

        for (uint i = 0; i < count; i++)
        {
            if ((families[i].queueFlags & VkQueueFlags.Graphics) == 0) continue;
            uint supported = 0;
            if (GetSurfaceSupport(device, i, _surface, &supported) != VkResult.Success) continue;
            if (supported == 0) continue;
            family = i;
            return true;
        }
        return false;
    }

    private void CreateDeviceAndQueue()
    {
        float priority = 1f;
        var queueInfo = new VkDeviceQueueCreateInfo
        {
            queueFamilyIndex = _queueFamily,
            queueCount = 1,
            pQueuePriorities = &priority,
        };

        // Everything the driver will give us, so that a video can later share an image with
        // GL without the device having to be rebuilt — which it cannot be.
        var wanted = new List<string> { "VK_KHR_swapchain" };
        var missing = InteropDeviceExtensions.Where(name => !HasDeviceExtension(name)).ToArray();
        _interopExtensionsEnabled = missing.Length == 0;
        if (_interopExtensionsEnabled) wanted.AddRange(InteropDeviceExtensions);
        else StartupTrace.Mark($"vulkan interop unavailable: no {string.Join(", ", missing)}");

        var noSemaphore = SemaphoreDeviceExtensions.Where(name => !HasDeviceExtension(name)).ToArray();
        _semaphoreExtensionsEnabled = _interopExtensionsEnabled && noSemaphore.Length == 0;
        if (_semaphoreExtensionsEnabled) wanted.AddRange(SemaphoreDeviceExtensions);
        else if (_interopExtensionsEnabled)
            StartupTrace.Mark($"vulkan interop will use glFinish: no {string.Join(", ", noSemaphore)}");
        // A swapchain that may outsize its window. Needs the instance half (asked for during
        // Init), the device extension, AND its feature bit turned on explicitly — an extension
        // whose feature is left false is enabled in name only.
        string? maintenanceExtension =
            HasDeviceExtension("VK_KHR_swapchain_maintenance1") ? "VK_KHR_swapchain_maintenance1"
            : HasDeviceExtension("VK_EXT_swapchain_maintenance1") ? "VK_EXT_swapchain_maintenance1"
            : null;
        // Opt-in, not opt-out: see FixedExtentOverride for why the default favours video.
        bool wantMaintenance = _surfaceMaintenanceEnabled && maintenanceExtension is not null
            && FixedExtentOverride == true;
        if (wantMaintenance) wanted.Add(maintenanceExtension!);
        EnabledDeviceExtensions = [.. wanted];

        var maintenanceFeature = new VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR
        {
            swapchainMaintenance1 = true,
        };

        using var extensions = new VkStringArray(EnabledDeviceExtensions);
        var deviceInfo = new VkDeviceCreateInfo
        {
            pNext = wantMaintenance ? &maintenanceFeature : null,
            queueCreateInfoCount = 1,
            pQueueCreateInfos = &queueInfo,
            enabledExtensionCount = extensions.Length,
            ppEnabledExtensionNames = extensions,
        };
        VkDevice device;
        Check(CreateDevice(_physicalDevice, &deviceInfo, 0, &device), "vkCreateDevice");
        _device = device;
        LoadDevice(device);

        VkQueue queue;
        GetDeviceQueue(_device, _queueFamily, 0, &queue);
        _queue = queue;

        // Asked once, after the device: the answer cannot change for this surface, and the
        // swapchain build needs it every time.
        _fixedExtentAvailable = wantMaintenance && SurfaceOffersOneToOne();

        // Skia reads this to build its caps. Queried after the device exists and kept in
        // unmanaged memory because Skia holds the pointer rather than copying.
        _features2 = System.Runtime.InteropServices.Marshal.AllocHGlobal(
            sizeof(VkPhysicalDeviceFeatures2));
        var features = (VkPhysicalDeviceFeatures2*)_features2;
        *features = new VkPhysicalDeviceFeatures2 { sType = VkStructureType.PhysicalDeviceFeatures2 };
        GetPhysicalDeviceFeatures2(_physicalDevice, features);

        var poolInfo = new VkCommandPoolCreateInfo
        {
            queueFamilyIndex = _queueFamily,
            flags = VkCommandPoolCreateFlags.ResetCommandBuffer,
        };
        VkCommandPool pool;
        Check(CreateCommandPool(_device, &poolInfo, 0, &pool), "vkCreateCommandPool");
        _pool = pool;

        var fenceInfo = new VkFenceCreateInfo();
        VkFence fence;
        Check(CreateFence(_device, &fenceInfo, 0, &fence), "vkCreateFence");
        _acquireFence = fence;
    }

    /// <summary>
    /// How much bigger than the window the swapchain is allowed to be allocated.
    ///
    /// <para>256 is a compromise, and both ends of it were measured. Smaller buckets rebuild more
    /// often, which is the cost being removed; larger ones waste memory, which is the other thing
    /// this renderer is already criticised for. At 1560x1140 in half-float, one bucket in each
    /// dimension is about 12 MB against a 42 MB swapchain, and it turns 122 rebuilds across a
    /// drag into a handful.</para>
    /// </summary>
    private const uint ExtentBucket = 256;

    /// <summary>
    /// Above this, a reported extent limit is treated as "no limit" rather than as a number.
    /// This driver answers 0xFFFFFFFE for <c>maxScaledImageExtent</c>, and arithmetic on that is
    /// how a clamp turns into an overflow.
    /// </summary>
    private const uint SaneExtentCeiling = 1u << 20;

    private static uint RoundUpToBucket(uint value) =>
        value == 0 ? 0 : ((value + ExtentBucket - 1) / ExtentBucket) * ExtentBucket;

    private void BuildSwapchain(int widthPx, int heightPx)
    {
        var capabilities = new VkSurfaceCapabilitiesKHR();
        Check(GetSurfaceCapabilities(_physicalDevice, _surface, &capabilities), "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");

        // currentExtent of 0xFFFFFFFF means "you choose", which is what Wayland says; every
        // other case is the compositor telling us the size and disagreeing with it fails.
        var extent = capabilities.currentExtent.width == uint.MaxValue
            ? new VkExtent2D(
                Math.Clamp((uint)widthPx, capabilities.minImageExtent.width, capabilities.maxImageExtent.width),
                Math.Clamp((uint)heightPx, capabilities.minImageExtent.height, capabilities.maxImageExtent.height))
            : capabilities.currentExtent;

        if (extent.width == 0 || extent.height == 0)
        {
            StartupTrace.Mark($"vulkan swapchain SKIPPED (asked {widthPx}x{heightPx})");
            _swapchain = VkSwapchainKHR.Null;
            _images = [];
            return;
        }

        // The window's own size, before any over-allocation. Everything the renderer draws is
        // sized by this; the images may be bigger.
        int windowWidth = (int)extent.width;
        int windowHeight = (int)extent.height;

        // Over-allocate to a bucket, so a drag crosses a boundary occasionally instead of
        // reallocating three half-float images for every size it passes through. Rounding up
        // costs at most one bucket in each dimension — about 12 MB at this size — and buys
        // roughly 5 GB of allocation churn back over a drag.
        if (_fixedExtentAvailable)
        {
            uint bucketW = RoundUpToBucket(extent.width);
            uint bucketH = RoundUpToBucket(extent.height);

            // Clamped to the SCALED extent range, and deliberately not to
            // VkSurfaceCapabilitiesKHR.maxImageExtent. On Win32 that equals currentExtent, so
            // clamping to it pulled the bucket straight back to the window size and the whole
            // mechanism did nothing while appearing to work. With scaling chained, the spec
            // bounds imageExtent by min/maxScaledImageExtent instead — which is the point of
            // asking for the scaling capabilities at all.
            if (_minScaledWidth > 0) bucketW = Math.Max(bucketW, _minScaledWidth);
            if (_minScaledHeight > 0) bucketH = Math.Max(bucketH, _minScaledHeight);
            if (_maxScaledWidth is > 0 and < SaneExtentCeiling) bucketW = Math.Min(bucketW, _maxScaledWidth);
            if (_maxScaledHeight is > 0 and < SaneExtentCeiling) bucketH = Math.Min(bucketH, _maxScaledHeight);

            // A bucket smaller than the window would present a cropped picture, which is worse
            // than reallocating. Fall back to the exact size in that case.
            if (bucketW >= extent.width && bucketH >= extent.height)
                extent = new VkExtent2D(bucketW, bucketH);
        }

        _format = ChooseFormat(out var colorSpace);
        _colorSpace = colorSpace;

        // A surface that cannot offer these is one this renderer cannot draw on, and saying
        // so here beats a null surface three call frames later.
        uint usage = SwapchainUsage & (uint)capabilities.supportedUsageFlags;
        if ((usage & (UsageColorAttachment | UsageTransferSrc)) != (UsageColorAttachment | UsageTransferSrc))
            throw new InvalidOperationException(
                $"this surface supports usage 0x{(uint)capabilities.supportedUsageFlags:X} — " +
                "Skia needs at least COLOR_ATTACHMENT and TRANSFER_SRC");
        _usage = usage;

        uint imageCount = capabilities.minImageCount + 1;
        if (capabilities.maxImageCount > 0 && imageCount > capabilities.maxImageCount)
            imageCount = capabilities.maxImageCount;

        var old = _swapchain;

        // Only chained when the swapchain is actually allowed to differ from the window. Asking
        // for ONE_TO_ONE says "show the top-left of the image at its true size and leave the
        // rest"; without it the platform default is STRETCH, which is the resize artefact.
        var scalingInfo = new VkSwapchainPresentScalingCreateInfoKHR
        {
            sType = VkStructureType.SwapchainPresentScalingCreateInfoKHR,
            scalingBehavior = VkPresentScalingFlagsKHR.OneToOne,
            presentGravityX = VkPresentGravityFlagsKHR.Min,
            presentGravityY = VkPresentGravityFlagsKHR.Min,
        };

        var info = new VkSwapchainCreateInfoKHR
        {
            pNext = _fixedExtentAvailable ? &scalingInfo : null,
            surface = _surface,
            minImageCount = imageCount,
            imageFormat = _format,
            imageColorSpace = colorSpace,
            imageExtent = extent,
            imageArrayLayers = 1,
            // TransferDst as well as ColorAttachment: Skia wants it for the clear path, and
            // a swapchain image without it fails GRBackendRenderTarget validation.
            imageUsage = (VkImageUsageFlags)_usage,
            imageSharingMode = VkSharingMode.Exclusive,
            preTransform = capabilities.currentTransform,
            compositeAlpha = VkCompositeAlphaFlagsKHR.Opaque,
            presentMode = _presentMode = ChoosePresentMode(),
            clipped = true,
            oldSwapchain = old,
        };

        VkSwapchainKHR swapchain;
        Check(VulkanCommands.CreateSwapchain(_device, &info, 0, &swapchain), "vkCreateSwapchainKHR");
        _swapchain = swapchain;
        if (!old.IsNull) DestroySwapchain(_device, old, 0);

        uint got = 0;
        Check(GetSwapchainImages(_device, _swapchain, &got, null), "vkGetSwapchainImagesKHR");
        var images = new VkImage[got];
        fixed (VkImage* p = images)
            Check(GetSwapchainImages(_device, _swapchain, &got, p), "vkGetSwapchainImagesKHR");
        _images = images;

        CreatePerImageObjects();

        // Two sizes now, and keeping them apart is the whole discipline here: _widthPx is the
        // window, which is what gets drawn and presented, and _allocWidthPx is how big the
        // images really are. They are equal unless the swapchain was over-allocated.
        _widthPx = windowWidth;
        _heightPx = windowHeight;
        _allocWidthPx = (int)extent.width;
        _allocHeightPx = (int)extent.height;
        StartupTrace.Mark($"vulkan swapchain {_widthPx}x{_heightPx} x{_images.Length} " +
            $"{_format}/{_colorSpace}" +
            (_allocWidthPx != _widthPx || _allocHeightPx != _heightPx
                ? $" (allocated {_allocWidthPx}x{_allocHeightPx}, one-to-one)" : ""));
        ResetCapture();
    }

    /// <summary>
    /// One semaphore and one command buffer per swapchain image, replacing the set the last
    /// swapchain had. Both are indexed by image, so an image that is still being presented
    /// never has its own reused underneath it.
    /// </summary>
    private void CreatePerImageObjects()
    {
        DestroyPerImageObjects();

        int count = _images.Length;
        _renderFinished = new VkSemaphore[count];
        _presentCmd = new VkCommandBuffer[count];

        var semaphoreInfo = new VkSemaphoreCreateInfo();
        var allocate = new VkCommandBufferAllocateInfo
        {
            commandPool = _pool,
            level = VkCommandBufferLevel.Primary,
            commandBufferCount = 1,
        };

        for (int i = 0; i < count; i++)
        {
            VkSemaphore semaphore;
            Check(CreateSemaphore(_device, &semaphoreInfo, 0, &semaphore), "vkCreateSemaphore");
            _renderFinished[i] = semaphore;

            VkCommandBuffer cmd;
            Check(AllocateCommandBuffers(_device, &allocate, &cmd), "vkAllocateCommandBuffers");
            _presentCmd[i] = cmd;
        }
    }

    private void DestroyPerImageObjects()
    {
        // The caller has already waited the device idle; nothing here is in flight.
        foreach (var semaphore in _renderFinished)
            if (!semaphore.IsNull) DestroySemaphore(_device, semaphore, 0);
        _renderFinished = [];

        for (int i = 0; i < _presentCmd.Length; i++)
        {
            var cmd = _presentCmd[i];
            if (!cmd.IsNull) FreeCommandBuffers(_device, _pool, 1, &cmd);
        }
        _presentCmd = [];
    }

    /// <summary>
    /// How frames reach the screen, and deliberately not vsync.
    ///
    /// <para>The GL path sets <c>SDL_GL_SetSwapInterval(0)</c> and lets the framework decide
    /// when to draw: <c>Application</c> waits out the display interval itself, except when
    /// something asks to be unpaced — <c>FramePacing.Unlimited</c>, or the DevTools
    /// continuous-redraw mode. FIFO is vsync in the presentation engine, so it overrides
    /// that decision from underneath and pins the loop to the refresh rate however fast the
    /// framework wanted to go. Continuous redraw read exactly 120 on a 120 Hz display.</para>
    ///
    /// <para>MAILBOX is preferred, because it lets the renderer run ahead exactly as
    /// IMMEDIATE does — dropping frames nobody sees rather than pacing the loop — without
    /// tearing, and without the resize artefact below. FIFO is the last resort and the only
    /// mode an implementation must support.</para>
    ///
    /// <para><b>IMMEDIATE stretches during a resize, and that is why it is no longer
    /// preferred.</b> Measured on Windows: a bar drawn 300 device pixels wide was captured
    /// from the screen at 402 during a resize that grew the window by exactly that ratio, on
    /// 10 of 40 resizes. The compositor was scaling the previous swapchain's image into the
    /// larger window. GL never did it, and neither MAILBOX nor FIFO did it in the same test —
    /// so it is IMMEDIATE specifically, not Vulkan, and not frame timing: resize frames
    /// arrive every 26 ms on GL and 30 on Vulkan, near enough the same.</para>
    /// </summary>
    private VkPresentModeKHR ChoosePresentMode(bool trace = true)
    {
        uint count = 0;
        if (GetSurfacePresentModes(_physicalDevice, _surface, &count, null) != VkResult.Success || count == 0)
            return VkPresentModeKHR.Fifo;

        var modes = stackalloc VkPresentModeKHR[(int)count];
        if (GetSurfacePresentModes(_physicalDevice, _surface, &count, modes) != VkResult.Success)
            return VkPresentModeKHR.Fifo;

        bool immediate = false, mailbox = false;
        for (uint i = 0; i < count; i++)
        {
            if (modes[i] == VkPresentModeKHR.Immediate) immediate = true;
            if (modes[i] == VkPresentModeKHR.Mailbox) mailbox = true;
        }

        // An override only counts if the surface actually offers it; asking for a mode the
        // implementation does not support is undefined, and silently getting a different
        // one than the trace claims would make the comparison this exists for worthless.
        if (PresentModeOverride is { } wanted)
        {
            bool offered = false;
            for (uint i = 0; i < count; i++) if (modes[i] == wanted) offered = true;
            if (offered)
            {
                if (trace) StartupTrace.Mark($"vulkan present mode {wanted} (forced)");
                return wanted;
            }
            if (trace) StartupTrace.Mark($"vulkan present mode {wanted} not offered, choosing instead");
        }

        var chosen = mailbox ? VkPresentModeKHR.Mailbox
            : immediate ? VkPresentModeKHR.Immediate
            : VkPresentModeKHR.Fifo;
        if (trace) StartupTrace.Mark($"vulkan present mode {chosen}");
        return chosen;
    }

    /// <summary>
    /// Whether this surface will present a swapchain larger than the window without scaling it.
    ///
    /// <para>Asked <b>per present mode</b>, which is the whole reason the chained query exists:
    /// the scaling a surface supports is a property of how it presents, so the answer for
    /// MAILBOX says nothing about FIFO. It is asked for the mode actually chosen.</para>
    ///
    /// <para>ONE_TO_ONE plus MIN gravity on both axes is the combination that means "show the
    /// top-left corner of the image at its true size". The default is
    /// <c>PRESENT_SCALING_STRETCH</c>, which is the resize artefact this is meant to remove,
    /// named in the specification.</para>
    /// </summary>
    private bool SurfaceOffersOneToOne()
    {
        if (GetSurfaceCapabilities2 is null) return false;

        var modeInfo = new VkSurfacePresentModeKHR
        {
            sType = VkStructureType.SurfacePresentModeKHR,
            presentMode = ChoosePresentMode(trace: false),
        };
        var surfaceInfo = new VkPhysicalDeviceSurfaceInfo2KHR
        {
            sType = VkStructureType.PhysicalDeviceSurfaceInfo2KHR,
            pNext = &modeInfo,
            surface = _surface,
        };
        var scaling = new VkSurfacePresentScalingCapabilitiesKHR
        {
            sType = VkStructureType.SurfacePresentScalingCapabilitiesKHR,
        };
        var caps = new VkSurfaceCapabilities2KHR
        {
            sType = VkStructureType.SurfaceCapabilities2KHR,
            pNext = &scaling,
        };
        if (GetSurfaceCapabilities2(_physicalDevice, &surfaceInfo, &caps) != VkResult.Success)
            return false;

        bool oneToOne = (scaling.supportedPresentScaling & VkPresentScalingFlagsKHR.OneToOne) != 0;
        bool gravity = (scaling.supportedPresentGravityX & VkPresentGravityFlagsKHR.Min) != 0
            && (scaling.supportedPresentGravityY & VkPresentGravityFlagsKHR.Min) != 0;

        // Unsigned, and this driver answers 0xFFFFFFFE — "no limit worth naming" rather than a
        // real bound, which read as -2 the first time it was printed as a signed int. Anything
        // past a sane ceiling is treated as unbounded instead of trusted arithmetically.
        _maxScaledWidth = scaling.maxScaledImageExtent.width;
        _maxScaledHeight = scaling.maxScaledImageExtent.height;
        _minScaledWidth = scaling.minScaledImageExtent.width;
        _minScaledHeight = scaling.minScaledImageExtent.height;

        StartupTrace.Mark($"vulkan present scaling: oneToOne={oneToOne} gravityMin={gravity} " +
            $"scaledExtent={scaling.minScaledImageExtent.width}x{scaling.minScaledImageExtent.height}" +
            $"..{_maxScaledWidth}x{_maxScaledHeight}" +
            (oneToOne && gravity ? " — fixed extent available" : " — rebuilding per resize"));
        return oneToOne && gravity;
    }

    /// <summary>The largest extent this surface will present un-scaled. A swapchain may not be
    /// over-allocated past it, however much slack would be convenient.</summary>
    private uint _maxScaledWidth;
    private uint _maxScaledHeight;
    private uint _minScaledWidth;
    private uint _minScaledHeight;

    /// <summary>
    /// A plain 8-bit BGRA or RGBA format with the sRGB-nonlinear colour space — phase 1 is
    /// SDR and the aim is to match the GL path exactly. The <c>_SRGB</c> variants are
    /// deliberately not preferred: they would have the hardware do the encode, which is a
    /// different answer from the one the GL surface gives, and telling the two apart is the
    /// whole test for this phase. Phase 3 is where this function learns about HDR.
    /// </summary>
    /// <param name="colorSpace">The colour space that goes with the chosen format.</param>
    /// <param name="trace">False when the answer is only being compared against the current
    /// format rather than acted on, so that asking does not log. Every display event asks.</param>
    private VkFormat ChooseFormat(out VkColorSpaceKHR colorSpace, bool trace = true)
    {
        colorSpace = VkColorSpaceKHR.SrgbNonLinear;

        uint count = 0;
        Check(GetSurfaceFormats(_physicalDevice, _surface, &count, null), "vkGetPhysicalDeviceSurfaceFormatsKHR");
        if (count == 0) return VkFormat.B8G8R8A8Unorm;

        var formats = stackalloc VkSurfaceFormatKHR[(int)count];
        Check(GetSurfaceFormats(_physicalDevice, _surface, &count, formats), "vkGetPhysicalDeviceSurfaceFormatsKHR");

        // Half-float, linear light, 1.0 being SDR white — scRGB. Every colour the framework
        // has is 8-bit sRGB, so this is only correct because the renderer now tells Skia what
        // the destination is and lets it convert; before phase 2 it would have washed the
        // whole UI out. Opt-in while that claim is only proven on one machine.
        if (WantsWideColor && !_colorSpaceExtensionEnabled)
        {
            // Drivers list EXTENDED_SRGB_LINEAR regardless, so taking it from the list here
            // would be invalid usage that happens to work on the machine it was written on.
            if (trace) StartupTrace.Mark("vulkan wide colour asked for, but the instance has no VK_EXT_swapchain_colorspace");
        }
        else if (WantsWideColor)
        {
            for (uint i = 0; i < count; i++)
            {
                if (formats[i].format != VkFormat.R16G16B16A16Sfloat) continue;
                if (formats[i].colorSpace != VkColorSpaceKHR.ExtendedSrgbLinearEXT) continue;
                colorSpace = formats[i].colorSpace;
                return formats[i].format;
            }
            if (trace) StartupTrace.Mark("vulkan wide colour asked for, surface has none");
        }

        for (uint i = 0; i < count; i++)
        {
            if (formats[i].colorSpace != VkColorSpaceKHR.SrgbNonLinear) continue;
            if (formats[i].format is VkFormat.B8G8R8A8Unorm or VkFormat.R8G8B8A8Unorm)
            {
                colorSpace = formats[i].colorSpace;
                return formats[i].format;
            }
        }

        colorSpace = formats[0].colorSpace;
        return formats[0].format;
    }

    public VulkanFrameTarget? AcquireNextImage()
    {
        if (_disposed || _swapchain.IsNull || _images.Length == 0) return null;
        if (_haveAcquired) return Current();

        uint index = 0;
        var fence = _acquireFence;
        var result = VulkanCommands.AcquireNextImage(_device, _swapchain, NoTimeout, VkSemaphore.Null, fence, &index);

        if (result is VkResult.ErrorOutOfDateKHR)
        {
            // The window changed under us. Rebuilding here rather than reporting an error
            // keeps the caller's "no frame this time" contract simple. Forced, because the
            // driver has already said these images are unusable — "the window still fits" is
            // not an argument against that, and honouring it would hang the window.
            Rebuild(_widthPx, _heightPx, force: true);
            return null;
        }
        if (result is not (VkResult.Success or VkResult.SuboptimalKHR)) return null;

        // The fence is how this waits for the image without a semaphore to hand Skia — see
        // the note on the class.
        long before = AcquireStallTrace ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        Check(WaitForFences(_device, 1, &fence, 1, NoTimeout), "vkWaitForFences");
        Check(ResetFences(_device, 1, &fence), "vkResetFences");
        if (AcquireStallTrace) RecordAcquireStall(before);

        _acquiredIndex = index;
        _haveAcquired = true;
        return Current();
    }

    /// <summary>
    /// <c>HULTUI_VULKAN_ACQUIRE_TRACE=1</c> reports what the acquire wait actually costs.
    ///
    /// <para>The class comment has said since phase 1 that this backend stalls the CPU once a
    /// frame. Half of that was true and is now gone — the queue drain after Skia submitted was
    /// replaced by a semaphore the present waits on. What is left is this fence, and whether it
    /// is worth replacing depends on a number nobody has taken.</para>
    /// </summary>
    private static readonly bool AcquireStallTrace =
        Environment.GetEnvironmentVariable("HULTUI_VULKAN_ACQUIRE_TRACE") == "1";

    private long _stallTicks;
    private int _stallFrames;

    private void RecordAcquireStall(long before)
    {
        _stallTicks += System.Diagnostics.Stopwatch.GetTimestamp() - before;
        if (++_stallFrames < 120) return;

        double ms = _stallTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Console.Error.WriteLine($"[vulkan] acquire wait: {ms / _stallFrames:0.0000} ms a frame " +
            $"over {_stallFrames} frames ({ms:0.0} ms total)");
        _stallTicks = 0;
        _stallFrames = 0;
    }

    private VulkanFrameTarget Current() => new(
        _images[_acquiredIndex].Handle,
        (uint)_format,
        // Undefined every frame: the contents are about to be overwritten, and claiming the
        // present layout would have Skia skip a transition it still needs to make.
        Layout: 0,
        _widthPx, _heightPx, (int)_acquiredIndex, _usage, (uint)_colorSpace);

    public void PresentAcquiredImage()
    {
        if (!_haveAcquired || _swapchain.IsNull) return;

        // The capture reads the image on the CPU, so it is the one thing here that still has
        // to wait for the GPU. Diagnostic only, and off unless asked for.
        CaptureIfAsked(_images[_acquiredIndex]);

        // Skia leaves the image as a colour attachment; the presentation engine requires
        // PRESENT_SRC_KHR, and there is no binding call to ask Skia for the transition.
        //
        // No queue drain before this any more. The barrier's source stage is
        // COLOR_ATTACHMENT_OUTPUT, which is an execution dependency against everything Skia
        // submitted earlier on the same queue — ordering that Vulkan already guarantees and
        // that vkQueueWaitIdle was paying for twice.
        var done = TransitionToPresent(_images[_acquiredIndex], _acquiredIndex);

        uint index = _acquiredIndex;
        var swapchain = _swapchain;
        var present = new VkPresentInfoKHR
        {
            // …and this is what replaces the drain: presentation waits for that submit to
            // signal rather than for the whole queue to fall idle. Without a wait semaphore
            // a present is not ordered against prior queue work at all.
            waitSemaphoreCount = 1,
            pWaitSemaphores = &done,
            swapchainCount = 1,
            pSwapchains = &swapchain,
            pImageIndices = &index,
        };
        var result = QueuePresent(_queue, &present);
        _haveAcquired = false;

        if (result is VkResult.ErrorOutOfDateKHR or VkResult.SuboptimalKHR)
            Rebuild(_widthPx, _heightPx, force: true);
    }

    /// <summary>Records and submits the transition to PRESENT_SRC_KHR, returning the
    /// semaphore it signals when that work is done.</summary>
    private VkSemaphore TransitionToPresent(VkImage image, uint imageIndex)
    {
        var cmd = _presentCmd[imageIndex];
        var begin = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        Check(BeginCommandBuffer(cmd, &begin), "vkBeginCommandBuffer");

        var barrier = new VkImageMemoryBarrier
        {
            sType = VkStructureType.ImageMemoryBarrier,
            srcAccessMask = VkAccessFlags.ColorAttachmentWrite,
            dstAccessMask = VkAccessFlags.None,
            oldLayout = VkImageLayout.ColorAttachmentOptimal,
            newLayout = VkImageLayout.PresentSrcKHR,
            srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            image = image,
            subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1),
        };
        CmdPipelineBarrier(cmd, VkPipelineStageFlags.ColorAttachmentOutput,
            VkPipelineStageFlags.BottomOfPipe, VkDependencyFlags.None, 0, 0, 0, 0, 1, &barrier);

        // The last submit of the frame, so the shared image goes back to GL here if it was drawn.
        var release = ReleaseInteropImage(cmd);

        Check(EndCommandBuffer(cmd), "vkEndCommandBuffer");

        var signals = stackalloc VkSemaphore[2];
        signals[0] = _renderFinished[imageIndex];
        if (release is { } r) signals[1] = r;
        var submit = new VkSubmitInfo
        {
            commandBufferCount = 1,
            pCommandBuffers = &cmd,
            signalSemaphoreCount = release is null ? 1u : 2u,
            pSignalSemaphores = signals,
        };
        Check(QueueSubmit(_queue, 1, &submit, VkFence.Null), "vkQueueSubmit");
        return signals[0];
    }

    /// <summary>
    /// Resizes the presented area, rebuilding the swapchain only when it has to.
    ///
    /// <para><b>The whole point of the over-allocation is this early return.</b> When the images
    /// are already big enough for the new window size, nothing is reallocated: the window size is
    /// simply recorded, and the next frame draws into the same images and presents a smaller
    /// top-left region of them. A drag that only shrinks the window never rebuilds at all, and
    /// one that grows rebuilds when it crosses a bucket.</para>
    ///
    /// <para>No device wait either, because nothing is being destroyed — that wait was there to
    /// make it safe to throw the old images away.</para>
    /// </summary>
    public void RecreateSwapchain(int widthPx, int heightPx) =>
        Rebuild(widthPx, heightPx, force: false);

    /// <param name="force">Set when the driver has told us the swapchain is out of date, which
    /// no amount of "the window still fits" may override — the images have to be replaced
    /// whatever their size. Without this, an OUT_OF_DATE on the over-allocated path would be
    /// answered by doing nothing, acquire would keep failing, and the window would never draw
    /// again.</param>
    private void Rebuild(int widthPx, int heightPx, bool force)
    {
        if (_disposed) return;

        // _fixedExtentAvailable is not optional here. Without ONE_TO_ONE the presented extent
        // must equal the window's, so skipping a rebuild because the window merely got SMALLER
        // would leave an oversized swapchain being scaled — the artefact, reintroduced. Leaving
        // this check out also made HULTUI_VULKAN_FIXED_EXTENT=0 a dishonest "before": it kept
        // the skipping and lost only the scaling that justifies it.
        if (!force && _fixedExtentAvailable && !_swapchain.IsNull && widthPx > 0 && heightPx > 0
            && widthPx <= _allocWidthPx && heightPx <= _allocHeightPx
            && ChooseFormat(out _, trace: false) == _format)
        {
            _widthPx = widthPx;
            _heightPx = heightPx;
            return;
        }

        Check(DeviceWaitIdle(_device), "vkDeviceWaitIdle");
        _haveAcquired = false;
        BuildSwapchain(widthPx, heightPx);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // The pointer checks matter only after an Init that threw inside LoadDevice; everything
        // else below was created after LoadDevice finished, so its own null checks cover it.
        if (!_device.IsNull && DeviceWaitIdle is not null && DestroyDevice is not null)
        {
            DeviceWaitIdle(_device);
            // Before the pool and the device: the shared image is bound to both, and the GL
            // context that also names it outlives neither.
            DisposeInterop();
            DestroyPerImageObjects();
            if (!_acquireFence.IsNull) DestroyFence(_device, _acquireFence, 0);
            if (!_pool.IsNull) DestroyCommandPool(_device, _pool, 0);
            if (!_swapchain.IsNull) DestroySwapchain(_device, _swapchain, 0);
            DestroyDevice(_device, 0);
        }
        // Guarded because Dispose also cleans up after an Init that threw, which can be before
        // LoadInstance resolved these.
        if (!_surface.IsNull && DestroySurface is not null) DestroySurface(_instance, _surface, 0);
        if (!_instance.IsNull && DestroyInstance is not null) DestroyInstance(_instance, 0);
        if (_features2 != 0) System.Runtime.InteropServices.Marshal.FreeHGlobal(_features2);
    }

    private static void Check(VkResult result, string what)
    {
        if (result != VkResult.Success)
            throw new InvalidOperationException($"{what}: {result}");
    }
}

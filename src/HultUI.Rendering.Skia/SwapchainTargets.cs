namespace HultUI.Rendering.Skia;

/// <summary>
/// What a render target kept over one swapchain image was made for. A frame whose image, size,
/// format, usage or colour space differs gets a new one: the same index after a rebuild can be a
/// different image, and a window resized inside an over-allocated swapchain draws the same
/// image at another size.
/// </summary>
/// <param name="WhiteScale">The white scale folded into the colour space, for
/// <see cref="ScRgbFramePath.ScaledSpace"/> only; zero on every other path, where the target's
/// colour space does not depend on it.</param>
internal readonly record struct SwapchainTargetKey(
    ulong Image, int Width, int Height, uint Format, uint Usage, uint ColorSpace,
    ScRgbFramePath Path, float WhiteScale);

/// <summary>
/// How one frame uses the swapchain image: through a render target kept for it, and when the host
/// moves the image back to the layout that target believes it is in — or through one made for
/// this frame only, declared in the layout the image has, as every frame did before 2026-10-11.
/// </summary>
internal enum SwapchainTargetUse
{
    /// <summary>A render target for this frame only; no barrier from the host.</summary>
    ThisFrameOnly,

    /// <summary>The kept target, with the host's barrier asked for when the frame begins, before
    /// anything is drawn: the frame draws straight onto the swapchain image, and Skia may submit
    /// that work at any point. The host submits it with Skia's first submit, ahead of Skia's work.</summary>
    KeptTransitionAtBegin,

    /// <summary>The kept target, with the host's barrier asked for just before the offscreen is
    /// blitted to it: the frame is drawn into the offscreen, and the blit is the first of Skia's
    /// work on the swapchain image.</summary>
    KeptTransitionBeforeBlit,
}

/// <summary>The choice between the <see cref="SwapchainTargetUse"/>s, kept apart so it can be
/// tested.</summary>
internal static class SwapchainTargetUses
{
    /// <summary>
    /// <para><b>When the barrier goes.</b> A submit of the host's on the Vulkan queue just before
    /// mpv's GL draw of a video frame makes that draw slower on NVIDIA (610): Reel playing a
    /// 1080p24 clip, the frame's Render phase averaged 1.25–1.47 ms against 0.97–1.14 without the
    /// submit, in nine runs on both swapchain formats. Submitted after the GL draw it cost nothing
    /// (1.10–1.14). The host asked at the frame's beginning owes the barrier until Skia's first
    /// submit, which comes after the GL draw (<c>IVulkanSurfaceHost.TransitionAcquiredImage</c>),
    /// so a frame straight onto the swapchain keeps its target after a video frame too. Until
    /// 2026-10-26 the host submitted at once, and such a frame wrapped the image for itself: 2 KB,
    /// an image view and a framebuffer a video frame on an 8-bit swapchain. Through the offscreen
    /// the barrier is still asked for just before the blit, the first of Skia's work on the
    /// swapchain image, which keeps it after the GL draw with a host that submits at once.</para>
    /// </summary>
    /// <param name="hostKeeps">The host restores the layout and calls before the images go
    /// (<c>IVulkanSurfaceHost.SetSwapchainReleasing</c>).</param>
    /// <param name="throughOffscreen">This frame is drawn into the offscreen and blitted.</param>
    public static SwapchainTargetUse Choose(bool hostKeeps, bool throughOffscreen) =>
        !hostKeeps ? SwapchainTargetUse.ThisFrameOnly
        : throughOffscreen ? SwapchainTargetUse.KeptTransitionBeforeBlit
        : SwapchainTargetUse.KeptTransitionAtBegin;
}

/// <summary>
/// One kept object per swapchain image, by index, for as long as the swapchain lives — the
/// bookkeeping half of <see cref="SkiaVulkanRenderSurface"/>'s kept render targets, apart from
/// Skia so it can be tested without a GPU. An object made for another key is disposed when its
/// slot is filled again; <see cref="Clear"/> disposes them all, and is what the surface does
/// before the host destroys the images.
/// </summary>
internal sealed class SwapchainTargets<T> where T : class, IDisposable
{
    private T?[] _slots = [];
    private SwapchainTargetKey[] _keys = [];

    /// <summary>How many slots hold an object.</summary>
    public int Count { get; private set; }

    /// <summary>The object kept for <paramref name="index"/>, if it was made for
    /// <paramref name="key"/>.</summary>
    public bool TryGet(int index, in SwapchainTargetKey key, out T value)
    {
        if ((uint)index < (uint)_slots.Length && _slots[index] is { } kept && _keys[index] == key)
        {
            value = kept;
            return true;
        }
        value = null!;
        return false;
    }

    /// <summary>Keeps <paramref name="value"/> for <paramref name="index"/>, disposing what the slot
    /// held before.</summary>
    public void Put(int index, in SwapchainTargetKey key, T value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= _slots.Length)
        {
            Array.Resize(ref _slots, index + 1);
            Array.Resize(ref _keys, index + 1);
        }
        if (_slots[index] is { } old) { old.Dispose(); Count--; }
        _slots[index] = value;
        _keys[index] = key;
        Count++;
    }

    /// <summary>Disposes every kept object.</summary>
    public void Clear()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i]?.Dispose();
            _slots[i] = null;
        }
        Count = 0;
    }
}

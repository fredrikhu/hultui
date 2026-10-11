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

    /// <summary>The kept target, with the host's barrier submitted at once, before anything is
    /// drawn: the frame draws straight onto the swapchain image, and Skia may submit that work
    /// at any point.</summary>
    KeptTransitionNow,

    /// <summary>The kept target, with the host's barrier submitted just before the offscreen is
    /// blitted to it: the frame is drawn into the offscreen, and the blit is the first of Skia's
    /// work on the swapchain image.</summary>
    KeptTransitionBeforeBlit,
}

/// <summary>The choice between the <see cref="SwapchainTargetUse"/>s, kept apart so it can be
/// tested.</summary>
internal static class SwapchainTargetUses
{
    /// <summary>
    /// <para><b>Why the barrier is not always submitted at once.</b> A submit of the host's on
    /// the Vulkan queue just before mpv's GL draw of a video frame makes that draw slower on
    /// NVIDIA (610): Reel playing a 1080p24 clip, the frame's Render phase averaged 1.25–1.47 ms
    /// against 0.97–1.14 without the submit, in nine runs on both swapchain formats, while a frame
    /// without video got faster. Submitted after the GL draw, just before the blit, it cost
    /// nothing (1.10–1.14). That is safe only where nothing of Skia's on the swapchain image can be
    /// submitted earlier, which is the offscreen path. Straight onto the swapchain, a frame after
    /// one that drew a GPU source (a video, a web page) wraps the image for itself instead, as
    /// before: no barrier, and the 2 KB a frame that keeping the target saves.</para>
    /// </summary>
    /// <param name="hostKeeps">The host restores the layout and calls before the images go
    /// (<c>IVulkanSurfaceHost.SetSwapchainReleasing</c>).</param>
    /// <param name="throughOffscreen">This frame is drawn into the offscreen and blitted.</param>
    /// <param name="gpuSourceLastFrame">The frame before lent a GPU source its image.</param>
    public static SwapchainTargetUse Choose(bool hostKeeps, bool throughOffscreen, bool gpuSourceLastFrame) =>
        !hostKeeps ? SwapchainTargetUse.ThisFrameOnly
        : throughOffscreen ? SwapchainTargetUse.KeptTransitionBeforeBlit
        : gpuSourceLastFrame ? SwapchainTargetUse.ThisFrameOnly
        : SwapchainTargetUse.KeptTransitionNow;
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

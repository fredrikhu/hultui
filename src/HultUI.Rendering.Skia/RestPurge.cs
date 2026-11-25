namespace HultUI.Rendering.Skia;

/// <summary>
/// When a Vulkan surface frees what its last frame did not draw with, once the window has drawn
/// nothing for a while; kept apart from <see cref="SkiaVulkanRenderSurface"/> so it can be tested.
///
/// <para><b>What is freed.</b> Skia keeps the texture of every picture it has drawn, alive and off
/// screen or not, until its 256 MB budget pushes it out, and the buffers it staged uploads through
/// until a scratch purge. Aperture back at Home after a walk down its Films grid kept 59.91 MB in
/// 660 resources, the grid's posters among them; the purge leaves 11.78 MB, and the process's DXGI
/// local memory goes 91.3 → 51.3–55.3 MB. The purge lets go of what became unused before the
/// last frame began (the textures of pictures not drawn since, uploaded again from their pixels in
/// the frame they come back), then of every spare: the upload staging becomes one only when Skia
/// sees the GPU finish, which it does at the purge itself, so the first purge never frees it.</para>
///
/// <para><b>When.</b> <see cref="Delay"/> after the last frame began to be presented. Every frame
/// puts it off, so a window that keeps drawing (a video, an animation) never purges, and one at rest
/// purges once, from a deadline of the frame loop's own. Long enough that reading a page is not
/// paid for on Back, though that is cheap: the pictures of the page left come back in Back's frame,
/// one upload each: +0.9 ms of the frame's render for the posters of Aperture's grid (6.8 MB), +1.4 ms
/// for an item page (23.6 MB), and no blank frame. Netflix finds its members choose in 60 to 90 seconds,
/// having looked at perhaps three titles in detail (Gomez-Uribe and Hunt, 2015): 30 s covers
/// such a look, and an app left at rest gives the memory back half a minute in.</para>
/// </summary>
internal sealed class RestPurge
{
    /// <summary>How long a window draws nothing before the purge.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(30);

    private static readonly long MsTicks = System.Diagnostics.Stopwatch.Frequency / 1000;

    private readonly long _delayTicks;

    /// <summary>When the last frame began to be presented; 0 with no purge owed.</summary>
    private long _presentedAt;

    /// <param name="delay">How long a window draws nothing before the purge: <see cref="Delay"/>,
    /// or less for a probe.</param>
    public RestPurge(TimeSpan delay) =>
        _delayTicks = (long)(delay.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);

    /// <summary>A frame is about to be flushed, submitted and presented: the purge is owed a rest
    /// after it. What the frame draws with is let go only after its submit.</summary>
    /// <param name="now">A <see cref="System.Diagnostics.Stopwatch"/> timestamp.</param>
    public void Presenting(long now) => _presentedAt = now == 0 ? 1 : now;

    /// <summary>When the purge is due, as a <see cref="System.Diagnostics.Stopwatch"/> timestamp;
    /// <see cref="long.MaxValue"/> before the first frame and after the purge, until the next.</summary>
    public long DueAt => _presentedAt == 0 ? long.MaxValue : _presentedAt + _delayTicks;

    /// <summary>
    /// Whether the purge is due at <paramref name="now"/>; if so, it is no longer owed.
    /// </summary>
    /// <param name="now">A <see cref="System.Diagnostics.Stopwatch"/> timestamp.</param>
    /// <param name="msNotUsed">For Skia's <c>PurgeUnusedResources</c>: the milliseconds since the
    /// last frame began to be presented, rounded up, so the time purged up to is never after it and
    /// nothing that frame drew with goes.</param>
    public bool TakeDue(long now, out long msNotUsed)
    {
        msNotUsed = 0;
        if (now < DueAt) return false;
        msNotUsed = Math.Max(0, (now - _presentedAt + MsTicks - 1) / MsTicks);
        _presentedAt = 0;
        return true;
    }
}

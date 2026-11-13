namespace HultUI.Rendering.Skia;

/// <summary>
/// When a Vulkan surface frees what Skia kept for the sizes a window no longer has, kept apart
/// from <see cref="SkiaVulkanRenderSurface"/> so it can be tested.
///
/// <para><b>What is freed.</b> Skia gives a render target a stencil attachment of its size when a
/// draw needs one (a rounded clip, a path), and shares it between targets of that size by a unique
/// key. A keyed resource is not a spare, so the scratch purge never lets it go: Aperture kept one
/// per size it had drawn at, 9 stencils and 21.8 MB after 18 size changes through 9 sizes, 19.0 MB
/// of them for sizes no longer shown, and every maximize, restore, snap or monitor move added the
/// window's width × height bytes until the context's 256 MB budget.</para>
///
/// <para><b>When.</b> Once a size change has rested: <see cref="RestDelay"/> after the last change,
/// and only once a frame has been presented after the targets were last let go (the swapchain's,
/// at a rebuild for any reason; the half-float offscreen's, at the purge after it is replaced), so
/// the size shown has its own stencil. A purge owed then waits for that frame. Never inside a drag:
/// each step is a change and puts it off.</para>
///
/// <para><b>What goes, and what stays.</b> Skia purges what became unused before a time it is
/// given in whole milliseconds back from now. That time is put just after the targets were let go
/// (<see cref="TakeDue"/>): what was let go with them or before, and not used since, goes — the
/// old sizes' stencils, spares, and the textures of images alive but not drawn since, which are
/// uploaded again from their pixels if they come back. What the frames since drew with was in use
/// until after they were submitted, and stays; and never later than the last frame began to be
/// presented, so nothing that frame drew with goes, even when it was let go at once (a frame read
/// back waits for the GPU, and Skia may notice its work done within the millisecond).</para>
/// </summary>
internal sealed class SizeRestPurge
{
    /// <summary>How long a size must stay before the purge.</summary>
    public static readonly TimeSpan RestDelay = TimeSpan.FromSeconds(1);

    private static readonly long RestTicks = (long)(RestDelay.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
    private static readonly long MsTicks = System.Diagnostics.Stopwatch.Frequency / 1000;

    /// <summary>The last change, in <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
    private long _changedAt;

    /// <summary>When render targets were last let go; 0 with no purge owed.</summary>
    private long _releasedAt;

    /// <summary>When the last frame presented since then began to be presented; 0 with none.</summary>
    private long _presentedAt;

    /// <summary>Whether a purge is owed: targets were let go and not purged after.</summary>
    public bool Owed => _releasedAt != 0;

    /// <summary>The window's size changed: a purge owed waits a while longer.</summary>
    /// <param name="now">A <see cref="System.Diagnostics.Stopwatch"/> timestamp.</param>
    public void Changed(long now)
    {
        if (_releasedAt != 0) _changedAt = now;
    }

    /// <summary>Render targets were let go, with whatever Skia had attached to them: a purge is
    /// owed, after the next frame and a rest.</summary>
    /// <param name="now">A <see cref="System.Diagnostics.Stopwatch"/> timestamp, taken after they went.</param>
    public void Released(long now)
    {
        if (now == 0) now = 1;
        _changedAt = now;
        _releasedAt = now;
        _presentedAt = 0;
    }

    /// <summary>A frame is about to be flushed, submitted and presented. Read before its flush:
    /// what it draws with is let go only after its submit.</summary>
    /// <param name="now">A <see cref="System.Diagnostics.Stopwatch"/> timestamp.</param>
    public void Presenting(long now)
    {
        if (_releasedAt != 0) _presentedAt = now;
    }

    /// <summary>When the purge is due, as a <see cref="System.Diagnostics.Stopwatch"/> timestamp;
    /// <see cref="long.MaxValue"/> when none is owed, or none can be until a frame is presented.</summary>
    public long DueAt => _releasedAt == 0 || _presentedAt == 0 ? long.MaxValue : _changedAt + RestTicks;

    /// <summary>
    /// Whether the purge is due at <paramref name="now"/>; if so, it is no longer owed.
    /// </summary>
    /// <param name="now">A <see cref="System.Diagnostics.Stopwatch"/> timestamp.</param>
    /// <param name="msNotUsed">For Skia's <c>PurgeUnusedResources</c>. The milliseconds since the
    /// targets were let go, rounded down, so the time purged up to is at that moment or within a
    /// millisecond after it; and at least the milliseconds since the last frame began to be
    /// presented, rounded up, so that time is never after the frame.</param>
    public bool TakeDue(long now, out long msNotUsed)
    {
        msNotUsed = 0;
        if (now < DueAt) return false;
        long sinceRelease = (now - _releasedAt) / MsTicks;
        long sinceFrame = (now - _presentedAt + MsTicks - 1) / MsTicks;
        msNotUsed = Math.Max(0, Math.Max(sinceRelease, sinceFrame));
        _releasedAt = 0;
        _presentedAt = 0;
        return true;
    }
}

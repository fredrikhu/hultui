namespace HultUI.Platform.SDL3;

/// <summary>
/// When the acquired image's barrier back to the renderer's layout is submitted: once per acquire,
/// and as late as allowed. Kept apart from the Vulkan calls so the rules can be tested without a
/// GPU; <see cref="SdlVulkanHost"/> submits whenever a call here says so.
///
/// <para><b>Why as late as allowed.</b> A submit of the host's on the queue before mpv's GL draw of
/// a video frame makes that draw slower on NVIDIA (610): Reel playing a 1080p24 clip, the frame's
/// Render phase averaged 1.25–1.47 ms against 0.97–1.14 without it. The renderer asks for the
/// barrier when its frame begins, before any GL draw; the host owes it until the renderer's first
/// submit of the frame, which comes after the GL draw and carries it ahead of the renderer's own
/// work, or until the present if the renderer submitted nothing. Submitted at once instead, a frame
/// straight onto an 8-bit swapchain after one that drew a video had to wrap its image for itself:
/// 2 KB, an image view and a framebuffer a frame.</para>
/// </summary>
internal struct AcquireBarrier
{
    /// <summary>Asked for and not yet submitted.</summary>
    public bool Owed { get; private set; }

    /// <summary>Submitted for the image acquired now.</summary>
    public bool Submitted { get; private set; }

    /// <summary>A new image was acquired, or the one acquired went with its swapchain: nothing is
    /// asked for it yet.</summary>
    public void Reset()
    {
        Owed = false;
        Submitted = false;
    }

    /// <summary>The renderer asks for the barrier. Returns whether to submit it now: false when it
    /// was asked for already, or when <paramref name="canDefer"/> (the renderer's submits pass the
    /// host first) leaves it owed.</summary>
    public bool Ask(bool canDefer)
    {
        if (Owed || Submitted) return false;
        if (canDefer)
        {
            Owed = true;
            return false;
        }
        Submitted = true;
        return true;
    }

    /// <summary>Just before the renderer's submit, or at the present: whether an owed barrier goes
    /// now. True once per acquire at most.</summary>
    public bool TakeOwed()
    {
        if (!Owed) return false;
        Owed = false;
        Submitted = true;
        return true;
    }
}

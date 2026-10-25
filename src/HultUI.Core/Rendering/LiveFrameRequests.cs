namespace HultUI.Core.Rendering;

/// <summary>
/// A GPU source's request for a frame, answered from inside a modal loop the application's own
/// loop cannot run in: Win32's size/move loop, while a window is dragged by its edge or its title
/// bar, or held still in the middle of a drag.
///
/// <para>A video or a page asks for a repaint from a thread of its own, and the request is a post
/// (see <c>MediaPlayer</c>): the loop runs it on its next pass, and the frame it marks dirty takes
/// the newest picture. Inside the size/move loop the post waits for the loop to end, and the video
/// was drawn only when SDL's live-resize timer came round, about 64 times a second: a 120 fps clip
/// showed 57 frames and mpv dropped 48 over a 0.9 s move, 128 and 111 over a drag held still for
/// two seconds. So while its window is in that loop the platform sets a poster here, which posts the
/// window a message the loop itself dispatches, and the window answers it with a frame.</para>
/// </summary>
internal static class LiveFrameRequests
{
    private static Action? s_poster;
    private static int s_waiting;

    /// <summary>
    /// Set by the platform while its UI thread is inside such a loop, null when it leaves. The
    /// poster runs on the requesting thread, which may be one a frame waits for: it may only post
    /// (a <c>PostMessage</c>, never anything that waits; see <c>docs/THREADING.md</c>).
    /// </summary>
    internal static void SetPoster(Action? poster)
    {
        Volatile.Write(ref s_poster, poster);
        if (poster is null) Volatile.Write(ref s_waiting, 0);
    }

    /// <summary>
    /// A GPU source has a new frame. Any thread. Outside such a loop it does nothing, and the
    /// source's ordinary post brings the frame.
    /// </summary>
    internal static void Request()
    {
        if (Volatile.Read(ref s_poster) is not { } poster) return;
        Volatile.Write(ref s_waiting, 1);
        poster();
    }

    /// <summary>Whether a request is waiting for a frame, and clears it: the live frame that
    /// draws for it asks. UI thread.</summary>
    internal static bool TakeWaiting() => Interlocked.Exchange(ref s_waiting, 0) == 1;

    /// <summary>Whether requests are answered now: a poster is set.</summary>
    internal static bool Answering => Volatile.Read(ref s_poster) is not null;
}

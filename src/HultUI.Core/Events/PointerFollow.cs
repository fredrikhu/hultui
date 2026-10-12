using HultUI.Core.Elements;

namespace HultUI.Core.Events;

/// <summary>
/// A host whose frames work can be run before: the window, which runs it at the start of its next
/// frame, before style and layout, so that frame shows what it did.
/// </summary>
internal interface IPreFrameHost
{
    /// <summary>Runs <paramref name="callback"/> once, at the start of the next frame, and makes
    /// sure there is one. Asking again with the same callback before then runs it once.</summary>
    void RunBeforeNextFrame(Action callback);
}

/// <summary>
/// What a drag or a hover does with the pointer, applied once per frame that may be drawn, at the
/// pointer's latest position, rather than on every move: a splitter's pane size, a pan's offset,
/// a slider's value and the value under the pointer (plan 17 round 3; the user chose these four,
/// and every other pointer-move handler still gets every move). A 1,000 Hz mouse sends about
/// sixteen moves for each frame of a 60 Hz display, and each of them rebuilt the owner: sixteen
/// rebuilds for one frame drawn.
///
/// <para>The owner keeps the position each move brings and calls <see cref="Request"/>; the
/// element's window runs the apply at the start of its next frame, once however many moves came
/// in between. An element in no window (a unit test's) applies at once, as before.
/// <see cref="Flush"/> applies a waiting request now: a release must end the drag exactly where
/// the pointer did, and a press or a key must act after the last move, not before it.</para>
/// </summary>
internal sealed class PointerFollow
{
    private readonly Action _apply;
    private readonly Action _run;
    private bool _pending;

    public PointerFollow(Action apply)
    {
        _apply = apply;
        _run = Flush;
    }

    /// <summary>Whether a position waits to be applied.</summary>
    public bool IsPending => _pending;

    /// <summary>Applies the owner's latest position with the next frame of
    /// <paramref name="element"/>'s window, or now if it is in none.</summary>
    public void Request(Element element)
    {
        _pending = true;
        if (element.OverlayHost is IPreFrameHost host)
            host.RunBeforeNextFrame(_run);
        else
            Flush();
    }

    /// <summary>Applies a waiting position now; nothing when none waits.</summary>
    public void Flush()
    {
        if (!_pending) return;
        _pending = false;
        _apply();
    }

    /// <summary>Forgets a waiting position: the pointer has gone, and what it pointed at with it.</summary>
    public void Cancel() => _pending = false;
}

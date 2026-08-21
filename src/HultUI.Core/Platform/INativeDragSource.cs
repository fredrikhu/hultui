namespace HultUI.Core.Platform;

/// <summary>
/// What a drag is allowed to do, and what the drop target actually did with it. The
/// values match the OS drop-effect bits on every platform that has them, so a platform
/// implementation can cast rather than translate.
/// </summary>
[Flags]
public enum DragEffect
{
    /// <summary>Nothing happened — the drag was cancelled, or the target refused it.</summary>
    None = 0,

    /// <summary>The target took a copy; the source keeps its own.</summary>
    Copy = 1,

    /// <summary>The target took ownership; the source is expected to give its copy up.</summary>
    Move = 2,

    /// <summary>The target made a reference (a shortcut / symlink) rather than a copy.</summary>
    Link = 4,
}

/// <summary>
/// Optional platform capability: starting a drag that <i>leaves</i> this application, so
/// other programs (a file manager, a mail client, an editor) can accept the drop. The
/// receiving half needs nothing from this interface — OS drops already arrive as
/// <c>DragEnter</c>/<c>DragOver</c>/<c>Drop</c> events on any platform SDL delivers them for.
/// </summary>
/// <remarks>
/// <para>Discovered with a type test, the way <see cref="IWindowChrome"/> is: a platform that
/// cannot start a native drag simply does not implement it, and the framework's
/// <c>Window.DragFilesOut</c> reports <see cref="DragEffect.None"/> there.</para>
/// <para><b>The call is modal.</b> Every OS implements outbound drag as a nested loop that
/// owns the mouse until the button comes up — Win32 <c>DoDragDrop</c> is the canonical one —
/// so the call does not return until the drop lands or is cancelled, and the app renders no
/// frames meanwhile. It must therefore be made from the UI thread, while the button is still
/// down: from a <c>DragStart</c> handler, which is exactly when the framework has decided a
/// drag is under way.</para>
/// </remarks>
public interface INativeDragSource
{
    /// <summary>True when this platform can actually start one (Windows today). Lets an app
    /// hide or disable a drag affordance rather than offer one that does nothing.</summary>
    bool CanDragFiles { get; }

    /// <summary>
    /// Offer <paramref name="paths"/> to the rest of the desktop as a file drag, and block
    /// until it resolves. Returns what the target did — which is <see cref="DragEffect.None"/>
    /// for a cancelled drag, and never more than <paramref name="allowed"/> permitted.
    /// </summary>
    /// <remarks>A caller that permits <see cref="DragEffect.Move"/> is promising to delete
    /// its own copies when Move comes back, because the target will not have done so.</remarks>
    DragEffect DragFiles(IReadOnlyList<string> paths, DragEffect allowed);
}

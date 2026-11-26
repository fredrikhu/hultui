namespace HultUI.Core.Events;

/// <summary>
/// Which pointer the input being handled on this thread comes from (docs/design/POINTER-IDENTITY.md).
/// The window enters it around a touch, and every <see cref="PointerEvent"/> made meanwhile takes
/// its <see cref="PointerEvent.PointerId"/>, <see cref="PointerEvent.PointerType"/> and
/// <see cref="PointerEvent.IsPrimary"/> from here. So the paths a primary touch shares with the
/// mouse (hover, press, click, capture, the drags) stamp it without each of their seventeen event
/// sites knowing. Outside a scope it is the mouse.
/// </summary>
internal static class PointerContext
{
    [ThreadStatic] private static int t_id;
    [ThreadStatic] private static PointerType t_type;
    [ThreadStatic] private static bool t_secondary;
    [ThreadStatic] private static bool t_cancelling;

    /// <summary>The pointer's ID; <see cref="PointerEvent.MousePointerId"/> for the mouse.</summary>
    public static int Id => t_id == 0 ? PointerEvent.MousePointerId : t_id;

    public static PointerType Type => t_type;

    public static bool IsPrimary => !t_secondary;

    public static bool IsTouch => t_type == PointerType.Touch;

    /// <summary>The press is being cancelled, not released: the release path sends
    /// <see cref="PointerCancelEvent"/> where it would send <see cref="PointerUpEvent"/>, and
    /// synthesizes no click.</summary>
    public static bool Cancelling => t_cancelling;

    /// <summary>Enters a pointer for the input handled until the scope is disposed.</summary>
    public static Scope Enter(int id, PointerType type, bool primary, bool cancelling = false)
    {
        var saved = new Scope(t_id, t_type, t_secondary, t_cancelling);
        t_id = id;
        t_type = type;
        t_secondary = !primary;
        t_cancelling = cancelling;
        return saved;
    }

    /// <summary>Puts back the pointer that was current before <see cref="Enter"/>.</summary>
    public readonly struct Scope(int id, PointerType type, bool secondary, bool cancelling) : IDisposable
    {
        public void Dispose()
        {
            t_id = id;
            t_type = type;
            t_secondary = secondary;
            t_cancelling = cancelling;
        }
    }
}

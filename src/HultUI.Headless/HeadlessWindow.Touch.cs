using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;

namespace HultUI.Headless;

// ──── Touch driver ────
// Fingers, as the platform reports them (docs/design/POINTER-IDENTITY.md): each is a number the
// test picks, stable from TouchDown to TouchUp or TouchCancel. Coordinates are layout units, as
// for the mouse. Synchronous like the mouse driver; nothing here advances virtual time, so a
// double tap and a long press are timed with AdvanceTime/PumpFrame.
public sealed partial class HeadlessWindow
{
    private readonly Dictionary<int, Point> _fingers = new();

    /// <summary>A finger touches the window at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public void TouchDown(int finger, float x, float y, KeyModifiers modifiers = KeyModifiers.None)
    {
        _fingers[finger] = new Point(x, y);
        _platform.RaiseInput(new InputEvent.TouchInput(finger, TouchPhase.Down, x, y, modifiers));
    }

    /// <summary>A finger that is down moves to (<paramref name="x"/>, <paramref name="y"/>), in one step.</summary>
    public void TouchMove(int finger, float x, float y, KeyModifiers modifiers = KeyModifiers.None)
    {
        _fingers[finger] = new Point(x, y);
        _platform.RaiseInput(new InputEvent.TouchInput(finger, TouchPhase.Move, x, y, modifiers));
    }

    /// <summary>A finger lifts where it last was.</summary>
    public void TouchUp(int finger, KeyModifiers modifiers = KeyModifiers.None)
    {
        var at = LastAt(finger);
        _fingers.Remove(finger);
        _platform.RaiseInput(new InputEvent.TouchInput(finger, TouchPhase.Up, at.X, at.Y, modifiers));
    }

    /// <summary>The system takes a finger (a system gesture, a call): no release follows.</summary>
    public void TouchCancel(int finger, KeyModifiers modifiers = KeyModifiers.None)
    {
        var at = LastAt(finger);
        _fingers.Remove(finger);
        _platform.RaiseInput(new InputEvent.TouchInput(finger, TouchPhase.Cancel, at.X, at.Y, modifiers));
    }

    /// <summary>A finger touches and lifts at one point, with no virtual time between.</summary>
    public void Tap(float x, float y, KeyModifiers modifiers = KeyModifiers.None)
    {
        int finger = FreeFinger();
        TouchDown(finger, x, y, modifiers);
        TouchUp(finger, modifiers);
    }

    /// <summary>Taps the center of the element's laid-out bounds, as <see cref="Click(Element, MouseButton, KeyModifiers)"/>
    /// aims (resolved at call time — layout must have run).</summary>
    public void Tap(Element element, KeyModifiers modifiers = KeyModifiers.None)
    {
        var center = CenterOf(element);
        Tap(center.X, center.Y, modifiers);
    }

    /// <summary>
    /// One finger drags from <paramref name="from"/> to <paramref name="to"/> over
    /// <paramref name="duration"/>, in <paramref name="steps"/> even moves with a frame pumped after
    /// each (virtual time advancing by <c>duration / steps</c>), then lifts at once. A fast swipe
    /// flings: pump on (<c>PumpFrame</c>, <c>PumpUntil</c>) to let the throw run out
    /// (docs/design/GESTURES.md).
    /// </summary>
    public void Swipe(Point from, Point to, TimeSpan duration, int steps = 10, KeyModifiers modifiers = KeyModifiers.None)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        int finger = FreeFinger();
        var step = TimeSpan.FromTicks(duration.Ticks / steps);
        TouchDown(finger, from.X, from.Y, modifiers);
        for (int i = 1; i <= steps; i++)
        {
            _session.PumpFrame(step);
            float t = i / (float)steps;
            TouchMove(finger, from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t, modifiers);
        }
        TouchUp(finger, modifiers);
    }

    /// <summary>A finger held still on the element's centre past the long-press time, frames
    /// pumped meanwhile, then lifted. What the long press opened (a context menu) is still open.</summary>
    public void LongPress(Element element, KeyModifiers modifiers = KeyModifiers.None)
    {
        var center = CenterOf(element);
        int finger = FreeFinger();
        TouchDown(finger, center.X, center.Y, modifiers);
        _session.PumpFrame(TimeSpan.FromSeconds(HultUI.Window.LongPressSeconds + 0.1));
        TouchUp(finger, modifiers);
    }

    private Point LastAt(int finger) =>
        _fingers.TryGetValue(finger, out var at)
            ? at
            : throw new InvalidOperationException($"Finger {finger} is not down: TouchDown it first.");

    private int FreeFinger()
    {
        int finger = 0;
        while (_fingers.ContainsKey(finger)) finger++;
        return finger;
    }
}

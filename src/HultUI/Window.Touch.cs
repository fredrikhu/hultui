using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;

namespace HultUI;

// Fingers (docs/design/POINTER-IDENTITY.md). The first finger down while no other is touching is
// the primary pointer: it goes through the mouse's own paths (hover, press, capture, click, focus,
// selection), with its ID and type stamped on every event they make (PointerContext), and the
// touch rules applied where those paths read them (PointerState's slop and double-tap timing, no
// tooltip, no cursor, no drag source, hover ending at the release). Other fingers hit-test nothing:
// they reach the element holding the primary pointer's capture, or nowhere.
public sealed partial class Window
{
    private struct Finger
    {
        public int Id;
        public Point At;
        public Point DownAt;
    }

    private const long NoFinger = long.MinValue;
    private const int FirstTouchPointerId = PointerEvent.MousePointerId + 1;

    /// <summary>The fingers touching the window, by the platform's finger.</summary>
    private readonly Dictionary<long, Finger> _fingers = new();

    /// <summary>The platform's finger that is the primary pointer, or <see cref="NoFinger"/>: none
    /// is, or it lifted while another finger still touches (no finger becomes primary until all
    /// have lifted).</summary>
    private long _primaryFinger = NoFinger;

    private int _nextPointerId = FirstTouchPointerId;

    /// <summary>Set while the primary finger's release is handled when it lifted within the touch
    /// slop of its press: the release, and the tap, go to the element it pressed, whatever its
    /// wobble crossed on the way (a button's label and the padding around it).</summary>
    private bool _releaseOnPressed;

    private void HandleTouch(InputEvent.TouchInput touch)
    {
        var at = new Point(touch.X, touch.Y);
        switch (touch.Phase)
        {
            case TouchPhase.Down:
            {
                if (_fingers.ContainsKey(touch.FingerId)) return;   // a second down for one finger
                bool primary = _fingers.Count == 0;
                // IDs count up while any finger touches, so none is reused during a gesture,
                // and start again once every finger has lifted.
                if (primary) _nextPointerId = FirstTouchPointerId;
                int id = _nextPointerId++;
                _fingers[touch.FingerId] = new Finger { Id = id, At = at, DownAt = at };
                if (!primary)
                {
                    SendToCapture(id, at, default, TouchPhase.Down, touch.Modifiers);
                    return;
                }
                _primaryFinger = touch.FingerId;
                using var scope = PointerContext.Enter(id, PointerType.Touch, primary: true);
                bool stoppedFling = StopFlingUnder(at);
                // The finger is over what it lands on: its hover starts there (PointerEnter, a
                // :hover look), with no move, as the web's touch has it.
                HoverAt(at, touch.Modifiers);
                HandleInputEvent(new InputEvent.MouseButtonInput(at.X, at.Y, PointerEvent.LeftButton, true, touch.Modifiers));
                // What the finger may become: a scroll, a pan, a long press (Window.Gestures.cs).
                BeginGesture(touch.FingerId, id, at, touch.Modifiers, stoppedFling);
                return;
            }

            case TouchPhase.Move:
            {
                if (!_fingers.TryGetValue(touch.FingerId, out var finger)) return;
                var delta = new Point(at.X - finger.At.X, at.Y - finger.At.Y);
                if (delta.X == 0 && delta.Y == 0) return;
                _fingers[touch.FingerId] = finger with { At = at };
                if (touch.FingerId != _primaryFinger)
                {
                    SendToCapture(finger.Id, at, delta, TouchPhase.Move, touch.Modifiers);
                    return;
                }
                using var scope = PointerContext.Enter(finger.Id, PointerType.Touch, primary: true);
                if (GestureMove(touch.FingerId, at, delta)) return;
                HandleInputEvent(new InputEvent.MouseMoveInput(at.X, at.Y, delta.X, delta.Y, touch.Modifiers));
                return;
            }

            default:   // Up, Cancel
            {
                if (!_fingers.Remove(touch.FingerId, out var finger)) return;
                bool cancel = touch.Phase == TouchPhase.Cancel;
                // A cancel ends the press where the finger last was: the platform's position on a
                // cancel is not one the finger reached.
                var end = cancel ? finger.At : at;
                if (touch.FingerId != _primaryFinger)
                {
                    SendToCapture(finger.Id, end, default, touch.Phase, touch.Modifiers);
                    return;
                }
                _primaryFinger = NoFinger;
                // A gesture that claimed the finger cancelled its press then: nothing to release.
                if (GestureEnd(touch.FingerId, end, ref cancel)) return;
                using var scope = PointerContext.Enter(finger.Id, PointerType.Touch, primary: true, cancelling: cancel);
                _releaseOnPressed = Distance(end, finger.DownAt) <= PointerState.TouchSlop;
                try
                {
                    HandleInputEvent(new InputEvent.MouseButtonInput(end.X, end.Y, PointerEvent.LeftButton, false, touch.Modifiers));
                }
                finally
                {
                    _releaseOnPressed = false;
                }
                // No sticky hover: a finger that has lifted is over nothing.
                HandleInputEvent(new InputEvent.PointerLeftWindowInput());
                return;
            }
        }
    }

    private static float Distance(Point a, Point b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>A finger that is not the primary pointer: to the element holding the primary
    /// pointer's capture, as that element's own coordinates have it, or nowhere.</summary>
    private void SendToCapture(int id, Point at, Point delta, TouchPhase phase, KeyModifiers modifiers)
    {
        if (_pointerCaptureTarget is not { } captured) return;
        if (captured.Parent is null && !ReferenceEquals(captured, _root)) return;   // rebuilt away
        var pos = new Point(at.X - _pointerCaptureOffset.X, at.Y - _pointerCaptureOffset.Y);
        using var scope = PointerContext.Enter(id, PointerType.Touch, primary: false);
        PointerEvent evt = phase switch
        {
            TouchPhase.Down => new PointerDownEvent { Position = pos, Button = PointerEvent.LeftButton, Modifiers = modifiers },
            TouchPhase.Move => new PointerMoveEvent { Position = pos, Delta = delta, Button = PointerEvent.LeftButton, Modifiers = modifiers },
            TouchPhase.Up => new PointerUpEvent { Position = pos, Button = PointerEvent.LeftButton, Modifiers = modifiers },
            _ => new PointerCancelEvent { Position = pos, Button = PointerEvent.LeftButton, Modifiers = modifiers },
        };
        EventDispatcher.Dispatch(evt, captured);
    }

    /// <summary>A press's release, or its cancel when the pointer was taken
    /// (<see cref="PointerContext.Cancelling"/>): <see cref="PointerCancelEvent"/> in place of
    /// <see cref="PointerUpEvent"/>, at the same point.</summary>
    private static void DispatchRelease(PointerUpEvent up, Element target)
    {
        if (!PointerContext.Cancelling)
        {
            EventDispatcher.Dispatch(up, target);
            return;
        }
        // The text elements' drag-select ends here, as their release ends it: a handler of their
        // own for the cancel was 24 B on every selectable label (plan 17 round 18 cut them to six).
        switch (target)
        {
            case TextElement label: label.EndDrag(); break;
            case TextInputElement input: input.EndDrag(); break;
            case TextAreaElement area: area.EndDrag(); break;
        }
        EventDispatcher.Dispatch(new PointerCancelEvent { Position = up.Position, Button = up.Button, Modifiers = up.Modifiers }, target);
    }
}

using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Core.Events;

/// <summary>
/// Tracks pointer state for enter/leave detection and click synthesis.
/// </summary>
/// <param name="timeProvider">
/// Clock used for double-click timing. Defaults to the system clock; headless
/// testing injects a manually advanced provider so click timing is deterministic.
/// </param>
public sealed class PointerState(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private Element? _hoveredElement;
    private Element? _pressedElement;
    private int _pressedButton;
    private Point _pressedPosition;
    private Point _lastPosition;
    private DateTime _lastClickTime;
    private Point _lastClickPosition;
    private int _clickCount;

    private const double DoubleClickMaxMs = 400;
    private const float DoubleClickMaxDistance = 5f;

    public Element? HoveredElement => _hoveredElement;

    /// <summary>
    /// Update the hovered element based on pointer position.
    /// Dispatches PointerEnter/PointerLeave events following mouseenter/mouseleave
    /// semantics: moving between an element and its descendants does NOT fire
    /// leave on the ancestor.
    /// </summary>
    public void UpdateHover(Element? newHovered, Point position, KeyModifiers modifiers)
    {
        _lastPosition = position;

        if (ReferenceEquals(_hoveredElement, newHovered))
            return;

        var oldHovered = _hoveredElement;
        _hoveredElement = newHovered;

        // Find the common ancestor to avoid firing leave/enter on shared ancestors
        var commonAncestor = FindCommonAncestor(oldHovered, newHovered);

        // Fire PointerLeave on elements that lost the pointer (old → common ancestor, exclusive)
        var leaveEvt = new PointerLeaveEvent { Position = position, Modifiers = modifiers };
        for (var el = oldHovered; el is not null && !ReferenceEquals(el, commonAncestor); el = el.Parent)
        {
            el.SetInteractionHovered(false);
            EventDispatcher.DispatchDirect(leaveEvt, el);
        }

        // Fire PointerEnter on elements that gained the pointer (new → common ancestor, exclusive)
        var enterEvt = new PointerEnterEvent { Position = position, Modifiers = modifiers };
        for (var el = newHovered; el is not null && !ReferenceEquals(el, commonAncestor); el = el.Parent)
        {
            el.SetInteractionHovered(true);
            EventDispatcher.DispatchDirect(enterEvt, el);
        }
    }

    private static Element? FindCommonAncestor(Element? a, Element? b)
    {
        if (a is null || b is null) return null;

        // Bring the deeper of the two up to the other's depth, then step both up together
        // until they meet. Two elements in different trees (one of them since removed) reach
        // null together and share nothing. A set of a's ancestors, which this used to build,
        // was ~350 bytes on every hover change.
        int depthA = Depth(a), depthB = Depth(b);
        for (; depthA > depthB; depthA--) a = a!.Parent;
        for (; depthB > depthA; depthB--) b = b!.Parent;

        while (a is not null && !ReferenceEquals(a, b))
        {
            a = a.Parent;
            b = b!.Parent;
        }
        return a;
    }

    private static int Depth(Element element)
    {
        int depth = 0;
        for (var el = element.Parent; el is not null; el = el.Parent)
            depth++;
        return depth;
    }

    /// <summary>
    /// Handle a pointer down event. Returns the event for dispatching.
    /// </summary>
    public PointerDownEvent HandlePointerDown(Element target, Point position, int button, KeyModifiers modifiers)
    {
        ClearPressedChain(); // defensive: drop any stale press (e.g. a press consumed by a drag)
        _pressedElement = target;
        _pressedButton = button;
        _pressedPosition = position;

        // :active-style press applies to the target and its ancestors.
        for (var el = target; el is not null; el = el.Parent)
            el.SetInteractionPressed(true);

        // The run of repeated clicks is counted here, on the press, not on the release that
        // synthesizes the click. A double-click gesture that only resolves on release cannot
        // be dragged: by the time the word is selected the button is already up. Counting on
        // the press lets the second press select the word and the motion after it keep
        // extending — and it is what the platforms do (Win32 measures the double-click
        // interval between successive button-downs).
        if (button == PointerEvent.LeftButton)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            bool continuesRun = (now - _lastClickTime).TotalMilliseconds < DoubleClickMaxMs
                && Distance(position, _lastClickPosition) < DoubleClickMaxDistance;
            _clickCount = continuesRun ? _clickCount + 1 : 1;
            _lastClickTime = now;
            _lastClickPosition = position;
        }

        return new PointerDownEvent
        {
            Position = position,
            Button = button,
            Modifiers = modifiers,
            ClickCount = button == PointerEvent.LeftButton ? _clickCount : 1
        };
    }

    /// <summary>
    /// Handle a pointer up event. Returns the up event and optionally a click/double-click event.
    /// </summary>
    public (PointerUpEvent upEvent, ClickEvent? clickEvent, DoubleClickEvent? doubleClickEvent) HandlePointerUp(
        Element target, Point position, int button, KeyModifiers modifiers)
    {
        ClearPressedChain();

        var upEvent = new PointerUpEvent
        {
            Position = position,
            Button = button,
            Modifiers = modifiers
        };

        ClickEvent? clickEvent = null;
        DoubleClickEvent? doubleClickEvent = null;

        // Synthesize click if released on the same element that was pressed.
        // A retained-mode rebuild between down and up (e.g. selecting a row
        // MarkDirty()s and re-realizes VirtualList rows with fresh identities)
        // detaches the pressed element, so identity no longer matches even
        // though the pointer never left the spot. Fall back to: the pressed
        // element was removed from the tree (Parent is null) and the pointer
        // stayed put — treat it as a click on whatever now sits under it.
        bool sameTarget = ReferenceEquals(target, _pressedElement);
        bool pressedRecycled = !sameTarget
            && _pressedElement is { Parent: null }
            && Distance(position, _pressedPosition) < DoubleClickMaxDistance;
        // Only the primary button synthesizes a click. Every other toolkit draws the line
        // here — the DOM gives the middle and right buttons `auxclick`, not `click` — and so
        // does most of this one already: Calendar, ContextMenu, DatePicker, Dropdown and
        // TextArea each open with `if (evt.Button != 0) return`, one control at a time.
        //
        // What made it worth doing centrally: `OnClick` takes no button, so every handler in
        // every app reads as "when this is activated" and fired on a middle click too. That
        // was harmless while nobody pressed the middle button over a list, and stopped being
        // harmless the moment middle-drag became the way to scroll one — a press that never
        // left the autoscroll's dead zone staged the diff line under the pointer.
        //
        // The button is still on PointerDown and PointerUp, which is where a handler that
        // really wants the middle or right button belongs (Vesper closes a tab from there).
        if (button == PointerEvent.LeftButton && button == _pressedButton && (sameTarget || pressedRecycled))
        {
            // The count was settled by the matching press; the release only reports it, so a
            // handler that acts on the press and one that acts on the click see the same run.
            clickEvent = new ClickEvent
            {
                Position = position,
                Button = button,
                Modifiers = modifiers,
                ClickCount = _clickCount
            };

            if (_clickCount == 2)
            {
                doubleClickEvent = new DoubleClickEvent
                {
                    Position = position,
                    Button = button,
                    Modifiers = modifiers
                };
            }
        }

        _pressedElement = null;
        return (upEvent, clickEvent, doubleClickEvent);
    }

    /// <summary>
    /// Forget the press in progress without synthesizing a click. For gestures the OS takes
    /// over mid-press — a native drag runs a modal loop and swallows the release — where the
    /// press would otherwise stay armed and its :active styling stuck on the row.
    /// </summary>
    public void CancelPress()
    {
        ClearPressedChain();
        _pressedElement = null;
    }

    private void ClearPressedChain()
    {
        for (var el = _pressedElement; el is not null; el = el.Parent)
            el.SetInteractionPressed(false);
    }

    private static float Distance(Point a, Point b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

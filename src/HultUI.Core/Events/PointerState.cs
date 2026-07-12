using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Core.Events;

/// <summary>
/// Tracks pointer state for enter/leave detection and click synthesis.
/// </summary>
public sealed class PointerState
{
    private Element? _hoveredElement;
    private Element? _pressedElement;
    private int _pressedButton;
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

        // Collect ancestors of a into a set
        var ancestors = new HashSet<Element>(ReferenceEqualityComparer.Instance);
        for (var el = a; el is not null; el = el.Parent)
            ancestors.Add(el);

        // Walk up from b to find the first shared ancestor
        for (var el = b; el is not null; el = el.Parent)
        {
            if (ancestors.Contains(el))
                return el;
        }

        return null;
    }

    /// <summary>
    /// Handle a pointer down event. Returns the event for dispatching.
    /// </summary>
    public PointerDownEvent HandlePointerDown(Element target, Point position, int button, KeyModifiers modifiers)
    {
        ClearPressedChain(); // defensive: drop any stale press (e.g. a press consumed by a drag)
        _pressedElement = target;
        _pressedButton = button;

        // :active-style press applies to the target and its ancestors.
        for (var el = target; el is not null; el = el.Parent)
            el.SetInteractionPressed(true);

        return new PointerDownEvent
        {
            Position = position,
            Button = button,
            Modifiers = modifiers
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

        // Synthesize click if released on the same element that was pressed
        if (button == _pressedButton && ReferenceEquals(target, _pressedElement))
        {
            var now = DateTime.UtcNow;
            var timeSinceLastClick = (now - _lastClickTime).TotalMilliseconds;
            var distFromLastClick = Distance(position, _lastClickPosition);

            if (timeSinceLastClick < DoubleClickMaxMs && distFromLastClick < DoubleClickMaxDistance)
            {
                _clickCount++;
            }
            else
            {
                _clickCount = 1;
            }

            _lastClickTime = now;
            _lastClickPosition = position;

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

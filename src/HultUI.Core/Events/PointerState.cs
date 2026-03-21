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
    /// Dispatches PointerEnter/PointerLeave events as needed.
    /// </summary>
    public void UpdateHover(Element? newHovered, Point position, KeyModifiers modifiers)
    {
        _lastPosition = position;

        if (ReferenceEquals(_hoveredElement, newHovered))
            return;

        var oldHovered = _hoveredElement;
        _hoveredElement = newHovered;

        if (oldHovered is not null)
            EventDispatcher.DispatchDirect(new PointerLeaveEvent { Position = position, Modifiers = modifiers }, oldHovered);

        if (newHovered is not null)
            EventDispatcher.DispatchDirect(new PointerEnterEvent { Position = position, Modifiers = modifiers }, newHovered);
    }

    /// <summary>
    /// Handle a pointer down event. Returns the event for dispatching.
    /// </summary>
    public PointerDownEvent HandlePointerDown(Element target, Point position, int button, KeyModifiers modifiers)
    {
        _pressedElement = target;
        _pressedButton = button;

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
                _clickCount = 0;
            }
        }

        _pressedElement = null;
        return (upEvent, clickEvent, doubleClickEvent);
    }

    private static float Distance(Point a, Point b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

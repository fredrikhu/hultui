using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;

namespace HultUI.Headless;

// ──── Input driver ────
// All coordinates are layout units (the same space element bounds live in).
// Injection is synchronous: when a call returns, every handler has run and
// state changes are applied — pump a frame afterwards to render them.
// Nothing here advances virtual time; interactions that depend on timing
// (double-click windows, tooltip delays) are controlled explicitly with
// AdvanceTime/PumpFrame, which is what makes them deterministic.
public sealed partial class HeadlessWindow
{
    private float _mouseX;
    private float _mouseY;

    /// <summary>Moves the mouse, firing hover enter/leave and move events.</summary>
    public void MoveMouse(float x, float y, KeyModifiers modifiers = KeyModifiers.None)
    {
        float dx = x - _mouseX;
        float dy = y - _mouseY;
        _mouseX = x;
        _mouseY = y;
        _platform.RaiseInput(new InputEvent.MouseMoveInput(x, y, dx, dy, modifiers));
    }

    public void MouseDown(float x, float y, MouseButton button = MouseButton.Left,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        MoveMouse(x, y, modifiers);
        _platform.RaiseInput(new InputEvent.MouseButtonInput(x, y, (int)button, IsDown: true, modifiers));
    }

    public void MouseUp(float x, float y, MouseButton button = MouseButton.Left,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        _platform.RaiseInput(new InputEvent.MouseButtonInput(x, y, (int)button, IsDown: false, modifiers));
    }

    /// <summary>Full click: move, press, release at the given point.</summary>
    public void Click(float x, float y, MouseButton button = MouseButton.Left,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        MouseDown(x, y, button, modifiers);
        MouseUp(x, y, button, modifiers);
    }

    /// <summary>Clicks the center of the element's laid-out bounds (resolved at call time — layout must have run).</summary>
    public void Click(Element element, MouseButton button = MouseButton.Left,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        var center = CenterOf(element);
        Click(center.X, center.Y, button, modifiers);
    }

    /// <summary>
    /// Two clicks at the same point with no virtual time in between — always
    /// inside the double-click window, so the DoubleClick event fires.
    /// </summary>
    public void DoubleClick(float x, float y, KeyModifiers modifiers = KeyModifiers.None)
    {
        Click(x, y, MouseButton.Left, modifiers);
        Click(x, y, MouseButton.Left, modifiers);
    }

    public void DoubleClick(Element element, KeyModifiers modifiers = KeyModifiers.None)
    {
        var center = CenterOf(element);
        DoubleClick(center.X, center.Y, modifiers);
    }

    public void RightClick(float x, float y, KeyModifiers modifiers = KeyModifiers.None)
        => Click(x, y, MouseButton.Right, modifiers);

    public void RightClick(Element element, KeyModifiers modifiers = KeyModifiers.None)
    {
        var center = CenterOf(element);
        RightClick(center.X, center.Y, modifiers);
    }

    /// <summary>Scrolls the wheel over the given point. Positive <paramref name="deltaY"/> scrolls up (away from the user), matching SDL.</summary>
    public void Wheel(float x, float y, float deltaX, float deltaY,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        _platform.RaiseInput(new InputEvent.MouseWheelInput(x, y, deltaX, deltaY, modifiers));
    }

    /// <summary>
    /// Press-drag-release with intermediate move events, so drag thresholds
    /// (5 layout units) and per-move handlers fire like a real drag.
    /// </summary>
    public void Drag(Point from, Point to, int steps = 5, MouseButton button = MouseButton.Left,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        if (steps < 1) throw new ArgumentOutOfRangeException(nameof(steps));
        MouseDown(from.X, from.Y, button, modifiers);
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            MoveMouse(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t, modifiers);
        }
        MouseUp(to.X, to.Y, button, modifiers);
    }

    public void KeyDown(KeyCode key, KeyModifiers modifiers = KeyModifiers.None, bool repeat = false)
        => _platform.RaiseInput(new InputEvent.KeyInput(key, modifiers, IsDown: true, IsRepeat: repeat));

    public void KeyUp(KeyCode key, KeyModifiers modifiers = KeyModifiers.None)
        => _platform.RaiseInput(new InputEvent.KeyInput(key, modifiers, IsDown: false, IsRepeat: false));

    /// <summary>Press and release.</summary>
    public void PressKey(KeyCode key, KeyModifiers modifiers = KeyModifiers.None)
    {
        KeyDown(key, modifiers);
        KeyUp(key, modifiers);
    }

    /// <summary>
    /// Types text into the focused element, one character per event — the
    /// granularity a real keyboard produces. Focus the target first (click it
    /// or drive <see cref="PressKey"/> with Tab).
    /// </summary>
    public void TypeText(string text)
    {
        foreach (var ch in text)
            _platform.RaiseInput(new InputEvent.TextInputInput(ch.ToString()));
    }

    private static Point CenterOf(Element element)
    {
        var box = element.Layout.AbsoluteBorderBox;
        if (box.Width <= 0 || box.Height <= 0)
            throw new InvalidOperationException(
                "Element has no laid-out bounds — pump a frame after setting the root before interacting.");
        return new Point(box.X + box.Width / 2f, box.Y + box.Height / 2f);
    }
}

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

    /// <summary>Stand in for the OS foregrounding this window — what a click on the taskbar
    /// or an alt-tab back to it does. <see cref="Window.Activated"/> is raised on the next
    /// <c>PumpFrame</c>, the same point in the frame the real loop raises it.</summary>
    public void Activate() => _platform.ActivatedPending = true;

    /// <summary>Moves the mouse, firing hover enter/leave and move events.</summary>
    public void MoveMouse(float x, float y, KeyModifiers modifiers = KeyModifiers.None)
    {
        float dx = x - _mouseX;
        float dy = y - _mouseY;
        _mouseX = x;
        _mouseY = y;
        _platform.RaiseInput(new InputEvent.MouseMoveInput(x, y, dx, dy, modifiers));
    }

    /// <summary>The pointer leaves the window, as SDL reports when it crosses the edge.</summary>
    public void MouseLeaveWindow() => _platform.RaiseInput(new InputEvent.PointerLeftWindowInput());

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

    /// <summary>
    /// Simulates an OS-level file drop: the begin/position/file/complete sequence a
    /// desktop drag from another application produces. <paramref name="modifiers"/>
    /// is what the platform samples from the live keyboard — the only way Shift or
    /// Ctrl reaches an external drag, since no key event is delivered during one.
    /// </summary>
    public void DropFiles(Point at, IEnumerable<string> paths, KeyModifiers modifiers = KeyModifiers.None)
    {
        _platform.RaiseInput(new InputEvent.DropBeginInput());
        foreach (var path in paths)
            _platform.RaiseInput(new InputEvent.DropFileInput(path));
        _platform.RaiseInput(new InputEvent.DropPositionInput(at.X, at.Y, modifiers));
        _platform.RaiseInput(new InputEvent.DropCompleteInput(modifiers));
    }

    /// <summary>
    /// Simulates an OS drag of <paramref name="paths"/> entering the window and moving to
    /// <paramref name="at"/>, without dropping: finish it with <see cref="DropHere"/> or
    /// <see cref="CancelDrag"/>. Returns whether the element under the point accepts it — what
    /// a platform would tell the operating system.
    /// </summary>
    public bool DragFilesOver(Point at, IEnumerable<string> paths, KeyModifiers modifiers = KeyModifiers.None, nint nativeData = 0)
    {
        _platform.RaiseInput(new InputEvent.DropBeginInput(nativeData));
        foreach (var path in paths)
            _platform.RaiseInput(new InputEvent.DropFileInput(path));
        return DragTo(at, modifiers);
    }

    /// <summary>Moves an OS drag begun with <see cref="DragFilesOver"/>; returns whether it is accepted there.</summary>
    public bool DragTo(Point at, KeyModifiers modifiers = KeyModifiers.None)
    {
        var feedback = new DropFeedback();
        _platform.RaiseInput(new InputEvent.DropPositionInput(at.X, at.Y, modifiers, feedback));
        return feedback.Accepted;
    }

    /// <summary>Drops an OS drag begun with <see cref="DragFilesOver"/>; returns whether an element took it.</summary>
    public bool DropHere(KeyModifiers modifiers = KeyModifiers.None)
    {
        var feedback = new DropFeedback();
        _platform.RaiseInput(new InputEvent.DropCompleteInput(modifiers, feedback));
        return feedback.Accepted;
    }

    /// <summary>The OS drag leaves the window without a drop.</summary>
    public void CancelDrag() => _platform.RaiseInput(new InputEvent.DropCancelInput());

    /// <summary>Simulates an OS-level text drop at <paramref name="at"/>.</summary>
    public void DropText(Point at, string text, KeyModifiers modifiers = KeyModifiers.None)
    {
        _platform.RaiseInput(new InputEvent.DropBeginInput());
        _platform.RaiseInput(new InputEvent.DropTextInput(text));
        _platform.RaiseInput(new InputEvent.DropPositionInput(at.X, at.Y, modifiers));
        _platform.RaiseInput(new InputEvent.DropCompleteInput(modifiers));
    }

    /// <param name="scanCode">The physical key, as SDL would report it (the USB HID usage); 0 for none.</param>
    public void KeyDown(KeyCode key, KeyModifiers modifiers = KeyModifiers.None, bool repeat = false, ushort scanCode = 0)
        => _platform.RaiseInput(new InputEvent.KeyInput(key, modifiers, IsDown: true, IsRepeat: repeat, ScanCode: scanCode));

    /// <param name="scanCode">The physical key, as SDL would report it (the USB HID usage); 0 for none.</param>
    public void KeyUp(KeyCode key, KeyModifiers modifiers = KeyModifiers.None, ushort scanCode = 0)
        => _platform.RaiseInput(new InputEvent.KeyInput(key, modifiers, IsDown: false, IsRepeat: false, ScanCode: scanCode));

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

    /// <summary>
    /// Where the element is on screen, which inside a scrolled view is not where its layout box
    /// says it is — see <see cref="Element.ScreenBorderBox"/>. A click aimed at the layout box
    /// lands on whatever else is at that point, and a click that hits nothing is not an error,
    /// so getting this wrong shows up as a test passing with its handler never having run.
    /// </summary>
    private static Point CenterOf(Element element)
    {
        var box = element.ScreenBorderBox;
        if (box.Width <= 0 || box.Height <= 0)
            throw new InvalidOperationException(
                "Element has no laid-out bounds — pump a frame after setting the root before interacting.");

        return new Point(box.X + box.Width / 2f, box.Y + box.Height / 2f);
    }
}

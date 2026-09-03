using HultUI.Core.Events;

namespace HultUI.WinForms.Input;

/// <summary>
/// Turns a control's ordinary WinForms events into HultUI <see cref="InputEvent"/>s.
///
/// <para>This is the counterpart of the SDL backend's event translator, and like it,
/// it only maps: nothing here decides what a click <em>means</em>. Double-click
/// detection, hover enter/leave semantics and press tracking live in the framework's
/// <c>PointerState</c>, so the two backends cannot drift on behaviour — only on which
/// native events they read.</para>
/// </summary>
internal sealed class WinFormsInputTranslator
{
    private readonly Control _control;
    private readonly Func<float> _scale;
    private readonly Action<InputEvent> _emit;

    private Point _lastPosition;
    private bool _hasLastPosition;

    internal WinFormsInputTranslator(Control control, Func<float> scale, Action<InputEvent> emit)
    {
        _control = control;
        _scale = scale;
        _emit = emit;

        control.MouseMove += OnMouseMove;
        control.MouseDown += OnMouseDown;
        control.MouseUp += OnMouseUp;
        control.MouseWheel += OnMouseWheel;
        control.MouseLeave += OnMouseLeave;
    }

    internal void Detach()
    {
        _control.MouseMove -= OnMouseMove;
        _control.MouseDown -= OnMouseDown;
        _control.MouseUp -= OnMouseUp;
        _control.MouseWheel -= OnMouseWheel;
        _control.MouseLeave -= OnMouseLeave;
    }

    /// <summary>
    /// Horizontal wheel, which WinForms does not surface as an event — the control's
    /// window procedure hands us <c>WM_MOUSEHWHEEL</c> directly.
    /// </summary>
    /// <summary>A key press or release, from the control's key handlers.</summary>
    internal void EmitKey(Keys keyData, bool isDown, bool isRepeat)
    {
        var code = KeyMap.ToKeyCode(keyData & Keys.KeyCode);
        if (code == KeyCode.Unknown) return;
        _emit(new InputEvent.KeyInput(code, KeyMap.ToModifiers(keyData), isDown, isRepeat));
    }

    /// <summary>
    /// Committed text, from <c>WM_CHAR</c>.
    /// </summary>
    /// <remarks>
    /// Control characters are dropped: Win32 sends <c>WM_CHAR</c> for Backspace, Tab,
    /// Escape and Enter as well as for typing, and the framework expects text input to
    /// be text — those arrive as key events instead. A character outside the basic
    /// plane comes as a surrogate pair in two messages, so a lone high surrogate is
    /// held back until its partner arrives.
    /// </remarks>
    internal void EmitChar(char character)
    {
        if (char.IsHighSurrogate(character)) { _pendingHighSurrogate = character; return; }

        if (_pendingHighSurrogate is { } high && char.IsLowSurrogate(character))
        {
            _pendingHighSurrogate = null;
            _emit(new InputEvent.TextInputInput(new string([high, character])));
            return;
        }

        _pendingHighSurrogate = null;
        if (char.IsControl(character)) return;
        _emit(new InputEvent.TextInputInput(character.ToString()));
    }

    /// <summary>Text the IME has committed — already a whole string, no surrogate dance.</summary>
    internal void EmitText(string text)
    {
        if (text.Length > 0) _emit(new InputEvent.TextInputInput(text));
    }

    /// <summary>The in-flight IME composition, which the framework renders as a preedit.</summary>
    internal void EmitComposition(string text, int cursor)
        => _emit(new InputEvent.TextEditingInput(text, cursor, 0));

    private char? _pendingHighSurrogate;

    // ──── drops from other applications ────

    /// <summary>
    /// Announces an incoming drag and hands over its payload.
    /// </summary>
    /// <remarks>
    /// Payload before position, and that order matters: the first position update is
    /// what runs the hit test and dispatches <c>DragEnter</c>/<c>DragOver</c> to the
    /// element under the pointer, and a target that inspected the data then would see an
    /// empty payload if the files had not arrived yet.
    /// </remarks>
    internal void EmitDropBegin(IReadOnlyList<string> files, string? text)
    {
        _emit(new InputEvent.DropBeginInput());
        foreach (var path in files) _emit(new InputEvent.DropFileInput(path));
        if (text is { Length: > 0 }) _emit(new InputEvent.DropTextInput(text));
    }

    internal void EmitDropPosition(Point clientPosition, KeyModifiers modifiers)
    {
        float s = 1f / _scale();
        _emit(new InputEvent.DropPositionInput(clientPosition.X * s, clientPosition.Y * s, modifiers));
    }

    internal void EmitDropComplete(KeyModifiers modifiers)
        => _emit(new InputEvent.DropCompleteInput(modifiers));

    /// <summary>
    /// Ends a drag that left without dropping.
    /// </summary>
    /// <remarks>
    /// There is no cancel event to send — the framework has none — so the drag is ended
    /// at a position outside every element. The hit test then finds no target, no
    /// <c>Drop</c> is dispatched, the last hovered element still gets its
    /// <c>DragLeave</c>, and the drag state resets. Same shape as the pointer-leave
    /// handling above, and for the same reason: a control is left far more often than a
    /// window is.
    /// </remarks>
    internal void EmitDropCancelled(KeyModifiers modifiers)
    {
        _emit(new InputEvent.DropPositionInput(-1f, -1f, modifiers));
        _emit(new InputEvent.DropCompleteInput(modifiers));
    }

    internal void EmitHorizontalWheel(int delta, Point clientPosition)
    {
        float s = 1f / _scale();
        _emit(new InputEvent.MouseWheelInput(
            clientPosition.X * s, clientPosition.Y * s,
            delta / WheelDetent, 0f, Modifiers()));
    }

    /// <summary>One wheel notch, as Win32 reports it. SDL reports notches as 1.0.</summary>
    private const float WheelDetent = 120f;

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        float s = 1f / _scale();
        float dx = _hasLastPosition ? (e.X - _lastPosition.X) * s : 0f;
        float dy = _hasLastPosition ? (e.Y - _lastPosition.Y) * s : 0f;
        _lastPosition = e.Location;
        _hasLastPosition = true;
        _emit(new InputEvent.MouseMoveInput(e.X * s, e.Y * s, dx, dy, Modifiers()));
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        // Capture so a drag started inside the control keeps delivering moves once the
        // pointer leaves it — sliders, scrollbars and selection all depend on it. A
        // top-level window gets this from the OS for free; a control does not.
        _control.Capture = true;
        float s = 1f / _scale();
        _emit(new InputEvent.MouseButtonInput(e.X * s, e.Y * s, MapButton(e.Button), IsDown: true, Modifiers()));
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        _control.Capture = false;
        float s = 1f / _scale();
        _emit(new InputEvent.MouseButtonInput(e.X * s, e.Y * s, MapButton(e.Button), IsDown: false, Modifiers()));
    }

    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        float s = 1f / _scale();
        _emit(new InputEvent.MouseWheelInput(e.X * s, e.Y * s, 0f, e.Delta / WheelDetent, Modifiers()));
    }

    /// <summary>
    /// Reports the pointer as somewhere outside the control rather than inventing a
    /// leave event the framework has no concept of. The hit test then finds nothing and
    /// hover clears through the ordinary path.
    /// </summary>
    /// <remarks>
    /// A top-level window rarely needs this; one control among many in a form needs it
    /// constantly, which is why this has no counterpart in the SDL backend.
    /// </remarks>
    private void OnMouseLeave(object? sender, EventArgs e)
    {
        if (_control.Capture) return;   // mid-drag: the pointer is still ours
        _hasLastPosition = false;
        _emit(new InputEvent.MouseMoveInput(-1f, -1f, 0f, 0f, Modifiers()));
    }

    /// <summary>
    /// To the framework's normalised button numbers, which are not the order either
    /// Win32 or SDL uses — the named constants are the contract, and the two backends
    /// must land on the same ones for the same physical button.
    /// </summary>
    private static int MapButton(MouseButtons button) => button switch
    {
        MouseButtons.Left => PointerEvent.LeftButton,
        MouseButtons.Middle => PointerEvent.MiddleButton,
        MouseButtons.Right => PointerEvent.RightButton,
        MouseButtons.XButton1 => PointerEvent.BackButton,
        MouseButtons.XButton2 => PointerEvent.ForwardButton,
        _ => PointerEvent.LeftButton,
    };

    /// <summary>
    /// <see cref="KeyModifiers.Super"/> is absent: <see cref="Control.ModifierKeys"/>
    /// does not report the Windows key, and reading it out of the keyboard state
    /// separately would be a difference from what every other WinForms control sees.
    /// </summary>
    private static KeyModifiers Modifiers()
    {
        var modifiers = KeyModifiers.None;
        var keys = Control.ModifierKeys;
        if ((keys & Keys.Shift) != 0) modifiers |= KeyModifiers.Shift;
        if ((keys & Keys.Control) != 0) modifiers |= KeyModifiers.Ctrl;
        if ((keys & Keys.Alt) != 0) modifiers |= KeyModifiers.Alt;
        return modifiers;
    }
}

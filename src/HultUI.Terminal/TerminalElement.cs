using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Terminal.Emulation;

namespace HultUI.Terminal;

/// <summary>
/// Shows a <see cref="TerminalController"/>: draws its screen, sizes its grid from the element's
/// own size, and turns keys, text, the pointer and the wheel into what the program reads. It has
/// no size of its own; give it one (<c>FlexGrow</c>, <c>Width</c>/<c>Height</c>).
/// </summary>
/// <remarks>
/// <para><b>Keys.</b> Every key is the terminal's, Tab included, except its own copy and paste
/// (Ctrl+Shift+C/V, Ctrl+Insert, Shift+Insert) and Shift+PageUp/PageDown, which scroll the
/// scrollback. A shortcut — Ctrl, Alt or Super held, or F1–F12 — is offered to the element's
/// ancestors first, as a web view does; an application handler that stops it keeps it from the
/// program. Typed characters arrive as text, through the layout and the input method.</para>
/// <para><b>Layouts.</b> A chord is the layout's character with Ctrl or Alt, so Ctrl+Z is the
/// key labelled Z on QWERTZ. AltGr, and Ctrl+Alt (which Windows takes for AltGr), send the
/// character they type (<c>@</c> on a Swedish keyboard); one that types nothing sends its Ctrl+Alt
/// sequence. A key sent as itself never also types its text (Ctrl+Space, a keypad key in
/// application keypad mode).</para>
/// <para><b>Windows.</b> When the connection is ConPTY and the program asked for
/// win32-input-mode, Escape, Alt with a key, Ctrl+Space and Ctrl+Backspace go as Windows key
/// events, which ConPTY cannot misread; the rest stays VT.</para>
/// <para><b>Pointer.</b> When the program tracks the mouse, presses, drags and the wheel go to
/// it; Shift held keeps them for selection. Otherwise a drag selects, a double click selects a
/// word, a triple click a line, and the wheel scrolls the scrollback — or, on a full-screen
/// program's alternate screen, sends arrow keys.</para>
/// </remarks>
public sealed partial class TerminalElement : Element
{
    private const string DefaultName = "Terminal";

    private TerminalController? _controller;
    private bool _listening;

    /// <summary>The accessible name this element set itself: the program's title. A different
    /// name is the application's, and stays.</summary>
    private string _titleName = DefaultName;

    public TerminalElement()
    {
        IsFocusable = true;
        Role = AccessibilityRole.Document;
        AccessibleName = DefaultName;
        Inherited.Cursor = CursorType.Text;
        OnOwn<KeyDownEvent>(OnKeyDown);
        OnOwn<TextInputEvent>(OnTextInput);
        OnOwn<TextEditingEvent>(OnTextEditing);
        OnOwn<PointerDownEvent>(OnPointerDown);
        OnOwn<PointerMoveEvent>(OnPointerMove);
        OnOwn<PointerUpEvent>(OnPointerUp);
        OnOwn<PointerLeaveEvent>(OnPointerLeave);
        OnOwn<ScrollEvent>(OnScroll);
        OnOwn<FocusEvent>(_ => OnFocusChanged(true));
        OnOwn<BlurEvent>(_ => OnFocusChanged(false));
    }

    public TerminalController? Controller
    {
        get => _controller;
        set
        {
            if (ReferenceEquals(_controller, value)) return;
            StopListening();
            _controller = value;
            _selection = null;
            _searchBound = false;
            _announcedTo = null;
            if (value is not null && _search is { Query.Length: > 0 }) Rebind();
            if (value is not null) ApplyTitle(value.Title);
            StartListening();
            if (IsHosted) MarkPaintDirty();
        }
    }

    public static TerminalElement New(TerminalController? controller = null, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new TerminalElement { Controller = controller }.WithOrigin(file, line);

    /// <summary>Typing needs the window's text input and its input method while focused.</summary>
    internal override bool WantsTextInput => true;

    internal bool IsListening => _listening;

    // ──── Lifetime ────

    protected override void OnMounted()
    {
        base.OnMounted();
        StartListening();
    }

    protected override void OnUnmounted()
    {
        base.OnUnmounted();
        StopListening();
        StopSyncHold();
        StopResizeHold();
        StopBlink();
        StopSearchRebind();
        StopScreenWait();
    }

    private void StartListening()
    {
        if (_listening || _controller is null || !IsHosted) return;
        _controller.ContentChanged += OnContentChanged;
        _controller.ScreenSwitched += OnScreenSwitched;
        _controller.ClipboardWrite += OnClipboardWrite;
        _controller.TitleChanged += ApplyTitle;
        _listening = true;
        // A title set before this element listened (a program's first output, read while the
        // window was still being built) is only in the controller's signal now.
        ApplyTitle(_controller.Title);
    }

    private void StopListening()
    {
        if (!_listening) return;
        var c = _controller!;
        c.ContentChanged -= OnContentChanged;
        c.ScreenSwitched -= OnScreenSwitched;
        c.ClipboardWrite -= OnClipboardWrite;
        c.TitleChanged -= ApplyTitle;
        _listening = false;
    }

    private void OnContentChanged()
    {
        // Left behind without an unmount: the root swapped by SetRoot.
        if (!IsHosted) { StopListening(); return; }
        MarkPaintDirty();
        ScheduleSearchRebind();
        NoteScreenChanged();
    }

    private void OnScreenSwitched(bool alternate)
    {
        // A selection names lines of the screen it was made on, and the search searched it.
        _selection = null;
        MarkPaintDirty();
        if (_search is { Query.Length: > 0 })
        {
            StopSearchRebind();
            Rebind();
            SearchChanged?.Invoke();
        }
    }

    private void OnClipboardWrite(string text)
    {
        if (OverlayHost is Window window) window.Clipboard.SetText(text);
    }

    private void ApplyTitle(string title)
    {
        // The program's title names the element for a screen reader, unless the app named it.
        if (AccessibleName != _titleName) return;
        _titleName = string.IsNullOrEmpty(title) ? DefaultName : title;
        if (AccessibleName == _titleName) return;
        AccessibleName = _titleName;
        if (IsHosted) NotifyAccessibilityPropertyChanged(AccessibilityProperties.Name);
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is not TerminalElement terminal) return;
        _titleName = terminal._titleName;
        Controller = terminal.Controller;
        if (_controller is not null) ApplyTitle(_controller.Title);
    }

    // ──── Keys and text ────

    /// <summary>A Ctrl+Alt key held back until the frame's events are in, on a platform that
    /// cannot say whether a key typed text: AltGr arrives there as Ctrl+Alt with the character it
    /// typed right behind it, and then the character wins.</summary>
    private string? _heldChord;

    /// <summary>The text of a key that was sent as itself (or taken by the application), which
    /// arrives next as text input and must not be typed as well.</summary>
    private string? _keyText;

    private void OnKeyDown(KeyDownEvent e)
    {
        if (_controller is null) return;
        e.StopPropagation = true;
        _keyText = null;
        var mods = e.Modifiers & (KeyModifiers.Shift | KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Super);

        if (HandleOwnKey(e, mods)) { _keyText = e.Text; return; }

        if (IsShortcut(e.Key, mods) && Parent is { } parent)
        {
            var offered = e.CopyUnhandled();
            EventDispatcher.Dispatch(offered, parent);
            // Taken by the application: the program gets neither the key nor what it typed.
            if (offered.StopPropagation || offered.Handled) { _keyText = e.Text; return; }
        }

        bool ctrlAlt = (mods & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == (KeyModifiers.Ctrl | KeyModifiers.Alt);
        bool altGr = (e.ModifierKeys & ModifierKeys.AltGr) != 0;
        // AltGr, or Ctrl+Alt, which Windows takes for AltGr, typed a character: the character is
        // what was meant, and it comes as text.
        if (e.Text is { Length: > 0 } && (altGr || ctrlAlt)) return;

        var key = new TerminalKey(e.Key, e.Modifiers, e.IsRepeat, e.ScanCode, e.LayoutKey, e.ModifierKeys);
        var bytes = _controller.EncodeKey(key);
        if (bytes is null) return;

        if (e.Text is { Length: > 0 })
        {
            // The key is sent as itself (Ctrl+Space, a keypad key, Alt+key where Alt types):
            // what it typed would send it twice.
            _keyText = e.Text;
        }
        else if (e.Text is null && TerminalKeys.TypesText(key))
        {
            // The platform cannot say whether text follows.
            if ((ctrlAlt || altGr) && !TerminalKeys.IsKeypad(e.Key))
            {
                // Possibly AltGr. Events are dispatched before posted work runs, so the text this
                // key typed (if any) arrives before the flush below.
                bool queued = _heldChord is not null;
                _heldChord = bytes;
                if (!queued) Application.InvokeOnMainThread(FlushHeldChord);
                return;
            }
            // A keypad key that types: its text is on the way, and is what it sends anyway
            // outside application keypad mode, which needs the platform to say.
            if (TerminalKeys.IsKeypad(e.Key) && (mods & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0) return;
        }

        SendInput(bytes);
    }

    private void FlushHeldChord()
    {
        if (_heldChord is { } chord && _controller is not null) SendInput(chord);
        _heldChord = null;
    }

    private void OnTextInput(TextInputEvent e)
    {
        if (_controller is null) return;
        e.StopPropagation = true;
        _heldChord = null; // the key typed a character: it was AltGr, not a chord
        _preedit = null;
        if (_keyText is { } sent)
        {
            _keyText = null;
            if (sent == e.Text) return;
        }
        if (!string.IsNullOrEmpty(e.Text)) SendInput(e.Text);
    }

    private void OnTextEditing(TextEditingEvent e)
    {
        if (_controller is null) return;
        e.StopPropagation = true;
        _preedit = string.IsNullOrEmpty(e.Text) ? null : e.Text;
        MarkPaintDirty();
    }

    /// <summary>Input from the user: back to the bottom, drop the selection, send.</summary>
    private void SendInput(string bytes)
    {
        var c = _controller!;
        if (!_snapshot.AtBottom) c.ScrollToBottom();
        if (_selection is not null) { _selection = null; MarkPaintDirty(); }
        RestartBlink();
        _typedSinceAnnounce = true;
        c.Send(bytes);
    }

    /// <summary>The element's own keys: copy, paste, find, and scrolling the scrollback. The
    /// letters are the layout's, so Ctrl+Shift+C is the key labelled C on Dvorak too.</summary>
    private bool HandleOwnKey(KeyDownEvent e, KeyModifiers mods)
    {
        const KeyModifiers CtrlShift = KeyModifiers.Ctrl | KeyModifiers.Shift;
        switch (LetterOf(e))
        {
            case 'c' when mods == CtrlShift:
                Copy();
                return true;
            case 'v' when mods == CtrlShift:
                PasteFromClipboard();
                return true;
            case 'f' when mods == CtrlShift && FindRequested is not null:
                FindRequested();
                return true;
        }
        switch (e.Key)
        {
            case KeyCode.Insert when mods == KeyModifiers.Ctrl:
                Copy();
                return true;
            case KeyCode.Insert when mods == KeyModifiers.Shift:
                PasteFromClipboard();
                return true;
            case KeyCode.PageUp when mods == KeyModifiers.Shift:
                _controller!.ScrollViewport(-Math.Max(1, _snapshot.Rows - 1));
                return true;
            case KeyCode.PageDown when mods == KeyModifiers.Shift:
                _controller!.ScrollViewport(Math.Max(1, _snapshot.Rows - 1));
                return true;
            default:
                return false;
        }
    }

    /// <summary>The Latin letter a key types, lower case: the layout's when the platform said,
    /// else the one at its position on a US keyboard. 0 for any other key.</summary>
    private static char LetterOf(KeyDownEvent e) => e.LayoutKey switch
    {
        >= 'a' and <= 'z' => (char)e.LayoutKey,
        >= 'A' and <= 'Z' => (char)(e.LayoutKey + 32),
        0 when e.Key is >= KeyCode.A and <= KeyCode.Z => (char)('a' + (e.Key - KeyCode.A)),
        _ => '\0',
    };

    /// <summary>Keys an application might bind: offered to it before the program sees them.</summary>
    private static bool IsShortcut(KeyCode key, KeyModifiers mods) =>
        (mods & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Super)) != 0
        || key is >= KeyCode.F1 and <= KeyCode.F12;

    /// <summary>Copies the selection to the window's clipboard. Returns whether there was one.</summary>
    public bool Copy()
    {
        if (_controller is null || _selection is not { } s || OverlayHost is not Window window) return false;
        var text = TextOf(s);
        if (text.Length == 0) return false;
        window.Clipboard.SetText(text);
        return true;
    }

    /// <summary>Pastes the window's clipboard into the program.</summary>
    public void PasteFromClipboard()
    {
        if (_controller is null || OverlayHost is not Window window) return;
        if (window.Clipboard.GetText() is { Length: > 0 } text)
        {
            if (!_snapshot.AtBottom) _controller.ScrollToBottom();
            _typedSinceAnnounce = true;
            _controller.Paste(text);
        }
    }

    /// <summary>The selected text, or null when nothing is selected.</summary>
    public string? SelectedText =>
        _controller is not null && _selection is { } s ? TextOf(s) : null;

    // ──── Focus ────

    private void OnFocusChanged(bool focused)
    {
        if (_controller is null) return;
        if (_controller.EncodeFocus(focused) is { } report) _controller.Send(report);
        if (!focused) { _preedit = null; _heldChord = null; }
        MarkPaintDirty();
    }

    // ──── Pointer ────

    private enum SelectionUnit : byte { Character, Word, Line }

    /// <summary>A selection between two cells. A block (Alt+drag) is the rectangle they span, the
    /// same columns on every line, as Windows Terminal's.</summary>
    private readonly record struct Selection(TerminalPosition Anchor, TerminalPosition Active, SelectionUnit Unit,
        bool Block = false)
    {
        /// <summary>Start inclusive, end exclusive. A range in reading order, widened to whole
        /// words or lines; a block from its top-left cell to past its bottom-right one.</summary>
        public (TerminalPosition Start, TerminalPosition End) Ordered
        {
            get
            {
                if (Block)
                    return (new TerminalPosition(Math.Min(Anchor.Line, Active.Line), Math.Min(Anchor.Column, Active.Column)),
                        new TerminalPosition(Math.Max(Anchor.Line, Active.Line), Math.Max(Anchor.Column, Active.Column) + 1));
                var (a, b) = Anchor <= Active ? (Anchor, Active) : (Active, Anchor);
                return (a, b with { Column = b.Column + 1 });
            }
        }
    }

    /// <summary>The selected text: a range as it reads, wrapped lines joined; a block line by line.</summary>
    private string TextOf(Selection s)
    {
        var (start, end) = s.Ordered;
        if (!s.Block) return _controller!.GetText(start, end);
        var sb = new System.Text.StringBuilder();
        for (long line = start.Line; line <= end.Line; line++)
        {
            if (line > start.Line) sb.Append('\n');
            sb.Append(_controller!.GetText(new TerminalPosition(line, start.Column), new TerminalPosition(line, end.Column)));
        }
        return sb.ToString();
    }

    private Selection? _selection;
    private (TerminalPosition Start, TerminalPosition End)? _selectionUnitAnchor;
    private bool _selecting;
    private TerminalMouseButton _mouseButton;
    private float _wheelRemainder;

    private bool ProgramTakesMouse(KeyModifiers mods) =>
        (_snapshot.Modes & TerminalModes.MouseTracking) != 0 && (mods & KeyModifiers.Shift) == 0;

    /// <summary>The cell under a pointer position (window coordinates), clamped into the grid.</summary>
    private (int Column, int Row) CellAt(Point position)
    {
        var box = ScreenContentBox;
        int col = (int)MathF.Floor((position.X - box.X) / Math.Max(1f, _cellWidth));
        int row = (int)MathF.Floor((position.Y - box.Y) / Math.Max(1f, _cellHeight));
        return (Math.Clamp(col, 0, Math.Max(0, _snapshot.Columns - 1)), Math.Clamp(row, 0, Math.Max(0, _snapshot.Rows - 1)));
    }

    private TerminalPosition PositionOf(int column, int row) =>
        new(_snapshot.LinesDropped + _snapshot.ViewportTop + row, column);

    private static TerminalMouseButton ButtonOf(int button) => button switch
    {
        PointerEvent.LeftButton => TerminalMouseButton.Left,
        PointerEvent.MiddleButton => TerminalMouseButton.Middle,
        PointerEvent.RightButton => TerminalMouseButton.Right,
        _ => TerminalMouseButton.None,
    };

    private void OnPointerDown(PointerDownEvent e)
    {
        if (_controller is null) return;
        Focus();
        e.CapturePointer();
        e.StopPropagation = true;
        var (col, row) = CellAt(e.Position);

        if (ActivateLink(e, col, row)) return;

        if (ProgramTakesMouse(e.Modifiers))
        {
            _mouseButton = ButtonOf(e.Button);
            if (_controller.EncodeMouse(new TerminalMouse(TerminalMouseAction.Down, _mouseButton, col, row, e.Modifiers)) is { } bytes)
                _controller.Send(bytes);
            return;
        }

        if (e.Button != PointerEvent.LeftButton) return;
        var at = PositionOf(col, row);
        var unit = e.ClickCount switch { >= 3 => SelectionUnit.Line, 2 => SelectionUnit.Word, _ => SelectionUnit.Character };
        if (unit == SelectionUnit.Character)
        {
            // Alt makes it a block, as in Windows Terminal and most others.
            _selection = new Selection(at, at, unit, Block: (e.Modifiers & KeyModifiers.Alt) != 0);
            _selectionUnitAnchor = null;
        }
        else
        {
            var span = unit == SelectionUnit.Word ? _controller.WordAt(at) : _controller.LineAt(at);
            _selectionUnitAnchor = span;
            _selection = new Selection(span.Start, span.End with { Column = span.End.Column - 1 }, unit);
        }
        _selecting = true;
        MarkPaintDirty();
    }

    private void OnPointerMove(PointerMoveEvent e)
    {
        if (_controller is null) return;
        TrackHover(e.Position);
        var (col, row) = CellAt(e.Position);

        if (_mouseButton != TerminalMouseButton.None || (!_selecting && ProgramTakesMouse(e.Modifiers)))
        {
            if (_controller.EncodeMouse(new TerminalMouse(TerminalMouseAction.Move, _mouseButton, col, row, e.Modifiers)) is { } bytes)
                _controller.Send(bytes);
            return;
        }

        if (!_selecting || _selection is not { } s) return;
        var at = PositionOf(col, row);
        _selection = s.Unit switch
        {
            SelectionUnit.Character => s with { Active = at },
            _ => Extend(s, at),
        };
        MarkPaintDirty();
    }

    /// <summary>A word or line selection grows by whole words or lines, keeping the one it started on.</summary>
    private Selection Extend(Selection s, TerminalPosition at)
    {
        var anchor = _selectionUnitAnchor!.Value;
        var span = s.Unit == SelectionUnit.Word ? _controller!.WordAt(at) : _controller!.LineAt(at);
        return at < anchor.Start
            ? new Selection(anchor.End with { Column = anchor.End.Column - 1 }, span.Start, s.Unit)
            : new Selection(anchor.Start, span.End with { Column = span.End.Column - 1 }, s.Unit);
    }

    private void OnPointerUp(PointerUpEvent e)
    {
        if (_controller is null) return;
        var (col, row) = CellAt(e.Position);

        if (_mouseButton != TerminalMouseButton.None)
        {
            if (_controller.EncodeMouse(new TerminalMouse(TerminalMouseAction.Up, _mouseButton, col, row, e.Modifiers)) is { } bytes)
                _controller.Send(bytes);
            _mouseButton = TerminalMouseButton.None;
            e.StopPropagation = true;
            return;
        }

        if (!_selecting) return;
        _selecting = false;
        // A click that selected nothing clears the selection rather than selecting one cell.
        if (_selection is { Unit: SelectionUnit.Character } s && s.Anchor == s.Active)
        {
            _selection = null;
            MarkPaintDirty();
        }
        e.StopPropagation = true;
    }

    private void OnScroll(ScrollEvent e)
    {
        if (_controller is null) return;
        e.StopPropagation = true;

        // Whole notches; a precise wheel's fractions add up.
        _wheelRemainder += e.DeltaY;
        int notches = (int)_wheelRemainder;
        if (notches == 0) return;
        _wheelRemainder -= notches;

        if (ProgramTakesMouse(e.Modifiers))
        {
            var (col, row) = CellAt(e.Position);
            var action = notches > 0 ? TerminalMouseAction.WheelUp : TerminalMouseAction.WheelDown;
            for (int i = 0; i < Math.Abs(notches); i++)
                if (_controller.EncodeMouse(new TerminalMouse(action, TerminalMouseButton.None, col, row, e.Modifiers)) is { } bytes)
                    _controller.Send(bytes);
            return;
        }

        if ((_snapshot.Modes & TerminalModes.AlternateScreen) != 0)
        {
            // A full-screen program has no scrollback to show; it scrolls itself on arrow keys.
            var key = new TerminalKey(notches > 0 ? KeyCode.Up : KeyCode.Down, KeyModifiers.None);
            if (_controller.EncodeKey(key) is { } arrow)
                for (int i = 0; i < Math.Abs(notches) * LinesPerNotch; i++) _controller.Send(arrow);
            return;
        }

        _controller.ScrollViewport(-notches * LinesPerNotch);
    }

    private const int LinesPerNotch = 3;
}

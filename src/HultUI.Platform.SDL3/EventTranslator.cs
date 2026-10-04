using System.Runtime.InteropServices;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using SDL;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3;

/// <summary>
/// Translates SDL3 events into HultUI events and dispatches them.
/// </summary>
public sealed unsafe class EventTranslator
{
    public delegate void InputEventHandler(InputEvent evt);
    public event InputEventHandler? OnInputEvent;

    /// <summary>
    /// Factor applied to incoming SDL window-coordinate positions to convert
    /// them into the layout units the element tree lives in. Kept current by
    /// <see cref="SDL3Window.UpdateSize"/>; defaults to 1 until the first sync.
    /// Scroll wheel deltas are left untouched — they are scroll amounts, not
    /// positions.
    /// </summary>
    internal float InputScale { get; set; } = 1f;

    /// <summary>The window this translator's events are for: whether its text input is on
    /// decides what a key's <see cref="KeyDownEvent.Text"/> can say.</summary>
    internal SDL_Window* Window { get; set; }

    internal void ProcessEvent(SDL_Event* evt)
    {
        float s = InputScale;
        switch (evt->type)
        {
            case (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                OnInputEvent?.Invoke(new InputEvent.MouseButtonInput(
                    evt->button.x * s, evt->button.y * s,
                    MapMouseButton(evt->button.button),
                    IsDown: true,
                    TranslateKeyMod(SDL_GetModState())));
                break;

            case (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                OnInputEvent?.Invoke(new InputEvent.MouseButtonInput(
                    evt->button.x * s, evt->button.y * s,
                    MapMouseButton(evt->button.button),
                    IsDown: false,
                    TranslateKeyMod(SDL_GetModState())));
                break;

            case (uint)SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                OnInputEvent?.Invoke(new InputEvent.MouseMoveInput(
                    evt->motion.x * s, evt->motion.y * s,
                    evt->motion.xrel * s, evt->motion.yrel * s,
                    TranslateKeyMod(SDL_GetModState())));
                break;

            case (uint)SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE:
                OnInputEvent?.Invoke(new InputEvent.PointerLeftWindowInput());
                break;

            case (uint)SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                OnInputEvent?.Invoke(new InputEvent.MouseWheelInput(
                    evt->wheel.mouse_x * s, evt->wheel.mouse_y * s,
                    evt->wheel.x, evt->wheel.y,
                    TranslateKeyMod(SDL_GetModState())));
                break;

            case (uint)SDL_EventType.SDL_EVENT_KEY_DOWN:
                OnInputEvent?.Invoke(TranslateKeyDown(evt));
                break;

            case (uint)SDL_EventType.SDL_EVENT_KEY_UP:
                OnInputEvent?.Invoke(new InputEvent.KeyInput(
                    TranslateScancode(evt->key.scancode),
                    TranslateKeyMod(evt->key.mod),
                    IsDown: false,
                    IsRepeat: false,
                    ScanCode: (ushort)evt->key.scancode));
                break;

            case (uint)SDL_EventType.SDL_EVENT_TEXT_INPUT:
                var text = Marshal.PtrToStringUTF8((IntPtr)evt->text.text);
                if (text is not null)
                    OnInputEvent?.Invoke(new InputEvent.TextInputInput(text));
                break;

            case (uint)SDL_EventType.SDL_EVENT_TEXT_EDITING:
            {
                // edit.text is owned by SDL and only valid during this callback — copy it.
                var editText = Marshal.PtrToStringUTF8((IntPtr)evt->edit.text) ?? "";
                OnInputEvent?.Invoke(new InputEvent.TextEditingInput(
                    editText, evt->edit.start, evt->edit.length));
                break;
            }

            case (uint)SDL_EventType.SDL_EVENT_DROP_BEGIN:
                OnInputEvent?.Invoke(new InputEvent.DropBeginInput());
                break;

            case (uint)SDL_EventType.SDL_EVENT_DROP_POSITION:
                OnInputEvent?.Invoke(new InputEvent.DropPositionInput(evt->drop.x * s, evt->drop.y * s,
                    TranslateKeyMod(SDL_GetModState())));
                break;

            case (uint)SDL_EventType.SDL_EVENT_DROP_FILE:
            {
                var path = Marshal.PtrToStringUTF8((IntPtr)evt->drop.data);
                if (path is not null)
                    OnInputEvent?.Invoke(new InputEvent.DropFileInput(path));
                break;
            }

            case (uint)SDL_EventType.SDL_EVENT_DROP_TEXT:
            {
                var dropText = Marshal.PtrToStringUTF8((IntPtr)evt->drop.data);
                if (dropText is not null)
                    OnInputEvent?.Invoke(new InputEvent.DropTextInput(dropText));
                break;
            }

            case (uint)SDL_EventType.SDL_EVENT_DROP_COMPLETE:
                OnInputEvent?.Invoke(new InputEvent.DropCompleteInput(TranslateKeyMod(SDL_GetModState())));
                break;
        }
    }

    private static int MapMouseButton(byte sdlButton) => sdlButton switch
    {
        1 => 0, // SDL_BUTTON_LEFT → 0
        2 => 1, // SDL_BUTTON_MIDDLE → 1
        3 => 2, // SDL_BUTTON_RIGHT → 2
        _ => sdlButton // SDL_BUTTON_X1 → 4 (Back), SDL_BUTTON_X2 → 5 (Forward)
    };

    private InputEvent.KeyInput TranslateKeyDown(SDL_Event* evt)
    {
        var scancode = evt->key.scancode;
        var mod = evt->key.mod;
        var modifiers = TranslateKeyMod(mod);
        var keys = TranslateModifierKeys(mod);
        var text = TextAfterKey();

        if ((mod & SDL_Keymod.SDL_KMOD_MODE) != 0)
        {
            // X11 and Wayland: AltGr is ISO_Level3_Shift, which SDL reports as MODE, not Alt.
            keys |= ModifierKeys.AltGr;
        }
        else if (OperatingSystem.IsWindows() && (keys & ModifierKeys.AltRight) != 0 && text is { Length: > 0 })
        {
            // Windows: SDL drops the left Ctrl that Windows fakes ahead of AltGr, so AltGr arrives
            // as right Alt alone, which a US keyboard's right Alt also is. What tells them apart
            // is the text: a key pressed with Alt types nothing (Windows sends WM_SYSCHAR, which
            // SDL makes no text of), so a right Alt that typed something was AltGr. AltGr on a key
            // that types nothing stays right Alt.
            keys = (keys & ~ModifierKeys.AltRight) | ModifierKeys.AltGr;
            if ((keys & ModifierKeys.AltLeft) == 0) modifiers &= ~KeyModifiers.Alt;
        }

        return new InputEvent.KeyInput(
            TranslateScancode(scancode),
            modifiers,
            IsDown: true,
            IsRepeat: evt->key.repeat,
            ScanCode: (ushort)scancode,
            LayoutKey: LayoutKeyOf(scancode, mod, (keys & ModifierKeys.AltGr) != 0),
            ModifierKeys: keys,
            Text: text);
    }

    /// <summary>
    /// The text the key being translated typed: SDL queues it right behind the key, in the same
    /// pump, on Windows (WM_CHAR, posted by TranslateMessage), X11 and Wayland. Empty when text
    /// input is off, since then none follows; null when an input method has the key.
    /// </summary>
    /// <remarks>On Windows a pump that has already taken more than three new messages stops,
    /// and the character can land in the next one; the key then reads as typing nothing.</remarks>
    private string? TextAfterKey()
    {
        if (Window == null || !SDL_TextInputActive(Window)) return "";
        SDL_Event next;
        // The head of the queue, whatever its type: a type-filtered peek would skip ahead to the
        // text of a later key.
        if (SDL_PeepEvents(&next, 1, SDL_EventAction.SDL_PEEKEVENT,
                (uint)SDL_EventType.SDL_EVENT_FIRST, (uint)SDL_EventType.SDL_EVENT_LAST) != 1)
            return "";
        return next.type switch
        {
            (uint)SDL_EventType.SDL_EVENT_TEXT_INPUT => Marshal.PtrToStringUTF8((IntPtr)next.text.text) ?? "",
            (uint)SDL_EventType.SDL_EVENT_TEXT_EDITING or (uint)SDL_EventType.SDL_EVENT_TEXT_EDITING_CANDIDATES => null,
            _ => "",
        };
    }

    /// <summary>
    /// The character the key types on the layout, with Shift, CapsLock and AltGr applied (see
    /// <see cref="KeyDownEvent.LayoutKey"/>). SDL's own key code applies none of them, so the
    /// levels are read from its keymap. A letter key is named by its Latin letter, as SDL's key
    /// code names it, so Ctrl+C is ^C on a Russian layout as well.
    /// </summary>
    private static int LayoutKeyOf(SDL_Scancode scancode, SDL_Keymod mod, bool altGr)
    {
        uint basic = (uint)SDL_GetKeyFromScancode(scancode, SDL_Keymod.SDL_KMOD_NONE, true);
        if (!IsCharacter(basic)) return 0;
        bool shift = (mod & SDL_Keymod.SDL_KMOD_SHIFT) != 0, caps = (mod & SDL_Keymod.SDL_KMOD_CAPS) != 0;

        var level = (shift ? SDL_Keymod.SDL_KMOD_SHIFT : 0) | (caps ? SDL_Keymod.SDL_KMOD_CAPS : 0);
        if (altGr && Typed(scancode, level | SDL_Keymod.SDL_KMOD_MODE) is var third and not 0) return (int)third;
        if (basic is >= 'a' and <= 'z') return (int)(shift ^ caps ? basic - 32 : basic);
        if (level != 0 && Typed(scancode, level) is var shifted and not 0) return (int)shifted;
        return (int)basic;

        static uint Typed(SDL_Scancode scancode, SDL_Keymod level)
        {
            uint k = (uint)SDL_GetKeyFromScancode(scancode, level, false);
            return IsCharacter(k) ? k : 0;
        }
    }

    /// <summary>A key code that is a character: not a scancode-named or extended key, and not a
    /// control character (Enter, Tab, Backspace, Escape, Delete).</summary>
    private static bool IsCharacter(uint keycode) =>
        keycode >= 0x20 && keycode != 0x7F && (keycode & (SdlkScancodeMask | SdlkExtendedMask)) == 0;

    private const uint SdlkScancodeMask = 1u << 30, SdlkExtendedMask = 1u << 29;

    private static ModifierKeys TranslateModifierKeys(SDL_Keymod mod)
    {
        var keys = ModifierKeys.None;
        if ((mod & SDL_Keymod.SDL_KMOD_LSHIFT) != 0) keys |= ModifierKeys.ShiftLeft;
        if ((mod & SDL_Keymod.SDL_KMOD_RSHIFT) != 0) keys |= ModifierKeys.ShiftRight;
        if ((mod & SDL_Keymod.SDL_KMOD_LCTRL) != 0) keys |= ModifierKeys.CtrlLeft;
        if ((mod & SDL_Keymod.SDL_KMOD_RCTRL) != 0) keys |= ModifierKeys.CtrlRight;
        if ((mod & SDL_Keymod.SDL_KMOD_LALT) != 0) keys |= ModifierKeys.AltLeft;
        if ((mod & SDL_Keymod.SDL_KMOD_RALT) != 0) keys |= ModifierKeys.AltRight;
        if ((mod & SDL_Keymod.SDL_KMOD_LGUI) != 0) keys |= ModifierKeys.SuperLeft;
        if ((mod & SDL_Keymod.SDL_KMOD_RGUI) != 0) keys |= ModifierKeys.SuperRight;
        return keys;
    }

    private static KeyModifiers TranslateKeyMod(SDL_Keymod mod)
    {
        var result = KeyModifiers.None;
        if ((mod & SDL_Keymod.SDL_KMOD_SHIFT) != 0) result |= KeyModifiers.Shift;
        if ((mod & SDL_Keymod.SDL_KMOD_CTRL) != 0) result |= KeyModifiers.Ctrl;
        if ((mod & SDL_Keymod.SDL_KMOD_ALT) != 0) result |= KeyModifiers.Alt;
        if ((mod & SDL_Keymod.SDL_KMOD_GUI) != 0) result |= KeyModifiers.Super;
        if ((mod & SDL_Keymod.SDL_KMOD_CAPS) != 0) result |= KeyModifiers.CapsLock;
        if ((mod & SDL_Keymod.SDL_KMOD_NUM) != 0) result |= KeyModifiers.NumLock;
        return result;
    }

    private static KeyCode TranslateScancode(SDL_Scancode sc) => sc switch
    {
        SDL_Scancode.SDL_SCANCODE_A => KeyCode.A,
        SDL_Scancode.SDL_SCANCODE_B => KeyCode.B,
        SDL_Scancode.SDL_SCANCODE_C => KeyCode.C,
        SDL_Scancode.SDL_SCANCODE_D => KeyCode.D,
        SDL_Scancode.SDL_SCANCODE_E => KeyCode.E,
        SDL_Scancode.SDL_SCANCODE_F => KeyCode.F,
        SDL_Scancode.SDL_SCANCODE_G => KeyCode.G,
        SDL_Scancode.SDL_SCANCODE_H => KeyCode.H,
        SDL_Scancode.SDL_SCANCODE_I => KeyCode.I,
        SDL_Scancode.SDL_SCANCODE_J => KeyCode.J,
        SDL_Scancode.SDL_SCANCODE_K => KeyCode.K,
        SDL_Scancode.SDL_SCANCODE_L => KeyCode.L,
        SDL_Scancode.SDL_SCANCODE_M => KeyCode.M,
        SDL_Scancode.SDL_SCANCODE_N => KeyCode.N,
        SDL_Scancode.SDL_SCANCODE_O => KeyCode.O,
        SDL_Scancode.SDL_SCANCODE_P => KeyCode.P,
        SDL_Scancode.SDL_SCANCODE_Q => KeyCode.Q,
        SDL_Scancode.SDL_SCANCODE_R => KeyCode.R,
        SDL_Scancode.SDL_SCANCODE_S => KeyCode.S,
        SDL_Scancode.SDL_SCANCODE_T => KeyCode.T,
        SDL_Scancode.SDL_SCANCODE_U => KeyCode.U,
        SDL_Scancode.SDL_SCANCODE_V => KeyCode.V,
        SDL_Scancode.SDL_SCANCODE_W => KeyCode.W,
        SDL_Scancode.SDL_SCANCODE_X => KeyCode.X,
        SDL_Scancode.SDL_SCANCODE_Y => KeyCode.Y,
        SDL_Scancode.SDL_SCANCODE_Z => KeyCode.Z,
        SDL_Scancode.SDL_SCANCODE_0 => KeyCode.Digit0,
        SDL_Scancode.SDL_SCANCODE_1 => KeyCode.Digit1,
        SDL_Scancode.SDL_SCANCODE_2 => KeyCode.Digit2,
        SDL_Scancode.SDL_SCANCODE_3 => KeyCode.Digit3,
        SDL_Scancode.SDL_SCANCODE_4 => KeyCode.Digit4,
        SDL_Scancode.SDL_SCANCODE_5 => KeyCode.Digit5,
        SDL_Scancode.SDL_SCANCODE_6 => KeyCode.Digit6,
        SDL_Scancode.SDL_SCANCODE_7 => KeyCode.Digit7,
        SDL_Scancode.SDL_SCANCODE_8 => KeyCode.Digit8,
        SDL_Scancode.SDL_SCANCODE_9 => KeyCode.Digit9,
        SDL_Scancode.SDL_SCANCODE_F1 => KeyCode.F1,
        SDL_Scancode.SDL_SCANCODE_F2 => KeyCode.F2,
        SDL_Scancode.SDL_SCANCODE_F3 => KeyCode.F3,
        SDL_Scancode.SDL_SCANCODE_F4 => KeyCode.F4,
        SDL_Scancode.SDL_SCANCODE_F5 => KeyCode.F5,
        SDL_Scancode.SDL_SCANCODE_F6 => KeyCode.F6,
        SDL_Scancode.SDL_SCANCODE_F7 => KeyCode.F7,
        SDL_Scancode.SDL_SCANCODE_F8 => KeyCode.F8,
        SDL_Scancode.SDL_SCANCODE_F9 => KeyCode.F9,
        SDL_Scancode.SDL_SCANCODE_F10 => KeyCode.F10,
        SDL_Scancode.SDL_SCANCODE_F11 => KeyCode.F11,
        SDL_Scancode.SDL_SCANCODE_F12 => KeyCode.F12,
        SDL_Scancode.SDL_SCANCODE_UP => KeyCode.Up,
        SDL_Scancode.SDL_SCANCODE_DOWN => KeyCode.Down,
        SDL_Scancode.SDL_SCANCODE_LEFT => KeyCode.Left,
        SDL_Scancode.SDL_SCANCODE_RIGHT => KeyCode.Right,
        SDL_Scancode.SDL_SCANCODE_HOME => KeyCode.Home,
        SDL_Scancode.SDL_SCANCODE_END => KeyCode.End,
        SDL_Scancode.SDL_SCANCODE_PAGEUP => KeyCode.PageUp,
        SDL_Scancode.SDL_SCANCODE_PAGEDOWN => KeyCode.PageDown,
        SDL_Scancode.SDL_SCANCODE_RETURN => KeyCode.Enter,
        SDL_Scancode.SDL_SCANCODE_ESCAPE => KeyCode.Escape,
        SDL_Scancode.SDL_SCANCODE_BACKSPACE => KeyCode.Backspace,
        SDL_Scancode.SDL_SCANCODE_TAB => KeyCode.Tab,
        SDL_Scancode.SDL_SCANCODE_SPACE => KeyCode.Space,
        SDL_Scancode.SDL_SCANCODE_DELETE => KeyCode.Delete,
        SDL_Scancode.SDL_SCANCODE_INSERT => KeyCode.Insert,
        SDL_Scancode.SDL_SCANCODE_LSHIFT => KeyCode.ShiftLeft,
        SDL_Scancode.SDL_SCANCODE_RSHIFT => KeyCode.ShiftRight,
        SDL_Scancode.SDL_SCANCODE_LCTRL => KeyCode.CtrlLeft,
        SDL_Scancode.SDL_SCANCODE_RCTRL => KeyCode.CtrlRight,
        SDL_Scancode.SDL_SCANCODE_LALT => KeyCode.AltLeft,
        SDL_Scancode.SDL_SCANCODE_RALT => KeyCode.AltRight,
        SDL_Scancode.SDL_SCANCODE_LGUI => KeyCode.SuperLeft,
        SDL_Scancode.SDL_SCANCODE_RGUI => KeyCode.SuperRight,
        SDL_Scancode.SDL_SCANCODE_CAPSLOCK => KeyCode.CapsLock,
        SDL_Scancode.SDL_SCANCODE_NUMLOCKCLEAR => KeyCode.NumLock,
        SDL_Scancode.SDL_SCANCODE_SCROLLLOCK => KeyCode.ScrollLock,
        SDL_Scancode.SDL_SCANCODE_PRINTSCREEN => KeyCode.PrintScreen,
        SDL_Scancode.SDL_SCANCODE_PAUSE => KeyCode.Pause,
        SDL_Scancode.SDL_SCANCODE_COMMA => KeyCode.Comma,
        SDL_Scancode.SDL_SCANCODE_PERIOD => KeyCode.Period,
        SDL_Scancode.SDL_SCANCODE_SLASH => KeyCode.Slash,
        SDL_Scancode.SDL_SCANCODE_BACKSLASH => KeyCode.Backslash,
        SDL_Scancode.SDL_SCANCODE_SEMICOLON => KeyCode.Semicolon,
        SDL_Scancode.SDL_SCANCODE_APOSTROPHE => KeyCode.Quote,
        SDL_Scancode.SDL_SCANCODE_LEFTBRACKET => KeyCode.BracketLeft,
        SDL_Scancode.SDL_SCANCODE_RIGHTBRACKET => KeyCode.BracketRight,
        SDL_Scancode.SDL_SCANCODE_MINUS => KeyCode.Minus,
        SDL_Scancode.SDL_SCANCODE_EQUALS => KeyCode.Equals,
        SDL_Scancode.SDL_SCANCODE_GRAVE => KeyCode.Backtick,
        SDL_Scancode.SDL_SCANCODE_MENU => KeyCode.Menu,
        // The context-menu key beside the right Ctrl. SDL calls it APPLICATION and reserves
        // MENU for the HID "Menu" usage, which no PC keyboard carries — so without this the
        // key everyone actually presses arrived as Unknown.
        SDL_Scancode.SDL_SCANCODE_APPLICATION => KeyCode.Menu,
        SDL_Scancode.SDL_SCANCODE_KP_0 => KeyCode.Numpad0,
        SDL_Scancode.SDL_SCANCODE_KP_1 => KeyCode.Numpad1,
        SDL_Scancode.SDL_SCANCODE_KP_2 => KeyCode.Numpad2,
        SDL_Scancode.SDL_SCANCODE_KP_3 => KeyCode.Numpad3,
        SDL_Scancode.SDL_SCANCODE_KP_4 => KeyCode.Numpad4,
        SDL_Scancode.SDL_SCANCODE_KP_5 => KeyCode.Numpad5,
        SDL_Scancode.SDL_SCANCODE_KP_6 => KeyCode.Numpad6,
        SDL_Scancode.SDL_SCANCODE_KP_7 => KeyCode.Numpad7,
        SDL_Scancode.SDL_SCANCODE_KP_8 => KeyCode.Numpad8,
        SDL_Scancode.SDL_SCANCODE_KP_9 => KeyCode.Numpad9,
        SDL_Scancode.SDL_SCANCODE_KP_PERIOD => KeyCode.NumpadDecimal,
        SDL_Scancode.SDL_SCANCODE_KP_PLUS => KeyCode.NumpadAdd,
        SDL_Scancode.SDL_SCANCODE_KP_MINUS => KeyCode.NumpadSubtract,
        SDL_Scancode.SDL_SCANCODE_KP_MULTIPLY => KeyCode.NumpadMultiply,
        SDL_Scancode.SDL_SCANCODE_KP_DIVIDE => KeyCode.NumpadDivide,
        SDL_Scancode.SDL_SCANCODE_KP_ENTER => KeyCode.NumpadEnter,
        SDL_Scancode.SDL_SCANCODE_KP_EQUALS => KeyCode.NumpadEquals,
        SDL_Scancode.SDL_SCANCODE_F13 => KeyCode.F13,
        SDL_Scancode.SDL_SCANCODE_F14 => KeyCode.F14,
        SDL_Scancode.SDL_SCANCODE_F15 => KeyCode.F15,
        SDL_Scancode.SDL_SCANCODE_F16 => KeyCode.F16,
        SDL_Scancode.SDL_SCANCODE_F17 => KeyCode.F17,
        SDL_Scancode.SDL_SCANCODE_F18 => KeyCode.F18,
        SDL_Scancode.SDL_SCANCODE_F19 => KeyCode.F19,
        SDL_Scancode.SDL_SCANCODE_F20 => KeyCode.F20,
        SDL_Scancode.SDL_SCANCODE_F21 => KeyCode.F21,
        SDL_Scancode.SDL_SCANCODE_F22 => KeyCode.F22,
        SDL_Scancode.SDL_SCANCODE_F23 => KeyCode.F23,
        SDL_Scancode.SDL_SCANCODE_F24 => KeyCode.F24,
        _ => KeyCode.Unknown
    };
}

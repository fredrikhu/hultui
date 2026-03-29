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

    internal void ProcessEvent(SDL_Event* evt)
    {
        switch (evt->type)
        {
            case (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                OnInputEvent?.Invoke(new InputEvent.MouseButtonInput(
                    evt->button.x, evt->button.y,
                    MapMouseButton(evt->button.button),
                    IsDown: true,
                    TranslateKeyMod(SDL_GetModState())));
                break;

            case (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                OnInputEvent?.Invoke(new InputEvent.MouseButtonInput(
                    evt->button.x, evt->button.y,
                    MapMouseButton(evt->button.button),
                    IsDown: false,
                    TranslateKeyMod(SDL_GetModState())));
                break;

            case (uint)SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                OnInputEvent?.Invoke(new InputEvent.MouseMoveInput(
                    evt->motion.x, evt->motion.y,
                    evt->motion.xrel, evt->motion.yrel,
                    TranslateKeyMod(SDL_GetModState())));
                break;

            case (uint)SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                OnInputEvent?.Invoke(new InputEvent.MouseWheelInput(
                    evt->wheel.mouse_x, evt->wheel.mouse_y,
                    evt->wheel.x, evt->wheel.y,
                    TranslateKeyMod(SDL_GetModState())));
                break;

            case (uint)SDL_EventType.SDL_EVENT_KEY_DOWN:
                OnInputEvent?.Invoke(new InputEvent.KeyInput(
                    TranslateScancode(evt->key.scancode),
                    TranslateKeyMod(evt->key.mod),
                    IsDown: true,
                    IsRepeat: evt->key.repeat));
                break;

            case (uint)SDL_EventType.SDL_EVENT_KEY_UP:
                OnInputEvent?.Invoke(new InputEvent.KeyInput(
                    TranslateScancode(evt->key.scancode),
                    TranslateKeyMod(evt->key.mod),
                    IsDown: false,
                    IsRepeat: false));
                break;

            case (uint)SDL_EventType.SDL_EVENT_TEXT_INPUT:
                var text = Marshal.PtrToStringUTF8((IntPtr)evt->text.text);
                if (text is not null)
                    OnInputEvent?.Invoke(new InputEvent.TextInputInput(text));
                break;

            case (uint)SDL_EventType.SDL_EVENT_DROP_BEGIN:
                OnInputEvent?.Invoke(new InputEvent.DropBeginInput());
                break;

            case (uint)SDL_EventType.SDL_EVENT_DROP_POSITION:
                OnInputEvent?.Invoke(new InputEvent.DropPositionInput(evt->drop.x, evt->drop.y));
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
                OnInputEvent?.Invoke(new InputEvent.DropCompleteInput());
                break;
        }
    }

    private static int MapMouseButton(byte sdlButton) => sdlButton switch
    {
        1 => 0, // SDL_BUTTON_LEFT → 0
        2 => 1, // SDL_BUTTON_MIDDLE → 1
        3 => 2, // SDL_BUTTON_RIGHT → 2
        _ => sdlButton
    };

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
        _ => KeyCode.Unknown
    };
}

/// <summary>
/// Platform-level input events, before hit-testing.
/// </summary>
public abstract record InputEvent
{
    public record MouseButtonInput(float X, float Y, int Button, bool IsDown, KeyModifiers Modifiers) : InputEvent;
    public record MouseMoveInput(float X, float Y, float DeltaX, float DeltaY, KeyModifiers Modifiers) : InputEvent;
    public record MouseWheelInput(float MouseX, float MouseY, float DeltaX, float DeltaY, KeyModifiers Modifiers) : InputEvent;
    public record KeyInput(KeyCode KeyCode, KeyModifiers Modifiers, bool IsDown, bool IsRepeat) : InputEvent;
    public record TextInputInput(string Text) : InputEvent;
    public record DropBeginInput() : InputEvent;
    public record DropPositionInput(float X, float Y) : InputEvent;
    public record DropFileInput(string Path) : InputEvent;
    public record DropTextInput(string Text) : InputEvent;
    public record DropCompleteInput() : InputEvent;
}

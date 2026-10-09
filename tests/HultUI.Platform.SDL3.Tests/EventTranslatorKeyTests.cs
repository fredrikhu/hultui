using System.Runtime.InteropServices;
using HultUI.Core.Events;
using SDL;
using Xunit;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>
/// A key as the SDL backend reports it: the layout's character (seam K1), which modifier keys
/// and AltGr (K2), and the text the keystroke typed (K4). Real SDL, on its dummy video driver,
/// with the events a platform backend would queue pushed onto SDL's own queue. The dummy driver
/// has SDL's default US keymap.
/// </summary>
[Collection(SdlDummyDriver.Collection)]
public sealed unsafe class EventTranslatorKeyTests : IDisposable
{
    private readonly SDL_Window* _window;
    private readonly EventTranslator _translator = new();
    private readonly List<InputEvent> _events = [];
    private readonly List<nint> _strings = [];

    public EventTranslatorKeyTests()
    {
        SDL_SetHint(SDL_HINT_VIDEO_DRIVER, "dummy");
        Assert.True(SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO), SDL_GetError());
        _window = SDL_CreateWindow("keys", 100, 100, SDL_WindowFlags.SDL_WINDOW_HIDDEN);
        Assert.True(_window != null, SDL_GetError());
        _translator.Window = _window;
        _translator.OnInputEvent += _events.Add;
        DrainQueue(translate: false);
    }

    public void Dispose()
    {
        SDL_DestroyWindow(_window);
        SDL_Quit();
        foreach (var s in _strings) Marshal.FreeCoTaskMem(s);
    }

    private void Key(SDL_Scancode scancode, SDL_Keymod mod = SDL_Keymod.SDL_KMOD_NONE)
    {
        var e = new SDL_Event();
        e.key.type = SDL_EventType.SDL_EVENT_KEY_DOWN;
        e.key.windowID = SDL_GetWindowID(_window);
        e.key.scancode = scancode;
        e.key.key = SDL_GetKeyFromScancode(scancode, SDL_Keymod.SDL_KMOD_NONE, true);
        e.key.mod = mod;
        e.key.down = true;
        Assert.True(SDL_PushEvent(&e), SDL_GetError());
    }

    private void Text(string text)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(text);
        _strings.Add(utf8);
        var e = new SDL_Event();
        e.text.type = SDL_EventType.SDL_EVENT_TEXT_INPUT;
        e.text.windowID = SDL_GetWindowID(_window);
        e.text.text = (byte*)utf8;
        Assert.True(SDL_PushEvent(&e), SDL_GetError());
    }

    /// <summary>Takes SDL's queue the way the application loop does, a translator per window.</summary>
    private void DrainQueue(bool translate = true)
    {
        SDL_Event e;
        while (SDL_PollEvent(&e))
            if (translate) _translator.ProcessEvent(&e);
    }

    private InputEvent.KeyInput OnlyKey()
    {
        DrainQueue();
        return Assert.Single(_events.OfType<InputEvent.KeyInput>());
    }

    [Fact]
    public void TheTextAKeyTyped_IsOnTheKey_AndStillSentAfterIt()
    {
        SDL_StartTextInput(_window);
        Key(SDL_Scancode.SDL_SCANCODE_A, SDL_Keymod.SDL_KMOD_LSHIFT);
        Text("A");
        DrainQueue();

        var key = Assert.IsType<InputEvent.KeyInput>(_events[0]);
        Assert.Equal("A", key.Text);
        Assert.Equal(new InputEvent.TextInputInput("A"), _events[1]);
    }

    [Fact]
    public void AKeyThatTypedNothing_SaysSo()
    {
        SDL_StartTextInput(_window);
        Key(SDL_Scancode.SDL_SCANCODE_UP);
        Key(SDL_Scancode.SDL_SCANCODE_B);
        Text("b");
        DrainQueue();

        var keys = _events.OfType<InputEvent.KeyInput>().ToList();
        Assert.Equal("", keys[0].Text); // the text behind it is the next key's
        Assert.Equal("b", keys[1].Text);
    }

    [Fact]
    public void WithTextInputOff_NoTextFollows()
    {
        SDL_StopTextInput(_window);
        Key(SDL_Scancode.SDL_SCANCODE_A);
        Assert.Equal("", OnlyKey().Text);
    }

    [Fact]
    public void TheLayoutKey_HasShiftApplied_AndALetterItsCase()
    {
        Key(SDL_Scancode.SDL_SCANCODE_COMMA, SDL_Keymod.SDL_KMOD_LSHIFT);
        Key(SDL_Scancode.SDL_SCANCODE_A, SDL_Keymod.SDL_KMOD_LSHIFT | SDL_Keymod.SDL_KMOD_LCTRL);
        Key(SDL_Scancode.SDL_SCANCODE_A, SDL_Keymod.SDL_KMOD_CAPS);
        Key(SDL_Scancode.SDL_SCANCODE_1);
        Key(SDL_Scancode.SDL_SCANCODE_RETURN);
        Key(SDL_Scancode.SDL_SCANCODE_KP_7, SDL_Keymod.SDL_KMOD_NUM);
        DrainQueue();

        Assert.Equal(['<', 'A', 'A', '1', 0, 0], _events.OfType<InputEvent.KeyInput>().Select(k => k.LayoutKey));
    }

    [Fact]
    public void LeftAndRight_AreTold()
    {
        Key(SDL_Scancode.SDL_SCANCODE_A, SDL_Keymod.SDL_KMOD_RCTRL | SDL_Keymod.SDL_KMOD_LALT | SDL_Keymod.SDL_KMOD_RSHIFT);
        var key = OnlyKey();
        Assert.Equal(ModifierKeys.CtrlRight | ModifierKeys.AltLeft | ModifierKeys.ShiftRight, key.ModifierKeys);
        Assert.Equal(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift, key.Modifiers);
    }

    [Fact]
    public void Level3Shift_IsAltGr_NotAlt()
    {
        // X11 and Wayland: AltGr is ISO_Level3_Shift, SDL's MODE.
        Key(SDL_Scancode.SDL_SCANCODE_2, SDL_Keymod.SDL_KMOD_MODE);
        var key = OnlyKey();
        Assert.Equal(ModifierKeys.AltGr, key.ModifierKeys);
        Assert.Equal(KeyModifiers.None, key.Modifiers);
    }

    [Fact]
    public void OnWindows_ARightAltThatTypes_IsAltGr()
    {
        // SDL reports Windows' AltGr as right Alt alone: it drops the left Ctrl Windows fakes.
        if (!OperatingSystem.IsWindows()) return;
        SDL_StartTextInput(_window);
        Key(SDL_Scancode.SDL_SCANCODE_2, SDL_Keymod.SDL_KMOD_RALT);
        Text("@");
        var key = OnlyKey();
        Assert.Equal(ModifierKeys.AltGr, key.ModifierKeys);
        Assert.Equal(KeyModifiers.None, key.Modifiers);
        Assert.Equal("@", key.Text);
    }

    [Fact]
    public void ARightAltThatTypesNothing_IsAlt()
    {
        // A US keyboard's right Alt: Alt+a types nothing on Windows (WM_SYSCHAR).
        SDL_StartTextInput(_window);
        Key(SDL_Scancode.SDL_SCANCODE_A, SDL_Keymod.SDL_KMOD_RALT);
        var key = OnlyKey();
        Assert.Equal(ModifierKeys.AltRight, key.ModifierKeys);
        Assert.Equal(KeyModifiers.Alt, key.Modifiers);
    }

    [Fact]
    public void TheKeypadAndF13Up_HaveNames()
    {
        Key(SDL_Scancode.SDL_SCANCODE_KP_7);
        Key(SDL_Scancode.SDL_SCANCODE_KP_ENTER);
        Key(SDL_Scancode.SDL_SCANCODE_KP_EQUALS);
        Key(SDL_Scancode.SDL_SCANCODE_F13);
        Key(SDL_Scancode.SDL_SCANCODE_F24);
        DrainQueue();
        Assert.Equal([KeyCode.Numpad7, KeyCode.NumpadEnter, KeyCode.NumpadEquals, KeyCode.F13, KeyCode.F24],
            _events.OfType<InputEvent.KeyInput>().Select(k => k.KeyCode));
    }
}

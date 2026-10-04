using System.Runtime.InteropServices;
using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Terminal.Emulation;
using Xunit;
using static HultUI.Terminal.Tests.TerminalControllerTests;

namespace HultUI.Terminal.Tests;

/// <summary>
/// Keys by layout, AltGr, the keypad, and win32-input-mode on ConPTY: what the program receives
/// for a key as the platform describes it (its layout character, which modifier keys, the text
/// it typed).
/// </summary>
public class TerminalKeyboardTests
{
    /// <summary>What reaches the program, waited for without pumping: work posted to the UI
    /// thread (the AltGr hold) does not run, so this sees only what was sent at once.</summary>
    private static string SentAtOnce(Term t, string expected)
    {
        SpinWait.SpinUntil(() => t.Connection.Received.Contains(expected, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        return t.Connection.TakeReceived();
    }

    private static string Win32Press(int vk, int sc, int uc, int state) =>
        $"\x1b[{vk};{sc};{uc};1;{state};1_\x1b[{vk};{sc};{uc};0;{state};1_";

    // ──── The layout ────

    [Fact]
    public void CtrlAndALetter_IsTheLayoutsLetter_NotThePosition()
    {
        // QWERTZ: the key labelled Z is where a US keyboard has Y.
        using var t = Term.Shown();
        t.Window.PressKey(KeyCode.Y, KeyModifiers.Ctrl, layoutKey: 'z', text: "");
        Assert.Equal(Escape("\x1a"), Escape(t.Expect("\x1a")));
    }

    [Fact]
    public void AltShiftAndAKey_SendsTheShiftedCharacter()
    {
        // readline's M-< (beginning of history) on a US keyboard; it used to be ESC ','.
        using var t = Term.Shown();
        t.Window.PressKey(KeyCode.Comma, KeyModifiers.Alt | KeyModifiers.Shift, layoutKey: '<', text: "");
        Assert.Equal(Escape("\x1b<"), Escape(t.Expect("<")));
    }

    [Fact]
    public void AltAndAKeyWithNoName_SendsTheLayoutsCharacter()
    {
        // The ISO key beside the left Shift ('<' on Swedish), which KeyCode has no name for.
        using var t = Term.Shown();
        t.Window.PressKey(KeyCode.Unknown, KeyModifiers.Alt, layoutKey: '<', text: "", scanCode: 0x64);
        Assert.Equal(Escape("\x1b<"), Escape(t.Expect("<")));
    }

    [Fact]
    public void CopyAndFind_AreTheLayoutsLetters()
    {
        // Dvorak: the key labelled C is where a US keyboard has I, and US C types J.
        using var t = Term.Shown();
        t.Window.PressKey(KeyCode.I, KeyModifiers.Ctrl | KeyModifiers.Shift, layoutKey: 'C', text: "");
        Assert.Equal("", t.Settle()); // copy, the element's own
        t.Window.PressKey(KeyCode.C, KeyModifiers.Ctrl | KeyModifiers.Shift, layoutKey: 'J', text: "");
        Assert.Equal("\n", t.Expect("\n")); // Ctrl+J, the program's
    }

    // ──── AltGr ────

    [Fact]
    public void AltGr_SendsOnlyTheCharacterItTyped()
    {
        // As the SDL backend reports AltGr+2 on a Swedish keyboard: not Alt, AltGr, and the text.
        using var t = Term.Shown();
        t.Window.KeyDown(KeyCode.Digit2, layoutKey: '@', modifierKeys: ModifierKeys.AltGr, text: "@");
        t.Window.KeyUp(KeyCode.Digit2);
        Assert.Equal("@", t.Settle());
    }

    [Fact]
    public void CtrlAlt_KnownToHaveTypedACharacter_SendsOnlyIt()
    {
        using var t = Term.Shown();
        t.Window.KeyDown(KeyCode.Digit2, KeyModifiers.Ctrl | KeyModifiers.Alt, text: "@");
        Assert.Equal("@", t.Settle());
    }

    [Fact]
    public void CtrlAlt_KnownToTypeNothing_IsSentAtOnce()
    {
        // No hold for the text that might follow: the platform said none does.
        using var t = Term.Shown();
        t.Window.KeyDown(KeyCode.A, KeyModifiers.Ctrl | KeyModifiers.Alt, layoutKey: 'a', text: "");
        Assert.Equal(Escape("\x1b\x01"), Escape(SentAtOnce(t, "\x01")));
    }

    // ──── A key sent as itself never also types ────

    [Fact]
    public void CtrlSpace_SendsNul_AndNotTheSpaceItTyped()
    {
        // Windows types a space for Ctrl+Space.
        using var t = Term.Shown();
        t.Window.PressKey(KeyCode.Space, KeyModifiers.Ctrl, layoutKey: ' ', text: " ");
        Assert.Equal(Escape("\0"), Escape(t.Settle()));
    }

    [Fact]
    public void AShortcutTheAppTakes_TypesNothingEither()
    {
        using var t = Term.Shown(around: e => Flex.Column()
            .OnKeyDown(k => { if (k.Key == KeyCode.Space && k.Modifiers == KeyModifiers.Ctrl) k.Handled = true; })
            .Children(e));
        t.Window.PressKey(KeyCode.Space, KeyModifiers.Ctrl, layoutKey: ' ', text: " ");
        Assert.Equal("", t.Settle());
    }

    // ──── The keypad ────

    [Fact]
    public void TheKeypad_TypesItsDigitOnce()
    {
        using var t = Term.Shown();
        t.Window.PressKey(KeyCode.Numpad7, KeyModifiers.NumLock, text: "7");
        t.Window.PressKey(KeyCode.NumpadAdd, KeyModifiers.NumLock, text: "+");
        Assert.Equal("7+", t.Settle());
    }

    [Fact]
    public void ApplicationKeypadMode_SendsTheKeypadsSequences()
    {
        using var t = Term.Shown();
        t.Emit("\x1b="); // DECKPAM
        t.Window.PressKey(KeyCode.Numpad7, KeyModifiers.NumLock, text: "7");
        t.Window.PressKey(KeyCode.NumpadAdd, KeyModifiers.NumLock, text: "+");
        t.Window.PressKey(KeyCode.NumpadEnter, KeyModifiers.NumLock, text: "");
        t.Window.PressKey(KeyCode.NumpadEquals, KeyModifiers.NumLock, text: "=");
        Assert.Equal(Escape("\x1bOw\x1bOk\x1bOM\x1bOX"), Escape(t.Settle()));

        t.Emit("\x1b>"); // DECKPNM
        t.Window.PressKey(KeyCode.Numpad7, KeyModifiers.NumLock, text: "7");
        t.Window.PressKey(KeyCode.NumpadEnter, KeyModifiers.NumLock, text: "");
        Assert.Equal(Escape("7\r"), Escape(t.Settle()));
    }

    [Fact]
    public void WithNumLockOff_TheKeypadIsNavigation()
    {
        using var t = Term.Shown();
        foreach (var key in new[] { KeyCode.Numpad7, KeyCode.Numpad8, KeyCode.Numpad3, KeyCode.Numpad5, KeyCode.Numpad0, KeyCode.NumpadDecimal })
            t.Window.PressKey(key, text: "");
        Assert.Equal(Escape("\x1b[H\x1b[A\x1b[6~\x1b[E\x1b[2~\x1b[3~"), Escape(t.Settle()));

        t.Window.PressKey(KeyCode.Numpad4, KeyModifiers.Ctrl, text: "");
        Assert.Equal(Escape("\x1b[1;5D"), Escape(t.Settle()));
    }

    [Fact]
    public void WhenThePlatformCannotSay_TheKeypadLeavesItToTheText()
    {
        // No Text on the key (the WinForms host): what it typed follows as text, and is sent once.
        using var t = Term.Shown();
        t.Emit("\x1b=");
        t.Window.PressKey(KeyCode.Numpad7, KeyModifiers.NumLock);
        t.Window.TypeText("7");
        Assert.Equal("7", t.Settle());
    }

    [Fact]
    public void OnWindows_AltAndTheKeypadsDigits_TypeACharacterByItsCode()
    {
        // Alt, 6, 5 types A when Alt is let go; the digits themselves send nothing.
        if (!OperatingSystem.IsWindows()) return;
        using var t = Term.Shown();
        t.Window.PressKey(KeyCode.Numpad6, KeyModifiers.Alt | KeyModifiers.NumLock, modifierKeys: ModifierKeys.AltLeft, text: "");
        t.Window.PressKey(KeyCode.Numpad5, KeyModifiers.Alt | KeyModifiers.NumLock, modifierKeys: ModifierKeys.AltLeft, text: "");
        t.Window.TypeText("A");
        Assert.Equal("A", t.Settle());
    }

    [Fact]
    public void F13ToF20_HaveVt220Codes_F21AndUpNone()
    {
        using var t = Term.Shown();
        t.Window.PressKey(KeyCode.F13);
        t.Window.PressKey(KeyCode.F20, KeyModifiers.Shift);
        t.Window.PressKey(KeyCode.F21);
        Assert.Equal(Escape("\x1b[25~\x1b[34;2~"), Escape(t.Settle()));
    }

    // ──── win32-input-mode on ConPTY ────

    private static Term OnConPty(bool modeOn = true)
    {
        var t = Term.Shown(traits: TerminalConnectionTraits.WantsWin32Input);
        if (modeOn) t.Emit("\x1b[?9001h");
        return t;
    }

    [Fact]
    public void OnConPty_EscapeIsAWin32KeyPress()
    {
        using var t = OnConPty();
        t.Window.PressKey(KeyCode.Escape, text: "");
        Assert.Equal(Escape(Win32Press(0x1B, 0x01, 0x1B, 0)), Escape(t.Settle()));
    }

    [Fact]
    public void WithoutTheModeOrTheTrait_EscapeIsABareEsc()
    {
        using (var t = OnConPty(modeOn: false))
        {
            t.Window.PressKey(KeyCode.Escape, text: "");
            Assert.Equal(Escape("\x1b"), Escape(t.Settle()));
        }
        using (var t = Term.Shown())
        {
            t.Emit("\x1b[?9001h"); // asked for, by a program not behind ConPTY
            t.Window.PressKey(KeyCode.Escape, text: "");
            Assert.Equal(Escape("\x1b"), Escape(t.Settle()));
        }
    }

    [Fact]
    public void OnConPty_TheOtherAmbiguousKeys_AreWin32KeyPresses()
    {
        using var t = OnConPty();
        t.Window.PressKey(KeyCode.A, KeyModifiers.Alt, layoutKey: 'a', modifierKeys: ModifierKeys.AltLeft, text: "");
        Assert.Equal(Escape(Win32Press('A', 0x1E, 'a', 0x02)), Escape(t.Settle()));
        t.Window.PressKey(KeyCode.A, KeyModifiers.Alt | KeyModifiers.Shift, layoutKey: 'A', modifierKeys: ModifierKeys.AltRight | ModifierKeys.ShiftLeft, text: "");
        Assert.Equal(Escape(Win32Press('A', 0x1E, 'A', 0x01 | 0x10)), Escape(t.Settle()));
        t.Window.PressKey(KeyCode.Space, KeyModifiers.Ctrl, layoutKey: ' ', modifierKeys: ModifierKeys.CtrlLeft, text: " ");
        Assert.Equal(Escape(Win32Press(0x20, 0x39, ' ', 0x08)), Escape(t.Settle()));
        t.Window.PressKey(KeyCode.Backspace, KeyModifiers.Ctrl, modifierKeys: ModifierKeys.CtrlRight, text: "");
        Assert.Equal(Escape(Win32Press(0x08, 0x0E, 0x7F, 0x04)), Escape(t.Settle()));
        t.Window.PressKey(KeyCode.Escape, KeyModifiers.Shift | KeyModifiers.NumLock, text: "");
        Assert.Equal(Escape(Win32Press(0x1B, 0x01, 0x1B, 0x10 | 0x20)), Escape(t.Settle()));
    }

    [Fact]
    public void OnConPty_TheRestStaysVt_AndTextUtf8()
    {
        using var t = OnConPty();
        t.Window.PressKey(KeyCode.Up, text: "");
        t.Window.PressKey(KeyCode.C, KeyModifiers.Ctrl, layoutKey: 'c', text: "");
        t.Window.TypeText("é");
        Assert.Equal(Escape("\x1b[A\x03é"), Escape(t.Settle()));
    }

    [Fact]
    public void OnConPty_AltGrsCharacter_IsText()
    {
        using var t = OnConPty();
        t.Window.KeyDown(KeyCode.Digit2, layoutKey: '@', modifierKeys: ModifierKeys.AltGr, text: "@");
        Assert.Equal("@", t.Settle());
    }

    // ──── The win32 tables ────

    [Fact]
    public void TheScanCodeTable_IsWindowsOwn()
    {
        // The table is HID usage to set 1, as Windows' keyboard driver translates. Windows' own
        // scan-code-to-virtual-key mapping checks it for the keys no layout moves.
        if (!OperatingSystem.IsWindows()) return;
        (KeyCode Key, int Vk)[] fixedKeys =
        [
            (KeyCode.Escape, 0x1B), (KeyCode.Enter, 0x0D), (KeyCode.Backspace, 0x08), (KeyCode.Tab, 0x09), (KeyCode.Space, 0x20),
            (KeyCode.CapsLock, 0x14), (KeyCode.F1, 0x70), (KeyCode.F10, 0x79), (KeyCode.F11, 0x7A), (KeyCode.F12, 0x7B),
            (KeyCode.F13, 0x7C), (KeyCode.F23, 0x86), (KeyCode.F24, 0x87),
            (KeyCode.Insert, 0x2D), (KeyCode.Home, 0x24), (KeyCode.PageUp, 0x21), (KeyCode.Delete, 0x2E), (KeyCode.End, 0x23),
            (KeyCode.PageDown, 0x22), (KeyCode.Right, 0x27), (KeyCode.Left, 0x25), (KeyCode.Down, 0x28), (KeyCode.Up, 0x26),
            (KeyCode.NumLock, 0x90), (KeyCode.ScrollLock, 0x91), (KeyCode.NumpadDivide, 0x6F), (KeyCode.NumpadMultiply, 0x6A),
            (KeyCode.NumpadSubtract, 0x6D), (KeyCode.NumpadAdd, 0x6B), (KeyCode.NumpadEnter, 0x0D), (KeyCode.Menu, 0x5D),
            (KeyCode.CtrlLeft, 0xA2), (KeyCode.ShiftLeft, 0xA0), (KeyCode.AltLeft, 0xA4), (KeyCode.SuperLeft, 0x5B),
            (KeyCode.CtrlRight, 0xA3), (KeyCode.ShiftRight, 0xA1), (KeyCode.AltRight, 0xA5), (KeyCode.SuperRight, 0x5C),
        ];
        foreach (var (key, vk) in fixedKeys)
        {
            var set1 = Win32Keys.Set1(Win32Keys.Usage(key));
            Assert.True(set1 != 0, $"{key} has no scan code");
            Assert.True(vk == (int)MapVirtualKeyW(set1, 3), $"{key}: scan code 0x{set1:X4} is VK 0x{MapVirtualKeyW(set1, 3):X2} to Windows, not 0x{vk:X2}");
        }

        // And every scan code the table gives is a key to Windows, but the international ones
        // (Katakana, Yen, Henkan), which are keys only to a Japanese layout.
        for (int usage = 0x04; usage <= 0xE7; usage++)
            if (usage is < 0x85 or > 0x8B && Win32Keys.Set1(usage) is var set1 and not 0)
                Assert.True(MapVirtualKeyW(set1, 3) != 0, $"usage 0x{usage:X2}: scan code 0x{set1:X4} is no key to Windows");
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);
}

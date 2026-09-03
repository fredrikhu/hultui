using System.Windows.Forms;
using HultUI.Core.Events;
using HultUI.WinForms.Input;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// The WinForms backend names keys by physical position, as the SDL one does, by
/// translating layout-dependent virtual keys back through their scan code. This guards
/// the table that translation reads.
/// </summary>
/// <remarks>
/// <para>These are ordinary facts, not <see cref="UiFactAttribute"/>s: they open no
/// window and take no foreground, so unlike the rest of this assembly they run in CI.
/// A typo in the scan-code table would otherwise be invisible — typed text arrives
/// through <c>WM_CHAR</c> and never touches this, so every end-to-end typing test would
/// still pass while a key binding silently reported the wrong key.</para>
///
/// <para>What cannot be asserted here is the layout independence itself. The mapping
/// depends on the machine's active keyboard layout, and a test that switched it would be
/// changing the developer's machine — so the assertions below hold on any QWERTY-family
/// layout, and the German QWERTZ swap of Y and Z is exactly the case they are careful
/// not to assume away.</para>
/// </remarks>
public class KeyMapTests
{
    [Fact]
    public void EveryScanCodeNamesADifferentKey()
    {
        // A copy-paste slip in the table — two rows landing on the same KeyCode — would
        // make one physical key unreachable and another report its neighbour.
        var seen = new Dictionary<KeyCode, uint>();
        for (uint scanCode = 0; scanCode <= 0x58; scanCode++)
        {
            var code = KeyMap.FromScanCode(scanCode);
            if (code == KeyCode.Unknown) continue;

            Assert.False(seen.TryGetValue(code, out uint first),
                $"scan codes 0x{first:X2} and 0x{scanCode:X2} both map to {code}");
            seen[code] = scanCode;
        }
    }

    [Fact]
    public void TheTableCoversEveryLetterAndDigit()
    {
        var covered = new HashSet<KeyCode>();
        for (uint scanCode = 0; scanCode <= 0x58; scanCode++)
            covered.Add(KeyMap.FromScanCode(scanCode));

        for (var letter = KeyCode.A; letter <= KeyCode.Z; letter++)
            Assert.Contains(letter, covered);

        for (var digit = KeyCode.Digit0; digit <= KeyCode.Digit9; digit++)
            Assert.Contains(digit, covered);
    }

    [Fact]
    public void EveryLetterKeyResolvesToSomePosition()
    {
        // Whatever the active layout, a letter key must name a position rather than
        // falling through to Unknown — which is what an application binding a shortcut
        // to it would otherwise see.
        for (var key = Keys.A; key <= Keys.Z; key++)
            Assert.NotEqual(KeyCode.Unknown, KeyMap.ToKeyCode(key));

        for (var key = Keys.D0; key <= Keys.D9; key++)
            Assert.NotEqual(KeyCode.Unknown, KeyMap.ToKeyCode(key));
    }

    [Theory]
    // Keys with no layout interpretation to undo: these must map exactly, everywhere.
    [InlineData(Keys.Left, KeyCode.Left)]
    [InlineData(Keys.Right, KeyCode.Right)]
    [InlineData(Keys.Up, KeyCode.Up)]
    [InlineData(Keys.Down, KeyCode.Down)]
    [InlineData(Keys.Back, KeyCode.Backspace)]
    [InlineData(Keys.Tab, KeyCode.Tab)]
    [InlineData(Keys.Enter, KeyCode.Enter)]
    [InlineData(Keys.Escape, KeyCode.Escape)]
    [InlineData(Keys.Space, KeyCode.Space)]
    [InlineData(Keys.Home, KeyCode.Home)]
    [InlineData(Keys.End, KeyCode.End)]
    [InlineData(Keys.Delete, KeyCode.Delete)]
    [InlineData(Keys.F1, KeyCode.F1)]
    [InlineData(Keys.F12, KeyCode.F12)]
    [InlineData(Keys.NumPad0, KeyCode.Digit0)]
    [InlineData(Keys.NumPad9, KeyCode.Digit9)]
    public void LayoutIndependentKeys_MapExactly(Keys key, KeyCode expected)
        => Assert.Equal(expected, KeyMap.ToKeyCode(key));

    [Fact]
    public void ModifiersComeFromTheHighBits()
    {
        Assert.Equal(KeyModifiers.None, KeyMap.ToModifiers(Keys.A) & ~AmbientLocks);

        Assert.True(KeyMap.ToModifiers(Keys.A | Keys.Control).HasFlag(KeyModifiers.Ctrl));
        Assert.True(KeyMap.ToModifiers(Keys.A | Keys.Shift).HasFlag(KeyModifiers.Shift));
        Assert.True(KeyMap.ToModifiers(Keys.A | Keys.Alt).HasFlag(KeyModifiers.Alt));

        var all = KeyMap.ToModifiers(Keys.A | Keys.Control | Keys.Shift | Keys.Alt);
        Assert.True(all.HasFlag(KeyModifiers.Ctrl) && all.HasFlag(KeyModifiers.Shift) && all.HasFlag(KeyModifiers.Alt));
    }

    /// <summary>Caps and Num lock are read from live keyboard state, so a test cannot
    /// know what they will be — they are masked out rather than asserted.</summary>
    private const KeyModifiers AmbientLocks = KeyModifiers.CapsLock | KeyModifiers.NumLock;
}

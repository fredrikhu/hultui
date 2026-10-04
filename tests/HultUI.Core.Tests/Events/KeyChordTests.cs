using HultUI.Core.Events;
using Xunit;

namespace HultUI.Core.Tests.Events;

/// <summary>
/// <see cref="KeyChord"/>: what a press is, by the rules the apps each had a partial copy of.
/// The layout cases are a Swedish keyboard's, where + and - have keys of their own, and a German
/// one's, where Y and Z trade places.
/// </summary>
public sealed class KeyChordTests
{
    private static KeyDownEvent Press(KeyCode key, KeyModifiers modifiers = KeyModifiers.None, int layoutKey = 0) =>
        new() { Key = key, Modifiers = modifiers, LayoutKey = layoutKey };

    [Fact]
    public void TheModifiersMustBeExactlyTheChords()
    {
        var copy = new KeyChord(KeyCode.C, KeyModifiers.Ctrl);
        var bare = new KeyChord(KeyCode.C);

        Assert.True(copy.Matches(Press(KeyCode.C, KeyModifiers.Ctrl)));
        Assert.False(bare.Matches(Press(KeyCode.C, KeyModifiers.Ctrl)));   // Ctrl+C is not C
        Assert.False(copy.Matches(Press(KeyCode.C)));
        Assert.False(copy.Matches(Press(KeyCode.C, KeyModifiers.Ctrl | KeyModifiers.Shift)));
        Assert.False(copy.Matches(Press(KeyCode.C, KeyModifiers.Ctrl | KeyModifiers.Alt)));
    }

    [Fact]
    public void SuperIsCtrl_AndTheLocksNeverCount()
    {
        var palette = new KeyChord(KeyCode.P, KeyModifiers.Ctrl);
        Assert.True(palette.Matches(Press(KeyCode.P, KeyModifiers.Super)));
        Assert.True(palette.Matches(Press(KeyCode.P, KeyModifiers.Ctrl | KeyModifiers.CapsLock | KeyModifiers.NumLock)));
        Assert.Equal(palette, new KeyChord(KeyCode.P, KeyModifiers.Super));
    }

    [Fact]
    public void ALetter_IsTheLetterPrintedOnTheKey()
    {
        var undo = new KeyChord(KeyCode.Z, KeyModifiers.Ctrl);
        // German: the key a US keyboard has Y on types z.
        Assert.True(undo.Matches(Press(KeyCode.Y, KeyModifiers.Ctrl, layoutKey: 'z')));
        Assert.False(undo.Matches(Press(KeyCode.Z, KeyModifiers.Ctrl, layoutKey: 'y')));
        // CapsLock gives a capital, which is the same letter.
        Assert.True(undo.Matches(Press(KeyCode.Y, KeyModifiers.Ctrl | KeyModifiers.CapsLock, layoutKey: 'Z')));
        // A platform that did not say: the US position.
        Assert.True(undo.Matches(Press(KeyCode.Z, KeyModifiers.Ctrl)));
    }

    [Fact]
    public void AnyOtherKey_IsAPosition()
    {
        var search = new KeyChord(KeyCode.Slash);
        Assert.True(search.Matches(Press(KeyCode.Slash, layoutKey: '-')));   // Swedish: the - key
        Assert.False(search.Matches(Press(KeyCode.Digit7, KeyModifiers.Shift, layoutKey: '/')));
    }

    [Fact]
    public void ACharacterChord_IsTheCharacterTheLayoutTypes_WhateverShiftItTakes()
    {
        var zoomIn = KeyChord.Of('+', KeyModifiers.Ctrl);
        // Swedish: + is a key of its own, at the US minus position, unshifted.
        Assert.True(zoomIn.Matches(Press(KeyCode.Minus, KeyModifiers.Ctrl, layoutKey: '+')));
        // US: Shift and =.
        Assert.True(zoomIn.Matches(Press(KeyCode.Equals, KeyModifiers.Ctrl | KeyModifiers.Shift, layoutKey: '+')));
        // The keypad's +, which types nothing the platform reports.
        Assert.True(zoomIn.Matches(Press(KeyCode.NumpadAdd, KeyModifiers.Ctrl)));
        // Headless US Shift+= with no layout said.
        Assert.True(zoomIn.Matches(Press(KeyCode.Equals, KeyModifiers.Ctrl | KeyModifiers.Shift)));

        // The Swedish + key used to be KeyCode.Minus — zoom out.
        Assert.False(KeyChord.Of('-', KeyModifiers.Ctrl).Matches(Press(KeyCode.Minus, KeyModifiers.Ctrl, layoutKey: '+')));
        Assert.False(zoomIn.Matches(Press(KeyCode.Minus, KeyModifiers.None, layoutKey: '+')));
    }

    [Fact]
    public void ALetterOrDigitCharacter_IsTheKeyChord()
    {
        Assert.Equal(new KeyChord(KeyCode.P, KeyModifiers.Ctrl), KeyChord.Of('p', KeyModifiers.Ctrl));
        Assert.Equal(new KeyChord(KeyCode.Digit3), KeyChord.Of('3'));
        Assert.False(KeyChord.Of('+').Equals(new KeyChord(KeyCode.Equals)));
    }

    [Fact]
    public void ADigit_IsTheNumberRowOrTheKeypadWithNumLock_AndEnterEitherEnter()
    {
        var first = new KeyChord(KeyCode.Digit1, KeyModifiers.Alt);
        Assert.True(first.Matches(Press(KeyCode.Digit1, KeyModifiers.Alt)));
        Assert.True(first.Matches(Press(KeyCode.Numpad1, KeyModifiers.Alt | KeyModifiers.NumLock)));
        Assert.False(first.Matches(Press(KeyCode.Numpad1, KeyModifiers.Alt)));   // NumLock off: End

        var post = new KeyChord(KeyCode.Enter, KeyModifiers.Ctrl);
        Assert.True(post.Matches(Press(KeyCode.NumpadEnter, KeyModifiers.Ctrl)));
    }

    [Theory]
    [InlineData("Ctrl+Shift+P")]
    [InlineData("Alt+↑")]
    [InlineData("Ctrl+,")]
    [InlineData("F5")]
    [InlineData("Shift+F6")]
    [InlineData("Ctrl+PgUp")]
    [InlineData("Alt+Shift+Enter")]
    [InlineData("Esc")]
    [InlineData("Menu")]
    [InlineData("Num +")]
    [InlineData("Ctrl++")]
    [InlineData("Ctrl+1")]
    [InlineData("`")]
    [InlineData("Ctrl+\\")]
    [InlineData("Space")]
    [InlineData("m")]
    [InlineData("Shift+M")]
    public void ItsTextReadsBack(string text)
    {
        var chord = KeyChord.Parse(text);
        Assert.NotNull(chord);
        Assert.Equal(text, chord.Value.ToString());
        Assert.Equal(chord, KeyChord.Parse(chord.Value.ToString()));
    }

    [Theory]
    [InlineData("Ctrl+Shift+F6", "Ctrl+Shift+F6")]
    [InlineData("Alt+F7", "Alt+F7")]
    [InlineData("=", "=")]
    [InlineData(",", ",")]
    [InlineData("Ctrl+PgDn", "Ctrl+PgDn")]
    [InlineData("Ctrl+Digit1", "Ctrl+1")]
    [InlineData("Alt+Enter", "Alt+Enter")]
    [InlineData("Alt+Up", "Alt+↑")]
    [InlineData("cmd+p", "Ctrl+P")]
    [InlineData(" Shift + Alt + Escape ", "Alt+Shift+Esc")]
    public void TheSpellingsSettingsAlreadyHold_StillRead(string stored, string now)
    {
        // Vesper wrote enum names and PgUp/PgDn; a person's rebinds must survive the move.
        Assert.Equal(now, KeyChord.Parse(stored)?.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+X")]
    [InlineData("Ctrl+Nope")]
    [InlineData("12")]
    public void NonsenseIsNull(string text) => Assert.Null(KeyChord.Parse(text));

    [Fact]
    public void ACapturedPress_IsAKeyChord_AndAModifierAloneIsNothing()
    {
        Assert.Equal(new KeyChord(KeyCode.F6, KeyModifiers.Shift), KeyChord.From(Press(KeyCode.F6, KeyModifiers.Shift)));
        Assert.Null(KeyChord.From(Press(KeyCode.CtrlLeft, KeyModifiers.Ctrl)));
    }
}

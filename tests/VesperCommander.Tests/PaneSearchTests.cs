using HultUI.Core.Events;
using VesperCommander;
using Xunit;

namespace VesperCommander.Tests;

public class PaneSearchTests
{
    [Theory]
    [InlineData(KeyCode.Digit8, true, '*')]   // the reported case: Shift+8 → wildcard
    [InlineData(KeyCode.Digit8, false, '8')]
    [InlineData(KeyCode.Digit2, true, '@')]
    [InlineData(KeyCode.Slash, true, '?')]    // Shift+/ → the single-char wildcard
    [InlineData(KeyCode.Slash, false, '/')]
    [InlineData(KeyCode.Minus, true, '_')]
    [InlineData(KeyCode.Period, false, '.')]
    [InlineData(KeyCode.BracketLeft, true, '{')]
    [InlineData(KeyCode.A, false, 'a')]
    [InlineData(KeyCode.A, true, 'a')]        // letters ignore shift (search is case-insensitive)
    public void SearchChar_MapsShiftedSymbols(KeyCode key, bool shift, char expected) =>
        Assert.Equal(expected, CommanderApp.SearchChar(key, shift));

    [Fact]
    public void SearchChar_IgnoresNonPrintableKeys() =>
        Assert.Equal('\0', CommanderApp.SearchChar(KeyCode.F5, false));
}

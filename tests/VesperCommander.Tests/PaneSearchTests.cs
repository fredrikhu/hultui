using HultUI.Core.Events;
using VesperCommander;
using Xunit;

namespace VesperCommander.Tests;

public class PaneSearchTests
{
    [Theory]
    [InlineData("archive.zip", ".zip")]
    [InlineData("backup.tar.gz", ".gz")]   // last segment only
    [InlineData(".gitignore", ".gitignore")] // a dotfile is its own extension
    [InlineData("Makefile", "")]            // no dot → none
    [InlineData("noext.", ".")]             // trailing dot
    public void ExtensionOf_TakesLastSegment(string name, string expected) =>
        Assert.Equal(expected, CommanderApp.ExtensionOf(name));

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

    [Theory]
    [InlineData("", "CommanderApp.cs", true)]        // empty filter matches all
    [InlineData("com", "CommanderApp.cs", true)]     // substring, case-insensitive
    [InlineData("COM", "CommanderApp.cs", true)]
    [InlineData("xyz", "CommanderApp.cs", false)]
    [InlineData(".cs", "CommanderApp.cs", true)]     // leading dot → extension (prefix)
    [InlineData(".cs", "notes.csv", true)]           // ".csv" starts with ".cs" — incremental
    [InlineData(".csv", "notes.csv", true)]
    [InlineData(".cs", "readme.md", false)]
    public void FilterMatch_SubstringOrExtension(string search, string name, bool expected) =>
        Assert.Equal(expected, CommanderApp.FilterMatch(name, search));

    // Marquee ≥50%-coverage: entry box is (10,10) 100×20.
    [Theory]
    [InlineData(0, 0, 200, 200, true)]     // rect fully contains the entry
    [InlineData(10, 10, 60, 30, true)]     // covers the left half exactly (50%)
    [InlineData(10, 10, 40, 30, false)]    // covers 30% of width → <50%
    [InlineData(300, 300, 400, 400, false)] // disjoint
    [InlineData(10, 20, 110, 30, true)]    // full width × bottom half height = 50% area
    [InlineData(10, 25, 110, 35, false)]   // full width × 25% height = 25% area
    public void CoversHalf_Area(float rx0, float ry0, float rx1, float ry1, bool expected) =>
        Assert.Equal(expected, CommanderApp.CoversHalf(rx0, ry0, rx1, ry1, ex: 10, ey: 10, ew: 100, eh: 20));
}

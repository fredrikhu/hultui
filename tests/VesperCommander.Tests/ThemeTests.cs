using HultUI.Core.Primitives;
using HultUI.Themes;
using VesperCommander.Theme;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The colour register, now that it is a window onto a selectable scheme rather than a set of
/// constants.
///
/// <para>The first test is the one that matters most: Nocturne was drawn by hand, the app is
/// full of layouts tuned against it, and turning the register into a facade must not have
/// moved it by a single channel. The rest pin the switching machinery — that a scheme
/// actually takes effect, that a derived colour follows it rather than freezing at type-init
/// (the failure mode <c>static readonly</c> would have reintroduced), and that an unknown id
/// lands on the default instead of throwing at startup.</para>
/// </summary>
public class ThemeTests : IDisposable
{
    // Every test in this class moves process-wide state, so each one puts it back.
    public void Dispose() => Nocturne.Use("nocturne", 0);

    [Fact]
    public void TheDefaultSchemeIsTheSameNocturneAsBefore()
    {
        Nocturne.Use("nocturne", 0);

        Assert.Equal(Color.FromHex("#161826"), Nocturne.Bg);
        Assert.Equal(Color.FromHex("#232532"), Nocturne.Surface);
        Assert.Equal(Color.FromHex("#e9e9ed"), Nocturne.Text);
        Assert.Equal(Color.FromHex("#9184d9"), Nocturne.Accent);

        // The hand-tuned ramps, both ends and the middle.
        Assert.Equal(Color.FromHex("#f3f5fe"), Nocturne.Neutral100);
        Assert.Equal(Color.FromHex("#9397ab"), Nocturne.Neutral500);
        Assert.Equal(Color.FromHex("#292b31"), Nocturne.Neutral900);
        Assert.Equal(Color.FromHex("#f5f4ff"), Nocturne.Accent100);
        Assert.Equal(Color.FromHex("#968ae0"), Nocturne.Accent500);
        Assert.Equal(Color.FromHex("#2b2741"), Nocturne.Accent900);

        // The file-manager literals and the derived steps built on them.
        Assert.Equal(Color.FromHex("#e0685f"), Nocturne.MarkRed);
        Assert.Equal(Color.FromHex("#a95349"), Nocturne.MarkRedDim);
        Assert.Equal(Color.FromHex("#3eb37e"), Nocturne.DiffGreen);
        Assert.Equal(Color.FromHex("#7fd0a5"), Nocturne.DiffGreenText);
        Assert.Equal(Color.FromHex("#e3b341"), Nocturne.MarkAmber);
        Assert.Equal(new Color(0xe9, 0xe9, 0xed, 40), Nocturne.Divider);   // text at 16%
    }

    [Fact]
    public void PickingASchemeRepaintsTheRegister()
    {
        Nocturne.Use("gruvbox-dark", 0);

        Assert.Equal("gruvbox-dark", Nocturne.Theme.Id);
        Assert.Equal(Color.FromHex("#1d2021"), Nocturne.Bg);
        Assert.Equal(Color.FromHex("#ebdbb2"), Nocturne.Text);
    }

    [Fact]
    public void DerivedColoursFollowTheSchemeRatherThanFreezing()
    {
        // Divider, DirText and the diff tints are computed from the register. Read one of
        // each before the switch: a static readonly field would answer with the old scheme's
        // value afterwards, which is exactly what this file replaced.
        Nocturne.Use("nocturne", 0);
        var (divider, dir, addBg) = (Nocturne.Divider, Nocturne.DirText, Nocturne.DiffAddBg);

        Nocturne.Use("nord", 0);

        Assert.NotEqual(divider, Nocturne.Divider);
        Assert.NotEqual(dir, Nocturne.DirText);
        Assert.NotEqual(addBg, Nocturne.DiffAddBg);
        Assert.Equal(Nocturne.Accent200, Nocturne.DirText);   // still the role it always was
    }

    [Fact]
    public void TheFrameworkThemeIsRebuiltFromWhicheverSchemeIsActive()
    {
        // Built-in widgets read Application.Theme, not this register, so the bridge has to
        // pick up the switch too — otherwise the text fields stay the old scheme's colour.
        Nocturne.Use("dracula", 0);
        var hult = Nocturne.ToHultTheme();

        Assert.Equal(Nocturne.Bg, hult.Background);
        Assert.Equal(Nocturne.Text, hult.TextColor);
        Assert.Equal(Nocturne.Accent, hult.Primary);
    }

    [Theory]
    [InlineData("gruvbox-chartreuse")]   // never existed
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownSchemeIdOpensOnTheDefault(string? id)
    {
        // This is a settings file from a different build. It must not throw during startup.
        Nocturne.Use(id, 0);

        Assert.Equal("nocturne", Nocturne.Theme.Id);
        Assert.Equal(Color.FromHex("#161826"), Nocturne.Bg);
    }

    [Fact]
    public void AccentIndexZeroLeavesTheSchemesOwnAccentAlone()
    {
        Nocturne.Use("gruvbox-dark", 0);
        Assert.Equal(ThemeCatalog.GruvboxDark.Accent, Nocturne.Accent);
    }

    [Fact]
    public void AnAccentOverrideReplacesTheSchemesAccentAndItsWholeRamp()
    {
        Nocturne.Use("gruvbox-dark", 0);
        var (accent, ramp500) = (Nocturne.Accent, Nocturne.Accent500);

        Nocturne.Use("gruvbox-dark", 2);

        Assert.NotEqual(accent, Nocturne.Accent);
        Assert.NotEqual(ramp500, Nocturne.Accent500);
        // The ground is the scheme's, not the accent's — an override is an accent, not a theme.
        Assert.Equal(ThemeCatalog.GruvboxDark.Bg, Nocturne.Bg);
    }

    [Fact]
    public void AnOutOfRangeAccentIndexIsIgnoredRatherThanThrowing()
    {
        Nocturne.Use("nocturne", 99);
        Assert.Equal(Color.FromHex("#9184d9"), Nocturne.Accent);
    }

    [Fact]
    public void EverySchemeResolvesToACompletePalette()
    {
        // Palette.For has two paths — the literal for Nocturne and the generator for the
        // rest. Walk them all so a scheme added later cannot half-resolve.
        foreach (var t in ThemeCatalog.All)
        {
            var p = Palette.For(t, 0);
            Assert.Equal(Ramp.Steps, p.Neutrals.Length);
            Assert.Equal(Ramp.Steps, p.Accents.Length);
            Assert.NotEqual(default, p.MarkRed);
            Assert.NotEqual(default, p.DiffGreen);
            Assert.NotEqual(default, p.MarkAmber);
        }
    }
}

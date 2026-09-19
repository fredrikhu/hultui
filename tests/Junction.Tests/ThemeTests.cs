using HultUI.Core.Primitives;
using HultUI.Themes;
using Junction.Theme;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The colour register, now that it is a window onto a selectable scheme rather than a set of
/// constants.
///
/// <para>The first test is the one that matters most: Nocturne was drawn by hand, the app is
/// full of views tuned against it, and turning the register into a facade must not have moved
/// it by a single channel. The rest pin the switching machinery — that a scheme takes effect,
/// that derived colours follow it rather than freezing at type-init (the failure mode
/// <c>static readonly</c> would have reintroduced), and that an unknown id lands on the
/// default instead of throwing at startup.</para>
/// </summary>
public class ThemeTests : IDisposable
{
    // Every test in this class moves process-wide state, so each one puts it back.
    public void Dispose() => Nocturne.Use("nocturne");

    [Fact]
    public void TheDefaultSchemeIsTheSameNocturneAsBefore()
    {
        Nocturne.Use("nocturne");

        Assert.Equal(Color.FromHex("#161826"), Nocturne.Bg);
        Assert.Equal(Color.FromHex("#232532"), Nocturne.Surface);
        Assert.Equal(Color.FromHex("#e9e9ed"), Nocturne.Text);
        Assert.Equal(Color.FromHex("#9184d9"), Nocturne.Accent);
        Assert.Equal(Color.FromHex("#a7a1db"), Nocturne.Accent2);

        // The three hand-tuned ramps, both ends and the middle of each.
        Assert.Equal(Color.FromHex("#f3f5fe"), Nocturne.Neutral100);
        Assert.Equal(Color.FromHex("#9397ab"), Nocturne.Neutral500);
        Assert.Equal(Color.FromHex("#292b31"), Nocturne.Neutral900);
        Assert.Equal(Color.FromHex("#f5f4ff"), Nocturne.Accent100);
        Assert.Equal(Color.FromHex("#968ae0"), Nocturne.Accent500);
        Assert.Equal(Color.FromHex("#2b2741"), Nocturne.Accent900);
        Assert.Equal(Color.FromHex("#f5f4ff"), Nocturne.Accent2_100);
        Assert.Equal(Color.FromHex("#9690c9"), Nocturne.Accent2_500);
        Assert.Equal(Color.FromHex("#2b293a"), Nocturne.Accent2_900);

        // Diff colours, including the two the design gave as rgba rather than hex.
        Assert.Equal(new Color(120, 178, 116, 33), Nocturne.DiffAddBg);
        Assert.Equal(new Color(120, 178, 116, 72), Nocturne.DiffAddHl);
        Assert.Equal(Color.FromHex("#bfe0ba"), Nocturne.DiffAddText);
        Assert.Equal(Color.FromHex("#7fbf79"), Nocturne.DiffAddSign);
        Assert.Equal(new Color(200, 120, 120, 33), Nocturne.DiffDelBg);
        Assert.Equal(Color.FromHex("#e3b9b9"), Nocturne.DiffDelText);
        Assert.Equal(Color.FromHex("#cf8f8f"), Nocturne.DiffDelSign);
        Assert.Equal(new Color(200, 120, 120, 72), Nocturne.DiffDelHl);

        // Status and find.
        Assert.Equal(Color.FromHex("#93c98d"), Nocturne.AddCount);
        Assert.Equal(Color.FromHex("#cf9494"), Nocturne.DelCount);
        Assert.Equal(Color.FromHex("#e39a9a"), Nocturne.DangerText);
        Assert.Equal(Color.FromHex("#cf8f8f"), Nocturne.DangerIcon);
        Assert.Equal(Color.FromHex("#e3b341"), Nocturne.MarkAmber);
        Assert.Equal(new Color(0xe9, 0xe9, 0xed, 40), Nocturne.Divider);   // text at 16%
    }

    [Fact]
    public void PickingASchemeRepaintsTheRegister()
    {
        Nocturne.Use("gruvbox-dark");

        Assert.Equal("gruvbox-dark", Nocturne.Theme.Id);
        Assert.Equal(Color.FromHex("#1d2021"), Nocturne.Bg);
        Assert.Equal(Color.FromHex("#ebdbb2"), Nocturne.Text);
    }

    [Fact]
    public void DerivedColoursFollowTheSchemeRatherThanFreezing()
    {
        Nocturne.Use("nocturne");
        var (divider, addBg, findHit) = (Nocturne.Divider, Nocturne.DiffAddBg, Nocturne.FindHitBg);

        Nocturne.Use("nord");

        Assert.NotEqual(divider, Nocturne.Divider);
        Assert.NotEqual(addBg, Nocturne.DiffAddBg);
        Assert.NotEqual(findHit, Nocturne.FindHitBg);
    }

    [Fact]
    public void TheRowTintAndTheWordHighlightStayOneHue()
    {
        // They are the same colour at two opacities by construction, so a scheme cannot end
        // up with a wash and an intra-line highlight that disagree about what "added" is.
        foreach (var t in ThemeCatalog.All)
        {
            Nocturne.Use(t.Id);
            Assert.Equal(
                (Nocturne.DiffAddBg.R, Nocturne.DiffAddBg.G, Nocturne.DiffAddBg.B),
                (Nocturne.DiffAddHl.R, Nocturne.DiffAddHl.G, Nocturne.DiffAddHl.B));
            Assert.True(Nocturne.DiffAddHl.A > Nocturne.DiffAddBg.A,
                $"{t.Name}: the word-level highlight is no stronger than the row tint");
        }
    }

    [Fact]
    public void StatusLettersStayTellableApartInEveryScheme()
    {
        // A/M/D are added, modified and deleted in the file tree. Two of them resolving to
        // one colour makes the column decorative rather than informative.
        foreach (var t in ThemeCatalog.All)
        {
            Nocturne.Use(t.Id);
            Assert.NotEqual(Nocturne.StatusLetter('A'), Nocturne.StatusLetter('D'));
            Assert.NotEqual(Nocturne.StatusLetter('A'), Nocturne.StatusLetter('M'));
            Assert.NotEqual(Nocturne.StatusLetter('D'), Nocturne.StatusLetter('M'));
        }
    }

    [Theory]
    [InlineData("gruvbox-chartreuse")]   // never existed
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownSchemeIdOpensOnTheDefault(string? id)
    {
        // This is a settings file from a different build. It must not throw during startup.
        Nocturne.Use(id);

        Assert.Equal("nocturne", Nocturne.Theme.Id);
        Assert.Equal(Color.FromHex("#161826"), Nocturne.Bg);
    }

    [Fact]
    public void EverySchemeResolvesToACompletePalette()
    {
        // Palette.For has two paths — the literal for Nocturne and the generator for the
        // rest. Walk them all so a scheme added later cannot half-resolve.
        foreach (var t in ThemeCatalog.All)
        {
            var p = Palette.For(t);
            Assert.Equal(Ramp.Steps, p.Neutrals.Length);
            Assert.Equal(Ramp.Steps, p.Accents.Length);
            Assert.Equal(Ramp.Steps, p.Accent2s.Length);
            Assert.NotEqual(default, p.AddSign);
            Assert.NotEqual(default, p.DelSign);
            Assert.NotEqual(default, p.MarkAmber);
        }
    }

    /// <summary>
    /// What a scheme's diff and search marks compose to on screen, and whether the text on
    /// them survives. Nocturne's washes were picked by hand for exactly this; the generated
    /// schemes take their published green, red and amber, and Dracula's — terminal colours,
    /// turned all the way up — composed to a word highlight its own comments read at 1.4:1 on,
    /// and a search mark its strings vanished into.
    ///
    /// <para>The floors are WCAG's where the design can meet them: 4.5:1 for the added or
    /// deleted text on its own word highlight on a dark ground, 3:1 for the body text on it. A
    /// light theme's word highlight is a saturated green at the same opacities over paper,
    /// which leaves its added text at 3.4:1, so the light floor is 3:1. The current search hit
    /// is held to 2.5:1: Solarized Dark's body text starts at 7:1 on its own ground, and no
    /// mark bright enough to be found stays above 3:1 under it.</para>
    /// </summary>
    [Fact]
    public void TextStaysLegibleOnEverySchemesHighlights()
    {
        foreach (var t in ThemeCatalog.All)
        {
            Nocturne.Use(t.Id);
            var ground = Nocturne.Bg;
            var addHl = Over(Nocturne.DiffAddHl, Over(Nocturne.DiffAddBg, ground));
            var delHl = Over(Nocturne.DiffDelHl, Over(Nocturne.DiffDelBg, ground));
            var current = Over(Nocturne.FindCurrentBg, ground);
            double changed = t.IsDark ? 4.5 : 3.0;

            AssertContrast(t, "added text on the word highlight", Nocturne.DiffAddText, addHl, changed);
            AssertContrast(t, "deleted text on the word highlight", Nocturne.DiffDelText, delHl, changed);
            AssertContrast(t, "body text on the added word highlight", Nocturne.Text, addHl, 3.0);
            AssertContrast(t, "body text on the deleted word highlight", Nocturne.Text, delHl, 3.0);
            AssertContrast(t, "body text on the current search hit", Nocturne.Text, current, 2.5);
        }
    }

    [Fact]
    public void TheWashIsCappedAndNeverLifted()
    {
        // The cap on a dark scheme's wash only ever pulls a colour down, and keeps its hue: a
        // scheme still tints in its own green, just not at a terminal's full blast. Solarized's
        // green is already dark and comes through at its printed lightness; a light theme's
        // is untouched.
        foreach (var t in ThemeCatalog.All)
        {
            if (t.Id == ThemeCatalog.Nocturne.Id) continue;   // drawn by hand, not derived
            var p = Palette.For(t);
            foreach (var (published, washed, what) in new[]
            {
                (t.Green, p.AddBase, "green"), (t.Red, p.DelBase, "red"), (t.Amber, p.MarkAmber, "amber"),
            })
            {
                if (!t.IsDark) { Assert.Equal(published, washed); continue; }
                var (h0, s0, l0) = ColorMath.ToHsl(published);
                var (h1, s1, l1) = ColorMath.ToHsl(washed);
                Assert.True(Math.Abs(h1 - h0) < 0.02, $"{t.Name}: the {what} wash changed hue");
                Assert.True(s1 <= s0 + 0.01 && l1 <= l0 + 0.01, $"{t.Name}: the {what} wash was lifted");
            }
        }
        Assert.Equal(ColorMath.ToHsl(Color.FromHex("#859900")).L,
            ColorMath.ToHsl(Palette.For(ThemeCatalog.SolarizedDark).AddBase).L, 2);
    }

    /// <summary>Source-over: <paramref name="top"/> at its alpha on an opaque
    /// <paramref name="under"/> — what the renderer puts on screen.</summary>
    private static Color Over(Color top, Color under)
    {
        double a = top.A / 255.0;
        return new(
            (byte)Math.Round(under.R + (top.R - under.R) * a),
            (byte)Math.Round(under.G + (top.G - under.G) * a),
            (byte)Math.Round(under.B + (top.B - under.B) * a));
    }

    private static void AssertContrast(NamedTheme t, string what, Color fg, Color bg, double floor)
    {
        double lf = ColorMath.Luminance(fg), lb = ColorMath.Luminance(bg);
        double ratio = (Math.Max(lf, lb) + 0.05) / (Math.Min(lf, lb) + 0.05);
        Assert.True(ratio >= floor, $"{t.Name}: {what} is {ratio:0.0}:1, under {floor}:1");
    }
}

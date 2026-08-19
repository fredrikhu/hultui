using HultUI.Core.Primitives;
using HultUI.Themes;
using Xunit;

namespace HultUI.Themes.Tests;

/// <summary>
/// What every scheme in the catalogue has to be true of, whoever added it.
///
/// <para>These are contrast and direction checks rather than colour comparisons. A palette
/// is a matter of taste and nothing here has an opinion about it; what is <i>not</i> a matter
/// of taste is that text can be read against the ground it is painted on, and that the ramps
/// run the way the call sites assume — which is the one thing a light scheme gets wrong if
/// the reversal in <see cref="Ramp"/> is ever undone.</para>
/// </summary>
public class ThemeCatalogTests
{
    public static TheoryData<string> ThemeIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var t in ThemeCatalog.All) data.Add(t.Id);
            return data;
        }
    }

    private static NamedTheme Theme(string id) => ThemeCatalog.ById(id);

    /// <summary>WCAG contrast ratio, so a failure can be read as a number people already
    /// have an intuition for: 4.5 is the AA threshold for body text, 3.0 for large text.</summary>
    private static double Contrast(Color a, Color b)
    {
        double la = ColorMath.Luminance(a), lb = ColorMath.Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void TextIsReadableOnTheGroundAndOnASurface(string id)
    {
        var t = Theme(id);

        Assert.True(Contrast(t.Text, t.Bg) >= 4.5,
            $"{t.Name}: body text on the ground is {Contrast(t.Text, t.Bg):0.00}:1, under the 4.5:1 floor");
        Assert.True(Contrast(t.Text, t.Surface) >= 4.5,
            $"{t.Name}: body text on a raised surface is {Contrast(t.Text, t.Surface):0.00}:1, under the 4.5:1 floor");
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void TheGroundAndTheSurfaceAreTellableApart(string id)
    {
        var t = Theme(id);
        // Not a contrast threshold — a raised bar is meant to be a shade, not a border. But
        // identical values mean the surface has vanished, which is a transcription slip.
        Assert.True(Math.Abs(ColorMath.Luminance(t.Bg) - ColorMath.Luminance(t.Surface)) > 0.002,
            $"{t.Name}: the ground and the raised surface are the same colour");
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void IsDarkAgreesWithTheGround(string id)
    {
        var t = Theme(id);
        // Every ramp's direction is decided by this flag, so a scheme that mislabels itself
        // inverts its entire palette rather than just looking slightly off.
        Assert.Equal(t.IsDark, ColorMath.Luminance(t.Bg) < ColorMath.Luminance(t.Text));
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void RampsRunAwayFromTheGround(string id)
    {
        var t = Theme(id);

        foreach (var (name, ramp) in new (string, Color[])[]
        {
            ("neutral", t.Neutrals), ("accent", t.Accents), ("accent-2", t.Accent2s),
        })
        {
            Assert.Equal(Ramp.Steps, ramp.Length);

            // Step 100 is the far end from the background and step 900 the near end, on a
            // light scheme as much as a dark one. Call sites depend on this: Accent900 backs
            // a selected row and Accent200 is the label on it, and a ramp numbered the other
            // way makes that pair near-black on near-black.
            double first = ColorMath.Luminance(ramp[0]), last = ColorMath.Luminance(ramp[^1]);
            if (t.IsDark)
                Assert.True(first > last, $"{t.Name} {name}: step 100 should be the lightest on a dark scheme");
            else
                Assert.True(first < last, $"{t.Name} {name}: step 100 should be the darkest on a light scheme");
        }
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void RampsAreMonotonic(string id)
    {
        var t = Theme(id);
        foreach (var (name, ramp) in new (string, Color[])[]
        {
            ("neutral", t.Neutrals), ("accent", t.Accents), ("accent-2", t.Accent2s),
        })
            for (int i = 1; i < ramp.Length; i++)
            {
                double prev = ColorMath.Luminance(ramp[i - 1]), cur = ColorMath.Luminance(ramp[i]);
                bool ordered = t.IsDark ? cur < prev : cur > prev;
                Assert.True(ordered,
                    $"{t.Name} {name}: step {(i + 1) * 100} does not continue the scale set by step {i * 100}");
            }
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void TheNearEndOfTheNeutralRampSitsCloseToTheGround(string id)
    {
        var t = Theme(id);
        // Neutral900 is the hover tint and the zebra stripe. It has to lift off the ground
        // without becoming a block of its own, which is what GenerateNeutral's tail is for.
        double delta = Math.Abs(ColorMath.Luminance(t.Neutrals[8]) - ColorMath.Luminance(t.Bg));
        Assert.True(delta < 0.06,
            $"{t.Name}: neutral-900 is {delta:0.000} off the ground — too far for a hover tint");
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void SelectedRowsReadTheirLabel(string id)
    {
        var t = Theme(id);
        // The pairing the nav and the selected tab actually use.
        Assert.True(Contrast(t.Accents[1], t.Accents[8]) >= 4.5,
            $"{t.Name}: accent-200 on accent-900 is {Contrast(t.Accents[1], t.Accents[8]):0.00}:1");
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void TheSemanticColoursStayApartFromEachOther(string id)
    {
        var t = Theme(id);
        // Green, red and amber carry meaning — added, deleted, found. Two of them landing on
        // the same colour makes a diff unreadable, and it is an easy transcription slip.
        Assert.NotEqual(t.Red, t.Green);
        Assert.NotEqual(t.Red, t.Amber);
        Assert.NotEqual(t.Green, t.Amber);
    }

    [Fact]
    public void EveryIdIsUniqueAndEveryNameIsFilledIn()
    {
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.All.Select(t => t.Id).Distinct().Count());
        Assert.All(ThemeCatalog.All, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Blurb));
            Assert.False(string.IsNullOrWhiteSpace(t.SyntaxTheme));
        });
    }

    [Fact]
    public void AnUnknownIdFallsBackToTheDefaultRatherThanThrowing()
    {
        // This is what a settings file written by a different build reads as, so it has to
        // be a fallback and not an exception on startup.
        Assert.Same(ThemeCatalog.Nocturne, ThemeCatalog.ById("gruvbox-chartreuse"));
        Assert.Same(ThemeCatalog.Nocturne, ThemeCatalog.ById(null));
        Assert.Same(ThemeCatalog.Nocturne, ThemeCatalog.ById(""));
    }

    [Fact]
    public void TheDefaultIsFirstInTheList()
    {
        // The picker lists them in this order, and the house style leading is the point.
        Assert.Same(ThemeCatalog.Nocturne, ThemeCatalog.All[0]);
    }
}

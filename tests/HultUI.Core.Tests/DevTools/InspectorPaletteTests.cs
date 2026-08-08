using HultUI.Core.DevTools;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.DevTools;

/// <summary>
/// The inspector's chrome colors. The case that matters is a partially specified
/// theme: <see cref="Theme"/> is a record whose every token defaults to a light
/// value, and real apps override only the handful they care about. A dark theme that
/// never assigns SurfaceVariant or Border still reports slate-100 and slate-200 for
/// them, so any palette that reads those tokens directly paints near-white buttons
/// and divider lines onto a dark panel.
/// </summary>
public class InspectorPaletteTests
{
    // What a real app's theme looks like: dark ground and text, an accent, and
    // nothing else touched (this is Nocturne's shape).
    private static Theme PartialDarkTheme() => new()
    {
        Background = new Color(20, 22, 30),
        TextColor = new Color(226, 228, 238),
        Primary = new Color(126, 154, 255),
    };

    private static bool IsLight(Color c) => InspectorPalette.IsLight(c);

    [Fact]
    public void PartiallySpecifiedDarkTheme_ProducesDarkChrome()
    {
        var theme = PartialDarkTheme();
        Assert.True(IsLight(theme.SurfaceVariant), "precondition: the unset token is still a light default");
        Assert.True(IsLight(theme.Border), "precondition: the unset token is still a light default");

        var p = InspectorPalette.From(theme);

        // Every surface tracks the background, not the tokens the theme never set.
        Assert.False(IsLight(p.Surface));
        Assert.False(IsLight(p.Control));
        Assert.False(IsLight(p.ControlHover));
        Assert.False(IsLight(p.RowHover));
        Assert.False(IsLight(p.Border));
    }

    [Fact]
    public void PartiallySpecifiedLightTheme_ProducesLightChrome()
    {
        var p = InspectorPalette.From(new Theme
        {
            Background = Color.White,
            TextColor = new Color(15, 23, 42),
            Primary = new Color(79, 70, 229),
        });

        Assert.True(IsLight(p.Surface));
        Assert.True(IsLight(p.Control));
        Assert.True(IsLight(p.Border));
    }

    [Fact]
    public void SurfacesStepAwayFromTheBackgroundInOrder()
    {
        var p = InspectorPalette.From(PartialDarkTheme());

        // Each step is a bit further from the ground than the last, so a row hover
        // is subtler than a control, which is subtler than that control hovered.
        float ground = Distance(p.Background, p.Background);
        Assert.Equal(0f, ground);
        Assert.True(Distance(p.Background, p.RowHover) < Distance(p.Background, p.Control));
        Assert.True(Distance(p.Background, p.Control) < Distance(p.Background, p.ControlHover));
    }

    [Fact]
    public void MutedTextSitsBetweenTheTextColorAndTheBackground()
    {
        var theme = PartialDarkTheme();
        var p = InspectorPalette.From(theme);

        Assert.True(Distance(p.Background, p.Muted) > 0);
        Assert.True(Distance(p.Background, p.Muted) < Distance(p.Background, p.Text));
    }

    [Fact]
    public void AccentText_ContrastsWithTheAccent_RatherThanTrustingPrimaryText()
    {
        // Theme.PrimaryText defaults to white, which is unreadable on a pale accent —
        // and an app that sets a pale Primary usually hasn't thought about PrimaryText.
        var pale = InspectorPalette.From(new Theme { Primary = new Color(250, 220, 120) });
        Assert.False(IsLight(pale.AccentText));

        var deep = InspectorPalette.From(new Theme { Primary = new Color(60, 40, 160) });
        Assert.True(IsLight(deep.AccentText));
    }

    [Fact]
    public void Mix_KeepsTheBaseAlpha_AndHitsBothEnds()
    {
        var a = new Color(0, 0, 0, 200);
        var b = new Color(255, 255, 255, 10);

        Assert.Equal(a, InspectorPalette.Mix(a, b, 0f));
        Assert.Equal(new Color(255, 255, 255, 200), InspectorPalette.Mix(a, b, 1f));
        Assert.Equal(new Color(128, 128, 128, 200), InspectorPalette.Mix(a, b, 0.5f));
    }

    private static float Distance(Color a, Color b)
    {
        float dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return MathF.Sqrt(dr * dr + dg * dg + db * db);
    }
}

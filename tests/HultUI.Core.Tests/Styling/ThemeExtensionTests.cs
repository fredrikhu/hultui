using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Styling;

/// <summary>
/// The seam between "a control author picks defaults" and "an app imposes its palette on a
/// control it did not write". <see cref="GaugeTheme"/> stands in for a control library that
/// ships outside this assembly and knows nothing about the app using it.
/// </summary>
public class ThemeExtensionTests
{
    /// <summary>A third-party control's knobs, deriving what it can from the palette.</summary>
    private sealed class GaugeTheme : IThemeExtension<GaugeTheme>
    {
        public Color Track { get; init; }
        public Color Fill { get; init; }
        public float Thickness { get; init; } = 4f;

        public static GaugeTheme CreateDefault(Theme theme) => new()
        {
            Track = theme.SurfaceVariant,
            Fill = theme.Primary,
        };
    }

    [Fact]
    public void AnUnstyledExtension_DerivesFromThePalette()
    {
        // The point of handing CreateDefault the theme: a control nobody has themed still
        // looks like it belongs, in whichever palette it lands in.
        Assert.Equal(Theme.Light.Primary, Theme.Light.Get<GaugeTheme>().Fill);
        Assert.Equal(Theme.Dark.Primary, Theme.Dark.Get<GaugeTheme>().Fill);
        Assert.NotEqual(Theme.Light.Get<GaugeTheme>().Fill, Theme.Dark.Get<GaugeTheme>().Fill);
    }

    [Fact]
    public void TheDefaultIsMaterializedOnce()
    {
        var theme = new Theme();
        Assert.Same(theme.Get<GaugeTheme>(), theme.Get<GaugeTheme>());
    }

    [Fact]
    public void With_OverridesTheDefault()
    {
        var brand = new GaugeTheme { Fill = Color.Red, Track = Color.White, Thickness = 9f };

        var themed = Theme.Dark.With(brand);

        Assert.Same(brand, themed.Get<GaugeTheme>());
        Assert.Equal(9f, themed.Get<GaugeTheme>().Thickness);
    }

    [Fact]
    public void With_DoesNotTouchTheThemeItCopied()
    {
        // Theme.Light and Theme.Dark are shared process-wide. A With() that mutated in
        // place would leak one app's brand colour into every other consumer in the
        // process — a second window, or the DevTools inspector.
        var before = Theme.Dark.Get<GaugeTheme>().Fill;

        _ = Theme.Dark.With(new GaugeTheme { Fill = Color.Red });

        Assert.Equal(before, Theme.Dark.Get<GaugeTheme>().Fill);
        Assert.False(Theme.Dark.IsOverridden<GaugeTheme>());
    }

    [Fact]
    public void With_ReturnsANewInstance_SoElementsSeeAChange()
    {
        // TabsElement rebuilds on ReferenceEquals(theme, _theme) failing, so a theme that
        // mutated in place would leave stale visuals behind.
        var themed = Theme.Dark.With(new GaugeTheme { Fill = Color.Red });
        Assert.NotSame(Theme.Dark, themed);
    }

    [Fact]
    public void With_CarriesTheRestOfTheTheme()
    {
        var themed = Theme.Dark.With(new GaugeTheme { Fill = Color.Red });

        Assert.Equal(Theme.Dark.Background, themed.Background);
        Assert.Equal(Theme.Dark.Primary, themed.Primary);
        Assert.Equal(Theme.Dark.FontFamily, themed.FontFamily);
        // Including extensions the app had already overridden.
        Assert.Equal(Theme.Dark.Tooltip.Background, themed.Tooltip.Background);
    }

    [Fact]
    public void With_ChainsForSeveralExtensions()
    {
        var themed = Theme.Light
            .With(new GaugeTheme { Fill = Color.Red })
            .With(new ButtonTheme { FontSize = 20f });

        Assert.Equal(Color.Red, themed.Get<GaugeTheme>().Fill);
        Assert.Equal(20f, themed.Button.FontSize);
    }

    [Fact]
    public void ADefaultDerivedBeforeAnOverride_IsNotCarriedOver()
    {
        var theme = new Theme();
        _ = theme.Get<GaugeTheme>();   // materialize the default first

        var themed = theme.With(new GaugeTheme { Fill = Color.Red });

        Assert.Equal(Color.Red, themed.Get<GaugeTheme>().Fill);
    }

    // ──── The built-in themes go through the same store ────

    [Fact]
    public void BuiltInProperties_AreTheExtensionStore()
    {
        var theme = new Theme();

        // The facade and the generic accessor are two views of one thing, which is the
        // whole point of the migration: no privileged mechanism for the framework's own
        // controls.
        Assert.Same(theme.Button, theme.Get<ButtonTheme>());
        Assert.Same(theme.Tooltip, theme.Get<TooltipTheme>());
    }

    [Fact]
    public void AnInitializerAssignment_ReadsBack()
    {
        var theme = new Theme { Button = new ButtonTheme { FontSize = 22f } };

        Assert.Equal(22f, theme.Button.FontSize);
        Assert.Equal(22f, theme.Get<ButtonTheme>().FontSize);
        Assert.True(theme.IsOverridden<ButtonTheme>());
        Assert.False(theme.IsOverridden<CheckboxTheme>());
    }

    [Fact]
    public void DarkKeepsItsExplicitTooltipAndMenu()
    {
        // Theme.Dark sets these in its own initializer. If the facade migration dropped
        // them, tooltips and context menus would silently go light in dark mode.
        Assert.Equal(new Color(51, 65, 85), Theme.Dark.Tooltip.Background);
        Assert.Equal(new Color(226, 232, 240), Theme.Dark.Tooltip.TextColor);
        Assert.Equal(new Color(30, 41, 59), Theme.Dark.ContextMenu.Background);

        // …and Light still gets the literal defaults, which are different.
        Assert.Equal(new Color(15, 23, 42), Theme.Light.Tooltip.Background);
    }
}

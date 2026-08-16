using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The whole point of theme extension is the seam an app crosses to restyle a control it
/// did not write, so proving it inside the framework proves nothing. <see cref="GaugeElement"/>
/// and its theme are written here exactly as a control library would write them — no
/// framework changes, no privileged access — and driven through a real session including a
/// runtime theme switch.
/// </summary>
public class ThirdPartyControlThemingTests
{
    // ──── What a control library ships ────

    private sealed class GaugeTheme : IThemeExtension<GaugeTheme>
    {
        public Color Track { get; init; }
        public Color Fill { get; init; }

        public static GaugeTheme CreateDefault(Theme theme) => new()
        {
            Track = theme.SurfaceVariant,
            Fill = theme.Primary,
        };
    }

    private sealed class GaugeElement : BoxElement
    {
        public Color ResolvedTrack { get; private set; }
        public Color ResolvedFill { get; private set; }

        protected internal override void ApplyTheme(Theme theme)
        {
            var gauge = theme.Get<GaugeTheme>();
            ResolvedTrack = gauge.Track;
            ResolvedFill = gauge.Fill;
            BackgroundColor = gauge.Fill;
        }
    }

    private static (HeadlessSession, GaugeElement) Mount(Theme theme)
    {
        var session = HeadlessSession.Create(400, 200);
        session.Application.Theme = theme;

        var gauge = new GaugeElement();
        gauge.Width(Dimension.Px(100)).Height(Dimension.Px(20));
        session.Window.SetRoot(Flex.Column().Children(gauge));
        session.PumpFrame();
        return (session, gauge);
    }

    [Fact]
    public void AnUnthemedControl_TakesTheAppPalette()
    {
        var (session, gauge) = Mount(Theme.Dark);
        using (session)
        {
            Assert.Equal(Theme.Dark.Primary, gauge.ResolvedFill);
            Assert.Equal(Theme.Dark.SurfaceVariant, gauge.ResolvedTrack);
        }
    }

    [Fact]
    public void TheAppCanImposeItsBrand_OnAControlItDidNotWrite()
    {
        var brandRed = new Color(200, 30, 60);
        var (session, gauge) = Mount(Theme.Dark.With(new GaugeTheme
        {
            Fill = brandRed,
            Track = Color.Black,
        }));

        using (session)
        {
            Assert.Equal(brandRed, gauge.ResolvedFill);
            Assert.Equal(brandRed, gauge.BackgroundColor);
        }
    }

    [Fact]
    public void ARuntimeThemeSwitch_ReachesTheExtension()
    {
        // The pass that matters: nothing in GaugeElement subscribes to anything, it just
        // reads the theme in ApplyTheme. If the style pass did not re-run for extensions
        // the way it does for built-ins, a third-party control would freeze at whatever
        // palette it first resolved.
        var (session, gauge) = Mount(Theme.Light);
        using (session)
        {
            Assert.Equal(Theme.Light.Primary, gauge.ResolvedFill);

            session.Application.Theme = Theme.Dark;
            session.PumpFrame();

            Assert.Equal(Theme.Dark.Primary, gauge.ResolvedFill);
        }
    }

    [Fact]
    public void SwitchingToABrandedTheme_RepaintsTheControl()
    {
        var brandRed = new Color(200, 30, 60);
        var (session, gauge) = Mount(Theme.Light);
        using (session)
        {
            session.Application.Theme = Theme.Light.With(new GaugeTheme
            {
                Fill = brandRed,
                Track = Color.White,
            });
            session.PumpFrame();

            Assert.Equal(brandRed, gauge.ResolvedFill);
            Assert.Equal(brandRed, gauge.BackgroundColor);
        }
    }

    [Fact]
    public void OverridingOneExtension_LeavesTheBuiltInsAlone()
    {
        var themed = Theme.Dark.With(new GaugeTheme { Fill = Color.Red, Track = Color.Black });
        var (session, _) = Mount(themed);
        using (session)
        {
            // A brand override for one control must not disturb the rest of the palette.
            Assert.Equal(Theme.Dark.Tooltip.Background, session.Application.Theme.Tooltip.Background);
            Assert.Equal(Theme.Dark.Button.FontSize, session.Application.Theme.Button.FontSize);
        }
    }
}

using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

// ─────────────────────────────────────────────────────────────────────────────
// Theming a control the app did not write.
//
// Everything under "the control library" below is written the way a package
// shipped from outside this repo would write it: no framework changes, no
// privileged access to Theme, nothing registered at startup. It declares one
// theme type, derives its defaults from the app's palette, and reads them in
// ApplyTheme. That is the entire contract.
//
// The app then does what an app wants to do — switch light/dark, and impose its
// own brand colours on a control whose source it does not own — with one call
// each. Watch the gauges follow both.
// ─────────────────────────────────────────────────────────────────────────────

var app = new Application("Theming a control you didn't write", 760, 560);

var brandRed = new Color(219, 39, 76);
var brandSand = new Color(244, 233, 214);

bool dark = false;
bool branded = false;

Theme Current()
{
    var basePalette = dark ? Theme.Dark : Theme.Light;

    // One line is the whole override story. With() copies, so Theme.Dark itself
    // is never touched — another window in this process still sees stock indigo.
    return branded
        ? basePalette.With(new GaugeTheme { Fill = brandRed, Track = brandSand, Thickness = 10f })
        : basePalette;
}

var gauges = new[]
{
    new GaugeElement { Value = 0.25f },
    new GaugeElement { Value = 0.6f },
    new GaugeElement { Value = 0.92f },
};

var stateLabel = Text.New("").FontSize(13f);

void Refresh()
{
    app.Theme = Current();
    stateLabel.Content = $"{(dark ? "Dark" : "Light")} palette · " +
        (branded ? "GaugeTheme overridden by the app" : "GaugeTheme default, derived from the palette");
}

app.SetRoot(Flex.Column(gap: 20).Padding(28).Children(
    Flex.Column(gap: 6).Children(
        Text.New("A control from someone else's package").FontSize(21f).FontWeight(FontWeight.Bold),
        Text.New("GaugeElement knows nothing about this app. It reads theme.Get<GaugeTheme>() " +
                 "in ApplyTheme, and that is all it takes to follow the palette and to be " +
                 "restyled by an app that never saw its source.")
            .FontSize(13f).MaxWidth(Dimension.Px(620))
    ),

    Flex.Column(gap: 14).Children([.. gauges.Cast<Element>()]),

    stateLabel,

    Flex.Row(gap: 10).Children(
        Button.New("Toggle light / dark").OnClick(_ => { dark = !dark; Refresh(); }),
        Button.New("Toggle the app's brand override").OnClick(_ => { branded = !branded; Refresh(); })
    ),

    Text.New("Unbranded, the gauge derives Fill from theme.Primary and Track from " +
             "theme.SurfaceVariant, so it already belongs in either palette. Branded, the app " +
             "replaces the whole GaugeTheme — and nothing else in the theme moves.")
        .FontSize(12f).MaxWidth(Dimension.Px(620))
));

Refresh();
app.Run();


// ─────────────────────────────────────────────────────────────────────────────
// The control library — imagine this in its own NuGet package.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The gauge's themable knobs. Defaults come from the app's palette rather than
/// being hardcoded, which is why an unstyled gauge already looks right in a dark
/// theme instead of needing to be re-themed for every palette.
/// </summary>
public sealed class GaugeTheme : IThemeExtension<GaugeTheme>
{
    public Color Track { get; init; }
    public Color Fill { get; init; }
    public float Thickness { get; init; } = 14f;
    public CornerRadius Rounding { get; init; } = new(7f);

    public static GaugeTheme CreateDefault(Theme theme) => new()
    {
        Track = theme.SurfaceVariant,
        Fill = theme.Primary,
    };
}

/// <summary>
/// A horizontal bar. The only theming code is the ApplyTheme override: the style
/// pass calls it whenever the theme changes, so nothing here subscribes to
/// anything and a runtime palette switch just works.
/// </summary>
public sealed class GaugeElement : Element
{
    private GaugeTheme _theme = GaugeTheme.CreateDefault(Theme.Light);

    /// <summary>How full the gauge is, 0..1.</summary>
    public float Value { get; init; } = 0.5f;

    // `protected`, not `protected internal`: from outside the framework assembly that is
    // the only form the compiler accepts, and it is what a real control library writes.
    protected override void ApplyTheme(Theme theme)
    {
        _theme = theme.Get<GaugeTheme>();

        // Height is a themed knob, so a theme change is a layout change too —
        // the style pass does not imply one (HULTUI002 exists for this reason).
        if (Box.Height != Dimension.Px(_theme.Thickness))
        {
            Box.Height = Dimension.Px(_theme.Thickness);
            MarkLayoutDirty();
        }
    }

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteBorderBox;
        ctx.DrawRoundedRect(box, _theme.Rounding, _theme.Track, Color.Transparent, default);

        float filled = Math.Clamp(Value, 0f, 1f) * box.Width;
        if (filled > 0)
            ctx.DrawRoundedRect(box with { Width = filled }, _theme.Rounding, _theme.Fill,
                Color.Transparent, default);
    }
}

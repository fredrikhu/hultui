using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace PerfSuite;

static partial class Scenarios
{
    // A rendered frame repaints everything in view, so one tile changing colour times the
    // paint of the whole screen. The same 400 tiles in five finishes put a price on each
    // effect: compare a variant's render column with paint/tiles plain. Headless renders on
    // the CPU, so these are raster costs, not GPU ones — the relative order is the signal.
    private static IEnumerable<Scenario> Paint()
    {
        yield return Tiles("plain", "flat fills", t => t);
        yield return Tiles("rounded + border", "corner radius 10 and a 1 px border",
            t => t.CornerRadius(10f).BorderColor(Ui.Line).BorderWidth(1f));
        yield return Tiles("shadow", "rounded with a blurred drop shadow",
            t => t.CornerRadius(10f).BoxShadow(0, 4, 14, new Color(15, 23, 42, 55)));
        yield return Tiles("gradient", "rounded with a 135° linear gradient fill",
            t => t.CornerRadius(10f).Background(Gradient.Linear(135f, Color.FromHex("#6366F1"), Color.FromHex("#8B5CF6"))));
        yield return Tiles("transform + opacity", "rotated 4° and 85 % opaque",
            t => t.CornerRadius(10f).Transform(Transform2D.Rotate(4f)).Opacity(0.85f));

        yield return new Scenario("paint/dense text screen",
            "A screen packed with ~1,600 small labels; one label changes colour, the whole view repaints",
            ctx =>
            {
                var labels = new TextElement[3_000];
                for (int i = 0; i < labels.Length; i++)
                    labels[i] = Text.New(Ui.Label(ctx.Rng)).FontSize(11f).Width(Dimension.Px(165));
                var wrap = Flex.Row(gap: 4).FlexWrap(FlexWrap.Wrap).FlexShrink(0).Padding(6).Children(labels);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(wrap));
                // Inherited text properties are builder state: a live change asks for its own restyle.
                return new Op
                {
                    Run = step =>
                    {
                        var label = labels[step % 200].TextColor((step / 200) % 2 == 0 ? Ui.Accent : Color.Black);
                        label.MarkStyleDirty();
                    },
                };
            });
    }

    private static Scenario Tiles(string variant, string description, Func<BoxElement, BoxElement> finish)
        => new($"paint/tiles {variant}",
            $"400 tiles ({description}); one tile changes colour, the whole view repaints",
            ctx =>
            {
                var tiles = new BoxElement[400];
                for (int i = 0; i < tiles.Length; i++)
                    tiles[i] = finish(Flex.Column().Width(Dimension.Px(112)).Height(Dimension.Px(72)).FlexShrink(0)
                        .Background(Ui.CardFill).Padding(8)
                        .Children(Text.New($"Tile {i}").FontSize(12f).FontWeight(FontWeight.SemiBold)));
                var wrap = Flex.Row(gap: 12).FlexWrap(FlexWrap.Wrap).FlexShrink(0).Padding(12).Children(tiles);
                ctx.Mount(ScrollView.New().FlexGrow(1).Background(Color.FromHex("#EEF2F7")).Children(wrap));
                var target = tiles[9];
                return new Op { Run = step => target.BackgroundColor = step % 2 == 0 ? Ui.Selected : Ui.CardFill };
            });
}

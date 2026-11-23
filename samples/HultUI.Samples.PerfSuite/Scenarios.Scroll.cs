using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace PerfSuite;

static partial class Scenarios
{
    private static IEnumerable<Scenario> Scroll()
    {
        yield return new Scenario("scroll/scrollbar drag over 50 GiB",
            $"Drag a ScrollbarElement over a 50 GiB document 7 px a step, beside the canvas of text/canvas lines: "
            + "its OnChanged writes the top line and rebuilds the viewer, which draws {CanvasLines.Rows} lines from there",
            ctx =>
            {
                var viewer = new BigDocument(ctx);
                var host = viewer.Mount();
                ctx.Mount(host);
                // The live bar: a build's is a twin the next patch is copied from.
                var bar = (ScrollbarElement)host.Children[0].Children[1];
                var thumb = bar.WindowContentBox;
                float x = thumb.X + thumb.Width / 2, y0 = thumb.Y + 5;
                ctx.Window.MouseDown(x, y0);
                // Down the track and back, so every step moves the thumb.
                static float Along(int step) => 7f * (step % 180 < 90 ? step % 90 + 1 : 89 - step % 90);
                int builds = 0;
                return new Op
                {
                    Run = step => { builds = viewer.Builds; ctx.Window.MoveMouse(x, y0 + Along(step)); },
                    Figure = () => viewer.Builds - builds,
                    FigureUnit = "builds/frame",
                    Check = _ => !bar.IsDragging ? "the bar is not being dragged"
                        : viewer.Lines.Drawn(viewer.Top / BigDocument.BytesPerLine),
                };
            });

        yield return new Scenario("scroll/wheel 5,000-row list",
            "Mouse-wheel 3 notches through a non-virtualized 5,000-row list: no layout, paint culled to the viewport",
            ctx =>
            {
                var view = ScrollView.New().FlexGrow(1).Children(Ui.Rows(ctx.Rng, 5_000));
                ctx.Mount(view);
                return new Op
                {
                    Run = step => ctx.Window.Wheel(500, 380, 0, WheelDirection(step) * 3),
                    Check = ScrolledTo(() => view.ScrollOffsetY),
                };
            });

        yield return new Scenario("scroll/jump 5,000-row list",
            "Set the scroll offset of the 5,000-row list to somewhere new: a whole viewport of rows not painted last frame",
            ctx =>
            {
                var view = ScrollView.New().FlexGrow(1).Children(Ui.Rows(ctx.Rng, 5_000));
                ctx.Mount(view);
                return new Op { Run = step => view.ScrollOffsetY = (step * 7_919 % 4_800) * 31f };
            });

        yield return new Scenario("scroll/sticky sections wheel",
            "Wheel through 40 sections of 25 rows, each with a sticky header that pins and hands over",
            ctx =>
            {
                var sections = new Element[40];
                int id = 1;
                for (int s = 0; s < sections.Length; s++)
                {
                    var rows = new Element[26];
                    rows[0] = Box.New().Sticky(top: 0).ZIndex(1).Padding(new Thickness(6, 16))
                        .Background(Color.FromHex("#E0E7FF"))
                        .Children(Text.New($"Section {s + 1}").FontSize(14f).FontWeight(FontWeight.Bold));
                    for (int r = 1; r < rows.Length; r++) rows[r] = Ui.Row(id++, Ui.Label(ctx.Rng));
                    sections[s] = Flex.Column().FlexShrink(0).Children(rows);
                }
                var view = ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(sections));
                ctx.Mount(view);
                return new Op
                {
                    Run = step => ctx.Window.Wheel(500, 380, 0, WheelDirection(step) * 3),
                    Check = ScrolledTo(() => view.ScrollOffsetY),
                };
            });

        yield return new Scenario("scroll/nested chaining wheel",
            "Wheel over one of 8 inner lists inside an outer scroll view: inner scrolls to its end, then the outer takes over",
            ctx =>
            {
                var panes = new Element[8];
                for (int p = 0; p < panes.Length; p++)
                    panes[p] = ScrollView.New().Height(Dimension.Px(260)).FlexShrink(0)
                        .BorderColor(Ui.Line).BorderWidth(1f)
                        .Children(Ui.Rows(ctx.Rng, 60, firstId: p * 100));
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Flex.Column(gap: 16).FlexShrink(0).Padding(16).Children(panes)));
                return new Op { Run = step => ctx.Window.Wheel(500, 200, 0, WheelDirection(step) * 5) };
            });
    }

    /// <summary>A check that the view moved since the last step: a wheel that hit nothing scrolls nothing.</summary>
    private static Func<int, string?> ScrolledTo(Func<float> offset)
    {
        float last = offset();
        return _ =>
        {
            float now = offset();
            bool moved = now != last;
            last = now;
            return moved ? null : $"the view did not scroll (offset {now})";
        };
    }

    /// <summary>Down for 20 steps, then up for 20: the view keeps moving and never parks at an end.</summary>
    private static int WheelDirection(int step) => (step / 20) % 2 == 0 ? -1 : 1;

    /// <summary>A 50 GiB document's viewer: the canvas of <see cref="CanvasLines"/> and a
    /// <see cref="ScrollbarElement"/> over its bytes, rebuilt from the bar's reports.</summary>
    private sealed class BigDocument(Ctx ctx) : Component
    {
        public const long Length = 50L << 30;
        public const long BytesPerLine = 50;

        public readonly CanvasLines Lines = new(ctx, mount: false);
        public long Top;
        public int Builds;

        public override Element Build()
        {
            Builds++;
            Lines.TopLine = Top / BytesPerLine;
            var bar = HultUI.Core.Builders.Scrollbar.Vertical(Top, Length, CanvasLines.Rows * BytesPerLine);
            bar.OnChanged = v => { Top = v; MarkDirty(); };
            return Flex.Row().FlexGrow(1).AlignItems(AlignItems.Stretch).Children(Lines.NewCanvas(), bar);
        }
    }
}

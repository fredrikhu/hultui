using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Icons;
using HultUI.Themes;

namespace PerfSuite;

static partial class Scenarios
{
    private static IEnumerable<Scenario> Controls()
    {
        yield return new Scenario("controls/gallery mount",
            "Mount a settings-style screen of 25 control groups (buttons, checkboxes, switches, sliders, dropdowns, inputs, steppers, progress)",
            ctx =>
            {
                var host = Flex.Column().FlexGrow(1);
                ctx.Mount(host);
                return new Op
                {
                    Reset = _ => host.ReplaceChildren(),
                    Run = _ => host.ReplaceChildren(Gallery(ctx.Rng, 25)),
                };
            });

        yield return new Scenario("controls/tabs switch",
            "Click the next of five tab headers, each tab a page of 300 rows built on demand",
            ctx =>
            {
                var tabs = Tabs.New();
                for (int t = 0; t < 5; t++)
                {
                    int seed = t;
                    tabs.Tab($"Page {t + 1}", () => ScrollView.New().FlexGrow(1).Children(Ui.Rows(new Random(seed), 300)));
                }
                ctx.Mount(tabs.FlexGrow(1));
                return new Op
                {
                    Run = step => ctx.Window.Click(tabs.Children[0].Children[(step + 1) % 5]),
                    Check = step => tabs.SelectedIndex == (step + 1) % 5 ? null : $"tab {tabs.SelectedIndex} selected, not {(step + 1) % 5}",
                };
            });

        yield return new Scenario("controls/expander toggle (animated)",
            "Open or close one of 30 expanders (each 20 rows) and run its 0.18 s animation to the end: 12 frames",
            ctx =>
            {
                var expanders = new ExpanderElement[30];
                for (int i = 0; i < expanders.Length; i++)
                {
                    int seed = i;
                    expanders[i] = Expander.New($"Group {i + 1}", () => Ui.Rows(new Random(seed), 20)).DefaultExpanded(i % 4 == 0);
                }
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Flex.Column(gap: 4).FlexShrink(0).Padding(8).Children(expanders)));
                return new Op
                {
                    Run = step => expanders[1].IsExpanded = step % 2 == 0,
                    Frames = 12,
                    Check = step => expanders[1].Progress == (step % 2 == 0 ? 1f : 0f) ? null : $"the animation stopped at {expanders[1].Progress:0.00}",
                };
            });

        yield return new Scenario("controls/slider drag",
            "Drag a slider's thumb across its track (8 pointer moves), its value bound to a label",
            ctx =>
            {
                var label = Text.New("0").FontSize(13f);
                var slider = Slider.New(50, 0, 100).Width(Dimension.Px(600)).OnValueChanged(v => label.Content = $"{v:0}");
                ctx.Mount(Busy(ctx, Flex.Row(gap: 12).AlignItems(AlignItems.Center).Children(slider, label)));
                return new Op
                {
                    Run = step =>
                    {
                        var box = slider.WindowBorderBox;
                        float y = box.Y + box.Height / 2, mid = box.X + box.Width / 2;
                        float to = step % 2 == 0 ? box.X + 20 : box.X + box.Width - 20;
                        ctx.Window.Drag(new Point(mid, y), new Point(to, y), steps: 8);
                    },
                    Check = step => (step % 2 == 0 ? slider.Value < 20 : slider.Value > 80) ? null : $"the drag left the slider at {slider.Value:0}",
                };
            });

        yield return new Scenario("icons/mount 600",
            "Mount 600 vector icons (Phosphor, 20 px) in a grid of labelled tiles",
            ctx =>
            {
                var names = Phosphor.Names.ToArray();
                var host = Flex.Row(gap: 6).FlexWrap(FlexWrap.Wrap).FlexShrink(0).Padding(8);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(host));
                int color = 0;
                return new Op
                {
                    Reset = _ => host.ReplaceChildren(),
                    Run = _ =>
                    {
                        // A colour per step, so the icon cache is asked for pictures it has not drawn.
                        var tint = (color++ % 2 == 0) ? Ui.Accent : Color.FromHex("#0F766E");
                        var tiles = new Element[600];
                        for (int i = 0; i < tiles.Length; i++)
                            tiles[i] = Flex.Column(gap: 2).Width(Dimension.Px(70)).AlignItems(AlignItems.Center).Padding(4).Children(
                                Phosphor.Draw(names[i % names.Length], 20f, tint),
                                Text.New(names[i % names.Length]).FontSize(9f).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
                        host.ReplaceChildren(tiles);
                    },
                };
            });

        yield return new Scenario("theme/light-dark switch",
            "Switch the application theme on the 25-group control screen: every control restyles, the screen repaints",
            ctx =>
            {
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(Gallery(ctx.Rng, 25))));
                return new Op
                {
                    Run = step => ctx.Session.Application.Theme = step % 2 == 0 ? Theme.Dark : Theme.Light,
                    Check = step => ctx.Session.Application.Theme == (step % 2 == 0 ? Theme.Dark : Theme.Light) ? null : "the theme did not change",
                };
            });

        yield return new Scenario("theme/named scheme cycle",
            "Cycle through the HultUI.Themes catalogue on the control screen (mapped onto Theme)",
            ctx =>
            {
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(Gallery(ctx.Rng, 25))));
                var themes = ThemeCatalog.All.Select(t => new Theme
                {
                    Background = t.Bg, Surface = t.Surface, TextColor = t.Text, Primary = t.Accent,
                }).ToArray();
                return new Op { Run = step => ctx.Session.Application.Theme = themes[step % themes.Length] };
            });

        yield return new Scenario("anim/60 looping tweens frame",
            "One frame of 60 looping tweens (width, background, corner radius, opacity) on 60 boxes",
            ctx =>
            {
                var boxes = new BoxElement[60];
                for (int i = 0; i < boxes.Length; i++)
                    boxes[i] = Box.New().Width(Dimension.Px(100)).Height(Dimension.Px(24)).Background(Ui.Selected).CornerRadius(4f);
                ctx.Mount(Flex.Column().FlexGrow(1).Children(
                    Flex.Column(gap: 4).FlexShrink(0).Padding(8).Children(boxes),
                    ScrollView.New().FlexGrow(1).Children(Ui.Rows(ctx.Rng, 500))));
                for (int i = 0; i < boxes.Length; i++)
                {
                    float seconds = 1f + i % 5 * 0.1f;
                    switch (i % 4)
                    {
                        case 0: Animate.Width(boxes[i], Dimension.Px(100), Dimension.Px(400)).Duration(seconds).Loop().Yoyo().Start(); break;
                        case 1: Animate.Background(boxes[i], Ui.Selected, Ui.Accent).Duration(seconds).Loop().Yoyo().Start(); break;
                        case 2: Animate.CornerRadius(boxes[i], new CornerRadius(0f), new CornerRadius(12f)).Duration(seconds).Loop().Yoyo().Start(); break;
                        default: Animate.Opacity(boxes[i], 1f, 0.2f).Duration(seconds).Loop().Yoyo().Start(); break;
                    }
                }
                return new Op { Run = _ => { } };
            });

        yield return new Scenario("anim/spinners + progress frame",
            "One frame of 24 spinners and 24 indeterminate progress bars over a 500-row list",
            ctx =>
            {
                var spinners = new Element[24];
                var bars = new Element[24];
                for (int i = 0; i < 24; i++)
                {
                    spinners[i] = Spinner.New(20f);
                    var bar = ProgressBar.New().Width(Dimension.Px(160));
                    bar.IsIndeterminate = true;
                    bars[i] = bar;
                }
                ctx.Mount(Flex.Column().FlexGrow(1).Children(
                    Flex.Row(gap: 8).FlexWrap(FlexWrap.Wrap).FlexShrink(0).Padding(8).Children(spinners),
                    Flex.Row(gap: 8).FlexWrap(FlexWrap.Wrap).FlexShrink(0).Padding(8).Children(bars),
                    ScrollView.New().FlexGrow(1).Children(Ui.Rows(ctx.Rng, 500))));
                return new Op { Run = _ => { } };
            });

        yield return new Scenario("anim/hover transitions",
            "Move between two of 40 buttons with 160 ms background and transform transitions: 10 frames of easing",
            ctx =>
            {
                var buttons = new BoxElement[40];
                for (int i = 0; i < buttons.Length; i++)
                    buttons[i] = Box.New().Width(Dimension.Px(110)).Height(Dimension.Px(40)).CornerRadius(8f)
                        .Background(Ui.Accent)
                        .OnHover(b => b.Background(Color.FromHex("#4338CA")).TranslateBy(0f, -3f))
                        .Transition(TransitionProperty.Background, 160f)
                        .Transition(TransitionProperty.Transform, 160f)
                        .Children(Text.New($"Action {i}").FontSize(13f).TextColor(Color.White));
                ctx.Mount(Flex.Row(gap: 10).FlexWrap(FlexWrap.Wrap).FlexShrink(0).Padding(12).Children(buttons));
                return new Op
                {
                    Run = step =>
                    {
                        var box = buttons[step % 2 == 0 ? 3 : 12].WindowBorderBox;
                        ctx.Window.MoveMouse(box.X + box.Width / 2, box.Y + box.Height / 2);
                    },
                    Frames = 10,
                };
            });
    }

    /// <summary>A settings screen: <paramref name="groups"/> cards of every common control.</summary>
    private static Element[] Gallery(Random rng, int groups)
    {
        var cards = new Element[groups];
        for (int g = 0; g < groups; g++)
            cards[g] = Flex.Column(gap: 8).Width(Dimension.Px(300)).FlexShrink(0).Padding(14)
                .Background(Ui.CardFill).CornerRadius(10f).BorderColor(Ui.Line).BorderWidth(1f)
                .Children(
                    Text.New($"Group {g + 1}").FontSize(15f).FontWeight(FontWeight.SemiBold),
                    Flex.Row(gap: 8).Children(Button.New("Apply"), Button.New("Reset", ButtonVariant.Secondary)),
                    Checkbox.New(g % 2 == 0, Ui.Label(rng)),
                    ToggleSwitch.New(g % 3 == 0, Ui.Label(rng)),
                    Slider.New(g * 4 % 100, 0, 100),
                    Dropdown.New(["Small", "Medium", "Large", "Huge"], g % 4),
                    TextInput.New(Ui.Label(rng)),
                    NumericUpDown.New(g, 0, 100, 1),
                    ProgressBar.New(g % 10 / 10.0));
        return [Flex.Row(gap: 12).FlexWrap(FlexWrap.Wrap).FlexShrink(0).Padding(12).Children(cards)];
    }
}

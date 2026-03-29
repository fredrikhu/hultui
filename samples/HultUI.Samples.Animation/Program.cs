using HultUI;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Events;
using HultUI.Core.Styling;

using var app = new Application("Animations", 600, 650);
app.Theme = Theme.Dark;
app.SetRoot(new AnimationDemo().Mount());
app.Run();

// ── Demo component ──

partial class AnimationDemo : Component
{
    [Reactive] public partial int TriggerCount { get; set; }

    public override Element Build()
    {
        var gen = TriggerCount; // track dependency + use as key suffix

        return ScrollView.New().Children(
            Flex.Column(gap: 24).Padding(24).FlexShrink(0).Children(

                // Header
                Text.New("Animation Showcase").FontSize(22f).FontWeight(FontWeight.Bold),

                // Trigger button
                Button.New("Play All Animations").OnClick(_ => TriggerCount++),

                // ── Fade In ──
                Section("Fade In",
                    AnimatedBox($"fadeIn-{gen}", new Color(99, 102, 241), box =>
                    {
                        box.Opacity = 0f;
                        Animate.Opacity(box, 0f, 1f).Duration(0.8f).Ease(Easing.EaseOutCubic).Start();
                    })
                ),

                // ── Expand Width ──
                Section("Expand Width (EaseOutBack)",
                    AnimatedBox($"expandW-{gen}", new Color(16, 185, 129), box =>
                    {
                        box.Box.Width = Dimension.Px(0);
                        Animate.Width(box, Dimension.Px(0), Dimension.Px(300))
                            .Duration(0.6f).Ease(Easing.EaseOutBack).Start();
                    })
                ),

                // ── Expand Height ──
                Section("Expand Height (EaseOutElastic)",
                    AnimatedBox($"expandH-{gen}", new Color(245, 158, 11), box =>
                    {
                        box.Box.Height = Dimension.Px(0);
                        Animate.Height(box, Dimension.Px(0), Dimension.Px(60))
                            .Duration(1f).Ease(Easing.EaseOutElastic).Start();
                    })
                ),

                // ── Color Transition ──
                Section("Background Color",
                    AnimatedBox($"color-{gen}", new Color(239, 68, 68), box =>
                    {
                        Animate.Background(box, new Color(239, 68, 68), new Color(59, 130, 246))
                            .Duration(1f).Ease(Easing.EaseInOutCubic).Start();
                    })
                ),

                // ── Corner Radius ──
                Section("Corner Radius",
                    AnimatedBox($"radius-{gen}", new Color(168, 85, 247), box =>
                    {
                        Animate.CornerRadius(box, new CornerRadius(0f), new CornerRadius(30f))
                            .Duration(0.8f).Ease(Easing.EaseOutCubic).Start();
                    })
                ),

                // ── Padding ──
                Section("Padding Grow",
                    PaddingDemo(gen)
                ),

                // ── Staggered (chained via delay) ──
                Section("Staggered Fade In",
                    StaggeredDemo(gen)
                ),

                // ── Chained (via OnComplete) ──
                Section("Chained Sequence",
                    ChainedDemo(gen)
                ),

                // ── Hover ──
                Section("Hover Transition",
                    HoverDemo()
                ),

                // ── Easing Comparison ──
                Section("Easing Comparison",
                    EasingComparison(gen)
                )
            )
        );
    }

    private static BoxElement AnimatedBox(string key, Color color, Action<BoxElement> animate)
    {
        var box = Box.New().Width(300).Height(60).Background(color).CornerRadius(8f);
        box.Key = key;
        animate(box);
        return box;
    }

    private Element PaddingDemo(int gen)
    {
        var box = Box.New().Background(new Color(20, 184, 166)).CornerRadius(8f)
            .Children(Text.New("Watch the padding grow").FontSize(13f));
        box.Key = $"padding-{gen}";
        box.Box.Padding = new Thickness(4f);
        Animate.Padding(box, new Thickness(4f), new Thickness(20f))
            .Duration(0.8f).Ease(Easing.EaseOutCubic).Start();
        return box;
    }

    private Element StaggeredDemo(int gen)
    {
        var colors = new[]
        {
            new Color(239, 68, 68),   // red
            new Color(245, 158, 11),  // amber
            new Color(16, 185, 129),  // emerald
            new Color(59, 130, 246),  // blue
            new Color(168, 85, 247),  // purple
        };

        var row = Flex.Row(gap: 8);
        row.Key = $"stagger-{gen}";
        var children = new Element[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            var box = Box.New().Width(50).Height(50).Background(colors[i]).CornerRadius(8f);
            box.Opacity = 0f;
            Animate.Opacity(box, 0f, 1f)
                .Duration(0.4f)
                .Delay(i * 0.1f)
                .Ease(Easing.EaseOutCubic)
                .Start();
            children[i] = box;
        }
        return row.Children(children);
    }

    private Element ChainedDemo(int gen)
    {
        var box = Box.New().Width(60).Height(60)
            .Background(new Color(239, 68, 68)).CornerRadius(4f);
        box.Key = $"chain-{gen}";

        // Phase 1: expand width
        Animate.Width(box, Dimension.Px(60), Dimension.Px(300))
            .Duration(0.4f).Ease(Easing.EaseOutCubic)
            .OnComplete(() =>
            {
                // Phase 2: change color
                Animate.Background(box, new Color(239, 68, 68), new Color(16, 185, 129))
                    .Duration(0.3f).Ease(Easing.EaseInOutCubic)
                    .OnComplete(() =>
                    {
                        // Phase 3: round corners
                        Animate.CornerRadius(box, new CornerRadius(4f), new CornerRadius(30f))
                            .Duration(0.3f).Ease(Easing.EaseOutBack).Start();
                    }).Start();
            }).Start();

        return box;
    }

    private Element EasingComparison(int gen)
    {
        var easings = new (string Name, Func<float, float> Func)[]
        {
            ("Linear", Easing.Linear),
            ("EaseOutQuad", Easing.EaseOutQuad),
            ("EaseOutCubic", Easing.EaseOutCubic),
            ("EaseOutBack", Easing.EaseOutBack),
            ("EaseOutElastic", Easing.EaseOutElastic),
            ("EaseOutExpo", Easing.EaseOutExpo),
        };

        var column = Flex.Column(gap: 6);
        column.Key = $"easings-{gen}";
        var children = new Element[easings.Length];
        for (int i = 0; i < easings.Length; i++)
        {
            var (name, func) = easings[i];
            var bar = Box.New().Height(28).Background(new Color(99, 102, 241)).CornerRadius(4f)
                .Children(
                    Text.New(name).FontSize(11f).Padding(new Thickness(4f, 8f, 4f, 8f))
                );
            bar.Box.Width = Dimension.Px(0);
            Animate.Width(bar, Dimension.Px(0), Dimension.Px(350))
                .Duration(1f).Delay(i * 0.15f).Ease(func).Start();
            children[i] = bar;
        }
        return column.Children(children);
    }

    private static Element HoverDemo()
    {
        var row = Flex.Row(gap: 12);
        var children = new Element[3];

        var configs = new (string Label, Color Normal, Color Hover)[]
        {
            ("Primary", new Color(99, 102, 241), new Color(129, 140, 248)),
            ("Success", new Color(16, 185, 129), new Color(52, 211, 153)),
            ("Danger", new Color(239, 68, 68), new Color(248, 113, 113)),
        };

        for (int i = 0; i < configs.Length; i++)
        {
            var (label, normal, hover) = configs[i];
            var btn = Box.New()
                .Padding(new Thickness(12, 24, 12, 24))
                .Background(normal)
                .CornerRadius(8f)
                .Children(Text.New(label).FontSize(14f).FontWeight(FontWeight.SemiBold));
            btn.Inherited.Cursor = CursorType.Pointer;

            TweenHandle handle = default;
            btn.On<PointerEnterEvent>(_ =>
            {
                handle.Cancel();
                handle = Animate.Background(btn, btn.BackgroundColor, hover)
                    .Duration(0.15f).Ease(Easing.EaseOutCubic).Start();
            });
            btn.On<PointerLeaveEvent>(_ =>
            {
                handle.Cancel();
                handle = Animate.Background(btn, btn.BackgroundColor, normal)
                    .Duration(0.25f).Ease(Easing.EaseOutCubic).Start();
            });

            children[i] = btn;
        }

        return row.Children(children);
    }

    private static Element Section(string title, Element content)
    {
        return Flex.Column(gap: 8).FlexShrink(0).Children(
            Text.New(title).FontSize(14f).FontWeight(FontWeight.SemiBold)
                .TextColor(new Color(156, 163, 175)),
            content
        );
    }
}

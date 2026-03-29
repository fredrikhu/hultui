using HultUI;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

using var app = new Application("Z-Index Demo", 650, 700);
app.Theme = Theme.Dark;
app.SetRoot(new ZIndexDemo().Mount());
app.Run();

// ── Demo component ──

partial class ZIndexDemo : Component
{
    [Reactive] public partial int FrontCardIndex { get; set; }

    public override Element Build()
    {
        return ScrollView.New().Children(
            Flex.Column(gap: 32).Padding(24).FlexShrink(0).Children(

                // Header
                Text.New("Z-Index Showcase").FontSize(22f).FontWeight(FontWeight.Bold),

                // ── 1. Static stacking order ──
                Section("Static Stacking Order",
                    Text.New("Three overlapping cards with ZIndex -1, 0, 1. Higher ZIndex renders on top.")
                        .FontSize(12f).TextColor(new Color(156, 163, 175)),
                    StaticStackingDemo()
                ),

                // ── 2. Click to bring to front ──
                Section("Click to Bring to Front",
                    Text.New("Click a card to give it the highest ZIndex.")
                        .FontSize(12f).TextColor(new Color(156, 163, 175)),
                    BringToFrontDemo()
                ),

                // ── 3. Badge / overlay pattern ──
                Section("Badge Overlay",
                    Text.New("A notification badge (ZIndex 1) floats above the button.")
                        .FontSize(12f).TextColor(new Color(156, 163, 175)),
                    BadgeDemo()
                ),

                // ── 4. Layered panels ──
                Section("Layered Panels",
                    Text.New("Panels at ZIndex 0, 1, 2 overlap with increasing offsets.")
                        .FontSize(12f).TextColor(new Color(156, 163, 175)),
                    LayeredPanelsDemo()
                )
            )
        );
    }

    // ── 1. Static stacking: three overlapping colored cards ──
    private static Element StaticStackingDemo()
    {
        var container = Box.New().Height(140);

        var colors = new[]
        {
            (new Color(239, 68, 68), "ZIndex -1", -1),   // red, back
            (new Color(59, 130, 246), "ZIndex 0", 0),     // blue, middle
            (new Color(16, 185, 129), "ZIndex 1", 1),     // green, front
        };

        var children = new Element[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            var (color, label, z) = colors[i];
            var card = Box.New()
                .Width(160).Height(100)
                .Background(color).CornerRadius(8f)
                .Children(
                    Text.New(label).FontSize(13f).FontWeight(FontWeight.SemiBold)
                        .Padding(new Thickness(8f))
                );
            card.Position.Type = PositionType.Absolute;
            card.Position.Top = Dimension.Px(i * 20);
            card.Position.Left = Dimension.Px(i * 60);
            card.Position.ZIndex = z;
            children[i] = card;
        }

        return container.Children(children);
    }

    // ── 2. Click to bring a card to front ──
    private Element BringToFrontDemo()
    {
        var container = Box.New().Height(140);

        var configs = new[]
        {
            (new Color(168, 85, 247), "Purple"),
            (new Color(245, 158, 11), "Amber"),
            (new Color(236, 72, 153), "Pink"),
        };

        var front = FrontCardIndex;
        var children = new Element[configs.Length];
        for (int i = 0; i < configs.Length; i++)
        {
            var (color, label) = configs[i];
            bool isFront = i == front;
            var card = Box.New()
                .Width(180).Height(100)
                .Background(isFront ? color : Darken(color, 0.4f))
                .CornerRadius(8f)
                .Children(
                    Flex.Column(gap: 4).Padding(10).Children(
                        Text.New(label).FontSize(14f).FontWeight(FontWeight.SemiBold),
                        Text.New(isFront ? "Front" : "Click me").FontSize(11f)
                            .TextColor(new Color(200, 200, 200))
                    )
                );
            card.Position.Type = PositionType.Absolute;
            card.Position.Top = Dimension.Px(i * 20);
            card.Position.Left = Dimension.Px(i * 70);
            card.Position.ZIndex = isFront ? 10 : i;
            card.Inherited.Cursor = CursorType.Pointer;

            int capturedIndex = i;
            card.On<ClickEvent>(_ => FrontCardIndex = capturedIndex);

            children[i] = card;
        }

        return container.Children(children);
    }

    // ── 3. Badge floating over a button ──
    private static Element BadgeDemo()
    {
        var container = Box.New().Width(200).Height(50);

        var button = Box.New()
            .Width(160).Height(44)
            .Background(new Color(99, 102, 241)).CornerRadius(8f)
            .Children(
                Text.New("Notifications").FontSize(14f).FontWeight(FontWeight.SemiBold)
                    .Padding(new Thickness(12f, 16f, 12f, 16f))
            );
        button.Position.Type = PositionType.Absolute;
        button.Position.Top = Dimension.Px(3);
        button.Position.Left = Dimension.Px(0);
        button.Position.ZIndex = 0;

        var badge = Box.New()
            .Width(28).Height(28)
            .Background(new Color(239, 68, 68)).CornerRadius(14f)
            .Children(
                Text.New("3").FontSize(12f).FontWeight(FontWeight.Bold)
                    .Padding(new Thickness(5f, 8f, 5f, 8f))
            );
        badge.Position.Type = PositionType.Absolute;
        badge.Position.Top = Dimension.Px(0);
        badge.Position.Left = Dimension.Px(146);
        badge.Position.ZIndex = 1;

        return container.Children(button, badge);
    }

    // ── 4. Layered translucent panels ──
    private static Element LayeredPanelsDemo()
    {
        var container = Box.New().Height(160);

        var layers = new[]
        {
            (new Color(239, 68, 68),  "Layer 0 (back)",   0, 0,  0),
            (new Color(59, 130, 246), "Layer 1 (middle)",  1, 30, 15),
            (new Color(16, 185, 129), "Layer 2 (front)",   2, 60, 30),
        };

        var children = new Element[layers.Length];
        for (int i = 0; i < layers.Length; i++)
        {
            var (color, label, z, offsetX, offsetY) = layers[i];
            var panel = Box.New()
                .Width(220).Height(100)
                .Background(color).CornerRadius(12f)
                .Opacity(0.85f)
                .Children(
                    Text.New(label).FontSize(13f).FontWeight(FontWeight.SemiBold)
                        .Padding(new Thickness(12f))
                );
            panel.Position.Type = PositionType.Absolute;
            panel.Position.Top = Dimension.Px(offsetY);
            panel.Position.Left = Dimension.Px(offsetX);
            panel.Position.ZIndex = z;
            children[i] = panel;
        }

        return container.Children(children);
    }

    // ── Helpers ──

    private static Color Darken(Color c, float amount)
    {
        float factor = 1f - amount;
        return new Color(
            (byte)(c.R * factor),
            (byte)(c.G * factor),
            (byte)(c.B * factor),
            c.A);
    }

    private static Element Section(string title, params Element[] content)
    {
        var children = new Element[content.Length + 1];
        children[0] = Text.New(title).FontSize(15f).FontWeight(FontWeight.SemiBold);
        for (int i = 0; i < content.Length; i++)
            children[i + 1] = content[i];
        return Flex.Column(gap: 8).FlexShrink(0).Children(children);
    }
}

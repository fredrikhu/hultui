using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

// Build a UI tree with nested colored boxes and text
var root = Flex.Column(gap: 16).Padding(24).Children(

    // Header row (FlexShrink=0 so it keeps its height when window is small)
    Flex.Row(gap: 12).Height(60).FlexShrink(0).AlignItems(AlignItems.Center).Children(
        Box.New().FlexGrow(1).Padding(new Thickness(6, 16, 6, 16))
            .Background(Color.FromHex("#4F46E5")).CornerRadius(8f)
            .TextColor(Color.FromHex("#FFFFFF")).FontSize(24f).FontWeight(FontWeight.Bold)
            .Children(Text.New("HultUI")),
        Box.New().Width(120).Padding(12)
            .Background(Color.FromHex("#7C3AED")).CornerRadius(8f)
            .TextColor(Color.FromHex("#FFFFFF"))
            .Children(Text.New("Docs")),
        Box.New().Width(80).Padding(12)
            .Background(Color.FromHex("#EC4899")).CornerRadius(8f)
            .TextColor(Color.FromHex("#FFFFFF"))
            .Children(Text.New("About"))
    ),

    // Main content area
    Flex.Row(gap: 16).FlexGrow(1).Children(

        // Sidebar
        Flex.Column(gap: 8).Width(200).Children(
            Box.New().Height(40).Padding(10).Background(Color.FromHex("#1E293B")).CornerRadius(6f)
                .TextColor(Color.FromHex("#E2E8F0")).FontSize(14f)
                .Children(Text.New("Dashboard")),
            Box.New().Height(40).Padding(10).Background(Color.FromHex("#334155")).CornerRadius(6f)
                .TextColor(Color.FromHex("#94A3B8")).FontSize(14f)
                .Children(Text.New("Components")),
            Box.New().Height(40).Padding(10).Background(Color.FromHex("#475569")).CornerRadius(6f)
                .TextColor(Color.FromHex("#94A3B8")).FontSize(14f)
                .Children(Text.New("Settings")),
            Box.New().FlexGrow(1).Background(Color.FromHex("#0F172A")).CornerRadius(6f)
        ),

        // Content grid
        Flex.Row(gap: 12).FlexWrap(FlexWrap.Wrap).FlexGrow(1)
            .AlignContent(AlignContent.FlexStart)
            .Children(
                MakeCard("#06B6D4", "Cyan"),
                MakeCard("#14B8A6", "Teal"),
                MakeCard("#22C55E", "Green"),
                MakeCard("#EAB308", "Yellow"),
                MakeCard("#F97316", "Orange"),
                MakeCard("#EF4444", "Red")
            )
    ),

    // Footer (FlexShrink=0 so it keeps its height when window is small)
    Box.New().Height(40).FlexShrink(0).Padding(10).Background(Color.FromHex("#1E293B")).CornerRadius(8f)
        .TextColor(Color.FromHex("#64748B")).FontSize(12f)
        .Children(Text.New("Built with HultUI — a cross-platform C# UI framework"))
);

using var app = new Application("HultUI - Hello World", 900, 650);
app.SetRoot(root);
app.Run();

static HultUI.Core.Elements.BoxElement MakeCard(string hex, string label) =>
    Flex.Column(gap: 0)
        .Width(160).Height(120)
        .Background(Color.FromHex(hex)).CornerRadius(12f)
        .Padding(16)
        .JustifyContent(JustifyContent.FlexEnd)
        .TextColor(Color.FromHex("#FFFFFF")).FontSize(18f).FontWeight(FontWeight.Bold)
        .Children(Text.New(label));

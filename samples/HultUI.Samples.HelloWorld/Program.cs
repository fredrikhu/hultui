using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;

// Build a UI tree with nested colored boxes
var root = Flex.Column(gap: 16).Padding(24).Children(

    // Header row
    Flex.Row(gap: 12).Height(60).Children(
        Box.New().FlexGrow(1).Height(60)
            .Background(Color.FromHex("#4F46E5")).CornerRadius(8f),
        Box.New().Width(120).Height(60)
            .Background(Color.FromHex("#7C3AED")).CornerRadius(8f),
        Box.New().Width(80).Height(60)
            .Background(Color.FromHex("#EC4899")).CornerRadius(8f)
    ),

    // Main content area
    Flex.Row(gap: 16).FlexGrow(1).Children(

        // Sidebar
        Flex.Column(gap: 8).Width(200).Children(
            Box.New().Height(40).Background(Color.FromHex("#1E293B")).CornerRadius(6f),
            Box.New().Height(40).Background(Color.FromHex("#334155")).CornerRadius(6f),
            Box.New().Height(40).Background(Color.FromHex("#475569")).CornerRadius(6f),
            Box.New().FlexGrow(1).Background(Color.FromHex("#0F172A")).CornerRadius(6f)
        ),

        // Content grid
        Flex.Row(gap: 12).FlexWrap(FlexWrap.Wrap).FlexGrow(1)
            .AlignContent(AlignContent.FlexStart)
            .Children(
                MakeCard("#06B6D4"),
                MakeCard("#14B8A6"),
                MakeCard("#22C55E"),
                MakeCard("#EAB308"),
                MakeCard("#F97316"),
                MakeCard("#EF4444")
            )
    ),

    // Footer
    Box.New().Height(40).Background(Color.FromHex("#1E293B")).CornerRadius(8f)
);

using var app = new Application("HultUI - Hello World", 900, 650);
app.SetRoot(root);
app.Run();

static HultUI.Core.Elements.BoxElement MakeCard(string hex) =>
    Box.New().Width(160).Height(120)
        .Background(Color.FromHex(hex)).CornerRadius(12f);

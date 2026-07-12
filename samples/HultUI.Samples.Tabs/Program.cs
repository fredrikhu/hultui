using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

using var app = new Application("HultUI - Tabs", 860, 780);

var heading = Color.FromHex("#1E293B");
var muted = Color.FromHex("#64748B");

var root = ScrollView.New().Background(Color.FromHex("#EEF2F7")).Children(
    Flex.Column(gap: 24).Padding(32).Children(

        Text.New("Tabs").FontSize(28f).FontWeight(FontWeight.Bold).TextColor(heading).FlexShrink(0),

        Demo("UNDERLINE", TabStyle.Underline, TabPlacement.Top),
        Demo("PILL", TabStyle.Pill, TabPlacement.Top),
        Demo("ENCLOSED", TabStyle.Enclosed, TabPlacement.Top),
        Demo("LEFT PLACEMENT", TabStyle.Underline, TabPlacement.Left),
        Demo("BOTTOM PLACEMENT", TabStyle.Pill, TabPlacement.Bottom),

        // Rich headers: icon-free but with a count badge and a closable tab.
        Section("RICH HEADERS",
            Tabs.New().Style(TabStyle.Underline).Height(150).FlexShrink(0)
                .Tab("Changes", () => Panel("Working-tree changes"), badge: "12")
                .Tab("History", () => Panel("Commit history"))
                .Tab("README.md", () => Panel("File preview"), onClose: () => Console.WriteLine("close README")))
    )
);

app.SetRoot(root);
app.Run();
return;

Element Section(string title, Element body) =>
    Flex.Column(gap: 8).FlexShrink(0).Children(
        Text.New(title).FontSize(12f).FontWeight(FontWeight.SemiBold).TextColor(Color.FromHex("#4F46E5")).LetterSpacing(1.5f),
        body);

Element Demo(string title, TabStyle style, TabPlacement placement) =>
    Section(title,
        Tabs.New().Style(style).Placement(placement).Height(150).FlexShrink(0)
            .Tab("Files", () => Panel("Files panel"))
            .Tab("History", () => Panel("History panel"))
            .Tab("Settings", () => Panel("Settings panel")));

Element Panel(string text) =>
    Flex.Column(gap: 6).Padding(18).FlexGrow(1).Children(
        Text.New(text).FontSize(15f).FontWeight(FontWeight.SemiBold).TextColor(heading),
        Text.New("Tab content is built lazily when the tab is selected.").FontSize(12f).TextColor(muted));

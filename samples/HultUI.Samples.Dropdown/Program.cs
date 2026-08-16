using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

using var app = new Application("HultUI - Dropdown", 560, 520);

var heading = Color.FromHex("#1E293B");
var muted = Color.FromHex("#64748B");

string[] fruits = ["Apple", "Banana", "Cherry", "Dragonfruit", "Elderberry"];
string[] themes = ["System", "Light", "Dark"];

var picked = Text.New("Nothing picked yet.").FontSize(12f).TextColor(muted);

var root = Flex.Column(gap: 24).Padding(32).Background(Color.FromHex("#EEF2F7")).Children(

    Text.New("Dropdown").FontSize(28f).FontWeight(FontWeight.Bold).TextColor(heading).FlexShrink(0),

    Section("BASIC",
        Flex.Column(gap: 10).Children(
            Dropdown.New(fruits).Changed(i => Console.WriteLine($"fruit: {fruits[i]}")),
            Text.New("Opens on click, Enter, Space or Down. Escape closes without changing.")
                .FontSize(12f).TextColor(muted))),

    Section("PRESELECTED",
        Flex.Row(gap: 12).Children(
            Dropdown.New(themes, selectedIndex: 0).Changed(i =>
                picked.Content = $"Theme choice: {themes[i]}"),
            picked)),

    Section("KEYBOARD",
        Flex.Column(gap: 10).Children(
            Flex.Row(gap: 12).Children(
                Dropdown.New(fruits, selectedIndex: 2),
                Dropdown.New(themes, selectedIndex: 1)),
            Text.New("Tab moves between them; the list opens with the current value highlighted.")
                .FontSize(12f).TextColor(muted)))
);

app.SetRoot(root);
app.Run();
return;

Element Section(string title, Element body) =>
    Flex.Column(gap: 8).FlexShrink(0).Children(
        Text.New(title).FontSize(12f).FontWeight(FontWeight.SemiBold)
            .TextColor(Color.FromHex("#4F46E5")).LetterSpacing(1.5f),
        body);

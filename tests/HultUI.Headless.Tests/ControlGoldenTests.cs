using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// Golden images of the built-in controls: what they look like, pinned. A change to a theme,
/// a control's drawing, the text renderer or layout that moves a pixel fails here, on Windows
/// and Linux alike — the frames are drawn in the bundled fonts as outlines
/// (<see cref="HeadlessOptions.Golden"/>), which come out the same on both.
/// Baselines are in <c>Golden/</c>; <c>HULTUI_UPDATE_GOLDENS=1</c> rewrites them.
/// </summary>
public sealed class ControlGoldenTests
{
    private static HeadlessSession Session(int width, int height, Theme? theme = null)
    {
        var session = HeadlessSession.Create(width, height, options: HeadlessOptions.Golden());
        if (theme is not null) session.Application.Theme = theme;
        return session;
    }

    private static Element Controls() =>
        Flex.Column(gap: 14).Padding(new Thickness(20)).Children(
            Flex.Row(gap: 8).Children(
                Button.New("Primary"),
                Button.New("Secondary", ButtonVariant.Secondary),
                Button.New("Danger", ButtonVariant.Danger),
                Button.New("Ghost", ButtonVariant.Ghost),
                Button.New("Disabled").Enabled(false)),
            Flex.Row(gap: 16).Children(
                Checkbox.New(true, "Checked"),
                Checkbox.New(false, "Unchecked"),
                RadioButton.New("g", true, "Picked"),
                RadioButton.New("g", false, "Other")),
            Flex.Row(gap: 16).Children(
                ToggleSwitch.New(true, "On"),
                ToggleSwitch.New(false, "Off")),
            Slider.New(40).Width(240),
            ProgressBar.New(0.6).Width(240),
            TextInput.New("Some text").Width(240),
            Dropdown.New(["Alpha", "Beta", "Gamma"], 1).Width(240));

    [Fact]
    public void Controls_Light()
    {
        using var session = Session(560, 360, Theme.Light);
        session.Window.SetRoot(Controls());
        session.PumpFrames(3);
        GoldenImage.Match(session.Window, "controls-light");
    }

    [Fact]
    public void Controls_Dark()
    {
        using var session = Session(560, 360, Theme.Dark);
        session.Window.SetRoot(Controls());
        session.PumpFrames(3);
        GoldenImage.Match(session.Window, "controls-dark");
    }

    [Fact]
    public void FocusRing_OnAButtonReachedByTab()
    {
        using var session = Session(240, 80);
        session.Window.SetRoot(Flex.Row(gap: 8).Padding(new Thickness(20)).Children(
            Button.New("First"), Button.New("Second", ButtonVariant.Secondary)));
        session.PumpFrame();
        session.Window.PressKey(KeyCode.Tab);
        session.Window.PressKey(KeyCode.Tab);
        session.PumpFrames(3);
        GoldenImage.Match(session.Window, "focus-ring");
    }

    [Fact]
    public void Text_WeightsFamiliesSizesAndWrapping()
    {
        using var session = Session(420, 340);
        session.Window.SetRoot(Flex.Column(gap: 6).Padding(new Thickness(16)).Children(
            Text.New("Regular 14 — Hamburgefonstiv 0123456789"),
            Text.New("Bold 14 — Hamburgefonstiv").FontWeight(FontWeight.Bold),
            Text.New("Mono 13 — fn main() { 0O1lI }").FontFamily(FontFamilies.Monospace).FontSize(13),
            Text.New("Italic 14 — the slant").Italic(),
            Text.New("Bold italic").FontWeight(FontWeight.Bold).Italic(),
            Text.New("Underlined, struck").Underline().Strikethrough(),
            Text.New("Small 11").FontSize(11),
            Text.New("Large 24").FontSize(24),
            Text.New("Coloured").TextColor(new Color(204, 51, 26)),
            Text.New("Åäö ÆØ fiffl “quotes” ½ — a paragraph long enough to wrap onto a second line in its column.")
                .Width(300),
            Text.New("A named family that is not installed still draws in DejaVu").FontFamily("Segoe UI")));
        session.PumpFrames(2);
        GoldenImage.Match(session.Window, "text");
    }

    private sealed record Entry(string Name, long Size, bool Folder);

    private sealed class TablePage : Component
    {
        private readonly TableView<Entry> _table;
        private readonly Entry[] _entries =
        [
            new("src", 0, true), new("docs", 0, true),
            new("README.md", 2048, false), new("build.sh", 312, false), new("LICENSE", 1071, false),
        ];

        public TablePage()
        {
            _table = new TableView<Entry>(
            [
                new TableColumn<Entry> { Id = "name", Header = "Name", Fill = 1, Text = e => e.Name,
                    Compare = (a, b) => string.CompareOrdinal(a.Name, b.Name) },
                new TableColumn<Entry> { Id = "size", Header = "Size", Width = 90, Align = ColumnAlign.End,
                    Text = e => e.Folder ? "" : $"{e.Size} B", Compare = (a, b) => a.Size.CompareTo(b.Size) },
                new TableColumn<Entry> { Id = "kind", Header = "Kind", Width = 70, Text = e => e.Folder ? "Folder" : "File" },
            ], MarkDirty)
            {
                Group = e => e.Folder ? 0 : 1,
            };
        }

        public override Element Build() =>
            Flex.Column(gap: 0).Height(180).Children(_table.Header(), _table.Body(_entries).FlexGrow(1));
    }

    [Fact]
    public void MenuBar_Expander_AndTable()
    {
        using var session = Session(520, 400);
        var bar = new MenuBar(
        [
            new("&File", [new ContextMenuItem { Label = "Open" }]),
            new("&Edit", [new ContextMenuItem { Label = "Undo" }]),
            new("&View", [new ContextMenuItem { Label = "Zoom" }]),
        ]);
        var open = Expander.New("Open section", Text.New("The body of an open expander."));
        open.IsExpanded = true;
        session.Window.SetRoot(Flex.Column(gap: 0).Children(
            bar.Mount(),
            Flex.Column(gap: 10).Padding(new Thickness(16)).Children(
                open,
                Expander.New("Closed section", Text.New("Hidden.")),
                new TablePage().Mount())));
        session.PumpFrames(3);
        session.PumpFrame(TimeSpan.FromMilliseconds(500));
        GoldenImage.Match(session.Window, "menubar-expander-table");
    }
}

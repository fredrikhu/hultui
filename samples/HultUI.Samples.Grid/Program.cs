using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

// Three demos showcasing what the grid layout adds over flex:
//   1. Property inspector — auto-sized labels, value column fills.
//   2. Settings dialog   — three-column label/control/hint with row-aligned cells.
//   3. Tabular data      — header + data rows where columns line up perfectly.

var bg = Color.FromHex("#F8FAFC");
var panelBg = Color.FromHex("#FFFFFF");
var border = Color.FromHex("#E2E8F0");
var labelColor = Color.FromHex("#475569");
var hintColor = Color.FromHex("#94A3B8");
var headerColor = Color.FromHex("#1E293B");

var root = Flex.Column().Background(bg).Children(

    // Header strip stays pinned; panels scroll below it
    Flex.Column(gap: 4).Padding(new Thickness(24, 24, 8, 24)).FlexShrink(0).Children(
        Text.New("HultUI Grid Layout").FontSize(24).FontWeight(FontWeight.Bold)
            .TextColor(headerColor),
        Text.New("Three layouts that flex can't do cleanly").FontSize(13)
            .TextColor(hintColor)
    ),

    ScrollView.New().FlexGrow(1).Padding(new Thickness(0, 24, 24, 24)).Children(
        Flex.Column(gap: 24).Children(
            BuildPropertyInspector(),
            BuildSettingsDialog(),
            BuildTabularData()
        )
    )
);

using var app = new Application("HultUI Grid Demo", 820, 720);
app.SetRoot(root);
app.Run();
return;

// ──── Demo 1: Property inspector ────

Element BuildPropertyInspector()
{
    return Panel("Property Inspector",
        Grid.Of("auto 1fr", gap: 8).Children(

            Label("Name", 0, 0),
            Input("Untitled object", 0, 1),

            Label("Position X", 1, 0),
            Input("0.00", 1, 1),

            Label("Position Y", 2, 0),
            Input("0.00", 2, 1),

            Label("Visible", 3, 0),
            Checkbox.New(true).GridCell(3, 1).JustifySelf(JustifySelf.Start),

            Label("Description", 4, 0),
            TextArea.New().GridCell(4, 1).Height(80)
                .Background(panelBg).BorderColor(border).BorderWidth(1f).CornerRadius(4f)
        )
    );
}

// ──── Demo 2: Settings dialog (label / control / hint) ────

Element BuildSettingsDialog()
{
    return Panel("Editor Settings",
        Grid.Of("auto 1fr auto", gap: 12).Children(

            Label("Theme", 0, 0).AlignSelf(AlignSelf.Center),
            Input("Dark", 0, 1),
            Hint("Restart required", 0, 2),

            Label("Tab size", 1, 0).AlignSelf(AlignSelf.Center),
            Input("4", 1, 1),
            Hint("Spaces per indent", 1, 2),

            Label("Word wrap", 2, 0).AlignSelf(AlignSelf.Center),
            Checkbox.New().GridCell(2, 1).JustifySelf(JustifySelf.Start)
                .AlignSelf(AlignSelf.Center),
            Hint("Soft-wrap long lines", 2, 2),

            Label("Font family", 3, 0).AlignSelf(AlignSelf.Center),
            Input("JetBrains Mono", 3, 1),
            Hint("Monospaced recommended", 3, 2),

            Label("Auto-save delay", 4, 0).AlignSelf(AlignSelf.Center),
            Input("500", 4, 1),
            Hint("Milliseconds", 4, 2)
        )
    );
}

// ──── Demo 3: Tabular data ────

Element BuildTabularData()
{
    var rows = new (string Id, string Name, string Status, string Updated)[]
    {
        ("U-001", "Ada Lovelace",         "Active",   "2026-04-12"),
        ("U-002", "Grace Hopper",         "Active",   "2026-04-09"),
        ("U-003", "Linus Torvalds",       "Pending",  "2026-04-15"),
        ("U-004", "Margaret Hamilton",    "Active",   "2026-04-11"),
        ("U-005", "Donald Knuth",         "Inactive", "2026-03-28"),
        ("U-006", "Barbara Liskov",       "Active",   "2026-04-14"),
        ("U-007", "Edsger Dijkstra",      "Active",   "2026-04-08"),
    };

    var children = new List<Element>(capacity: (rows.Length + 1) * 4);

    // Header row
    children.Add(HeaderCell("ID",      0, 0));
    children.Add(HeaderCell("Name",    0, 1));
    children.Add(HeaderCell("Status",  0, 2));
    children.Add(HeaderCell("Updated", 0, 3));

    // Data rows
    for (int i = 0; i < rows.Length; i++)
    {
        int row = i + 1;
        var rowBg = i % 2 == 0 ? Color.Transparent : Color.FromHex("#F8FAFC");

        children.Add(DataCell(rows[i].Id,      row, 0, rowBg, mono: true));
        children.Add(DataCell(rows[i].Name,    row, 1, rowBg));
        children.Add(StatusCell(rows[i].Status, row, 2, rowBg));
        children.Add(DataCell(rows[i].Updated, row, 3, rowBg, mono: true,
            justify: JustifySelf.End));
    }

    var table = Grid.Of("80px 1fr 100px 110px").Children(children.ToArray());

    return Panel("Tabular Data", table);
}

// ──── Helpers ────

Element Panel(string title, Element body) =>
    // FlexShrink(0): panels keep their natural height; the outer ScrollView
    // handles the case where the window can't fit everything. Without this,
    // the flex parent would shrink each panel and grid auto-rows (which match
    // CSS Grid by NOT shrinking) would overflow the panel border.
    Flex.Column(gap: 12).Padding(20).FlexShrink(0).Background(panelBg)
        .BorderColor(border).BorderWidth(1f).CornerRadius(8f)
        .Children(
            Text.New(title).FontSize(16).FontWeight(FontWeight.SemiBold)
                .TextColor(headerColor),
            body
        );

Element Label(string text, int row, int col) =>
    Text.New(text).GridCell(row, col).FontSize(13).TextColor(labelColor)
        .AlignSelf(AlignSelf.Center);

TextInputElement Input(string placeholder, int row, int col) =>
    TextInput.New().GridCell(row, col)
        .PlaceholderText(placeholder)
        .Height(32)
        .Background(panelBg).BorderColor(border).BorderWidth(1f).CornerRadius(4f)
        .Padding(new Thickness(6, 10));

Element Hint(string text, int row, int col) =>
    Text.New(text).GridCell(row, col).FontSize(11).TextColor(hintColor)
        .AlignSelf(AlignSelf.Center);

Element HeaderCell(string text, int row, int col) =>
    Box.New().GridCell(row, col)
        .Padding(new Thickness(8, 12))
        .Background(Color.FromHex("#F1F5F9"))
        .Children(Text.New(text).FontSize(12).FontWeight(FontWeight.Bold)
            .TextColor(headerColor));

Element DataCell(string text, int row, int col, Color rowBg, bool mono = false,
    JustifySelf justify = JustifySelf.Start)
{
    var label = Text.New(text).FontSize(13).TextColor(headerColor);
    if (mono) label.FontFamily("monospace");

    return Box.New().GridCell(row, col)
        .Padding(new Thickness(8, 12))
        .Background(rowBg)
        .JustifyContent(justify == JustifySelf.End
            ? JustifyContent.FlexEnd : JustifyContent.FlexStart)
        .Children(label);
}

Element StatusCell(string status, int row, int col, Color rowBg)
{
    var (fg, badgeBg) = status switch
    {
        "Active" => (Color.FromHex("#15803D"), Color.FromHex("#DCFCE7")),
        "Pending" => (Color.FromHex("#A16207"), Color.FromHex("#FEF3C7")),
        _ => (Color.FromHex("#475569"), Color.FromHex("#E2E8F0")),
    };

    return Box.New().GridCell(row, col)
        .Padding(new Thickness(8, 12))
        .Background(rowBg)
        .JustifyContent(JustifyContent.FlexStart)
        .AlignItems(AlignItems.Center)
        .Children(
            Box.New().Padding(new Thickness(2, 8))
                .Background(badgeBg).CornerRadius(10f)
                .Children(Text.New(status).FontSize(11)
                    .FontWeight(FontWeight.SemiBold).TextColor(fg))
        );
}

using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.WinForms;
using Color = HultUI.Core.Primitives.Color;
// `Text` is Control.Text inside a Form, so the builder needs a name of its own here.
using UiText = HultUI.Core.Builders.Text;

namespace HultUI.Samples.WinFormsHost;

/// <summary>
/// The migration shape: an existing WinForms form where one region has been replaced
/// with HultUI, and the rest is still WinForms. Two <see cref="HultUIControl"/>s share
/// one application and one UI thread, beside a <c>DataGridView</c> and a menu strip
/// that keep behaving exactly as they did.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // First statement, before anything else: the graphics driver loads on a worker
        // while the form is being built, instead of costing the first control ~140ms.
        HultUIApplicationHost.BeginGraphicsPreload();

        // PerMonitorV2 (set in the csproj) matters: HultUI takes its scale from the
        // control's DeviceDpi, so a DPI-unaware host would render the tree at 1x on a
        // scaled display while the WinForms controls beside it scaled properly.
        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private int _clicks;
    private readonly Label _status = new() { Dock = DockStyle.Bottom, AutoSize = false, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };

    public MainForm()
    {
        Text = "HultUI inside WinForms";
        Width = 1000;
        Height = 640;

        var menu = new MenuStrip();
        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.Add("&Dark theme", null, (_, _) => HultUIApplicationHost.Theme = Theme.Dark);
        view.DropDownItems.Add("&Light theme", null, (_, _) => HultUIApplicationHost.Theme = Theme.Light);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add("Zoom &in", null, (_, _) => Zoom(+0.1f));
        view.DropDownItems.Add("Zoom &out", null, (_, _) => Zoom(-0.1f));
        menu.Items.Add(view);
        MainMenuStrip = menu;

        // A plain WinForms control, to show the two coexisting rather than one taking over.
        var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false };
        grid.Columns.Add("part", "Part");
        grid.Columns.Add("owner", "Owned by");
        grid.Rows.Add("Menu, grid, status bar", "WinForms");
        grid.Rows.Add("The first two tabs on the right", "HultUI");
        grid.Rows.Add("Focus, tab order, the message loop", "WinForms");
        grid.Rows.Add("Layout, painting, hit-testing inside a tab", "HultUI");

        Counter = new HultUIControl { Name = "counter", Dock = DockStyle.Fill, RootFactory = BuildCounter };
        Counter.Mounted += (_, _) => UpdateStatus();

        Palette = new HultUIControl { Name = "palette", Dock = DockStyle.Fill, RootFactory = BuildPalette };

        // Tabs are the migration shape: a page at a time moves to HultUI while the rest
        // stays as it was. The palette page also proves the lazy case — a TabControl does
        // not create a page's handle until the page is first shown, so that control mounts
        // long after the first one.
        var tabs = new TabControl { Name = "tabs", Dock = DockStyle.Fill };
        var migrated = new TabPage("Migrated") { Name = "tab-migrated" };
        migrated.Controls.Add(Counter);
        var paletteTab = new TabPage("Also migrated") { Name = "tab-palette" };
        paletteTab.Controls.Add(Palette);
        DragDrop = new HultUIControl { Name = "dragdrop", Dock = DockStyle.Fill, RootFactory = BuildDragDrop };
        var dragTab = new TabPage("Drag and drop") { Name = "tab-dragdrop" };
        dragTab.Controls.Add(DragDrop);

        var legacy = new TabPage("Not yet") { Name = "tab-legacy", Padding = new Padding(12) };
        legacy.Controls.Add(new CheckBox
        {
            Name = "legacy-check",
            Text = "An ordinary WinForms page, untouched",
            AutoSize = true,
            Location = new System.Drawing.Point(12, 12),
        });
        tabs.TabPages.AddRange([migrated, paletteTab, dragTab, legacy]);

        _split = new SplitContainer { Dock = DockStyle.Fill };
        var split = _split;
        split.Panel1.Controls.Add(grid);
        split.Panel2.Controls.Add(tabs);

        Controls.Add(split);
        Controls.Add(_status);
        Controls.Add(menu);
    }

    private readonly SplitContainer _split;

    private HultUIControl Counter { get; }
    private HultUIControl Palette { get; }
    private HultUIControl DragDrop { get; }

    /// <summary>
    /// The splitter position is set here, not in the constructor: a SplitContainer clamps
    /// SplitterDistance to the size it has at the time, and before layout that is not the
    /// size it ends up with.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _split.SplitterDistance = (int)(_split.Width * 0.42f);
    }

    private void Zoom(float by)
    {
        Counter.UserScale = Math.Clamp(Counter.UserScale + by, 0.5f, 3f);
        // Only if its page has been shown: an unmounted control has no window to scale.
        if (Palette.Window is not null) Palette.UserScale = Counter.UserScale;
        UpdateStatus();
    }

    private void UpdateStatus()
        => _status.Text = $"HultUI application: {(HultUIApplicationHost.Application is null ? "not started" : "running")}"
            + $"   ·   control DPI {Counter.DeviceDpi}   ·   zoom {Counter.UserScale:0.0}x"
            + "   ·   Tab moves in, through and out of the HultUI panels";

    /// <summary>
    /// A factory rather than a prebuilt tree: WinForms can recreate a control's handle,
    /// and a new handle means a new GL context and a new framework window to mount into.
    /// </summary>
    private Element BuildCounter()
    {
        var label = UiText.New("clicked 0 times").FontSize(15f).AutomationId("click-count");

        var button = Box.New().Padding(new Thickness(16, 10)).CornerRadius(8f)
            .Background(Color.FromHex("#4F46E5")).TextColor(Color.White)
            .Cursor(CursorType.Pointer)
            .Children(UiText.New("Click me").FontWeight(FontWeight.Bold))
            .AutomationId("click-me").AccessibleName("Click me").AccessibleRole(AccessibilityRole.Button);

        button.OnPointerEnter(_ => button.BackgroundColor = Color.FromHex("#4338CA"));
        button.OnPointerLeave(_ => button.BackgroundColor = Color.FromHex("#4F46E5"));
        button.OnClick(_ =>
        {
            _clicks++;
            label.Content = $"clicked {_clicks} time{(_clicks == 1 ? "" : "s")}";
        });

        // Tab reaches these from the WinForms controls, moves between them, and leaves
        // the control again once the tree is exhausted.
        var first = TextInput.New().AutomationId("field-one").AccessibleName("First field");
        var second = TextInput.New().AutomationId("field-two").AccessibleName("Second field");

        return Flex.Column(gap: 14).Padding(20).Children(
            UiText.New("Pointer and keyboard").FontSize(19f).FontWeight(FontWeight.Bold),
            UiText.New("Tab in from the grid, through these, and out to the tab strip.")
                .FontSize(13f),
            Flex.Row(gap: 14).AlignItems(AlignItems.Center).Children(button, label),
            Flex.Row(gap: 10).AlignItems(AlignItems.Center).Children(
                Box.New().Width(150).Children(first),
                Box.New().Width(150).Children(second)));
    }


    /// <summary>
    /// Drops from other applications, and a drag that leaves for them. Both go through
    /// WinForms — AllowDrop and DoDragDrop — rather than through SDL, so a HultUI region
    /// exchanges files with Explorer exactly as the rest of the form does.
    /// </summary>
    private Element BuildDragDrop()
    {
        var dropped = UiText.New("Nothing dropped yet.").FontSize(13f).AutomationId("drop-result");

        var zone = Box.New().Padding(24).CornerRadius(10f)
            .Background(Color.FromHex("#EEF2FF"))
            .BorderWidth(2f).BorderColor(Color.FromHex("#C7D2FE"))
            .AutomationId("drop-zone")
            .AccessibleName("File drop zone")
            .AccessibleRole(AccessibilityRole.Group)
            .Children(UiText.New("Drop files here").FontSize(15f).FontWeight(FontWeight.Bold));

        zone.OnDragEnter(_ => zone.BackgroundColor = Color.FromHex("#C7D2FE"));
        zone.OnDragLeave(_ => zone.BackgroundColor = Color.FromHex("#EEF2FF"));
        zone.OnDragOver(e => e.Accepted = e.DragData.Files.Count > 0);
        zone.OnDrop(e =>
        {
            zone.BackgroundColor = Color.FromHex("#EEF2FF");
            dropped.Content = e.DragData.Files.Count switch
            {
                0 => "Dropped, but with no files.",
                1 => "Dropped: " + Path.GetFileName(e.DragData.Files[0]),
                var n => $"Dropped {n} files, first " + Path.GetFileName(e.DragData.Files[0]),
            };
        });

        // The other direction: a source that hands this application's own file to the
        // desktop. DragFilesOut is modal, which is why it belongs in a DragStart handler
        // — the mouse button is still down and the OS takes over from here.
        var ownPath = Environment.ProcessPath ?? "";
        var source = Box.New().Padding(new Thickness(16, 10)).CornerRadius(8f)
            .Background(Color.FromHex("#0F766E")).TextColor(Color.White)
            .Cursor(CursorType.Move)
            // Required: only a Draggable element becomes a drag source, so without this
            // the press is just a press and DragStart never fires.
            .Draggable()
            .AutomationId("drag-source")
            .AccessibleName("Drag me to Explorer")
            .AccessibleRole(AccessibilityRole.Button)
            .Children(UiText.New("Drag me to Explorer").FontWeight(FontWeight.Bold));

        source.OnDragStart(_ =>
        {
            var window = DragDrop.Window;
            if (window is { CanDragFilesOut: true } && ownPath.Length > 0)
                window.DragFilesOut([ownPath], DragEffect.Copy);
        });

        // Reported rather than assumed: an outbound drag is an OLE operation and the
        // capability says so for itself, so a reader can see whether this build has it.
        var capability = UiText.New("outbound drag: checking…").FontSize(12f).AutomationId("drag-capability");
        DragDrop.Mounted += (_, _) =>
            capability.Content = "outbound drag available: "
                + (DragDrop.Window?.CanDragFilesOut == true ? "yes" : "no");

        return Flex.Column(gap: 16).Padding(20).Children(
            UiText.New("Drag and drop").FontSize(19f).FontWeight(FontWeight.Bold),
            UiText.New("Both directions go through WinForms, not SDL.").FontSize(13f),
            zone,
            dropped,
            source,
            capability);
    }

    /// <summary>A readable spread of hues without pulling in a colour-space helper.</summary>
    private static Color Ramp(int index)
    {
        float t = index / 40f * 6f;
        int sector = (int)t % 6;
        byte rise = (byte)(60 + (t - (int)t) * 195);
        byte fall = (byte)(255 - (t - (int)t) * 195);
        return sector switch
        {
            0 => new Color(255, rise, 60),
            1 => new Color(fall, 255, 60),
            2 => new Color(60, 255, rise),
            3 => new Color(60, fall, 255),
            4 => new Color(rise, 60, 255),
            _ => new Color(255, 60, fall),
        };
    }

    /// <summary>A scrolling list, to show wheel input and layout under a real resize.</summary>
    private static Element BuildPalette()
    {
        // Children() replaces the child list rather than appending, so the rows are
        // collected first and handed over in one call.
        var rows = new Element[40];
        for (int i = 0; i < rows.Length; i++)
        {
            var swatch = Box.New().Width(28).Height(18).CornerRadius(4f).Background(Ramp(i));
            rows[i] = Flex.Row(gap: 10).AlignItems(AlignItems.Center).Children(
                swatch,
                UiText.New($"row {i:00} — scroll me with the wheel").FontSize(13f));
        }

        return Flex.Column(gap: 10).Padding(20).Children(
            UiText.New("Scrolling and resize").FontSize(19f).FontWeight(FontWeight.Bold),
            ScrollView.New().FlexGrow(1).Children(Flex.Column(gap: 6).Children(rows)));
    }
}

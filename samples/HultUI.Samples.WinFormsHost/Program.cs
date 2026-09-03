using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
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
        grid.Rows.Add("The two panels on the right", "HultUI");
        grid.Rows.Add("Focus, tab order, the message loop", "WinForms");
        grid.Rows.Add("Layout, painting, hit-testing inside a panel", "HultUI");

        Counter = new HultUIControl { Name = "counter", Dock = DockStyle.Fill, RootFactory = BuildCounter };
        Counter.Mounted += (_, _) => UpdateStatus();

        Palette = new HultUIControl { Name = "palette", Dock = DockStyle.Fill, RootFactory = BuildPalette };

        _hultUiSide = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        var hultUiSide = _hultUiSide;
        hultUiSide.Panel1.Controls.Add(Counter);
        hultUiSide.Panel2.Controls.Add(Palette);

        _split = new SplitContainer { Dock = DockStyle.Fill };
        var split = _split;
        split.Panel1.Controls.Add(grid);
        split.Panel2.Controls.Add(hultUiSide);

        Controls.Add(split);
        Controls.Add(_status);
        Controls.Add(menu);
    }

    private readonly SplitContainer _split;
    private readonly SplitContainer _hultUiSide;

    private HultUIControl Counter { get; }
    private HultUIControl Palette { get; }

    /// <summary>
    /// Splitter positions are set here, not in the constructor: a SplitContainer clamps
    /// SplitterDistance to the size it has at the time, and before layout that is not
    /// the size it ends up with.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _split.SplitterDistance = (int)(_split.Width * 0.42f);
        _hultUiSide.SplitterDistance = (int)(_hultUiSide.Height * 0.45f);
    }

    private void Zoom(float by)
    {
        Counter.UserScale = Math.Clamp(Counter.UserScale + by, 0.5f, 3f);
        Palette.UserScale = Counter.UserScale;
        UpdateStatus();
    }

    private void UpdateStatus()
        => _status.Text = $"HultUI application: {(HultUIApplicationHost.Application is null ? "not started" : "running")}"
            + $"   ·   control DPI {Counter.DeviceDpi}   ·   zoom {Counter.UserScale:0.0}x"
            + "   ·   pointer input works; keyboard is the next slice";

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

        return Flex.Column(gap: 14).Padding(20).Children(
            UiText.New("Pointer input").FontSize(19f).FontWeight(FontWeight.Bold),
            UiText.New("Hover, click and the wheel arrive as ordinary WinForms events.")
                .FontSize(13f),
            Flex.Row(gap: 14).AlignItems(AlignItems.Center).Children(button, label));
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

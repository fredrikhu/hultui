using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Meridian.Theme;

namespace Meridian;

public partial class MeridianApp
{
    // ── Title bar ────────────────────────────────────────────────────────

    // The in-app wordmark per the sheet: 12px, az-text-muted, no flanking rules.
    // The tracking is the letterform's (0.35em); the wordmark never takes the
    // accent colour.
    private Element BuildTitleBar()
    {
        var bar = Flex.Row(gap: 0)
            .Height(40).FlexShrink(0)
            .AlignItems(AlignItems.Stretch)
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0));

        var crumb = Flex.Row(gap: 10).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 0, 0, 16));
        crumb.AddChild(Wordmark(12f, Azimuth.TextMuted));
        if (_workspace is { } ws)
        {
            crumb.AddChild(Text.New("·").FontSize(12f).TextColor(Azimuth.TextFaint));
            crumb.AddChild(Text.New(ws.Name).FontSize(12f).TextColor(Azimuth.TextDim));
        }
        crumb.WindowDrag();
        bar.AddChild(crumb);

        var right = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Center);
        // Draggable filler so the empty middle of the bar drags the window.
        right.AddChild(Flex.Row(gap: 0).FlexGrow(1).AlignSelf(AlignSelf.Stretch).WindowDrag());

        // The active environment, when one is loaded. Plain text on purpose —
        // the switcher popover is P4, and a chip that looks clickable but isn't
        // would be a phantom control.
        if (_envName is { } envName)
            right.AddChild(Ui.Mono($"env: {envName}", 11f, Azimuth.TextMuted).AutomationId("env-name"));

        right.AddChild(Box.New().Width(14));

        var controls = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch);
        controls.AddChild(WinButton(Icons.WinMinimize, "Minimize", () => _window.Minimize(), Azimuth.Raised));
        // Flagged so the Windows custom frame reports HTMAXBUTTON and Windows 11
        // shows its Snap Layouts flyout on hover.
        controls.AddChild(WinButton(Icons.WinMaximize, "Maximize", () => _window.ToggleMaximize(), Azimuth.Raised)
            .WindowMaximizeButton());
        controls.AddChild(WinButton(Icons.WinClose, "Close", () => _window.Close(), new Color(232, 17, 35)));
        right.AddChild(controls);

        bar.AddChild(right);
        return bar;
    }

    /// <summary>"MERIDIAN" at the letterform's 0.35em tracking.</summary>
    internal static TextElement Wordmark(float size, Color color) =>
        Text.New("MERIDIAN").FontSize(size).FontWeight(FontWeight.Medium)
            .TextColor(color).LetterSpacing(Azimuth.WordmarkTracking(size));

    private BoxElement WinButton(string glyph, string tooltip, Action onClick, Color hoverBg)
    {
        // No pointer cursor — Windows caption buttons keep the arrow (see Junction's
        // chrome for the Snap Layouts rationale).
        var b = Flex.Row(gap: 0).Width(46).Height(40)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Tooltip(tooltip)
            .AccessibleRole(AccessibilityRole.Button).AccessibleName(tooltip);
        b.AddChild(Ui.Icon(glyph, 13f, Azimuth.Alpha(Azimuth.Text, 0.75)));
        b.OnClick(_ => onClick());
        return b.HoverBg(hoverBg, Color.Transparent);
    }

    // ── View rail ────────────────────────────────────────────────────────

    private Element BuildRail()
    {
        var rail = Flex.Column(gap: Azimuth.S1).FlexShrink(0)
            .Width(Azimuth.RailWidth)
            .Padding(new Thickness(Azimuth.S2, 0, Azimuth.S2, 0))
            .AlignItems(AlignItems.Center)
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 1, 0, 0));

        rail.AddChild(RailButton(MeridianView.Requests, "Requests", Icons.Requests, "1"));
        rail.AddChild(RailButton(MeridianView.Runner, "Runner", Icons.Runner, "2"));
        rail.AddChild(RailButton(MeridianView.Discovery, "Discovery", Icons.Discovery, "3"));
        rail.AddChild(RailButton(MeridianView.History, "History", Icons.History, "4"));
        rail.AddChild(RailButton(MeridianView.Environments, "Environments", Icons.Environments, "5"));
        return rail;
    }

    private Element RailButton(MeridianView view, string label, string glyph, string num)
    {
        bool active = View == view;
        var fg = active ? Azimuth.Accent : Azimuth.TextMuted;

        // Active: 16% accent tint + 2dip left edge. The edge border is reserved
        // (transparent) on inactive buttons so activation never shifts the glyph.
        var b = Flex.Column(gap: 2)
            .Width(40).Height(44).FlexShrink(0)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .CornerRadius(Azimuth.Radius1)
            .BorderColor(active ? Azimuth.Accent : Color.Transparent)
            .BorderWidth(new Thickness(2, 0, 0, 0))
            .Cursor(CursorType.Pointer)
            .Tooltip($"{label} ({num})")
            .AccessibleRole(AccessibilityRole.Button).AccessibleName($"{label} ({num})")
            .AutomationId("rail-" + label.ToLowerInvariant());
        if (active)
            b.Background(Azimuth.Tint(Azimuth.Accent));

        b.AddChild(Ui.Icon(glyph, 16f, fg));
        b.AddChild(Ui.Mono(num, 9f, active ? Azimuth.Alpha(Azimuth.Accent, 0.85) : Azimuth.TextFaint));
        b.OnClick(_ => SetView(view));
        if (!active)
            b.HoverBg(Azimuth.Raised, Color.Transparent);
        return b;
    }

    // ── Toast ────────────────────────────────────────────────────────────

    private Element BuildToast(string message)
    {
        var strip = Flex.Row(gap: 0)
            .Width(Dimension.Percent(100))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.FlexEnd)
            .Padding(new Thickness(0, 0, 22, 0))
            .ZIndex(50);
        strip.Position.Type = PositionType.Absolute;
        strip.Position.Left = Dimension.Px(0);
        strip.Position.Bottom = Dimension.Px(0);

        var pill = Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center)
            .Padding(new Thickness(9, 16, 9, 16))
            .CornerRadius(Azimuth.Radius2)
            .Background(Azimuth.Raised)
            .BorderColor(Azimuth.BorderStrong).BorderWidth(1f)
            .Children(Text.New(message).FontSize(12.5f).TextColor(Azimuth.Text));

        strip.AddChild(pill);
        return strip;
    }
}

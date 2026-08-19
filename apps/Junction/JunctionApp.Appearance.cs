using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Themes;
using Junction.Theme;

namespace Junction;

/// <summary>
/// The colour-scheme picker. Junction has no settings window, so this is a small overlay of
/// its own rather than a tab in one — reached from the command palette.
/// </summary>
public partial class JunctionApp
{
    private bool _appearanceOpen;

    private void OpenAppearance()
    {
        _appearanceOpen = true;
        _paletteOpen = false;
        MarkDirty();
    }

    private void CloseAppearance()
    {
        _appearanceOpen = false;
        MarkDirty();
    }

    /// <summary>
    /// Apply a scheme and remember it. Applied immediately rather than on an OK button:
    /// the whole window is behind the picker, so the change is its own preview, and there is
    /// nothing to confirm that the user cannot see.
    /// </summary>
    private void UseTheme(string id)
    {
        if (_settings.Appearance.Theme == id) return;

        string wasSyntax = Nocturne.Theme.SyntaxTheme;
        _settings.Appearance.Theme = id;
        Persist();

        Nocturne.Use(id);
        Ui.InvalidateIconCache();          // glyphs are rasterized against the ramp
        _app.Theme = Nocturne.ToHultTheme();   // the framework's widgets hold their own copy
        if (Nocturne.Theme.SyntaxTheme != wasSyntax) RetokenizeCode();
        MarkDirty();
    }

    private Element BuildAppearance()
    {
        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 140))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Padding(new Thickness(24))
            .ZIndex(46);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; CloseAppearance(); });

        var card = Flex.Column(gap: 14)
            .Width(720)
            .Padding(new Thickness(20))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg);
        card.On<ClickEvent>(e => e.StopPropagation = true);

        var header = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
        header.AddChild(Text.New("Appearance").FontSize(19f).FontWeight(FontWeight.SemiBold).FlexGrow(1));
        header.AddChild(Ui.GhostButton("Done", null, CloseAppearance));
        card.AddChild(header);
        card.AddChild(Text.New("Each card is painted in its own colours. Picking one applies it straight away.")
            .FontSize(13f).TextColor(Nocturne.On(0.72)));

        var scroll = ScrollView.Vertical().MaxHeight(420);
        var col = Flex.Column(gap: 10).Padding(new Thickness(0, 10, 0, 0));
        var themes = ThemeCatalog.All;
        for (int i = 0; i < themes.Count; i += 3)
        {
            var row = Flex.Row(gap: 10).AlignItems(AlignItems.Stretch);
            for (int c = i; c < Math.Min(i + 3, themes.Count); c++)
                row.AddChild(ThemeCard(themes[c]).FlexGrow(1).FlexBasis(Dimension.Px(0)));
            // Pad the last row so two cards don't stretch to the width of three.
            for (int pad = themes.Count; pad < i + 3; pad++)
                row.AddChild(Box.New().FlexGrow(1).FlexBasis(Dimension.Px(0)));
            col.AddChild(row);
        }
        scroll.AddChild(col);
        card.AddChild(scroll);

        backdrop.AddChild(card);
        return backdrop;
    }

    private Element ThemeCard(NamedTheme t)
    {
        bool on = _settings.Appearance.Theme == t.Id;
        var p = Palette.For(t);

        var card = Flex.Column(gap: 7)
            .Padding(new Thickness(10, 11, 10, 11)).CornerRadius(Nocturne.RadiusMd)
            .Background(p.Bg).Cursor(CursorType.Pointer)
            // Selection reads off the card's own palette, so it stays visible on a light
            // scheme where the app's current accent would not be.
            .BorderWidth(on ? 2f : 1f)
            .BorderColor(on ? p.Accents[3] : ColorMath.Alpha(p.Text, 0.18));

        var title = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        title.AddChild(Text.New(t.Name).FontSize(13f)
            .FontWeight(on ? FontWeight.Medium : FontWeight.Regular)
            .TextColor(p.Text).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).FlexGrow(1));
        if (on) title.AddChild(Ui.Icon(Icons.Check, 12f, p.Accents[3]));
        card.AddChild(title);

        // A strip of surface with the colours that land on it: the accent, the second
        // accent, and the three a diff and a search hit are drawn in.
        var strip = Flex.Row(gap: 6).AlignItems(AlignItems.Center).Height(22)
            .Padding(new Thickness(0, 7, 0, 7)).CornerRadius(Nocturne.RadiusSm)
            .Background(p.Surface);
        foreach (var dot in new[] { p.Accent, p.Accent2, p.AddSign, p.DelSign, p.MarkAmber })
            strip.AddChild(Box.New().Width(10).Height(10).CornerRadius(5f).Background(dot).FlexShrink(0));
        card.AddChild(strip);

        card.AddChild(Text.New(t.Blurb).FontSize(10.5f)
            .TextColor(ColorMath.Alpha(p.Text, 0.55))
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis));

        card.OnClick(_ => UseTheme(t.Id));
        card.AutomationId = "theme:" + t.Id;
        card.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        card.AccessibleName = t.Name;
        return card;
    }
}

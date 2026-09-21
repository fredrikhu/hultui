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
/// Options › Appearance: the colour-scheme cards, the interface scale and the code font.
/// This was a small overlay of its own while Junction had no settings window; it is a pane
/// of the Options dialog now (see JunctionApp.Options), with the same controls.
/// </summary>
public partial class JunctionApp
{
    /// <summary>
    /// Apply a scheme and remember it. Applied immediately rather than on an OK button:
    /// the whole window is behind the dialog, so the change is its own preview, and there is
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

    private Element BuildAppearancePane()
    {
        var col = Flex.Column(gap: 14).Padding(new Thickness(8, 0, 0, 0));
        col.AddChild(Text.New("Each card is painted in its own colours. Picking one applies it straight away.")
            .FontSize(12.5f).TextColor(Nocturne.On(0.62)));

        var grid = Flex.Column(gap: 10);
        var themes = ThemeCatalog.All;
        for (int i = 0; i < themes.Count; i += 3)
        {
            var row = Flex.Row(gap: 10).AlignItems(AlignItems.Stretch);
            for (int c = i; c < Math.Min(i + 3, themes.Count); c++)
                row.AddChild(ThemeCard(themes[c]).FlexGrow(1).FlexBasis(Dimension.Px(0)));
            // Pad the last row so two cards don't stretch to the width of three.
            for (int pad = themes.Count; pad < i + 3; pad++)
                row.AddChild(Box.New().FlexGrow(1).FlexBasis(Dimension.Px(0)));
            grid.AddChild(row);
        }
        col.AddChild(grid);

        col.AddChild(Box.New().Height(1).Background(Nocturne.Divider));
        col.AddChild(BuildScaleSection());
        // The code font moved to Code & diff with the rest of the diff's settings.
        return col;
    }

    /// <summary>The steps the zoom is offered in. Discrete rather than a slider: these are
    /// the sizes anyone actually wants, and each is one click away from the last.</summary>
    private static readonly float[] UiScaleSteps = [0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.5f, 1.75f, 2f];

    private Element BuildScaleSection()
    {
        var col = Flex.Column(gap: 7);
        col.AddChild(SectionTitle("Interface scale"));
        col.AddChild(Text.New("Scales the whole interface — rows, icons and gutters along with the text — on top of what the system asks for.")
            .FontSize(11.5f).TextColor(Nocturne.On(0.55)));

        var row = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        foreach (var step in UiScaleSteps)
            row.AddChild(ScaleChip(step));
        col.AddChild(row);
        return col;
    }

    private Element ScaleChip(float scale)
    {
        bool on = MathF.Abs(_settings.Appearance.UiScale - scale) < 0.001f;
        var chip = Flex.Row().AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(5, 10, 5, 10)).CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer)
            .Background(on ? Nocturne.Alpha(Nocturne.Accent, 0.14) : Nocturne.Bg)
            // An outline rather than a border: these sit in a row, and a border on the
            // selected one would be 2px wider than its neighbours and shift them.
            .Outline(1f, on ? Nocturne.Accent : Nocturne.Divider);
        chip.AddChild(Text.New($"{scale * 100f:0}%").FontSize(12f)
            .FontWeight(on ? FontWeight.Medium : FontWeight.Regular)
            .TextColor(on ? Nocturne.Accent : Nocturne.On(0.78)));
        chip.OnClick(e => { e.StopPropagation = true; UseUiScale(scale); });
        chip.AutomationId = $"ui-scale:{scale * 100f:0}";
        chip.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        chip.AccessibleName = $"{scale * 100f:0} percent";
        return chip;
    }

    private BoxElement BuildCodeFontSection()
    {
        var col = Flex.Column(gap: 7);
        col.AddChild(SectionTitle("Code font"));
        col.AddChild(Text.New("Used for the diff, the file viewer and blame. Leave the name empty for the system's monospace; a face this machine does not have falls back to it.")
            .FontSize(11.5f).TextColor(Nocturne.On(0.55)));

        var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center);

        var input = TextInput.New(_settings.Appearance.CodeFont).Style(Ui.Field())
            .PlaceholderText(Fonts.Mono)
            .OnTextChanged(UseCodeFont)
            .Height(Dimension.Px(32)).FlexGrow(1)
            .FontSize(12.5f).TextColor(Nocturne.Text);
        input.AutomationId = "code-font-name";
        row.AddChild(input);

        row.AddChild(StepButton(Icons.Minus, "Smaller", () => NudgeCodeFontSize(-1f),
            _settings.Appearance.CodeFontSize > JunctionSettingsData.MinCodeFontSize));
        row.AddChild(Box.New().Width(38).AlignItems(AlignItems.Center)
            .Children(Text.New($"{_settings.Appearance.CodeFontSize:0.#}")
                .FontSize(12.5f).TextColor(Nocturne.Text)));
        row.AddChild(StepButton(Icons.Plus, "Larger", () => NudgeCodeFontSize(1f),
            _settings.Appearance.CodeFontSize < JunctionSettingsData.MaxCodeFontSize));
        col.AddChild(row);

        // The preview is drawn in the chosen face at the chosen size, which is the only
        // honest way to say whether the machine actually has it.
        col.AddChild(Box.New()
            .Padding(new Thickness(7, 10, 7, 10)).CornerRadius(Nocturne.RadiusSm)
            .Background(Nocturne.Bg).Overflow(Overflow.Hidden)
            .Children(Ui.Code("if (hunk.Lines[i] != null) { stage(++count); }  0O1lI",
                    _settings.Appearance.CodeFontSize, CodeFont, Nocturne.On(0.85))
                .MaxLines(1).TextOverflow(TextOverflow.Ellipsis)));
        return col;
    }

    private static Element SectionTitle(string text) =>
        Text.New(text).FontSize(13f).FontWeight(FontWeight.SemiBold).TextColor(Nocturne.Text);

    private Element StepButton(string glyph, string tooltip, Action onClick, bool enabled) =>
        Ui.IconButton(glyph, onClick, tooltip, 28f).Enabled(enabled);

    /// <summary>
    /// Apply a zoom and remember it. Like the scheme cards above, it takes effect at once —
    /// the whole window is behind the dialog, so the change is its own preview.
    /// </summary>
    private void UseUiScale(float scale)
    {
        scale = Math.Clamp(scale, JunctionSettingsData.MinUiScale, JunctionSettingsData.MaxUiScale);
        if (MathF.Abs(_settings.Appearance.UiScale - scale) < 0.001f) return;
        _settings.Appearance.UiScale = scale;
        Persist();
        // The window relayouts on the resize path; OnWindowResize picks up the new device
        // scale and re-rasterizes the icons for it.
        _window.UserScale = scale;
        MarkDirty();
    }

    /// <summary>One step up or down the offered scales; <paramref name="direction"/> 0 goes
    /// back to 100%, which is what Ctrl+0 means everywhere else.</summary>
    private void NudgeUiScale(int direction)
    {
        if (direction == 0) { UseUiScale(1f); return; }
        float current = _settings.Appearance.UiScale;
        if (direction > 0)
        {
            foreach (var step in UiScaleSteps)
                if (step > current + 0.001f) { UseUiScale(step); return; }
        }
        else
        {
            for (int i = UiScaleSteps.Length - 1; i >= 0; i--)
                if (UiScaleSteps[i] < current - 0.001f) { UseUiScale(UiScaleSteps[i]); return; }
        }
    }

    private void UseCodeFont(string family)
    {
        family = family.Trim();
        if (_settings.Appearance.CodeFont == family) return;
        _settings.Appearance.CodeFont = family;
        InvalidateCodeMetrics();   // persisted when Options closes — see CloseOptions
        MarkDirty();
    }

    private void NudgeCodeFontSize(float delta) => UseCodeFontSize(_settings.Appearance.CodeFontSize + delta);

    private void UseCodeFontSize(float size)
    {
        size = Math.Clamp(MathF.Round(size * 2f) / 2f,
            JunctionSettingsData.MinCodeFontSize, JunctionSettingsData.MaxCodeFontSize);
        if (MathF.Abs(_settings.Appearance.CodeFontSize - size) < 0.001f) return;
        _settings.Appearance.CodeFontSize = size;
        Persist();
        InvalidateCodeMetrics();
        MarkDirty();
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
            .BorderWidth(1f)
            .BorderColor(on ? p.Accents[3] : ColorMath.Alpha(p.Text, 0.18))
            // The second px of the selected card's edge is a ring outside it. As a
            // border it made the card taller than its unselected neighbours, and the
            // row stretches to the tallest, so the whole row grew when selection moved.
            .Outline(on ? 1f : 0f, on ? p.Accents[3] : Color.Transparent);

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

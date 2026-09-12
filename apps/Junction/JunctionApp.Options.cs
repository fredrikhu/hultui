using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Theme;

namespace Junction;

/// <summary>The Options dialog's sections, in rail order.</summary>
internal enum OptionsSection { General, Appearance, CodeAndDiff, ExternalTools, GitLabAccounts, KeyboardShortcuts }

/// <summary>
/// The Options dialog: a rail of sections on the left, one pane on the right, settings that
/// save as they change. Reached from the palette and Ctrl+, — there is no gear in the
/// chrome by design. Appearance moved in here from its own overlay; GitLab accounts is the
/// pane the integration needed a home for. The other four sections are drawn in the design
/// and land with a later phase, so the rail shows only the panes that exist.
/// </summary>
public partial class JunctionApp
{
    private bool _optionsOpen;
    private OptionsSection _optionsSection = OptionsSection.Appearance;

    private static readonly (OptionsSection Section, string Icon, string Title)[] OptionsRail =
    [
        (OptionsSection.General, Icons.SlidersHorizontal, "General"),
        (OptionsSection.Appearance, Icons.Palette, "Appearance"),
        (OptionsSection.CodeAndDiff, Icons.Code, "Code & diff"),
        (OptionsSection.ExternalTools, Icons.TerminalWindow, "External tools"),
        (OptionsSection.GitLabAccounts, Icons.GitLabLogo, "GitLab accounts"),
        (OptionsSection.KeyboardShortcuts, Icons.Terminal, "Keyboard shortcuts"),
    ];

    /// <summary>Which sections have a pane today. The rest come with the Options phase.</summary>
    private static bool OptionsSectionBuilt(OptionsSection s) =>
        s is OptionsSection.Appearance or OptionsSection.GitLabAccounts;

    internal bool OptionsOpen => _optionsOpen;
    internal OptionsSection OptionsSectionShown => _optionsSection;

    internal void OpenOptions(OptionsSection? section = null)
    {
        var wanted = section ?? OptionsSection.General;
        _optionsSection = OptionsSectionBuilt(wanted)
            ? wanted
            : OptionsRail.First(r => OptionsSectionBuilt(r.Section)).Section;
        _optionsOpen = true;
        _paletteOpen = false;
        _popover = null;
        MarkDirty();
    }

    private void CloseOptions()
    {
        _optionsOpen = false;
        CancelHostCard();
        CancelOAuth();
        // The code font is typed, and writing the settings file once per keystroke is a
        // file write per character; every other choice in here persists as it is made, so
        // the one that cannot persists on the way out.
        Persist();
        MarkDirty();
    }

    /// <summary>Esc backs out one step: the add-host card first, then the dialog.</summary>
    private void OptionsEscape()
    {
        if (_hostCard != null) { CancelHostCard(); MarkDirty(); return; }
        CloseOptions();
    }

    private void ShowOptionsSection(OptionsSection s)
    {
        if (!OptionsSectionBuilt(s) || _optionsSection == s) return;
        _optionsSection = s;
        MarkDirty();
    }

    private Element BuildOptions()
    {
        // Column backdrop: JustifyContent is the vertical axis, so FlexStart + top padding
        // anchors the card 44px down; AlignItems centres it horizontally.
        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 140))
            .JustifyContent(JustifyContent.FlexStart).AlignItems(AlignItems.Center)
            .Padding(new Thickness(44, 24, 24, 24))
            // Above the repository panels (42), below the confirm dialog (45) a sign-out asks
            // through, and below the repo modal (70).
            .ZIndex(44);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; CloseOptions(); });

        var card = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch)
            .Width(860).Height(640)
            .MaxWidth(Dimension.Percent(94)).MaxHeight(Dimension.Percent(92))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg)
            .Overflow(Overflow.Hidden);
        card.On<ClickEvent>(e => e.StopPropagation = true);
        card.AutomationId = "options";

        card.AddChild(BuildOptionsRail());
        card.AddChild(BuildOptionsPane());
        backdrop.AddChild(card);
        return backdrop;
    }

    private Element BuildOptionsRail()
    {
        // The rail paints its own ground, so it has to round the card's two left corners
        // itself — a child's fill does not follow the parent's radius, and a square fill in
        // a rounded card reads as a corner that was forgotten.
        var rail = Flex.Column(gap: 0).Width(200).FlexShrink(0)
            .Background(Nocturne.Bg)
            .CornerRadius(new CornerRadius(Nocturne.RadiusLg, 0, 0, Nocturne.RadiusLg))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));

        rail.AddChild(Text.New("Options").FontSize(16f).FontWeight(FontWeight.Medium)
            .Padding(new Thickness(16, 16, 10, 16)));

        var list = Flex.Column(gap: 2).Padding(new Thickness(0, 8, 0, 8));
        foreach (var (section, icon, title) in OptionsRail)
        {
            if (!OptionsSectionBuilt(section)) continue;
            bool on = section == _optionsSection;
            var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center)
                .Padding(new Thickness(8, 10, 8, 10)).CornerRadius(Nocturne.RadiusMd)
                .Cursor(CursorType.Pointer);
            if (on)
                row.Background(Nocturne.Accent900).BorderColor(Nocturne.Accent).BorderWidth(new Thickness(0, 0, 0, 2));
            row.AddChild(Ui.Icon(icon, 15f, on ? Nocturne.Accent100 : Nocturne.On(0.7)));
            row.AddChild(Text.New(title).FontSize(13f).TextColor(on ? Nocturne.Accent100 : Nocturne.Text));
            row.OnClick(_ => ShowOptionsSection(section));
            if (!on) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
            row.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
            row.AccessibleName = title;
            row.Key = "options-rail-" + section;
            list.AddChild(row);
        }
        rail.AddChild(list);

        rail.AddChild(Box.New().FlexGrow(1));
        rail.AddChild(Text.New("Settings save as you change them.").FontSize(10.5f).TextColor(Nocturne.On(0.42))
            .Padding(new Thickness(12, 16, 12, 16)));
        return rail;
    }

    private Element BuildOptionsPane()
    {
        var (_, _, title) = OptionsRail.First(r => r.Section == _optionsSection);
        var pane = Flex.Column(gap: 0).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));

        var header = Flex.Row(gap: 10).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(14, 22, 12, 22))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Text.New(title).FontSize(15f).FontWeight(FontWeight.Medium).FlexGrow(1));
        var done = Ui.GhostButton("Done", null, CloseOptions);
        done.AccessibleName = "Done";
        header.AddChild(done);
        pane.AddChild(header);

        // Pad the inner column, not the ScrollView, so bordered children are not clipped
        // against the scroll's content-box edge.
        var body = ScrollView.New().FlexGrow(1).FlexShrink(1);
        var col = Flex.Column(gap: 0).Padding(new Thickness(6, 22, 22, 22));
        col.AddChild(_optionsSection switch
        {
            OptionsSection.GitLabAccounts => BuildGitLabAccountsPane(),
            _ => BuildAppearancePane(),
        });
        body.AddChild(col);
        // Keyed by section so switching panes replaces the subtree rather than patching one
        // pane's fields onto another's.
        body.Key = "options-pane-" + _optionsSection;
        pane.AddChild(body);
        return pane;
    }

    // ── Setting-row primitives (shared by every pane) ────────────────────────────────

    /// <summary>Label + description on the left, the control flush right, a hairline below.</summary>
    private static Element OptionRow(string label, string? description, Element control)
    {
        var row = Flex.Row(gap: 16).AlignItems(AlignItems.Center)
            .Padding(new Thickness(12, 0, 12, 0))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        var text = Flex.Column(gap: 0).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));
        text.AddChild(Text.New(label).FontSize(13f).FontWeight(FontWeight.Medium));
        if (description != null)
            text.AddChild(Text.New(description).FontSize(11.5f).TextColor(Nocturne.On(0.52)));
        row.AddChild(text);
        control.FlexShrink(0);
        row.AddChild(control);
        return row;
    }

    /// <summary>The 34×20 pill toggle: accent when on, neutral when off, a 16px knob.</summary>
    private static Element OptionToggle(bool on, Action onToggle, string name)
    {
        var track = Box.New().Width(34).Height(20).CornerRadius(10f)
            .Background(on ? Nocturne.Accent : Nocturne.Neutral700)
            .Cursor(CursorType.Pointer)
            .Padding(new Thickness(2, on ? 2 : 16, 2, on ? 16 : 2));
        track.AddChild(Box.New().Width(16).Height(16).CornerRadius(8f).Background(Nocturne.Text));
        track.OnClick(_ => onToggle());
        track.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        track.AccessibleName = name;
        track.Key = "toggle-" + name + (on ? "-on" : "-off");
        return track;
    }

    /// <summary>The include-bar chip: divider outline off, accent outline + 14% fill on.</summary>
    private static Element OptionChip(string label, bool on, Action onClick)
    {
        var chip = Flex.Row(gap: 5).AlignItems(AlignItems.Center)
            .Padding(new Thickness(3, 9, 3, 9)).CornerRadius(999f)
            .Cursor(CursorType.Pointer)
            .Background(on ? Nocturne.Alpha(Nocturne.Accent, 0.14) : Color.Transparent)
            .Outline(1f, on ? Nocturne.Accent : Nocturne.Divider);
        chip.AddChild(Text.New(label).FontSize(11f).TextColor(on ? Nocturne.Accent200 : Nocturne.On(0.62)));
        chip.OnClick(_ => onClick());
        chip.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        chip.AccessibleName = label;
        return chip;
    }
}

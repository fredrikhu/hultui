using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Components.Shared;

public sealed class OptionsDialogState
{
    public required GitClientSettingsData Settings { get; init; }
    public required Action OnSettingsChanged { get; init; }
    public OptionsTab ActiveTab { get; set; } = OptionsTab.Appearance;
}

public enum OptionsTab
{
    General,
    Git,
    ExternalTools,
    Appearance,
}

public static class OptionsDialog
{
    public static Element Build(OptionsDialogState state, Action dismiss)
    {
        var backdrop = Box.New()
            .Width(Dimension.Percent(100))
            .Height(Dimension.Percent(100))
            .Background(GitClientColors.DialogBackdrop)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .ZIndex(100);

        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            dismiss();
        });

        var panel = Flex.Column(gap: 0)
            .Width(720)
            .Height(520)
            .CornerRadius(8f)
            .Background(GitClientColors.DialogBackground)
            .BorderColor(GitClientColors.DialogBorder)
            .BorderWidth(1);
        panel.On<ClickEvent>(e => e.StopPropagation = true);

        panel.AddChild(BuildHeader(dismiss));
        panel.AddChild(Flex.Row(gap: 0)
            .FlexGrow(1)
            .Children(
                BuildTabRail(state),
                BuildContent(state).FlexGrow(1)
            ));

        backdrop.AddChild(panel);
        return backdrop;
    }

    private static Element BuildHeader(Action dismiss)
    {
        return Flex.Row(gap: 8)
            .Height(48)
            .Padding(new Thickness(0, 16, 0, 16))
            .AlignItems(AlignItems.Center)
            .BorderColor(GitClientColors.Border)
            .BorderWidth(new Thickness(0, 0, 1, 0))
            .Children(
                Text.New("Options").FontSize(16f).FontWeight(FontWeight.Bold)
                    .TextColor(GitClientColors.TextPrimary)
                    .FlexGrow(1),
                IconButton("x", "Close", dismiss)
            );
    }

    private static Element BuildTabRail(OptionsDialogState state)
    {
        return Flex.Column(gap: 4)
            .Width(180)
            .FlexShrink(0)
            .Padding(new Thickness(12, 10, 12, 10))
            .Background(GitClientColors.Surface)
            .BorderColor(GitClientColors.Border)
            .BorderWidth(new Thickness(0, 0, 0, 1))
            .Children(
                TabButton(state, OptionsTab.General, "General"),
                TabButton(state, OptionsTab.Git, "Git"),
                TabButton(state, OptionsTab.ExternalTools, "External Tools"),
                TabButton(state, OptionsTab.Appearance, "Appearance")
            );
    }

    private static Element TabButton(OptionsDialogState state, OptionsTab tab, string label)
    {
        var active = state.ActiveTab == tab;
        var bg = active ? GitClientColors.Selection : Color.Transparent;
        var hoverBg = active ? GitClientColors.Selection : GitClientColors.SurfaceHover;
        var item = Flex.Row(gap: 0)
            .Width(Dimension.Percent(100))
            .Height(32)
            .Padding(new Thickness(0, 10, 0, 10))
            .CornerRadius(4f)
            .AlignItems(AlignItems.Center)
            .Background(bg)
            .Cursor(CursorType.Pointer)
            .Children(Text.New(label).FontSize(12f).FontWeight(active ? FontWeight.SemiBold : FontWeight.Regular)
                .TextColor(active ? GitClientColors.TextPrimary : GitClientColors.TextSecondary)
                .MaxLines(1)
                .OverflowMode(TextOverflow.Ellipsis));

        item.On<PointerEnterEvent>(_ => item.BackgroundColor = hoverBg);
        item.On<PointerLeaveEvent>(_ => item.BackgroundColor = bg);
        item.On<ClickEvent>(_ =>
        {
            state.ActiveTab = tab;
            state.OnSettingsChanged();
        });
        return item;
    }

    private static Element BuildContent(OptionsDialogState state)
    {
        var content = Flex.Column(gap: 16)
            .Padding(new Thickness(18, 20, 18, 20))
            .Background(GitClientColors.Background);

        switch (state.ActiveTab)
        {
            case OptionsTab.General:
                BuildGeneral(content, state);
                break;
            case OptionsTab.Git:
                BuildGit(content, state);
                break;
            case OptionsTab.ExternalTools:
                BuildExternalTools(content, state);
                break;
            case OptionsTab.Appearance:
                BuildAppearance(content, state);
                break;
        }

        return content;
    }

    private static void BuildGeneral(BoxElement content, OptionsDialogState state)
    {
        content.AddChild(SectionTitle("General"));
        content.AddChild(Checkbox.New(state.Settings.General.AutoFetchEnabled, "Enable auto-fetch")
            .TextColor(GitClientColors.TextPrimary)
            .OnCheckedChanged(value =>
            {
                state.Settings.General.AutoFetchEnabled = value;
                state.OnSettingsChanged();
            }));

        content.AddChild(OptionRow(
            "Interval minutes",
            TextInput.New(state.Settings.General.AutoFetchIntervalMinutes.ToString())
                .Width(90)
                .Height(30)
                .Background(GitClientColors.Surface)
                .BorderColor(GitClientColors.Border)
                .BorderWidth(1)
                .CornerRadius(4)
                .TextColor(GitClientColors.TextPrimary)
                .OnTextChanged(value =>
                {
                    if (int.TryParse(value, out var minutes))
                    {
                        state.Settings.General.AutoFetchIntervalMinutes = Math.Clamp(minutes, 1, 120);
                        state.OnSettingsChanged();
                    }
                })));

        content.AddChild(RadioGroup(
            "Fetch scope",
            ("Current repository", state.Settings.General.AutoFetchMode == AutoFetchMode.CurrentRepository, () =>
            {
                state.Settings.General.AutoFetchMode = AutoFetchMode.CurrentRepository;
                state.OnSettingsChanged();
            }),
            ("All open repositories", state.Settings.General.AutoFetchMode == AutoFetchMode.AllOpenRepositories, () =>
            {
                state.Settings.General.AutoFetchMode = AutoFetchMode.AllOpenRepositories;
                state.OnSettingsChanged();
            })));
    }

    private static void BuildGit(BoxElement content, OptionsDialogState state)
    {
        content.AddChild(SectionTitle("Git"));
        content.AddChild(RadioGroup(
            "Pull strategy",
            ("Rebase", state.Settings.Git.PullStrategy == PullStrategy.Rebase, () =>
            {
                state.Settings.Git.PullStrategy = PullStrategy.Rebase;
                state.Settings.Git.PullAutoStash = true;
                state.OnSettingsChanged();
            }),
            ("Merge", state.Settings.Git.PullStrategy == PullStrategy.Merge, () =>
            {
                state.Settings.Git.PullStrategy = PullStrategy.Merge;
                state.OnSettingsChanged();
            }),
            ("Fast-forward only", state.Settings.Git.PullStrategy == PullStrategy.FastForwardOnly, () =>
            {
                state.Settings.Git.PullStrategy = PullStrategy.FastForwardOnly;
                state.OnSettingsChanged();
            })));

        content.AddChild(Checkbox.New(state.Settings.Git.PullAutoStash, "Auto-stash before pull")
            .TextColor(GitClientColors.TextPrimary)
            .OnCheckedChanged(value =>
            {
                state.Settings.Git.PullAutoStash = value;
                state.OnSettingsChanged();
            }));

        content.AddChild(Checkbox.New(state.Settings.Git.PruneOnFetch, "Prune during fetch")
            .TextColor(GitClientColors.TextPrimary)
            .OnCheckedChanged(value =>
            {
                state.Settings.Git.PruneOnFetch = value;
                state.OnSettingsChanged();
            }));
    }

    private static void BuildExternalTools(BoxElement content, OptionsDialogState state)
    {
        content.AddChild(SectionTitle("External Tools"));
        content.AddChild(RadioGroup(
            "Diff tool",
            ("None", state.Settings.ExternalTools.DiffToolPreset == ExternalToolPreset.None, () =>
            {
                state.Settings.ExternalTools.DiffToolPreset = ExternalToolPreset.None;
                state.OnSettingsChanged();
            }),
            ("VS Code", state.Settings.ExternalTools.DiffToolPreset == ExternalToolPreset.VSCode, () =>
            {
                state.Settings.ExternalTools.DiffToolPreset = ExternalToolPreset.VSCode;
                state.Settings.ExternalTools.ExternalDiffToolPath = "code";
                state.Settings.ExternalTools.ExternalDiffToolArgs = "--wait --diff {left} {right}";
                state.Settings.ExternalTools.ExternalMergeToolPath = "code";
                state.Settings.ExternalTools.ExternalMergeToolArgs = "--wait {merged}";
                state.OnSettingsChanged();
            }),
            ("Custom", state.Settings.ExternalTools.DiffToolPreset == ExternalToolPreset.Custom, () =>
            {
                state.Settings.ExternalTools.DiffToolPreset = ExternalToolPreset.Custom;
                state.OnSettingsChanged();
            })));

        content.AddChild(OptionRow("Diff executable", ExternalInput(
            state.Settings.ExternalTools.ExternalDiffToolPath,
            value =>
            {
                state.Settings.ExternalTools.ExternalDiffToolPath = value;
                state.Settings.ExternalTools.DiffToolPreset = ExternalToolPreset.Custom;
                state.OnSettingsChanged();
            })));

        content.AddChild(OptionRow("Diff arguments", ExternalInput(
            state.Settings.ExternalTools.ExternalDiffToolArgs,
            value =>
            {
                state.Settings.ExternalTools.ExternalDiffToolArgs = value;
                state.Settings.ExternalTools.DiffToolPreset = ExternalToolPreset.Custom;
                state.OnSettingsChanged();
            })));
    }

    private static void BuildAppearance(BoxElement content, OptionsDialogState state)
    {
        content.AddChild(SectionTitle("Appearance"));
        content.AddChild(RadioGroup(
            "Theme",
            ("Dark", state.Settings.Appearance.ThemeMode == ThemeMode.Dark, () =>
            {
                state.Settings.Appearance.ThemeMode = ThemeMode.Dark;
                state.OnSettingsChanged();
            }),
            ("Light", state.Settings.Appearance.ThemeMode == ThemeMode.Light, () =>
            {
                state.Settings.Appearance.ThemeMode = ThemeMode.Light;
                state.OnSettingsChanged();
            }),
            ("System", state.Settings.Appearance.ThemeMode == ThemeMode.System, () =>
            {
                state.Settings.Appearance.ThemeMode = ThemeMode.System;
                state.OnSettingsChanged();
            })));
    }

    private static Element SectionTitle(string text)
    {
        return Text.New(text).FontSize(14f).FontWeight(FontWeight.Bold)
            .TextColor(GitClientColors.TextPrimary);
    }

    private static Element OptionRow(string label, Element control)
    {
        return Flex.Row(gap: 12)
            .AlignItems(AlignItems.Center)
            .Children(
                Text.New(label).FontSize(12f).TextColor(GitClientColors.TextSecondary)
                    .Width(140),
                control.FlexGrow(1)
            );
    }

    private static Element RadioGroup(string label, params (string Label, bool Checked, Action Select)[] options)
    {
        var groupName = $"options-{label}";
        var column = Flex.Column(gap: 8);
        column.AddChild(Text.New(label).FontSize(12f).TextColor(GitClientColors.TextSecondary));

        foreach (var option in options)
        {
            column.AddChild(RadioButton.New(groupName, option.Checked, option.Label)
                .TextColor(GitClientColors.TextPrimary)
                .OnRadioChanged(checked_ =>
                {
                    if (checked_)
                        option.Select();
                }));
        }

        return column;
    }

    private static Element ExternalInput(string value, Action<string> onChanged)
    {
        return TextInput.New(value)
            .Height(30)
            .Background(GitClientColors.Surface)
            .BorderColor(GitClientColors.Border)
            .BorderWidth(1)
            .CornerRadius(4)
            .TextColor(GitClientColors.TextPrimary)
            .OnTextChanged(onChanged);
    }

    private static Element IconButton(string label, string tooltip, Action onClick)
    {
        var btn = Box.New()
            .Width(28)
            .Height(28)
            .CornerRadius(4)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .Tooltip(tooltip)
            .Children(Text.New(label).FontSize(12f).TextColor(GitClientColors.TextSecondary));

        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = GitClientColors.SurfaceHover);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = Color.Transparent);
        btn.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            onClick();
        });

        return btn;
    }
}

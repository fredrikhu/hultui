using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Theme;

namespace Junction;

/// <summary>
/// Options › General, Code &amp; diff, External tools and Keyboard shortcuts — the four
/// panes the design drew and Phase 6 built. Every control writes its setting as it is
/// touched; the panes that used to be palette toggles (background fetch) live here now.
/// </summary>
public partial class JunctionApp
{
    // ── General ──────────────────────────────────────────────────────────────────────

    private static readonly int[] AutoFetchIntervals = [1, 5, 15, 30];
    private static readonly int[] GitLabRefreshIntervals = [2, 5, 15];

    private Element BuildGeneralPane()
    {
        var g = _settings.General;
        var col = Flex.Column(gap: 0);

        col.AddChild(OptionRow("Auto-fetch",
            "Fetch every open repository in the background, so ahead/behind counts and remote branches stay current.",
            OptionToggle(g.AutoFetchEnabled, () => { ToggleAutoFetch(); MarkDirty(); }, "Auto-fetch")));
        if (g.AutoFetchEnabled)
        {
            var chips = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
            foreach (var minutes in AutoFetchIntervals)
            {
                int m = minutes;
                chips.AddChild(OptionChip($"{m} min", g.AutoFetchIntervalMinutes == m, () =>
                {
                    g.AutoFetchIntervalMinutes = m;
                    Persist();
                    RestartAutoFetch();
                    MarkDirty();
                }));
            }
            col.AddChild(IndentedRow("Every", chips));
        }

        col.AddChild(OptionRow("Pull strategy", "How Pull brings the remote's commits into the branch.",
            OptionSegmented(["Merge", "Rebase", "Fast-forward only"], (int)_settings.Git.PullStrategy, i =>
            {
                _settings.Git.PullStrategy = (PullStrategy)i;
                Persist();
                MarkDirty();
            }, "Pull strategy")));

        col.AddChild(OptionRow("Prune on fetch", "Drop remote-tracking branches the remote no longer has.",
            OptionToggle(_settings.Git.PruneOnFetch, () => { _settings.Git.PruneOnFetch = !_settings.Git.PruneOnFetch; Persist(); MarkDirty(); }, "Prune on fetch")));

        col.AddChild(OptionRow("Refresh GitLab data on a timer",
            "Merge requests, pipelines and threads reload on Fetch, when the panel opens and on a repository switch. On a timer they also reload every few minutes. Every hosted surface shows when its data is from, and marks it stale after ten minutes.",
            OptionToggle(g.GitLabRefreshMinutes > 0, () =>
            {
                g.GitLabRefreshMinutes = g.GitLabRefreshMinutes > 0 ? 0 : 5;
                Persist();
                RestartGitLabRefresh();
                MarkDirty();
            }, "Refresh GitLab data on a timer")));
        if (g.GitLabRefreshMinutes > 0)
        {
            var chips = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
            foreach (var minutes in GitLabRefreshIntervals)
            {
                int m = minutes;
                chips.AddChild(OptionChip($"{m} min", g.GitLabRefreshMinutes == m, () =>
                {
                    g.GitLabRefreshMinutes = m;
                    Persist();
                    RestartGitLabRefresh();
                    MarkDirty();
                }));
            }
            col.AddChild(IndentedRow("Every", chips));
        }

        col.AddChild(OptionRow("Confirm destructive actions",
            "Ask before discarding changes or deleting a merged branch. A branch with unmerged work always asks.",
            OptionToggle(g.ConfirmDestructive, () => { g.ConfirmDestructive = !g.ConfirmDestructive; Persist(); MarkDirty(); }, "Confirm destructive actions")));

        // Todos: which kinds ring the bell, and from where.
        var kinds = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        kinds.FlexWrap(FlexWrap.Wrap);
        foreach (var (kind, label) in TodoKinds)
        {
            var k = kind;
            bool on = !g.TodoHiddenKinds.Contains(k);
            kinds.AddChild(OptionChip(label, on, () =>
            {
                if (on) g.TodoHiddenKinds.Add(k); else g.TodoHiddenKinds.Remove(k);
                Persist();
                MarkDirty();
            }));
        }
        col.AddChild(OptionRow("Todos that ring the bell", "Kinds of GitLab todo the bell counts. The rest stay on GitLab.", Box.New()));
        col.AddChild(IndentedRow(null, kinds));
        col.AddChild(OptionRow("Only the open workspace's repositories",
            "Count todos only on projects that a repository in the open workspace is a clone of.",
            OptionToggle(g.TodoOnlyWorkspaceRepos, () => { g.TodoOnlyWorkspaceRepos = !g.TodoOnlyWorkspaceRepos; Persist(); MarkDirty(); }, "Only the open workspace's repositories")));
        return col;
    }

    /// <summary>The todo kinds a person can silence, with the words the bell uses for them.</summary>
    private static readonly (string Kind, string Label)[] TodoKinds =
    [
        ("review_requested", "review requested"),
        ("mentioned", "mentioned"),
        ("assigned", "assigned"),
        ("build_failed", "pipeline failed"),
        ("approval_required", "approval required"),
        ("unmergeable", "unmergeable"),
        ("marked", "marked"),
    ];

    /// <summary>A sub-row under a toggle: indented, no divider of its own.</summary>
    private static Element IndentedRow(string? label, Element control)
    {
        var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 0, 12, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        if (label != null) row.AddChild(Text.New(label).FontSize(11.5f).TextColor(Nocturne.On(0.52)));
        row.AddChild(control);
        return row;
    }

    /// <summary>The diff pane's Unified/Split segmented style, as an option control.</summary>
    private static Element OptionSegmented(string[] labels, int selected, Action<int> pick, string name)
    {
        var seg = Flex.Row(gap: 2).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(2))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusRow);
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            bool on = i == selected;
            var b = Flex.Row(gap: 5).FlexShrink(0).AlignItems(AlignItems.Center)
                .Padding(new Thickness(4, 9, 4, 9)).CornerRadius(5f).Cursor(CursorType.Pointer);
            if (on) b.Background(Nocturne.Bg).Outline(1f, Nocturne.Accent);
            b.AddChild(Text.New(labels[i]).FontSize(11.5f).FontWeight(FontWeight.Medium)
                .TextColor(on ? Nocturne.Accent : Nocturne.On(0.58)));
            b.OnClick(e => { e.StopPropagation = true; pick(index); });
            b.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
            b.AccessibleName = labels[i];
            b.Key = $"seg-{name}-{i}-{(on ? "on" : "off")}";
            seg.AddChild(b);
        }
        return seg;
    }

    // ── Code & diff ──────────────────────────────────────────────────────────────────

    private Element BuildCodeAndDiffPane()
    {
        var col = Flex.Column(gap: 0);
        var font = BuildCodeFontSection();
        font.Padding(new Thickness(12, 0, 14, 0));
        font.BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        col.AddChild(font);

        col.AddChild(OptionRow("Default diff view", "Unified shows one column of lines; split shows the two versions side by side. The s key switches.",
            OptionSegmented(["Unified", "Split"], _diffSplit ? 1 : 0, i => SetDiffSplit(i == 1), "Default diff view")));
        col.AddChild(OptionRow("Wrap long lines", "Wrapped, every line is visible in full; unwrapped, long lines scroll sideways. The w key switches.",
            OptionToggle(_diffWrap, ToggleDiffWrap, "Wrap long lines")));
        col.AddChild(OptionRow("Ignore whitespace", "Hide whitespace-only changes (git --ignore-all-space). Line staging is off while this is on. The i key switches.",
            OptionToggle(_settings.Git.IgnoreWhitespace, ToggleIgnoreWhitespace, "Ignore whitespace")));
        return col;
    }

    // ── External tools ───────────────────────────────────────────────────────────────

    private static readonly (EditorPreset Preset, string Label)[] Editors =
    [
        (EditorPreset.VSCode, "VS Code"),
        (EditorPreset.VisualStudio, "Visual Studio"),
        (EditorPreset.Rider, "Rider"),
        (EditorPreset.Custom, "Custom"),
    ];

    private static readonly (TerminalPreset Preset, string Label)[] Terminals =
    [
        (TerminalPreset.System, "System default"),
        (TerminalPreset.WindowsTerminal, "Windows Terminal"),
        (TerminalPreset.PowerShell, "PowerShell"),
        (TerminalPreset.CommandPrompt, "Command Prompt"),
    ];

    private Element BuildExternalToolsPane()
    {
        var t = _settings.ExternalTools;
        var col = Flex.Column(gap: 0);

        var editors = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        foreach (var (preset, label) in Editors)
        {
            var p = preset;
            editors.AddChild(OptionChip(label, t.Editor == p, () => { t.Editor = p; Persist(); MarkDirty(); }));
        }
        col.AddChild(OptionRow("Editor", "What Ctrl D opens a diff in.", Box.New()));
        col.AddChild(IndentedRow(null, editors));
        if (t.Editor == EditorPreset.Custom)
        {
            var field = Flex.Row(gap: 0).AlignItems(AlignItems.Center)
                .Height(32).Padding(new Thickness(0, 10, 0, 10)).FlexGrow(1)
                .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f)
                .CornerRadius(Nocturne.RadiusMd);
            var input = TextInput.New(t.EditorCommand)
                .PlaceholderText("code --wait --diff {left} {right}")
                .OnTextChanged(v => t.EditorCommand = v)   // persisted when Options closes
                .Height(Dimension.Px(26)).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
                .FontFamily(Fonts.Mono).FontSize(12f).TextColor(Nocturne.Text);
            input.AutomationId = "editor-command";
            field.AddChild(input);
            col.AddChild(IndentedRow("Command", field));
        }

        var terminals = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        terminals.FlexWrap(FlexWrap.Wrap);
        foreach (var (preset, label) in Terminals)
        {
            var p = preset;
            terminals.AddChild(OptionChip(label, t.Terminal == p, () => { t.Terminal = p; Persist(); MarkDirty(); }));
        }
        col.AddChild(OptionRow("Terminal", "What \"Open in terminal\" starts, in the repository's folder.", Box.New()));
        col.AddChild(IndentedRow(null, terminals));
        return col;
    }

    // ── Keyboard shortcuts ───────────────────────────────────────────────────────────

    /// <summary>Two columns of groups, each a list of key + description rows, all read
    /// from <see cref="Shortcuts.All"/> — the table the legends and tooltips read.</summary>
    private static Element BuildShortcutsPane()
    {
        var groups = Shortcuts.Groups.ToList();
        var columns = Flex.Row(gap: 24).AlignItems(AlignItems.FlexStart).Padding(new Thickness(10, 0, 0, 0));
        var left = Flex.Column(gap: 18).FlexGrow(1).FlexBasis(Dimension.Px(0));
        var right = Flex.Column(gap: 18).FlexGrow(1).FlexBasis(Dimension.Px(0));
        for (int i = 0; i < groups.Count; i++)
            (i % 2 == 0 ? left : right).AddChild(ShortcutGroup(groups[i]));
        columns.AddChild(left);
        columns.AddChild(right);
        return columns;
    }

    private static Element ShortcutGroup(string group)
    {
        var col = Flex.Column(gap: 0);
        col.AddChild(Text.New(group).FontSize(11f).LetterSpacing(0.8f).TextColor(Nocturne.Accent300)
            .Padding(new Thickness(0, 0, 6, 0)));
        foreach (var entry in Shortcuts.All.Where(e => e.Group == group))
        {
            var row = Flex.Row(gap: 12).AlignItems(AlignItems.Center)
                .Padding(new Thickness(6, 0, 6, 0))
                .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
            row.AddChild(Kbd(entry.Keys).FlexShrink(0));
            row.AddChild(Text.New(entry.What).FontSize(12f).TextColor(Nocturne.On(0.78)).FlexShrink(1));
            col.AddChild(row);
        }
        return col;
    }
}

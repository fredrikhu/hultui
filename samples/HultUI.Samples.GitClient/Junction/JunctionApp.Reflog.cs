using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Junction;

public partial class JunctionApp
{
    // The reflog recovery panel: HEAD's history as a list, so work "lost" to a
    // reset/rebase/checkout can be branched or reset back to. A centred overlay
    // like the worktrees panel (opened from the command palette), not a view.

    private bool _reflogOpen;

    private readonly Dictionary<string, AsyncLoader<IReadOnlyList<GitReflogEntry>>> _reflogLoaders =
        new(PathComparer);

    private AsyncLoader<IReadOnlyList<GitReflogEntry>> _reflog =>
        LoaderFor(_reflogLoaders, _activeRepoPath, (IReadOnlyList<GitReflogEntry>)[]);

    private void OpenReflog()
    {
        if (!HasActiveRepo) { Flash("Open a repository first"); return; }
        _reflogOpen = true;
        // Rerender, not MarkDirty: opening activates this repo's loader for the
        // first time, and an untracked MarkDirty rebuild would leave the Effect
        // unsubscribed from it (panel stuck on its loading placeholder).
        Rerender();
    }

    private void CloseReflog()
    {
        _reflogOpen = false;
        MarkDirty();
    }

    private IReadOnlyList<GitReflogEntry> LoadReflog() => RepoFor(_activeRepoPath).GetReflog();

    // ── Panel ────────────────────────────────────────────────────────────

    private Element BuildReflogPanel()
    {
        _reflog.Load($"{_activeRepoPath}|{DataGen(_activeRepoPath)}", LoadReflog);
        var entries = _reflog.Value;
        var loading = _reflog.IsLoading && entries.Count == 0;

        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 140))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Padding(new Thickness(24))
            .ZIndex(42);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; CloseReflog(); });

        var card = Flex.Column(gap: 0)
            .Width(720).MaxHeight(600)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg);
        card.On<ClickEvent>(e => e.StopPropagation = true);

        card.AddChild(BuildReflogHeader());

        var scroll = ScrollView.New().FlexGrow(1).Padding(new Thickness(8, 10, 8, 10));
        if (loading)
            scroll.AddChild(ReflogPlaceholder("Loading reflog…"));
        else if (entries.Count == 0)
            scroll.AddChild(ReflogPlaceholder("No reflog entries"));
        else
            for (var i = 0; i < entries.Count; i++)
                scroll.AddChild(ReflogRow(entries[i], i));
        card.AddChild(scroll);

        backdrop.AddChild(card);
        return backdrop;
    }

    private Element BuildReflogHeader()
    {
        var repoName = System.IO.Path.GetFileName(_activeRepoPath.TrimEnd('/', '\\'));
        var bar = Flex.Row(gap: 10).AlignItems(AlignItems.Center)
            .Padding(new Thickness(14, 18, 14, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(Ui.Icon(Icons.CounterClockwise, 16f, Nocturne.Accent300).FlexShrink(0));
        var titleCol = Flex.Column(gap: 1).FlexGrow(1);
        titleCol.AddChild(Text.New("Reflog").FontSize(16f).FontWeight(FontWeight.SemiBold));
        titleCol.AddChild(Text.New($"{repoName} · branch or reset back here to recover lost work")
            .FontSize(11.5f).TextColor(Nocturne.On(0.5)).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        bar.AddChild(titleCol);
        bar.AddChild(Ui.IconButton(Icons.WinClose, CloseReflog, "Close (Esc)", 30f).FlexShrink(0));
        return bar;
    }

    private static Element ReflogPlaceholder(string text) =>
        Box.New().Padding(new Thickness(28)).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Text.New(text).FontSize(13f).TextColor(Nocturne.On(0.45)));

    private Element ReflogRow(GitReflogEntry e, int index)
    {
        var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center)
            .Padding(new Thickness(7, 10, 7, 10)).CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer);
        row.Key = "reflog:" + e.Selector;

        // Selector (HEAD@{n}), fixed width so the verb chips line up.
        row.AddChild(Box.New().Width(74).FlexShrink(0)
            .Children(Ui.Mono(e.Selector, 11f, Nocturne.On(0.5)).OverflowMode(TextOverflow.Ellipsis).MaxLines(1)));

        var (chipBg, chipFg) = ReflogVerbTint(e.Verb);
        row.AddChild(Ui.Tag(e.Verb.Length > 0 ? e.Verb : "entry", chipBg, chipFg, 9.5f).FlexShrink(0));

        var textCol = Flex.Column(gap: 1).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));
        textCol.AddChild(Text.New(e.Subject.Length > 0 ? e.Subject : e.Action).FontSize(12.5f)
            .TextColor(Nocturne.Text).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        textCol.AddChild(Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
            Ui.Mono(e.ShortHash, 10.5f, Nocturne.On(0.46)),
            Text.New(RelativeTime(e.Date)).FontSize(10.5f).TextColor(Nocturne.On(0.42))));
        row.AddChild(textCol);

        // Primary safe-recovery affordance + full menu on the caret / right-click.
        var branch = Ui.SecondaryButton("Branch here", Icons.GitBranch,
            () => PromptCreateBranchAt(e.Hash, e.Selector), 11f);
        branch.OnClick(ev => ev.StopPropagation = true);
        row.AddChild(branch.FlexShrink(0));

        row.OnClick(_ => PromptCreateBranchAt(e.Hash, e.Selector));
        row.ContextMenu(() => ReflogMenuItems(e));
        row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    private static (Color Bg, Color Fg) ReflogVerbTint(string verb) => verb switch
    {
        "reset" or "rebase" => (Nocturne.Accent2_800, Nocturne.Accent2_100),   // history-rewrite
        "commit" or "commit (amend)" => (Nocturne.Accent800, Nocturne.Accent100),
        _ => (Nocturne.Neutral800, Nocturne.Neutral100),
    };

    private IReadOnlyList<ContextMenuItem> ReflogMenuItems(GitReflogEntry e)
    {
        var cur = CurrentBranchTitle();
        return
        [
            Item(Icons.GitBranch, "Create branch here…", () => PromptCreateBranchAt(e.Hash, e.Selector)),
            Item(Icons.BendDownRight, "Checkout this commit (detached)", () => ReflogCheckout(e.Hash)),
            ContextMenuItem.Separator(),
            Item(Icons.LineLeft, $"Reset {cur} soft to here", () => ReflogReset(e.Hash, ResetMode.Soft)),
            Item(Icons.LineLeft, "Reset mixed to here", () => ReflogReset(e.Hash, ResetMode.Mixed)),
            Danger(Icons.Warning, "Reset hard to here", () => ReflogReset(e.Hash, ResetMode.Hard)),
            ContextMenuItem.Separator(),
            Item(Icons.Copy, "Copy commit SHA", () => CopyToClipboard(e.Hash)),
        ];
    }

    // Destructive/HEAD-moving recoveries close the panel so the user lands back on
    // the (now-updated) views; branching stays open so several can be recovered.
    private void ReflogCheckout(string hash) { CloseReflog(); DoCheckoutDetached(hash); }
    private void ReflogReset(string hash, ResetMode mode) { CloseReflog(); DoReset(hash, mode); }
}

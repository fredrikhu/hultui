using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    // Worktrees are a per-repo management surface presented as a centred overlay
    // (like the fullscreen diff / rebase editor) rather than a sixth numbered
    // view — worktree upkeep is occasional, not a primary daily tab. Opened from
    // the command palette and from a branch's context menu.

    private bool _worktreesOpen;

    private readonly KeyedAsyncLoader<string, IReadOnlyList<GitWorktree>> _worktreeLoaders =
        new([], capacity: 16, comparer: PathComparer);

    private AsyncLoader<IReadOnlyList<GitWorktree>> _worktrees =>
        _worktreeLoaders.For(_activeRepoPath);

    private void OpenWorktrees()
    {
        if (!HasActiveRepo) { Flash("Open a repository first"); return; }
        _worktreesOpen = true;
        // Rerender, not MarkDirty: opening activates this repo's loader for the
        // first time, and an untracked MarkDirty rebuild would leave the Effect
        // unsubscribed from it (panel stuck on its loading placeholder).
        Rerender();
    }

    private void CloseWorktrees()
    {
        _worktreesOpen = false;
        MarkDirty();
    }

    private IReadOnlyList<GitWorktree> LoadWorktrees() => RepoFor(_activeRepoPath).GetWorktrees();

    // ── Panel ────────────────────────────────────────────────────────────

    private Element BuildWorktreesPanel()
    {
        _worktrees.Load($"{_activeRepoPath}|{DataGen(_activeRepoPath)}", LoadWorktrees);
        var list = _worktrees.Value;
        var loading = _worktrees.IsLoading && list.Count == 0;

        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 140))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Padding(new Thickness(24))
            .ZIndex(42);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; CloseWorktrees(); });

        var card = Flex.Column(gap: 0)
            .Width(640).MaxHeight(560)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg);
        card.On<ClickEvent>(e => e.StopPropagation = true);

        card.AddChild(BuildWorktreesHeader());
        card.AddChild(BuildWorktreesToolbar());

        var scroll = ScrollView.New().FlexGrow(1).Padding(new Thickness(8, 10, 8, 10));
        if (loading)
            scroll.AddChild(WorktreesPlaceholder("Loading worktrees…"));
        else if (list.Count == 0)
            scroll.AddChild(WorktreesPlaceholder("No worktrees"));
        else
            foreach (var wt in list)
                scroll.AddChild(WorktreeRow(wt));
        card.AddChild(scroll);

        backdrop.AddChild(card);
        return backdrop;
    }

    private Element BuildWorktreesHeader()
    {
        var repoName = System.IO.Path.GetFileName(_activeRepoPath.TrimEnd('/', '\\'));
        var bar = Flex.Row(gap: 10).AlignItems(AlignItems.Center)
            .Padding(new Thickness(14, 18, 14, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(Ui.Icon(Icons.TreeStructure, 16f, Nocturne.Accent300).FlexShrink(0));
        var titleCol = Flex.Column(gap: 1).FlexGrow(1);
        titleCol.AddChild(Text.New("Worktrees").FontSize(16f).FontWeight(FontWeight.SemiBold));
        titleCol.AddChild(Text.New(repoName).FontSize(11.5f).TextColor(Nocturne.On(0.5))
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        bar.AddChild(titleCol);
        bar.AddChild(Ui.IconButton(Icons.WinClose, CloseWorktrees, "Close (Esc)", 30f).FlexShrink(0));
        return bar;
    }

    private Element BuildWorktreesToolbar()
    {
        var bar = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 14, 8, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        bar.AddChild(Ui.PrimaryButton("New worktree", Icons.FolderPlus, PromptNewWorktree, 12.5f));
        bar.AddChild(Ui.SecondaryButton("Prune", Icons.Broom, PruneWorktreesAction, 12.5f));
        bar.AddChild(Box.New().FlexGrow(1));
        return bar;
    }

    private static Element WorktreesPlaceholder(string text) =>
        Box.New().Padding(new Thickness(28)).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Text.New(text).FontSize(13f).TextColor(Nocturne.On(0.45)));

    private Element WorktreeRow(GitWorktree wt)
    {
        var isCurrent = PathEquals(wt.Path, _activeRepoPath);
        var name = System.IO.Path.GetFileName(wt.Path.TrimEnd('/', '\\'));

        var row = Flex.Row(gap: 11).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 10, 8, 10)).CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer);
        row.Key = "wt:" + wt.Path + (isCurrent ? "#c" : "");
        if (isCurrent)
            row.Background(Nocturne.Accent900).BorderColor(Nocturne.Accent).BorderWidth(new Thickness(0, 0, 0, 2));

        row.AddChild(Box.New().Width(26).Height(26).FlexShrink(0).CornerRadius(Nocturne.RadiusRow)
            .Background(Nocturne.Neutral800).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(wt.IsMain ? Icons.GitRepository : Icons.TreeStructure, 13f, Nocturne.Accent200)));

        var textCol = Flex.Column(gap: 1).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));
        var titleRow = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        titleRow.AddChild(Ui.Mono(name, 12.5f, Nocturne.Text).FlexShrink(1)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        titleRow.AddChild(Ui.Chip(Icons.GitBranch, wt.RefLabel, Nocturne.Neutral800, Nocturne.Neutral100).FlexShrink(0));
        if (isCurrent) titleRow.AddChild(Ui.TagOutline("current", Nocturne.Accent, 9f).FlexShrink(0));
        else if (wt.IsMain) titleRow.AddChild(Ui.TagOutline("main", Nocturne.On(0.5), 9f).FlexShrink(0));
        if (wt.IsLocked) titleRow.AddChild(Ui.TagOutline("locked", Nocturne.On(0.5), 9f).FlexShrink(0));
        if (wt.Prunable) titleRow.AddChild(Ui.TagOutline("prunable", Nocturne.DelCount, 9f).FlexShrink(0));
        textCol.AddChild(titleRow);
        textCol.AddChild(Text.New(wt.Path).FontSize(10.5f).TextColor(Nocturne.On(0.42))
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(textCol);

        var actions = Flex.Row(gap: 5).FlexShrink(0).AlignItems(AlignItems.Center);
        if (!isCurrent)
            actions.AddChild(Ui.SecondaryButton("Open", Icons.BendDownRight,
                () => OpenWorktree(wt.Path), 11.5f));
        // The main worktree can't be removed (it's the repo itself); the current
        // one git also refuses to remove. Both keep just the Open/badge affordance.
        if (!wt.IsMain && !isCurrent)
        {
            var trash = Ui.IconButton(Icons.Trash, () => RemoveWorktreeAction(wt.Path, force: false), "Remove", 30f);
            trash.OnClick(e => e.StopPropagation = true);
            actions.AddChild(trash);
        }
        row.AddChild(actions);

        row.OnClick(_ => { if (!isCurrent) OpenWorktree(wt.Path); });
        row.On<PointerDownEvent>(e => { if (e.Button == 2) MarkDirty(); });
        row.ContextMenu(() => WorktreeMenuItems(wt, isCurrent));
        if (!isCurrent) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    private IReadOnlyList<ContextMenuItem> WorktreeMenuItems(GitWorktree wt, bool isCurrent)
    {
        var items = new List<ContextMenuItem>();
        if (!isCurrent)
            items.Add(Item(Icons.BendDownRight, "Open in Junction", () => OpenWorktree(wt.Path)));
        items.Add(Item(Icons.Copy, "Copy path", () => CopyToClipboard(wt.Path)));
        if (!wt.IsMain && !isCurrent)
        {
            items.Add(ContextMenuItem.Separator());
            items.Add(Item(Icons.Trash, "Remove", () => RemoveWorktreeAction(wt.Path, force: false)));
            items.Add(Danger(Icons.Warning, "Force remove", () => RemoveWorktreeAction(wt.Path, force: true)));
        }
        return items;
    }

    // ── Actions ────────────────────────────────────────────────────────────

    /// <summary>Opens a linked worktree as a repository in Junction: registers its
    /// path (a worktree is just the same repo at another directory, which the rest
    /// of the app handles natively), adds it to the active workspace, and selects
    /// it. Closes the panel.</summary>
    private void OpenWorktree(string path)
    {
        if (!_settings.RepositoryPaths.Any(p => PathEquals(p, path)))
            _settings.RepositoryPaths.Add(path);
        var ws = ActiveWorkspace;
        if (ws != null && !ws.RepoPaths.Any(p => PathEquals(p, path)))
            ws.RepoPaths.Add(path);
        Persist();
        CloseWorktrees();
        SelectRepo(path);
        Flash($"Opened worktree {System.IO.Path.GetFileName(path.TrimEnd('/', '\\'))}");
    }

    private void RemoveWorktreeAction(string path, bool force)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.RemoveWorktree(path, force);
        if (res.Success)
            Flash($"Removed worktree {System.IO.Path.GetFileName(path.TrimEnd('/', '\\'))}");
        else
            Flash(FirstLine(res.Stderr, "Remove failed — worktree may have changes (try Force remove)"));
        RefreshAll();
    }

    private void PruneWorktreesAction()
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.PruneWorktrees();
        Flash(res.Success ? "Pruned stale worktrees" : FirstLine(res.Stderr, "Prune failed"));
        RefreshAll();
    }

    // ── New-worktree prompts ─────────────────────────────────────────────

    /// <summary>A sibling folder of the repo root, suffixed — the conventional
    /// place to put a worktree so it sits beside its repo.</summary>
    private string SuggestWorktreePath(string suffix)
    {
        var root = _activeRepoPath.TrimEnd('/', '\\');
        var parent = System.IO.Path.GetDirectoryName(root) ?? root;
        var name = System.IO.Path.GetFileName(root);
        var slug = Slug(suffix);
        return System.IO.Path.Combine(parent, $"{name}-{(slug.Length > 0 ? slug : "worktree")}");
    }

    private void PromptNewWorktree() => OpenDialog(new DialogSpec(
        "New worktree",
        "Creates a worktree in a new folder. By default it starts a new branch from the current HEAD; uncheck to check out HEAD detached.",
        "path/to/worktree",
        "Create",
        (path, newBranch) =>
        {
            if (ActiveRepo is not { } repo) return;
            var folder = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
            var res = newBranch
                ? repo.AddWorktree(path, committish: null, newBranch: folder)
                : repo.AddWorktree(path, committish: "HEAD");
            Flash(res.Success ? $"Created worktree {folder}" : FirstLine(res.Stderr, "Create worktree failed"));
            RefreshAll();
        },
        Validate: NonEmptyPath,
        CheckboxLabel: "Create a new branch (named after the folder)"),
        initial: SuggestWorktreePath("wt"));

    /// <summary>Branch context-menu entry: check out an existing branch in a new
    /// worktree (git forbids the same branch in two worktrees, so this only makes
    /// sense for a branch not already checked out).</summary>
    private void PromptWorktreeForBranch(string branch) => OpenDialog(new DialogSpec(
        "Worktree for branch",
        $"Checks out {branch} in a new worktree folder.",
        "path/to/worktree",
        "Create",
        (path, _) =>
        {
            if (ActiveRepo is not { } repo) return;
            var res = repo.AddWorktree(path, committish: branch);
            if (res.Success)
            {
                Flash($"Created worktree for {branch}");
                OpenWorktree(path);   // jump straight into the new checkout
            }
            else
            {
                Flash(FirstLine(res.Stderr, "Create worktree failed"));
            }
            RefreshAll();
        },
        Validate: NonEmptyPath),
        initial: SuggestWorktreePath(branch));

    private static string? NonEmptyPath(string p) =>
        string.IsNullOrWhiteSpace(p) ? "Enter a folder path" : null;
}

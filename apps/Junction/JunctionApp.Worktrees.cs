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

    /// <summary>Opens the panel on a repository other than the active one — the sidebar's
    /// repository menu, where the row you right-clicked is the one you meant.</summary>
    private void OpenWorktreesFor(string path)
    {
        SelectRepo(path);
        OpenWorktrees();
    }

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


    // ── Panel ────────────────────────────────────────────────────────────

    private Element BuildWorktreesPanel()
    {
        // The same load the sidebar runs for every repository it lists, under the same key,
        // so opening this panel on a repository whose worktrees are already on screen is a
        // cache hit rather than another `git worktree list`.
        var list = WorktreesFor(_activeRepoPath);
        var loading = _worktrees.IsLoading && list.Count == 0;

        var card = PanelCard(640, 560);

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

        return PanelBackdrop(card, CloseWorktrees);
    }

    private Element BuildWorktreesHeader() => PanelHeader(Icons.TreeStructure, "Worktrees", ActiveRepoName, CloseWorktrees);

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
        // Every flag that changes what the row renders belongs in the key: rows come and
        // go as worktrees are added and pruned, and a lock or a detach swaps a chip and a
        // whole line in or out at fixed positions — patched by position, they'd cross.
        row.Key = "wt:" + wt.Path
            + (isCurrent ? "#c" : "") + (wt.IsDetached ? "#d" : "")
            + (wt.IsLocked ? "#l" : "") + (wt.Prunable ? "#p" : "");
        if (isCurrent)
            row.Background(Nocturne.Accent900).BorderColor(Nocturne.Accent).BorderWidth(new Thickness(0, 0, 0, 2));

        row.AddChild(Box.New().Width(26).Height(26).FlexShrink(0).CornerRadius(Nocturne.RadiusRow)
            .Background(Nocturne.Neutral800).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(wt.IsMain ? Icons.GitRepository : Icons.TreeStructure, 13f, Nocturne.Accent200)));

        var textCol = Flex.Column(gap: 1).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));
        var titleRow = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        titleRow.AddChild(Ui.Mono(name, 12.5f, Nocturne.Text).FlexShrink(1)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        // Detached, the ref label is only ever the word "(detached)" — the short HEAD is
        // what actually says where the checkout sits, so the chip carries that instead.
        // (The row key carries the detached/locked flags, so this swap is a fresh row
        // rather than an in-place patch of one glyph over another.)
        titleRow.AddChild(wt.IsDetached
            ? Ui.Chip(Icons.GitCommit, Short(wt.Head), Nocturne.Neutral800, Nocturne.Neutral100).FlexShrink(0)
            : Ui.Chip(Icons.GitBranch, wt.RefLabel, Nocturne.Neutral800, Nocturne.Neutral100).FlexShrink(0));
        if (isCurrent) titleRow.AddChild(Ui.TagOutline("current", Nocturne.Accent, 9f).FlexShrink(0));
        else if (wt.IsMain) titleRow.AddChild(Ui.TagOutline("main", Nocturne.On(0.5), 9f).FlexShrink(0));
        if (wt.IsDetached) titleRow.AddChild(Ui.TagOutline("detached", Nocturne.On(0.5), 9f).FlexShrink(0));
        if (wt.IsLocked) titleRow.AddChild(Ui.TagOutline("locked", Nocturne.Accent2, 9f).FlexShrink(0));
        if (wt.Prunable) titleRow.AddChild(Ui.TagOutline("prunable", Nocturne.DelCount, 9f).FlexShrink(0));
        textCol.AddChild(titleRow);
        textCol.AddChild(Text.New(wt.Path).FontSize(10.5f).TextColor(Nocturne.On(0.42))
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        // A lock is the reason Remove refuses even with --force, so its note belongs on
        // screen rather than in a tooltip nobody hovers before clicking Remove.
        if (wt.IsLocked && wt.LockReason is { Length: > 0 } reason)
            textCol.AddChild(Text.New($"Locked — {reason}").FontSize(10.5f).TextColor(Nocturne.Accent2)
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(textCol);

        var actions = Flex.Row(gap: 5).FlexShrink(0).AlignItems(AlignItems.Center);
        if (!isCurrent)
        {
            // The row itself opens the worktree too, so without stopping here the button
            // ran the whole action twice — two Persist calls, two SelectRepo, two toasts.
            var open = Ui.SecondaryButton("Open", Icons.BendDownRight, () => OpenWorktree(wt.Path), 11.5f);
            open.OnClick(e => e.StopPropagation = true);
            actions.AddChild(open);
        }
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
        // (No right-click PointerDown handler here. The sidebar's rows have one because
        // right-clicking a repo there also selects it; these rows carry no selection, so
        // the copy of it only ever marked dirty for a rebuild the menu already causes.)
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
        // Locking is git's own answer to "don't prune this one" (a checkout on a drive
        // that isn't always mounted), and a locked worktree refuses to be removed even
        // with --force — so unlocking has to be reachable from the same menu as Remove.
        if (!wt.IsMain)
            items.Add(wt.IsLocked
                ? Item(Icons.Unlock, "Unlock", () => UnlockWorktreeAction(wt.Path))
                : Item(Icons.Lock, "Lock", () => PromptLockWorktree(wt.Path)));
        if (!wt.IsMain && !isCurrent)
        {
            items.Add(ContextMenuItem.Separator());
            items.Add(Item(Icons.Trash, "Remove", () => RemoveWorktreeAction(wt.Path, force: false)));
            items.Add(Danger(Icons.Warning, "Force remove", () => RemoveWorktreeAction(wt.Path, force: true)));
        }
        return items;
    }

    // ── Actions ────────────────────────────────────────────────────────────

    /// <summary>Switches the window to a linked worktree — the same repository at another
    /// directory, which the rest of the app handles natively. Closes the panel.
    ///
    /// <para>Deliberately registers nothing. A worktree used to be added to the workspace as
    /// a repository of its own, and the sidebar then carried two entries sharing one name,
    /// one history and one set of remotes — a repository that does not exist. The sidebar
    /// derives worktrees from <c>git worktree list</c> and nests them under the repository
    /// instead (see <see cref="SidebarRepoGroups"/>), so there is nothing to add here and
    /// nothing left over to remove afterwards.</para></summary>
    private void OpenWorktree(string path)
    {
        CloseWorktrees();
        SelectRepo(path);
        Flash($"Opened worktree {WorktreeName(path)}");
    }

    private static string WorktreeName(string path) =>
        System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));

    private void RemoveWorktreeAction(string path, bool force)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.RemoveWorktree(path, force);
        if (res.Success)
        {
            // The folder is gone from disk. Nothing registers a worktree any more, but a
            // settings file written by an older build still can, and a leftover row would
            // point at nothing — ForgetRepo is the same undo the sidebar's "Remove
            // repository" runs, reselection and all.
            ForgetRepo(path);
            Persist();
            Flash($"Removed worktree {WorktreeName(path)}");
        }
        else
            Flash(FirstLine(res.Stderr, "Remove failed — worktree may have changes (try Force remove)"));
        RefreshAll();
    }

    private void PruneWorktreesAction()
    {
        if (ActiveRepo is not { } repo) return;
        // Which linked worktrees git actually dropped, rather than which ones looked
        // prunable: a locked one stays even when its directory is gone, and only the
        // ones that really went may have their registrations taken with them.
        var before = repo.GetWorktrees();
        var res = repo.PruneWorktrees();
        if (res.Success)
        {
            var kept = new HashSet<string>(repo.GetWorktrees().Select(w => w.Path), PathComparer);
            foreach (var wt in before)
                if (!wt.IsMain && !kept.Contains(wt.Path))
                    ForgetRepo(wt.Path);   // same undo as Remove — see RemoveWorktreeAction
            Persist();   // one write for the whole batch
        }
        Flash(res.Success ? "Pruned stale worktrees" : FirstLine(res.Stderr, "Prune failed"));
        RefreshAll();
    }

    // ── Locking ──────────────────────────────────────────────────────────

    private void PromptLockWorktree(string path) => OpenDialog(new DialogSpec(
        "Lock worktree",
        "A locked worktree is never pruned and refuses to be removed. The reason is optional — git shows it beside the worktree.",
        "on the external drive",
        "Lock",
        (reason, _) =>
        {
            if (ActiveRepo is not { } repo) return;
            var res = repo.LockWorktree(path, reason);
            Flash(res.Success
                ? $"Locked {System.IO.Path.GetFileName(path.TrimEnd('/', '\\'))}"
                : FirstLine(res.Stderr, "Lock failed"));
            RefreshAll();
        }));

    private void UnlockWorktreeAction(string path)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.UnlockWorktree(path);
        Flash(res.Success
            ? $"Unlocked {System.IO.Path.GetFileName(path.TrimEnd('/', '\\'))}"
            : FirstLine(res.Stderr, "Unlock failed"));
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

    /// <summary>Resolves a typed folder the way git itself would — <c>~</c> expanded,
    /// relative paths taken against the repository root, since that is the working
    /// directory the worktree command runs in. Everything downstream (registering the
    /// path, matching it against <c>GetWorktrees</c>) compares full OS-native paths, so a
    /// verbatim "../side" would never match the worktree it just created.</summary>
    private string ResolveWorktreePath(string path)
    {
        try
        {
            return System.IO.Path.TrimEndingDirectorySeparator(
                System.IO.Path.GetFullPath(ExpandPath(path.Trim()), _activeRepoPath));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            // Not something the OS can even shape into a path (a stray '|', a name past the
            // length limit). Hand it back as typed: git's own refusal names the folder,
            // which is a better message than an exception out of a validator.
            return path.Trim();
        }
    }

    private void PromptNewWorktree() => OpenDialog(new DialogSpec(
        "New worktree",
        "Creates a worktree in a new folder. By default it starts a new branch from the current HEAD; uncheck to check out HEAD detached.",
        "path/to/worktree",
        "Create",
        (path, newBranch) =>
        {
            if (ActiveRepo is not { } repo) return;
            var full = ResolveWorktreePath(path);
            var folder = System.IO.Path.GetFileName(full);
            var res = newBranch
                ? repo.AddWorktree(full, committish: null, newBranch: folder)
                : repo.AddWorktree(full, committish: "HEAD");
            Flash(res.Success ? $"Created worktree {folder}" : FirstLine(res.Stderr, "Create worktree failed"));
            RefreshAll();
        },
        Validate: ValidateNewWorktree,
        CheckboxLabel: "Create a new branch (named after the folder)"),
        initial: SuggestWorktreePath("wt"));

    /// <summary>The folder has to be a path, and — while the checkbox is on — also has to
    /// work as a branch name, because that is what <c>worktree add -b</c> receives. Checked
    /// here so the prompt stays open with a sentence naming the offending character,
    /// instead of closing and letting git answer "not a valid branch name". Reads the live
    /// checkbox, so clearing it lifts the branch rule.</summary>
    private string? ValidateNewWorktree(string path)
    {
        if (NonEmptyPath(path) is { } bad) return bad;
        if (!_dialogChecked) return null;
        var folder = System.IO.Path.GetFileName(ResolveWorktreePath(path));
        return BranchName.Validate(folder) is { } err
            ? $"{err} — the new branch takes the folder's name"
            : null;
    }

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
            // Full path, not the typed one: OpenWorktree registers whatever it is given,
            // and a stored relative path would never PathEquals-match the absolute one
            // the panel and the sidebar compare against.
            var full = ResolveWorktreePath(path);
            var res = repo.AddWorktree(full, committish: branch);
            if (res.Success)
            {
                Flash($"Created worktree for {branch}");
                OpenWorktree(full);   // jump straight into the new checkout
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

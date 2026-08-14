using HultUI.Core.Elements;
using Junction.Git;

using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Icons;

namespace Junction;

public partial class JunctionApp
{
    // ── Item builders ────────────────────────────────────────────────────

    /// <summary>Phosphor artwork for a menu row, tinted to whatever colour the menu
    /// resolved for that item's state (normal, disabled, danger).</summary>
    private static Func<Color, IImageSource?> MenuIcon(string name) =>
        color => Phosphor.Source(name, Ui.IconBox(14f), color, Ui.DisplayScale);

    // Every builder carries the Phosphor name as well as the artwork factory. The
    // framework's context menu paints its own rows and uses Icon; the app's own
    // left-click popover (BuildPopover) builds elements and needs the name to draw
    // the same glyph. Icon takes precedence in the framework menu, so setting both
    // costs nothing and keeps the two menu surfaces showing the same artwork —
    // without it the popover rows drew a blank icon column.
    private static ContextMenuItem Item(string glyph, string label, Action run) =>
        new() { Icon = MenuIcon(glyph), Glyph = glyph, Label = label, Action = run };

    private static ContextMenuItem Danger(string glyph, string label, Action run) =>
        new() { Icon = MenuIcon(glyph), Glyph = glyph, Label = label, Action = run, IsDanger = true };

    /// <summary>An entry that stays in the menu but cannot be chosen — for an action that
    /// belongs here and is unavailable right now, where dropping the row instead would only
    /// leave the reader wondering whether the feature exists.</summary>
    private static ContextMenuItem Unavailable(string glyph, string label) =>
        new() { Icon = MenuIcon(glyph), Glyph = glyph, Label = label, IsDisabled = true };

    private static ContextMenuItem SubMenu(string glyph, string label, IReadOnlyList<ContextMenuItem> children) =>
        new() { Icon = MenuIcon(glyph), Glyph = glyph, Label = label, Children = children };

    private ContextMenuItem IncludeItem(string name)
    {
        var set = IncludeSet(_activeRepoPath);
        var included = set.Count == 0 || set.Contains(name);
        return Item(included ? Icons.EyeSlash : Icons.Eye,
            included ? "Exclude from history" : "Include in history",
            () => ToggleInclude(name));
    }

    private string CurrentBranchTitle() =>
        BranchName.Parse(SummaryFor(_activeRepoPath).Branch).Title;

    // ── Commit menu ──────────────────────────────────────────────────────

    private IReadOnlyList<ContextMenuItem> CommitMenuItems(GitCommit c)
    {
        var cur = CurrentBranchTitle();

        // Right-clicking inside a multi-selection targets the whole selection
        // for the operations that generalize (cherry-pick, drop, patch);
        // everything else acts on the clicked commit.
        var sel = SelectionContains(c.Hash) && _selectedCommitHashes.Count > 1
            ? SelectedHashesOldestFirst()
            : [c.Hash];
        var n = sel.Count;
        var multi = n > 1;

        // Quick rebase: scripted history edits that need no further input.
        var quick = new List<ContextMenuItem>();
        if (!multi)
        {
            quick.Add(Item(Icons.ArrowsInLineVertical, "Squash into parent", () => DoQuickMeld(c, RebaseOp.Squash)));
            quick.Add(Item(Icons.ArrowLineUp, "Fixup into parent (discard message)", () => DoQuickMeld(c, RebaseOp.Fixup)));
            quick.Add(ContextMenuItem.Separator());
        }
        quick.Add(Danger(Icons.Trash, multi ? $"Drop {n} commits" : "Drop commit", () => DoQuickDrop(sel)));

        return
        [
            Item(Icons.BendDownRight, "Checkout this commit (detached)", () => DoCheckoutDetached(c.Hash)),
            Item(Icons.GitBranch, "Create branch here…", () => PromptCreateBranchAt(c.Hash, c.ShortHash)),
            Item(Icons.Tag, "Create tag here…", () => PromptCreateTagAt(c.Hash, c.ShortHash)),
            ContextMenuItem.Separator(),
            Item(Icons.Cherries, multi ? $"Cherry-pick {n} commits onto {cur}" : $"Cherry-pick onto {cur}",
                () => DoCherryPick(sel)),
            Item(Icons.ArrowsDownUp, $"Rebase {cur} onto this", () => DoRebase(c.Hash)),
            SubMenu(Icons.Lightning, "Quick rebase", quick),
            Item(Icons.ListChecks, "Interactive rebase from here…", () => OpenRebase(c)),
            Item(Icons.CounterClockwise, "Revert commit", () => DoRevert(c.Hash)),
            ContextMenuItem.Separator(),
            Item(Icons.LineLeft, $"Reset {cur} soft", () => DoReset(c.Hash, ResetMode.Soft)),
            Item(Icons.LineLeft, "Reset mixed", () => DoReset(c.Hash, ResetMode.Mixed)),
            Danger(Icons.Warning, "Reset hard", () => DoReset(c.Hash, ResetMode.Hard)),
            ContextMenuItem.Separator(),
            Item(Icons.FileArrowDown, multi ? $"Save {n} commits as patch…" : "Save as patch…",
                () => SavePatch(sel)),
            Item(Icons.Copy, "Copy commit SHA", () => CopyToClipboard(c.Hash)),
        ];
    }

    /// <summary>Selected commit hashes ordered oldest→newest (the history list
    /// is newest-first).</summary>
    private List<string> SelectedHashesOldestFirst()
    {
        var list = new List<string>();
        foreach (var c in _history.Value.Commits)
            if (_selectedCommitHashes.Contains(c.Hash))
                list.Add(c.Hash);
        list.Reverse();
        return list;
    }

    // ── Branch menu (adapts to current / remote) ─────────────────────────

    private IReadOnlyList<ContextMenuItem> BranchMenuItems(GitBranch b)
    {
        var cur = CurrentBranchTitle();
        var (_, leaf) = b.IsRemote ? SplitRemote(b.Name) : ("", b.Name);
        var title = BranchName.Parse(leaf).Title;
        var items = new List<ContextMenuItem>();

        // A remote-tracking ref: only the operations that make sense off a remote.
        if (b.IsRemote)
        {
            items.Add(Item(Icons.BendDownRight, "Checkout", () => CheckoutBranch(leaf)));
            items.Add(Item(Icons.GitBranch, $"Create branch from {title}…", () => PromptCreateBranchAt(b.Name, title)));
            items.Add(ContextMenuItem.Separator());
            items.Add(IncludeItem(b.Name));
            items.Add(Item(Icons.Copy, "Copy branch name", () => CopyToClipboard(b.Name)));
            return items;
        }

        if (!b.IsCurrent)
            items.Add(Item(Icons.BendDownRight, "Checkout", () => CheckoutBranch(b.Name)));
        items.Add(Item(Icons.GitBranch, $"Create branch from {title}…", () => PromptCreateBranchAt(b.Name, title)));
        items.Add(Item(Icons.PencilSimple, "Rename branch…", () => PromptRenameBranch(b.Name)));
        // A branch already checked out (the current one) can't go in a second
        // worktree, so only offer this for the others.
        if (!b.IsCurrent)
            items.Add(Item(Icons.TreeStructure, "Create worktree for this branch…", () => PromptWorktreeForBranch(b.Name)));

        if (!b.IsCurrent)
        {
            items.Add(ContextMenuItem.Separator());
            items.Add(Item(Icons.GitMerge, $"Merge into {cur}", () => DoMerge(b.Name)));
            items.Add(Item(Icons.ArrowsDownUp, $"Rebase {cur} onto {title}", () => DoRebase(b.Name)));
        }

        items.Add(ContextMenuItem.Separator());
        items.Add(Item(Icons.ArrowUp, "Push", () => PushBranch(b)));
        items.Add(Item(Icons.ArrowDown, "Pull", () => PullBranch(b)));
        items.Add(Item(Icons.Cloud, "Set upstream…", () => PromptSetUpstream(b.Name)));

        items.Add(ContextMenuItem.Separator());
        items.Add(IncludeItem(b.Name));
        items.Add(Item(Icons.Copy, "Copy branch name", () => CopyToClipboard(b.Name)));

        if (!b.IsCurrent)
        {
            items.Add(ContextMenuItem.Separator());
            items.Add(Danger(Icons.Trash, "Delete branch", () => DeleteBranchAction(b)));
        }
        return items;
    }

    // ── Direct git actions (dialog-less menu items) ──────────────────────

    private void DoCheckoutDetached(string hash)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.CheckoutDetached(hash);
        Flash(res.Success ? $"Checked out {Short(hash)} (detached)" : FirstLine(res.Stderr, "Checkout failed"));
        RefreshAll();
    }

    private void DoCherryPick(IReadOnlyList<string> hashesOldestFirst)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.CherryPick([.. hashesOldestFirst]);
        Flash(res.Success
            ? hashesOldestFirst.Count > 1
                ? $"Cherry-picked {hashesOldestFirst.Count} commits"
                : $"Cherry-picked {Short(hashesOldestFirst[0])}"
            : FirstLine(res.Stderr, "Cherry-pick failed"));
        RefreshAll();
    }

    // ── Patch export ─────────────────────────────────────────────────────

    /// <summary>"Save as patch…": an am-compatible <c>git format-patch</c> series.
    /// A multi-selection spans oldest→newest inclusive — the same span the
    /// compare view shows, with each commit as its own patch section.</summary>
    private async void SavePatch(IReadOnlyList<string> selOldestFirst)
    {
        if (ActiveRepo is not { } repo) return;
        if (!_saveFilePicker.IsAvailable) { Flash("No native save dialog available"); return; }

        var oldest = selOldestFirst[0];
        var newest = selOldestFirst[^1];
        var single = selOldestFirst.Count == 1;
        // If the span starts at the initial commit there is no oldest^ to diff from,
        // so ask format-patch to run --root (patches back to the first commit).
        var oldestIsRoot = _history.Value.Commits.FirstOrDefault(x => x.Hash == oldest) is { ParentHashes.Length: 0 };
        var slug = Slug(_history.Value.Commits.FirstOrDefault(x => x.Hash == newest)?.Subject ?? "");
        var name = single
            ? slug.Length > 0 ? $"{Short(newest)}-{slug}.patch" : $"{Short(newest)}.patch"
            : $"{Short(oldest)}..{Short(newest)}.patch";

        var path = await _saveFilePicker.PickSaveFileAsync(name, repo.Path, "Patch files", "patch");
        if (path == null) return;

        var error = await Task.Run(() =>
        {
            var text = repo.FormatPatch(single ? null : oldest + "^", newest, fromRoot: oldestIsRoot);
            if (text == null) return "git format-patch failed";
            try { File.WriteAllText(path, text); return null; }
            catch (Exception ex) { return ex.Message; }
        });
        Flash(error ?? $"Saved patch to {System.IO.Path.GetFileName(path)}");
    }

    // ── Quick rebase (scripted history edits, no dialog) ─────────────────

    /// <summary>Drops the given commits from the current branch via a scripted
    /// interactive rebase from the oldest one's parent.</summary>
    private async void DoQuickDrop(IReadOnlyList<string> hashesOldestFirst)
    {
        if (ActiveRepo is not { } repo) return;
        var set = new HashSet<string>(hashesOldestFirst, StringComparer.Ordinal);
        var baseRef = hashesOldestFirst[0] + "^";
        var n = set.Count;

        var msg = await Task.Run(() =>
        {
            var range = repo.GetRebaseRange(baseRef);
            if (range.Count == 0)
                return "Cannot drop — commit has no parent or is not on the current branch";
            if (hashesOldestFirst.Any(h => range.All(x => x.Hash != h)))
                return "Cannot drop — selected commits are not all on the current branch";

            var plan = range.Select(x => new RebaseEntry
            {
                Hash = x.Hash, ShortHash = x.ShortHash, Subject = x.Subject,
                Op = set.Contains(x.Hash) ? RebaseOp.Drop : RebaseOp.Pick,
            }).ToList();
            var res = repo.InteractiveRebase(baseRef, plan);
            return res.Success
                ? n == 1 ? $"Dropped {Short(hashesOldestFirst[0])}" : $"Dropped {n} commits"
                : FirstLine(res.Stderr, "Rebase stopped — resolve conflicts or abort");
        });
        Flash(msg);
        RefreshAll();
    }

    /// <summary>Squash/fixup a commit into its parent without opening the rebase
    /// dialog. Squash keeps both messages; fixup keeps only the parent's.</summary>
    private async void DoQuickMeld(GitCommit c, RebaseOp op)
    {
        if (ActiveRepo is not { } repo) return;
        var baseRef = c.Hash + "~2";   // rebase from the grandparent: parent stays, c melds into it

        var msg = await Task.Run(() =>
        {
            var range = repo.GetRebaseRange(baseRef);
            if (range.Count < 2 || range.All(x => x.Hash != c.Hash))
                return "Cannot squash — commit needs a parent on the current branch";

            var plan = range.Select(x => new RebaseEntry
            {
                Hash = x.Hash, ShortHash = x.ShortHash, Subject = x.Subject,
                Op = x.Hash == c.Hash ? op : RebaseOp.Pick,
            }).ToList();
            var res = repo.InteractiveRebase(baseRef, plan);
            return res.Success
                ? op == RebaseOp.Squash
                    ? $"Squashed {c.ShortHash} into its parent"
                    : $"Fixed up {c.ShortHash} into its parent"
                : FirstLine(res.Stderr, "Rebase stopped — resolve conflicts or abort");
        });
        Flash(msg);
        RefreshAll();
    }

    private void DoRebase(string onto)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.Rebase(onto);
        Flash(res.Success ? $"Rebased onto {Short(onto)}" : FirstLine(res.Stderr, "Rebase failed"));
        RefreshAll();
    }

    private void DoRevert(string hash)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.Revert(hash);
        Flash(res.Success ? $"Reverted {Short(hash)}" : FirstLine(res.Stderr, "Revert failed"));
        RefreshAll();
    }

    private void DoReset(string hash, ResetMode mode)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.Reset(hash, mode);
        Flash(res.Success ? $"Reset {mode.ToString().ToLowerInvariant()} to {Short(hash)}"
                          : FirstLine(res.Stderr, "Reset failed"));
        RefreshAll();
    }

    private void DoMerge(string branch)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.Merge(branch);
        if (res.Success)
            Flash($"Merged {branch}");
        else if (repo.IsMerging())
        {
            // Conflicts — drop the user into the Changes view to resolve them.
            Flash($"Merge conflicts in {branch} — resolve below");
            SetView(JunctionView.Changes);
        }
        else
            Flash(FirstLine(res.Stderr, "Merge failed"));
        RefreshAll();
    }

    private static string Short(string hash) => hash.Length > 7 ? hash[..7] : hash;
}

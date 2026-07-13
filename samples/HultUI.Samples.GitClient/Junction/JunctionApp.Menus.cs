using HultUI.Core.Elements;
using GitClient.Git;

namespace GitClient.Junction;

public partial class JunctionApp
{
    // ── Item builders ────────────────────────────────────────────────────

    private static ContextMenuItem Item(string glyph, string label, Action run) =>
        new() { Glyph = glyph, Label = label, Action = run };

    private static ContextMenuItem Danger(string glyph, string label, Action run) =>
        new() { Glyph = glyph, Label = label, Action = run, IsDanger = true };

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
        return
        [
            Item(Icons.BendDownRight, "Checkout this commit (detached)", () => DoCheckoutDetached(c.Hash)),
            Item(Icons.GitBranch, "Create branch here…", () => PromptCreateBranchAt(c.Hash, c.ShortHash)),
            Item(Icons.Tag, "Create tag here…", () => PromptCreateTagAt(c.Hash, c.ShortHash)),
            ContextMenuItem.Separator(),
            Item(Icons.Cherries, $"Cherry-pick onto {cur}", () => DoCherryPick(c.Hash)),
            Item(Icons.ArrowsDownUp, $"Rebase {cur} onto this", () => DoRebase(c.Hash)),
            Item(Icons.CounterClockwise, "Revert commit", () => DoRevert(c.Hash)),
            ContextMenuItem.Separator(),
            Item(Icons.LineLeft, $"Reset {cur} soft", () => DoReset(c.Hash, ResetMode.Soft)),
            Item(Icons.LineLeft, "Reset mixed", () => DoReset(c.Hash, ResetMode.Mixed)),
            Danger(Icons.Warning, "Reset hard", () => DoReset(c.Hash, ResetMode.Hard)),
            ContextMenuItem.Separator(),
            Item(Icons.Copy, "Copy commit SHA", () => CopyToClipboard(c.Hash)),
        ];
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

    private void DoCherryPick(string hash)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.CherryPick(hash);
        Flash(res.Success ? $"Cherry-picked {Short(hash)}" : FirstLine(res.Stderr, "Cherry-pick failed"));
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
        Flash(res.Success ? $"Merged {branch}" : FirstLine(res.Stderr, "Merge failed"));
        RefreshAll();
    }

    private static string Short(string hash) => hash.Length > 7 ? hash[..7] : hash;
}

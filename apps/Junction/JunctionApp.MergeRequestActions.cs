using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.GitLab;
using Junction.Theme;

namespace Junction;

/// <summary>
/// Acting on a merge request from its detail page — design §C's action row: approve or
/// revoke, merge now or when the pipeline succeeds (behind a confirm that quotes the
/// target's protection and carries the squash and delete-source choices), rebase when
/// GitLab asks for one, and the draft toggle. Every busy state is a spinner in its own
/// button, and a refusal is one line under the row rather than a toast that goes away.
/// </summary>
public partial class JunctionApp
{
    /// <summary>Which button is busy — <c>checkout · approve · merge · rebase · draft</c> —
    /// so the others stay usable and only that one spins.</summary>
    private string? _mrBusyAction;
    /// <summary>What GitLab last refused, shown under the action row until the next action
    /// or a page change. 409 says refresh, 405 says why, 403 says scope, 404 says moved.</summary>
    private string? _mrActionFailure;

    /// <summary>The detail page's action row: checkout and review on the left, the request's
    /// own verbs after a divider, and the merge's reason inline when it cannot merge.</summary>
    private Element MrActionRow(string path, long iid, GlMergeRequest mr, GlApprovals? approvals)
    {
        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(12, 18, 12, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));

        var checkout = _mrBusy is { } busy && _mrBusyAction is "checkout"
            ? DisabledButton(busy, Icons.CircleNotch)
            : Ui.PrimaryButton("Checkout locally", Icons.BendDownRight, () => CheckoutMergeRequest(path, mr), 12.5f);
        checkout.Key = "mr-checkout-" + (_mrBusyAction is "checkout" ? "off" : "on");
        checkout.AccessibleName = "Checkout locally";
        row.AddChild(checkout);
        var review = Ui.SecondaryButton("Review", Icons.Eyes, () => StartReview(path, iid), 12.5f)
            .Tooltip("Open the diff with its threads and the review gutter (r)");
        review.AccessibleName = "Review";
        row.AddChild(review);

        row.AddChild(Box.New().Width(1).Height(22).Background(Nocturne.Divider).FlexShrink(0));

        // Approve / Revoke. Hidden only when GitLab says this user cannot approve at all.
        if (approvals is not { UserCanApprove: false, UserHasApproved: false })
        {
            bool mine = approvals?.UserHasApproved == true;
            row.AddChild(ActionButton("approve", mine ? "Revoke approval" : "Approve", mine ? null : Icons.SealCheck,
                () => ToggleApproval(path, mr, approvals), mine ? "Take your approval back (a)" : "Approve this request (a)"));
        }

        // Merge ▾ — with the reason inline when it cannot.
        var (mergeNow, whenPipeline, reason) = MergeOptions(mr);
        if (mergeNow || whenPipeline)
        {
            var merge = ActionButton("merge", "Merge", Icons.CaretDown, () => { }, "Merge now, or when the pipeline succeeds", primary: true);
            if (_mrBusyAction is not "merge")
                merge.On<ClickEvent>(e =>
                {
                    e.StopPropagation = true;
                    var box = e.CurrentTarget.Layout.AbsoluteBorderBox;
                    _popoverX = Math.Max(8f, box.X);
                    _popoverY = box.Y - 4f - (mergeNow && whenPipeline ? 2 : 1) * 34f;
                    var items = new List<ContextMenuItem>();
                    if (mergeNow) items.Add(Item(Icons.GitMerge, "Merge now", () => ConfirmMerge(path, mr, whenPipelineSucceeds: false)));
                    if (whenPipeline) items.Add(Item(Icons.Clock, "Merge when pipeline succeeds", () => ConfirmMerge(path, mr, whenPipelineSucceeds: true)));
                    _popover = items;
                    MarkDirty();
                });
            row.AddChild(merge);
        }
        else
        {
            var off = DisabledButton("Merge", Icons.GitMerge);
            off.Key = "mr-merge-off";
            off.AccessibleName = "Merge";
            row.AddChild(off);
            if (reason.Length > 0)
            {
                var why = Text.New(reason).FontSize(11.5f).TextColor(Nocturne.On(0.5)).FlexShrink(1).MaxLines(1).OverflowMode(TextOverflow.Ellipsis);
                why.AutomationId = "mr-merge-reason";
                row.AddChild(why);
            }
        }

        if (mr.DetailedMergeStatus == "need_rebase")
            row.AddChild(ActionButton("rebase", "Rebase", Icons.ArrowsClockwise, () => RebaseRequest(path, mr),
                $"Rebase onto {mr.TargetBranch} on GitLab"));

        row.AddChild(ActionButton("draft", mr.Draft ? "Mark as ready" : "Mark as draft", null, () => ToggleDraft(path, mr),
            mr.Draft ? "Remove the Draft: prefix so it can merge" : "Add the Draft: prefix so it cannot merge yet"));

        row.AddChild(Box.New().FlexGrow(1));
        return row;
    }

    /// <summary>A secondary button that turns into a spinner while its own action runs.</summary>
    private BoxElement ActionButton(string action, string label, string? glyph, Action run, string tooltip, bool primary = false)
    {
        BoxElement b;
        if (_mrBusyAction == action && _mrBusy is { } busy)
        {
            b = (BoxElement)DisabledButton(busy, Icons.CircleNotch);
        }
        else
        {
            b = primary ? Ui.PrimaryButton(label, glyph, run, 12.5f) : Ui.SecondaryButton(label, glyph, run, 12.5f);
            b.Tooltip(tooltip);
        }
        b.Key = $"mr-{action}-" + (_mrBusyAction == action ? "off" : "on");
        b.AccessibleName = label;
        b.AutomationId = $"mr-{action}";
        return b;
    }

    /// <summary>What the merge menu can offer: merging now needs GitLab's <c>mergeable</c>;
    /// a running pipeline allows only the deferred merge; anything else is refused with the
    /// status's word as the reason. Auto-merge already set disables the button too.</summary>
    private static (bool Now, bool WhenPipeline, string Reason) MergeOptions(GlMergeRequest mr)
    {
        if (!mr.IsOpen) return (false, false, "Closed");
        if (mr.MergeWhenPipelineSucceeds) return (false, false, "Auto-merge set");
        return mr.DetailedMergeStatus switch
        {
            "mergeable" => (true, true, ""),
            "ci_still_running" => (false, true, ""),
            _ => (false, false, StatusWord(mr).Word),
        };
    }

    /// <summary>The submit menu's verbs without the popover, for tests.</summary>
    internal void ChooseMerge(bool whenPipelineSucceeds)
    {
        if (_mrOpen is { } o && MergeRequestsFor(o.Path).Value.FirstOrDefault(m => m.Iid == o.Iid) is { } mr)
            ConfirmMerge(o.Path, mr, whenPipelineSucceeds);
    }

    private void ConfirmMerge(string path, GlMergeRequest mr, bool whenPipelineSucceeds)
    {
        var project = ProjectFor(path).Value?.Project;
        var host = GitLabRemoteFor(path)?.Host ?? "GitLab";
        string? info = ProtectionFor(path, mr.TargetBranch) is { } rule ? ProtectionSummary(rule) : null;
        bool squash = mr.Squash ?? project?.SquashByDefault ?? false;
        bool remove = mr.ShouldRemoveSourceBranch ?? mr.ForceRemoveSourceBranch ?? project?.RemoveSourceBranchAfterMerge ?? false;
        OpenDialog(new DialogSpec(
            Title: $"Merge !{mr.Iid} into {mr.TargetBranch}",
            Body: whenPipelineSucceeds
                ? $"{mr.SourceBranch} → {mr.TargetBranch}, as soon as the pipeline on {Short7(mr.Sha ?? "")} passes. {host} does the merge."
                : $"{mr.SourceBranch} → {mr.TargetBranch}. {host} does the merge at {Short7(mr.Sha ?? "")}; a head that has moved since is refused.",
            Placeholder: null,
            ConfirmLabel: whenPipelineSucceeds ? "Merge when pipeline succeeds" : "Merge now",
            OnConfirm: (_, _) => { },
            CheckboxLabel: "Squash commits",
            CheckboxInitial: squash,
            Info: info,
            SecondCheckboxLabel: "Delete source branch when merged",
            SecondCheckboxInitial: remove,
            OnConfirm2: (_, doSquash, doRemove) => DoMerge(path, mr, doSquash, doRemove, whenPipelineSucceeds)));
    }

    private async void DoMerge(string path, GlMergeRequest mr, bool squash, bool removeSource, bool whenPipelineSucceeds)
    {
        if (!BeginAction("merge", whenPipelineSucceeds ? "Setting auto-merge…" : "Merging…", path, out var client, out var remote)) return;
        var (failure, merged) = await Task.Run(() =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return (new GitLabFailure(GitLabFailureKind.NotFound, $"Project not found on {remote.Host}"), false);
            var r = Sync(client.MergeAsync(id.Value, mr.Iid, mr.Sha, squash, removeSource, whenPipelineSucceeds));
            // "When the pipeline succeeds" on a pipeline that already has merges at once:
            // GitLab answers with the merged request, and the page has nothing left to show.
            return r.Ok ? (null, r.Value.State == "merged") : (r.Failure, false);
        });
        EndAction(path, failure, remote.Host, merged || !whenPipelineSucceeds
            ? $"Merged !{mr.Iid} into {mr.TargetBranch}"
            : $"!{mr.Iid} merges when its pipeline succeeds");
        if (failure is null && (merged || !whenPipelineSucceeds))
        {
            BumpDataGen(path);
            if (_mrOpen is { } open && open.Iid == mr.Iid) BackToMrList();
        }
    }

    private async void ToggleApproval(string path, GlMergeRequest mr, GlApprovals? approvals)
    {
        bool revoke = approvals?.UserHasApproved == true;
        if (!BeginAction("approve", revoke ? "Revoking…" : "Approving…", path, out var client, out var remote)) return;
        var failure = await Task.Run(() =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return new GitLabFailure(GitLabFailureKind.NotFound, $"Project not found on {remote.Host}");
            if (revoke) { var u = Sync(client.UnapproveAsync(id.Value, mr.Iid)); return u.Ok ? null : u.Failure; }
            var a = Sync(client.ApproveAsync(id.Value, mr.Iid, mr.Sha));
            return a.Ok ? null : a.Failure;
        });
        EndAction(path, failure, remote.Host, revoke ? $"Approval of !{mr.Iid} revoked" : $"Approved !{mr.Iid}");
    }

    /// <summary>Asks GitLab to rebase, then waits for it: the call returns at once and the
    /// request's <c>rebase_in_progress</c> says when it is done; <c>merge_error</c> says
    /// whether it could not.</summary>
    private async void RebaseRequest(string path, GlMergeRequest mr)
    {
        if (!BeginAction("rebase", "Rebasing…", path, out var client, out var remote)) return;
        var failure = await Task.Run(() =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return new GitLabFailure(GitLabFailureKind.NotFound, $"Project not found on {remote.Host}");
            var r = Sync(client.RebaseAsync(id.Value, mr.Iid));
            if (!r.Ok) return r.Failure;
            for (int i = 0; i < 40; i++)
            {
                Thread.Sleep(i == 0 ? 500 : 1500);
                var m = Sync(client.GetMergeRequestAsync(id.Value, mr.Iid));
                if (!m.Ok) return m.Failure;
                if (m.Value.RebaseInProgress == true) continue;
                return m.Value.MergeError is { Length: > 0 } err
                    ? new GitLabFailure(GitLabFailureKind.Conflict, err)
                    : null;
            }
            return new GitLabFailure(GitLabFailureKind.Timeout, $"{remote.Host} is still rebasing — check again in a moment");
        });
        EndAction(path, failure, remote.Host, $"Rebased !{mr.Iid} onto {mr.TargetBranch}");
    }

    private async void ToggleDraft(string path, GlMergeRequest mr)
    {
        var title = mr.Draft ? StripDraftPrefix(mr.Title) : "Draft: " + mr.Title;
        if (!BeginAction("draft", mr.Draft ? "Marking ready…" : "Marking draft…", path, out var client, out var remote)) return;
        var failure = await Task.Run(() =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return new GitLabFailure(GitLabFailureKind.NotFound, $"Project not found on {remote.Host}");
            var r = Sync(client.UpdateTitleAsync(id.Value, mr.Iid, title));
            return r.Ok ? null : r.Failure;
        });
        EndAction(path, failure, remote.Host, mr.Draft ? $"!{mr.Iid} marked as ready" : $"!{mr.Iid} marked as draft");
    }

    /// <summary>The prefixes GitLab treats as the draft flag, in the forms it accepts.</summary>
    internal static string StripDraftPrefix(string title)
    {
        var t = title.TrimStart();
        foreach (var prefix in new[] { "Draft:", "[Draft]", "(Draft)", "WIP:", "[WIP]" })
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return t[prefix.Length..].TrimStart();
        return title;
    }

    private bool BeginAction(string action, string busyLabel, string path, out GitLabClient client, out GitLabRemote remote)
    {
        client = null!;
        remote = null!;
        if (_mrBusy != null) return false;
        if (GitLabRemoteFor(path) is not { } r || _gitlab.ClientFor(r.Host) is not { } c) return false;
        client = c;
        remote = r;
        _mrBusy = busyLabel;
        _mrBusyAction = action;
        _mrActionFailure = null;
        MarkDirty();
        return true;
    }

    /// <summary>Ends the action: a failure stays on the page under the row; success says so
    /// in a toast and reloads the hosted data so the cards and the row catch up.</summary>
    private void EndAction(string path, GitLabFailure? failure, string host, string success)
    {
        _mrBusy = null;
        _mrBusyAction = null;
        if (failure != null)
        {
            _mrActionFailure = failure.Kind switch
            {
                GitLabFailureKind.NotAllowed => "GitLab refused: " + failure.Message,
                GitLabFailureKind.Conflict => "The request changed under you — refresh and try again.",
                GitLabFailureKind.Forbidden => "Your token needs the api scope, or your role does not allow this.",
                GitLabFailureKind.NotFound => $"Merge request not found on {host} — was it moved?",
                _ => failure.UserMessage(host, "Merge request"),
            };
            MarkDirty();
            return;
        }
        Flash(success);
        RefreshHosted(path);
    }

    /// <summary>The line under the action row, when GitLab refused the last action.</summary>
    private Element? MrActionFailureLine()
    {
        if (_mrActionFailure is not { } text) return null;
        var line = Flex.Row(gap: 6).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(0, 18, 8, 18));
        line.AddChild(Ui.Icon(Icons.XCircle, 12f, Nocturne.DangerText).FlexShrink(0));
        line.AddChild(Text.New(text).FontSize(11.5f).TextColor(Nocturne.DangerText).FlexShrink(1).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
        line.AutomationId = "mr-action-failure";
        return line;
    }
}

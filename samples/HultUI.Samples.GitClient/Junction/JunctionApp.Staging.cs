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
    // ── Interactive line/hunk staging in the Changes diff pane ────────────
    //
    // Click a changed line to (de)select it, click a hunk header to toggle the
    // whole hunk; the action bar under the diff header stages / unstages /
    // discards the selection by synthesizing a patch (see GitPatchBuilder) and
    // applying it with `git apply` (full-context, so no --unidiff-zero).

    /// <summary>Which file/side the diff pane is showing interactively. Staged=true
    /// ⇒ the staged side (selection unstages); false ⇒ the working side (stage/discard).</summary>
    private sealed record DiffInteract(string Path, bool Staged);

    private readonly HashSet<GitDiffLine> _stageSel = new();
    private string? _stageSelKey;   // "path|staged" the current selection belongs to

    /// <summary>The interactive context for the Changes view's diff, or null
    /// (read-only) for the Commit view, fullscreen, or a conflicted selection.</summary>
    private DiffInteract? ChangesDiffInteract(GitFileDiff? diff)
    {
        if (View != JunctionView.Changes || diff == null || _changeSelPath == null || _changeSelConflicted)
            return null;
        // With --ignore-all-space the diff no longer describes the file byte-for-byte
        // (whitespace-only changes are omitted and shown as context), so a patch built
        // from it wouldn't apply. Staging stays off until the toggle is cleared.
        if (_settings.Git.IgnoreWhitespace) return null;
        return new DiffInteract(_changeSelPath, _changeSelStaged);
    }

    /// <summary>Resets the selection when the shown file/side changes, so lines
    /// from a previous file can't leak into a stage of the current one.</summary>
    private void SyncStageSelection(DiffInteract it)
    {
        var key = it.Path + "|" + it.Staged;
        if (_stageSelKey != key) { _stageSel.Clear(); _stageSelKey = key; }
    }

    private static IEnumerable<GitDiffLine> HunkChangedLines(GitDiffHunk h) =>
        h.Lines.Where(l => l.Kind is DiffLineKind.Addition or DiffLineKind.Deletion);

    private void ToggleStageLine(GitDiffLine l)
    {
        if (!_stageSel.Add(l)) _stageSel.Remove(l);
        MarkDirty();
    }

    /// <summary>Toggle a whole hunk: select all its changed lines, or clear them
    /// if they're already all selected.</summary>
    private void ToggleStageHunk(GitDiffHunk h)
    {
        var changed = HunkChangedLines(h).ToList();
        if (changed.Count == 0) return;
        if (changed.All(_stageSel.Contains))
            foreach (var l in changed) _stageSel.Remove(l);
        else
            foreach (var l in changed) _stageSel.Add(l);
        MarkDirty();
    }

    private void ClearStageSelection() { _stageSel.Clear(); MarkDirty(); }

    private void ApplyStaging(GitFileDiff diff, StageOp op, IReadOnlyCollection<GitDiffLine> lines)
    {
        if (ActiveRepo is not { } repo) return;
        if (lines.Count == 0) { Flash("No lines selected"); return; }

        var set = new HashSet<GitDiffLine>(lines);
        var patch = GitPatchBuilder.BuildSelectionPatch(diff, op, set);
        if (patch == null) { Flash("Nothing to " + StageVerb(op)); return; }

        var (reverse, cached) = op switch
        {
            StageOp.Stage => (false, true),
            StageOp.Unstage => (true, true),
            _ => (true, false),   // Discard: reverse-apply to the working tree
        };
        var res = repo.ApplyPatch(patch, reverse, cached, unidiffZero: false);
        if (res.Success)
        {
            int n = lines.Count;
            _stageSel.Clear();
            Flash($"{StageVerbPast(op)} {n} line{(n == 1 ? "" : "s")}");
            RefreshAll();
        }
        else
        {
            Flash(FirstLine(res.Stderr, StageVerb(op) + " failed"));
        }
    }

    private static string StageVerb(StageOp op) =>
        op switch { StageOp.Stage => "stage", StageOp.Unstage => "unstage", _ => "discard" };
    private static string StageVerbPast(StageOp op) =>
        op switch { StageOp.Stage => "Staged", StageOp.Unstage => "Unstaged", _ => "Discarded" };

    /// <summary>The action bar under the diff header, shown while lines are selected.</summary>
    private Element BuildStageActionBar(GitFileDiff diff, DiffInteract it)
    {
        var lines = _stageSel.ToList();
        int n = lines.Count;

        var bar = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 12, 6, 12))
            .Background(Nocturne.Alpha(Nocturne.Accent, 0.07))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(Ui.Icon(Icons.CheckSquare, 13f, Nocturne.Accent300).FlexShrink(0));
        bar.AddChild(Text.New($"{n} line{(n == 1 ? "" : "s")} selected")
            .FontSize(12f).TextColor(Nocturne.On(0.72)).FlexGrow(1));

        if (it.Staged)
        {
            bar.AddChild(Ui.PrimaryButton("Unstage", Icons.MinusCircle,
                () => ApplyStaging(diff, StageOp.Unstage, lines), 11.5f).FlexShrink(0));
        }
        else
        {
            bar.AddChild(Ui.PrimaryButton("Stage", Icons.PlusCircle,
                () => ApplyStaging(diff, StageOp.Stage, lines), 11.5f).FlexShrink(0));
            bar.AddChild(Ui.SecondaryButton("Discard", Icons.CounterClockwise,
                () => ApplyStaging(diff, StageOp.Discard, lines), 11.5f).FlexShrink(0));
        }
        bar.AddChild(Ui.GhostButton("Clear", Icons.X, ClearStageSelection, 11.5f).FlexShrink(0));
        return bar;
    }
}

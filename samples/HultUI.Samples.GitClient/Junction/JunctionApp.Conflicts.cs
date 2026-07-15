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
    // ── Merge-conflict resolution (Changes view) ─────────────────────────────
    //
    // Conflicts appear in the file list like any other change; selecting one swaps
    // the diff pane for a resolution view — Use ours / Use theirs / Open in VS Code
    // plus the identity of each side (branch · hash · subject · author). Merge-level
    // Abort / Continue live in a pinned banner above the list. No diffs by design.

    // Pinned above the file list while a merge is in progress.
    private Element BuildMergeBanner(ChangesData data)
    {
        int left = data.Conflicted.Count;
        var bar = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(9, 12, 9, 12))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(Ui.Icon(Icons.GitMerge, 14f, Nocturne.DangerIcon).FlexShrink(0));
        var label = left > 0
            ? $"Merge · {left} conflict{(left == 1 ? "" : "s")} left"
            : "Merge · all resolved";
        bar.AddChild(Text.New(label).FontSize(12f).FontWeight(FontWeight.Medium)
            .TextColor(Nocturne.Text).FlexGrow(1).FlexShrink(1)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));

        bar.AddChild(ConflictButton("Abort", Icons.X, DoMergeAbort, Nocturne.DangerText));
        bool canContinue = left == 0;
        var cont = ConflictButton("Continue", Icons.Play,
            canContinue ? DoMergeContinue : null, canContinue ? Nocturne.Accent : Nocturne.On(0.3));
        cont.Key = "merge-continue-" + (canContinue ? "on" : "off");
        bar.AddChild(cont);
        return bar;
    }

    // A conflict entry in the file list (no checkbox — you resolve via the pane).
    private Element ConflictListRow(GitFileChange f)
    {
        var selected = _changeSelConflicted && f.Path == _changeSelPath;
        var name = System.IO.Path.GetFileName(f.Path);
        var dir = f.Path.Length > name.Length ? f.Path[..^name.Length] : "";

        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 8, 6, 8)).CornerRadius(Nocturne.RadiusRow)
            .Cursor(CursorType.Pointer).Tooltip(f.Path);
        row.Key = "c:" + f.Path + (selected ? "#s" : "");
        if (selected) row.Background(Nocturne.Accent900);

        row.AddChild(Box.New().Width(14).FlexShrink(0)
            .Children(Ui.Icon(Icons.Warning, 12f, Nocturne.DangerIcon)));

        var text = Flex.Column(gap: 0).FlexGrow(1).FlexShrink(1);
        text.AddChild(Text.New(name).FontSize(12f)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (dir.Length > 0)
            text.AddChild(Text.New(dir.TrimEnd('/', '\\')).FontSize(9.5f).TextColor(Nocturne.On(0.40))
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(text);

        row.AddChild(Ui.Tag("conflict", Nocturne.Alpha(Nocturne.DangerText, 0.16), Nocturne.DangerText, 9.5f).FlexShrink(0));

        row.OnClick(_ => SelectConflict(f.Path));
        row.On<PointerDownEvent>(e => { if (e.Button == 2) SelectConflict(f.Path); });
        row.ContextMenu(() => ConflictFileMenuItems(f));
        if (!selected) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    private IReadOnlyList<ContextMenuItem> ConflictFileMenuItems(GitFileChange f) =>
    [
        Item(Icons.ArrowFatRight, "Use ours (current)", () => ResolveConflict(f.Path, ours: true)),
        Item(Icons.Cloud, "Use theirs (incoming)", () => ResolveConflict(f.Path, ours: false)),
        ContextMenuItem.Separator(),
        Item(Icons.FileCode, "Open in VS Code", () => OpenConflictInVsCode(f.Path)),
    ];

    // The right pane for a selected conflicted file: resolution buttons + the
    // identity of each side (what "ours" and "theirs" actually are).
    private Element BuildConflictResolutionPane(string path, MergeSides? merge)
    {
        var col = Flex.Column(gap: 0).FlexGrow(1);

        var header = Flex.Row(gap: 10).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(8, 12, 8, 12))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Ui.Mono(path.Length > 0 ? path : "—", 12f, Nocturne.Text)
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        header.AddChild(Ui.Tag("merge conflict", Nocturne.Alpha(Nocturne.DangerText, 0.16), Nocturne.DangerText, 10f)
            .FlexShrink(0));
        col.AddChild(header);

        var body = ScrollView.New().FlexGrow(1).Padding(new Thickness(16));
        var inner = Flex.Column(gap: 12);

        inner.AddChild(Text.New("This file has changes on both sides. Pick a side to keep, or open it in VS Code to merge by hand.")
            .FontSize(12.5f).TextColor(Nocturne.On(0.7)));

        var btns = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        btns.AddChild(ConflictButton("Use ours", null, () => ResolveConflict(path, ours: true), Nocturne.Accent300));
        btns.AddChild(ConflictButton("Use theirs", null, () => ResolveConflict(path, ours: false), Nocturne.Accent2_300));
        btns.AddChild(ConflictButton("Open in VS Code", Icons.FileCode, () => OpenConflictInVsCode(path), Nocturne.On(0.75)));
        inner.AddChild(btns);

        if (merge is { } m)
        {
            inner.AddChild(MergeSideCard("Incoming · theirs", m.TheirsBranch, m.Theirs, Nocturne.Accent2_400));
            inner.AddChild(MergeSideCard("Current · ours", m.OursBranch, m.Ours, Nocturne.Accent400));
        }

        body.AddChild(inner);
        col.AddChild(body);
        return col;
    }

    private Element MergeSideCard(string role, string? branch, GitCommit? commit, Color accent)
    {
        var card = Flex.Column(gap: 4)
            .Padding(new Thickness(9, 11, 9, 11))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusSm);

        var top = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        top.AddChild(Text.New(role.ToUpperInvariant()).FontSize(9.5f)
            .FontWeight(FontWeight.Medium).TextColor(accent).FlexGrow(1));
        if (!string.IsNullOrEmpty(branch))
            top.AddChild(Ui.Chip(Icons.GitBranch, branch, Nocturne.Neutral800, Nocturne.Neutral100).FlexShrink(0));
        card.AddChild(top);

        if (commit is { } c)
        {
            var line = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
            line.AddChild(Ui.Mono(c.ShortHash, 11f, accent).FlexShrink(0));
            line.AddChild(Text.New(c.Subject).FontSize(12f).TextColor(Nocturne.Text)
                .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
            card.AddChild(line);
            card.AddChild(Text.New(c.AuthorName).FontSize(11f).TextColor(Nocturne.On(0.5)));
        }
        else
        {
            card.AddChild(Text.New("(commit unavailable)").FontSize(11f).TextColor(Nocturne.On(0.4)));
        }
        return card;
    }

    // A small bordered action button; a null onClick renders it disabled (used for
    // Continue while conflicts remain).
    private static BoxElement ConflictButton(string label, string? glyph, Action? onClick, Color fg)
    {
        var b = Flex.Row(gap: 5).FlexShrink(0).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(4, 9, 4, 9)).CornerRadius(Nocturne.RadiusSm)
            .BorderColor(Nocturne.Divider).BorderWidth(1f);
        if (glyph != null) b.AddChild(Ui.Icon(glyph, 12f, fg));
        b.AddChild(Text.New(label).FontSize(11f).FontWeight(FontWeight.Medium).TextColor(fg));
        if (onClick != null)
        {
            b.Cursor(CursorType.Pointer);
            b.OnClick(e => { e.StopPropagation = true; onClick(); });
            b.HoverBg(Nocturne.Alpha(fg, 0.10), Color.Transparent);
        }
        return b;
    }

    // ── Actions ──────────────────────────────────────────────────────────

    private void ResolveConflict(string path, bool ours)
    {
        if (ActiveRepo is not { } repo) return;
        var res = ours ? repo.CheckoutOurs(path) : repo.CheckoutTheirs(path);
        if (res.Success)
        {
            // Resolved files are staged; keep this one selected as a normal change so
            // its (now-merged) diff shows, and the next conflict is a click away.
            _changeSelConflicted = false;
            _changeSelStaged = true;
        }
        Flash(res.Success
            ? $"Used {(ours ? "ours" : "theirs")} for {System.IO.Path.GetFileName(path)}"
            : FirstLine(res.Stderr, "Resolve failed"));
        RefreshAll();
    }

    private async void OpenConflictInVsCode(string path)
    {
        if (ActiveRepo is not { } repo) return;
        Flash("Opening in VS Code…");
        var full = System.IO.Path.Combine(repo.Path, path);
        var (ok, err) = await Task.Run(() =>
        {
            bool o = ExternalDiffLauncher.TryOpen(repo.Path, "code", [full], out var e);
            return (o, e);
        });
        if (!ok) Flash(string.IsNullOrEmpty(err) ? "Could not open VS Code" : err);
    }

    private void DoMergeContinue()
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.MergeContinue();
        Flash(res.Success ? "Merge completed" : FirstLine(res.Stderr, "Resolve every conflict first"));
        RefreshAll();
    }

    private void DoMergeAbort()
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.MergeAbort();
        Flash(res.Success ? "Merge aborted" : FirstLine(res.Stderr, "Abort failed"));
        RefreshAll();
    }
}

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
    private readonly record struct PaletteItem(string Kind, string KindLabel, string Glyph, string Label, string Hint, Action Run);

    private void TogglePalette()
    {
        _paletteOpen = !_paletteOpen;
        _paletteQuery = "";
        _paletteSel = 0;
        if (_paletteOpen) _pendingPaletteFocus = true;
        MarkDirty();
    }

    private void PaletteMove(int delta)
    {
        var items = FilteredPaletteItems();
        if (items.Count == 0) return;
        _paletteSel = Math.Clamp(_paletteSel + delta, 0, items.Count - 1);
        _pendingScrollToSelected = true;
        MarkDirty();
    }

    private void PaletteRun()
    {
        var items = FilteredPaletteItems();
        if (_paletteSel >= 0 && _paletteSel < items.Count)
            items[_paletteSel].Run();
    }

    private void PaletteKey(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Down: PaletteMove(1); e.StopPropagation = true; break;
            case KeyCode.Up: PaletteMove(-1); e.StopPropagation = true; break;
            case KeyCode.PageDown: PaletteMove(6); e.StopPropagation = true; break;
            case KeyCode.PageUp: PaletteMove(-6); e.StopPropagation = true; break;
            case KeyCode.Enter: PaletteRun(); e.StopPropagation = true; break;
            case KeyCode.Escape: ClosePalette(); e.StopPropagation = true; break;
        }
    }

    private void ClosePalette()
    {
        _paletteOpen = false;
        MarkDirty();
    }

    private Element BuildPalette()
    {
        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 140))
            .JustifyContent(JustifyContent.FlexStart).AlignItems(AlignItems.Center)
            .Padding(new Thickness(96, 0, 0, 0))
            .ZIndex(40);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; ClosePalette(); });

        var card = Flex.Column(gap: 0)
            .Width(600).MaxHeight(460)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg);
        card.On<ClickEvent>(e => e.StopPropagation = true);

        // Header: search field. Pin the height so the row doesn't collapse to the
        // empty input's measured height (which grows once text is entered).
        var header = Flex.Row(gap: 10).AlignItems(AlignItems.Center)
            .Height(56).FlexShrink(0)
            .Padding(new Thickness(0, 16, 0, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Ui.Icon(Icons.MagnifyingGlass, 16f, Nocturne.On(0.50)).FlexShrink(0));
        // Fields are flush/borderless via the Nocturne theme (theme.TextInput).
        // FlexBasis(0) makes it fill the remaining width regardless of content;
        // the explicit height keeps the row from collapsing to the empty field.
        var input = TextInput.New(_paletteQuery);
        input.PlaceholderText("Jump to a repo or branch, or run a command…")
            // Refining the query resets to the first match; scroll it back into view
            // so a list left scrolled down doesn't hide the new selection.
            .OnTextChanged(q => { _paletteQuery = q; _paletteSel = 0; _pendingScrollToSelected = true; MarkDirty(); })
            .Height(Dimension.Px(30))
            .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
            .TextColor(Nocturne.Text)
            .WithRef(_paletteRef);
        input.On<KeyDownEvent>(PaletteKey);
        header.AddChild(input);
        header.AddChild(Kbd("Esc").FlexShrink(0));
        card.AddChild(header);

        // Results.
        var items = FilteredPaletteItems();
        _paletteSel = Math.Clamp(_paletteSel, 0, Math.Max(0, items.Count - 1));
        var list = ScrollView.New().FlexGrow(1).Padding(new Thickness(6)).WithRef(_paletteScrollRef);
        if (items.Count == 0)
        {
            list.AddChild(Box.New().Padding(new Thickness(24)).JustifyContent(JustifyContent.Center)
                .AlignItems(AlignItems.Center)
                .Children(Text.New($"No matches for \"{_paletteQuery}\"").FontSize(13f).TextColor(Nocturne.On(0.45))));
        }
        else
        {
            for (var i = 0; i < items.Count; i++)
            {
                var rowEl = PaletteRow(items[i], i == _paletteSel);
                if (i == _paletteSel) rowEl.WithRef(_paletteSelRef);
                list.AddChild(rowEl);
            }
        }
        card.AddChild(list);

        // Footer.
        var footer = Flex.Row(gap: 16).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 14, 8, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        footer.AddChild(Hint("↑ ↓", "navigate"));
        footer.AddChild(Hint("↵", "select"));
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(Text.New("Repositories · Branches · Actions").FontSize(10.5f).TextColor(Nocturne.On(0.42)));
        card.AddChild(footer);

        backdrop.AddChild(card);
        return backdrop;
    }

    private Element PaletteRow(PaletteItem it, bool selected)
    {
        var (bg, fg) = PaletteTint(it.Kind);
        var normalBg = selected ? Nocturne.Alpha(Nocturne.Accent, 0.14) : Color.Transparent;
        var row = Flex.Row(gap: 11).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 10, 8, 10)).CornerRadius(Nocturne.RadiusMd)
            .Background(normalBg)
            .Cursor(CursorType.Pointer);
        // Stable key so reconciliation matches rows by identity, not position —
        // otherwise a filtered list keeps the previous row's (wrong) click handler.
        row.Key = $"{it.Kind}|{it.Label}";

        row.AddChild(Box.New().Width(26).Height(26).FlexShrink(0).CornerRadius(Nocturne.RadiusRow)
            .Background(bg).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(it.Glyph, 14f, fg)));

        var text = Flex.Column(gap: 0).FlexGrow(1);
        text.AddChild(Text.New(it.Label).FontSize(13.5f)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (it.Hint.Length > 0)
            text.AddChild(Text.New(it.Hint).FontSize(11f).TextColor(Nocturne.On(0.45))
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(text);

        row.AddChild(Ui.Tag(it.KindLabel, Nocturne.Neutral800, Nocturne.Neutral100, 9f));

        row.OnClick(_ => it.Run());
        row.HoverBg(Nocturne.Neutral900, normalBg);
        return row;
    }

    private static (Color Bg, Color Fg) PaletteTint(string kind) => kind switch
    {
        "action" => (Nocturne.Accent800, Nocturne.Accent100),
        "view" => (Nocturne.Neutral800, Nocturne.Neutral100),
        "repo" => (Nocturne.Accent2_900, Nocturne.Accent2_200),
        "ws" => (Nocturne.Neutral800, Nocturne.Neutral100),
        "branch" => (Nocturne.Accent900, Nocturne.Accent200),
        _ => (Nocturne.Neutral800, Nocturne.Neutral100),
    };

    private List<PaletteItem> FilteredPaletteItems()
    {
        var q = _paletteQuery.Trim();
        var all = BuildPaletteItems();
        if (q.Length == 0) return all;
        return all.Where(it => (it.Label + " " + it.Hint).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private List<PaletteItem> BuildPaletteItems()
    {
        var items = new List<PaletteItem>();

        // Actions.
        items.Add(new("action", "Action", Icons.ArrowDown, "Pull", "", () => { ClosePalette(); DoPull(); }));
        items.Add(new("action", "Action", Icons.ArrowUp, "Push", "", () => { ClosePalette(); DoPush(); }));
        items.Add(new("action", "Action", Icons.ArrowsClockwise, "Fetch all remotes", "", () => { ClosePalette(); DoFetch(); }));
        items.Add(new("action", "Action", Icons.GitBranch, "Create new branch…", "", () => { ClosePalette(); PromptNewBranch(); }));
        items.Add(new("action", "Action", Icons.TreeStructure, "Worktrees…", "", () => { ClosePalette(); OpenWorktrees(); }));
        items.Add(new("action", "Action", Icons.FileMagnifier, "Search file contents…", "", () => { ClosePalette(); OpenContentSearch(); }));
        items.Add(new("action", "Action", Icons.Funnel, "History: only the current branch", "",
            () => { ClosePalette(); SetView(JunctionView.History); ToggleCurrentBranchOnly(); }));
        // The diff's view toggles. The hint is the key, so the palette teaches it rather
        // than being the only way in; each switches to a diff view first, the way
        // "Search file contents…" switches to History.
        items.Add(new("action", "Action", Icons.Columns, "Diff: unified or split", "s",
            () => { ClosePalette(); ShowDiffView(); SetDiffSplit(!_diffSplit); }));
        items.Add(new("action", "Action", Icons.Rows, "Diff: wrap long lines", "w",
            () => { ClosePalette(); ShowDiffView(); ToggleDiffWrap(); }));
        items.Add(new("action", "Action", Icons.ArrowsOut, "Diff: fullscreen", "F11",
            () => { ClosePalette(); ShowDiffView(); ToggleDiffFullscreen(); }));
        items.Add(new("action", "Action", Icons.FileCode, "Diff: ignore whitespace changes", "i",
            () => { ClosePalette(); ShowDiffView(); ToggleIgnoreWhitespace(); }));
        items.Add(new("action", "Action", Icons.GitCommit, "Commit: show or hide the message body", "m",
            () => { ClosePalette(); SetView(JunctionView.Commit); ToggleCommitMessage(); }));
        // Find-in-text, which until now was only ever reachable by knowing Ctrl+F.
        items.Add(new("action", "Action", Icons.MagnifyingGlass, "Find in this diff or file", "f · Ctrl+F",
            () => { ClosePalette(); ShowDiffView(); if (ActiveFindBar() is { } bar) OpenFindBar(bar); }));
        items.Add(new("action", "Action", Icons.CounterClockwise, "Reflog (recover lost commits)…", "", () => { ClosePalette(); OpenReflog(); }));
        items.Add(new("action", "Action", Icons.Cube, "Submodules…", "", () => { ClosePalette(); OpenSubmodules(); }));
        items.Add(new("action", "Action", Icons.Cloud, "Remotes…", "", () => { ClosePalette(); OpenRemotes(); }));
        items.Add(new("action", "Action", Icons.GitCommit, "Commit staged changes", "", () => { ClosePalette(); SetView(JunctionView.Changes); }));
        items.Add(new("action", "Action", Icons.TrayArrowDown, "Stash changes", "", () => { ClosePalette(); DoStash(includeUntracked: false); }));
        items.Add(new("action", "Action", Icons.Stack, "Stash including untracked", "", () => { ClosePalette(); DoStash(includeUntracked: true); }));
        items.Add(new("action", "Action", Icons.FolderPlus, "Add existing repository…", "", () => { ClosePalette(); OpenAddRepo(); }));
        items.Add(new("action", "Action", Icons.DownloadSimple, "Clone repository…", "", () => { ClosePalette(); OpenCloneRepo(); }));
        items.Add(new("action", "Action", Icons.StackPlus, "New workspace…", "", () => { ClosePalette(); OpenNewWorkspace(); }));

        // Go to view.
        void GoTo(string label, string glyph, JunctionView v) =>
            items.Add(new("view", "Go to", glyph, $"Go to {label}", "", () => { ClosePalette(); SetView(v); }));
        GoTo("Overview", Icons.SquaresFour, JunctionView.Overview);
        GoTo("History", Icons.GitCommit, JunctionView.History);
        GoTo("Commit details", Icons.GitDiff, JunctionView.Commit);
        GoTo("Changes", Icons.Pencil, JunctionView.Changes);
        GoTo("Branches", Icons.GitBranch, JunctionView.Branches);
        GoTo("Tags", Icons.Tag, JunctionView.Tags);
        GoTo("Files", Icons.Folders, JunctionView.Files);

        // Repositories (union of all workspaces).
        foreach (var path in _settings.RepositoryPaths)
        {
            var name = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
            var p = path;
            items.Add(new("repo", "Repo", Icons.GitRepository, name, path, () => { SelectRepo(p); ClosePalette(); }));
        }

        // Workspaces.
        foreach (var ws in _settings.Workspaces)
        {
            var id = ws.Id;
            items.Add(new("ws", "Workspace", Icons.Stack, $"Switch to {ws.Name}",
                $"{ws.RepoPaths.Count} repositories", () => { SelectWorkspace(id); ClosePalette(); }));
        }

        // Branches of the active repo.
        if (ActiveRepo is { } repo)
        {
            foreach (var b in repo.GetBranches().Where(b => !b.IsRemote))
            {
                var name = b.Name;
                var pb = BranchName.Parse(name);
                items.Add(new("branch", "Branch", Icons.GitBranch, $"Checkout {pb.Title}", name,
                    () => { ClosePalette(); CheckoutBranch(name); }));
            }
        }

        return items;
    }

    private void CheckoutBranch(string name)
    {
        if (ActiveRepo is not { } repo) return;
        var result = repo.Checkout(name);
        Flash(result.Success ? $"Checked out {BranchName.Parse(name).Title}" : FirstLine(result.Stderr, "Checkout failed"));
        RefreshAll();
    }
}

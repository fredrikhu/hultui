using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Junction.Git;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    private CommandPalette? _palette;

    /// <summary>The framework's palette over <see cref="BuildPaletteItems"/>: each row's kind in a
    /// tinted square and a tag, the repository or branch it names on a second line, and the key
    /// that does the same in a chip. Ctrl+P (or K) closes it again.</summary>
    private CommandPalette PaletteDialog => _palette ??= new CommandPalette(BuildPaletteItems)
    {
        Placeholder = "Jump to a repo or branch, or run a command…",
        ToggleKeys = Shortcuts.Map["palette"].Keys,
        GroupStyle = PaletteGroupStyle.Trailing,
        SearchIcon = () => Ui.Icon(Icons.MagnifyingGlass, 16f, Nocturne.On(0.50)),
        Footer = "Repositories · Branches · Actions",
    };

    private bool PaletteOpen => _palette is { IsOpen: true };

    private void TogglePalette() => PaletteDialog.Toggle(_window);

    private void ClosePalette() => _palette?.Close();

    /// <summary>A row whose second line says where it leads: a path, a count, a branch.</summary>
    private static PaletteCommand Row(string kind, string kindLabel, string glyph, string label, string detail, Action run) =>
        new(label, run) { Group = kindLabel, Detail = detail.Length > 0 ? detail : null, Icon = () => KindIcon(kind, glyph) };

    /// <summary>A row with a key that does the same without the palette.</summary>
    private static PaletteCommand Keyed(string kind, string kindLabel, string glyph, string label, string keys, Action run) =>
        new(label, run) { Group = kindLabel, Keys = keys.Length > 0 ? keys : null, Icon = () => KindIcon(kind, glyph) };

    private static Element KindIcon(string kind, string glyph)
    {
        var (bg, fg) = PaletteTint(kind);
        return Box.New().Width(26).Height(26).CornerRadius(Nocturne.RadiusRow)
            .Background(bg).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(glyph, 14f, fg));
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

    private List<PaletteCommand> BuildPaletteItems()
    {
        var items = new List<PaletteCommand>();

        // Actions.
        items.Add(Row("action", "Action", Icons.ArrowDown, "Pull", "", () => { DoPull(); }));
        items.Add(Row("action", "Action", Icons.ArrowUp, "Push", "", () => { DoPush(); }));
        items.Add(Row("action", "Action", Icons.ArrowsClockwise, "Fetch all remotes", "", () => { DoFetch(); }));
        items.Add(Row("action", "Action", Icons.GitBranch, "Create new branch…", "", () => { PromptNewBranch(); }));
        items.Add(Row("action", "Action", Icons.TreeStructure, "Worktrees…", "", () => { OpenWorktrees(); }));
        items.Add(Row("action", "Action", Icons.FileMagnifier, "Search file contents…", "", () => { OpenContentSearch(); }));
        items.Add(Row("action", "Action", Icons.Funnel, "History: only the current branch", "",
            () => { SetView(JunctionView.History); ToggleCurrentBranchOnly(); }));
        // The diff's view toggles. The hint is the key, so the palette teaches it rather
        // than being the only way in; each switches to a diff view first, the way
        // "Search file contents…" switches to History.
        items.Add(Keyed("action", "Action", Icons.Columns, "Diff: unified or split", Shortcuts.Keys("diff.split"),
            () => { ShowDiffView(); SetDiffSplit(!_diffSplit); }));
        items.Add(Keyed("action", "Action", Icons.Rows, "Diff: wrap long lines", Shortcuts.Keys("diff.wrap"),
            () => { ShowDiffView(); ToggleDiffWrap(); }));
        items.Add(Keyed("action", "Action", Icons.ArrowsOut, "Diff: fullscreen", Shortcuts.Keys("diff.fullscreen"),
            () => { ShowDiffView(); ToggleDiffFullscreen(); }));
        items.Add(Keyed("action", "Action", Icons.FileCode, "Diff: ignore whitespace changes", Shortcuts.Keys("diff.whitespace"),
            () => { ShowDiffView(); ToggleIgnoreWhitespace(); }));
        items.Add(Keyed("action", "Action", Icons.GitCommit, "Commit: show or hide the message body", Shortcuts.Keys("commit.message"),
            () => { SetView(JunctionView.Commit); ToggleCommitMessage(); }));
        // Find-in-text, which until now was only ever reachable by knowing Ctrl+F.
        items.Add(Keyed("action", "Action", Icons.MagnifyingGlass, "Find in this diff or file", Shortcuts.Keys("find"),
            () => { ShowDiffView(); if (ActiveFindBar() is { } bar) bar.Open(); }));
        // Background fetch is a switch in Options › General now, not a palette toggle.
        items.Add(Row("action", "Action", Icons.ArrowsClockwise,
            "Background fetch: " + (_settings.General.AutoFetchEnabled ? "on" : "off"),
            "Options › General",
            () => { OpenOptions(OptionsSection.General); }));
        items.Add(Row("action", "Action", Icons.CounterClockwise, "Reflog (recover lost commits)…", "", () => { OpenReflog(); }));
        // Options has no gear in the chrome by design: these verbs and Ctrl+, are the ways in.
        items.Add(Keyed("action", "Action", Icons.Gear, "Options…", Shortcuts.Keys("options"),
            () => { OpenOptions(); }));
        items.Add(Row("action", "Action", Icons.GitLabLogo, "GitLab accounts…",
            _gitlab.All.Count == 0 ? "none" : string.Join(", ", _gitlab.All.Select(a => a.Host)),
            () => { OpenOptions(OptionsSection.GitLabAccounts); }));
        if (_gitlab.All.Count > 0)
        {
            items.Add(Keyed("action", "Action", Icons.ArrowsClockwise, "Refresh GitLab data", Shortcuts.Keys("gitlab.refresh"),
                () => { RefreshGitLabNow(); }));
            items.Add(Row("action", "Action", Icons.Bell, "To do on GitLab…",
                TodoCount is var todos && todos > 0 ? $"{todos} pending" : "nothing pending",
                () => { OpenTodos(); }));
        }
        items.Add(Row("action", "Action", Icons.Palette, "Appearance: colour scheme…",
            Nocturne.Theme.Name, () => { OpenOptions(OptionsSection.Appearance); }));
        if (GitLabRemoteFor(_activeRepoPath) is { } gitlabRemote)
        {
            bool signedIn = _gitlab.HasAccount(gitlabRemote.Host);
            items.Add(Row("action", "Action", Icons.GitPullRequest, "Merge requests…",
                signedIn ? gitlabRemote.ProjectPath : $"sign in to {gitlabRemote.Host} first",
                () => { if (signedIn) OpenMergeRequests(); else OpenOptions(OptionsSection.GitLabAccounts); }));
            if (signedIn && HasActiveRepo)
            {
                var leaf = BranchName.Parse(SummaryFor(_activeRepoPath).Branch).Title;
                items.Add(Row("action", "Action", Icons.GitPullRequest, "Create merge request…", $"from {leaf}",
                    () => { OpenCreateMergeRequest(); }));
            }
        }
        items.Add(Row("action", "Action", Icons.Cube, "Submodules…", "", () => { OpenSubmodules(); }));
        items.Add(Row("action", "Action", Icons.Cloud, "Remotes…", "", () => { OpenRemotes(); }));
        if (GitLabActive(_activeRepoPath))
            items.Add(Row("action", "Action", Icons.GitBranch, "Create branch from issue…", "<iid>-<slug> from the default branch",
                () => { OpenBranchFromIssue(); }));
        items.Add(Row("action", "Action", Icons.GitCommit, "Commit staged changes", "", () => { SetView(JunctionView.Changes); }));
        items.Add(Row("action", "Action", Icons.TrayArrowDown, "Stash changes", "", () => { DoStash(includeUntracked: false); }));
        items.Add(Row("action", "Action", Icons.Stack, "Stash including untracked", "", () => { DoStash(includeUntracked: true); }));
        items.Add(Row("action", "Action", Icons.FolderPlus, "Add existing repository…", "", () => { OpenAddRepo(); }));
        items.Add(Row("action", "Action", Icons.DownloadSimple, "Clone repository…", "", () => { OpenCloneRepo(); }));
        items.Add(Row("action", "Action", Icons.StackPlus, "New workspace…", "", () => { OpenNewWorkspace(); }));

        // Go to view.
        void GoTo(string label, string glyph, JunctionView v) =>
            items.Add(Row("view", "Go to", glyph, $"Go to {label}", "", () => { SetView(v); }));
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
            items.Add(Row("repo", "Repo", Icons.GitRepository, name, path, () => { SelectRepo(p); }));
        }

        // Workspaces.
        foreach (var ws in _settings.Workspaces)
        {
            var id = ws.Id;
            items.Add(Row("ws", "Workspace", Icons.Stack, $"Switch to {ws.Name}",
                $"{ws.RepoPaths.Count} repositories", () => { SelectWorkspace(id); }));
        }

        // Branches of the active repo.
        if (ActiveRepo is { } repo)
        {
            foreach (var b in repo.GetBranches().Where(b => !b.IsRemote))
            {
                var name = b.Name;
                var pb = BranchName.Parse(name);
                items.Add(Row("branch", "Branch", Icons.GitBranch, $"Checkout {pb.Title}", name,
                    () => { CheckoutBranch(name); }));
            }
        }

        return items;
    }

    private void CheckoutBranch(string name)
    {
        if (ActiveRepo is not { } repo) return;
        RunGitCommand(() => repo.Checkout(name), result =>
        {
            Flash(result.Success ? $"Checked out {BranchName.Parse(name).Title}" : FirstLine(result.Stderr, "Checkout failed"));
            RefreshAll();
        });
    }
}

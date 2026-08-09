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
    // Submodule management: a per-repo overlay panel (like worktrees / reflog),
    // opened from the command palette. Lists each submodule with its status and
    // per-row init / update / open / deinit, plus update-all / sync / add.

    private bool _submodulesOpen;

    private readonly KeyedAsyncLoader<string, IReadOnlyList<GitSubmodule>> _submoduleLoaders =
        new([], capacity: 16, comparer: PathComparer);

    private AsyncLoader<IReadOnlyList<GitSubmodule>> _submodules =>
        _submoduleLoaders.For(_activeRepoPath);

    private bool _submoduleOpRunning;

    private void OpenSubmodules()
    {
        if (!HasActiveRepo) { Flash("Open a repository first"); return; }
        _submodulesOpen = true;
        // Rerender, not MarkDirty: opening activates this repo's loader for the
        // first time, and an untracked MarkDirty rebuild would leave the Effect
        // unsubscribed from it (panel stuck on its loading placeholder).
        Rerender();
    }

    private void CloseSubmodules()
    {
        _submodulesOpen = false;
        MarkDirty();
    }

    private IReadOnlyList<GitSubmodule> LoadSubmodules() => RepoFor(_activeRepoPath).GetSubmodules();

    // ── Panel ────────────────────────────────────────────────────────────

    private Element BuildSubmodulesPanel()
    {
        _submodules.Load($"{_activeRepoPath}|{DataGen(_activeRepoPath)}", LoadSubmodules);
        var list = _submodules.Value;
        var loading = _submodules.IsLoading && list.Count == 0;

        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 140))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Padding(new Thickness(24))
            .ZIndex(42);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; CloseSubmodules(); });

        var card = Flex.Column(gap: 0)
            .Width(680).MaxHeight(580)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg);
        card.On<ClickEvent>(e => e.StopPropagation = true);

        card.AddChild(BuildSubmodulesHeader());
        card.AddChild(BuildSubmodulesToolbar(list.Count));

        var scroll = ScrollView.New().FlexGrow(1).Padding(new Thickness(8, 10, 8, 10));
        if (loading)
            scroll.AddChild(SubmodulesPlaceholder("Loading submodules…"));
        else if (list.Count == 0)
            scroll.AddChild(SubmodulesPlaceholder("This repository has no submodules"));
        else
            foreach (var sm in list)
                scroll.AddChild(SubmoduleRow(sm));
        card.AddChild(scroll);

        backdrop.AddChild(card);
        return backdrop;
    }

    private Element BuildSubmodulesHeader()
    {
        var repoName = System.IO.Path.GetFileName(_activeRepoPath.TrimEnd('/', '\\'));
        var bar = Flex.Row(gap: 10).AlignItems(AlignItems.Center)
            .Padding(new Thickness(14, 18, 14, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(Ui.Icon(Icons.Cube, 16f, Nocturne.Accent300).FlexShrink(0));
        var titleCol = Flex.Column(gap: 1).FlexGrow(1);
        titleCol.AddChild(Text.New("Submodules").FontSize(16f).FontWeight(FontWeight.SemiBold));
        titleCol.AddChild(Text.New(repoName).FontSize(11.5f).TextColor(Nocturne.On(0.5))
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        bar.AddChild(titleCol);
        bar.AddChild(Ui.IconButton(Icons.WinClose, CloseSubmodules, "Close (Esc)", 30f).FlexShrink(0));
        return bar;
    }

    private Element BuildSubmodulesToolbar(int count)
    {
        var bar = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 14, 8, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        bar.AddChild(Ui.PrimaryButton("Add submodule", Icons.FolderPlus, PromptAddSubmodule, 12.5f));
        if (count > 0)
        {
            bar.AddChild(Ui.SecondaryButton("Update all", Icons.ArrowsClockwise,
                () => UpdateAllSubmodules(), 12.5f));
            bar.AddChild(Ui.SecondaryButton("Sync URLs", Icons.Cloud, SyncSubmodulesAction, 12.5f));
        }
        bar.AddChild(Box.New().FlexGrow(1));
        return bar;
    }

    private static Element SubmodulesPlaceholder(string text) =>
        Box.New().Padding(new Thickness(28)).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Text.New(text).FontSize(13f).TextColor(Nocturne.On(0.45)));

    private Element SubmoduleRow(GitSubmodule sm)
    {
        var name = sm.Path.TrimEnd('/', '\\');

        var row = Flex.Row(gap: 11).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 10, 8, 10)).CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer);
        row.Key = "sm:" + sm.Path + (sm.Uninitialized ? "#u" : sm.Dirty ? "#d" : "");

        row.AddChild(Box.New().Width(26).Height(26).FlexShrink(0).CornerRadius(Nocturne.RadiusRow)
            .Background(Nocturne.Neutral800).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(Icons.Cube, 13f, Nocturne.Accent200)));

        var textCol = Flex.Column(gap: 1).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));
        var titleRow = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        titleRow.AddChild(Ui.Mono(name, 12.5f, Nocturne.Text).FlexShrink(1)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (sm.Uninitialized)
            titleRow.AddChild(Ui.TagOutline("not initialized", Nocturne.On(0.5), 9f).FlexShrink(0));
        else if (sm.Dirty)
            titleRow.AddChild(Ui.TagOutline("modified", Nocturne.DelCount, 9f).FlexShrink(0));
        textCol.AddChild(titleRow);
        var sub = sm.Describe is { Length: > 0 } d ? $"{Short(sm.Hash)} · {d}" : Short(sm.Hash);
        textCol.AddChild(Text.New(sub).FontSize(10.5f).TextColor(Nocturne.On(0.42))
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(textCol);

        var actions = Flex.Row(gap: 5).FlexShrink(0).AlignItems(AlignItems.Center);
        if (sm.Uninitialized)
        {
            actions.AddChild(Ui.SecondaryButton("Init & update", Icons.DownloadSimple,
                () => UpdateOneSubmodule(sm.Path, init: true), 11f));
        }
        else
        {
            actions.AddChild(Ui.SecondaryButton("Open", Icons.BendDownRight,
                () => OpenSubmodule(sm.Path), 11f));
            var upd = Ui.IconButton(Icons.ArrowsClockwise, () => UpdateOneSubmodule(sm.Path, init: false),
                "Update to recorded commit", 30f);
            upd.OnClick(e => e.StopPropagation = true);
            actions.AddChild(upd);
        }
        row.AddChild(actions);

        row.OnClick(_ => { if (!sm.Uninitialized) OpenSubmodule(sm.Path); });
        row.ContextMenu(() => SubmoduleMenuItems(sm));
        row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    private IReadOnlyList<ContextMenuItem> SubmoduleMenuItems(GitSubmodule sm)
    {
        var items = new List<ContextMenuItem>();
        if (sm.Uninitialized)
        {
            items.Add(Item(Icons.DownloadSimple, "Init & update", () => UpdateOneSubmodule(sm.Path, init: true)));
        }
        else
        {
            items.Add(Item(Icons.BendDownRight, "Open in Junction", () => OpenSubmodule(sm.Path)));
            items.Add(Item(Icons.ArrowsClockwise, "Update to recorded commit", () => UpdateOneSubmodule(sm.Path, init: false)));
        }
        items.Add(Item(Icons.Copy, "Copy path", () => CopyToClipboard(sm.Path)));
        if (!sm.Uninitialized)
        {
            items.Add(ContextMenuItem.Separator());
            items.Add(Item(Icons.Trash, "Deinitialize", () => DeinitSubmoduleAction(sm.Path, force: false)));
            items.Add(Danger(Icons.Warning, "Deinitialize (discard changes)", () => DeinitSubmoduleAction(sm.Path, force: true)));
        }
        return items;
    }

    // ── Actions ────────────────────────────────────────────────────────────

    /// <summary>Opens an initialized submodule as a repository in Junction — its
    /// working tree is a nested git repo, which the rest of the app handles.</summary>
    private void OpenSubmodule(string path)
    {
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(_activeRepoPath, path));
        if (!_settings.RepositoryPaths.Any(p => PathEquals(p, full)))
            _settings.RepositoryPaths.Add(full);
        var ws = ActiveWorkspace;
        if (ws != null && !ws.RepoPaths.Any(p => PathEquals(p, full)))
            ws.RepoPaths.Add(full);
        Persist();
        CloseSubmodules();
        SelectRepo(full);
        Flash($"Opened submodule {System.IO.Path.GetFileName(path.TrimEnd('/', '\\'))}");
    }

    private async void UpdateAllSubmodules()
    {
        if (ActiveRepo is not { } repo || _submoduleOpRunning) return;
        _submoduleOpRunning = true;
        Flash("Updating submodules…");
        var res = await Task.Run(() => repo.UpdateSubmodules(init: true));
        _submoduleOpRunning = false;
        Flash(res.Success ? "Submodules updated" : FirstLine(res.Stderr, "Update failed"));
        RefreshAll();
    }

    private async void UpdateOneSubmodule(string path, bool init)
    {
        if (ActiveRepo is not { } repo || _submoduleOpRunning) return;
        _submoduleOpRunning = true;
        Flash(init ? $"Initializing {path}…" : $"Updating {path}…");
        var res = await Task.Run(() => repo.UpdateSubmodule(path, init));
        _submoduleOpRunning = false;
        Flash(res.Success
            ? (init ? $"Initialized {path}" : $"Updated {path}")
            : FirstLine(res.Stderr, "Update failed"));
        RefreshAll();
    }

    private async void SyncSubmodulesAction()
    {
        if (ActiveRepo is not { } repo || _submoduleOpRunning) return;
        _submoduleOpRunning = true;
        Flash("Syncing submodule URLs…");
        var res = await Task.Run(() => repo.SyncSubmodules());
        _submoduleOpRunning = false;
        Flash(res.Success ? "Submodule URLs synced" : FirstLine(res.Stderr, "Sync failed"));
        RefreshAll();
    }

    private async void DeinitSubmoduleAction(string path, bool force)
    {
        if (ActiveRepo is not { } repo || _submoduleOpRunning) return;
        _submoduleOpRunning = true;
        var res = await Task.Run(() => repo.DeinitSubmodule(path, force));
        _submoduleOpRunning = false;
        Flash(res.Success ? $"Deinitialized {path}"
            : FirstLine(res.Stderr, "Deinit failed — it may have local changes (try discard)"));
        RefreshAll();
    }

    private void PromptAddSubmodule() => OpenDialog(new DialogSpec(
        "Add submodule",
        "Clones a repository as a submodule in a subfolder named after its URL.",
        "https://github.com/owner/repo.git",
        "Add",
        (url, _) =>
        {
            if (ActiveRepo is not { } repo) return;
            var name = NameFromUrl(url.Trim());
            if (string.IsNullOrEmpty(name)) { Flash("Couldn't derive a folder name from the URL"); return; }
            AddSubmoduleAsync(repo, url.Trim(), name);
        },
        Validate: u => string.IsNullOrWhiteSpace(u) ? "Enter a repository URL" : null));

    private async void AddSubmoduleAsync(GitRepository repo, string url, string name)
    {
        if (_submoduleOpRunning) return;
        _submoduleOpRunning = true;
        Flash($"Adding submodule {name}…");
        var res = await Task.Run(() => repo.AddSubmodule(url, name));
        _submoduleOpRunning = false;
        Flash(res.Success ? $"Added submodule {name}" : FirstLine(res.Stderr, "Add submodule failed"));
        RefreshAll();
    }
}

using HultUI;
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
    // Remote management: a per-repo overlay panel (like worktrees / reflog /
    // submodules), opened from the command palette. Add / edit-url / rename /
    // remove the repo's remotes.

    private bool _remotesOpen;

    private readonly Dictionary<string, AsyncLoader<IReadOnlyList<GitRemote>>> _remoteDetailLoaders =
        new(PathComparer);

    private AsyncLoader<IReadOnlyList<GitRemote>> _remoteDetails =>
        LoaderFor(_remoteDetailLoaders, _activeRepoPath, (IReadOnlyList<GitRemote>)[]);

    private void OpenRemotes()
    {
        if (!HasActiveRepo) { Flash("Open a repository first"); return; }
        _remotesOpen = true;
        MarkDirty();
    }

    private void CloseRemotes()
    {
        _remotesOpen = false;
        MarkDirty();
    }

    private IReadOnlyList<GitRemote> LoadRemotes() => RepoFor(_activeRepoPath).GetRemotesDetailed();

    // ── Panel ────────────────────────────────────────────────────────────

    private Element BuildRemotesPanel()
    {
        _remoteDetails.Load($"{_activeRepoPath}|{DataGen(_activeRepoPath)}", LoadRemotes);
        var list = _remoteDetails.Value;
        var loading = _remoteDetails.IsLoading && list.Count == 0;

        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 140))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Padding(new Thickness(24))
            .ZIndex(42);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; CloseRemotes(); });

        var card = Flex.Column(gap: 0)
            .Width(680).MaxHeight(560)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg);
        card.On<ClickEvent>(e => e.StopPropagation = true);

        card.AddChild(BuildRemotesHeader());
        card.AddChild(BuildRemotesToolbar());

        var scroll = ScrollView.New().FlexGrow(1).Padding(new Thickness(8, 10, 8, 10));
        if (loading)
            scroll.AddChild(RemotesPlaceholder("Loading remotes…"));
        else if (list.Count == 0)
            scroll.AddChild(RemotesPlaceholder("No remotes configured"));
        else
            foreach (var r in list)
                scroll.AddChild(RemoteRow(r));
        card.AddChild(scroll);

        backdrop.AddChild(card);
        return backdrop;
    }

    private Element BuildRemotesHeader()
    {
        var repoName = System.IO.Path.GetFileName(_activeRepoPath.TrimEnd('/', '\\'));
        var bar = Flex.Row(gap: 10).AlignItems(AlignItems.Center)
            .Padding(new Thickness(14, 18, 14, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(Ui.Icon(Icons.Cloud, 16f, Nocturne.Accent300).FlexShrink(0));
        var titleCol = Flex.Column(gap: 1).FlexGrow(1);
        titleCol.AddChild(Text.New("Remotes").FontSize(16f).FontWeight(FontWeight.SemiBold));
        titleCol.AddChild(Text.New(repoName).FontSize(11.5f).TextColor(Nocturne.On(0.5))
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        bar.AddChild(titleCol);
        bar.AddChild(Ui.IconButton(Icons.WinClose, CloseRemotes, "Close (Esc)", 30f).FlexShrink(0));
        return bar;
    }

    private Element BuildRemotesToolbar()
    {
        var bar = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 14, 8, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        bar.AddChild(Ui.PrimaryButton("Add remote", Icons.FolderPlus, PromptAddRemote, 12.5f));
        bar.AddChild(Box.New().FlexGrow(1));
        return bar;
    }

    private static Element RemotesPlaceholder(string text) =>
        Box.New().Padding(new Thickness(28)).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Text.New(text).FontSize(13f).TextColor(Nocturne.On(0.45)));

    private Element RemoteRow(GitRemote r)
    {
        var row = Flex.Row(gap: 11).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 10, 8, 10)).CornerRadius(Nocturne.RadiusMd);
        row.Key = "remote:" + r.Name;

        row.AddChild(Box.New().Width(26).Height(26).FlexShrink(0).CornerRadius(Nocturne.RadiusRow)
            .Background(Nocturne.Neutral800).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(Icons.Cloud, 13f, Nocturne.Accent200)));

        var textCol = Flex.Column(gap: 1).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));
        var titleRow = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        titleRow.AddChild(Text.New(r.Name).FontSize(12.5f).FontWeight(FontWeight.Medium).FlexShrink(0));
        if (r.HasSeparatePushUrl)
            titleRow.AddChild(Ui.TagOutline("push ≠ fetch", Nocturne.On(0.5), 9f).FlexShrink(0));
        textCol.AddChild(titleRow);
        textCol.AddChild(Ui.Mono(r.FetchUrl.Length > 0 ? r.FetchUrl : "—", 10.5f, Nocturne.On(0.5))
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (r.HasSeparatePushUrl)
            textCol.AddChild(Ui.Mono($"push: {r.PushUrl}", 10f, Nocturne.On(0.42))
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(textCol);

        var actions = Flex.Row(gap: 5).FlexShrink(0).AlignItems(AlignItems.Center);
        actions.AddChild(Ui.SecondaryButton("Edit URL", Icons.PencilSimple, () => PromptEditRemoteUrl(r), 11f));
        var trash = Ui.IconButton(Icons.Trash, () => RemoveRemoteAction(r.Name), "Remove remote", 30f);
        trash.OnClick(e => e.StopPropagation = true);
        actions.AddChild(trash);
        row.AddChild(actions);

        row.ContextMenu(() => RemoteMenuItems(r));
        row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    private IReadOnlyList<ContextMenuItem> RemoteMenuItems(GitRemote r) =>
    [
        Item(Icons.PencilSimple, "Edit URL…", () => PromptEditRemoteUrl(r)),
        Item(Icons.PencilSimple, "Rename…", () => PromptRenameRemote(r.Name)),
        Item(Icons.Copy, "Copy fetch URL", () => CopyToClipboard(r.FetchUrl)),
        ContextMenuItem.Separator(),
        Danger(Icons.Trash, "Remove remote", () => RemoveRemoteAction(r.Name)),
    ];

    // ── Actions ────────────────────────────────────────────────────────────

    /// <summary>Add is two-field, so it chains: prompt the name, then the URL.</summary>
    private void PromptAddRemote() => OpenDialog(new DialogSpec(
        "Add remote — name",
        "Step 1 of 2: the short name git uses for this remote.",
        "origin",
        "Next",
        (name, _) => PromptAddRemoteUrl(name.Trim()),
        Validate: n => string.IsNullOrWhiteSpace(n) ? "Enter a remote name" : null));

    private void PromptAddRemoteUrl(string name) => OpenDialog(new DialogSpec(
        $"Add remote — URL for '{name}'",
        "Step 2 of 2: the repository URL to fetch from and push to.",
        "https://github.com/owner/repo.git",
        "Add",
        (url, _) =>
        {
            if (ActiveRepo is not { } repo) return;
            var res = repo.AddRemote(name, url.Trim());
            Flash(res.Success ? $"Added remote {name}" : FirstLine(res.Stderr, "Add remote failed"));
            RefreshAll();
        },
        Validate: u => string.IsNullOrWhiteSpace(u) ? "Enter a repository URL" : null));

    private void PromptEditRemoteUrl(GitRemote r) => OpenDialog(new DialogSpec(
        $"Edit URL for '{r.Name}'",
        "Sets the fetch and push URL for this remote.",
        "https://github.com/owner/repo.git",
        "Save",
        (url, _) =>
        {
            if (ActiveRepo is not { } repo) return;
            var res = repo.SetRemoteUrl(r.Name, url.Trim());
            Flash(res.Success ? $"Updated {r.Name} URL" : FirstLine(res.Stderr, "Set URL failed"));
            RefreshAll();
        },
        Validate: u => string.IsNullOrWhiteSpace(u) ? "Enter a repository URL" : null),
        initial: r.FetchUrl);

    private void PromptRenameRemote(string oldName) => OpenDialog(new DialogSpec(
        "Rename remote",
        $"Renames '{oldName}' (also rewrites its remote-tracking refs).",
        "new-name",
        "Rename",
        (newName, _) =>
        {
            if (ActiveRepo is not { } repo) return;
            var res = repo.RenameRemote(oldName, newName.Trim());
            Flash(res.Success ? $"Renamed to {newName.Trim()}" : FirstLine(res.Stderr, "Rename failed"));
            RefreshAll();
        },
        Validate: n => string.IsNullOrWhiteSpace(n) ? "Enter a new name" : null),
        initial: oldName);

    private void RemoveRemoteAction(string name)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.RemoveRemote(name);
        Flash(res.Success ? $"Removed remote {name}" : FirstLine(res.Stderr, "Remove failed"));
        RefreshAll();
    }
}

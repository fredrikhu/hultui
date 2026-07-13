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
    private const string Brand = "Junction";

    // ── Title bar ────────────────────────────────────────────────────────

    private Element BuildTitleBar()
    {
        var sum = HasActiveRepo ? SummaryFor(_activeRepoPath) : RepoSummary.Pending;
        var pb = BranchName.Parse(sum.Branch);
        var repoName = HasActiveRepo ? System.IO.Path.GetFileName(_activeRepoPath.TrimEnd('/', '\\')) : "—";
        var wsName = ActiveWorkspace?.Name ?? "";

        var bar = Flex.Row(gap: 0)
            .Height(40).FlexShrink(0)
            .AlignItems(AlignItems.Stretch)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        // Left drag region (balances the right group so the breadcrumb centres).
        bar.AddChild(Flex.Row(gap: 0).FlexGrow(1).Padding(new Thickness(0, 0, 0, 14)).WindowDrag());

        // Centred breadcrumb (draggable — no handlers on these children).
        var crumb = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center);
        crumb.AddChild(Text.New(Brand).FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Nocturne.On(0.70)));
        crumb.AddChild(Sep());
        crumb.AddChild(Ui.Icon(Icons.GitRepository, 13f, Nocturne.Accent300));
        crumb.AddChild(Text.New(repoName).FontSize(13f).TextColor(Nocturne.On(0.70)));
        crumb.AddChild(Sep());
        crumb.AddChild(Ui.Icon(Icons.GitBranch, 12f, Nocturne.Accent300));
        crumb.AddChild(Ui.Mono(pb.Title, 12f, Nocturne.On(0.70)));
        if (wsName.Length > 0)
        {
            crumb.AddChild(Sep());
            crumb.AddChild(Text.New(wsName).FontSize(13f).TextColor(Nocturne.On(0.75)));
        }
        crumb.WindowDrag();
        bar.AddChild(crumb);

        // Right group: search button + Windows-style window controls (flush right).
        var right = Flex.Row(gap: 0).FlexGrow(1)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.FlexEnd);

        var search = Flex.Row(gap: 8)
            .Height(26).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 8, 0, 10))
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer);
        search.AddChild(Ui.Icon(Icons.MagnifyingGlass, 13f, Nocturne.On(0.55)));
        search.AddChild(Text.New("Search & commands").FontSize(12f).TextColor(Nocturne.On(0.55)));
        search.AddChild(Kbd("Ctrl K"));
        search.OnClick(_ => TogglePalette());
        search.HoverBg(Nocturne.Alpha(Nocturne.Accent, 0.08), Nocturne.Bg);
        right.AddChild(search);

        right.AddChild(Box.New().Width(10));

        var controls = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch);
        controls.AddChild(WinButton(Icons.WinMinimize, "Minimize", () => _window.Minimize(), Nocturne.Neutral800));
        controls.AddChild(WinButton(Icons.WinMaximize, "Maximize", () => _window.ToggleMaximize(), Nocturne.Neutral800));
        controls.AddChild(WinButton(Icons.WinClose, "Close", () => _window.Close(), new Color(232, 17, 35)));
        right.AddChild(controls);

        bar.AddChild(right);
        return bar;
    }

    private static Element Sep() => Text.New("·").FontSize(13f).TextColor(Nocturne.On(0.35));

    private BoxElement WinButton(string glyph, string tooltip, Action onClick, Color hoverBg)
    {
        var b = Flex.Row(gap: 0).Width(46).Height(40)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Cursor(CursorType.Pointer).Tooltip(tooltip);
        b.AddChild(Ui.Icon(glyph, 11f, Nocturne.On(0.75)));
        b.OnClick(_ => onClick());
        return b.HoverBg(hoverBg, Color.Transparent);
    }

    private static BoxElement Kbd(string text) =>
        Box.New()
            .Padding(new Thickness(2, 5, 2, 5))
            .CornerRadius(4f)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .Children(Ui.Mono(text, 10f, Nocturne.On(0.55)));

    // ── Toolbar (view tabs + git actions) ────────────────────────────────

    private Element BuildToolbar()
    {
        var sum = HasActiveRepo ? SummaryFor(_activeRepoPath) : RepoSummary.Pending;

        var bar = Flex.Row(gap: 14)
            .Height(52).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 16, 0, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        // Segmented view tabs.
        var seg = Flex.Row(gap: 2)
            .Padding(new Thickness(3))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd)
            .AlignItems(AlignItems.Center);
        seg.AddChild(ViewTab(JunctionView.Overview, "Overview", Icons.SquaresFour, "1", 0));
        seg.AddChild(ViewTab(JunctionView.History, "History", Icons.GitCommit, "2", 0));
        seg.AddChild(ViewTab(JunctionView.Commit, "Commit", Icons.GitDiff, "3", 0));
        seg.AddChild(ViewTab(JunctionView.Changes, "Changes", Icons.Pencil, "4", sum.Dirty));
        seg.AddChild(ViewTab(JunctionView.Branches, "Branches", Icons.GitBranch, "5", 0));
        bar.AddChild(seg);

        bar.AddChild(Box.New().FlexGrow(1));

        // Git actions.
        var actions = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        actions.AddChild(Ui.SecondaryButton("Fetch", Icons.ArrowsClockwise, DoFetch));

        var pull = Ui.SecondaryButton("Pull", Icons.ArrowDown, DoPull);
        if (sum.Behind > 0)
            pull.AddChild(Text.New(sum.Behind.ToString()).FontSize(13f).TextColor(Nocturne.Accent300));
        actions.AddChild(pull);

        var push = Ui.PrimaryButton("Push", Icons.ArrowUp, DoPush);
        if (sum.Ahead > 0)
            push.AddChild(Text.New(sum.Ahead.ToString()).FontSize(13f).TextColor(Nocturne.Accent));
        actions.AddChild(push);

        actions.AddChild(Ui.IconButton(Icons.ArchiveTray, DoStash, "Stash"));
        bar.AddChild(actions);

        return bar;
    }

    private Element ViewTab(JunctionView view, string label, string glyph, string num, int badge)
    {
        var active = View == view;
        var fg = active ? Nocturne.Accent : Nocturne.On(0.60);

        var tab = Flex.Row(gap: 6)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(5, 10, 5, 10))
            .CornerRadius(Nocturne.RadiusRow)
            .Cursor(CursorType.Pointer)
            .Tooltip($"{label} ({num})");
        if (active)
            tab.Background(Nocturne.Bg).BorderColor(Nocturne.Accent).BorderWidth(1f);

        tab.AddChild(Ui.Icon(glyph, 13f, fg));
        tab.AddChild(Text.New(label).FontSize(12.5f).FontWeight(FontWeight.Medium).TextColor(fg));
        tab.AddChild(
            Box.New().Padding(new Thickness(1, 4, 1, 4)).CornerRadius(3f)
                .BorderColor(Nocturne.Divider).BorderWidth(1f)
                .Children(Ui.Mono(num, 9f, Nocturne.On(active ? 0.8 : 0.5))));

        if (badge > 0)
            tab.AddChild(
                Box.New().MinWidth(15).Height(15).Padding(new Thickness(0, 4, 0, 4)).CornerRadius(8f)
                    .Background(Nocturne.Accent700)
                    .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                    .Children(Text.New(badge.ToString()).FontSize(9f).TextColor(Nocturne.Accent100)));

        tab.OnClick(_ => SetView(view));
        if (!active)
            tab.OnPointerEnter(_ => tab.BackgroundColor = Nocturne.Alpha(Nocturne.Text, 0.05));
        tab.OnPointerLeave(_ => { if (View != view) tab.BackgroundColor = Color.Transparent; });
        return tab;
    }

    // ── Git actions ──────────────────────────────────────────────────────

    private static Task<string?> NoAskPass(string _) => Task.FromResult<string?>(null);

    private async void DoFetch()
    {
        if (ActiveRepo is not { } repo || _remoteOpRunning) return;
        _remoteOpRunning = true;
        Flash("Fetching…");
        var result = await repo.FetchAsync(NoAskPass, _settings.Git.PruneOnFetch);
        _remoteOpRunning = false;
        Flash(result.Success ? "Fetched all remotes" : FirstLine(result.Stderr, "Fetch failed"));
        RefreshAll();
    }

    private async void DoPull()
    {
        if (ActiveRepo is not { } repo || _remoteOpRunning) return;
        _remoteOpRunning = true;
        Flash("Pulling…");
        var result = await repo.PullAsync(NoAskPass, new PullOptions
        {
            Strategy = _settings.Git.PullStrategy,
            AutoStash = _settings.Git.PullAutoStash,
            Prune = _settings.Git.PruneOnFetch,
        });
        _remoteOpRunning = false;
        Flash(result.Success ? "Pulled latest changes" : FirstLine(result.Stderr, "Pull failed"));
        RefreshAll();
    }

    private async void DoPush()
    {
        if (ActiveRepo is not { } repo || _remoteOpRunning) return;
        _remoteOpRunning = true;
        Flash("Pushing…");
        var result = await repo.PushAsync(NoAskPass);
        _remoteOpRunning = false;
        Flash(result.Success ? "Pushed successfully" : FirstLine(result.Stderr, "Push failed"));
        RefreshAll();
    }

    private void DoStash()
    {
        if (ActiveRepo is not { } repo) return;
        var result = repo.StashCreate(null);
        Flash(result.Success ? "Changes stashed" : FirstLine(result.Stderr, "Nothing to stash"));
        RefreshAll();
    }

    private static string FirstLine(string s, string fallback)
    {
        var line = s.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        return string.IsNullOrEmpty(line) ? fallback : line;
    }
}

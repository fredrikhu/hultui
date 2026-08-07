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

        // Breadcrumb pinned to the left with a fixed inset (draggable — no handlers
        // on these children). A grow-based spacer here would size from the leftover
        // space, which rounds differently as the active repo's name/branch width
        // changes and jitters "Junction" by ~1px; a fixed left inset keeps its left
        // edge constant. The right group's own grow filler pushes the controls flush
        // right.
        var crumb = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 0, 0, 14));
        crumb.AddChild(Text.New(Brand).FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Nocturne.On(0.70)));
        crumb.AddChild(Sep());
        crumb.AddChild(Ui.Icon(Icons.GitRepository, 13f, Nocturne.Accent300));
        crumb.AddChild(Text.New(repoName).FontSize(13f).TextColor(Nocturne.On(0.70)));
        // Narrow windows drop the branch + workspace segments so the breadcrumb
        // doesn't run into the search box; they're still visible in the sidebar.
        if (!_compactChrome)
        {
            crumb.AddChild(Sep());
            crumb.AddChild(Ui.Icon(Icons.GitBranch, 12f, Nocturne.Accent300));
            crumb.AddChild(Ui.Mono(pb.Title, 12f, Nocturne.On(0.70)));
            if (wsName.Length > 0)
            {
                crumb.AddChild(Sep());
                crumb.AddChild(Text.New(wsName).FontSize(13f).TextColor(Nocturne.On(0.75)));
            }
        }
        crumb.WindowDrag();
        bar.AddChild(crumb);

        // Right group: search button + Windows-style window controls (flush right).
        var right = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Center);

        // Leading draggable filler so the gap between the breadcrumb and the search
        // box drags the window too (a plain FlexEnd spacer here has no WindowDrag
        // ancestor, so hits on it fall through as non-draggable).
        right.AddChild(Flex.Row(gap: 0).FlexGrow(1).AlignSelf(AlignSelf.Stretch).WindowDrag());

        var search = Flex.Row(gap: 8)
            .Height(26).AlignItems(AlignItems.Center)
            .Padding(_compactChrome ? new Thickness(0, 7, 0, 7) : new Thickness(0, 8, 0, 10))
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer)
            .Tooltip("Search & commands (Ctrl K)");
        search.AddChild(Ui.Icon(Icons.MagnifyingGlass, 13f, Nocturne.On(0.55)));
        // Narrow windows collapse the search box to just its icon.
        if (!_compactChrome)
        {
            search.AddChild(Text.New("Search & commands").FontSize(12f).TextColor(Nocturne.On(0.55)));
            search.AddChild(Kbd("Ctrl K"));
        }
        search.OnClick(_ => TogglePalette());
        search.HoverBg(Nocturne.Alpha(Nocturne.Accent, 0.08), Nocturne.Bg);
        right.AddChild(search);

        right.AddChild(Box.New().Width(10));

        var controls = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch);
        controls.AddChild(WinButton(Icons.WinMinimize, "Minimize", () => _window.Minimize(), Nocturne.Neutral800));
        // Flag the maximize button so the Windows custom frame reports HTMAXBUTTON
        // there and Windows 11 shows its Snap Layouts flyout on hover.
        controls.AddChild(WinButton(Icons.WinMaximize, "Maximize", () => _window.ToggleMaximize(), Nocturne.Neutral800)
            .WindowMaximizeButton());
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

        bool compact = _compactChrome;
        var bar = Flex.Row(gap: compact ? 8 : 14)
            .Height(52).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, compact ? 10 : 16, 0, compact ? 10 : 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        // Segmented view tabs. FlexShrink(0) so the row never compresses the tabs
        // (which would let each tab's shortcut-number badge creep into its label);
        // the compact breakpoint switches them to icon-only before they'd overflow.
        var seg = Flex.Row(gap: 2).FlexShrink(0)
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
        seg.AddChild(ViewTab(JunctionView.Files, "Files", Icons.Folders, "6", 0));
        bar.AddChild(seg);

        bar.AddChild(Box.New().FlexGrow(1));

        // Git actions. Narrow windows drop the button labels (keeping the
        // ahead/behind counts) so Fetch/Pull/Push don't crowd the view tabs.
        var actions = Flex.Row(gap: 6).FlexShrink(0).AlignItems(AlignItems.Center);
        actions.AddChild(ActionButton("Fetch", Icons.ArrowsClockwise, DoFetch, primary: false, compact));

        var pull = ActionButton("Pull", Icons.ArrowDown, DoPull, primary: false, compact);
        if (sum.Behind > 0)
            pull.AddChild(Text.New(sum.Behind.ToString()).FontSize(13f).TextColor(Nocturne.Accent300));
        actions.AddChild(pull);

        var push = ActionButton("Push", Icons.ArrowUp, DoPush, primary: true, compact);
        if (sum.Ahead > 0)
            push.AddChild(Text.New(sum.Ahead.ToString()).FontSize(13f).TextColor(Nocturne.Accent));
        actions.AddChild(push);

        actions.AddChild(StashButton());
        bar.AddChild(actions);

        return bar;
    }

    // A toolbar git action. Full width shows an icon + label; compact drops the
    // label to an icon-only button (with the label as its tooltip) but keeps room
    // for a trailing ahead/behind count the caller may append.
    private static BoxElement ActionButton(string label, string glyph, Action onClick, bool primary, bool compact)
    {
        if (!compact)
            return primary
                ? Ui.PrimaryButton(label, glyph, onClick)
                : Ui.SecondaryButton(label, glyph, onClick);

        var fg = primary ? Nocturne.Accent : Nocturne.Text;
        var b = Flex.Row(gap: 5).Height(34).MinWidth(34)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(0, 9, 0, 9))
            .CornerRadius(Nocturne.RadiusMd)
            .BorderColor(primary ? Nocturne.Accent : Nocturne.Divider).BorderWidth(1f)
            .Cursor(CursorType.Pointer).Tooltip(label);
        b.AddChild(Ui.Icon(glyph, 15f, fg));
        b.OnClick(_ => onClick());
        return b.HoverBg(Nocturne.Alpha(fg, primary ? 0.12 : 0.07), Color.Transparent);
    }

    private Element ViewTab(JunctionView view, string label, string glyph, string num, int badge)
    {
        var active = View == view;
        var fg = active ? Nocturne.Accent : Nocturne.On(0.60);
        bool compact = _compactChrome;

        // Every tab reserves a 1px border always; only the active one colors it.
        // Layout sizes the border box from BorderWidth regardless of color, so a
        // transparent border on inactive tabs keeps all five fixed in place —
        // otherwise selecting a tab would grow its box 2px and nudge its siblings.
        var tab = Flex.Row(gap: 6).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(compact ? new Thickness(5, 8, 5, 8) : new Thickness(5, 10, 5, 10))
            .CornerRadius(Nocturne.RadiusRow)
            .BorderColor(active ? Nocturne.Accent : Color.Transparent).BorderWidth(1f)
            .Cursor(CursorType.Pointer)
            .Tooltip($"{label} ({num})");
        if (active)
            tab.Background(Nocturne.Bg);

        tab.AddChild(Ui.Icon(glyph, 13f, fg));
        // Narrow windows show icon-only tabs (the shortcut number and label move to
        // the tooltip) so all five fit without overlapping the git actions.
        if (!compact)
        {
            tab.AddChild(Text.New(label).FontSize(12.5f).FontWeight(FontWeight.Medium).TextColor(fg));
            tab.AddChild(
                Box.New().Padding(new Thickness(1, 4, 1, 4)).CornerRadius(3f)
                    .BorderColor(Nocturne.Divider).BorderWidth(1f)
                    .Children(Ui.Mono(num, 9f, Nocturne.On(active ? 0.8 : 0.5))));
        }

        if (badge > 0)
            tab.AddChild(
                Box.New().MinWidth(15).Height(15).Padding(new Thickness(0, 4, 0, 4)).CornerRadius(8f)
                    .Background(Nocturne.Accent700)
                    .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                    .Children(Text.New(badge.ToString()).FontSize(9f).TextColor(Nocturne.Accent100)));

        tab.OnClick(_ => SetView(view));
        // Declarative hover: the interaction config follows the latest build, so the
        // tint attaches only while inactive and disappears when the tab becomes active
        // (un-hover then restores the active build's Bg base, no guard needed).
        if (!active)
            tab.HoverBg(Nocturne.Alpha(Nocturne.Text, 0.05), Color.Transparent);
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

    // Stash affordance: a toolbar button that opens a small popover offering
    // "stash" and "stash including untracked". Anchors the popover under itself,
    // right-aligned so the 230px menu doesn't run off the window edge.
    private Element StashButton()
    {
        var b = Flex.Row(gap: 0).Width(34).Height(34)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .CornerRadius(Nocturne.RadiusMd)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .Cursor(CursorType.Pointer).Tooltip("Stash…");
        b.AddChild(Ui.Icon(Icons.ArchiveTray, 15f, Nocturne.Text));
        b.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            var box = e.CurrentTarget.Layout.AbsoluteBorderBox;   // live element's box, not the captured build's
            _popoverX = Math.Max(8f, box.X + box.Width - 230f);
            _popoverY = box.Y + box.Height + 4f;
            _popover = StashMenuItems();
            MarkDirty();
        });
        return b.HoverBg(Nocturne.Alpha(Nocturne.Text, 0.07), Color.Transparent);
    }

    private IReadOnlyList<ContextMenuItem> StashMenuItems() =>
    [
        Item(Icons.ArchiveTray, "Stash changes", () => DoStash(includeUntracked: false)),
        Item(Icons.Stack, "Stash including untracked", () => DoStash(includeUntracked: true)),
    ];

    private void DoStash(bool includeUntracked)
    {
        if (ActiveRepo is not { } repo) return;
        var result = repo.StashCreate(null, includeUntracked);
        Flash(result.Success
            ? (includeUntracked ? "Stashed changes and untracked files" : "Stashed changes")
            : FirstLine(result.Stderr, "Nothing to stash"));
        RefreshAll();
    }

    private static string FirstLine(string s, string fallback)
    {
        var line = s.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        return string.IsNullOrEmpty(line) ? fallback : line;
    }
}

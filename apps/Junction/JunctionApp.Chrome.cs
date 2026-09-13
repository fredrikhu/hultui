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
        if (HasActiveRepo && GitLabMarkFor(_activeRepoPath) is { } gitlabMark)
            crumb.AddChild(gitlabMark);
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

        // GitLab's todos, when there are any: a bell with the count, left of the search.
        if (TodosBell() is { } bell)
        {
            right.AddChild(bell);
            right.AddChild(Box.New().Width(8));
        }

        var search = Flex.Row(gap: 8)
            .Height(26).AlignItems(AlignItems.Center)
            .Padding(_compactChrome ? new Thickness(0, 7, 0, 7) : new Thickness(0, 8, 0, 10))
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer)
            .Tooltip("Search & commands (Ctrl P)");
        search.AddChild(Ui.Icon(Icons.MagnifyingGlass, 13f, Nocturne.On(0.55)));
        // Narrow windows collapse the search box to just its icon.
        if (!_compactChrome)
        {
            search.AddChild(Text.New("Search & commands").FontSize(12f).TextColor(Nocturne.On(0.55)));
            search.AddChild(Kbd("Ctrl P"));
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
        // No pointer cursor: Windows caption buttons keep the arrow, and the maximize
        // button here has to anyway — it is non-client so Snap Layouts works, which puts
        // the cursor under the OS. Setting it on the other two only made the trio
        // inconsistent with each other and with every other window on the desktop.
        var b = Flex.Row(gap: 0).Width(46).Height(40)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Tooltip(tooltip);
        // 13 rather than 11: the caption marks are hairlines, and below this the bar
        // in the minimize glyph lands under one device pixel and washes out.
        b.AddChild(Ui.Icon(glyph, 13f, Nocturne.On(0.75)));
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
        seg.AddChild(ViewTab(JunctionView.Tags, "Tags", Icons.Tag, "6", 0));
        seg.AddChild(ViewTab(JunctionView.Files, "Files", Icons.Folders, "7", 0));
        bar.AddChild(seg);

        bar.AddChild(Box.New().FlexGrow(1));

        // The current branch's head on GitLab: its latest pipeline and the merge request it
        // is the source of. Absent — not greyed — when the repository is not GitLab-backed,
        // when there is no account, and when the host knows nothing about the head.
        if (HeadPipelineButton(compact) is { } head)
        {
            head.Key = "head-pipeline";
            bar.AddChild(head);
        }

        // Git actions. Narrow windows drop the button labels (keeping the
        // ahead/behind counts) so Fetch/Pull/Push don't crowd the view tabs.
        // Keyed: the compact breakpoint swaps each button between its labelled and
        // its icon-only build at the same position, which is exactly the case that
        // must reconcile by identity rather than by index.
        var actions = Flex.Row(gap: 6).FlexShrink(0).AlignItems(AlignItems.Center);
        var fetch = ActionButton("Fetch", Icons.ArrowsClockwise, DoFetch, primary: false, compact, RemoteOp.Fetch);
        // Keyed on whether it is the one running: the busy build swaps the label and
        // hands in an animated icon, so it has to reconcile as a different button.
        fetch.Key = "fetch" + (_remoteOp == RemoteOp.Fetch ? "#busy" : "");
        actions.AddChild(fetch);

        var pull = ActionButton("Pull", Icons.ArrowDown, DoPull, primary: false, compact, RemoteOp.Pull);
        pull.Key = "pull" + (_remoteOp == RemoteOp.Pull ? "#busy" : "");
        if (sum.Behind > 0 && _remoteOp != RemoteOp.Pull)
            pull.AddChild(Text.New(sum.Behind.ToString()).FontSize(13f).TextColor(Nocturne.Accent300));
        actions.AddChild(pull);

        var push = ActionButton("Push", Icons.ArrowUp, DoPush, primary: true, compact, RemoteOp.Push);
        push.Key = "push" + (_remoteOp == RemoteOp.Push ? "#busy" : "");
        if (sum.Ahead > 0 && _remoteOp != RemoteOp.Push)
            push.AddChild(Text.New(sum.Ahead.ToString()).FontSize(13f).TextColor(Nocturne.Accent));
        actions.AddChild(push);

        var stash = StashButton();
        stash.Key = "stash";
        actions.AddChild(stash);

        var tools = RepositoryToolsButton();
        tools.Key = "repo-tools";
        actions.AddChild(tools);
        bar.AddChild(actions);

        return bar;
    }

    // A toolbar git action. Full width shows an icon + label; compact drops the
    // label to an icon-only button (with the label as its tooltip) but keeps room
    // for a trailing ahead/behind count the caller may append.
    //
    // While its own command is running the icon is handed in already in motion (see
    // RemoteOpIcon) and the label goes present-tense, so the button itself reports the
    // work rather than leaving it to a toast that has already faded.
    private BoxElement ActionButton(string label, string glyph, Action onClick, bool primary, bool compact,
        RemoteOp op = RemoteOp.None)
    {
        bool busy = op != RemoteOp.None && _remoteOp == op;
        var fg = primary ? Nocturne.Accent : Nocturne.Text;

        if (!compact)
        {
            // Built without its glyph, then given the icon as a child: Ui.Icon's artwork
            // is an ordinary element, so a transform on it moves the mark alone and
            // leaves the label sitting still beside it.
            var full = primary
                ? Ui.PrimaryButton(busy ? RemoteOpLabel(op) : label, null, onClick)
                : Ui.SecondaryButton(busy ? RemoteOpLabel(op) : label, null, onClick);
            full.InsertChild(0, RemoteOpIcon(glyph, 14f, fg, op));
            return full;
        }

        var b = Flex.Row(gap: 5).Height(34).MinWidth(34)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(0, 9, 0, 9))
            .CornerRadius(Nocturne.RadiusMd)
            .BorderColor(primary ? Nocturne.Accent : Nocturne.Divider).BorderWidth(1f)
            .Cursor(CursorType.Pointer).Tooltip(busy ? RemoteOpLabel(op) : label);
        b.AddChild(RemoteOpIcon(glyph, 15f, fg, op));
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

    // Every one of these ends its busy state in a finally. The state is no longer just a
    // re-entrancy flag: it also owns a looping animation, so a git call that throws would
    // otherwise leave the toolbar spinning and every later remote command refused, for the
    // rest of the session.
    private async void DoFetch()
    {
        if (ActiveRepo is not { } repo || RemoteOpRunning) return;
        BeginRemoteOp(RemoteOp.Fetch);
        GitResult result;
        try { result = await repo.FetchAsync(NoAskPass, _settings.Git.PruneOnFetch); }
        finally { EndRemoteOp(); }
        Flash(result.Success ? "Fetched all remotes" : FirstLine(result.Stderr, "Fetch failed"));
        RefreshAll();
        // A fetch is the moment someone expects the hosted side to be current too.
        RefreshHosted(_activeRepoPath);
    }

    private async void DoPull()
    {
        if (ActiveRepo is not { } repo || RemoteOpRunning) return;
        BeginRemoteOp(RemoteOp.Pull);
        GitResult result;
        try
        {
            result = await repo.PullAsync(NoAskPass, new PullOptions
            {
                Strategy = _settings.Git.PullStrategy,
                AutoStash = _settings.Git.PullAutoStash,
                Prune = _settings.Git.PruneOnFetch,
            });
        }
        finally { EndRemoteOp(); }
        if (result.Success)
            Flash("Pulled latest changes");
        else if (!StoppedOnConflict(repo, "Pull"))
            Flash(FirstLine(result.Stderr, "Pull failed"));
        RefreshAll();
    }

    /// <summary>
    /// Pushes the checked-out branch, publishing it when it has nowhere to go yet.
    ///
    /// <para>A branch created locally has no upstream, and a bare <c>git push</c> answers
    /// that with a fatal error and a command to run by hand — which is the one thing a git
    /// client exists to save you from. The plan is worked out first (off the UI thread,
    /// since it reads git config) so the card can say <i>publish</i> rather than
    /// <i>push</i>: setting an upstream is a decision the user should see recorded.</para>
    ///
    /// <para>It asks before it pushes. Not because a push is dangerous — it is the one
    /// remote command that cannot lose anything — but because this is where force-with-lease
    /// belongs: after a rebase or an amend the ordinary push is refused, and the only way
    /// through it used to be a terminal. One card says what is about to leave, and offers
    /// the lease as a deliberate second choice rather than a second button to mis-click.</para>
    /// </summary>
    private async void DoPush()
    {
        if (ActiveRepo is not { } repo || RemoteOpRunning) return;
        var plan = await Task.Run(() => repo.PlanPush());
        if (plan.Problem is { } problem) { Flash(problem); return; }
        // What the host will say, said first: a protected branch's rule sits under the body,
        // the lease is greyed where force is not allowed, and a branch nobody can push to
        // says so and offers to try anyway rather than pretending.
        var guard = PushGuard(plan.Branch);
        OpenDialog(new DialogSpec(
            Title: plan.SetUpstream ? $"Publish {plan.Branch}" : $"Push {plan.Branch}",
            Body: guard.NobodyCanPush
                ? $"{plan.Remote} will reject a direct push to {plan.Branch}. Open a merge request instead."
                : PushDialogBody(plan),
            Placeholder: null,
            ConfirmLabel: guard.NobodyCanPush ? "Try anyway" : plan.SetUpstream ? "Publish" : "Push",
            OnConfirm: (_, force) => RunPush(plan, force),
            CheckboxLabel: ForceWithLeaseLabel,
            CheckboxInitial: false,
            Info: guard.Info,
            CheckboxDisabledReason: guard.ForceDisabledReason));
    }

    /// <summary>What GitLab's protected-branch rule says about pushing <paramref name="branch"/>,
    /// for the confirm card. Everything null/false on a branch it does not protect, or a
    /// repository that is not on GitLab.</summary>
    private (string? Info, string? ForceDisabledReason, bool NobodyCanPush) PushGuard(string branch)
    {
        if (!HasActiveRepo || !GitLabActive(_activeRepoPath) || GitLabRemoteFor(_activeRepoPath) is not { } remote)
            return (null, null, false);
        if (ProtectionFor(_activeRepoPath, branch) is not { } rule) return (null, null, false);
        return (PushRuleSummary(branch, rule, remote.Host),
            rule.AllowForcePush ? null : "— not allowed on this branch",
            rule.PushLevel == 0);
    }

    /// <summary>The toast after a push: on a GitLab-backed repository it offers to create the
    /// merge request the branch does not have yet, or to open the one it has.</summary>
    private void AfterPushToast(string branch, string message)
    {
        if (!HasActiveRepo || !GitLabActive(_activeRepoPath)) { Flash(message); return; }
        var defaultBranch = ProjectFor(_activeRepoPath).Value?.Project.DefaultBranch ?? "main";
        if (branch == defaultBranch) { Flash(message); return; }
        if (MergeRequestForBranch(_activeRepoPath, branch) is { } mr)
            Flash(message, ($"Open !{mr.Iid}", () => OpenMergeRequest(_activeRepoPath, mr.Iid)));
        else
            Flash(message, ("Create merge request", () => OpenCreateMergeRequest(branch)));
    }

    /// <summary>The wording of the force option, shared by every push that offers it. It
    /// names the safety rather than the danger, because the safety is the point: the lease
    /// is what makes an overwrite refuse rather than erase.</summary>
    internal const string ForceWithLeaseLabel =
        "Force push (with lease) — replace the remote branch, unless it has moved since the last fetch";

    /// <summary>What the push card says is about to happen. The ahead/behind counts are the
    /// ones the sidebar is already showing, so the card cannot disagree with the chrome.</summary>
    private string PushDialogBody(PushPlan plan)
    {
        if (plan.SetUpstream)
            return $"{plan.Branch} has no upstream yet. Pushing publishes it to {plan.Remote} and tracks it from now on.";

        var sum = HasActiveRepo ? SummaryFor(_activeRepoPath) : RepoSummary.Pending;
        var counts = sum.Ahead == 0 && sum.Behind == 0
            ? "Nothing to send — the branch and its upstream are level."
            : sum.Behind == 0
                ? $"{Commits(sum.Ahead)} to send."
                : $"{Commits(sum.Ahead)} to send, {sum.Behind} behind the upstream — an ordinary push will be refused until you pull or force with the lease.";
        return $"Pushes {plan.Branch} to its upstream. {counts}";

        static string Commits(int n) => n == 1 ? "1 commit" : $"{n} commits";
    }

    private async void RunPush(PushPlan plan, bool forceWithLease)
    {
        if (ActiveRepo is not { } repo || RemoteOpRunning) return;
        BeginRemoteOp(RemoteOp.Push);
        GitResult result;
        try { result = await repo.RunPushAsync(plan, NoAskPass, forceWithLease); }
        finally { EndRemoteOp(); }
        if (!result.Success)
        {
            Flash(FirstLine(result.Stderr, "Push failed"));
            RefreshAll();
            return;
        }
        var message = plan.SetUpstream ? $"Published {plan.Branch} to {plan.Remote}"
            : forceWithLease ? $"Force-pushed {plan.Branch}" : $"Pushed {plan.Branch} to {plan.Remote}";
        RefreshAll();
        AfterPushToast(plan.Branch, message);
    }

    // Stash affordance: a toolbar button that opens a small popover offering
    // "stash" and "stash including untracked".
    private BoxElement StashButton() =>
        ToolbarMenuButton(Icons.TrayArrowDown, "Stash…", StashMenuItems, accessibleName: "Stash");

    private IReadOnlyList<ContextMenuItem> StashMenuItems() =>
    [
        Item(Icons.TrayArrowDown, "Stash changes", () => DoStash(includeUntracked: false)),
        Item(Icons.Stack, "Stash including untracked", () => DoStash(includeUntracked: true)),
    ];

    // ── Repository tools ─────────────────────────────────────────────────

    // Worktrees, submodules, remotes and the reflog are complete panels that used to
    // be reachable only by opening the palette and typing their name — which is to
    // say, only by someone who already knew they existed (the app's owner asked for
    // worktree support months after it shipped). They are still overlays rather than
    // numbered views: this is occasional upkeep, not a daily tab, and eight tabs would
    // dilute the ones people do use every day. What they were missing was a home in
    // the chrome, so here it is — one menu, beside the other repository-wide actions,
    // that any future panel of the same kind joins.
    //
    // Icon-only at every width, unlike Fetch/Pull/Push: the labelled toolbar already
    // needs all but ~75 dip of the default 1440 window's content width, and a labelled
    // button here costs about 130 — which would drop the *default* window below the
    // compact breakpoint and take every toolbar label with it. The tooltip names the
    // four, so a hover answers "what is in here" without opening it.
    private BoxElement RepositoryToolsButton() =>
        ToolbarMenuButton(Icons.Wrench, "Repository tools — worktrees, submodules, remotes, reflog",
            RepositoryToolMenuItems, caret: true, accessibleName: "Repository tools");

    private IReadOnlyList<ContextMenuItem> RepositoryToolMenuItems()
    {
        // With no repository open the panels have nothing to show. They stay in the
        // menu, greyed: dropping the rows instead would leave a reader wondering
        // whether the app has worktrees at all — the very question this menu exists
        // to answer. (See Unavailable.)
        if (!HasActiveRepo)
            return
            [
                Unavailable(Icons.TreeStructure, "Worktrees…"),
                Unavailable(Icons.Cube, "Submodules…"),
                Unavailable(Icons.Cloud, "Remotes…"),
                ContextMenuItem.Separator(),
                Unavailable(Icons.CounterClockwise, "Reflog (recover work)…"),
            ];

        var items = new List<ContextMenuItem>
        {
            Item(Icons.TreeStructure, "Worktrees…", OpenWorktrees),
            Item(Icons.Cube, "Submodules…", OpenSubmodules),
            Item(Icons.Cloud, "Remotes…", OpenRemotes),
            ContextMenuItem.Separator(),
            // Set apart: the other three manage a repository, this one recovers from
            // an accident with it.
            Item(Icons.CounterClockwise, "Reflog (recover work)…", OpenReflog),
        };
        // The hosted group, only for a repository whose remote reads as GitLab. Signed
        // out, the entry leads to the accounts pane rather than to an empty panel.
        if (GitLabRemoteFor(_activeRepoPath) is { } remote)
        {
            items.Add(ContextMenuItem.Separator());
            items.Add(_gitlab.HasAccount(remote.Host)
                ? Item(Icons.GitPullRequest, "Merge requests…", () => OpenMergeRequests())
                : Item(Icons.GitPullRequest, "Merge requests…", () => OpenOptions(OptionsSection.GitLabAccounts)));
            if (_gitlab.HasAccount(remote.Host))
                items.Add(Item(Icons.GitPullRequest, "Create merge request…", () => OpenCreateMergeRequest()));
        }
        return items;
    }

    /// <summary>A square toolbar button that drops a menu under itself, right-aligned so
    /// the fixed-width menu cannot run off the window edge. The anchor comes from the live
    /// element's box inside the handler — a Ref read during Build() has not been laid out
    /// yet, and the box captured while building is a frame stale.</summary>
    private BoxElement ToolbarMenuButton(string glyph, string tooltip,
        Func<IReadOnlyList<ContextMenuItem>> items, bool caret = false, string? accessibleName = null)
    {
        var b = Flex.Row(gap: 2).Width(caret ? 44 : 34).Height(34)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .CornerRadius(Nocturne.RadiusMd)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .Cursor(CursorType.Pointer).Tooltip(tooltip);
        b.AddChild(Ui.Icon(glyph, 15f, Nocturne.Text));
        if (caret)
            b.AddChild(Ui.Icon(Icons.CaretDown, 9f, Nocturne.On(0.55)));
        // An icon-only button has no text for the accessibility tree to name it from, and
        // a tooltip is not a name — spell it out so screen readers (and tests) find it.
        if (accessibleName != null) b.AccessibleName(accessibleName);
        b.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            var box = e.CurrentTarget.Layout.AbsoluteBorderBox;   // live element's box, not the captured build's
            _popoverX = Math.Max(8f, box.X + box.Width - PopoverWidth);
            _popoverY = box.Y + box.Height + 4f;
            _popover = items();
            MarkDirty();
        });
        return b.HoverBg(Nocturne.Alpha(Nocturne.Text, 0.07), Color.Transparent);
    }

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

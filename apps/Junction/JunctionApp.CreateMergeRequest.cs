using System.Text.RegularExpressions;
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.RichText;
using HultUI.Core.Styling;
using HultUI.RichText.Markdown;
using Junction.Git;
using Junction.GitLab;
using Junction.Theme;

namespace Junction;

/// <summary>
/// The create-merge-request card: a branch, a target, a title and description pre-filled
/// from the commits, reviewers, labels, and the three flags the project defaults. Reached
/// from a Branches row, its menu, the <c>m</c> key, the palette, the tools menu and the
/// push toast. A branch with unpushed commits is pushed first — the primary says so.
/// </summary>
public partial class JunctionApp
{
    private sealed class CreateMrForm
    {
        public required string Path;
        public required string Source;
        public string Target = "";
        public string Title = "";
        /// <summary>The description as the editor holds it; GitLab is sent its Markdown
        /// (<see cref="RichMarkdown.Write"/>).</summary>
        public RichDocument Description = RichDocument.Empty;
        public bool Draft;
        public bool RemoveSource = true;
        public bool Squash;
        public readonly HashSet<long> ReviewerIds = [];
        public readonly HashSet<string> Labels = [];
        public bool Busy;
        public string? Error;
        public string BusyLabel = "";
        /// <summary>Whether the target and flags were taken from the project. False when the
        /// card opened before the project had loaded; the build applies them when it lands.</summary>
        public bool DefaultsApplied;
        /// <summary>Set once the user edits the field, so a late pre-fill (issues or the
        /// project landing after the card opened) never overwrites what they typed.</summary>
        public bool TitleTouched, DescriptionTouched;
        public bool IssuesSeen;
    }

    private CreateMrForm? _createMr;

    // The description's editor and its formatting bar: Markdown's formats only, since GitLab is
    // sent Markdown.
    private readonly Ref<RichTextEditorElement> _createMrDescRef = new();
    private RichTextToolbar? _createMrToolbar;

    private static readonly IReadOnlyList<RichTextCommand?> CreateMrToolbarCommands =
    [
        RichTextCommand.Bold, RichTextCommand.Italic, RichTextCommand.Strike, RichTextCommand.Code,
        RichTextCommand.Link, null,
        RichTextCommand.Heading2, RichTextCommand.BulletList, RichTextCommand.NumberedList,
        RichTextCommand.TaskList, RichTextCommand.Quote, RichTextCommand.CodeBlock,
    ];

    private RichTextToolbar CreateMrToolbar() => _createMrToolbar ??= new RichTextToolbar(_createMrDescRef, MarkDirty)
    {
        Commands = CreateMrToolbarCommands,
        ButtonSize = 26f,
        Icon = command => command switch
        {
            RichTextCommand.Link => Ui.Icon(Icons.LinkSimple, 14f, Nocturne.On(0.8)),
            RichTextCommand.Code => Ui.Icon(Icons.Code, 14f, Nocturne.On(0.8)),
            RichTextCommand.TaskList => Ui.Icon(Icons.ListChecks, 14f, Nocturne.On(0.8)),
            _ => null,
        },
    };

    private readonly KeyedAsyncLoader<string, Hosted<IReadOnlyList<GlMember>>> _memberLoaders =
        new(Hosted<IReadOnlyList<GlMember>>.Empty([]), capacity: 8, comparer: PathComparer);
    private readonly KeyedAsyncLoader<string, Hosted<IReadOnlyList<GlLabel>>> _labelLoaders =
        new(Hosted<IReadOnlyList<GlLabel>>.Empty([]), capacity: 8, comparer: PathComparer);

    internal bool CreateMergeRequestOpen => _createMr != null;

    // ── Open ─────────────────────────────────────────────────────────────────────────

    /// <summary>Opens the card for <paramref name="branch"/> (the current branch when null).
    /// A branch that already has an open merge request opens that instead.</summary>
    internal void OpenCreateMergeRequest(string? branch = null)
    {
        if (!HasActiveRepo) return;
        if (GitLabRemoteFor(_activeRepoPath) is not { } remote) { Flash("This repository is not on GitLab"); return; }
        if (!_gitlab.HasAccount(remote.Host)) { OpenOptions(OptionsSection.GitLabAccounts); return; }
        var path = _activeRepoPath;
        var sum = SummaryFor(path);
        branch ??= sum.Branch;
        if (string.IsNullOrEmpty(branch) || branch == "HEAD") { Flash("Check out a branch first"); return; }

        if (MergeRequestForBranch(path, branch) is { } existing)
        {
            Flash($"!{existing.Iid} already exists for {branch}");
            OpenMergeRequest(path, existing.Iid);
            return;
        }

        var project = ProjectFor(path).Value?.Project;
        var target = project?.DefaultBranch ?? "main";
        if (target == branch) { Flash($"{branch} is the default branch — check out the branch to merge first"); return; }

        var form = new CreateMrForm
        {
            Path = path,
            Source = branch,
            Target = target,
            RemoveSource = project?.RemoveSourceBranchAfterMerge ?? true,
            Squash = project?.SquashByDefault ?? false,
            DefaultsApplied = project != null,
        };
        PrefillCreateMr(form);
        _createMr = form;
        ClosePalette();
        _popover = null;
        _mrPanelOpen = false;
        Rerender();   // activates the member/label loaders
    }

    private void CloseCreateMr()
    {
        _createMr = null;
        _popover = null;
        MarkDirty();
    }

    /// <summary>Title and description from the commits the branch adds over the target: one
    /// commit → its subject; several → a title from the branch leaf and a bullet per subject.
    /// A branch named <c>&lt;iid&gt;-…</c> the GitLab way closes that issue.</summary>
    private void PrefillCreateMr(CreateMrForm form)
    {
        var repo = RepoFor(form.Path);
        var remoteName = HostingRemoteName(form.Path);
        var subjects = repo.GetSubjectsBetween($"{remoteName}/{form.Target}", form.Source);
        if (subjects.Count == 0) subjects = repo.GetSubjectsBetween(form.Target, form.Source);

        var leaf = BranchName.Parse(form.Source).Title;
        form.Title = subjects.Count == 1 ? subjects[0] : TitleFromLeaf(leaf);

        var lines = new List<string>();
        if (subjects.Count > 1)
            foreach (var s in subjects.AsEnumerable().Reverse()) lines.Add("- " + s);
        var issue = IssueFromLeaf(leaf);
        if (issue != null && !IssueLeaf.IsMatch(leaf))
        {
            // A ticket-looking number in the middle of the leaf only counts when it is an
            // open issue; the GitLab <iid>-slug form at the start is taken at its word.
            var open = IssuesFor(form.Path).Value;
            if (open.Count > 0 && !open.Any(i => i.Iid.ToString() == issue)) issue = null;
        }
        if (issue != null)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add($"Closes #{issue}");
        }
        // Written as the Markdown GitLab would be sent, and read into the editor's document: a
        // subject's own markup reads the way GitLab will render it.
        form.Description = RichMarkdown.Parse(string.Join("\n", lines));
    }

    /// <summary><c>PC-1042-refund-splitting</c> → <c>PC-1042: refund splitting</c>;
    /// <c>refund-splitting</c> → <c>refund splitting</c>.</summary>
    internal static string TitleFromLeaf(string leaf)
    {
        var m = TicketLeaf.Match(leaf);
        if (m.Success) return $"{m.Groups[1].Value}: {m.Groups[2].Value.Replace('-', ' ')}";
        return leaf.Replace('-', ' ').Replace('_', ' ');
    }

    /// <summary>The issue a GitLab-style branch name (<c>1042-refund-splitting</c>) points at.</summary>
    internal static string? IssueFromLeaf(string leaf)
    {
        var m = IssueLeaf.Match(leaf);
        if (m.Success) return m.Groups[1].Value;
        var n = LeafNumber.Match(leaf);
        return n.Success ? n.Groups[1].Value : null;
    }

    private static readonly Regex TicketLeaf = new(@"^([A-Za-z][A-Za-z0-9]+-\d+)-(.+)$", RegexOptions.Compiled);
    private static readonly Regex IssueLeaf = new(@"^(\d+)-", RegexOptions.Compiled);
    private static readonly Regex LeafNumber = new(@"(\d{3,})", RegexOptions.Compiled);

    // ── Loaders ──────────────────────────────────────────────────────────────────────

    private Hosted<IReadOnlyList<GlMember>> MembersFor(string path)
    {
        if (GitLabRemoteFor(path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client)
            return Hosted<IReadOnlyList<GlMember>>.Empty([]);
        var loader = _memberLoaders.For(path);
        var prev = loader.Value;
        loader.Load($"{path}#{HostedGen(path)}", () =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return prev;
            var m = Sync(client.ListMembersAsync(id.Value, maxPages: 2));
            return m.Ok
                ? new Hosted<IReadOnlyList<GlMember>>(m.Value.Where(x => x.State is null or "active").ToList(), DateTimeOffset.UtcNow, null, true)
                : Keep(prev, m.Failure!);
        });
        return loader.Value;
    }

    private Hosted<IReadOnlyList<GlLabel>> LabelsFor(string path)
    {
        if (GitLabRemoteFor(path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client)
            return Hosted<IReadOnlyList<GlLabel>>.Empty([]);
        var loader = _labelLoaders.For(path);
        var prev = loader.Value;
        loader.Load($"{path}#{HostedGen(path)}", () =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return prev;
            var l = Sync(client.ListLabelsAsync(id.Value, maxPages: 2));
            return l.Ok ? new Hosted<IReadOnlyList<GlLabel>>(l.Value, DateTimeOffset.UtcNow, null, true) : Keep(prev, l.Failure!);
        });
        return loader.Value;
    }

    // ── The card ─────────────────────────────────────────────────────────────────────

    private Element BuildCreateMrDialog(CreateMrForm f)
    {
        var remote = GitLabRemoteFor(f.Path)!;
        // The project's defaults, applied once, when the card opened ahead of them.
        if (!f.DefaultsApplied && ProjectFor(f.Path).Value?.Project is { } late)
        {
            f.DefaultsApplied = true;
            f.RemoveSource = late.RemoveSourceBranchAfterMerge ?? f.RemoveSource;
            f.Squash = late.SquashByDefault;
            if (late.DefaultBranch is { Length: > 0 } def && def != f.Source && f.Target != def)
                f.Target = def;
            PrefillCreateMr(f);
        }
        // The issues, likewise: the Closes line is decided once they are known.
        if (!f.DescriptionTouched && !f.IssuesSeen && IssuesFor(f.Path).Loaded)
        {
            f.IssuesSeen = true;
            PrefillCreateMr(f);
        }
        var sum = SummaryFor(f.Path);
        var branch = sum.Branches.FirstOrDefault(b => !b.IsRemote && b.Name == f.Source);
        bool unpublished = branch is { TrackingBranch: null or "" };
        int unpushed = branch?.AheadCount ?? 0;
        bool needsPush = branch != null && (unpublished || unpushed > 0);

        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 140))
            .JustifyContent(JustifyContent.FlexStart).AlignItems(AlignItems.Center)
            .Padding(new Thickness(56, 24, 24, 24))
            // Above the merge-request panel and the plain dialog, below the popover the
            // reviewer and label pickers open.
            .ZIndex(58);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; if (!f.Busy) CloseCreateMr(); });

        var card = Flex.Column(gap: 0)
            .Width(560).MaxWidth(Dimension.Percent(94)).MaxHeight(Dimension.Percent(92))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg)
            .Overflow(Overflow.Hidden);
        card.On<ClickEvent>(e => e.StopPropagation = true);
        card.AutomationId = "create-mr";

        // Header.
        var header = Flex.Row(gap: 12).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(14, 18, 12, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Box.New().Width(34).Height(34).FlexShrink(0).CornerRadius(Nocturne.RadiusMd)
            .Background(Nocturne.Accent800).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(Icons.GitPullRequest, 17f, Nocturne.Accent200)));
        var titleCol = Flex.Column(gap: 1).FlexGrow(1).FlexShrink(1);
        titleCol.AddChild(Text.New("Create merge request").FontSize(16f).FontWeight(FontWeight.Medium));
        titleCol.AddChild(Ui.Mono($"{remote.ProjectPath} on {remote.Host}", 11.5f, Nocturne.On(0.52)));
        header.AddChild(titleCol);
        header.AddChild(Ui.IconButton(Icons.X, CloseCreateMr, "Cancel (Esc)", 28f).FlexShrink(0));
        card.AddChild(header);

        // Body.
        var body = ScrollView.New().FlexGrow(1).FlexShrink(1);
        var col = Flex.Column(gap: 14).Padding(new Thickness(14, 18, 14, 18));

        if (needsPush)
        {
            var note = Flex.Row(gap: 8).AlignItems(AlignItems.FlexStart)
                .Padding(new Thickness(8, 10, 8, 10)).CornerRadius(Nocturne.RadiusSm)
                .Background(Nocturne.Alpha(Nocturne.Accent, 0.08)).BorderColor(Nocturne.Accent700).BorderWidth(1f);
            note.AddChild(Ui.Icon(Icons.ArrowUp, 13f, Nocturne.Accent300).FlexShrink(0));
            note.AddChild(Text.New(unpublished
                    ? $"{BranchName.Parse(f.Source).Title} has not been pushed yet — it is published to {HostingRemoteName(f.Path)} first."
                    : $"{BranchName.Parse(f.Source).Title} has {unpushed} unpushed commit{(unpushed == 1 ? "" : "s")} — they are pushed to {HostingRemoteName(f.Path)} first.")
                .FontSize(12f).TextColor(Nocturne.On(0.8)).FlexShrink(1));
            note.AutomationId = "create-mr-push-note";
            col.AddChild(note);
        }

        // Branches: source → target.
        var branches = Flex.Column(gap: 6);
        branches.AddChild(CreateMrLabel("Branches"));
        var pair = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        var pb = BranchName.Parse(f.Source);
        var (chipBg, chipFg) = pb.ChipLabel switch
        {
            "FEAT" => (Nocturne.Accent800, Nocturne.Accent100),
            "WIP" => (Nocturne.Accent2_800, Nocturne.Accent2_100),
            _ => (Nocturne.Neutral800, Nocturne.Neutral100),
        };
        pair.AddChild(Ui.Tag(pb.ChipLabel, chipBg, chipFg, 9f));
        pair.AddChild(Ui.Mono(pb.Title, 12f, Nocturne.Text).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        pair.AddChild(Ui.Icon(Icons.ArrowRight, 12f, Nocturne.On(0.4)).FlexShrink(0));
        var targets = TargetChoices(f, sum);
        int selected = Math.Max(0, targets.IndexOf(f.Target));
        var select = Dropdown.New(targets, selected);
        select.OnSelectionChanged = i => { if (i >= 0 && i < targets.Count) { f.Target = targets[i]; PrefillCreateMr(f); MarkDirty(); } };
        select.AutomationId = "create-mr-target";
        select.FlexShrink(0);
        pair.AddChild(select);
        branches.AddChild(pair);
        if (ProtectionFor(f.Path, f.Target) is { } rule)
        {
            var info = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
            info.AddChild(Ui.Icon(Icons.Lock, 12f, Nocturne.On(0.5)));
            info.AddChild(Text.New(MergeRuleSummary(f.Path, f.Target, rule, remote.Host)).FontSize(11f).TextColor(Nocturne.On(0.55)).FlexShrink(1));
            branches.AddChild(info);
        }
        col.AddChild(branches);

        // Title + description.
        col.AddChild(CreateMrField("Title", CreateMrInput(f.Title, "What this change does", v => { f.Title = v; f.TitleTouched = true; MarkDirty(); }, "create-mr-title")));
        var toolbar = CreateMrToolbar();
        var mdContext = MdContextFor(f.Path);
        var desc = RichTextEditor.New(f.Description)
            .Features(RichMarkdown.Features)
            .PlaceholderText("Pre-filled from the commits. Closes #… closes the issue on merge.")
            .OnDocumentChanged(d => { f.Description = d; f.DescriptionTouched = true; MarkDirty(); })
            .OnSelectionChanged(MarkDirty)
            .OnLinkRequested(toolbar.OpenLinkEditor)
            .OnLinkActivated(OpenUrl)
            // Pictures load as the Markdown view loads them: uploads through the account, the
            // rest plainly, one cache for both. One arriving rebuilds, and the editor asks again.
            .Images(url => MdPictureFor(ResolveMarkdownUrl(url, mdContext.Remote), mdContext).Source)
            .AutoHeight(96, 320)
            .Style(Ui.Field(verticalPadding: 8f))
            .TextColor(Nocturne.Text);
        desc.AutomationId = "create-mr-description";
        // The bar reads the editor on screen through the ref: built before the ref moves to
        // this freshly built one.
        var bar = toolbar.Build();
        desc.WithRef(_createMrDescRef);
        col.AddChild(CreateMrField("Description", Flex.Column(gap: 4).Children(bar, desc)));

        // Reviewers and labels: pills, each picker a popover.
        col.AddChild(CreateMrField("Reviewers", BuildReviewerPills(f)));
        col.AddChild(CreateMrField("Labels", BuildLabelPills(f)));

        // Flags.
        var flags = Flex.Column(gap: 6);
        flags.AddChild(CreateMrCheck("Draft — not ready to merge yet", f.Draft, v => { f.Draft = v; MarkDirty(); }, "create-mr-draft"));
        flags.AddChild(CreateMrCheck("Delete source branch when merged", f.RemoveSource, v => { f.RemoveSource = v; MarkDirty(); }, "create-mr-remove-source"));
        flags.AddChild(CreateMrCheck("Squash commits when merging", f.Squash, v => { f.Squash = v; MarkDirty(); }, "create-mr-squash"));
        col.AddChild(flags);

        if (f.Error is { } error)
        {
            var bad = Flex.Row(gap: 8).AlignItems(AlignItems.FlexStart)
                .Padding(new Thickness(8, 10, 8, 10)).CornerRadius(Nocturne.RadiusSm)
                .Background(Nocturne.DiffDelBg).BorderColor(Nocturne.Alpha(Nocturne.DiffDelSign, 0.35)).BorderWidth(1f);
            bad.AddChild(Ui.Icon(Icons.XCircle, 15f, Nocturne.DiffDelSign).FlexShrink(0));
            bad.AddChild(Text.New(error).FontSize(12f).FlexShrink(1));
            bad.AutomationId = "create-mr-error";
            col.AddChild(bad);
        }

        body.AddChild(col);
        card.AddChild(body);

        // Footer: summary · Cancel · primary.
        var footer = Flex.Row(gap: 8).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(12, 18, 12, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        footer.AddChild(Text.New(CreateMrSummary(f)).FontSize(11f).TextColor(Nocturne.On(0.45)).FlexGrow(1).FlexShrink(1)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        footer.AddChild(Ui.SecondaryButton("Cancel", null, CloseCreateMr, 12.5f));
        bool valid = f.Title.Trim().Length > 0 && !f.Busy && f.Source != f.Target;
        string primaryLabel = f.Busy ? f.BusyLabel : needsPush ? "Push and create merge request" : "Create merge request";
        var primary = valid
            ? Ui.PrimaryButton(primaryLabel, Icons.GitPullRequest, SubmitCreateMr, 12.5f)
            : f.Busy ? BusyButton(primaryLabel) : DisabledButton(primaryLabel, Icons.GitPullRequest);
        primary.Key = "create-mr-primary-" + (valid ? "on" : "off");
        primary.AccessibleName = "Create merge request";
        footer.AddChild(primary);
        card.AddChild(footer);

        backdrop.AddChild(card);
        return backdrop;
    }

    private List<string> TargetChoices(CreateMrForm f, RepoSummary sum)
    {
        var remoteName = HostingRemoteName(f.Path);
        var names = new List<string>();
        foreach (var b in sum.Branches)
        {
            if (!b.IsRemote) continue;
            var (r, leaf) = SplitRemote(b.Name);
            if (r != remoteName || leaf == f.Source || leaf == "HEAD") continue;
            names.Add(leaf);
        }
        if (names.Count == 0)
            names.AddRange(sum.Branches.Where(b => !b.IsRemote && b.Name != f.Source).Select(b => b.Name));
        if (!names.Contains(f.Target)) names.Insert(0, f.Target);
        // The default branch first, then the rest alphabetically.
        var def = ProjectFor(f.Path).Value?.Project.DefaultBranch;
        return names.Distinct().OrderBy(n => n == def ? 0 : 1).ThenBy(n => n, StringComparer.Ordinal).ToList();
    }

    private static string CreateMrSummary(CreateMrForm f)
    {
        var parts = new List<string>();
        if (f.Draft) parts.Add("draft");
        if (f.ReviewerIds.Count > 0) parts.Add(f.ReviewerIds.Count == 1 ? "1 reviewer" : $"{f.ReviewerIds.Count} reviewers");
        if (f.Labels.Count > 0) parts.Add(f.Labels.Count == 1 ? "1 label" : $"{f.Labels.Count} labels");
        if (f.Squash) parts.Add("squash");
        if (f.RemoveSource) parts.Add("delete source");
        return string.Join(" · ", parts);
    }

    private static Element CreateMrLabel(string label) =>
        Text.New(label).FontSize(12f).TextColor(Nocturne.On(0.7));

    private static Element CreateMrField(string label, Element control)
    {
        var col = Flex.Column(gap: 5);
        col.AddChild(CreateMrLabel(label));
        col.AddChild(control);
        return col;
    }

    private static Element CreateMrInput(string value, string placeholder, Action<string> onChange, string automationId)
    {
        var input = TextInput.New(value).Style(Ui.Field())
            .PlaceholderText(placeholder)
            .OnTextChanged(onChange)
            .Height(Dimension.Px(36)).FlexShrink(0)
            .FontSize(13f).TextColor(Nocturne.Text);
        input.AutomationId = automationId;
        return input;
    }

    private static Element CreateMrCheck(string label, bool on, Action<bool> onChange, string automationId)
    {
        var check = Checkbox.New(on, label).OnCheckedChanged(onChange);
        check.LabelGap = 8f;
        check.TextColor(Nocturne.On(0.8)).FontSize(12.5f);
        check.AutomationId = automationId;
        check.Key = automationId + (on ? "-on" : "-off");
        return check;
    }

    private Element BuildReviewerPills(CreateMrForm f)
    {
        var members = MembersFor(f.Path).Value;
        var row = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        foreach (var id in f.ReviewerIds)
        {
            var m = members.FirstOrDefault(x => x.Id == id);
            var name = m?.Name ?? $"#{id}";
            var pill = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
                .Padding(new Thickness(2, 8, 2, 4)).CornerRadius(999f)
                .Background(Nocturne.Accent900).Outline(1f, Nocturne.Accent700).Cursor(CursorType.Pointer)
                .Tooltip("Remove reviewer");
            pill.AddChild(Box.New().Width(16).Height(16).CornerRadius(8f).Background(Nocturne.Accent800)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New(Initials(name)).FontSize(8f).TextColor(Nocturne.Accent100)));
            pill.AddChild(Text.New(name).FontSize(11.5f).TextColor(Nocturne.Accent100));
            pill.AddChild(Ui.Icon(Icons.X, 10f, Nocturne.Accent200));
            pill.On<ClickEvent>(e => { e.StopPropagation = true; f.ReviewerIds.Remove(id); MarkDirty(); });
            row.AddChild(pill);
        }
        var add = Ui.GhostButton(f.ReviewerIds.Count == 0 ? "Add reviewer" : "Add", Icons.Plus, () => { }, 11.5f);
        add.AccessibleName = "Add reviewer";
        add.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            if (members.Count == 0) { Flash(MembersFor(f.Path).Loaded ? "No other members on this project" : "Loading members…"); return; }
            var b = e.CurrentTarget.Layout.AbsoluteBorderBox;
            _popoverX = b.X;
            _popoverY = b.Y + b.Height + 4;
            var me = MyIdOn(f.Path);
            _popover = members.Where(m => m.Id != me).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Take(30)
                .Select(m => Item(f.ReviewerIds.Contains(m.Id) ? Icons.Check : Icons.UserCircle, $"{m.Name}  @{m.Username}",
                    () => { if (!f.ReviewerIds.Remove(m.Id)) f.ReviewerIds.Add(m.Id); _popover = null; MarkDirty(); }))
                .ToList();
            MarkDirty();
        });
        row.AddChild(add);
        return row;
    }

    private Element BuildLabelPills(CreateMrForm f)
    {
        var labels = LabelsFor(f.Path).Value;
        var row = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        foreach (var name in f.Labels)
        {
            var pill = Flex.Row(gap: 5).AlignItems(AlignItems.Center)
                .Padding(new Thickness(2, 8, 2, 8)).CornerRadius(999f)
                .Background(Nocturne.Neutral800).Cursor(CursorType.Pointer).Tooltip("Remove label");
            pill.AddChild(Text.New(name).FontSize(11.5f).TextColor(Nocturne.Neutral100));
            pill.AddChild(Ui.Icon(Icons.X, 10f, Nocturne.Neutral300));
            var captured = name;
            pill.On<ClickEvent>(e => { e.StopPropagation = true; f.Labels.Remove(captured); MarkDirty(); });
            row.AddChild(pill);
        }
        var add = Ui.GhostButton(f.Labels.Count == 0 ? "Add label" : "Add", Icons.Tag, () => { }, 11.5f);
        add.AccessibleName = "Add label";
        add.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            if (labels.Count == 0) { Flash(LabelsFor(f.Path).Loaded ? "This project has no labels" : "Loading labels…"); return; }
            var b = e.CurrentTarget.Layout.AbsoluteBorderBox;
            _popoverX = b.X;
            _popoverY = b.Y + b.Height + 4;
            _popover = labels.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).Take(40)
                .Select(l => Item(f.Labels.Contains(l.Name) ? Icons.Check : Icons.Tag, l.Name,
                    () => { if (!f.Labels.Remove(l.Name)) f.Labels.Add(l.Name); _popover = null; MarkDirty(); }))
                .ToList();
            MarkDirty();
        });
        row.AddChild(add);
        return row;
    }

    // ── Submit ───────────────────────────────────────────────────────────────────────

    private async void SubmitCreateMr()
    {
        if (_createMr is not { } f || f.Busy || f.Title.Trim().Length == 0 || RemoteOpRunning) return;
        if (GitLabRemoteFor(f.Path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client) return;
        var repo = RepoFor(f.Path);
        var remoteName = HostingRemoteName(f.Path);
        f.Error = null;

        // Push first when the branch is behind the remote or not there at all.
        var sum = SummaryFor(f.Path);
        var branch = sum.Branches.FirstOrDefault(b => !b.IsRemote && b.Name == f.Source);
        if (branch != null && (string.IsNullOrEmpty(branch.TrackingBranch) || branch.AheadCount > 0))
        {
            f.Busy = true;
            f.BusyLabel = "Pushing…";
            MarkDirty();
            GitResult push;
            BeginRemoteOp(RemoteOp.Push);
            try
            {
                push = string.IsNullOrEmpty(branch.TrackingBranch)
                    ? await repo.RunPushAsync(new PushPlan(remoteName, f.Source, SetUpstream: true, Problem: null), NoAskPass, false)
                    : await repo.PushToAsync(remoteName, f.Source, NoAskPass, false);
            }
            finally { EndRemoteOp(); }
            if (_createMr != f) return;
            if (!push.Success)
            {
                f.Busy = false;
                f.Error = FirstLine(push.Stderr, "Push failed");
                MarkDirty();
                return;
            }
            RefreshAll();
        }

        f.Busy = true;
        f.BusyLabel = "Creating…";
        MarkDirty();
        var request = new NewMergeRequest(
            f.Source, f.Target, f.Title.Trim(),
            Description: RichMarkdown.Write(f.Description).Trim() is { Length: > 0 } description ? description : null,
            Draft: f.Draft,
            ReviewerIds: f.ReviewerIds.Count > 0 ? [.. f.ReviewerIds] : null,
            Labels: f.Labels.Count > 0 ? [.. f.Labels] : null,
            RemoveSourceBranch: f.RemoveSource,
            Squash: f.Squash);
        var created = await Task.Run(async () =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return GitLabResult<GlMergeRequest>.Fail(GitLabFailureKind.NotFound, "Project not found");
            return await client.CreateMergeRequestAsync(id.Value, request);
        });
        if (_createMr != f) return;

        if (!created.Ok)
        {
            f.Busy = false;
            f.Error = created.Failure!.Kind switch
            {
                GitLabFailureKind.Conflict => $"A merge request for {BranchName.Parse(f.Source).Title} already exists. {created.Failure.Message}",
                GitLabFailureKind.Forbidden => created.Failure.UserMessage(remote.Host),
                _ => created.Failure.Message,
            };
            MarkDirty();
            return;
        }

        var mr = created.Value;
        var path = f.Path;
        _createMr = null;
        RefreshHosted(path);
        Flash($"Created !{mr.Iid}", ("Open", () => OpenMergeRequest(path, mr.Iid)));
        MarkDirty();
    }

    // ── Guards shared with push and delete ───────────────────────────────────────────

    private static string RoleName(int level) => level switch
    {
        0 => "no one",
        30 => "developers and maintainers",
        40 => "maintainers",
        60 => "admins",
        var n => $"access level {n}",
    };

    /// <summary>The design's info line: <i>main is protected on gitlab.com — maintainers can
    /// push, force push is not allowed.</i></summary>
    private static string PushRuleSummary(string branch, GlProtectedBranch rule, string host) =>
        $"{branch} is protected on {host} — {RoleName(rule.PushLevel)} can push, force push is {(rule.AllowForcePush ? "allowed" : "not allowed")}.";

    private static string MergeRuleSummary(string path, string target, GlProtectedBranch rule, string host)
    {
        int mergeLevel = rule.MergeAccessLevels.Count == 0 ? 0 : rule.MergeAccessLevels.Max(l => l.AccessLevel);
        return $"{target} is protected — {RoleName(mergeLevel)} can merge; the request waits for one of them.";
    }
}

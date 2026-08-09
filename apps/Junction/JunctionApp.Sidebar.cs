using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    private Element BuildSidebar() => _sidebarCollapsed ? BuildSidebarRail() : BuildSidebarExpanded();

    // ── Expanded (266px) ─────────────────────────────────────────────────

    private Element BuildSidebarExpanded()
    {
        var aside = Flex.Column(gap: 0)
            .Width(266).FlexShrink(0)
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));

        // Header: app mark + brand + collapse.
        var header = Flex.Row(gap: 9).AlignItems(AlignItems.Center)
            .Padding(new Thickness(12, 14, 10, 14)).FlexShrink(0);
        header.AddChild(Box.New().Width(22).Height(22).CornerRadius(Nocturne.RadiusRow)
            .Background(Nocturne.Accent800).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(Icons.GitFork, 14f, Nocturne.Accent200)));
        header.AddChild(Text.New(Brand).FontSize(15f).FontWeight(FontWeight.Medium).FlexGrow(1));
        header.AddChild(Ui.IconButton(Icons.Sidebar, ToggleSidebar, "Collapse", 28f));
        aside.AddChild(header);

        var scroll = ScrollView.New().FlexGrow(1).Padding(new Thickness(6, 10, 16, 10));

        // Workspaces.
        scroll.AddChild(SectionLabel(Icons.Stack, "Workspaces", null,
            SectionPlus(Icons.Plus, "New workspace", OpenNewWorkspace)));
        foreach (var ws in _settings.Workspaces)
            scroll.AddChild(WorkspaceRow(ws));

        // Repositories.
        var repos = ActiveWorkspaceRepos;
        scroll.AddChild(SectionLabel(Icons.Folders, "Repositories", repos.Count.ToString(),
            SectionPlus(Icons.Plus, "Add or clone repository", OpenRepoAddPopover)));
        foreach (var path in repos)
            scroll.AddChild(RepoRow(path));

        // Co-usage suggestions.
        var suggestions = CoUsageSuggestions();
        if (suggestions.Count > 0)
            scroll.AddChild(CoUsageCard(suggestions));

        aside.AddChild(scroll);
        return aside;
    }

    private Element SectionLabel(string glyph, string text, string? count, Element? action = null)
    {
        var row = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Padding(new Thickness(12, 6, 6, 6));
        row.AddChild(Ui.Icon(glyph, 12f, Nocturne.On(0.42)));
        row.AddChild(Text.New(text.ToUpperInvariant()).FontSize(10f).TextColor(Nocturne.On(0.42)).FlexGrow(1));
        if (count != null)
            row.AddChild(Text.New(count).FontSize(11f).TextColor(Nocturne.On(0.45)));
        if (action != null)
            row.AddChild(action);
        return row;
    }

    /// <summary>A small borderless "+" affordance for a section header. The click
    /// handler receives the button's on-screen box so popovers can anchor to it.</summary>
    private Element SectionPlus(string glyph, string tooltip, Action onClick)
    {
        var b = Box.New()
            .Width(22).Height(22).FlexShrink(0).CornerRadius(Nocturne.RadiusSm)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer).Tooltip(tooltip)
            .Children(Ui.Icon(glyph, 14f, Nocturne.On(0.6)));
        b.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            // Read the live button's laid-out box so popovers anchor under it.
            var box = e.CurrentTarget.Layout.AbsoluteBorderBox;
            _popoverX = box.X;
            _popoverY = box.Y + box.Height + 4;
            onClick();
        });
        b.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return b;
    }

    private void OpenRepoAddPopover()
    {
        _popover = RepoAddMenuItems();
        MarkDirty();
    }

    private Element WorkspaceRow(WorkspaceData ws)
    {
        var active = ws.Id == _activeWorkspaceId;
        var row = Flex.Row(gap: 9).AlignItems(AlignItems.Center)
            .Padding(new Thickness(7, 8, 7, 8)).CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer);
        if (active)
            row.Background(Nocturne.Accent900).BorderColor(Nocturne.Accent).BorderWidth(new Thickness(0, 0, 0, 2));

        row.AddChild(Ui.Icon(WorkspaceGlyph(ws.Icon), 15f, Nocturne.Accent300));
        row.AddChild(Text.New(ws.Name).FontSize(13f).FlexGrow(1)
            .OverflowMode(HultUI.Core.Layout.TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(Text.New(ws.RepoPaths.Count.ToString()).FontSize(11f).TextColor(Nocturne.On(0.45)));

        row.OnClick(_ => SelectWorkspace(ws.Id));
        row.On<PointerDownEvent>(e => { if (e.Button == 2) SelectWorkspace(ws.Id); });
        row.ContextMenu(() => WorkspaceMenuItems(ws));
        if (!active) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    private Element RepoRow(string path)
    {
        var active = PathEquals(path, _activeRepoPath);
        var name = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
        var sum = SummaryFor(path);
        var (tileBg, tileFg) = RepoTile(path);

        var row = Flex.Row(gap: 9).AlignItems(AlignItems.Center)
            .Padding(new Thickness(7, 8, 7, 8)).CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer).Tooltip(path);
        if (active)
            row.Background(Nocturne.Accent900).BorderColor(Nocturne.Accent).BorderWidth(new Thickness(0, 0, 0, 2));

        row.AddChild(Ui.InitialTile(name, tileBg, tileFg, 24f, 11f));

        var text = Flex.Column(gap: 0).FlexGrow(1);
        text.AddChild(Text.New(name).FontSize(13f).FontWeight(FontWeight.Medium)
            .OverflowMode(HultUI.Core.Layout.TextOverflow.Ellipsis).MaxLines(1));
        text.AddChild(Text.New(RepoSubtitle(path)).FontSize(10.5f).TextColor(Nocturne.On(0.45))
            .OverflowMode(HultUI.Core.Layout.TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(text);

        var status = Flex.Row(gap: 6).AlignItems(AlignItems.Center).FlexShrink(0);
        if (sum.Dirty > 0)
            status.AddChild(Box.New().Width(7).Height(7).CornerRadius(3.5f).Background(Nocturne.Accent)
                .Tooltip("Uncommitted changes"));
        status.AddChild(AheadBehind(sum.Ahead, sum.Behind));
        row.AddChild(status);

        row.OnClick(_ => SelectRepo(path));
        row.On<PointerDownEvent>(e => { if (e.Button == 2) SelectRepo(path); });
        row.ContextMenu(() => RepoMenuItems(path));
        if (!active) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    private Element AheadBehind(int ahead, int behind)
    {
        var row = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        row.AddChild(Flex.Row(gap: 1).AlignItems(AlignItems.Center).Children(
            Ui.Icon(Icons.ArrowUp, 9f, Nocturne.On(0.55)),
            Text.New(ahead.ToString()).FontSize(10f).TextColor(Nocturne.On(0.55))));
        row.AddChild(Flex.Row(gap: 1).AlignItems(AlignItems.Center).Children(
            Ui.Icon(Icons.ArrowDown, 9f, Nocturne.On(0.55)),
            Text.New(behind.ToString()).FontSize(10f).TextColor(Nocturne.On(0.55))));
        return row;
    }

    private Element CoUsageCard(List<string> suggestions)
    {
        var activeName = HasActiveRepo
            ? System.IO.Path.GetFileName(_activeRepoPath.TrimEnd('/', '\\')) : "";

        var card = Flex.Column(gap: 0)
            .Padding(new Thickness(11))
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd)
            .Background(Nocturne.Alpha(Nocturne.Accent, 0.05));

        var head = Flex.Row(gap: 6).AlignItems(AlignItems.Center).Padding(new Thickness(0, 0, 8, 0));
        head.AddChild(Ui.Icon(Icons.Sparkle, 12f, Nocturne.Accent300));
        head.AddChild(Text.New($"Often opened with {activeName}").FontSize(10.5f).TextColor(Nocturne.Accent300));
        card.AddChild(head);

        foreach (var path in suggestions)
        {
            var name = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
            var (bg, fg) = RepoTile(path);
            var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
                .Padding(new Thickness(5, 6, 5, 6)).CornerRadius(Nocturne.RadiusRow)
                .Cursor(CursorType.Pointer).Tooltip($"Add {name} to this workspace");
            row.AddChild(Ui.InitialTile(name, bg, fg, 18f, 9f));
            row.AddChild(Text.New(name).FontSize(12f).FlexGrow(1));
            row.AddChild(Ui.Icon(Icons.Plus, 11f, Nocturne.On(0.5)));
            row.OnClick(_ => AddRepoToActiveWorkspace(path));
            row.HoverBg(Nocturne.Neutral900, Color.Transparent);
            card.AddChild(row);
        }

        // Small top margin via a spacer row.
        return Flex.Column(gap: 0).Padding(new Thickness(18, 0, 0, 0)).Children(card);
    }

    // ── Collapsed rail (58px) ────────────────────────────────────────────

    private Element BuildSidebarRail()
    {
        var rail = Flex.Column(gap: 6)
            .Width(58).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(12, 0, 12, 0))
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));

        rail.AddChild(Ui.IconButton(Icons.Sidebar, ToggleSidebar, "Expand", 32f));
        rail.AddChild(Box.New().Width(24).Height(1).Background(Nocturne.Divider));

        foreach (var path in ActiveWorkspaceRepos)
        {
            var active = PathEquals(path, _activeRepoPath);
            var name = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
            var (bg, fg) = RepoTile(path);
            var tile = Box.New()
                .Width(34).Height(34).FlexShrink(0).CornerRadius(Nocturne.RadiusMd)
                .Background(active ? Nocturne.Accent800 : Nocturne.Neutral900)
                .BorderColor(active ? Nocturne.Accent : Nocturne.Divider).BorderWidth(1f)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Cursor(CursorType.Pointer).Tooltip(name)
                .Children(Text.New(string.IsNullOrEmpty(name) ? "?" : char.ToUpperInvariant(name[0]).ToString())
                    .FontSize(12f).FontWeight(FontWeight.SemiBold).TextColor(fg));
            tile.OnClick(_ => SelectRepo(path));
            rail.AddChild(tile);
        }

        return rail;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private void AddRepoToActiveWorkspace(string path)
    {
        var ws = ActiveWorkspace;
        if (ws == null) return;
        if (!ws.RepoPaths.Any(p => PathEquals(p, path)))
            ws.RepoPaths.Add(path);
        Persist();
        SelectRepo(path);
        var name = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
        Flash($"Added {name} to {ws.Name}");
    }

    private static string RepoSubtitle(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path.TrimEnd('/', '\\'));
        return string.IsNullOrEmpty(dir) ? path : dir;
    }

    internal static string WorkspaceGlyph(string icon) => icon switch
    {
        "browsers" => Icons.Browsers,
        "shapes" => Icons.Shapes,
        "rocket-launch" => Icons.RocketLaunch,
        "package" => Icons.Package,
        "cube" => Icons.Cube,
        "database" => Icons.Database,
        "cloud" => Icons.Cloud,
        "flask" => Icons.Flask,
        "tree-structure" => Icons.TreeStructure,
        "git-fork" => Icons.GitFork,
        "lightning" => Icons.Lightning,
        "palette" => Icons.Palette,
        "wrench" => Icons.Wrench,
        "bug" => Icons.Bug,
        "squares-four" => Icons.SquaresFour,
        "stack" => Icons.Stack,
        _ => Icons.Stack,
    };
}

using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Components.Shared;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Components.Layout;

public sealed class TabbedApp : Component, IDisposable
{
    private readonly Window _window;
    private readonly List<RepositoryTab> _tabs = [];
    private int _activeIndex;
    private OpenRepositoryDialogState? _pendingOpenRepository;

    public TabbedApp(Window window, IEnumerable<string> repositoryPaths, int activeIndex)
    {
        _window = window;

        foreach (var path in repositoryPaths)
            AddTab(path);

        if (_tabs.Count == 0)
            throw new InvalidOperationException("At least one repository path is required.");

        _activeIndex = Math.Clamp(activeIndex, 0, _tabs.Count - 1);
        RefreshRepositoryChrome();
        PersistTabs();
        _window.Closed += Dispose;

        Mount().On<KeyDownEvent>(HandleRootKeyDown);
    }

    public override Element Build()
    {
        var root = Flex.Column(gap: 0)
            .TextColor(GitClientColors.TextPrimary)
            .FontSize(13f)
            .Background(GitClientColors.Background)
            .Children(
                Active.App.Mount().FlexGrow(1)
            );

        if (_pendingOpenRepository != null)
            root.AddChild(OpenRepositoryDialog.Build(_pendingOpenRepository, DismissOpenRepositoryDialog));

        return root;
    }

    protected override void OnAfterBuild()
    {
        if (_pendingOpenRepository is { PendingFocus: true } dialog)
        {
            dialog.PendingFocus = false;
            dialog.InputRef.Current?.Focus();
        }
    }

    private RepositoryTab Active => _tabs[_activeIndex];

    private Element BuildTabStrip()
    {
        var row = Flex.Row(gap: 0)
            .Height(Dimension.Px(32))
            .AlignItems(AlignItems.Stretch)
            .Background(GitClientColors.StatusBar)
            .FlexShrink(0);

        for (var i = 0; i < _tabs.Count; i++)
            row.AddChild(BuildTab(_tabs[i], i));

        row.AddChild(BuildOpenButton());
        row.AddChild(Box.New().FlexGrow(1).WindowDrag());
        return row;
    }

    private Element BuildTab(RepositoryTab tab, int index)
    {
        var isActive = index == _activeIndex;
        var bg = isActive ? GitClientColors.Background : GitClientColors.StatusBar;
        var hoverBg = isActive ? GitClientColors.Background : GitClientColors.SurfaceHover;

        var tabBox = Flex.Row(gap: 6)
            .MinWidth(Dimension.Px(130))
            .MaxWidth(Dimension.Px(240))
            .Padding(new Thickness(0, 10, 0, 10))
            .AlignItems(AlignItems.Center)
            .Background(bg)
            .BorderColor(isActive ? GitClientColors.SelectionBorder : Color.Transparent)
            .BorderWidth(new Thickness(0, 0, isActive ? 2 : 0, 0))
            .Cursor(CursorType.Pointer)
            .Tooltip(tab.Path);

        tabBox.AddChild(Text.New(tab.Name).FontSize(12f)
            .FontWeight(isActive ? FontWeight.SemiBold : FontWeight.Regular)
            .TextColor(isActive ? GitClientColors.TextPrimary : GitClientColors.TextSecondary)
            .FlexGrow(1)
            .FlexShrink(1)
            .OverflowMode(TextOverflow.Ellipsis)
            .MaxLines(1));

        var close = Box.New()
            .Width(18).Height(18)
            .CornerRadius(3f)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .Tooltip("Close repository")
            .Children(Text.New("x").FontSize(11f).TextColor(GitClientColors.TextMuted));
        close.On<PointerEnterEvent>(_ => close.BackgroundColor = GitClientColors.SurfaceHover);
        close.On<PointerLeaveEvent>(_ => close.BackgroundColor = Color.Transparent);
        close.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            CloseTab(index);
        });
        tabBox.AddChild(close);

        tabBox.On<PointerEnterEvent>(_ => tabBox.BackgroundColor = hoverBg);
        tabBox.On<PointerLeaveEvent>(_ => tabBox.BackgroundColor = bg);
        tabBox.On<ClickEvent>(_ => SwitchTo(index));
        return tabBox;
    }

    private Element BuildOpenButton()
    {
        var btn = Box.New()
            .Width(34)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .Tooltip("Open repository")
            .Children(Text.New("+").FontSize(16f).TextColor(GitClientColors.TextSecondary));

        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = GitClientColors.SurfaceHover);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = Color.Transparent);
        btn.On<ClickEvent>(_ => ShowOpenRepositoryDialog());
        return btn;
    }

    private void HandleRootKeyDown(KeyDownEvent e)
    {
        if (e.Key == KeyCode.O && e.Modifiers.HasFlag(KeyModifiers.Ctrl))
        {
            ShowOpenRepositoryDialog();
            e.StopPropagation = true;
        }
        else if (e.Key == KeyCode.Escape && _pendingOpenRepository != null)
        {
            DismissOpenRepositoryDialog();
            e.StopPropagation = true;
        }

        if (!e.StopPropagation && _pendingOpenRepository == null)
            Active.App.HandleGlobalKeyDown(e);
    }

    private void SwitchTo(int index)
    {
        if (index < 0 || index >= _tabs.Count || index == _activeIndex) return;
        _activeIndex = index;
        PersistTabs();
        RefreshRepositoryChrome();
        MarkDirty();
    }

    private void CloseTab(int index)
    {
        if (_tabs.Count == 1) return;
        if (index < 0 || index >= _tabs.Count) return;

        var closing = _tabs[index];
        _tabs.RemoveAt(index);
        closing.Dispose();

        if (_activeIndex >= _tabs.Count)
            _activeIndex = _tabs.Count - 1;
        else if (index < _activeIndex)
            _activeIndex--;

        PersistTabs();
        RefreshRepositoryChrome();
        MarkDirty();
    }

    private void ShowOpenRepositoryDialog()
    {
        _pendingOpenRepository = new OpenRepositoryDialogState
        {
            Path = "",
            OnConfirm = ConfirmOpenRepository,
        };
        MarkDirty();
    }

    private void ConfirmOpenRepository(string path)
    {
        if (_pendingOpenRepository == null) return;

        var gitRoot = RepositoryPathResolver.FindGitRoot(path.Trim());
        if (gitRoot == null)
        {
            _pendingOpenRepository.Error = "Enter a path inside a git repository.";
            MarkDirty();
            return;
        }

        var existingIndex = _tabs.FindIndex(t => SamePath(t.Path, gitRoot));
        if (existingIndex >= 0)
        {
            _activeIndex = existingIndex;
        }
        else
        {
            AddTab(gitRoot);
            _activeIndex = _tabs.Count - 1;
        }

        _pendingOpenRepository = null;
        PersistTabs();
        RefreshRepositoryChrome();
        MarkDirty();
    }

    private void DismissOpenRepositoryDialog()
    {
        _pendingOpenRepository = null;
        MarkDirty();
    }

    private void AddTab(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (_tabs.Any(t => SamePath(t.Path, fullPath))) return;

        var app = new App(_window, new GitRepository(fullPath))
        {
            BuildBelowTitleBar = BuildTabStrip,
        };
        _tabs.Add(new RepositoryTab(fullPath, app));
    }

    private void RefreshRepositoryChrome()
    {
        foreach (var tab in _tabs)
            tab.App.RefreshChrome();
    }

    private void PersistTabs()
    {
        GitClientSettings.Save(new GitClientSettingsData
        {
            RepositoryPaths = _tabs.Select(t => t.Path).ToList(),
            ActiveRepositoryIndex = _activeIndex,
        });
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public void Dispose()
    {
        _window.Closed -= Dispose;
        foreach (var tab in _tabs)
            tab.Dispose();
        _tabs.Clear();
    }

    private sealed class RepositoryTab(string path, App app) : IDisposable
    {
        public string Path { get; } = path;
        public string Name { get; } = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        public App App { get; } = app;

        public void Dispose() => App.Dispose();
    }
}

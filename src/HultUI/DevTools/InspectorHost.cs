using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Styling;

namespace HultUI.DevTools;

/// <summary>
/// Owns the DevTools inspector window and keeps it in step with the window being
/// inspected. One host per application: pointing the inspector at a different window
/// re-targets the existing panel instead of opening a second one.
/// </summary>
internal sealed class InspectorHost
{
    // The inspected tree is re-read on a timer rather than on every frame: an app
    // mid-animation renders 60 times a second, and rebuilding the panel that often
    // would make the inspector the most expensive thing on screen. Selection changes
    // bypass the timer, so anything the user does still feels immediate.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

    private readonly Application _application;
    private readonly ElementInspector _inspector = new();

    private Window? _window;
    private Window? _target;
    private InspectorView? _view;
    private long _lastRefreshTimestamp;
    private bool _refreshPending;

    // The platform clock, not Environment.TickCount64: headless sessions drive a
    // manual clock, and a wall-clock throttle would make the panel's refresh depend
    // on how fast the test machine happened to be.
    private TimeProvider Clock => _application.Platform.TimeProvider;

    public InspectorHost(Application application)
    {
        _application = application;
        _inspector.Changed += OnInspectorChanged;
    }

    public bool IsOpen => _window is { IsOpen: true };

    /// <summary>The window currently being inspected, or null when the panel is closed.</summary>
    public Window? Target => _target;

    /// <summary>
    /// Opens the panel against <paramref name="target"/>, or re-targets and raises it
    /// if it is already open. Inspecting the inspector is refused — it would show a
    /// tree that changes as a result of being looked at.
    /// </summary>
    public void Open(Window target, bool startPicking)
    {
        if (target.IsDevToolsWindow) return;

        if (_window is { IsOpen: true })
        {
            if (!ReferenceEquals(_target, target)) Retarget(target);
            _window.Raise();
        }
        else
        {
            CreateWindow(target);
        }

        _inspector.SetPicking(startPicking);
        Sync();
    }

    public void Close()
    {
        if (_window is { IsOpen: true }) _window.Close();
        Detach();
    }

    /// <summary>Toggles the panel for <paramref name="target"/>.</summary>
    public void Toggle(Window target, bool startPicking)
    {
        if (IsOpen && ReferenceEquals(_target, target) && !startPicking) Close();
        else Open(target, startPicking);
    }

    private void CreateWindow(Window target)
    {
        _target = target;
        _view = new InspectorView(_inspector, () => _application.Theme, CopySelectionToClipboard);

        _window = _application.CreateDevToolsWindow("HultUI Inspector", 900, 620);
        _window.SetMinimumSize(560, 320);
        _window.SetRoot(_view.Mount());
        _window.Closed += Detach;

        target.DevTools.Inspector = _inspector;
        target.Closed += OnTargetClosed;
        target.FrameRendered += OnTargetFrameRendered;
    }

    private void Retarget(Window target)
    {
        DetachTarget();
        _target = target;
        target.DevTools.Inspector = _inspector;
        target.Closed += OnTargetClosed;
        target.FrameRendered += OnTargetFrameRendered;
        _inspector.Clear();
    }

    private void OnTargetClosed()
    {
        // The tree the panel describes is gone; showing its corpse is worse than
        // closing, and a lingering DevTools window would keep the app "running".
        Close();
    }

    private void OnTargetFrameRendered()
    {
        if (Clock.GetElapsedTime(_lastRefreshTimestamp) < RefreshInterval)
        {
            _refreshPending = true;
            return;
        }
        Sync();
    }

    private void OnInspectorChanged()
    {
        // Model changes (selection, pick preview, expansion) refresh both windows at
        // once: the panel because its content changed, the app because the highlight
        // moved.
        _view?.Refresh();
        _target?.Root?.MarkPaintDirty();
    }

    /// <summary>
    /// Re-reads the inspectable roots and rebuilds the panel. The overlay roots are
    /// included because context menus, tooltips, and dialogs are separate trees —
    /// without them, the one element you opened the inspector to look at is often
    /// exactly the one you cannot reach.
    /// </summary>
    private void Sync()
    {
        if (_window is not { IsOpen: true } || _target is not { IsOpen: true }) return;

        _lastRefreshTimestamp = Clock.GetTimestamp();
        _refreshPending = false;

        var roots = new List<Element>();
        if (_target.Root is { } root) roots.Add(root);
        roots.AddRange(_target.OverlayContents);
        _inspector.SetRoots(roots);

        // Always rebuild, not just when SetRoots reported a change: the usual case is
        // the same root with a mutated subtree, which SetRoots cannot see but the
        // panel still has to reflect.
        _view?.Refresh();
    }

    /// <summary>Runs any refresh that the interval throttle deferred. Called once per application frame.</summary>
    public void Tick()
    {
        if (!_refreshPending) return;
        if (Clock.GetElapsedTime(_lastRefreshTimestamp) < RefreshInterval) return;
        Sync();
    }

    // ──── Keyboard (inside the inspector window) ────
    //
    // Picking itself is driven from the inspected window's input pipeline, which has
    // the hit-tester; it reaches the model through Window.DevTools.Inspector rather
    // than through this host.

    /// <summary>
    /// Handles the panel's own navigation keys. Returns true when the key was
    /// consumed; anything else falls through so the panel's text filter keeps working.
    /// </summary>
    public bool HandleKey(KeyCode key, KeyModifiers modifiers)
    {
        // Let the filter box have plain typing and caret movement; navigation only
        // applies when the tree is what the user is driving.
        bool typing = _window?.FocusedElement is TextInputElement;

        switch (key)
        {
            case KeyCode.Escape:
                if (_inspector.IsPicking) _inspector.SetPicking(false);
                else _inspector.Clear();
                return true;

            case KeyCode.C when modifiers.HasFlag(KeyModifiers.Ctrl) && !typing:
                CopySelectionToClipboard();
                return true;

            case KeyCode.Up when !typing:
                _inspector.MoveSelection(-1);
                return true;

            case KeyCode.Down when !typing:
                _inspector.MoveSelection(1);
                return true;

            case KeyCode.Left when !typing:
                _inspector.CollapseOrExit();
                return true;

            case KeyCode.Right when !typing:
                _inspector.ExpandOrEnter();
                return true;

            case KeyCode.PageUp when !typing:
                _inspector.MoveSelection(-10);
                return true;

            case KeyCode.PageDown when !typing:
                _inspector.MoveSelection(10);
                return true;

            default:
                return false;
        }
    }

    private void CopySelectionToClipboard()
    {
        if (_inspector.Selected is not { } selected) return;
        _window?.Clipboard.SetText(ElementProperties.ToText(selected));
    }

    // ──── Teardown ────

    private void Detach()
    {
        DetachTarget();
        _window = null;
        _view = null;
        _inspector.Clear();
    }

    private void DetachTarget()
    {
        if (_target is null) return;
        _target.DevTools.Inspector = null;
        _target.Closed -= OnTargetClosed;
        _target.FrameRendered -= OnTargetFrameRendered;
        // Drop the highlight the panel was drawing over the app.
        _target.Root?.MarkPaintDirty();
        _target = null;
    }
}
